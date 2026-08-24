using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using CcpClient.Desktop;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Tray;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The shell's duck and restore, on a REAL Avalonia window.
///
/// <para><b>The fact this file exists for is the first one.</b> WPF tucks the main window into the
/// tray while the descent runs; the port minimizes instead, and the reason is a measured Avalonia
/// behaviour rather than caution. Someone reading <c>ShellTray.Duck</c> later will see a minimize
/// where WPF has a <c>Hide()</c> and be tempted to "finish the job". The first test is what that
/// person runs into: hiding the shell in Avalonia hides the DTRH window with it.</para>
///
/// <para>Draw-level ONLY (verification-harness.md). These are facts about Avalonia's window model
/// and the port's own state machine. Nothing here claims a composited pixel, a taskbar button
/// really disappearing or reappearing, a tray icon being visible to a human, a real click, or
/// <c>TrackPopupMenu</c>'s modal loop — all of those are headed.</para>
/// </summary>
public class ShellTrayHeadlessTests : HeadlessTest
{
    [AvaloniaFact]
    public void AvaloniaHidesOwnedWindowsWithTheirOwner_WhichIsWhyTheShellIsNeverHidden()
    {
        // THE MEASUREMENT THE "NO TUCK" DECISION RESTS ON (wpf-surface-reachability.md §12 D35 @ 7527243e7),
        // pinned rather than described. WPF's tuck is _mainWindow.Hide() plus a visible icon
        // (Services/Notifications/TrayIconService.cs:145-148), fired when the hole opens
        // (Services/Chaos/DtrhHostService.cs:156). It survives there because Win32 does not
        // propagate a hide to owned windows — and WPF NEEDS the descent owned, in its own words at
        // DtrhHostService.cs:129-132 (OwnedByMainWindow = true): "Glue the descent above MainWindow
        // via native ownership: main gets raised by plenty of things we don't control ... and used
        // to land on top of the game."
        //
        // The port owns the descent the same way (Features/Dtrh/DtrhLaunchCoordinator.cs:167,
        // window.Show(_owner)). In Avalonia 12.1.1 the two properties are mutually exclusive, and
        // this test is the proof. If a future Avalonia stops propagating, this test fails in the
        // "we can have both now" direction and the tuck gets re-opened DELIBERATELY.
        var shell = new Window { Width = 400, Height = 300 };
        shell.Show();
        var descent = new Window { Width = 320, Height = 240 };
        descent.Show(shell);
        Assert.True(descent.IsVisible);

        shell.Hide();
        Assert.False(descent.IsVisible,
            "Avalonia no longer hides owned windows with their owner. That removes the ONLY reason the port "
            + "minimizes where WPF tucks — re-open the tuck decision on purpose (wpf-surface-reachability.md §12 D35 @ 7527243e7) "
            + "rather than leaving this test green in a direction nobody read");

        shell.Show();
        Assert.False(descent.IsVisible,
            "re-showing the owner brought the owned window back, so a hide/show pair would have been survivable");

        // And the second half of the same seam: while the owner is hidden a new owned window cannot
        // even be created, which is what the watchdog's one permitted relaunch does
        // (DtrhLaunchCoordinator.cs:113 -> :167).
        var relaunch = new Window { Width = 100, Height = 100 };
        shell.Hide();
        Assert.Throws<InvalidOperationException>(() => relaunch.Show(shell));

        // The state the port actually uses. A fresh pair, because `descent` above is now hidden and
        // this leg is about what MINIMIZE does rather than what hide left behind.
        var owner = new Window { Width = 400, Height = 300 };
        owner.Show();
        var owned = new Window { Width = 200, Height = 200 };
        owned.Show(owner);

        owner.WindowState = WindowState.Minimized;
        Assert.True(owned.IsVisible,
            "minimizing the owner took the owned window down too, so the port's duck is no safer than the tuck it "
            + "refused and the DTRH window has nowhere left to be");
        Assert.True(owner.IsVisible,
            "a minimized window must still count as visible — the taskbar button is the port's guaranteed way back");

        var relaunchOverMinimized = new Window { Width = 100, Height = 100 };
        relaunchOverMinimized.Show(owner);
        Assert.True(relaunchOverMinimized.IsVisible,
            "an owned window could not be shown over a MINIMIZED owner, which is what the watchdog relaunch does");
    }

