using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
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

        launcher.Play(ConditioningControlPanel.Services.Launcher.LauncherCards.Find("intake")!);
        Dispatcher.UIThread.RunJobs();
        Assert.False(launcher.IsVisible);
        Assert.True(shell.IsVisible);
        Assert.Equal("gradedintake", shell.CurrentTab);
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
    });
}
