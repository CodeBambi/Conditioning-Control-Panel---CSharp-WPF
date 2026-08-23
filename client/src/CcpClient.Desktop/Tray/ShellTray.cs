using Avalonia.Controls;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Tray;

/// <summary>
/// The shell's duck/restore, and the ONE consumer of <see cref="ITrayPresence"/> anywhere
/// in the port. WPF's counterpart is <c>Services/Notifications/TrayIconService.cs</c> plus the two
/// <c>MainWindow</c> entry points it is driven through
/// (<c>MainWindow/MainWindow.RemoteControl.cs:1517</c>, <c>:1554</c>).
///
/// <para><b>THE THING THIS CLASS DOES NOT DO, AND WHY.</b> WPF's tuck is
/// <c>_mainWindow.Hide()</c> plus a visible icon (<c>TrayIconService.cs:145-148</c>), driven from
/// <c>Services/Chaos/DtrhHostService.cs:156</c> when the hole opens and undone at <c>:998</c>. This
/// class NEVER hides the shell. The reason is measured, not stylistic, and it is pinned by
/// <c>ShellTrayHeadlessTests.AvaloniaHidesOwnedWindowsWithTheirOwner_WhichIsWhyTheShellIsNeverHidden</c>:
/// <b>in Avalonia 12.1.1 <c>Window.Hide()</c> on an owner also hides every window owned by it, and
/// the owner's later <c>Show()</c> does not bring them back.</b> The DTRH host window is owned by
/// the shell (<c>Features/Dtrh/DtrhLaunchCoordinator.cs:167</c>, <c>window.Show(_owner)</c>), so a
/// WPF-shaped tuck here would hide the descent the tuck exists to make room for.</para>
///
/// <para>WPF survives the same call because Win32 does not propagate a hide to owned windows — and
/// WPF needs that ownership, in its own words at the construction site
/// (<c>DtrhHostService.cs:129-132</c>, <c>OwnedByMainWindow = true</c>): <i>"Glue the descent above
/// MainWindow via native ownership: main gets raised by plenty of things we don't control (avatar
/// barks, a video window closing, a tray restore) and used to land on top of the game."</i> In
/// Avalonia the two properties are mutually exclusive at 12.1.1, and keeping the descent owned is
/// the load-bearing one. Full reasoning at wpf-surface-reachability.md §12 D35 @ 7527243e7.</para>
///
/// <para><b>What ships instead.</b> The shell is plain-minimized — the taskbar button never leaves
/// — AND a shell-confirmed icon carrying the full menu is placed for exactly the interval WPF's
/// icon is up. So the user has three ways back where WPF has two, and no way to be stranded,
/// because nothing is ever hidden.</para>
///
/// <para><b>Thread affinity.</b> Everything here runs on the UI thread: <see cref="Duck"/> is
/// called from the coordinator's <c>HostOpened</c> inside a UI-thread invoke, the presence's
/// <see cref="ITrayPresence.Activated"/> is delivered on the thread that called
/// <see cref="ITrayPresence.Place"/> (the interface's own contract), and
/// <see cref="Dispose"/> must run there too because the backend's owner window is thread-affine.</para>
/// </summary>
public sealed class ShellTray : IDisposable
{
    private readonly Window _shell;
    private readonly Action<string> _log;
    private readonly Func<ITrayPresence> _presenceFactory;

    private ITrayPresence? _presence;
    private bool _iconPlaced;
    private bool _ducked;
    private bool _balloonAsked;
    private WindowState? _stateBeforeDuck;
    private bool _disposed;