    [AvaloniaFact]
    public void DuckingWithAnUnavailableTray_MinimizesAndNeverHides_AndSaysWhyThereIsNoIcon()
    {
        // THE BRANCH LINUX GETS, treated as the main case. TrayPresenceFactory hands every
        // non-Windows run an UnsupportedTrayPresence, which refuses SetMenu, Place and
        // ShowNotification. The shell must then do exactly what it does with a working tray minus
        // the icon: minimize, keep its taskbar button, and never hide.
        var (shell, tray, _) = Build(() => TrayPresenceFactory.CreateFor(TrayHostPlatform.Linux));

        tray.Duck();

        Assert.True(shell.IsVisible,
            "the shell was HIDDEN while the tray reported Unavailable — there is no icon and no menu, so the taskbar "
            + "button was the only way back and it has just been removed");
        Assert.Equal(WindowState.Minimized, shell.WindowState);
        Assert.True(tray.IsDucked);
        Assert.False(tray.IsIconPlaced);
        Assert.Equal(0, tray.BalloonsRequested);
        Assert.Contains(TrayReasonCodes.TrayMechanismAbsent, tray.LastIconDiagnostic!, StringComparison.Ordinal);
        Assert.Contains("taskbar button is the way back", tray.LastIconDiagnostic!, StringComparison.Ordinal);

        tray.Restore();
        Assert.True(shell.IsVisible);
        Assert.Equal(WindowState.Normal, shell.WindowState);
        Assert.False(tray.IsDucked);
    }

    [AvaloniaFact]
    public void DuckingWithAFullyAvailableTray_StillNeverHidesTheShell_AndPutsTheIconUp()
    {
        // The other half of the same rule, and the one a tuck would break. Even when every tray
        // call succeeds — icon confirmed, menu confirmed, balloon accepted — the shell is minimized,
        // not hidden, because the descent window is owned by it (see the first test in this file).
        var presence = new AvailablePresence();
        var (shell, tray, _) = Build(() => presence);

        tray.Duck();

        Assert.True(shell.IsVisible,
            "the shell was hidden while the tray was fully Available. That is the WPF tuck, and in Avalonia it takes "
            + "the owned DTRH window down with it");
        Assert.Equal(WindowState.Minimized, shell.WindowState);
        Assert.True(tray.IsIconPlaced);

        // The menu goes up BEFORE the icon: an icon whose right-click leads nowhere is the shape
        // the Play door refused to build on.
        Assert.Equal(new[] { "SetMenu", "Place", "ShowNotification" }, presence.Calls.ToArray());
        Assert.Equal(4, presence.LastMenu!.Items.Count);
    }

    [AvaloniaFact]
    public void TheMenusShowDashboardEntry_RestoresTheRealWindow_AndTakesTheIconDown()
    {
        // WPF's "Show Dashboard" (TrayIconService.cs:98-100 -> ShowWindow at :161-175). This invokes
        // the SAME Action object that is installed into the OS menu — the unit suite proves the OS
        // holds that entry and that a real right-click notification dispatches to it, and this
        // proves what the entry then does to a real window.
        var presence = new AvailablePresence();
        var (shell, tray, _) = Build(() => presence);

        tray.Duck();
        Assert.Equal(WindowState.Minimized, shell.WindowState);

        var showDashboard = tray.Menu.Items.Single(i => i.IsRestore);
        Assert.Equal("Show Dashboard", showDashboard.Label);
        showDashboard.Invoke();

        Assert.True(shell.IsVisible);
        Assert.Equal(WindowState.Normal, shell.WindowState);
        Assert.False(tray.IsDucked);
        Assert.False(tray.IsIconPlaced);
        // WPF takes the icon down before showing the window, because that is what dismisses a
        // balloon that is still up (TrayIconService.cs:165-169). The whole sequence, in order.
        Assert.Equal(new[] { "SetMenu", "Place", "ShowNotification", "Remove" }, presence.Calls.ToArray());
    }

    [AvaloniaFact]
    public void TheIconsActivation_RestoresTheRealWindow_ThroughTheSameFunnel()
    {
        // WPF admits a single left-click deliberately, because "clicking the tray icon does nothing"
        // reads as the app being gone (TrayIconService.cs:112-119). The tray capability already proves the
        // shell's own click notification reaches Activated through the real window proc; this is
        // the other end of that wire.
        var presence = new AvailablePresence();
        var (shell, tray, _) = Build(() => presence);

        tray.Duck();
        presence.RaiseActivated();

        Assert.True(shell.IsVisible);
        Assert.Equal(WindowState.Normal, shell.WindowState);
        Assert.False(tray.IsDucked);
        Assert.False(tray.IsIconPlaced);

        // Idempotent: a second click on an icon that is already gone must not thrash the window.
        presence.RaiseActivated();
        Assert.Equal(WindowState.Normal, shell.WindowState);
    }

