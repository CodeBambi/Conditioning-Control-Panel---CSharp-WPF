using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Exclusives.cs motion + chrome: tier plates, livery, teasers, breath, pulse,
/// Ken Burns and sheens, reached through the shell's ShowTab and parked when the tab hides (P01).</summary>
public sealed class ExclusivesVaultMotionTests
{
    private static readonly SteppedClock Clock = new();

    [Fact]
    public Task VaultChromeAndMotionFollowTheTabAndTheGates() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.MotionLevel, s.PerformanceMode);
        var (premium, lab, free) = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider);
        FxAdorner.Time = Clock;
        MainShellWindow? shell = null;
        try
        {
            (s.MotionLevel, s.PerformanceMode) = (MotionLevel.Full, false);
            CoreEntitlement.HasPremiumProvider = null;
            CoreEntitlement.HasLabProvider = null;
            CoreEntitlement.IsFreeTodayProvider = null;
            shell = new MainShellWindow { Width = 1400, Height = 900 };
            shell.Show(); Settle();
            shell.ShowTab("exclusives"); Settle();
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            Assert.True(view.MotionRunning);

            // Free account: both tier plates dim, none breathes.
            var p1 = view.FindControl<Border>("TierPlate1")!;
            Assert.Null(view.LitPlate);
            Assert.Equal(0.3, p1.Opacity);
            Step(view, 1.7);

            // Two reserved teasers whose "?" breathes; tiered cards wear the 3px living livery.
            var items = view.FindControl<ItemsControl>("ExclusivesShelf")!.Items;
            Assert.Equal(2, items.OfType<ExclusiveTeaserRow>().Count());
            var mark = Parts(view, "TeaserMark").First();
            Assert.InRange(((DropShadowEffect)mark.Effect!).Opacity, 0.6, 0.65);
            var tiered = Parts(view, "ExCard").Cast<Border>().First(b => ((ExclusiveCardRow)b.DataContext!).Tier > 0);
            Assert.True(TierFxBorder.GetTier(tiered) > 0);
            Assert.Equal(new Thickness(3), tiered.BorderThickness);
            Assert.Equal(ExclusiveFeature.All[0].Tier, TierFxBorder.GetTier(view.FindControl<Border>("SpotlightCard")!));
            Assert.True(view.SheenCount > 0);

            // FREE TODAY pulse (fade + swell). No roster entry reaches the untiered gold pill today (fyp,
            // the spotlight, re-stamps its badge instead), so the hero pill is shown by hand.
            var pill = view.FindControl<Border>("SpotFreeToday")!;
            pill.IsVisible = true;
            Step(view, 1.7);
            Assert.True(pill.Opacity < 1);
            Assert.True(((ScaleTransform)pill.RenderTransform!).ScaleX > 1);

            // Another tab: every loop parks and the room rests.
            shell.ShowTab("settings"); Settle();
            Assert.False(view.MotionRunning);
            Assert.Equal(0, view.SheenCount);
            Assert.Null(mark.Effect);
            Assert.Equal(1, pill.Opacity);
            Assert.Equal(1, ((ScaleTransform)pill.RenderTransform!).ScaleX);

            // Back with Basic access: plate I lights and breathes 0.75..1.0.
            CoreEntitlement.HasPremiumProvider = () => true;
            shell.ShowTab("exclusives"); Settle();
            Assert.True(view.MotionRunning);
            Assert.Same(p1, view.LitPlate);
            Assert.Equal(0.3, view.FindControl<Border>("TierPlate2")!.Opacity);
            Step(view, 3.4 * 2 + 1.7);
            Assert.InRange(p1.Opacity, 0.87, 0.88);

            // Reduced motion: no clock, the lit plate rests at full.
            s.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(view.MotionRunning);
            Assert.Equal(1, p1.Opacity);

            // The Lab bar lights plate II instead.
            CoreEntitlement.HasLabProvider = () => true;
            shell.RefreshExclusivesTab();
            Assert.Same(view.FindControl<Border>("TierPlate2"), view.LitPlate);
            Assert.Equal(0.55, p1.Opacity);
        }
        finally
        {
            shell?.Close();
            FxAdorner.Time = TimeProvider.System;
            (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider) = (premium, lab, free);
            (s.MotionLevel, s.PerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void BreathIsWpfSineEaseAutoReverse()
    {
        Assert.Equal(0.35, ExclusivesTabView.Breath(0, 3.4, 0.35, 0.9), 6);
        Assert.Equal(0.9, ExclusivesTabView.Breath(3.4, 3.4, 0.35, 0.9), 6);
        Assert.Equal(0.625, ExclusivesTabView.Breath(1.7, 3.4, 0.35, 0.9), 6);
        Assert.Equal(0.35, ExclusivesTabView.Breath(6.8, 3.4, 0.35, 0.9), 6);
        Assert.Equal(0.35 + 0.55 * (1 - Math.Cos(Math.PI / 4)) / 2, ExclusivesTabView.Breath(0.85, 3.4, 0.35, 0.9), 6);   // eased, not linear
    }

    private static Control[] Parts(Control root, string name) =>
        root.GetVisualDescendants().OfType<Control>().Where(c => c.Name == name).ToArray();

    private static void Step(ExclusivesTabView view, double seconds)
    {
        Clock.Now += (long)(seconds * TimeSpan.TicksPerSecond);
        view.MotionFrame();
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
