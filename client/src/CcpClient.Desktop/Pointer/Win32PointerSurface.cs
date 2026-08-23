using System.Runtime.InteropServices;
using System.Text;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Pointer;

/// <summary>The native handle behind one target, so a fact can ask the OS about it independently of
/// the thing under test.</summary>
public readonly record struct PointerNativeHandles(nint Window);

/// <summary>
/// The Windows pointer surface: one non-activating, topmost, tool-window per target, each of which
/// CATCHES its own clicks and takes nothing.
///
/// <para><b>Why one window per target and not one shared host.</b> Upstream has both paths. The
/// shared-host path decides which bubble was hit in USER SPACE, from an immutable disc snapshot
/// rebuilt once per UI tick and read on the mouse-hook thread (<c>Services/BubbleService.cs</c>
/// primer §4c/§4e and gotcha 4) — which is exactly the race predicted for this packet: the
/// hit test's answer is a function of a position that changes between asking and clicking. The
/// per-window path (<c>:3113</c>, <c>MouseLeftButtonDown</c> on the bubble's own window) has no such
/// gap, because the arbiter is the window manager at the instant of the click, over the position the
/// operating system itself holds. <b>This port takes the per-window path and takes its cost with
/// it</b>: upstream caps it at three concurrent bubbles because each move is a <c>SetWindowPos</c>
/// (<c>:26</c>), and so does <see cref="MaxConcurrentTargets"/>.</para>
///
/// <para><b>What that buys, stated exactly.</b> Nothing in this file hit-tests and then acts on the
/// answer. <c>WindowFromPoint</c> appears only where <see cref="CapabilityState.Available"/> is
/// earned. The residue is that a caller's belief about routing can be one move old, and that is
/// bounded arithmetic rather than a hope — see <see cref="IPointerSurface.Move"/>.</para>
///
/// <para><b>Threading.</b> A native window belongs to the thread that created it, and every message
/// this surface cares about is delivered to that thread's queue. One thread owns the whole
/// surface.</para>
/// </summary>
public sealed class Win32PointerSurface : IPointerSurface
{
    /// <summary>
    /// How many targets this surface will hold at once.
    ///
    /// <para>Upstream's own per-window cap, and its own reason: <c>MAX_BUBBLES = 3</c>, commented
    /// <i>"per-window fallback cap (SetWindowPos-bound — keep small)"</i>
    /// (<c>Services/BubbleService.cs:26</c>), with the gotcha behind it spelled out at
    /// <c>docs/primers/BUBBLE_POP_PRIMER.md</c> §9.1 — creating and closing layered windows per
    /// bubble caused a 2 GB+ OOM with no managed exception, and resizing a rented shell triggers a
    /// synchronous <c>CompleteRender</c> UI-thread deadlock. This port keeps the cap and sidesteps
    /// the second hazard by never resizing a window after creation: a target that changes size is a
    /// close and a fresh open.</para>
    /// </summary>
    public const int MaxConcurrentTargets = 3;

    /// <summary>
    /// How many times the topmost band is re-asserted before a routing answer is taken as final.
    /// WPF re-asserts <c>HWND_TOPMOST</c> on its own bubbles for the same reason
    /// (<c>Services/BubbleService.cs:4778-4787</c>, <c>BringToFront</c>). A bounded iteration count,
    /// never a wall-clock wait.
    /// </summary>
    public const int MaxRaiseAttempts = 8;

    /// <summary>
    /// The outermost rows and columns of a target are background at every size, so the ink
    /// differential always has a control point that the painter fills and never draws on. Same
    /// constant and same reason as the card's (its own review: a fixed <c>(2,2)</c> fell inside a
    /// band once the proportional inset collapsed).
    /// </summary>
    public const int ControlMargin = 3;

    /// <summary>Roughly how many pixels the ink read samples, whatever the target's size. The read
    /// runs on every placement, and a target covering a quarter of a 4K display must not cost
    /// proportionally more than a small one.</summary>
    private const int TargetInkSamples = 400;

    private readonly string _windowClassName = "CcpClientPointerTarget." + Guid.NewGuid().ToString("N");
    private readonly Win32PointerInterop.WndProc _windowProc;
    private readonly Dictionary<int, Target> _targets = [];
    private readonly Dictionary<nint, int> _byWindow = [];

    private nint _moduleHandle;
    private ushort _classAtom;
    private int _nextTarget = 1;
    private int _mouseActivateQueries;
    private int _mouseActivateRefusals;
    private int _pressesSeen;
    private int _pressesDropped;
    private int _callbackFaults;
    private bool _disposed;

