using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using System.Linq;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>User bug: after leaving Play its rabbit-hole ember loop kept showing through other
/// pages. WPF's IsVisible folds in the ancestors (AmbientFxCanvas.cs:601/664); Avalonia's does
/// not, so the canvas on the hidden tab kept ticking and invalidating. Leaving the tab must stop
/// its clock and coming back must restart it.</summary>
public sealed class PlaySpiralBleedTests
{
    [Fact]
    public async Task LeavingPlayStopsItsAmbientLoop()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var w = new MainShellWindow();
            w.Show();
            w.Activate();
            Dispatcher.UIThread.RunJobs();

            w.ShowTab("play");
            Dispatcher.UIThread.RunJobs();
            var play = w.Named<global::ConditioningControlPanel.Avalonia.Views.Tabs.PlayTabView>("PlayTab")!;
            var fx = play.FindControl<AmbientFxCanvas>("RabbitHoleFx")!;
            var badge = play.FindControl<TierBadge>("PlayBadgeDtrh")!;
            Assert.True(fx.IsTicking, "Play's ember loop never started - the test proves nothing");
            Assert.True(badge.IsAnimating, "Play's tier badge never animated - the test proves nothing");
            // The lapping rim ("the spiral around the items") lives on the WINDOW's AdornerLayer.
            var rims = w.GetVisualDescendants().OfType<TierFxBorderAdorner>().Where(a => a.IsVisible).ToList();
            Assert.NotEmpty(rims);
            Assert.All(rims, r => Assert.True(r.IsAnimating, "a Play rim never lapped - the test proves nothing"));

            w.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.False(fx.IsEffectivelyVisible);
            Assert.False(fx.IsTicking, "Play's ember loop kept running on another tab");
            Assert.False(badge.IsAnimating, "Play's tier badge kept animating on another tab");
            Assert.All(rims, r => Assert.False(r.IsVisible, "a Play rim still draws over another tab"));
            Assert.All(rims, r => Assert.False(r.IsAnimating, "a Play rim kept lapping on another tab"));

            w.ShowTab("play");
            Dispatcher.UIThread.RunJobs();
            Assert.True(fx.IsTicking, "coming back to Play did not restart its loop");
            Assert.True(badge.IsAnimating, "coming back to Play did not restart its badge");
            Assert.All(rims, r => Assert.True(r.IsVisible && r.IsAnimating, "coming back to Play did not restore a rim"));
            w.Close();
            return Task.CompletedTask;
        });
    }
}