    /// <param name="shell">The window that ducks. Never hidden by this class.</param>
    /// <param name="log">The host's content-free diagnostic sink.</param>
    /// <param name="wakeCompanion">WPF's "Wake Bambi Up!" entry (<c>TrayIconService.cs:102-105</c>
    /// -> <c>MainWindow/MainWindow.xaml.cs:348-351</c> -> <c>MainWindow.Companion.cs:265</c>), which
    /// wpf-surface-reachability.md §5 @ 7527243e7 records as one of the three ways a user reaches the companion.</param>
    /// <param name="exit">WPF's "Exit" entry (<c>TrayIconService.cs:109-111</c> ->
    /// <c>MainWindow/MainWindow.xaml.cs:323-343</c>, ending in <c>Application.Current.Shutdown()</c>).</param>
    /// <param name="presenceFactory">The backend. Defaults to the platform's own — Windows gets the
    /// real one, every other platform gets the typed refusal.</param>
    public ShellTray(
        Window shell,
        Action<string> log,
        Action wakeCompanion,
        Action exit,
        Func<ITrayPresence>? presenceFactory = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(wakeCompanion);
        ArgumentNullException.ThrowIfNull(exit);

        _shell = shell;
        _log = log;
        _presenceFactory = presenceFactory ?? TrayPresenceFactory.Create;

        // WPF builds its menu once, at tray construction, and never rebuilds it
        // (TrayIconService.cs:95-110). Four entries, in this order.
        Menu = new TrayMenu(
        [
            // ":98" verbatim.
            TrayMenuItem.Restore("show", "Show Dashboard", Restore),

            // ":102-103". WPF picks "Wake Bambi Up!" when App.Mods?.IsBambiMode is true and
            // "Wake Up!" otherwise. The port has no mod system at all, so it is permanently the
            // second branch — this is WPF's own string for the state the port is in, not a
            // paraphrase of the first. Recorded as §12 D36.
            TrayMenuItem.Command("wake", "Wake Up!", wakeCompanion),

            // ":107".
            TrayMenuItem.Separator(),

            // ":109" verbatim.
            TrayMenuItem.Command("exit", "Exit", exit),
        ]);
    }

    /// <summary>The menu installed on the icon. Built once, in the constructor, like WPF's.</summary>
    public TrayMenu Menu { get; }

    /// <summary>True between a <see cref="Duck"/> that took effect and its <see cref="Restore"/>.</summary>
    public bool IsDucked => _ducked;

    /// <summary>True only while this class's icon is confirmed present in the notification area.</summary>
    public bool IsIconPlaced => _iconPlaced;

    /// <summary>The shell's window state before the duck, so a maximized shell comes back maximized.</summary>
    public WindowState? StateBeforeDuck => _stateBeforeDuck;

    /// <summary>The backend, once one has been built (first duck). Null before that: a user who
    /// never opens the hole never gets a native window created on their behalf.</summary>
    public ITrayPresence? Presence => _presence;

    /// <summary>What the last <see cref="Duck"/> did about the icon, in the words that went to the
    /// diagnostic sink. Read by tests so the Unavailable branch is checked by its reason and not
    /// only by its effect.</summary>
    public string? LastIconDiagnostic { get; private set; }

    /// <summary>How many balloons this process has ASKED the shell for. WPF's latch allows exactly
    /// one per process (<c>TrayIconService.cs:23,150-157</c>), so this never exceeds 1.</summary>
    public int BalloonsRequested { get; private set; }

    /// <summary>
    /// The shell gets out of the way while another window owns the screen.
    ///
    /// <para>Order matters and is the opposite of WPF's for one reason. WPF hides the window and
    /// then shows the icon (<c>TrayIconService.cs:145-146</c>); if its second step failed the user
    /// would be stranded. Here the window is minimized FIRST, which is safe on its own because the
    /// taskbar button survives it, and the icon is a strict addition afterwards that may fail
    /// without consequence.</para>
    /// </summary>
    public void Duck()
    {
        if (_disposed || _ducked)
        {
            return;
        }

        // Already minimized: nothing to duck, and nothing to undo later. The landed shape at
        // Features/Intake/IntakeHostWindow.axaml.cs:125-128, kept.
        if (_shell.WindowState == WindowState.Minimized)
        {
            return;
        }

        _stateBeforeDuck = _shell.WindowState;
        _ducked = true;
        _shell.WindowState = WindowState.Minimized;
        _log($"tray: shell minimized while another window owns the screen (prior state {_stateBeforeDuck}; "
            + "NOT a tray tuck — the taskbar button stays)");

        PlaceIcon();
    }

