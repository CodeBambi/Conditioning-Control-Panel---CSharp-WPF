using System.Runtime.InteropServices;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Input;

/// <summary>
/// <b>The emergency stop, and the one gesture in this application that does not need a window.</b>
///
/// <para><b>Why it exists.</b> Every effect surface this app puts on the desktop is
/// <c>HWND_TOPMOST</c>, and four of the six native surface types deliberately do NOT set
/// <c>WS_EX_TRANSPARENT</c>, so they absorb the pointer by design. The only affordance that stops a
/// session or exits the app lives INSIDE the Avalonia shell (its START/STOP button and, while a
/// descent has ducked it, a tray menu). Put those two facts together and the app had a state with
/// no way out: surfaces up, shell underneath them, and no key, chord or gesture anywhere that took
/// them down. It was reached by a real user on 2026-08-23 — nineteen visible surfaces, three of them
/// the full size of the monitor — and the process had to be killed for him from outside.
/// <c>Views/Pages/InputPanelNotices.cs</c> already said the absence out loud in shipped UI text:
/// <i>"a window that takes your keyboard must always have a way out"</i>.</para>
///
/// <para><b>Why RegisterHotKey and NOT upstream's mechanism.</b> The shipping WPF product watches
/// for a bare, rebindable panic key (default Escape) with a system-wide low-level keyboard hook —
/// <c>ConditioningControlPanel/Services/Input/GlobalKeyboardHook.cs</c> installs
/// <c>WH_KEYBOARD_LL</c> and <c>ConditioningControlPanel/MainWindow/MainWindow.xaml.cs</c> handles
/// the press. That mechanism sees EVERY keystroke on the machine, which is the input-capture
/// boundary the port's task board records as owner-blocked, and it carries two failure modes
/// upstream documents against itself at the install site: the callback is delivered on the UI
/// thread, so a wedged UI thread makes the panic key physically undeliverable, and Windows silently
/// un-installs a low-level hook whose callback overruns <c>LowLevelHooksTimeout</c>, with nothing to
/// re-install it.
///
/// <c>RegisterHotKey</c> has neither property. It observes no keystrokes at all: the OS delivers one
/// <c>WM_HOTKEY</c> carrying only the id this process registered, and it is a posted message that
/// waits in the queue rather than a callback that can time out. It therefore buys the same
/// user-visible outcome — a gesture that works from any application, with any window covering the
/// screen — without crossing the boundary that blocks the hook.</para>
///
/// <para><b>What it costs, stated rather than buried.</b> The chord is taken away from every other
/// application on the machine for as long as this app runs, so it cannot be a bare key: registering
/// Escape alone succeeds on Windows and would break Escape everywhere. <see cref="Gesture"/> is
/// <c>Ctrl+Alt+Esc</c>, measured registerable on Windows 11 while <c>Ctrl+Shift+Esc</c> (Task
/// Manager) and <c>Ctrl+Esc</c> (Start) both return <c>ERROR_HOTKEY_ALREADY_REGISTERED</c>. It is
/// NOT rebindable here and upstream's is; a rebind needs a settings surface, and shipping the escape
/// hatch was worth more than shipping the dial that renames it.</para>
///
/// <para><b>Thread affinity, and the measurement that moved it.</b> The owner window used to be
/// created on the thread that called <see cref="Arm"/> — the UI thread, whose Win32 message loop
/// Avalonia already pumps — and <see cref="Pressed"/> was raised there. That put the emergency stop
/// behind the one thread this application is known to lose: a measurement on this product at maximum
/// settings recorded the UI thread failing to answer its message loop for <b>607–1734 ms at a
/// stretch, peaking past a 2000 ms probe ceiling</b>, with one core pegged and fifteen idle. A
/// posted <c>WM_HOTKEY</c> sits in the queue of the thread that registered the hotkey, so on the old
/// shape the press was not merely late — for as long as the stall lasted it could not be OBSERVED at
/// all, and the state the user was actually in is the state in which the escape hatch is asleep.
///
/// This class now runs its <b>own</b> thread with its <b>own</b> message loop, and the window and
/// the hotkey belong to it. Nothing the UI thread does can stop that loop from running, so the press
/// is always seen; what it cannot do by itself is ACT, because a window belongs to the thread that
/// created it and every surface on the user's screen belongs to the UI thread. Handing the press
/// across that gap, and deciding what to do when the gap does not close, is
/// <see cref="PanicWatchdog"/>'s job — and this is where upstream's claim is repaid rather than
/// inverted: upstream's own watchdog can only ARM while the UI thread is still pumping, because its
/// <c>WH_KEYBOARD_LL</c> callback is delivered on that thread
/// (<c>ConditioningControlPanel/MainWindow/MainWindow.xaml.cs:886-894</c>, which says so).
/// This one has no such bound.</para>
///
/// <para><b>What that costs.</b> <see cref="Pressed"/> is raised on the panic thread and NOT on the
/// UI thread, so a handler may not touch a window from it. <see cref="Dispose"/> is now callable
/// from any thread — it asks the panic thread to take its own window down and waits for it — where
/// before it was thread-affine.</para>
/// </summary>
public sealed class Win32PanicKey : IDisposable
{
    /// <summary>Hotkey id within the owner window. This window registers exactly one.</summary>
    private const int HotkeyId = 1;