    [AvaloniaFact]
    public void AMaximizedShell_ComesBackMaximized_OnEveryRestoreRoute()
    {
        // The port's landed shape for this situation (Features/Intake/IntakeHostWindow.axaml.cs:153-160),
        // kept here rather than WPF's unconditional WindowState.Normal at TrayIconService.cs:172.
        // Recorded as a divergence at §12 D37 rather than smuggled in as parity.
        var presence = new AvailablePresence();
        var (shell, tray, _) = Build(() => presence);
        shell.WindowState = WindowState.Maximized;

        tray.Duck();
        Assert.Equal(WindowState.Minimized, shell.WindowState);
        Assert.Equal(WindowState.Maximized, tray.StateBeforeDuck);

        tray.Restore();
        Assert.Equal(WindowState.Maximized, shell.WindowState);
        Assert.True(shell.IsVisible);
    }

    [AvaloniaFact]
    public void TheFirstMinimizeBalloon_IsAskedForOnceEver_AndNeverWithoutAnIcon()
    {
        // WPF's latch: _hasShownFirstMinimizeNotification (TrayIconService.cs:23, set at :154).
        // The DTRH tuck's own comment says "No notification" (MainWindow.RemoteControl.cs:1515) and
        // the code it documents fires one. Code wins.
        var presence = new AvailablePresence();
        var (_, tray, _) = Build(() => presence);

        tray.Duck();
        tray.Restore();
        tray.Duck();
        tray.Restore();

        Assert.Equal(1, tray.BalloonsRequested);
        Assert.Equal(1, presence.NotificationsRequested);
        Assert.Equal("Conditioning Control Panel", presence.LastNotification!.Title);
        Assert.Equal("Running in background. Click the tray icon to restore.", presence.LastNotification!.Message);
        Assert.Equal(2000, presence.LastNotification!.Timeout.TotalMilliseconds);

        // And a duck that never got an icon must not burn the once-ever balloon: a user whose first
        // descent happened while the tray was refusing would otherwise never see it at all.
        var (_, refusedTray, _) = Build(() => TrayPresenceFactory.CreateFor(TrayHostPlatform.Linux));
        refusedTray.Duck();
        Assert.Equal(0, refusedTray.BalloonsRequested);
    }