    public Win32PointerSurface()
    {
        // Held in a field for the surface's whole life: the delegate is handed to the OS as a
        // function pointer, and a collected delegate is a callback into freed memory.
        _windowProc = WindowProc;
    }

    /// <inheritdoc/>
    public Action<PointerPress>? OnPress { get; set; }

    /// <inheritdoc/>
    public int OpenTargets => _targets.Count;

    /// <inheritdoc/>
    public CapabilityState? LastPlacement { get; private set; }

    /// <inheritdoc/>
    public int MouseActivateQueries => Volatile.Read(ref _mouseActivateQueries);

    /// <inheritdoc/>
    public int MouseActivateRefusals => Volatile.Read(ref _mouseActivateRefusals);

    /// <inheritdoc/>
    public int PressesSeen => Volatile.Read(ref _pressesSeen);

    /// <summary>How many presses arrived with no listener attached. Counted rather than queued: a
    /// press is an event in time and replaying it later would be a lie about when it happened.</summary>
    public int PressesDropped => Volatile.Read(ref _pressesDropped);

    /// <summary>How many times a caller's press callback threw and was contained. A callback that
    /// throws on a native callback frame would kill the process, so it is caught — and COUNTED,
    /// because a silently swallowed exception is the other way to lose a defect.</summary>
    public int CallbackFaults => Volatile.Read(ref _callbackFaults);

    /// <summary>Anything this surface could not clean up. Null when teardown was complete.</summary>
    public string? TeardownDiagnostic { get; private set; }

    /// <inheritdoc/>
    public bool CanReachAPointer => ObserveStation().Confirmed;

    /// <summary>The window behind one target, or zero when this surface does not hold it.</summary>
    public PointerNativeHandles NativeHandlesFor(int target) =>
        new(_targets.TryGetValue(target, out var found) ? found.Window : 0);

