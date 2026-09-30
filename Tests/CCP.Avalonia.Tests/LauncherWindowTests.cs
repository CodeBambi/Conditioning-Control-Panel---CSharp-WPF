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
}
