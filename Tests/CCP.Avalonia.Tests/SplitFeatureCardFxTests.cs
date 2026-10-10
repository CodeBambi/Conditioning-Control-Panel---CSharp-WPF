using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Split dashboard tile (WPF Features/SplitFeatureCard.xaml.cs): the breath, glow and seam sweep follow
/// MotionFx/PerformanceProfile (:433-441, :656-698), off halves dim (:713-718) and each half carries its "?" (:403-430).
/// Driven from the shell's dashboard with real pointer input.</summary>
public sealed class SplitFeatureCardFxTests
{
    private static T F<T>(object o, string name) =>
        (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;

    [Fact]
    public Task MotionAndTierGateTheBreathSweepAndHalves() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (motion, perf, wipe, drain) = (s.MotionLevel, s.PerformanceMode, s.MindWipeEnabled, s.BrainDrainEnabled);
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            var dash = shell.SettingsPage!;
            var card = dash.ComboMindDrain;
            void Reshow() { dash.IsVisible = false; dash.IsVisible = true; Dispatcher.UIThread.RunJobs(); }
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            s.BrainDrainEnabled = false;
            s.MindWipeEnabled = true;
            Dispatcher.UIThread.RunJobs();
            Reshow();

            Assert.True(card.IsActiveA);
            Assert.True(F<ConditioningControlPanel.Avalonia.Views.Features.BreathClock?>(card, "_breathClock")?.IsRunning == true);
            Assert.Equal(1.0, F<Panel>(card, "_halfA").Opacity);
            Assert.Equal(0.62, F<Panel>(card, "_halfB").Opacity);           // the off half rests dim
            Assert.True(F<Button>(card, "_btnHelpA").IsVisible, "MindWipe has help content");
            Assert.True(F<Button>(card, "_btnHelpB").IsVisible, "BrainDrain has help content");

            // Reduced: no ambient loop, glow parked at its peak.
            s.MotionLevel = MotionLevel.Reduced;
            Reshow();
            Assert.False(F<ConditioningControlPanel.Avalonia.Views.Features.BreathClock?>(card, "_breathClock")?.IsRunning == true);
            Assert.Equal(0.90, F<Border>(card, "_activeGlow").Opacity, 3);

            // Performance tier: no glow at all.
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = true;
            Reshow();
            Assert.False(F<ConditioningControlPanel.Avalonia.Views.Features.BreathClock?>(card, "_breathClock")?.IsRunning == true);
            Assert.Equal(0, F<Border>(card, "_activeGlow").Opacity);

            // Sweep gated off: hovering the OFF half B lands the seam on its end state at once, A's "?" steps aside.
            shell.UpdateLayout();
            // Headless runs no transitions, so the gate is observed as the transition being detached (Snap).
            int snaps = 0;
            card.PropertyChanged += (_, e) => { if (e.Property == global::Avalonia.Animation.Animatable.TransitionsProperty) snaps++; };
            var inB = card.TranslatePoint(new Point(card.Bounds.Width - 12, card.Bounds.Height - 12), shell)!.Value;
            shell.MouseMove(inB);
            Dispatcher.UIThread.RunJobs();
            var progress = typeof(SplitFeatureCard).GetField("SplitProgressProperty", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null) as StyledProperty<double>;
            Assert.Equal(0.26, card.GetValue(progress!), 3);
            Assert.True(snaps > 0, "gated: the seam lands with the transition detached");
            Assert.Equal(0, F<Button>(card, "_btnHelpA").Opacity);
            Assert.Equal(0.62, F<Panel>(card, "_halfB").Opacity);           // dashboard: hover does not undim

            // Full motion: the sweep animates instead of snapping.
            shell.MouseMove(card.TranslatePoint(new Point(-20, -20), shell)!.Value);
            Assert.Null(F<bool?>(card, "_halfHover"));
            Dispatcher.UIThread.RunJobs();
            s.PerformanceMode = false;
            int snapped = snaps;
            shell.MouseMove(inB);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(snapped, snaps);                                     // the sweep keeps its transition
        }
        finally
        {
            shell.Close();
            (s.MotionLevel, s.PerformanceMode, s.MindWipeEnabled, s.BrainDrainEnabled) = (motion, perf, wipe, drain);
        }
        return Task.CompletedTask;
    });
}
