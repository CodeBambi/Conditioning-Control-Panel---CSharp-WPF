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
/// its clock and coming back must restart it. The ember canvas left with the descent hero (WPF
/// 2026-09-18 relayout); the tier badges and lapping rims are what still animate on Play.</summary>
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
            var badge = play.FindControl<TierBadge>("PlayBadgeRemote")!;
            Assert.True(badge.IsAnimating, "Play's tier badge never animated - the test proves nothing");
            // The lapping rim ("the spiral around the items") lives on the WINDOW's AdornerLayer.
            var rims = w.GetVisualDescendants().OfType<TierFxBorderAdorner>().Where(a => a.IsVisible).ToList();
            Assert.NotEmpty(rims);
            Assert.All(rims, r => Assert.True(r.IsAnimating, "a Play rim never lapped - the test proves nothing"));

            w.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.False(badge.IsAnimating, "Play's tier badge kept animating on another tab");
            Assert.All(rims, r => Assert.False(r.IsVisible, "a Play rim still draws over another tab"));
            Assert.All(rims, r => Assert.False(r.IsAnimating, "a Play rim kept lapping on another tab"));

            w.ShowTab("play");
            Dispatcher.UIThread.RunJobs();
            Assert.True(badge.IsAnimating, "coming back to Play did not restart its badge");
            Assert.All(rims, r => Assert.True(r.IsVisible && r.IsAnimating, "coming back to Play did not restore a rim"));
            w.Close();
            return Task.CompletedTask;
        });
    }

    /// <summary>The same hidden-ancestor gap in the other ambient surfaces: each must stop when a
    /// PARENT hides and start again when it comes back.</summary>
    [Fact]
    public async Task OtherAmbientSurfacesParkAndResumeWithTheirParent()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var vat = new VatGlassCanvas { Width = 200, Height = 200 };
            var orb = new TakeoverOrb { Width = 200, Height = 200 };
            var glyph = new SpiralGlyph { Width = 40, Height = 40 };
            var tab = new StackPanel { Children = { vat, orb, glyph } };
            var w = new Window { Content = new Panel { Children = { tab } }, Width = 600, Height = 600 };
            w.Show();
            w.Activate();
            orb.SetActive(true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vat.IsTicking && orb.IsRunning && glyph.IsBreathing, "a surface never started - the test proves nothing");

            tab.IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(vat.IsTicking, "vat kept ticking under a hidden parent");
            Assert.False(orb.IsRunning, "orb kept ticking under a hidden parent");
            Assert.False(glyph.IsBreathing, "spiral glyph kept breathing under a hidden parent");

            tab.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(vat.IsTicking, "vat did not resume");
            Assert.True(orb.IsRunning, "orb did not resume");
            Assert.True(glyph.IsBreathing, "spiral glyph did not resume");
            w.Close();
            return Task.CompletedTask;
        });
    }
}