    [AvaloniaFact]
    public async Task TheShellsOwnTray_IsWpfsFourEntries_AndIsTheSameOneTheDtrhLauncherDucks()
    {
        // Cold composition-root boot, no command-line arguments: the tray a real user gets.
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp096-headless-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            EntitlementFactory = _ => new HostLoginEntitlement(new NoLoginReader()),
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        Track(host!);

        var window = new MainWindow(host!);
        window.Show();
        window.UpdateLayout();

        // ONE tray per shell: the DTRH launcher ducks the same object the menu restores, or the
        // icon would come down on a window nobody minimized.
        Assert.Same(window.ShellTray, window.Dtrh.Tray);

        // WPF's four entries, in WPF's order (TrayIconService.cs:98,103,107,109).
        var labels = window.ShellTray.Menu.Items.Select(i => i.IsSeparator ? "<separator>" : i.Label).ToArray();
        Assert.Equal(new[] { "Show Dashboard", "Wake Up!", "<separator>", "Exit" }, labels);
        Assert.Equal("Show Dashboard", window.ShellTray.Menu.RestoreItem.Label);

        // The wake entry really opens the port's companion window — §5's third route to her, and
        // the only one that works while the shell is out of the way.
        Assert.Null(window.Companion);
        window.ShellTray.Menu.Items.Single(i => i.Id == "wake").Invoke();
        Assert.NotNull(window.Companion);

        window.Close();
        await host!.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheMenusExitEntry_WithNoClassicLifetime_SaysSoAndShutsNothingDown()
    {
        // The ONE menu entry whose effect had never been executed by anything. WPF's Exit
        // (TrayIconService.cs:109-111 -> MainWindow/MainWindow.xaml.cs:323-343) ends in
        // Application.Current.Shutdown(); the port's counterpart is the classic desktop lifetime's
        // Shutdown(), which reaches the one guarded teardown (App.axaml.cs:88-95).
        //
        // The branch assertable here is the OTHER one — a host with no classic lifetime, which is
        // exactly what a test runner is. It exists so a menu entry can never kill the process that
        // is checking it, and it was written and then never run.
        var log = new List<string>();
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp097-exit-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            EntitlementFactory = _ => new HostLoginEntitlement(new NoLoginReader()),
            LogSinkFactory = () => new ListLogSink(log),
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        Assert.IsType<StartupOutcome.Success>(await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None));
        Track(host!);

        var window = new MainWindow(host!);
        window.Show();
        window.UpdateLayout();

        // THE PRECONDITION IS ASSERTED BEFORE THE ENTRY IS INVOKED, deliberately. If a future
        // headless host ever grew a classic lifetime, invoking Exit would shut the TEST RUNNER
        // down and the failure would look like an unrelated crash. This fails first, and says why.
        Assert.False(
            Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime,
            "this headless host now HAS a classic desktop lifetime, so invoking the tray's Exit entry would really "
            + "shut the test runner down. Re-point this fact at the no-lifetime branch some other way before "
            + "letting it run");

        var exit = window.ShellTray.Menu.Items.Single(i => i.Id == "exit");
        Assert.Equal("Exit", exit.Label);      // WPF's string, TrayIconService.cs:109
        Assert.False(exit.IsSeparator);
        Assert.False(exit.IsRestore);          // Exit is not a way back; only "Show Dashboard" is

        exit.Invoke();

        // Nothing was shut down, and the app SAID so rather than failing silently or throwing.
        Assert.False(host!.IsShutdown);
        Assert.True(window.IsVisible);
        Assert.Contains(
            log.ToArray(),
            line => line.Contains("tray: Exit chosen", StringComparison.Ordinal)
                && line.Contains("no classic desktop lifetime", StringComparison.Ordinal)
                && line.Contains("nothing was shut down", StringComparison.Ordinal));

        // Idempotent: a second Exit on a host that cannot exit still must not throw or half-do it.
        exit.Invoke();
        Assert.False(host.IsShutdown);
        Assert.True(window.IsVisible);

        window.Close();
        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheDtrhFlowENDING_RestoresTheShell_ThroughDtrhLaunchsOwnFunnel()
    {
        // An earlier packet proved the duck and every restore route ShellTray owns. What had no fact
        // is DtrhLaunch's own wiring of the restore — `coordinator.FlowEnded += RestoreOwner`
        // (Features/Dtrh/DtrhLaunch.cs) — the single funnel every real flow end goes through:
        // host window closed, picker cancelled, descend failed. WPF restores at the same point
        // (Services/Chaos/DtrhHostService.cs:998).
        //
        // The flow end here is REAL: an entitled FALL IN opens the REAL slot picker through the
        // REAL default descent seam, and cancelling it is WPF's own back-out (slot == null -> no
        // launch, MainWindow.Lab.cs:246-247) which raises FlowEnded from the real coordinator.
        // Only the DUCK is invoked directly, because the duck's real trigger is HostOpened — a
        // WebView2 host window no headless frame can present. That half is the duck's; this is the
        // half it left open.
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp097-restore-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            EntitlementFactory = _ => new HostLoginEntitlement(new NoLoginReader()),
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        Assert.IsType<StartupOutcome.Success>(await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None));
        Track(host!);

        var owner = new Window { Width = 500, Height = 380 };
        owner.Show();
        var presence = new AvailablePresence();
        var log = new List<string>();
        var tray = new ShellTray(owner, log.Add, wakeCompanion: () => { }, exit: () => { }, () => presence);

        // The launcher's OWN entitlement, entitled at the Lab bar so the gate really proceeds and
        // the real picker really opens. Nothing about the restore wiring is stubbed.
        var entitlement = new HostLoginEntitlement(new FixtureLoginReader(), new EntitledAuthority());
        var dtrh = new DtrhLaunch(host!, owner, entitlement, tray);
        Assert.Same(tray, dtrh.Tray);

        _ = dtrh.FallInAsync();
        await CcpClient.Tests.TestWait.Until(
            () => dtrh.Coordinator.Picker is not null,
            "the real slot picker to open after an entitled FALL IN",
            () => $"decision={dtrh.LastDecision}, picker={dtrh.Coordinator.Picker}, fault={dtrh.LastFault}");

        // The shell gets out of the way exactly as DuckOwner does it (ShellTray.Duck, the one
        // implementation), so what follows is a restore of a really-ducked shell.
        tray.Duck();
        Assert.True(tray.IsDucked);
        Assert.True(tray.IsIconPlaced);
        Assert.Equal(WindowState.Minimized, owner.WindowState);

        dtrh.Coordinator.Picker!.Close();

        await CcpClient.Tests.TestWait.Until(
            () => !tray.IsDucked,
            "cancelling the picker to end the flow and restore the shell through DtrhLaunch.RestoreOwner",
            () => $"ducked={tray.IsDucked}, picker={dtrh.Coordinator.Picker}, state={owner.WindowState}");

        Assert.Equal(WindowState.Normal, owner.WindowState);
        Assert.True(owner.IsVisible);
        Assert.False(tray.IsIconPlaced);
        // The icon came down before the window came back, WPF's order (TrayIconService.cs:165-169).
        Assert.Equal(new[] { "SetMenu", "Place", "ShowNotification", "Remove" }, presence.Calls.ToArray());
        Assert.Null(dtrh.Coordinator.HostWindow);

        owner.Close();
        await host!.ShutdownAsync();
    }

