using System.Runtime.InteropServices;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Tray;

/// <summary>
/// The handles a test (or a diagnostic surface) needs to ask the OPERATING SYSTEM whether this
/// presence's icon exists, without asking the presence itself. Exposed on purpose: a tray
/// capability that can only be interrogated through its own claim is exactly the capability
/// nobody can verify.
/// </summary>
/// <param name="OwnerWindow">The hidden top-level window the notification area sends callbacks to.</param>
/// <param name="IconId">The icon's id within <paramref name="OwnerWindow"/>.</param>
/// <param name="CallbackMessage">The private window message the shell posts on user interaction.</param>
public readonly record struct TrayNativeHandles(nint OwnerWindow, uint IconId, uint CallbackMessage);

/// <summary>
/// The Windows tray backend: a real icon in the notification area, via <c>Shell_NotifyIconW</c>.
///
/// <para><b>Why not <c>Avalonia.Controls.TrayIcon</c>.</b> Avalonia 12.1.1 does ship tray
/// backends (<c>Avalonia.Win32.TrayIconImpl</c>, <c>Avalonia.FreeDesktop.DBusTrayIconImpl</c>,
/// <c>Avalonia.X11.XEmbedTrayIconImpl</c> — all internal), but its public surface cannot answer
/// "is an icon placed". <c>ITrayIconImpl.SetIsVisible</c> returns <c>void</c>; the
/// <c>TrayIcon(ITrayIconImpl)</c> constructor is non-public so the impl is unreachable; and
/// with NO windowing platform registered at all, <c>new TrayIcon()</c> constructs, accepts
/// <c>IsVisible = true</c>, reads it back <c>true</c>, and disposes cleanly while no icon exists
/// anywhere (measured against the 12.1.1 assemblies — SP-093 record.md Step 2). A capability
/// built on that could not tell a placed icon from a missing one, which is the entire failure
/// this class exists to make impossible. So the backend owns the same call Avalonia's own
/// Win32 impl makes and checks its result.</para>
///
/// <para><b>The rule this class obeys.</b> Nothing here returns <see cref="CapabilityState.Available"/>
/// unless the shell was asked and answered yes, and then asked AGAIN and confirmed the icon
/// exists. <see cref="Remove"/> is symmetric: it confirms the icon is GONE before reporting
/// success. Failure is a typed <see cref="CapabilityState.Unavailable"/> carrying the failing
/// call and the Win32 last-error.</para>
///
/// <para><b>Thread affinity.</b> The owner window belongs to the thread that first called
/// <see cref="Place"/>. Call <see cref="Dispose"/> from that same thread, and expect
/// <see cref="Activated"/> only while that thread pumps its message queue (in the app that is
/// the UI thread, whose Win32 loop Avalonia already runs).</para>
/// </summary>
public sealed class Win32TrayPresence : ITrayPresence
{
    private readonly string _windowClassName = "CcpClientTrayOwner." + Guid.NewGuid().ToString("N");
    private readonly Win32TrayInterop.WndProc _windowProc;

    private nint _ownerWindow;
    private nint _moduleHandle;
    private ushort _classAtom;
    private nint _icon;
    private bool _ownsIconHandle;
    private string _iconSource = "none";
    private uint _taskbarCreatedMessage;
    private TrayIconRequest? _current;
    private bool _placed;
    private bool _disposed;
    private int _ownerThreadId;

    public Win32TrayPresence()
    {
        // Rooted for the lifetime of this instance: the shell calls this pointer back from
        // native code, and a collected delegate is an access violation, not an exception.
        _windowProc = WindowProc;
    }

    public event EventHandler? Activated;

    public bool IsPlaced => _placed;

    /// <summary>Zero until an owner window exists. The handles an out-of-band prober needs.</summary>
    public TrayNativeHandles NativeHandles =>
        new(_ownerWindow, Win32TrayInterop.TrayIconId, Win32TrayInterop.TrayCallbackMessage);

