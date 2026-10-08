using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>win-feature-intro: the shell's real doors open the one-shot cards (WPF
/// MainWindow.TabNavigation.cs MaybeShowFeatureIntro / OnDashboardTabVisibilityChanged), and a
/// startup modal or update dialog holds them unspent (WPF FeatureIntroPopup.xaml.cs:211).</summary>
public sealed class FeatureIntroWiringTests
{
    private static readonly FieldInfo UpdaterBusy =
        typeof(AppUpdater).GetField("_busy", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static Task Run(Action<List<Window>, MainShellWindow> body, params string[] unseen) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        StartupLadder.ResetForTests();
        FeatureIntroPopup.ResetForTests();
        var seen = CoreSettings.Current.SeenFeatureIntros;
        var was = seen.ToList();
        // Every card spent except the ones under test, so the Dashboard's launch cards stay out of the way.
        foreach (var k in FeatureIntros.All.Keys) if (!seen.Contains(k)) seen.Add(k);
        foreach (var k in unseen) seen.Remove(k);
        var opened = new List<Window>();
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            body(opened, shell);
        }
        finally
        {
            foreach (var w in opened.Where(w => w != shell).ToArray()) w.Close();
            shell.Close();
            MainShellWindow.IsStartupDialogShowing = false;
            UpdaterBusy.SetValue(null, false);
            seen.Clear();
            seen.AddRange(was);
            CoreSettings.Save();
            StartupLadder.ResetForTests();
            FeatureIntroPopup.ResetForTests();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    private static void ClickLockdown(MainShellWindow shell) => ClickNav(shell, "BtnNavLockdown");

    private static void ClickNav(MainShellWindow shell, string button)
    {
        // Each click is a fresh offer: forget the ladder's one-passive-at-a-time settle and the
        // card's pacing, so only the gate under test can refuse it.
        StartupLadder.ResetForTests();
        FeatureIntroPopup.ResetForTests();
        shell.Named<Button>(button)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static string? CardTitle(List<Window> opened) =>
        opened.OfType<FeatureIntroPopup>().SingleOrDefault()?.FindControl<TextBlock>("TxtTitle")?.Text;

    [Fact]
    public Task LockdownDoorOpensItsCardOnceAndSpendsIt() => Run((opened, shell) =>
    {
        Assert.Empty(opened.OfType<FeatureIntroPopup>());
        ClickLockdown(shell);
        Assert.Equal(FeatureIntros.All["lockdown"].Title, CardTitle(opened));
        Assert.Contains("lockdown", CoreSettings.Current.SeenFeatureIntros);

        ClickLockdown(shell);   // past the pacing: the seen flag alone refuses
        Assert.Single(opened.OfType<FeatureIntroPopup>());
    }, "lockdown");

    [Fact]
    public Task StartupModalAndUpdateDialogHoldTheCardUnspent() => Run((opened, shell) =>
    {
        MainShellWindow.IsStartupDialogShowing = true;
        ClickLockdown(shell);
        Assert.Empty(opened.OfType<FeatureIntroPopup>());
        Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);

        MainShellWindow.IsStartupDialogShowing = false;
        UpdaterBusy.SetValue(null, true);
        ClickLockdown(shell);
        Assert.Empty(opened.OfType<FeatureIntroPopup>());
        Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);

        UpdaterBusy.SetValue(null, false);
        ClickLockdown(shell);
        Assert.Equal(FeatureIntros.All["lockdown"].Title, CardTitle(opened));
    }, "lockdown");

    [Fact]
    public Task LaunchingOnTheDashboardQueuesDailyFreeAndHoldsOneAccount() => Run((opened, shell) =>
    {
        // The shell lands on the Dashboard with no ShowTab behind it: attach alone queued the cards.
        Assert.Equal(FeatureIntros.All["daily-free"].Title, CardTitle(opened));
        Assert.Contains("daily-free", CoreSettings.Current.SeenFeatureIntros);
        Assert.DoesNotContain("one-account", CoreSettings.Current.SeenFeatureIntros);
    }, "daily-free", "one-account");

    [Fact]
    public Task StudioDoorOpensStudioRackAndRemoteControlOpensNothing() => Run((opened, shell) =>
    {
        // WPF MainWindow.TabNavigation.cs:505: the rack card rides case "studio"; case
        // "remotecontrol" (a Play-door tab) calls nothing.
        ClickNav(shell, "BtnNavRemoteControl");
        Assert.Empty(opened.OfType<FeatureIntroPopup>());
        Assert.DoesNotContain("studio-rack", CoreSettings.Current.SeenFeatureIntros);

        ClickNav(shell, "BtnNavStudio");
        Assert.Equal(FeatureIntros.All["studio-rack"].Title, CardTitle(opened));
        Assert.Contains("studio-rack", CoreSettings.Current.SeenFeatureIntros);
    }, "studio-rack");
}