    /// <summary>The OS refused the chord — another application holds it, or the window is gone.
    /// The app must SAY so: a panic key nobody knows is dead is worse than none.</summary>
    public const string HotkeyRefused = "panic-hotkey-refused";

    /// <summary>No system-wide hotkey mechanism on this platform (everything that is not Windows).</summary>
    public const string HotkeyUnsupported = "panic-hotkey-unsupported";

    /// <summary>What the user presses. Shown to them; never inferred from the flags at a call site.</summary>
    public const string Gesture = "Ctrl+Alt+Esc";

    /// <summary>How long <see cref="Arm"/> waits for its own thread to say whether the OS granted
    /// the chord, and how long <see cref="Dispose"/> waits for that thread to give it back. Both are
    /// a handshake with a thread that does nothing but call four USER32 functions, so exceeding this
    /// means something is wrong rather than slow — and both are BOUNDED, because a panic key that
    /// hung its caller would be the wedge it exists to answer.</summary>
    private static readonly TimeSpan ThreadHandshake = TimeSpan.FromSeconds(5);

    private readonly string _windowClassName = "CcpClientPanicKey." + Guid.NewGuid().ToString("N");
    private readonly Win32PanicInterop.WndProc _windowProc;
    private readonly Action<string>? _log;
    /// <summary>The arm handshake. Deliberately never disposed: the ONE thread that can still set
    /// it is the one <see cref="Dispose"/> may fail to join, and disposing it there would turn a
    /// panic thread that came back late into an unhandled ObjectDisposedException — the emergency
    /// stop killing the process on its way out. One event per panic key, one panic key per
    /// process.</summary>
    private readonly ManualResetEventSlim _armed = new(false);

    private Thread? _thread;
    private CapabilityState? _armOutcome;
    private nint _ownerWindow;
    private nint _moduleHandle;
    private ushort _classAtom;
    private bool _registered;
    private bool _disposed;

    /// <param name="log">Where a handler's own failure is said. Optional, and the reason it exists
    /// at all is that this class used to swallow one in silence with the comment "this class holds
    /// no log sink": the press arrives inside a native window procedure, an exception crossing that
    /// frame ends the process, so it MUST be caught here — and a catch nobody can hear made the
    /// emergency path the quietest one in the application.</param>
    public Win32PanicKey(Action<string>? log = null)
    {
        // Rooted for this instance's lifetime: the OS calls this pointer back from native code,
        // and a collected delegate is an access violation rather than an exception.
        _windowProc = WindowProc;
        _log = log;
    }

    /// <summary>Raised on the arming thread when the chord is pressed, from any application.</summary>
    public event Action? Pressed;

    /// <summary>True only between an <see cref="Arm"/> the OS granted and <see cref="Dispose"/>.</summary>
    public bool IsArmed => _registered;

    /// <summary>The hidden owner window, or zero. Exposed so a test can ask USER32 rather than this
    /// object whether the registration is real.</summary>
    public nint OwnerWindow => _ownerWindow;

