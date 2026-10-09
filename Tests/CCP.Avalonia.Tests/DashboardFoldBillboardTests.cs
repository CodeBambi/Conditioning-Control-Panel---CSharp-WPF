using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.DashboardFold + DashboardBillboard: the saved fold, the chevron, the
/// run-only reveal, and the billboard that fills the freed row with its gated 12 s clock.</summary>
public sealed class DashboardFoldBillboardTests
{
    private static void Click(Control c) => c.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task FoldChevronRevealAndBillboardFollowTheUser() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var oldOpen = MainShellWindow.BillboardOpenUrl;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.DashboardBrowserCollapsed = true;
        s.MotionLevel = MotionLevel.Off;   // no ease: the settle is immediate
        s.PerformanceMode = false;
        s.OfflineMode = false;
        var opened = new List<string>();
        MainShellWindow.BillboardOpenUrl = opened.Add;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            var dash = shell.FindControl<SettingsTabView>("SettingsTab")!;
            T C<T>(string n) where T : Control => dash.FindControl<T>(n)!;
            var column = C<Grid>("BrowserColumn");
            var body = C<Border>("BrowserFoldBody");
            var billboard = C<Border>("DashBillboard");
            string Title() => C<TextBlock>("BillboardTitle").Text!;

            // Launch folded (the default): header only, billboard in the freed row, no clock at Motion Off.
            Assert.False(body.IsVisible);
            Assert.True(billboard.IsVisible);
            Assert.Equal("▾", C<TextBlock>("TxtFoldBrowser").Text);
            Assert.True(column.RowDefinitions[0].Height.IsAuto);
            Assert.True(column.RowDefinitions[1].Height.IsStar);
            Assert.Equal(Loc.Get("tooltip_browser_unfold"), ToolTip.GetTip(C<Button>("BtnFoldBrowser")));
            Assert.Equal(Loc.Get(DashboardBillboard.CardAt(0).TitleKey), Title());
            Assert.NotNull(C<Image>("BillboardCover").Source);   // the poster resource is linked
            Assert.Equal(DashboardBillboard.Roster.Count, C<StackPanel>("BillboardDots").Children.Count);
            Assert.False(shell.BillboardClockRunning);

            // Motion back to Full: the clock arms; a tick advances one slide.
            s.MotionLevel = MotionLevel.Full;
            global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(shell.BillboardClockRunning);
            shell.BillboardTick();
            Assert.Equal(1, shell.BillboardIndex);
            Assert.Equal(Loc.Get(DashboardBillboard.CardAt(1).TitleKey), Title());
            Assert.True(((RadioButton)C<StackPanel>("BillboardDots").Children[1]).IsChecked);

            // Another tab: the clock stops (P01); Home again: it re-arms.
            shell.ShowTab("quests");
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.BillboardClockRunning);
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.BillboardClockRunning);

            // Pause: no clock and a tick is refused; manual navigation still works.
            Click(C<Button>("BillboardPause"));
            Assert.False(shell.BillboardClockRunning);
            Assert.Equal("▶", C<TextBlock>("TxtBillboardPause").Text);
            shell.BillboardTick();
            Assert.Equal(1, shell.BillboardIndex);
            Click(C<Button>("BillboardPrevious"));
            Assert.Equal(0, shell.BillboardIndex);
            Click((RadioButton)C<StackPanel>("BillboardDots").Children[4]);   // "exclusives", a Tab card
            Assert.Equal(4, shell.BillboardIndex);
            Click(C<Button>("BillboardCard"));
            Assert.Equal("exclusives", shell.CurrentTab);
            shell.ShowTab("settings");
            Click(C<Button>("BillboardNext"));                                 // "support", a Link card
            Click(C<Button>("BillboardCard"));
            Assert.Equal(new[] { DashboardBillboard.PatreonUrl }, opened);

            // The chevron writes the preference: open card, billboard gone, clock stopped. Motion Off
            // so the fold settles at once (at Full the 180 ms ease keeps the body hidden until it lands).
            s.MotionLevel = MotionLevel.Off;
            Click(C<Button>("BtnFoldBrowser"));
            Dispatcher.UIThread.RunJobs();
            Assert.False(s.DashboardBrowserCollapsed);
            Assert.True(body.IsVisible);
            Assert.False(billboard.IsVisible);
            Assert.False(shell.BillboardClockRunning);
            Assert.Equal("▴", C<TextBlock>("TxtFoldBrowser").Text);
            Assert.True(column.RowDefinitions[0].Height.IsStar);
            Assert.Equal(0, column.RowDefinitions[1].Height.Value);

            // Fold again, then the app calls the browser (reload): it opens for this run only.
            Click(C<Button>("BtnFoldBrowser"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(s.DashboardBrowserCollapsed);
            Assert.False(body.IsVisible);
            Click(C<Button>("BtnReloadBrowser"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(body.IsVisible);
            Assert.True(s.DashboardBrowserCollapsed);   // a reveal is not a preference
            Assert.False(shell.BrowserFolded);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            MainShellWindow.BillboardOpenUrl = oldOpen;
            s.DashboardBrowserCollapsed = true;
            s.MotionLevel = MotionLevel.Full;
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });

    /// <summary>A normal launch: folded at Full motion, the clock runs once the window is shown,
    /// with no motion-gate event behind it (review P1).</summary>
    [Fact]
    public Task FoldedLaunchAtFullMotionStartsTheClock() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.DashboardBrowserCollapsed = true;
        s.MotionLevel = MotionLevel.Full;
        s.PerformanceMode = false;
        var shell = new MainShellWindow();
        try
        {
            Assert.False(shell.BillboardClockRunning);   // not on screen yet
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<Border>("DashBillboard")!.IsEffectivelyVisible);
            Assert.True(shell.BillboardClockRunning);
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.BillboardClockRunning);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}
