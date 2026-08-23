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
/// <param name="Menu">The <c>HMENU</c> the context menu lives in, or zero before one is set. Exposed
/// for the same reason as the rest: a menu that can only be interrogated through the object that
/// built it is a menu nobody can verify. With this handle a test asks USER32 directly what entries
/// exist.</param>
public readonly record struct TrayNativeHandles(nint OwnerWindow, uint IconId, uint CallbackMessage, nint Menu);

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
/// anywhere (measured against the 12.1.1 assemblies — record.md Step 2). A capability
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

    private readonly Dictionary<uint, TrayMenuItem> _menuCommands = new();

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
    private nint _menu;

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
        new(_ownerWindow, Win32TrayInterop.TrayIconId, Win32TrayInterop.TrayCallbackMessage, _menu);

    /// <summary>
    /// THE ONE UNINSTRUMENTABLE CALL, behind a seam so it is the only thing a test has to stand in
    /// for. The product default is the real <c>TrackPopupMenu</c>: it runs a modal message loop
    /// until the user picks an entry (returning its command id) or dismisses the menu (returning 0),
    /// and no headless run can drive a pointer into it.
    ///
    /// <para>Everything on both sides of this seam is exercised for real by the suite: the shell's
    /// own right-click notification arriving at the real window proc, the real OS-held
    /// <c>HMENU</c> this is handed, and <see cref="InvokeMenuCommand"/> turning the returned id
    /// into the entry's action. What the seam isolates — and what stays a headed claim — is the
    /// modal loop itself and a human's hand on the mouse.</para>
    ///
    /// <para>Arguments: the menu handle, the screen x and y, and the owner window.</para>
    /// </summary>
    public Func<nint, int, int, nint, uint> MenuTracker { get; set; } = static (menu, x, y, owner) =>
        Win32TrayInterop.TrackPopupMenu(
            menu, Win32TrayInterop.TpmReturncmd | Win32TrayInterop.TpmRightbutton, x, y, 0, owner, 0);

    /// <summary>How many times a right-click reached the tracker. Counts the GESTURE arriving, not
    /// a menu being seen; a test reads it to prove the shell's notification really got that far.</summary>
    public int MenuTrackerInvocations { get; private set; }

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

    public CapabilityState SetMenu(TrayMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        if (_disposed)
        {
            return Unavailable(TrayReasonCodes.TrayPresenceDisposed,
                "this tray presence was disposed; its menu was destroyed by teardown and it will never build another");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(TrayReasonCodes.TrayMechanismAbsent,
                "Win32TrayPresence builds an HMENU through USER32, which is a Windows mechanism; this process is on "
                + $"{RuntimeInformation.OSDescription} and nothing was attempted");
        }

        var handle = Win32TrayInterop.CreatePopupMenu();
        if (handle == 0)
        {
            return Unavailable(TrayReasonCodes.TrayMenuBuildFailed,
                $"CreatePopupMenu returned NULL (last-error {Marshal.GetLastWin32Error()}); no menu exists, so a "
                + "right-click on the icon would lead nowhere");
        }

        // Command ids start at 1: TrackPopupMenu(TPM_RETURNCMD) reports a DISMISSED menu as 0, so
        // an entry with id 0 would be indistinguishable from the user pressing Escape.
        var commands = new Dictionary<uint, TrayMenuItem>();
        uint nextId = 1;
        foreach (var item in menu.Items)
        {
            bool appended;
            if (item.IsSeparator)
            {
                appended = Win32TrayInterop.AppendMenuW(handle, Win32TrayInterop.MfSeparator, 0, null);
            }
            else
            {
                var id = nextId++;
                appended = Win32TrayInterop.AppendMenuW(handle, Win32TrayInterop.MfString, id, item.Label);
                if (appended)
                {
                    commands[id] = item;
                }
            }

            if (!appended)
            {
                var error = Marshal.GetLastWin32Error();
                Win32TrayInterop.DestroyMenu(handle);
                return Unavailable(TrayReasonCodes.TrayMenuBuildFailed,
                    $"AppendMenu failed for entry '{(item.IsSeparator ? "<separator>" : item.Id)}' (last-error "
                    + $"{error}); the partial menu was destroyed rather than installed, because a menu missing an "
                    + "entry is worse than no menu — the user cannot see which one is gone");
            }
        }

        // The claim is not that the appends returned. Ask USER32 back for the menu it now holds and
        // compare it to what was asked for, exactly as Place re-asks the shell about its icon.
        var mismatch = DescribeMenuMismatch(handle, menu, commands);
        if (mismatch is not null)
        {
            Win32TrayInterop.DestroyMenu(handle);
            return Unavailable(TrayReasonCodes.TrayMenuBuildFailed, mismatch);
        }

        if (_menu != 0)
        {
            Win32TrayInterop.DestroyMenu(_menu);
        }

        _menu = handle;
        _menuCommands.Clear();
        foreach (var (id, item) in commands)
        {
            _menuCommands[id] = item;
        }

        return new CapabilityState.Available(
            $"CreatePopupMenu built {menu.Items.Count} entries on HMENU 0x{handle:X} and a "
            + "GetMenuItemCount/GetMenuItemID/GetMenuString read-back confirms USER32 holds exactly those entries, in "
            + $"that order, with the restore entry '{menu.RestoreItem.Label}' among them. Confirms the MENU EXISTS; "
            + "that a user sees it needs TrackPopupMenu's modal loop and a real right-click, which is a headed claim");
    }

    public CapabilityState ShowNotification(TrayNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (_disposed)
        {
            return Unavailable(TrayReasonCodes.TrayPresenceDisposed,
                "this tray presence was disposed; there is no icon left to put a balloon on");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(TrayReasonCodes.TrayMechanismAbsent,
                "Shell_NotifyIcon balloons are a Windows mechanism; nothing was ever placed and nothing was attempted");
        }

        if (!_placed || _current is null)
        {
            return Unavailable(TrayReasonCodes.TrayNotificationWithoutIcon,
                "no icon is placed by this presence, so there is nothing for the shell to attach a balloon to");
        }

        var data = BuildData(_current);
        data.uFlags |= Win32TrayInterop.NifInfo;
        data.szInfo = notification.Message;
        data.szInfoTitle = notification.Title;
        data.dwInfoFlags = Win32TrayInterop.NiifInfo;
        data.uTimeoutOrVersion = (uint)Math.Clamp(notification.Timeout.TotalMilliseconds, 0, uint.MaxValue);

        if (!Win32TrayInterop.Shell_NotifyIconW(Win32TrayInterop.NimModify, ref data))
        {
            return Unavailable(TrayReasonCodes.TrayMechanismRefused,
                $"Shell_NotifyIcon(NIM_MODIFY|NIF_INFO) returned FALSE for icon uID={Win32TrayInterop.TrayIconId} "
                + $"(last-error {Marshal.GetLastWin32Error()}); no balloon was queued");
        }

        return new CapabilityState.Available(
            $"the shell accepted a balloon request (title \"{notification.Title}\", "
            + $"{notification.Timeout.TotalMilliseconds:0} ms requested) for icon uID={Win32TrayInterop.TrayIconId}, "
            + "which it confirms holding. Confirms ACCEPTANCE only: Windows suppresses notifications under Focus "
            + "Assist, quiet hours, a full-screen app and the per-app switch, and reports none of that back, so a "
            + "balloon actually appearing is a headed claim");
    }

    /// <summary>
    /// Turns a menu command id into the entry's action — the SAME method
    /// <see cref="MenuTracker"/>'s return value feeds. Public so the dispatch half can be exercised
    /// without a modal loop. Returns false for an id this menu does not carry (0 is the tracker's
    /// "the user dismissed it" answer and is never a command).
    /// </summary>
    public bool InvokeMenuCommand(uint commandId)
    {
        if (commandId == 0 || !_menuCommands.TryGetValue(commandId, out var item))
        {
            return false;
        }

        item.Invoke();
        return true;
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

        if (_menu != 0)
        {
            Win32TrayInterop.DestroyMenu(_menu);
            _menu = 0;
            _menuCommands.Clear();
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

    /// <summary>
    /// Asks USER32 what it actually holds and compares it, entry by entry, to what was asked for.
    /// Null when they match; otherwise the difference, in words, for the refusal's detail.
    ///
    /// <para>Three independent questions per entry — the count, the id, the label — because a menu
    /// can go wrong in three separable ways and a single "did it work" answer would conflate them.
    /// The separator is checked by its OS-reported id of 0 and empty string, which is how a
    /// separator differs from a command entry in USER32's own bookkeeping.</para>
    /// </summary>
    private static string? DescribeMenuMismatch(nint handle, TrayMenu menu, Dictionary<uint, TrayMenuItem> commands)
    {
        var count = Win32TrayInterop.GetMenuItemCount(handle);
        if (count != menu.Items.Count)
        {
            return $"the menu was built with {menu.Items.Count} entries but GetMenuItemCount reports {count} "
                + $"(last-error {Marshal.GetLastWin32Error()}); the OS does not hold the menu that was asked for";
        }

        var buffer = new System.Text.StringBuilder(512);
        for (var index = 0; index < count; index++)
        {
            var expected = menu.Items[index];
            var id = Win32TrayInterop.GetMenuItemID(handle, index);
            buffer.Clear();
            var copied = Win32TrayInterop.GetMenuStringW(
                handle, (uint)index, buffer, buffer.Capacity, Win32TrayInterop.MfByposition);
            var label = copied > 0 ? buffer.ToString() : string.Empty;

            if (expected.IsSeparator)
            {
                if (id != 0 || label.Length != 0)
                {
                    return $"entry {index} was appended as a separator but the OS reports id {id} and label "
                        + $"\"{label}\"";
                }

                continue;
            }

            if (id == 0 || !commands.TryGetValue(id, out var mapped) || !ReferenceEquals(mapped, expected))
            {
                return $"entry {index} ('{expected.Id}') was appended as a command but the OS reports id {id}, which "
                    + "is not the id this menu's command map holds for it — a click on it would run the wrong action "
                    + "or none";
            }

            if (!string.Equals(label, expected.Label, StringComparison.Ordinal))
            {
                return $"entry {index} ('{expected.Id}') was appended with label \"{expected.Label}\" but the OS "
                    + $"reports \"{label}\"";
            }
        }

        return null;
    }

    /// <summary>
    /// The right-click gesture. Foreground first, then the modal tracker, then the documented
    /// WM_NULL that lets a dismissed menu notice it lost the foreground (KB135788) — the same
    /// sequence WinForms' NotifyIcon performs internally, done here because this backend owns its
    /// own window proc.
    /// </summary>
    private void ShowContextMenu()
    {
        if (_menu == 0 || _menuCommands.Count == 0)
        {
            // No menu installed. Doing nothing is right: an empty popup at the cursor tells the
            // user less than no popup, and there is no state here worth throwing over.
            return;
        }

        // GUARDED ON VISIBILITY, and the guard is not defensive tidying.
        //
        // CORRECTED 2026-08-24, because the first version of this comment named the wrong cause,
        // and a wrong cause is worse than no comment. It claimed the call "cannot succeed" on a
        // hidden window and that the failed attempt costs this process the topmost band. Both
        // halves are false, and both were measured: SetForegroundWindow on a HIDDEN window RETURNS
        // TRUE, and on its own it costs nothing - the band is still reachable after three of them.
        //
        // What it actually does is worse than failing. It SUCCEEDS, and the foreground it moves is
        // the user's: GetForegroundWindow() afterwards is this owner window, which has no pixels
        // on screen. The user's next keystroke goes to a window they cannot see, with nothing to
        // explain where their typing went - and this runs on an ordinary right-click of the tray
        // icon. That is the defect, it is user-visible, and it is why the guard is here.
        //
        // Pinned by TrayCapabilityTests.TheRightClickGesture_NeverParksTheForegroundOnTheHidden-
        // OwnerWindow, which asserts the CAUSE (the foreground is not this window) rather than a
        // process-wide side effect. Asserting the return value would prove nothing: it is TRUE in
        // both the guarded and the unguarded case.
        if (Win32TrayInterop.IsWindowVisible(_ownerWindow))
        {
            Win32TrayInterop.SetForegroundWindow(_ownerWindow);
        }
        var point = Win32TrayInterop.GetCursorPos(out var cursor) ? cursor : default;

        MenuTrackerInvocations++;
        var command = MenuTracker(_menu, point.X, point.Y, _ownerWindow);
        Win32TrayInterop.PostMessageW(_ownerWindow, Win32TrayInterop.WmNull, 0, 0);

        // 0 means the user dismissed the menu without choosing, which is not a failure.
        InvokeMenuCommand(command);
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
            else if (mouse == Win32TrayInterop.WmRbuttonup)
            {
                // WPF gets this for free from ContextMenuStrip (TrayIconService.cs:110); a backend
                // that owns its own window has to route it.
                ShowContextMenu();
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