    /// <summary>
    /// Claim the chord. <see cref="CapabilityState.Available"/> only when the OS granted it; a
    /// refusal is typed and carries the Win32 last-error, never a silent false.
    ///
    /// <para>The window and the registration are made on this key's OWN thread, because a hotkey is
    /// delivered to the queue of the thread that registered it and that queue must belong to a
    /// thread nothing else can stall. This call blocks until that thread has an answer, which is one
    /// window creation and one <c>RegisterHotKey</c> away.</para>
    /// </summary>
    public CapabilityState Arm()
    {
        if (_disposed)
        {
            return Unavailable(HotkeyRefused, "this panic key was disposed; the chord is not held");
        }

        if (_registered)
        {
            return Available($"{Gesture} is already held by this process");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(HotkeyUnsupported,
                $"no system-wide hotkey was claimed: {Gesture} needs RegisterHotKey and this is not Windows. "
                + "The session can only be stopped from the shell window on this platform");
        }

        if (_thread is not null)
        {
            return _armOutcome ?? Unavailable(HotkeyRefused, "this panic key's thread has already answered once and refused");
        }

        _thread = new Thread(PumpUntilClosed)
        {
            IsBackground = true,
            Name = "CCP panic key",
        };

        // STA for the same reason every message pump takes it: this thread owns a top-level window
        // and dispatches messages to it, and STA is the apartment that contract is written for.
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_armed.Wait(ThreadHandshake))
        {
            return Unavailable(HotkeyRefused,
                $"the panic key's own message thread did not answer within {ThreadHandshake.TotalSeconds:0.#}s, so "
                + "nothing is known to hold the chord; there is NO system-wide emergency stop in this process");
        }

