using System.Runtime.InteropServices;
using System.Text;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Input;

/// <summary>The native handles this presence owns, for a test that wants to ask the OS about the
/// same window through its own declarations rather than through these.</summary>
public readonly record struct InputNativeHandles(nint Window);

/// <summary>
/// The Windows input presence: one activatable, topmost, ordinary top-level window that TAKES the
/// keyboard, receives keystrokes through the host's message loop, and reports what the operating
/// system actually did.
///
/// <para><b>The whole design in one paragraph.</b> Putting a window on screen is easy and proves
/// nothing; SP-099 established that for the overlay and the same is true here with the polarity
/// reversed. What is hard — and what this class does — is (1) getting the foreground from an
/// operating system that is entitled to refuse it, and (2) never claiming to have got it when it
/// did not. Both halves are measured rather than assumed: the escalation ladder below exists
/// because a plain <c>SetForegroundWindow</c> from a non-foreground process was measured returning
/// FALSE with every injected keystroke going to the other application, and the confirmation exists
/// because that state is indistinguishable, from inside the process, from a card the user is typing
/// into (SP-110 <c>plan.md</c> §0).</para>
///
/// <para><b>Nothing here synthesises input.</b> The suite does, to prove delivery; the product never
/// does. A capability that pressed keys to check it could receive keys would be putting characters
/// into whatever window it had just failed to take the foreground from.</para>
/// </summary>
public sealed class Win32InputPresence : IInputPresence
{
    /// <summary>
    /// Hard ceiling on the ask-the-OS-for-the-foreground ladder. The foreground band is CONTESTED —
    /// the shipping WPF product re-grabs it from its own <c>Deactivated</c> handler on every loss
    /// (<c>Windows/LockCardWindow.xaml.cs:160-176</c>) — so a bounded retry absorbs a lost race
    /// without a wall-clock wait, which this port forbids outright. Same constant and same reason as
    /// <c>Win32OverlayPresence.MaxRaiseAttempts</c>.
    /// </summary>
    public const int MaxForegroundAttempts = 32;

    /// <summary>How many pixels of the question band are sampled for ink. A grid, not every pixel:
    /// the fact is "the OS holds ink for this window", and a sparse grid answers it for the cost of
    /// a few hundred <c>GetPixel</c> calls rather than a few hundred thousand.</summary>
    public const int InkSampleStride = 3;

    /// <summary>WPF's own lock-card palette, as <c>COLORREF</c> (0x00BBGGRR).
    /// Background <c>#1A1A2E</c> (<c>CCP.Core/Models/AppSettings.cs:3386</c>).</summary>
    public const uint BackgroundColour = 0x002E1A1A;

    /// <summary>WPF's <c>#FF69B4</c> card text (<c>AppSettings.cs:3393</c>).</summary>
    public const uint QuestionColour = 0x00B469FF;

    /// <summary>WPF's <c>#FFFFFF</c> input text (<c>AppSettings.cs:3407</c>).</summary>
    public const uint AnswerColour = 0x00FFFFFF;

    private readonly string _windowClassName = "CcpClientInputPrompt." + Guid.NewGuid().ToString("N");
    private readonly Win32InputInterop.WndProc _windowProc;
    private readonly object _gate = new();

    private nint _window;
    private nint _moduleHandle;
    private ushort _classAtom;
    private bool _prompting;
    private bool _disposed;
    private InputBounds _bounds;
    private InputPromptContent _content = new(string.Empty, string.Empty, string.Empty, string.Empty);
    private Action<InputKeystroke>? _onKeystroke;
    private int _keystrokesSeen;
    private int _callbackFaults;

    public Win32InputPresence()
    {
        // Held in a field for the life of the presence: a delegate handed to the OS as a function
        // pointer must outlive every message the OS will send through it.
        _windowProc = WindowProc;
    }

    /// <inheritdoc/>
    public bool IsPrompting => _prompting;

    /// <inheritdoc/>
    public CapabilityState? LastPrompt { get; private set; }

    /// <inheritdoc/>
    public InputCaptureObservation LastObservation { get; private set; } = InputCaptureObservation.NotAsked;

    /// <summary>The window this presence owns, so a fact can ask the OS about it independently.</summary>
    public InputNativeHandles NativeHandles => new(_window);

