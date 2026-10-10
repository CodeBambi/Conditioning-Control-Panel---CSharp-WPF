using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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

/// <summary>
/// Main sync #6 Home layout (sync6-home-layout): the favorites drawer at the right edge
/// (WPF 11552cadf / e2c475a8a) and the browser fold arrow pill with the companion strip gone
/// (210e0e262), driven through the real shell from startup.
/// </summary>
public sealed class HomeLayoutSync6Tests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task FavoritesDrawerOpensFromItsHandleAndPeeksOnANewPin()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var favBefore = s.RailFavorites.ToList();
            bool openBefore = s.FavoritesDrawerOpen;
            var motionBefore = s.MotionLevel;
            MainShellWindow? w = null;
            try
            {
                s.RailFavorites.Clear();
                s.FavoritesDrawerOpen = false;
                s.MotionLevel = MotionLevel.Off;   // instant slides: the width is assertable
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var dash = w.Named<SettingsTabView>("SettingsTab")!;
                var body = dash.FindControl<Grid>("FavoritesDrawerBody")!;
                var handle = dash.FindControl<Button>("FavoritesDrawerHandle")!;

                // Closed by default; the handle sits in the last column and the old column 0 is gone.
                Assert.Equal(0, body.Width);
                Assert.Equal(3, Grid.GetColumn(dash.FindControl<Grid>("FavoritesDrawer")!));
                Assert.Equal("‹", dash.FindControl<TextBlock>("FavoritesDrawerChevron")!.Text);

                // The handle opens it and persists; a second click closes it again.
                handle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(SettingsTabView.FavoritesDrawerWidth, body.Width);
                Assert.True(s.FavoritesDrawerOpen);
                Assert.Equal("›", dash.FindControl<TextBlock>("FavoritesDrawerChevron")!.Text);
                handle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, body.Width);
                Assert.False(s.FavoritesDrawerOpen);

                // Pin from a RECENT chip's menu while closed (the rail rows that pinned left with
                // cd426fe36): the drawer slides out for a peek and writes nothing; the peek's end
                // puts it away.
                w.ShowTab("haptics");
                w.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                var row = (Button)dash.FindControl<StackPanel>("RecentList")!.Children[0];
                row.RaiseEvent(new ContextRequestedEventArgs());
                Dispatcher.UIThread.RunJobs();
                var item = Assert.IsType<MenuItem>(Assert.Single(row.ContextMenu!.Items));
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                row.ContextMenu.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("tab.haptics", s.RailFavorites);
                Assert.True(dash.FavoritesDrawerIsPeeking);
                Assert.Equal(SettingsTabView.FavoritesDrawerWidth, body.Width);
                Assert.False(s.FavoritesDrawerOpen);
                dash.FavoritesDrawerPeekElapsed();
                Dispatcher.UIThread.RunJobs();
                Assert.False(dash.FavoritesDrawerIsPeeking);
                Assert.Equal(0, body.Width);
            }
            finally
            {
                w?.Close();
                Dispatcher.UIThread.RunJobs();
                s.RailFavorites.Clear();
                s.RailFavorites.AddRange(favBefore);
                s.FavoritesDrawerOpen = openBefore;
                s.MotionLevel = motionBefore;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task FoldArrowIsALabelledPillThatBreathesOnlyWhileShut()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            bool foldBefore = s.DashboardBrowserCollapsed;
            var motionBefore = s.MotionLevel;
            bool perfBefore = s.PerformanceMode;
            MainShellWindow? w = null;
            try
            {
                s.DashboardBrowserCollapsed = true;
                s.MotionLevel = MotionLevel.Full;
                s.PerformanceMode = false;
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var dash = w.Named<SettingsTabView>("SettingsTab")!;
                var btn = dash.FindControl<Button>("BtnFoldBrowser")!;
                var label = dash.FindControl<TextBlock>("TxtFoldBrowserLabel")!;

                // The companion strip is gone (the rail has Companion).
                Assert.Null(dash.FindControl<Border>("CompanionStrip"));

                // Shut: "Show browser", a glow that breathes, in the header's star column.
                Assert.Equal(2, Grid.GetColumn(btn));
                Assert.Equal(Loc.Get("btn_browser_fold_show"), label.Text);
                Assert.Equal(Loc.Get("btn_browser_fold_show"), AutomationProperties.GetName(btn));
                var glow = Assert.IsType<DropShadowEffect>(btn.Effect);
                Assert.True(dash.FoldArrowBreathing);
                dash.StepFoldBreath();
                Assert.InRange(glow.Opacity, BrowserFoldRule.GlowLow, BrowserFoldRule.GlowHigh);

                // Hidden Home: no breath (P01).
                w.ShowTab("haptics");
                Dispatcher.UIThread.RunJobs();
                Assert.False(dash.FoldArrowBreathing);
                w.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                Assert.True(dash.FoldArrowBreathing);

                // Open it with the pill: "Hide browser", no glow.
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.False(s.DashboardBrowserCollapsed);
                Assert.Equal(Loc.Get("btn_browser_fold_hide"), label.Text);
                Assert.Null(btn.Effect);
                Assert.False(dash.FoldArrowBreathing);
            }
            finally
            {
                w?.Close();
                Dispatcher.UIThread.RunJobs();
                s.DashboardBrowserCollapsed = foldBefore;
                s.MotionLevel = motionBefore;
                s.PerformanceMode = perfBefore;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task DrawerJuiceRunsOnlyWhileHomeIsShownAtFullMotion()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var motionBefore = s.MotionLevel;
            bool perfBefore = s.PerformanceMode, foldBefore = s.DashboardBrowserCollapsed;
            MainShellWindow? w = null;
            try
            {
                s.MotionLevel = MotionLevel.Full;
                s.PerformanceMode = false;
                s.DashboardBrowserCollapsed = true;
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var dash = w.Named<SettingsTabView>("SettingsTab")!;
                var handle = dash.FindControl<Button>("FavoritesDrawerHandle")!;

                // e2c475a8a: the handle wears the mod's glow colour and a breathing glow.
                Assert.IsType<DropShadowEffect>(handle.Effect);
                var glow = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.GlowColor;
                var border = Assert.IsType<SolidColorBrush>(dash.FindControl<Grid>("FavoritesDrawer")!.Resources["FavHandleBorder"]);
                Assert.Equal(Color.FromArgb(0x80, glow.R, glow.G, glow.B), border.Color);
                Assert.True(dash.FavoritesDrawerBreathing);
                Assert.True(dash.FoldArrowBreathing);

                // Home hidden: both breaths park (P01); back on Home they run again.
                w.ShowTab("haptics");
                Dispatcher.UIThread.RunJobs();
                Assert.False(dash.FavoritesDrawerBreathing);
                Assert.False(dash.FoldArrowBreathing);
                w.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                Assert.True(dash.FavoritesDrawerBreathing);

                // Minimised: the ApplyDashboardFxLoops funnel parks them too.
                w.WindowState = WindowState.Minimized;
                Dispatcher.UIThread.RunJobs();
                Assert.False(dash.FavoritesDrawerBreathing);
                Assert.False(dash.FoldArrowBreathing);
                w.WindowState = WindowState.Normal;
                Dispatcher.UIThread.RunJobs();
                Assert.True(dash.FavoritesDrawerBreathing);
                Assert.True(dash.FoldArrowBreathing);

                // Motion Off: no glow at all.
                s.MotionLevel = MotionLevel.Off;
                ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Dispatcher.UIThread.RunJobs();
                Assert.Null(handle.Effect);
                Assert.False(dash.FavoritesDrawerBreathing);
            }
            finally
            {
                w?.Close();
                Dispatcher.UIThread.RunJobs();
                s.MotionLevel = motionBefore;
                s.PerformanceMode = perfBefore;
                s.DashboardBrowserCollapsed = foldBefore;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task WindowOpensAtTheWpfDefaultAndFitsUniformly()
    {
        // ce1159e23: 1661 x 1002 DIP; 30f656f78: a 1080p-tall work area shrinks both axes.
        var (w, h) = ConditioningControlPanel.Services.UI.WindowFitRule.FitPx(2000, 1200, 1920, 1040);
        Assert.Equal((1733, 1040), (w, h));
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var shell = new MainShellWindow();
            try
            {
                Assert.Equal(ConditioningControlPanel.Services.UI.WindowFitRule.DefaultWidthDip, shell.Width);
                Assert.Equal(ConditioningControlPanel.Services.UI.WindowFitRule.DefaultHeightDip, shell.Height);
            }
            finally { shell.Close(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>e2c475a8a: the drawer handle's motes include the Embers layer (WPF
    /// SettingsTabView.xaml.cs:260), sparks that fill in one per quarter second.</summary>
    [Fact]
    public async Task FavoritesDrawerHandleRaisesEmbers()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var motionBefore = s.MotionLevel;
            bool perfBefore = s.PerformanceMode;
            MainShellWindow? w = null;
            try
            {
                s.MotionLevel = MotionLevel.Full;
                s.PerformanceMode = false;
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var fx = w.Named<SettingsTabView>("SettingsTab")!
                    .FindControl<ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas>("FavoritesDrawerFx")!;
                Assert.True(fx.IsRunning);
                for (int i = 0; i < 8; i++) fx.StepEmbers(0.3f);
                Assert.True(fx.EmberCount > 0, $"embers {fx.EmberCount}");
            }
            finally
            {
                w?.Close();
                s.MotionLevel = motionBefore;
                s.PerformanceMode = perfBefore;
            }
            return Task.CompletedTask;
        });
    }
}