    /// <summary>
    /// The single restore funnel: the flow ending, a left-click on the icon, and the menu's "Show
    /// Dashboard" all arrive here. No-ops when nothing was ducked.
    /// </summary>
    public void Restore()
    {
        if (!_ducked)
        {
            return;
        }

        var prior = _stateBeforeDuck;
        _ducked = false;
        _stateBeforeDuck = null;

        // WPF takes the icon down BEFORE showing the window, and says why: hiding the icon is what
        // dismisses a balloon that is still up (TrayIconService.cs:165-169). Remove() is the port's
        // equivalent, and it runs first for the same reason.
        RemoveIcon();

        if (_shell.WindowState == WindowState.Minimized)
        {
            // Prior-state restore rather than WPF's unconditional WindowState.Normal
            // (TrayIconService.cs:172) — the port's landed shape for this exact situation
            // (Features/Intake/IntakeHostWindow.axaml.cs:153-160). Recorded as §12 D37.
            _shell.WindowState = prior == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            _shell.Activate();
            _log($"tray: shell restored ({_shell.WindowState})");
        }
        else
        {
            // The user already un-minimized it by hand. Nothing to do to the window, and saying so
            // beats a silent branch.
            _log($"tray: restore asked while the shell was already {_shell.WindowState}; icon taken down, window left alone");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _iconPlaced = false;
        _presence?.Dispose();
        _presence = null;
    }

    private void PlaceIcon()
    {
        var presence = _presence;
        if (presence is null)
        {
            presence = _presenceFactory();
            presence.Activated += OnIconActivated;
            _presence = presence;
        }

        // The MENU first, and the icon only if the menu is really there. An icon whose right-click
        // leads nowhere is the shape the port refused to build a tuck on; it does not
        // hide anything, so this is not load-bearing for safety here — but placing an icon that
        // cannot be asked to do anything would still be placing a dead control.
        var menuState = presence.SetMenu(Menu);
        if (menuState is not CapabilityState.Available)
        {
            LastIconDiagnostic = $"tray: no icon placed — the menu could not be built ({Describe(menuState)}). "
                + "The shell is minimized and its taskbar button is the way back";
            _log(LastIconDiagnostic);
            return;
        }

        var placeState = presence.Place(TrayIconRequest.Default);
        if (placeState is not CapabilityState.Available)
        {
            LastIconDiagnostic = $"tray: no icon placed ({Describe(placeState)}). "
                + "The shell is minimized and its taskbar button is the way back";
            _log(LastIconDiagnostic);
            return;
        }

        _iconPlaced = true;
        LastIconDiagnostic = $"tray: icon placed with a {Menu.Items.Count}-entry menu ({Describe(placeState)})";
        _log(LastIconDiagnostic);

        AskForFirstMinimizeBalloon(presence);
    }

    /// <summary>
    /// WPF's first-minimize balloon, reproduced from the CODE. The XML comment on the DTRH tuck
    /// says "No notification" (<c>MainWindow/MainWindow.RemoteControl.cs:1515</c>) and the call it
    /// documents reaches <c>MinimizeToTray()</c>, which fires a balloon on its first-ever
    /// invocation in the process (<c>TrayIconService.cs:150-157</c>). The latch is per-process
    /// there and per-instance here, and this class is built once per shell window.
    ///
    /// <para>The latch is consumed only when a balloon is really ASKED FOR. WPF always has an icon
    /// by this point, so the distinction never arises for it; here it does, and burning the
    /// once-ever balloon on a duck where no icon exists would mean a user whose first descent
    /// happened while Explorer was restarting never sees it at all.</para>
    /// </summary>
    private void AskForFirstMinimizeBalloon(ITrayPresence presence)
    {
        if (_balloonAsked)
        {
            return;
        }

        _balloonAsked = true;
        BalloonsRequested++;
        var state = presence.ShowNotification(TrayNotification.FirstMinimize);
        _log($"tray: first-minimize balloon requested (WPF TrayIconService.cs:150-157, once per process) — "
            + Describe(state));
    }

    private void RemoveIcon()
    {
        if (!_iconPlaced || _presence is null)
        {
            return;
        }

        _iconPlaced = false;
        _log("tray: icon removed — " + Describe(_presence.Remove()));
    }

    private void OnIconActivated(object? sender, EventArgs e) => Restore();

    private static string Describe(CapabilityState state) => state switch
    {
        CapabilityState.Available available => "Available: " + available.Detail,
        CapabilityState.Unavailable unavailable => $"Unavailable({unavailable.Reason.Code}): {unavailable.Reason.Detail}",
        CapabilityState.Faulted faulted => $"Faulted({faulted.Reason.Code}): {faulted.Reason.Detail}",
        _ => state.ToString() ?? "<null state>",
    };
}
