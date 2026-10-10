using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Launcher slice 1: the shell's back-to-client door, the cards this head can open, and
/// the close rule (WPF LauncherHost, decided by Core LauncherRules).</summary>
public sealed class LauncherWindowTests
{
    private static void Run(Action<MainShellWindow> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            // A welcomed, 18+-accepted install: the age gate would exit a panel it finds hidden.
            CoreSettings.Current.Welcomed = true;
            CoreSettings.Current.HasAcceptedAgeVerification = true;
            // Routing tests: motion off, so a hide is not held for the exit beat and the fade (LauncherFxTests cover those).
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            var (oldIn, oldLab) = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider);
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                body(shell);
            }
            finally
            {
                LauncherWindow.Instance?.Close();
                LauncherWindow.Boot = BootDecision.PanelFirst;
                LockdownService.Current = null;
                (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider) = (oldIn, oldLab);
                shell.Close();
                CoreSettings.ServiceProvider = null;
            }
        });
    }

    private static string[] Tiles(LauncherWindow w) =>
        w.FindControl<UniformGrid>("GamesGrid")!.Children.Select(c => (string)c.Tag!).ToArray();

    private static string?[] Texts(Control c) =>
        c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();

    [Fact]
    public void BackToLauncher_TucksThePanel_AndIntakeOpensItOnTheTab() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        CoreEntitlement.HasLabProvider = () => true;

        Assert.True(LauncherWindow.BackToLauncher(shell));
        Dispatcher.UIThread.RunJobs();
        var launcher = LauncherWindow.Instance!;
        Assert.True(launcher.IsVisible);
        Assert.False(shell.IsVisible);

        // Only the destinations this head has: no game host exists here yet.
        Assert.Equal(new[] { "intake" }, Tiles(launcher));
        var tile = launcher.FindControl<UniformGrid>("GamesGrid")!.Children[0];
        Assert.Contains(Loc.Get("launcher_game_intake_title"), Texts(tile));
        Assert.Contains(Loc.Get("launcher_play"), Texts(tile));

        ClickPlay(tile);
        Dispatcher.UIThread.RunJobs();
        Assert.False(launcher.IsVisible);
        Assert.True(shell.IsVisible);
        Assert.Equal("gradedintake", shell.CurrentTab);
    });

    /// <summary>WPF 7.1.5 Tiles.cs:220: a press and release anywhere on the card runs Play; a press
    /// that ends off the card does not.</summary>
    [Fact]
    public void WholeCard_PressAndReleaseOnTheArt_Plays_ReleaseOffTheCardDoesNot() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        CoreEntitlement.HasLabProvider = () => true;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var tile = w.FindControl<UniformGrid>("GamesGrid")!.Children[0];
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var art = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 4), w)!.Value;
        var off = tile.TranslatePoint(new Point(-40, tile.Bounds.Height / 4), w)!.Value;

        w.MouseDown(art, MouseButton.Left);
        w.MouseUp(off, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.IsVisible);
        Assert.False(shell.IsVisible);

        w.MouseDown(art, MouseButton.Left);
        w.MouseUp(art, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.IsVisible);
        Assert.True(shell.IsVisible);
        Assert.Equal("gradedintake", shell.CurrentTab);
    });

    /// <summary>WPF 7.1.5 GrowToFitTiles: overflowing tiles grow the window once, within the work area.</summary>
    [Fact]
    public void OverflowingTiles_GrowTheWindowOnce_WithinTheWorkArea() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var grid = w.FindControl<UniformGrid>("GamesGrid")!;
        // One tile fits: the block sits centred (WPF FitTiles).
        Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Center, grid.VerticalAlignment);
        for (int i = 0; i < 8; i++) grid.Children.Add(new Border());
        double old = w.Bounds.Height;
        w.SeatGrid();
        double grown = w.Height;
        Assert.True(grown > old + 1, $"{old} -> {grown}");
        var wa = w.Screens.ScreenFromWindow(w)!;
        Assert.True(grown <= wa.WorkingArea.Height / wa.Scaling + 0.5);
        Assert.InRange(w.Position.Y, wa.WorkingArea.Y, wa.WorkingArea.Bottom - (int)Math.Ceiling(grown * wa.Scaling));
        Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Top, grid.VerticalAlignment);

        w.Height = old;
        Dispatcher.UIThread.RunJobs();
        w.SeatGrid();
        Assert.Equal(old, w.Height);
    });

    private static void ClickPlay(Control tile) =>
        tile.GetVisualDescendants().OfType<Button>().Single()
            .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void Lockdown_TilePlayRefuses_LauncherStaysAndPanelStaysTucked() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        CoreEntitlement.HasLabProvider = () => true;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var launcher = LauncherWindow.Instance!;
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            ld.Activate(TimeSpan.FromMinutes(30));
            ClickPlay(launcher.FindControl<UniformGrid>("GamesGrid")!.Children[0]);
            Dispatcher.UIThread.RunJobs();
            Assert.True(launcher.IsVisible);
            Assert.False(shell.IsVisible);
        }
        finally { ld.Deactivate(); }
    });

    private static bool LoginOpen(LauncherWindow w) =>
        w.OwnedWindows.Any(o => o is ConditioningControlPanel.Avalonia.Views.Dialogs.LoginDialog { IsVisible: true });

    private static void Raise(LauncherWindow w, string name) =>
        w.FindControl<Button>(name)!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void Lockdown_SignedOutCardPress_AndSignInPillOpenNoLogin() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => false;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var tile = w.FindControl<UniformGrid>("GamesGrid")!.Children[0];
        // A real press/release on the art plate (not the Play button), as a user taps the card.
        void Press()
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var p = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 4), w)!.Value;
            w.MouseDown(p, MouseButton.Left);
            w.MouseUp(p, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            ld.Activate(TimeSpan.FromMinutes(30));
            Press();
            Assert.False(LoginOpen(w));
            Raise(w, "SignInPill");
            Dispatcher.UIThread.RunJobs();
            Assert.False(LoginOpen(w));
            // Control: the same press reaches the card once Lockdown ends.
            ld.Deactivate();
            Press();
            Assert.True(LoginOpen(w));
        }
        finally
        {
            ld.Deactivate();
            foreach (var o in w.OwnedWindows.ToList()) o.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Theory]
    [InlineData("PanelCta")]
    [InlineData("AccountChipButton")]
    public void Lockdown_PanelCtaAndAccountChip_LeaveThePanelTucked(string button) => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            ld.Activate(TimeSpan.FromMinutes(30));
            Raise(w, button);
            Dispatcher.UIThread.RunJobs();
            Assert.True(w.IsVisible);
            Assert.False(shell.IsVisible);
        }
        finally { ld.Deactivate(); }
    });

    [Fact]
    public void SignedOut_TheCardAsksForSignIn_AndPlayLeavesThePanelTucked() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => false;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var tile = LauncherWindow.Instance!.FindControl<UniformGrid>("GamesGrid")!.Children[0];
        Assert.Contains(Loc.Get("launcher_sign_in"), Texts(tile));
        Assert.Contains(Loc.Get("launcher_pill_sign_in"), Texts(tile));
        Assert.False(shell.IsVisible);
    });

    [Fact]
    public void Lockdown_RefusesTheDoor() => Run(shell =>
    {
        var ld = LockdownService.Current = new LockdownService();
        ld.Activate(TimeSpan.FromMinutes(30));
        Assert.False(LauncherWindow.BackToLauncher(shell));
        Assert.True(shell.IsVisible);
        Assert.Null(LauncherWindow.Instance);
        ld.Deactivate();
    });

    [Fact]
    public void Close_HidesWhileThePanelIsUp_ExitsWhenNothingIsLeft() => Run(shell =>
    {
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var launcher = LauncherWindow.Instance!;

        shell.Show();                               // the panel came back from the tray
        launcher.RequestClose();
        Assert.False(launcher.IsVisible);           // hidden, not exiting
        Assert.True(shell.IsVisible);

        var closed = false;
        shell.Closed += (_, _) => closed = true;
        LauncherWindow.Open();
        shell.Hide();
        launcher.RequestClose();                    // nothing running, panel tucked: leave
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
    });
    [Fact]
    public void ClosingTheShell_ClosesTheHiddenLauncher() => Run(shell =>
    {
        LauncherWindow.BackToLauncher(shell);
        var launcher = LauncherWindow.Instance!;
        launcher.Hide();
        shell.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(LauncherWindow.Instance);       // gone, not a hidden window keeping the process up
    });

    [Fact]
    public void Close_HidesWhileTheEngineOrASessionRuns_MinimizesWithNoTray() => Run(shell =>
    {
        var oldSession = CoreSession.IsSessionRunningProvider;
        try
        {
            CoreSettings.Current.FlashEnabled = true;
            shell.StartEngine();
            LauncherWindow.BackToLauncher(shell);
            var launcher = LauncherWindow.Instance!;
            shell.TrayHostPresent = () => true;
            launcher.RequestClose();
            Assert.False(launcher.IsVisible);           // engine running: hide, not exit
            Assert.True(CoreEngine.IsRunning);

            CoreEngine.Stop();
            CoreSession.IsSessionRunningProvider = () => true;
            LauncherWindow.Open();
            launcher.RequestClose();
            Assert.False(launcher.IsVisible);           // session running: hide, not exit
            Assert.Same(launcher, LauncherWindow.Instance);   // hidden, not closed

            LauncherWindow.Open();
            shell.TrayHostPresent = () => false;
            launcher.RequestClose();
            Assert.True(launcher.IsVisible);            // no tray: the way back stays on the taskbar
            Assert.Equal(WindowState.Minimized, launcher.WindowState);
        }
        finally
        {
            CoreSession.IsSessionRunningProvider = oldSession;
            CoreEngine.Stop();
        }
    });

    // ---- slice 2: boot surface, second-instance handoff, tray row, skip box

    [Fact]
    public void BootIntoTheLauncher_TucksThePanel() => Run(shell =>
    {
        LauncherWindow.Boot = BootDecision.LauncherFirst;
        LauncherWindow.RouteBoot(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.IsVisible);
        Assert.True(LauncherWindow.Instance!.IsVisible);
    });

    [Fact]
    public void BootIntoAGame_OpensItsDestination_UnknownHereShowsTheTiles() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        CoreEntitlement.HasLabProvider = () => true;
        LauncherWindow.Boot = BootDecision.GameFirst("intake");
        LauncherWindow.RouteBoot(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.IsVisible);
        Assert.Equal("gradedintake", shell.CurrentTab);
        Assert.False(LauncherWindow.Instance!.IsVisible);

        LauncherWindow.Boot = BootDecision.GameFirst("race");   // a WPF game with no host on this head
        LauncherWindow.RouteBoot(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.IsVisible);
        Assert.True(LauncherWindow.Instance!.IsVisible);
    });

    [Fact]
    public void SecondInstance_RoutesTheSurfaceItNames() => Run(shell =>
    {
        // SkipToPanelBox saves true to this run's settings.json, which Run's fresh SettingsService reloads.
        CoreSettings.Current.LauncherSkipToPanel = false;
        LauncherWindow.RouteHandoff(shell, LauncherHandoff.Encode(new[] { "--launcher" }));
        Dispatcher.UIThread.RunJobs();
        var launcher = LauncherWindow.Instance!;
        Assert.True(launcher.IsVisible);
        Assert.False(shell.IsVisible);

        LauncherWindow.RouteHandoff(shell, LauncherHandoff.Encode(new[] { "--panel" }));
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.IsVisible);
        Assert.False(launcher.IsVisible);

        LauncherWindow.RouteHandoff(shell, "game:nope");
        Dispatcher.UIThread.RunJobs();
        Assert.True(launcher.IsVisible);

        // Bare relaunch with the panel tucked: the launcher, unless "open the panel directly".
        shell.Hide();
        LauncherWindow.RouteHandoff(shell, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.IsVisible);
        Assert.True(launcher.IsVisible);
        CoreSettings.Current.LauncherSkipToPanel = true;
        LauncherWindow.RouteHandoff(shell, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.IsVisible);
        Assert.False(launcher.IsVisible);
    });

    [Fact]
    public void SecondInstance_UnderLockdown_NeverTucksThePanel() => Run(shell =>
    {
        var ld = LockdownService.Current = new LockdownService();
        ld.Activate(TimeSpan.FromMinutes(30));
        LauncherWindow.RouteHandoff(shell, "launcher");
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.IsVisible);
        ld.Deactivate();
    });

    private static NativeMenuItem BackRow(MainShellWindow shell) => shell.BuildTrayMenu().Items.OfType<NativeMenuItem>()
        .Single(i => i.Header == Loc.Get("launcher_back_to_client"));

    [Fact]
    public void TrayBackRow_OnlyWhileTheLauncherIsInPlay_GreyedUnderLockdown() => Run(shell =>
    {
        Assert.False(BackRow(shell).IsVisible);             // a panel boot, launcher never built
        LauncherWindow.Boot = BootDecision.LauncherFirst;
        var row = BackRow(shell);
        Assert.True(row.IsVisible);
        Assert.True(row.IsEnabled);

        var ld = LockdownService.Current = new LockdownService();
        ld.Activate(TimeSpan.FromMinutes(30));
        Assert.False(BackRow(shell).IsEnabled);
        ld.Deactivate();

        row.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.IsVisible);
        Assert.True(LauncherWindow.Instance!.IsVisible);
    });

    [Fact]
    public void SkipToPanelBox_ShowsAndSavesTheSetting() => Run(shell =>
    {
        CoreSettings.Current.LauncherSkipToPanel = false;
        var box = LauncherWindow.Open().FindControl<CheckBox>("SkipToPanel")!;
        Assert.False(box.IsChecked);
        Assert.Contains(Loc.Get("launcher_skip_to_panel"), Texts(box));
        box.IsChecked = true;
        box.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(CoreSettings.Current.LauncherSkipToPanel);
        // The click saved true to the run's settings.json; put it back so no later test inherits it.
        CoreSettings.Current.LauncherSkipToPanel = false;
        (CoreSettings.ServiceProvider?.Invoke() as SettingsService)?.SaveImmediate();
    });

    [Fact]
    public void BootHiddenPanel_KeepsTheCompanionTubeDown() => Run(_ =>
    {
        var old = CoreSettings.Current.AvatarEnabled;
        CoreSettings.Current.AvatarEnabled = true;
        var panel = new MainShellWindow { BuildingHiddenForBoot = true };   // as App's launcher boot
        try
        {
            panel.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(panel.Tube);                 // built, as WPF does on load
            Assert.False(panel.Tube!.IsVisible);        // but never on screen before the launcher
        }
        finally
        {
            panel.Close();
            CoreSettings.Current.AvatarEnabled = old;
        }
    });

    // ---- slice 3: account chip, mod pill, panel card status/stats (WPF LauncherWindow.xaml.cs:220-606)

    private static string? Text(LauncherWindow w, string name) => w.FindControl<TextBlock>(name)!.Text;

    [Fact]
    public void AccountChip_SignedInShowsNameInitialAndSp_SignedOutShowsThePill() => Run(shell =>
    {
        var s = CoreSettings.Current;
        s.UserDisplayName = "  bambi ";
        s.SkillPoints = 1234;
        CoreAccount.IsLoggedInProvider = () => true;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        Assert.True(w.FindControl<Button>("AccountChipButton")!.IsVisible);
        Assert.False(w.FindControl<Button>("SignInPill")!.IsVisible);
        Assert.Equal("bambi", Text(w, "AccountName"));
        Assert.Equal("B", Text(w, "AvatarInitial"));
        Assert.True(w.FindControl<StackPanel>("SpChip")!.IsVisible);
        Assert.Equal(1234.ToString("N0"), Text(w, "SpReadout"));
        Assert.False(w.FindControl<Image>("TierBadge")!.IsVisible);   // no Patreon tier here

        CoreAccount.IsLoggedInProvider = () => false;
        w.RefreshAccount();
        Assert.False(w.FindControl<Button>("AccountChipButton")!.IsVisible);
        Assert.True(w.FindControl<Button>("SignInPill")!.IsVisible);
        Assert.False(w.FindControl<StackPanel>("SpChip")!.IsVisible);
    });

    [Fact]
    public void PanelCard_ShowsStatsAndRunningStatus_StopLinkStopsTheEngine() => Run(shell =>
    {
        var s = CoreSettings.Current;
        (s.PlayerLevel, s.PlayerXP, s.SkillPoints, s.TotalConditioningMinutes) = (3, 50, 7, 125);
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        Assert.Equal("3", Text(w, "StatLevel"));
        Assert.Equal("7", Text(w, "StatSparkles"));
        Assert.Equal("2h 05m", Text(w, "StatTime"));
        var need = XpCurve.GetXPForLevel(3, XpCurve.EpochOf(s));
        Assert.Equal(Loc.GetF("launcher_stat_xp", "50", ((int)need).ToString("N0")), Text(w, "XpCaption"));
        Assert.Equal(Loc.Get("launcher_panel_idle"), Text(w, "StatusText"));
        Assert.Equal(Loc.Get("launcher_panel_launch"), Text(w, "PanelCtaText"));
        var stop = w.FindControl<Button>("StopLink")!;
        Assert.False(stop.IsVisible);

        try
        {
            s.FlashEnabled = true;
            shell.StartEngine();
            w.RefreshStatus();
            Assert.True(CoreEngine.IsRunning);
            Assert.Equal(Loc.GetF("launcher_panel_running", CoreEngine.StartedUtc!.Value.ToLocalTime().ToString("HH:mm")),
                Text(w, "StatusText"));
            Assert.Equal(Loc.Get("launcher_panel_open"), Text(w, "PanelCtaText"));
            Assert.True(stop.IsVisible);

            stop.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.False(CoreEngine.IsRunning);
            Assert.Null(CoreEngine.StartedUtc);
            Assert.False(stop.IsVisible);
            Assert.Equal(Loc.Get("launcher_panel_idle"), Text(w, "StatusText"));
        }
        finally { CoreEngine.Stop(); }
    });

    [Fact]
    public void ModPill_ReadsTheActiveMod_ASwitchRepaintsThePillAndRedrawsTheTiles() => Run(shell =>
    {
        var snapshot = new CoreModsSnapshot();
        var oldResources = Application.Current!.Resources.Keys.ToHashSet();
        try
        {
            CoreSettings.Current.ActiveModId = BuiltInMods.CCPDefaultId;
            global::ConditioningControlPanel.Avalonia.App.StartMods();
            var mods = global::ConditioningControlPanel.Avalonia.App.Mods!;
            LauncherWindow.BackToLauncher(shell);
            Dispatcher.UIThread.RunJobs();
            var w = LauncherWindow.Instance!;
            string Label() => LauncherModMenu.Label(Loc.Get("launcher_mod_label"), mods.ActiveMod.Name, "-");
            Assert.Equal(Label(), Text(w, "ModPillText"));
            var tileBefore = w.FindControl<UniformGrid>("GamesGrid")!.Children[0];

            w.SwitchMod(BuiltInMods.DronificationId);
            Dispatcher.UIThread.RunJobs();   // the tile redraw is posted, as WPF's
            Assert.Equal(BuiltInMods.DronificationId, mods.ActiveModId);
            Assert.Equal(Label(), Text(w, "ModPillText"));
            Assert.NotSame(tileBefore, w.FindControl<UniformGrid>("GamesGrid")!.Children[0]);
        }
        finally
        {
            foreach (var key in Application.Current.Resources.Keys.Where(k => !oldResources.Contains(k)).ToList())
                Application.Current.Resources.Remove(key);
            snapshot.Dispose();
            global::ConditioningControlPanel.Avalonia.App.ResetReleaseContent();
            CoreSettings.Current.ActiveModId = BuiltInMods.CCPDefaultId;
        }
    });
    [Fact]
    public void Lockdown_StopLinkRefuses_EngineKeepsRunning() => Run(shell =>
    {
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            CoreSettings.Current.FlashEnabled = true;
            shell.StartEngine();
            ld.Activate(TimeSpan.FromMinutes(30));
            w.FindControl<Button>("StopLink")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(CoreEngine.IsRunning);
        }
        finally { ld.Deactivate(); CoreEngine.Stop(); }
    });

    [Fact]
    public void Lockdown_ModPillOpensNoMenu_AndSwitchModRefuses() => Run(shell =>
    {
        var snapshot = new CoreModsSnapshot();
        var oldResources = Application.Current!.Resources.Keys.ToHashSet();
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            CoreSettings.Current.ActiveModId = BuiltInMods.CCPDefaultId;
            global::ConditioningControlPanel.Avalonia.App.StartMods();
            LauncherWindow.BackToLauncher(shell);
            Dispatcher.UIThread.RunJobs();
            var w = LauncherWindow.Instance!;
            ld.Activate(TimeSpan.FromMinutes(30));
            var pill = w.FindControl<Button>("ModPill")!;
            pill.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Null(pill.ContextMenu);
            w.SwitchMod(BuiltInMods.DronificationId);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BuiltInMods.CCPDefaultId, global::ConditioningControlPanel.Avalonia.App.Mods!.ActiveModId);
        }
        finally
        {
            ld.Deactivate();
            foreach (var key in Application.Current.Resources.Keys.Where(k => !oldResources.Contains(k)).ToList())
                Application.Current.Resources.Remove(key);
            snapshot.Dispose();
            global::ConditioningControlPanel.Avalonia.App.ResetReleaseContent();
            CoreSettings.Current.ActiveModId = BuiltInMods.CCPDefaultId;
        }
    });

    [Fact]
    public void StatusTimer_StopsWhenTheLauncherHides() => Run(shell =>
    {
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var timer = (DispatcherTimer)typeof(LauncherWindow)
            .GetField("_statusTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(w)!;
        Assert.True(timer.IsEnabled);
        w.Hide();
        Assert.False(timer.IsEnabled);
    });
}