        // Published by the panic thread before it set _armed; the Wait above is the barrier.
        return _armOutcome ?? Unavailable(HotkeyRefused, "the panic key's thread answered with nothing");
    }

    /// <summary>
    /// Give the chord back and take the window down. Callable from any thread: the window belongs to
    /// the panic thread, so this ASKS that thread to destroy it (the only legal way) and waits for
    /// the thread to end, which is what makes <c>IsWindow</c> false by the time this returns.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Pressed = null;

        var thread = _thread;
        if (thread is null || !OperatingSystem.IsWindows())
        {
            _registered = false;
            _ownerWindow = 0;
            _classAtom = 0;
            return;
        }

        if (_ownerWindow != 0)
        {
            Win32PanicInterop.PostMessageW(_ownerWindow, Win32PanicInterop.WmClose, 0, 0);
        }

        if (!thread.Join(ThreadHandshake))
        {
            // Bounded rather than absent: this is the emergency stop's own teardown, and a Join that
            // could hang would park whatever thread is closing the app. The thread is a background
            // one, so the process can still exit and the OS still reclaims the window and the chord.
            _log?.Invoke(
                $"panic: the panic key's thread did not end within {ThreadHandshake.TotalSeconds:0.#}s; "
                + $"{Gesture} stays claimed until this process exits");
            return;
        }

        _thread = null;
    }

    /// <summary>
    /// The panic key's own thread: create the window, claim the chord, publish the answer, then
    /// pump until somebody posts WM_CLOSE. Everything USER32 here — the class, the window, the
    /// registration and their teardown — happens on this one thread, which is the affinity rule
    /// windows and hotkeys both carry.
    /// </summary>
    private void PumpUntilClosed()
    {
        try
        {
            _armOutcome = ClaimChord();
        }
        catch (Exception ex)
        {
            _armOutcome = Unavailable(HotkeyRefused,
                $"the panic key's thread threw while claiming {Gesture} ({ex.GetType().Name}: {ex.Message}); "
                + "there is NO system-wide emergency stop in this process");
        }
        finally
        {
            _armed.Set();
        }

        if (_armOutcome is not CapabilityState.Available)
        {
            return;
        }

        while (Win32PanicInterop.GetMessageW(out var message, 0, 0, 0) > 0)
        {
            Win32PanicInterop.TranslateMessage(ref message);
            Win32PanicInterop.DispatchMessageW(ref message);
        }

        // WM_CLOSE released the chord and destroyed the window above; this is the class, which can
        // only be unregistered once no window of it survives.
        _registered = false;
        DestroyOwnerWindow();
    }

    private CapabilityState ClaimChord()
    {
        var window = EnsureOwnerWindow(out var failure);
        if (window == 0)
        {
            return Unavailable(HotkeyRefused, failure);
        }

        if (!Win32PanicInterop.RegisterHotKey(window, HotkeyId, Win32PanicInterop.ModControl | Win32PanicInterop.ModAlt | Win32PanicInterop.ModNoRepeat, Win32PanicInterop.VkEscape))
        {
            var error = Marshal.GetLastWin32Error();
            DestroyOwnerWindow();
            return Unavailable(HotkeyRefused,
                $"RegisterHotKey({Gesture}) was refused (last-error {error}"
                + (error == Win32PanicInterop.ErrorHotkeyAlreadyRegistered ? " — ERROR_HOTKEY_ALREADY_REGISTERED, another application holds it" : string.Empty)
                + "); there is NO system-wide emergency stop in this process");
        }

        _registered = true;
        return Available($"{Gesture} is held system-wide and stops the session from any application");
    }

    private nint EnsureOwnerWindow(out string failure)
    {
        failure = string.Empty;
        if (_ownerWindow != 0)
        {
            return _ownerWindow;
        }

        _moduleHandle = Win32PanicInterop.GetModuleHandleW(null);
        var cls = new Win32PanicInterop.WndClassExW
        {
            cbSize = (uint)Marshal.SizeOf<Win32PanicInterop.WndClassExW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            hInstance = _moduleHandle,
            lpszClassName = _windowClassName,
        };

        _classAtom = Win32PanicInterop.RegisterClassExW(ref cls);
        if (_classAtom == 0)
        {
            failure = $"RegisterClassEx('{_windowClassName}') failed (last-error {Marshal.GetLastWin32Error()}); "
                + "WM_HOTKEY needs a window to be posted to and none could be created";
            return 0;
        }

        // A real top-level popup that is never shown, and deliberately not an HWND_MESSAGE child:
        // the same choice Tray/Win32TrayInterop.cs makes and for a related reason — a message-only
        // window is outside the window manager, and this one must be a normal hotkey target.
        _ownerWindow = Win32PanicInterop.CreateWindowExW(
            Win32PanicInterop.WsExToolwindow, _windowClassName, "CCP panic key", Win32PanicInterop.WsPopup,
            0, 0, 0, 0, 0, 0, _moduleHandle, 0);
        if (_ownerWindow == 0)
        {
            failure = $"CreateWindowEx for the hidden panic-key owner failed (last-error {Marshal.GetLastWin32Error()})";
            Win32PanicInterop.UnregisterClassW(_windowClassName, _moduleHandle);
            _classAtom = 0;
            return 0;
        }

        return _ownerWindow;
    }

    private void DestroyOwnerWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            _ownerWindow = 0;
            _classAtom = 0;
            return;
        }

        if (_ownerWindow != 0)
        {
            Win32PanicInterop.DestroyWindow(_ownerWindow);
            _ownerWindow = 0;
        }

        if (_classAtom != 0)
        {
            Win32PanicInterop.UnregisterClassW(_windowClassName, _moduleHandle);
            _classAtom = 0;
        }
    }

    private nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == Win32PanicInterop.WmHotkey && (int)wParam == HotkeyId)
        {
            // A throwing handler must never escape into the native window procedure: the OS is the
            // caller here, and an exception crossing that frame ends the process — which for THIS
            // window would mean the emergency stop killing the app it was pressed to make safe.
            try
            {
                Pressed?.Invoke();
            }
            catch (Exception ex)
            {
                // Caught, and no longer SILENT. This catch used to say "this class holds no log
                // sink" and swallow — which made a failure on the emergency path the one failure in
                // the application nobody could ever hear, while the same failure on the STOP button
                // would at least reach a handler. The sink is optional and the catch is unchanged;
                // only the silence is gone.
                _log?.Invoke($"panic: the {Gesture} handler threw and the press did nothing "
                    + $"({ex.GetType().Name}: {ex.Message})");
            }

            return 0;
        }

        // Dispose's request, arriving on the one thread allowed to answer it. The chord is given
        // back BEFORE the window goes, so the release is this code's act rather than a side effect
        // of the window dying — a hotkey nobody released is Ctrl+Alt+Esc taken away from every
        // other application on the machine.
        if (message == Win32PanicInterop.WmClose)
        {
            if (_registered)
            {
                Win32PanicInterop.UnregisterHotKey(window, HotkeyId);
                _registered = false;
            }

            Win32PanicInterop.DestroyWindow(window);
            _ownerWindow = 0;
            return 0;
        }

        // The panic thread's own loop ends here and nowhere else.
        if (message == Win32PanicInterop.WmDestroy)
        {
            Win32PanicInterop.PostQuitMessage(0);
            return 0;
        }

        return Win32PanicInterop.DefWindowProcW(window, message, wParam, lParam);
    }

    private static CapabilityState.Available Available(string detail) => new(detail);

    private static CapabilityState.Unavailable Unavailable(string code, string detail) =>
        new(new CapabilityReason(code, detail));
}