    // ---------- fixtures ----------

    private static (Window Shell, ShellTray Tray, List<string> Log) Build(Func<ITrayPresence> presenceFactory)
    {
        var log = new List<string>();
        var shell = new Window { Width = 400, Height = 300 };
        shell.Show();
        var tray = new ShellTray(shell, log.Add, wakeCompanion: () => { }, exit: () => { }, presenceFactory);
        return (shell, tray, log);
    }

    /// <summary>
    /// A presence for which every platform call succeeds. It exists to prove the branch that CANNOT
    /// be reached on a Linux run and is not guaranteed on a Windows CI box either: a fully working
    /// tray. It is never used to prove that a real icon exists — the unit suite does that against
    /// the shell itself, and this double deliberately reports nothing the OS was asked about.
    /// </summary>
    private sealed class AvailablePresence : ITrayPresence
    {
        public event EventHandler? Activated;

        public List<string> Calls { get; } = [];

        public bool IsPlaced { get; private set; }

        public TrayMenu? LastMenu { get; private set; }

        public TrayNotification? LastNotification { get; private set; }

        public int NotificationsRequested { get; private set; }

        public CapabilityState Place(TrayIconRequest request)
        {
            Calls.Add("Place");
            IsPlaced = true;
            return new CapabilityState.Available("fixture: placed");
        }

        public CapabilityState Remove()
        {
            Calls.Add("Remove");
            IsPlaced = false;
            return new CapabilityState.Available("fixture: removed");
        }

        public CapabilityState SetMenu(TrayMenu menu)
        {
            Calls.Add("SetMenu");
            LastMenu = menu;
            return new CapabilityState.Available("fixture: menu installed");
        }

        public CapabilityState ShowNotification(TrayNotification notification)
        {
            Calls.Add("ShowNotification");
            LastNotification = notification;
            NotificationsRequested++;
            return new CapabilityState.Available("fixture: balloon accepted");
        }

        public void RaiseActivated() => Activated?.Invoke(this, EventArgs.Empty);

        public void Dispose() => Calls.Add("Dispose");
    }

    /// <summary>A shipping-app login that is simply not there — this build's ordinary state. Never
    /// touches the developer's real store.</summary>
    private sealed class NoLoginReader : IHostAuthTokenReader
    {
        public HostTokenRead Read() => HostTokenRead.NoTokenFile();
    }

    /// <summary>A readable login for the one test that needs the DTRH gate to say yes. Fresh per
    /// call: the capability disposes the read on every path.</summary>
    private sealed class FixtureLoginReader : IHostAuthTokenReader
    {
        public HostTokenRead Read() =>
            HostTokenRead.Found(new HostAuthToken("SP097-FIXTURE-NOT-A-REAL-TOKEN-4c1f9e"));
    }

    /// <summary>An authority that confirms the Lab tier. It never sees a real credential.</summary>
    private sealed class EntitledAuthority : IEntitlementTierSource
    {
        public Task<TierLookup> LookupAsync(HostAuthToken token, CancellationToken cancellationToken) =>
            Task.FromResult(TierLookup.Entitled(EntitlementTier.Lab));
    }

    /// <summary>The host's diagnostic sink, captured — a menu entry that only LOGS its refusal is
    /// checkable only through the log.</summary>
    private sealed class ListLogSink(List<string> lines) : ILogSink
    {
        public void Log(string message)
        {
            lock (lines)
            {
                lines.Add(message);
            }
        }
    }
}