    /// <inheritdoc/>
    public PointerStationObservation ObserveStation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return PointerStationObservation.NotAsked;
        }

        var station = Win32PointerInterop.GetProcessWindowStation();
        var visible = station != 0
            && Win32PointerInterop.GetUserObjectInformationW(
                station, Win32PointerInterop.UoiFlags, out var flags,
                Marshal.SizeOf<Win32PointerInterop.UserObjectFlags>(), out _)
            && (flags.dwFlags & Win32PointerInterop.WsfVisible) != 0;

        var desktop = Win32PointerInterop.GetThreadDesktop(Win32PointerInterop.GetCurrentThreadId());
        return new PointerStationObservation(
            Asked: true,
            WindowStationVisible: visible,
            DisplayCount: Win32PointerInterop.GetSystemMetrics(Win32PointerInterop.SmCmonitors),
            DesktopReachable: desktop != 0);
    }

    /// <inheritdoc/>
    public CapabilityState Open(PointerTargetRequest request, out int target)
    {
        ArgumentNullException.ThrowIfNull(request);
        target = 0;

        if (_disposed)
        {
            return LastPlacement = Unavailable(PointerReasonCodes.PointerSurfaceDisposed,
                "this pointer surface was disposed; its windows are gone and it will never place a target again");
        }

        // Backend SELECTION by platform is permitted; capability AVAILABILITY by platform is not
        // (runtime-capability-contract §2 rule 2). This branch only refuses.
        if (!OperatingSystem.IsWindows())
        {
            return LastPlacement = Unavailable(PointerReasonCodes.PointerMechanismAbsent,
                "Win32PointerSurface places non-activating click-catching windows through USER32, which is a "
                + $"Windows mechanism; this process is on {RuntimeInformation.OSDescription} and nothing was "
                + "attempted");
        }

        if (_targets.Count >= MaxConcurrentTargets)
        {
            return LastPlacement = Unavailable(PointerReasonCodes.PointerTargetLimitReached,
                $"this surface already holds {_targets.Count} of {MaxConcurrentTargets} targets; upstream's own "
                + "per-window cap is three and its reason is that every move is a SetWindowPos "
                + "(Services/BubbleService.cs:26). Nothing was placed");
        }

        // The necessary condition, asked BEFORE a window exists: a process with no interactive
        // window station can create an hwnd that satisfies every check below and reaches nobody.
        var station = ObserveStation();
        if (!station.Confirmed)
        {
            return LastPlacement = Unavailable(PointerReasonCodes.PointerNoInteractiveStation,
                $"the OS reports window-station-visible={station.WindowStationVisible}, displays={station.DisplayCount}, "
                + $"desktop-reachable={station.DesktopReachable}; this process cannot put a clickable target in front "
                + "of anybody, so nothing was placed and nothing is claimed");
        }

        var window = CreateTargetWindow(request.Bounds, out var creationFailure);
        if (window == 0)
        {
            return LastPlacement = Unavailable(PointerReasonCodes.PointerWindowCreationFailed, creationFailure);
        }

        var handle = _nextTarget++;
        var entry = new Target(handle, window, request.Bounds, request.Fill, request.Ink);
        _targets[handle] = entry;
        _byWindow[window] = handle;
        target = handle;

        return LastPlacement = Place(entry, request.Bounds);
    }

    /// <inheritdoc/>
    public CapabilityState Move(int target, PointerBounds bounds)
    {
        if (_disposed)
        {
            return LastPlacement = Unavailable(PointerReasonCodes.PointerSurfaceDisposed,
                "this pointer surface was disposed; nothing it once placed can be moved");
        }

        if (!_targets.TryGetValue(target, out var entry))
        {
            return LastPlacement = UnknownTarget(target);
        }

        // The window is NEVER resized after creation. Upstream's #494 is a synchronous
        // CompleteRender deadlock triggered by resizing a rented shell (BUBBLE_POP_PRIMER §9.1),
        // and the port's answer is to make the extents part of a target's identity: a size change
        // is a close and a fresh open, which the module does.
        if (bounds.Width != entry.Bounds.Width || bounds.Height != entry.Bounds.Height)
        {
            return LastPlacement = Unavailable(PointerReasonCodes.PointerTargetNotPlaced,
                $"target {target} was opened at {entry.Bounds.Width}x{entry.Bounds.Height} and a move to "
                + $"{bounds.Width}x{bounds.Height} would RESIZE it. This surface never resizes a placed window "
                + "(upstream's #494 deadlock, BUBBLE_POP_PRIMER §9.1); close the target and open a new one");
        }

        return LastPlacement = Place(entry, bounds);
    }

    /// <inheritdoc/>
    public CapabilityState Close(int target)
    {
        if (!_targets.TryGetValue(target, out var entry))
        {
            return UnknownTarget(target);
        }

        var foregroundBefore = Win32PointerInterop.GetForegroundWindow();
        var point = entry.Bounds.Centre;

        Win32PointerInterop.ShowWindow(entry.Window, Win32PointerInterop.SwHide);

        var stillVisible = Win32PointerInterop.IsWindowVisible(entry.Window);
        var stillRoutes = HitTestOnce(point) == entry.Window;
        var foregroundAfter = Win32PointerInterop.GetForegroundWindow();

        DestroyTarget(entry);

        if (stillVisible || stillRoutes)
        {
            return Unavailable(PointerReasonCodes.PointerTargetNotPlaced,
                $"after hiding target {target} the OS reports visible={stillVisible} and hit-test-still-routes-here="
                + $"{stillRoutes} at {point.X},{point.Y}; the window was destroyed anyway, and the take-down is not "
                + "claimed");
        }

        if (foregroundBefore != foregroundAfter)
        {
            return Unavailable(PointerReasonCodes.PointerTargetTookTheForeground,
                $"closing target {target} moved the foreground from {Describe(foregroundBefore)} to "
                + $"{Describe(foregroundAfter)}; taking a target down must cost the user nothing");
        }

        return Available($"target {target} is off the desktop: the OS reports it invisible, the hit test at "
            + $"{point.X},{point.Y} no longer answers it, and the foreground is still {Describe(foregroundAfter)}");
    }

    /// <inheritdoc/>
    public PointerTargetObservation Observe(int target)
    {
        if (!OperatingSystem.IsWindows() || !_targets.TryGetValue(target, out var entry))
        {
            return PointerTargetObservation.NotAsked;
        }

        var foreground = Win32PointerInterop.GetForegroundWindow();
        return Read(entry, foreground);
    }

    /// <inheritdoc/>
    public int Pump(int max)
    {
        if (!OperatingSystem.IsWindows() || max <= 0)
        {
            return 0;
        }

        // FILTERED TO THIS SURFACE'S OWN WINDOWS, one PeekMessageW per target, and the filter is the
        // load-bearing part. PeekMessageW(hWnd 0) drains the WHOLE CALLING THREAD, and in the
        // product that thread is Avalonia's UI thread: CompositionRoot builds SessionParticipant
        // (Lifecycle/CompositionRoot.cs:263), which builds BubblePopSurfacePresenter.ForProduct with
        // a dispatch that is Dispatcher.UIThread.Post (Session/SessionParticipant.cs:213-219, :269),
        // and StepOnce calls this from inside it (Effects/BubblePopSurfacePresenter.cs:374). An
        // unfiltered drain there pulls Avalonia's own window messages out of Avalonia's loop and
        // dispatches them from inside a bubble step - a reentrancy and ordering hazard rather than
        // lost messages, since DispatchMessageW still reaches the right WndProc. In the suite the
        // same drain eats the harness's per-thread floor window and whatever scratch windows a fact
        // has up; RealDesktopWindowFloor already had to filter ITS drain for exactly this reason
        // (RealDesktopCollection.cs). A surface drains what it owns and nothing else.
        //
        // Round-robin rather than window-by-window so one busy target cannot spend the whole budget
        // while another target's press waits for the next step. The handles are snapshotted because
        // dispatching a press can close a target from inside the callback.
        var windows = _byWindow.Keys.ToArray();
        var dispatched = 0;
        bool any;
        do
        {
            any = false;
            foreach (var window in windows)
            {
                if (dispatched >= max)
                {
                    return dispatched;
                }

                if (!Win32PointerInterop.PeekMessageW(out var msg, window, 0, 0, Win32PointerInterop.PmRemove))
                {
                    continue;
                }

                Win32PointerInterop.TranslateMessage(ref msg);
                Win32PointerInterop.DispatchMessageW(ref msg);
                dispatched++;
                any = true;
            }
        }
        while (any);

        return dispatched;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        OnPress = null;

        var failures = new List<string>();
        foreach (var entry in _targets.Values.ToArray())
        {
            if (OperatingSystem.IsWindows()
                && Win32PointerInterop.IsWindow(entry.Window)
                && !Win32PointerInterop.DestroyWindow(entry.Window))
            {
                failures.Add($"DestroyWindow(0x{entry.Window:X}) for target {entry.Handle} returned FALSE "
                    + $"(last-error {Marshal.GetLastWin32Error()})");
            }
        }

        _targets.Clear();
        _byWindow.Clear();

        if (_classAtom != 0 && OperatingSystem.IsWindows())
        {
            if (!Win32PointerInterop.UnregisterClassW(_windowClassName, _moduleHandle))
            {
                failures.Add($"UnregisterClass(\"{_windowClassName}\") returned FALSE "
                    + $"(last-error {Marshal.GetLastWin32Error()})");
            }

            _classAtom = 0;
        }

        TeardownDiagnostic = failures.Count == 0 ? null : string.Join("; ", failures);
    }

    // ---------------------------------------------------------------------------------------
    //  Placement, and the six questions Available rests on
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Place or re-place one target and ask the OS everything. <b>The foreground is sampled BEFORE
    /// the window is touched</b>, which is what makes the non-activation claim a comparison rather
    /// than an assertion.
    /// </summary>
    private CapabilityState Place(Target entry, PointerBounds bounds)
    {
        var foregroundBefore = Win32PointerInterop.GetForegroundWindow();

        // SWP_NOACTIVATE is upstream's own flag on the same operation for the same reason
        // (Services/BubbleService.cs:4785 on the topmost kick, :4807 on the reposition).
        if (!Win32PointerInterop.SetWindowPos(
                entry.Window, Win32PointerInterop.HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                Win32PointerInterop.SwpShowwindow | Win32PointerInterop.SwpNoactivate))
        {
            var error = Marshal.GetLastWin32Error();
            return Unavailable(PointerReasonCodes.PointerTargetNotPlaced,
                $"SetWindowPos(HWND_TOPMOST, {bounds}, SWP_SHOWWINDOW|SWP_NOACTIVATE) returned FALSE for target "
                + $"{entry.Handle} (window 0x{entry.Window:X}, last-error {error}); nothing is on screen");
        }

        entry.Bounds = bounds;

        // Painted before anything is claimed, so a target is never on screen blank while it is the
        // thing the user is being asked to hit.
        PaintNow(entry);

        var observation = Read(entry, foregroundBefore);
        entry.LastObservation = observation;

        return Classify(entry, bounds, observation);
    }

    private CapabilityState Classify(Target entry, PointerBounds asked, PointerTargetObservation observation)
    {
        if (!observation.WindowExists || !observation.WindowVisible)
        {
            return Unavailable(PointerReasonCodes.PointerTargetNotPlaced,
                $"after placement the OS reports target {entry.Handle} (window 0x{entry.Window:X}) "
                + $"exists={observation.WindowExists} visible={observation.WindowVisible}; it is not on screen");
        }

        if (observation.HeldBounds != asked)
        {
            return Unavailable(PointerReasonCodes.PointerTargetNotPlaced,
                $"the OS holds {observation.HeldBounds} for target {entry.Handle} but {asked} was asked for; a "
                + "routing answer about the wrong rectangle is an answer about somewhere else");
        }

        // The foreground clause is checked BEFORE routing: a target that stole the foreground has
        // already harmed the user, and reporting "not routable" for it would name the wrong fault.
        if (!observation.ForegroundUndisturbed)
        {
            return Unavailable(PointerReasonCodes.PointerTargetTookTheForeground,
                $"placing target {entry.Handle} moved the foreground from {Describe(observation.ForegroundBefore)} to "
                + $"{Describe(observation.ForegroundAfter)}"
                + (observation.ForegroundAfter == entry.Window ? " — which IS the target" : string.Empty)
                + ". A pointer target must take nothing from anybody, so nothing is claimed");
        }

        if (!observation.NonActivatingStyleHeld || observation.ClickThroughStyleHeld)
        {
            return Unavailable(PointerReasonCodes.PointerTargetStyleWrong,
                $"the OS holds WS_EX_NOACTIVATE={observation.NonActivatingStyleHeld} and "
                + $"WS_EX_TRANSPARENT={observation.ClickThroughStyleHeld} for target {entry.Handle}. A target "
                + "without the first can steal the user's foreground on a click; a target with the second is "
                + "unpoppable and the click falls through to the desktop (Services/BubbleService.cs:4880-4892)");
        }

        if (!observation.AboveEveryOrdinaryWindow)
        {
            return Unavailable(PointerReasonCodes.PointerTargetNotOnTop,
                $"the OS's own z-order walk does not put target {entry.Handle} above every ordinary window; a "
                + "target somebody else's window covers is a target nobody can hit");
        }

        if (!observation.HitTestRoutesHere)
        {
            var (x, y) = observation.HitTestPoint;
            return Unavailable(PointerReasonCodes.PointerTargetNotRoutable,
                $"after {MaxRaiseAttempts} topmost assertion(s) the window manager routes the point {x},{y} — target "
                + $"{entry.Handle}'s own centre — to {Describe(observation.HitTestWinner)} instead of 0x{entry.Window:X}. "
                + "The target is on screen and cannot be clicked");
        }

        if (!observation.Inked)
        {
            return new CapabilityState.Degraded(
                "the target is on screen, above every ordinary window, routable, and took nothing from anybody",
                new CapabilityReason(PointerReasonCodes.PointerTargetBlank,
                    $"the OS holds {observation.InkedPixels} non-background pixels of {observation.SampledPixels} "
                    + $"sampled in target {entry.Handle}'s client area, and background-held="
                    + $"{observation.BackgroundHeld}. A clickable rectangle nobody can see is not a bubble"));
        }

        var (hx, hy) = observation.HitTestPoint;
        return Available(
            $"the OS holds target {entry.Handle} at {observation.HeldBounds} with WS_EX_NOACTIVATE and without "
            + $"WS_EX_TRANSPARENT, above every ordinary window, routes the point {hx},{hy} to it, holds "
            + $"{observation.InkedPixels} of {observation.SampledPixels} sampled pixels as ink over a confirmed "
            + $"background, and left the foreground on {Describe(observation.ForegroundAfter)} throughout");
    }

    private PointerTargetObservation Read(Target entry, nint foregroundBefore)
    {
        var window = entry.Window;
        var exists = Win32PointerInterop.IsWindow(window);
        var visible = exists && Win32PointerInterop.IsWindowVisible(window);

        var held = default(PointerBounds);
        if (exists && Win32PointerInterop.GetWindowRect(window, out var rect))
        {
            held = new PointerBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        var exStyle = exists
            ? (uint)Win32PointerInterop.GetWindowLongPtrW(window, Win32PointerInterop.GwlExstyle)
            : 0;

        var zOrder = ReadZOrder(window);
        var (px, py) = held.Width > 0 ? held.Centre : entry.Bounds.Centre;
        var winner = HitTest(window, new Win32PointerInterop.Point { X = px, Y = py });

        ReadInk(entry, out var backgroundHeld, out var inked, out var sampled);

        return new PointerTargetObservation(
            Asked: true,
            Window: window,
            WindowExists: exists,
            WindowVisible: visible,
            HeldBounds: held,
            NonActivatingStyleHeld: (exStyle & Win32PointerInterop.WsExNoactivate) != 0,
            ClickThroughStyleHeld: (exStyle & Win32PointerInterop.WsExTransparent) != 0,
            AboveEveryOrdinaryWindow: zOrder.AboveEveryOrdinaryWindow,
            HitTestWinner: winner,
            HitTestPoint: (px, py),
            ForegroundBefore: foregroundBefore,
            ForegroundAfter: Win32PointerInterop.GetForegroundWindow(),
            BackgroundHeld: backgroundHeld,
            InkedPixels: inked,
            SampledPixels: sampled);
    }

    /// <summary>
    /// One routing question, preceded by a topmost re-assertion and repeated while the answer is
    /// not this window. Bounded iteration, never a wall-clock wait — the same shape and the same
    /// upstream provenance as the overlay's (<c>Overlay/Win32OverlayPresence.cs:602</c>).
    /// </summary>
    private static nint HitTest(nint window, Win32PointerInterop.Point point)
    {
        var winner = (nint)0;
        for (var attempt = 1; attempt <= MaxRaiseAttempts; attempt++)
        {
            Win32PointerInterop.SetWindowPos(
                window, Win32PointerInterop.HwndTopmost, 0, 0, 0, 0,
                Win32PointerInterop.SwpNomove | Win32PointerInterop.SwpNosize | Win32PointerInterop.SwpNoactivate);

            winner = Win32PointerInterop.WindowFromPoint(point);
            if (winner == window)
            {
                return winner;
            }
        }

        return winner;
    }

    /// <summary>The routing question with no re-assertion at all: what the OS says right now. Used
    /// where the expected answer is "not this window", where re-raising would be asking the OS to
    /// disagree with the thing being checked.</summary>
    private static nint HitTestOnce((int X, int Y) point) =>
        Win32PointerInterop.WindowFromPoint(new Win32PointerInterop.Point { X = point.X, Y = point.Y });

    /// <summary>
    /// Where the OS itself puts this window among the visible top-level windows, and where the first
    /// ordinary (non-topmost) one is. The ORDERING is the fact; <c>WS_EX_TOPMOST</c> is a flag.
    /// </summary>
    private static ZOrderPosition ReadZOrder(nint window)
    {
        var index = -1;
        var firstOrdinary = -1;
        var visible = 0;

        for (var candidate = Win32PointerInterop.GetTopWindow(0);
             candidate != 0;
             candidate = Win32PointerInterop.GetWindow(candidate, Win32PointerInterop.GwHwndnext))
        {
            if (!Win32PointerInterop.IsWindowVisible(candidate))
            {
                continue;
            }

            if (candidate == window)
            {
                index = visible;
            }
            else if (firstOrdinary < 0)
            {
                var exStyle = (uint)Win32PointerInterop.GetWindowLongPtrW(candidate, Win32PointerInterop.GwlExstyle);
                if ((exStyle & Win32PointerInterop.WsExTopmost) == 0)
                {
                    firstOrdinary = visible;
                }
            }

            visible++;
        }

        return new ZOrderPosition(index, firstOrdinary, visible);
    }

    private readonly record struct ZOrderPosition(int Index, int FirstOrdinaryIndex, int VisibleCount)
    {
        internal bool AboveEveryOrdinaryWindow =>
            Index >= 0 && (FirstOrdinaryIndex < 0 || Index < FirstOrdinaryIndex);
    }

    // ---------------------------------------------------------------------------------------
    //  Content
    // ---------------------------------------------------------------------------------------

    /// <summary>The disc's bounding box inside a target of this size, in client coordinates. Inset
    /// far enough that <see cref="ControlMargin"/> is never inside it at any legal size.</summary>
    internal static (int Left, int Top, int Right, int Bottom) DiscBox(int width, int height)
    {
        var inset = Math.Max(ControlMargin + 1, Math.Min(width, height) / 10);
        return (inset, inset, width - inset, height - inset);
    }

    private void PaintNow(Target entry)
    {
        var dc = Win32PointerInterop.GetDC(entry.Window);
        if (dc == 0)
        {
            return;
        }

        try
        {
            PaintInto(dc, entry);
        }
        finally
        {
            Win32PointerInterop.ReleaseDC(entry.Window, dc);
        }
    }

    private static void PaintInto(nint dc, Target entry)
    {
        var width = entry.Bounds.Width;
        var height = entry.Bounds.Height;

        var background = new Win32PointerInterop.Rect { Left = 0, Top = 0, Right = width, Bottom = height };
        var fillBrush = Win32PointerInterop.CreateSolidBrush(entry.Fill);
        if (fillBrush != 0)
        {
            Win32PointerInterop.FillRect(dc, ref background, fillBrush);
            Win32PointerInterop.DeleteObject(fillBrush);
        }

        var inkBrush = Win32PointerInterop.CreateSolidBrush(entry.Ink);
        if (inkBrush == 0)
        {
            return;
        }

        var previousBrush = Win32PointerInterop.SelectObject(dc, inkBrush);
        var previousPen = Win32PointerInterop.SelectObject(dc, Win32PointerInterop.GetStockObject(Win32PointerInterop.NullPen));
        var (left, top, right, bottom) = DiscBox(width, height);
        Win32PointerInterop.Ellipse(dc, left, top, right, bottom);
        Win32PointerInterop.SelectObject(dc, previousPen);
        Win32PointerInterop.SelectObject(dc, previousBrush);
        Win32PointerInterop.DeleteObject(inkBrush);
    }

    /// <summary>
    /// Read the target's own client area BACK out of the operating system, differentially.
    ///
    /// <para><b>Why the differential and not a bare non-background count.</b> The M-t mutation: this
    /// window class registers no background brush, so an UNPAINTED window's device context holds
    /// whatever the OS left in it — which differs from any chosen colour just as reliably as paint
    /// does. So a control point in a margin the painter fills and never draws on must read back
    /// EXACTLY the fill, and only then does a differing pixel inside the disc count as ink.</para>
    /// </summary>
    private static void ReadInk(Target entry, out bool backgroundHeld, out int inked, out int sampled)
    {
        backgroundHeld = false;
        inked = 0;
        sampled = 0;

        var dc = Win32PointerInterop.GetDC(entry.Window);
        if (dc == 0)
        {
            return;
        }

        try
        {
            var width = entry.Bounds.Width;
            var height = entry.Bounds.Height;
            var fill = entry.Fill & 0x00FFFFFF;

            // Four control points, one per corner margin, because a single one is satisfied by a
            // single stray pixel of the right colour.
            backgroundHeld =
                (Win32PointerInterop.GetPixel(dc, ControlMargin - 1, ControlMargin - 1) & 0x00FFFFFF) == fill
                && (Win32PointerInterop.GetPixel(dc, width - ControlMargin, ControlMargin - 1) & 0x00FFFFFF) == fill
                && (Win32PointerInterop.GetPixel(dc, ControlMargin - 1, height - ControlMargin) & 0x00FFFFFF) == fill
                && (Win32PointerInterop.GetPixel(dc, width - ControlMargin, height - ControlMargin) & 0x00FFFFFF) == fill;

            var (left, top, right, bottom) = DiscBox(width, height);
            var step = SampleStep(right - left, bottom - top);
            for (var y = top; y < bottom; y += step)
            {
                for (var x = left; x < right; x += step)
                {
                    sampled++;
                    if ((Win32PointerInterop.GetPixel(dc, x, y) & 0x00FFFFFF) != fill)
                    {
                        inked++;
                    }
                }
            }
        }
        finally
        {
            Win32PointerInterop.ReleaseDC(entry.Window, dc);
        }
    }

    /// <summary>The stride that keeps the read at roughly <see cref="TargetInkSamples"/> pixels
    /// whatever the target's size, so the same read on a huge target does not cost proportionally
    /// more than on a small one.</summary>
    internal static int SampleStep(int width, int height) =>
        Math.Max(1, (int)Math.Sqrt(Math.Max(1, (double)width * height / TargetInkSamples)));

    // ---------------------------------------------------------------------------------------
    //  The window, and the three messages that make this a pointer capability
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The target's window procedure.
    ///
    /// <para><b>This is where a pointer capability differs from every other window this port
    /// owns.</b> The overlay's procedure handles nothing that is not a paint, because a
    /// click-through surface must never see a click; the card's handles five messages and no mouse
    /// message at all (<c>Input/Win32InputPresence.cs:818-877</c>). This one answers
    /// <c>WM_MOUSEACTIVATE</c> with <c>MA_NOACTIVATE</c> — <b>refusing activation while KEEPING the
    /// click</b>, upstream's own answer on its sibling surface
    /// (<c>Windows/BubbleCountWindow.xaml.cs:1823-1824</c>, hook at <c>:1831-1839</c>) — and then
    /// hands the button messages to the caller.</para>
    ///
    /// <para>Runs on a native callback frame, so nothing here may throw: a caller's exception is
    /// caught and counted rather than allowed to kill the process.</para>
    /// </summary>
    private nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case Win32PointerInterop.WmMouseactivate:
            {
                // The answer is computed, then COUNTED ONLY IF IT IS THE REFUSAL, then returned.
                // Written this way because the first draft counted unconditionally and a mutation
                // proved the counter meaningless: returning MA_ACTIVATE changed nothing anyone
                // could observe, because WS_EX_NOACTIVATE already stops the activation on Windows.
                // MouseActivateRefusals now means what its name says.
                var answer = Win32PointerInterop.MaNoactivate;
                Interlocked.Increment(ref _mouseActivateQueries);
                if (answer == Win32PointerInterop.MaNoactivate)
                {
                    Interlocked.Increment(ref _mouseActivateRefusals);
                }

                return answer;
            }

            case Win32PointerInterop.WmPaint:
            {
                var dc = Win32PointerInterop.BeginPaint(window, out var paint);
                if (dc != 0)
                {
                    if (_byWindow.TryGetValue(window, out var repaintHandle)
                        && _targets.TryGetValue(repaintHandle, out var repaintTarget))
                    {
                        PaintInto(dc, repaintTarget);
                    }

                    Win32PointerInterop.EndPaint(window, ref paint);
                    return 0;
                }

                break;
            }

            case Win32PointerInterop.WmLbuttondown:
                Raise(window, PointerPressKind.Down, lParam);
                return 0;

            case Win32PointerInterop.WmLbuttonup:
                Raise(window, PointerPressKind.Up, lParam);
                return 0;

            case Win32PointerInterop.WmClose:
                // A target is closed by its owner and by nothing else. Upstream's bubbles are the
                // same: they are destroyed by the pop animation completing or by the field being
                // cleared, never by the window manager (Services/BubbleService.cs:4715).
                return 0;
        }

        return Win32PointerInterop.DefWindowProcW(window, message, wParam, lParam);
    }

    private void Raise(nint window, PointerPressKind kind, nint lParam)
    {
        Interlocked.Increment(ref _pressesSeen);

        if (!_byWindow.TryGetValue(window, out var handle))
        {
            return;
        }

        var listener = OnPress;
        if (listener is null)
        {
            Interlocked.Increment(ref _pressesDropped);
            return;
        }

        var x = (short)(lParam & 0xFFFF);
        var y = (short)((lParam >> 16) & 0xFFFF);

        try
        {
            listener(new PointerPress(handle, kind, x, y));
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _callbackFaults);
        }
    }

    private nint CreateTargetWindow(PointerBounds bounds, out string failure)
    {
        failure = string.Empty;

        if (_classAtom == 0)
        {
            _moduleHandle = Win32PointerInterop.GetModuleHandleW(null);
            var windowClass = new Win32PointerInterop.WndClassExW
            {
                cbSize = (uint)Marshal.SizeOf<Win32PointerInterop.WndClassExW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
                hInstance = _moduleHandle,
                lpszClassName = _windowClassName,
            };

            _classAtom = Win32PointerInterop.RegisterClassExW(ref windowClass);
            if (_classAtom == 0)
            {
                failure = $"RegisterClassEx(\"{_windowClassName}\") returned 0 "
                    + $"(last-error {Marshal.GetLastWin32Error()}); no pointer target could be created";
                return 0;
            }
        }

        // WS_EX_NOACTIVATE and WS_EX_TOOLWINDOW at creation, and WS_EX_TRANSPARENT deliberately
        // absent: upstream's own three-flag decision, made once per (re)show from a known base
        // rather than OR-ed onto whatever was there (Services/BubbleService.cs:4877-4894).
        var window = Win32PointerInterop.CreateWindowExW(
            Win32PointerInterop.WsExNoactivate | Win32PointerInterop.WsExToolwindow,
            _windowClassName,
            "CCP pointer target",
            Win32PointerInterop.WsPopup,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            0, 0, _moduleHandle, 0);

        if (window == 0)
        {
            failure = $"CreateWindowEx for class \"{_windowClassName}\" at {bounds} returned NULL "
                + $"(last-error {Marshal.GetLastWin32Error()})";
            return 0;
        }

        return window;
    }

    private void DestroyTarget(Target entry)
    {
        _targets.Remove(entry.Handle);
        _byWindow.Remove(entry.Window);

        if (OperatingSystem.IsWindows() && Win32PointerInterop.IsWindow(entry.Window))
        {
            Win32PointerInterop.DestroyWindow(entry.Window);
        }
    }

    private static string Describe(nint window)
    {
        if (window == 0)
        {
            return "no window at all";
        }

        if (!OperatingSystem.IsWindows())
        {
            return $"0x{window:X}";
        }

        var buffer = new StringBuilder(256);
        var copied = Win32PointerInterop.GetClassNameW(window, buffer, buffer.Capacity);
        return copied > 0 ? $"0x{window:X} (class \"{buffer}\")" : $"0x{window:X}";
    }

    private static CapabilityState.Available Available(string detail) => new(detail);

    private static CapabilityState.Unavailable Unavailable(string code, string detail) =>
        new(new CapabilityReason(code, detail));

    private static CapabilityState.Unavailable UnknownTarget(int target) =>
        new(new CapabilityReason(PointerReasonCodes.PointerTargetUnknown,
            $"this pointer surface does not hold a target {target}; it was never opened, or it was already closed"));

    private sealed class Target(int handle, nint window, PointerBounds bounds, uint fill, uint ink)
    {
        internal int Handle { get; } = handle;

        internal nint Window { get; } = window;

        internal PointerBounds Bounds { get; set; } = bounds;

        internal uint Fill { get; } = fill;

        internal uint Ink { get; } = ink;

        internal PointerTargetObservation LastObservation { get; set; } = PointerTargetObservation.NotAsked;
    }
}
