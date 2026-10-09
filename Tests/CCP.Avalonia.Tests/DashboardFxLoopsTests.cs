using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.DashboardFx ApplyDashboardFxLoops (:171): window state and the
/// motion/performance gate re-run every dashboard card's RefreshFx and the vault CTA breath.</summary>
public sealed class DashboardFxLoopsTests
{
    [Fact]
    public Task DashboardLoopsFollowWindowStateMotionAndPerformance() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.MotionLevel = MotionLevel.Full;
        s.PerformanceMode = false;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            var dash = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var card = dash.FindControl<FeatureCard>("CardFlash")!;
            var combo = dash.FindControl<SplitFeatureCard>("ComboVideoBubble")!;
            card.IsActive = true;
            combo.IsActiveA = true;
            Dispatcher.UIThread.RunJobs();
            void AllBreathing(bool on)
            {
                Assert.Equal(on, card.IsBreathing);
                Assert.Equal(on, combo.IsBreathing);
                Assert.Equal(on, shell.VaultCtaBreathing);
            }
            AllBreathing(true);

            // Minimised: every loop parks; restored: they re-arm (the funnel's window hook).
            shell.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            AllBreathing(false);
            shell.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            AllBreathing(true);

            // The user path: Settings > Performance > Motion = Reduced, then back to Home.
            shell.ShowTab("appsettings");
            Dispatcher.UIThread.RunJobs();
            var perf = shell.GetLogicalDescendants().OfType<PerformanceSettingsSection>().First();
            perf.FindControl<ComboBox>("CmbMotionLevel")!.SelectedIndex = 1;
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(MotionLevel.Reduced, s.MotionLevel);
            AllBreathing(false);
            var glow = card.FindControl<Border>("GlowLayer")!;   // the glow is a sibling BoxShadow layer (AVALONIA EFFECT/CACHE RULE), not an Effect
            Assert.Equal(0.90, glow.Opacity, 3);                       // parked at PEAK, WPF :660
            Assert.Equal(1.0, card.FindControl<Border>("ActiveBorder")!.Opacity, 3);

            // A gate change while Home is on screen re-runs the funnel (Env.MotionGateChanged).
            s.MotionLevel = MotionLevel.Full;
            global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Dispatcher.UIThread.RunJobs();
            AllBreathing(true);

            // Performance mode raises the same gate (tier Performance forbids ambient loops).
            shell.ShowTab("appsettings");
            Dispatcher.UIThread.RunJobs();
            perf.FindControl<CheckBox>("ChkPerformanceMode")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(s.PerformanceMode);
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            AllBreathing(false);
            Assert.Equal(0.0, glow.Opacity, 3);                         // no glow on the Performance tier

        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}