    /// <summary>How many foreground assertions the last <see cref="Prompt"/> needed. Diagnostic
    /// only; it is never an input to any claim.</summary>
    public int LastForegroundAttempts { get; private set; }

    /// <summary>How many times a caller's keystroke callback threw and was contained. A callback
    /// that throws on a native callback frame would kill the process, so it is caught — and COUNTED,
    /// because a silently swallowed exception is the other way to lose a defect.</summary>
    public int CallbackFaults => Volatile.Read(ref _callbackFaults);

    /// <summary>Anything this presence could not clean up. Null when teardown was complete.</summary>
    public string? TeardownDiagnostic { get; private set; }

    /// <inheritdoc/>
    public bool HoldsTheInput
    {
        get
        {
            var window = _window;
            if (!OperatingSystem.IsWindows() || window == 0 || _disposed)
            {
                return false;
            }

            return Win32InputInterop.IsWindow(window)
                && Win32InputInterop.IsWindowVisible(window)
                && Win32InputInterop.GetForegroundWindow() == window
                && SystemKeyboardFocus() == window;
        }
    }

    /// <inheritdoc/>
    public bool CanReachAUser => ObserveStation().Confirmed;

    /// <inheritdoc/>
    public InputStationObservation ObserveStation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return InputStationObservation.NotAsked;
        }

        var station = Win32InputInterop.GetProcessWindowStation();
        var visible = station != 0
            && Win32InputInterop.GetUserObjectInformationW(
                station, Win32InputInterop.UoiFlags, out var flags,
                Marshal.SizeOf<Win32InputInterop.UserObjectFlags>(), out _)
            && (flags.dwFlags & Win32InputInterop.WsfVisible) != 0;

        var desktop = Win32InputInterop.GetThreadDesktop(Win32InputInterop.GetCurrentThreadId());
        return new InputStationObservation(
            Asked: true,
            WindowStationVisible: visible,
            DisplayCount: Win32InputInterop.GetSystemMetrics(Win32InputInterop.SmCmonitors),
            DesktopReachable: desktop != 0);
    }

    /// <inheritdoc/>
    public CapabilityState Prompt(InputPromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_disposed)
        {
            return Unavailable(InputReasonCodes.InputPresenceDisposed,
                "this input presence was disposed; its window is gone and it will never ask anything again");
        }

        // Backend SELECTION by platform is permitted; capability AVAILABILITY by platform is not
        // (runtime-capability-contract §2 rule 2). This branch only refuses.
        if (!OperatingSystem.IsWindows())
        {
            return LastPrompt = Unavailable(InputReasonCodes.InputMechanismAbsent,
                "Win32InputPresence takes the foreground and the keyboard focus through USER32, which is a "
                + $"Windows mechanism; this process is on {RuntimeInformation.OSDescription} and nothing was "
                + "attempted");
        }

        // The necessary condition, asked of the OS BEFORE a window is created: a process with no
        // interactive window station cannot put a question in front of anybody, and creating a
        // window there would produce an hwnd that satisfies every other check and reaches nobody.
        var station = ObserveStation();
        if (!station.Confirmed)
        {
            return LastPrompt = Unavailable(InputReasonCodes.InputNoInteractiveStation,
                $"the OS reports window-station-visible={station.WindowStationVisible}, displays={station.DisplayCount}, "
                + $"desktop-reachable={station.DesktopReachable}; this process cannot put a window in front of a "
                + "human, so nothing was shown and nothing is claimed");
        }

        var window = EnsureWindow(request.Bounds, out var creationFailure);
        if (window == 0)
        {
            return LastPrompt = Unavailable(InputReasonCodes.InputWindowCreationFailed, creationFailure);
        }

        lock (_gate)
        {
            _bounds = request.Bounds;
            _content = request.Content;
            _onKeystroke = request.OnKeystroke;
        }

        var bounds = request.Bounds;
        if (!Win32InputInterop.SetWindowPos(
                window, Win32InputInterop.HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                Win32InputInterop.SwpShowwindow))
        {
            var error = Marshal.GetLastWin32Error();
            return LastPrompt = Unavailable(InputReasonCodes.InputPromptNotOnScreen,
                $"SetWindowPos(HWND_TOPMOST, {bounds}, SWP_SHOWWINDOW) returned FALSE for window 0x{window:X} "
                + $"(last-error {error}); nothing is on screen and nothing was asked");
        }

        // Painted BEFORE the foreground is taken, so the card is never on screen blank while it is
        // the thing the user is looking at. WPF configures a card fully before showing it for the
        // neighbouring reason (Windows/LockCardWindow.xaml.cs:1565-1567: Configure, then Show, then
        // OnShown).
        PaintNow();

        LastForegroundAttempts = TakeForeground(window);

        var observation = ReadAndRemember();
        _prompting = observation.Confirmed;

        if (!observation.WindowExists || !observation.WindowVisible)
        {
            return RefuseAndWithdraw(InputReasonCodes.InputPromptNotOnScreen,
                $"after placement the OS reports window 0x{window:X} exists={observation.WindowExists} "
                + $"visible={observation.WindowVisible}; the card is not on screen");
        }

        if (observation.HeldBounds != bounds)
        {
            return RefuseAndWithdraw(InputReasonCodes.InputPromptNotOnScreen,
                $"the OS holds {observation.HeldBounds} for window 0x{window:X} but {bounds} was asked for; the "
                + "card is not where the caller put it");
        }

        if (!observation.AboveEveryOrdinaryWindow)
        {
            return RefuseAndWithdraw(InputReasonCodes.InputPromptNotOnScreen,
                $"the OS's own top-level z-order puts window 0x{window:X} at or below the first ordinary "
                + "(non-topmost) window; a card the user cannot see is a question nobody is being asked");
        }

        if (!observation.IsForegroundWindow)
        {
            return RefuseAndWithdraw(InputReasonCodes.InputNotCaptured,
                $"after {LastForegroundAttempts} attempt(s) the OS still reports "
                + $"{Describe(Win32InputInterop.GetForegroundWindow())} as the foreground window, not the card "
                + $"0x{window:X}. The foreground is LENT by the operating system and it refused: the card was on "
                + "screen and every keystroke was going somewhere else");
        }

        if (!observation.SystemKeyboardFocusIsThisWindow)
        {
            return RefuseAndWithdraw(InputReasonCodes.InputNotCaptured,
                $"the OS reports the card 0x{window:X} as the foreground window but the FOREGROUND THREAD's "
                + $"keyboard focus as {Describe(SystemKeyboardFocus())}; keystrokes are routed elsewhere. This is "
                + "the read that must be GetGUIThreadInfo(0) — the thread-local read answers 'ours' for a window "
                + "nobody can type into (SP-110 plan.md §0)");
        }

        if (!observation.HitTestRoutesHere)
        {
            return RefuseAndWithdraw(InputReasonCodes.InputNotCaptured,
                $"the window manager routes the card's centre {bounds.Centre} to "
                + $"{Describe(observation.HitTestWinner)} instead of the card 0x{window:X}; something is over it "
                + "and its input behaviour cannot be distinguished from being buried");
        }

        if (observation.InkedPixels <= 0)
        {
            return LastPrompt = new CapabilityState.Degraded(
                "the operating system has given this card the foreground and the keyboard focus, and keystrokes "
                + "will reach it",
                new CapabilityReason(InputReasonCodes.InputPromptNotInked,
                    $"the OS holds no ink for window 0x{window:X}'s own client area "
                    + $"({observation.InkedPixels} of {observation.SampledPixels} sampled pixels differ from the "
                    + "painted background); the card is focused and blank, so the user has the keyboard and no "
                    + "question to answer"));
        }

        return LastPrompt = new CapabilityState.Available(
            $"window 0x{window:X} is asking at {bounds}: the OS reports it visible with that exact rectangle, above "
            + "every ordinary window in its own z-order, as THE FOREGROUND WINDOW, as the FOREGROUND THREAD's "
            + $"keyboard focus (GetGUIThreadInfo(0)) after {LastForegroundAttempts} attempt(s), routes a click at "
            + $"its centre TO it, and holds ink for {observation.InkedPixels} of {observation.SampledPixels} sampled "
            + "pixels of its client area. Confirms that the OPERATING SYSTEM is routing input here — NOT that a "
            + "human pressed anything, which is a manual gate, and not that the card is legible, which is a headed "
            + "claim");
    }

    /// <summary>
    /// Refuse, and take the card back OFF the screen on the way out.
    ///
    /// <para>This is not tidiness. Every refusal it serves happens with a window already visible and
    /// topmost, and the one thing worse for a user than no card is a card that is up, unanswerable
    /// and un-dismissable. Upstream reaches the same conclusion from the same place: its show path's
    /// catch force-closes every card it may have half-shown, precisely so a half-registered window
    /// cannot sit on screen with nothing able to reach it
    /// (<c>Services/LockCard/LockCardService.cs:308-313</c>).</para>
    /// </summary>
    private CapabilityState RefuseAndWithdraw(string code, string detail)
    {
        _prompting = false;
        lock (_gate)
        {
            _onKeystroke = null;
        }

        if (_window != 0)
        {
            Win32InputInterop.ShowWindow(_window, Win32InputInterop.SwHide);
        }

        return LastPrompt = Unavailable(code, detail);
    }

    /// <inheritdoc/>
    public CapabilityState Update(InputPromptContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (_disposed)
        {
            return Unavailable(InputReasonCodes.InputPresenceDisposed,
                "this input presence was disposed; there is no card whose content could change");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(InputReasonCodes.InputMechanismAbsent,
                "Win32InputPresence paints through GDI, which is a Windows mechanism; nothing was ever asked");
        }

        if (!_prompting || _window == 0)
        {
            return Unavailable(InputReasonCodes.InputNothingPrompted,
                "no card is up on this presence, so there is nothing to repaint and nothing succeeded");
        }

        lock (_gate)
        {
            _content = content;
        }

        var inked = PaintNow();
        if (inked <= 0)
        {
            return new CapabilityState.Degraded(
                "the card is still up and still holds the input",
                new CapabilityReason(InputReasonCodes.InputPromptNotInked,
                    $"the OS holds no ink for window 0x{_window:X} after the repaint"));
        }

        return new CapabilityState.Available(
            $"window 0x{_window:X} was repainted and the OS holds ink for {inked} sampled pixel(s) of its client "
            + "area — confirmed by reading the window's own device context back, not by the draw call returning");
    }

    /// <inheritdoc/>
    public CapabilityState Dismiss()
    {
        if (_disposed)
        {
            return Unavailable(InputReasonCodes.InputPresenceDisposed,
                "this input presence was disposed; there is no card to take down");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(InputReasonCodes.InputMechanismAbsent,
                "Win32InputPresence drives USER32 windows, which is a Windows mechanism; nothing was ever asked");
        }

        if (_window == 0)
        {
            return Unavailable(InputReasonCodes.InputNothingPrompted,
                "this presence has never put a card up, so there was nothing to take down and nothing succeeded");
        }

        var window = _window;
        Win32InputInterop.ShowWindow(window, Win32InputInterop.SwHide);
        lock (_gate)
        {
            _onKeystroke = null;
        }

        _prompting = false;
        var observation = ReadAndRemember();

        if (observation.WindowVisible)
        {
            return Unavailable(InputReasonCodes.InputPromptNotOnScreen,
                $"after ShowWindow(SW_HIDE) the OS still reports card 0x{window:X} visible — it was not taken down, "
                + "only reported taken down");
        }

        if (observation.HitTestRoutesHere)
        {
            return Unavailable(InputReasonCodes.InputNotCaptured,
                $"after ShowWindow(SW_HIDE) the window manager still routes the card's centre to 0x{window:X}: a "
                + "hidden window still eating input is the worst of both states");
        }

        if (observation.IsForegroundWindow)
        {
            return Unavailable(InputReasonCodes.InputNotCaptured,
                $"after ShowWindow(SW_HIDE) the OS still reports card 0x{window:X} as the foreground window, so the "
                + "user's keyboard is still pointed at a window they cannot see");
        }

        return new CapabilityState.Available(
            $"card 0x{window:X} is down: the OS no longer reports it visible, no longer routes its centre to it, and "
            + "no longer calls it the foreground window");
    }

    /// <inheritdoc/>
    public InputCaptureObservation Observe()
    {
        if (!OperatingSystem.IsWindows() || _window == 0)
        {
            return InputCaptureObservation.NotAsked;
        }

        var window = _window;
        var exists = Win32InputInterop.IsWindow(window);
        var visible = exists && Win32InputInterop.IsWindowVisible(window);

        var held = default(InputBounds);
        if (exists && Win32InputInterop.GetWindowRect(window, out var rect))
        {
            held = new InputBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        var (x, y) = held.Width > 0 ? held.Centre : (0, 0);
        var winner = held.Width > 0
            ? Win32InputInterop.WindowFromPoint(new Win32InputInterop.Point { X = x, Y = y })
            : 0;

        var (inked, sampled) = exists ? ReadInk(window, held) : (0, 0);

        return new InputCaptureObservation(
            Asked: true,
            Window: window,
            WindowExists: exists,
            WindowVisible: visible,
            HeldBounds: held,
            AboveEveryOrdinaryWindow: exists && ReadZOrder(window).AboveEveryOrdinaryWindow,
            IsForegroundWindow: exists && Win32InputInterop.GetForegroundWindow() == window,
            SystemKeyboardFocusIsThisWindow: exists && SystemKeyboardFocus() == window,
            HitTestWinner: winner,
            InkedPixels: inked,
            SampledPixels: sampled,
            KeystrokesSeen: Volatile.Read(ref _keystrokesSeen));
    }

    /// <inheritdoc/>
    public int Pump(int maxMessages)
    {
        if (!OperatingSystem.IsWindows() || maxMessages <= 0)
        {
            return 0;
        }

        var dispatched = 0;
        while (dispatched < maxMessages
               && Win32InputInterop.PeekMessageW(out var msg, 0, 0, 0, Win32InputInterop.PmRemove))
        {
            Win32InputInterop.TranslateMessage(ref msg);
            Win32InputInterop.DispatchMessageW(ref msg);
            dispatched++;
        }

        return dispatched;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _prompting = false;
        lock (_gate)
        {
            _onKeystroke = null;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var window = _window;
        _window = 0;
        if (window != 0 && !Win32InputInterop.DestroyWindow(window))
        {
            TeardownDiagnostic =
                $"DestroyWindow(0x{window:X}) returned FALSE (last-error {Marshal.GetLastWin32Error()}); the card's "
                + "window may still exist";
        }

        if (_classAtom != 0)
        {
            Win32InputInterop.UnregisterClassW(_windowClassName, _moduleHandle);
            _classAtom = 0;
        }

        GC.KeepAlive(_windowProc);
    }

    // ---------- the OS round trips ----------

    /// <summary>
    /// The escalation ladder, and every rung of it was measured before it was written (SP-110
    /// plan.md §0).
    ///
    /// <para>Rung 1 is what WPF does: <c>SetForegroundWindow</c>
    /// (<c>Windows/LockCardWindow.xaml.cs:598</c>). Measured here returning FALSE from a process
    /// that did not already own the foreground, with the OS keeping the keyboard where it was.
    /// Upstream survives that because it re-grabs from its own <c>Deactivated</c> handler forever
    /// (<c>:160-176</c>); this port must EARN the claim or refuse it, so rung 2 attaches this
    /// thread's input queue to the current foreground thread's and asks again — measured returning
    /// TRUE and handing over both the foreground and the system keyboard focus.</para>
    ///
    /// <para>The attach is held for the three calls it takes and detached immediately, including on
    /// the failure path. It is a DIVERGENCE from WPF and it is recorded as one.</para>
    /// </summary>
    private int TakeForeground(nint window)
    {
        for (var attempt = 1; attempt <= MaxForegroundAttempts; attempt++)
        {
            Win32InputInterop.SetForegroundWindow(window);
            Win32InputInterop.SetFocus(window);
            if (Win32InputInterop.GetForegroundWindow() == window && SystemKeyboardFocus() == window)
            {
                return attempt;
            }

            Escalate(window);
            if (Win32InputInterop.GetForegroundWindow() == window && SystemKeyboardFocus() == window)
            {
                return attempt;
            }
        }

        return MaxForegroundAttempts;
    }

    private void Escalate(nint window)
    {
        var foreground = Win32InputInterop.GetForegroundWindow();
        var foregroundThread = foreground == 0 ? 0u : Win32InputInterop.GetWindowThreadProcessId(foreground, out _);
        var ourThread = Win32InputInterop.GetCurrentThreadId();
        var attached = foregroundThread != 0
            && foregroundThread != ourThread
            && Win32InputInterop.AttachThreadInput(ourThread, foregroundThread, true);

        try
        {
            Win32InputInterop.SetForegroundWindow(window);
            Win32InputInterop.BringWindowToTop(window);
            Win32InputInterop.SetActiveWindow(window);
            Win32InputInterop.SetFocus(window);
        }
        finally
        {
            if (attached)
            {
                Win32InputInterop.AttachThreadInput(ourThread, foregroundThread, false);
            }
        }
    }

    /// <summary>
    /// <c>GetGUIThreadInfo(0).hwndFocus</c> — the FOREGROUND thread's focus, which is the operating
    /// system's own statement of where the next keystroke goes. Never the thread-local read; see
    /// <see cref="InputCaptureObservation.SystemKeyboardFocusIsThisWindow"/>.
    /// </summary>
    private static nint SystemKeyboardFocus()
    {
        var info = new Win32InputInterop.GuiThreadInfo
        {
            cbSize = (uint)Marshal.SizeOf<Win32InputInterop.GuiThreadInfo>(),
        };

        return Win32InputInterop.GetGUIThreadInfo(0, ref info) ? info.hwndFocus : 0;
    }

    private InputCaptureObservation ReadAndRemember() => LastObservation = Observe();

    /// <summary>
    /// Paint the card, then read its own device context back and count the pixels that are not the
    /// background. Returns the ink count.
    ///
    /// <para><b>Why the read-back is from the WINDOW and not from what was written.</b> The same
    /// argument SP-100 made for the overlay's content: a <c>DrawText</c> that returned says a GDI
    /// call succeeded; pixels read back out of the window's DC say the OS holds them in the surface
    /// the compositor draws from. Measured before this was written: immediately after the draw, with
    /// no wait of any kind, the window's DC returns 161 non-background pixels of 7600 sampled
    /// (SP-110 plan.md §0, F6).</para>
    /// </summary>
    private int PaintNow()
    {
        var window = _window;
        if (window == 0)
        {
            return 0;
        }

        var dc = Win32InputInterop.GetDC(window);
        if (dc == 0)
        {
            return 0;
        }

        try
        {
            PaintInto(dc);
        }
        finally
        {
            Win32InputInterop.ReleaseDC(window, dc);
        }

        var (inked, _) = ReadInk(window, CurrentBounds());
        return inked;
    }

    private InputBounds CurrentBounds()
    {
        lock (_gate)
        {
            return _bounds;
        }
    }

    private void PaintInto(nint dc)
    {
        InputPromptContent content;
        InputBounds bounds;
        lock (_gate)
        {
            content = _content;
            bounds = _bounds;
        }

        var width = Math.Max(1, bounds.Width);
        var height = Math.Max(1, bounds.Height);

        var background = Win32InputInterop.CreateSolidBrush(BackgroundColour);
        try
        {
            var whole = new Win32InputInterop.Rect { Left = 0, Top = 0, Right = width, Bottom = height };
            Win32InputInterop.FillRect(dc, ref whole, background);
        }
        finally
        {
            Win32InputInterop.DeleteObject(background);
        }

        Win32InputInterop.SetBkMode(dc, Win32InputInterop.BkModeTransparent);

        // The four bands, in the order upstream's card stacks them: progress, question, the echo of
        // what has been typed, then the exit hint at the foot (Windows/LockCardWindow.xaml).
        var progress = Band(width, height, 0.06, 0.16);
        var question = Band(width, height, 0.22, 0.52);
        var answer = Band(width, height, 0.58, 0.76);
        var hint = Band(width, height, 0.82, 0.96);

        Win32InputInterop.SetTextColor(dc, AnswerColour);
        Win32InputInterop.DrawTextW(dc, content.Progress, -1, ref progress, CentredSingleLine);

        Win32InputInterop.SetTextColor(dc, QuestionColour);
        Win32InputInterop.DrawTextW(dc, content.Question, -1, ref question, CentredWrapped);

        Win32InputInterop.SetTextColor(dc, AnswerColour);
        Win32InputInterop.DrawTextW(dc, content.Answer, -1, ref answer, CentredSingleLine);
        Win32InputInterop.DrawTextW(dc, content.Hint, -1, ref hint, CentredSingleLine);
    }

    private const uint CentredSingleLine =
        Win32InputInterop.DtCenter | Win32InputInterop.DtVcenter | Win32InputInterop.DtSinglelineFormat
        | Win32InputInterop.DtEndEllipsis;

    private const uint CentredWrapped =
        Win32InputInterop.DtCenter | Win32InputInterop.DtWordbreak;

    private static Win32InputInterop.Rect Band(int width, int height, double top, double bottom) =>
        new()
        {
            Left = width / 12,
            Top = (int)(height * top),
            Right = width - (width / 12),
            Bottom = (int)(height * bottom),
        };

    /// <summary>
    /// Ask the OS for the card's own pixels back and count the ones that are not its background. The
    /// question band only: that is where the text the user must read is, and a count over the whole
    /// window would be satisfied by the hint line alone.
    /// </summary>
    private static (int Inked, int Sampled) ReadInk(nint window, InputBounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return (0, 0);
        }

        var dc = Win32InputInterop.GetDC(window);
        if (dc == 0)
        {
            return (0, 0);
        }

        try
        {
            var left = bounds.Width / 12;
            var right = bounds.Width - left;
            var top = (int)(bounds.Height * 0.22);
            var bottom = (int)(bounds.Height * 0.52);
            var inked = 0;
            var sampled = 0;

            for (var y = top; y < bottom; y += InkSampleStride)
            {
                for (var x = left; x < right; x += InkSampleStride)
                {
                    sampled++;
                    if (Win32InputInterop.GetPixel(dc, x, y) != BackgroundColour)
                    {
                        inked++;
                    }
                }
            }

            return (inked, sampled);
        }
        finally
        {
            Win32InputInterop.ReleaseDC(window, dc);
        }
    }

    /// <summary>
    /// The card's window procedure. It does three things: repaint from the retained content so a
    /// card the OS invalidates does not go blank, turn delivered keys into
    /// <see cref="InputKeystroke"/>s for the caller, and refuse to be closed by anything but the
    /// caller — <c>WM_CLOSE</c> becomes a cancel keystroke rather than a destroyed window, because
    /// the decision to let go of a lock card belongs to the module and its strict-mode rule
    /// (<c>Windows/LockCardWindow.xaml.cs:678-681</c> blocks Alt+F4 for exactly this reason).
    ///
    /// <para>Runs on a native callback frame, so nothing here may throw: a caller's exception is
    /// caught and counted rather than allowed to kill the process.</para>
    /// </summary>
    private nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case Win32InputInterop.WmPaint:
            {
                var dc = Win32InputInterop.BeginPaint(window, out var paint);
                if (dc != 0)
                {
                    PaintInto(dc);
                    Win32InputInterop.EndPaint(window, ref paint);
                    return 0;
                }

                break;
            }

            case Win32InputInterop.WmKeydown:
            case Win32InputInterop.WmSyskeydown:
            {
                Interlocked.Increment(ref _keystrokesSeen);
                var virtualKey = (int)(wParam & 0xFFFF);
                if (virtualKey == Win32InputInterop.VkEscape)
                {
                    Raise(new InputKeystroke(InputKeystrokeKind.Cancel, '\0', virtualKey));
                }
                else if (virtualKey == Win32InputInterop.VkBack)
                {
                    Raise(new InputKeystroke(InputKeystrokeKind.Backspace, '\0', virtualKey));
                }
                else
                {
                    Raise(new InputKeystroke(InputKeystrokeKind.Key, '\0', virtualKey));
                }

                break;
            }

            case Win32InputInterop.WmChar:
            {
                var character = (char)(wParam & 0xFFFF);
                // Control characters are not typing. With no edit control there is no paste route
                // at all, so upstream's clipboard-gesture list (:246-264) reduces to exactly this:
                // Ctrl+V arrives as WM_CHAR 0x16, Ctrl+Z as 0x1A, and neither is a character the
                // user produced. Backspace and Escape already have their own kinds above.
                if (character >= ' ' && character != '\u007F')
                {
                    Interlocked.Increment(ref _keystrokesSeen);
                    Raise(new InputKeystroke(InputKeystrokeKind.Character, character, character));
                }

                return 0;
            }

            case Win32InputInterop.WmClose:
                Raise(new InputKeystroke(InputKeystrokeKind.Cancel, '\0', Win32InputInterop.VkEscape));
                return 0;
        }

        return Win32InputInterop.DefWindowProcW(window, message, wParam, lParam);
    }

    private void Raise(InputKeystroke keystroke)
    {
        Action<InputKeystroke>? callback;
        lock (_gate)
        {
            callback = _onKeystroke;
        }

        if (callback is null)
        {
            return;
        }

        try
        {
            callback(keystroke);
        }
        catch
        {
            // A native callback frame: an escaping exception is a process kill, not a fault. Counted
            // rather than silently swallowed, so a caller that throws on every keystroke is visible.
            Interlocked.Increment(ref _callbackFaults);
        }
    }

    private readonly record struct ZOrderPosition(int Index, int FirstOrdinaryIndex, int VisibleCount)
    {
        internal bool AboveEveryOrdinaryWindow =>
            Index >= 0 && (FirstOrdinaryIndex < 0 || Index < FirstOrdinaryIndex);
    }

    /// <summary>Where the OS itself puts this window among the visible top-level windows. The
    /// ORDERING is the fact; <c>WS_EX_TOPMOST</c> is a flag, and a flag is what the first port
    /// attempt trusted.</summary>
    private static ZOrderPosition ReadZOrder(nint window)
    {
        var index = -1;
        var firstOrdinary = -1;
        var visible = 0;

        for (var candidate = Win32InputInterop.GetTopWindow(0);
             candidate != 0;
             candidate = Win32InputInterop.GetWindow(candidate, Win32InputInterop.GwHwndnext))
        {
            if (!Win32InputInterop.IsWindowVisible(candidate))
            {
                continue;
            }

            if (candidate == window)
            {
                index = visible;
            }
            else if (firstOrdinary < 0)
            {
                var exStyle = (uint)Win32InputInterop.GetWindowLongPtrW(candidate, Win32InputInterop.GwlExstyle);
                if ((exStyle & Win32InputInterop.WsExTopmost) == 0)
                {
                    firstOrdinary = visible;
                }
            }

            visible++;
        }

        return new ZOrderPosition(index, firstOrdinary, visible);
    }

    private nint EnsureWindow(InputBounds bounds, out string failure)
    {
        failure = string.Empty;
        if (_window != 0)
        {
            return _window;
        }

        _moduleHandle = Win32InputInterop.GetModuleHandleW(null);
        var windowClass = new Win32InputInterop.WndClassExW
        {
            cbSize = (uint)Marshal.SizeOf<Win32InputInterop.WndClassExW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            hInstance = _moduleHandle,
            lpszClassName = _windowClassName,
        };

        _classAtom = Win32InputInterop.RegisterClassExW(ref windowClass);
        if (_classAtom == 0)
        {
            failure = $"RegisterClassEx(\"{_windowClassName}\") returned 0 "
                + $"(last-error {Marshal.GetLastWin32Error()}); no card window could be created";
            return 0;
        }

        // NOT visible at creation, and deliberately carrying neither WS_EX_NOACTIVATE nor
        // WS_EX_TRANSPARENT — those two are the overlay's, and their absence is what makes this
        // window the opposite kind of window.
        _window = Win32InputInterop.CreateWindowExW(
            Win32InputInterop.WsExToolwindow,
            _windowClassName,
            "CCP input prompt",
            Win32InputInterop.WsPopup,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            0, 0, _moduleHandle, 0);

        if (_window == 0)
        {
            failure = $"CreateWindowEx for class \"{_windowClassName}\" at {bounds} returned NULL "
                + $"(last-error {Marshal.GetLastWin32Error()})";
            Win32InputInterop.UnregisterClassW(_windowClassName, _moduleHandle);
            _classAtom = 0;
            return 0;
        }

        return _window;
    }

    private static string Describe(nint window)
    {
        if (window == 0)
        {
            return "no window at all";
        }

        var buffer = new StringBuilder(256);
        var copied = Win32InputInterop.GetClassNameW(window, buffer, buffer.Capacity);
        return copied > 0 ? $"0x{window:X} (class \"{buffer}\")" : $"0x{window:X}";
    }

    private static CapabilityState.Unavailable Unavailable(string code, string detail) =>
        new(new CapabilityReason(code, detail));
}
