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
/// <para><b>Thread affinity.</b> The owner window belongs to the thread that called
/// <see cref="Arm"/> — in the app, the UI thread, whose Win32 message loop Avalonia already pumps.
/// <see cref="Pressed"/> is raised on that thread, which is what lets a handler touch windows
/// directly. <see cref="Dispose"/> must run on the same thread.</para>
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

    private readonly string _windowClassName = "CcpClientPanicKey." + Guid.NewGuid().ToString("N");
    private readonly Win32PanicInterop.WndProc _windowProc;

    private nint _ownerWindow;
    private nint _moduleHandle;
    private ushort _classAtom;
    private bool _registered;
    private bool _disposed;

    public Win32PanicKey()
    {
        // Rooted for this instance's lifetime: the OS calls this pointer back from native code,
        // and a collected delegate is an access violation rather than an exception.
        _windowProc = WindowProc;
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Pressed = null;

        if (_registered && OperatingSystem.IsWindows() && _ownerWindow != 0)
        {
            Win32PanicInterop.UnregisterHotKey(_ownerWindow, HotkeyId);
        }

        _registered = false;
        DestroyOwnerWindow();
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
            catch (Exception)
            {
                // Deliberately swallowed and deliberately silent: this class holds no log sink, and
                // the handler is the party that knows what its own failure means.
            }

            return 0;
        }

        return Win32PanicInterop.DefWindowProcW(window, message, wParam, lParam);
    }

    private static CapabilityState.Available Available(string detail) => new(detail);

    private static CapabilityState.Unavailable Unavailable(string code, string detail) =>
        new(new CapabilityReason(code, detail));
}