    /// <summary>
    /// Set only when teardown could not complete (wrong thread, or <c>DestroyWindow</c> refused).
    /// Null after a clean <see cref="Dispose"/>. <c>Dispose</c> must not throw, and it must not
    /// pretend either — this is where the difference is recorded.
    /// </summary>
    public string? TeardownDiagnostic { get; private set; }

    public CapabilityState Place(TrayIconRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_disposed)
        {
            return Unavailable(TrayReasonCodes.TrayPresenceDisposed,
                "this tray presence was disposed; its owner window and icon are gone and it will never place another");
        }

        // Backend SELECTION by platform is permitted; capability AVAILABILITY by platform is not
        // (runtime-capability-contract §2 rule 2). This branch only refuses — Available below is
        // earned by the exercised shell call, never by this check.
        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(TrayReasonCodes.TrayMechanismAbsent,
                $"Win32TrayPresence drives Shell_NotifyIcon, which is a Windows mechanism; this process is on "
                + $"{RuntimeInformation.OSDescription} and nothing was attempted");
        }

        var owner = EnsureOwnerWindow(out var ownerFailure);
        if (owner == 0)
        {
            return Unavailable(TrayReasonCodes.TrayOwnerWindowFailed, ownerFailure);
        }

        EnsureIcon();
        _current = request;

        var adding = !_placed;
        var data = BuildData(request);
        if (!Win32TrayInterop.Shell_NotifyIconW(adding ? Win32TrayInterop.NimAdd : Win32TrayInterop.NimModify, ref data))
        {
            var error = Marshal.GetLastWin32Error();
            _placed = false;
            return Unavailable(TrayReasonCodes.TrayMechanismRefused,
                $"Shell_NotifyIcon({(adding ? "NIM_ADD" : "NIM_MODIFY")}) returned FALSE for icon uID="
                + $"{Win32TrayInterop.TrayIconId} on owner window 0x{owner:X} (last-error {error}); no icon is placed. "
                + "The usual cause is a session with no notification area (session 0 / no shell / Explorer restarting)");
        }

        // The claim is not the return value. Ask the shell again: NIM_MODIFY succeeds only for
        // an (hWnd, uID) pair the notification area actually holds.
        if (!ConfirmIconExists(request))
        {
            var error = Marshal.GetLastWin32Error();
            var undo = BuildData(request);
            Win32TrayInterop.Shell_NotifyIconW(Win32TrayInterop.NimDelete, ref undo);
            _placed = false;
            return Unavailable(TrayReasonCodes.TrayMechanismRefused,
                $"Shell_NotifyIcon(NIM_ADD) reported success but the confirming NIM_MODIFY round-trip failed "
                + $"(last-error {error}) — the shell does not hold icon uID={Win32TrayInterop.TrayIconId} on owner "
                + $"window 0x{owner:X}, so no placement is claimed");
        }

        _placed = true;
        return new CapabilityState.Available(
            $"Shell_NotifyIcon(NIM_ADD) accepted icon uID={Win32TrayInterop.TrayIconId} on owner window 0x{owner:X} "
            + $"and a NIM_MODIFY round-trip confirmed the notification area holds it; tooltip \"{request.ToolTip}\", "
            + $"icon source = {_iconSource}. Confirms PLACEMENT only — that a user can see and click it is a headed claim");
    }

    public CapabilityState Remove()
    {
        if (_disposed)
        {
            return Unavailable(TrayReasonCodes.TrayPresenceDisposed,
                "this tray presence was disposed; its icon was already removed by teardown");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(TrayReasonCodes.TrayMechanismAbsent,
                "Win32TrayPresence drives Shell_NotifyIcon, which is a Windows mechanism; nothing was ever placed");
        }

        if (!_placed || _current is null)
        {
            return Unavailable(TrayReasonCodes.TrayNothingPlaced,
                "no icon is placed by this presence, so there is nothing to remove and nothing succeeded");
        }

        var data = BuildData(_current);
        if (!Win32TrayInterop.Shell_NotifyIconW(Win32TrayInterop.NimDelete, ref data))
        {
            var error = Marshal.GetLastWin32Error();
            return Unavailable(TrayReasonCodes.TrayMechanismRefused,
                $"Shell_NotifyIcon(NIM_DELETE) returned FALSE for icon uID={Win32TrayInterop.TrayIconId} "
                + $"(last-error {error}); the icon may still be in the notification area, so placement is still claimed");
        }

        // Symmetric to Place: confirm the ABSENCE. NIM_MODIFY must now fail.
        if (ConfirmIconExists(_current))
        {
            return Unavailable(TrayReasonCodes.TrayMechanismRefused,
                $"Shell_NotifyIcon(NIM_DELETE) reported success but a NIM_MODIFY round-trip still finds icon "
                + $"uID={Win32TrayInterop.TrayIconId} — the icon is still placed and removal is not claimed");
        }

        _placed = false;
        return new CapabilityState.Available(
            $"Shell_NotifyIcon(NIM_DELETE) accepted and a NIM_MODIFY round-trip confirms the notification area no "
            + $"longer holds icon uID={Win32TrayInterop.TrayIconId}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (_placed && _current is not null)
        {
            var data = BuildData(_current);
            Win32TrayInterop.Shell_NotifyIconW(Win32TrayInterop.NimDelete, ref data);
            _placed = false;
        }

        if (_ownsIconHandle && _icon != 0)
        {
            Win32TrayInterop.DestroyIcon(_icon);
        }

        _icon = 0;
        _ownsIconHandle = false;

        if (_ownerWindow != 0)
        {
            // DestroyWindow is thread-affine: only the creating thread may call it. Saying so is
            // the point — a window this presence leaks would keep an invisible top-level window
            // alive for the process lifetime, and silence would hide it.
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                TeardownDiagnostic =
                    $"Dispose ran on managed thread {Environment.CurrentManagedThreadId} but the owner window was "
                    + $"created on thread {_ownerThreadId}; DestroyWindow is thread-affine, so window 0x{_ownerWindow:X} "
                    + "was NOT destroyed";
                return;
            }

            if (!Win32TrayInterop.DestroyWindow(_ownerWindow))
            {
                TeardownDiagnostic = $"DestroyWindow(0x{_ownerWindow:X}) returned FALSE (last-error {Marshal.GetLastWin32Error()})";
                return;
            }

            _ownerWindow = 0;
        }

        if (_classAtom != 0)
        {
            Win32TrayInterop.UnregisterClassW(_windowClassName, _moduleHandle);
            _classAtom = 0;
        }
    }

    private nint EnsureOwnerWindow(out string failure)
    {
        failure = string.Empty;
        if (_ownerWindow != 0)
        {
            return _ownerWindow;
        }

        _moduleHandle = Win32TrayInterop.GetModuleHandleW(null);
        var cls = new Win32TrayInterop.WndClassExW
        {
            cbSize = (uint)Marshal.SizeOf<Win32TrayInterop.WndClassExW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            hInstance = _moduleHandle,
            lpszClassName = _windowClassName,
        };

        _classAtom = Win32TrayInterop.RegisterClassExW(ref cls);
        if (_classAtom == 0)
        {
            failure = $"RegisterClassEx('{_windowClassName}') failed (last-error {Marshal.GetLastWin32Error()}); "
                + "the notification area's click callbacks need an owner window and none could be created";
            return 0;
        }

        _ownerWindow = Win32TrayInterop.CreateWindowExW(
            Win32TrayInterop.WsExToolwindow, _windowClassName, "CCP tray owner", Win32TrayInterop.WsPopup,
            0, 0, 0, 0, 0, 0, _moduleHandle, 0);
        if (_ownerWindow == 0)
        {
            failure = $"CreateWindowEx for the hidden tray owner failed (last-error {Marshal.GetLastWin32Error()})";
            Win32TrayInterop.UnregisterClassW(_windowClassName, _moduleHandle);
            _classAtom = 0;
            return 0;
        }

        _ownerThreadId = Environment.CurrentManagedThreadId;
        // Explorer restarting destroys every notification-area icon and broadcasts this to all
        // TOP-LEVEL windows. Registering it is why the owner is a WS_POPUP and not HWND_MESSAGE.
        _taskbarCreatedMessage = Win32TrayInterop.RegisterWindowMessageW("TaskbarCreated");
        return _ownerWindow;
    }

    private void EnsureIcon()
    {
        if (_icon != 0)
        {
            return;
        }

        // WPF's fallback chain in the same order (TrayIconService.cs:67-91): the product's own
        // icon first, the stock application icon if the image carries none.
        var image = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(image))
        {
            if (Win32TrayInterop.ExtractIconExW(image, 0, out var large, out var small, 1) > 0 && (small != 0 || large != 0))
            {
                if (small != 0)
                {
                    _icon = small;
                    if (large != 0)
                    {
                        Win32TrayInterop.DestroyIcon(large);
                    }
                }
                else
                {
                    _icon = large;
                }

                _ownsIconHandle = true;
                _iconSource = $"process image '{image}'";
                return;
            }
        }

        // Shared system icon: never destroyed (TrayIconService.cs:91 falls back the same way).
        _icon = Win32TrayInterop.LoadIconW(0, Win32TrayInterop.IdiApplication);
        _ownsIconHandle = false;
        _iconSource = "system default IDI_APPLICATION (the process image carries no icon resource)";
    }

    private Win32TrayInterop.NotifyIconDataW BuildData(TrayIconRequest request) => new()
    {
        cbSize = (uint)Marshal.SizeOf<Win32TrayInterop.NotifyIconDataW>(),
        hWnd = _ownerWindow,
        uID = Win32TrayInterop.TrayIconId,
        uFlags = Win32TrayInterop.NifMessage | Win32TrayInterop.NifIcon | Win32TrayInterop.NifTip,
        uCallbackMessage = Win32TrayInterop.TrayCallbackMessage,
        hIcon = _icon,
        szTip = request.ToolTip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    /// <summary>
    /// Asks the shell whether it holds this presence's icon. NIM_MODIFY succeeds only for an
    /// (hWnd, uID) pair the notification area really has — measured on Windows 11 with both
    /// negative controls (a never-added id and a deleted id both answer FALSE).
    /// </summary>
    private bool ConfirmIconExists(TrayIconRequest request)
    {
        var probe = BuildData(request);
        return Win32TrayInterop.Shell_NotifyIconW(Win32TrayInterop.NimModify, ref probe);
    }

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == Win32TrayInterop.TrayCallbackMessage)
        {
            // No NIM_SETVERSION is issued, so the shell uses the original convention: lParam's
            // low word is the mouse message. Left-click AND double-click both restore, matching
            // WPF (TrayIconService.cs:112-119 — single-click was added because "clicking the tray
            // icon does nothing" reads as the app being gone).
            var mouse = (uint)(lParam & 0xFFFF);
            if (mouse is Win32TrayInterop.WmLbuttonup or Win32TrayInterop.WmLbuttondblclk)
            {
                Activated?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage && _placed && _current is not null)
        {
            // Explorer restarted and took the icon with it. Re-add, and stop claiming placement
            // if the re-add does not confirm.
            var data = BuildData(_current);
            _placed = Win32TrayInterop.Shell_NotifyIconW(Win32TrayInterop.NimAdd, ref data)
                && ConfirmIconExists(_current);
        }

        return Win32TrayInterop.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static CapabilityState Unavailable(string code, string detail) =>
        new CapabilityState.Unavailable(new CapabilityReason(code, detail));
}
