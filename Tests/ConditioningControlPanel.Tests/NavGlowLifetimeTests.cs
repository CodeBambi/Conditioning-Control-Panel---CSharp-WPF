using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Polish 12 round 2 (owner desk pass, 2026-10-07): "Going back Home gets us this" - a lilac ring
/// over the dashboard tiles. Choosing Dashboard on Home &gt; Premium rang the Dashboard pill
/// (NavGlow, an adorner), then the strip collapsed for the dashboard. An adorner layer keeps drawing
/// an adorner whose element was collapsed, at the element's last spot, so the ring hung over the
/// mosaic. The glow now leaves with its target.
/// </summary>
public class NavGlowLifetimeTests
{
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    [Fact]
    public void A_glow_leaves_when_its_target_is_collapsed()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var pill = new Button { Width = 140, Height = 38, Content = "Dashboard" };
            var row = new StackPanel();
            row.Children.Add(pill);
            var window = new Window
            {
                Left = -10000, Top = -10000, Width = 300, Height = 120,
                ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None,
                Content = new AdornerDecorator { Child = row },
            };
            try
            {
                window.Show();
                Pump();
                Assert.True(NavGlow.Once(pill, Colors.MediumPurple, MotionLevel.Full, "test"));
                var layer = AdornerLayer.GetAdornerLayer(pill)!;
                Assert.NotNull(layer.GetAdorners(pill));

                // The strip collapses (Home's dashboard draws no header): the ring must go too.
                row.Visibility = Visibility.Collapsed;
                Pump();
                Assert.Null(layer.GetAdorners(pill));

                // A target already gone draws nothing at all.
                Assert.False(NavGlow.Once(pill, Colors.MediumPurple, MotionLevel.Full, "test"));
                Assert.Null(layer.GetAdorners(pill));
            }
            finally { window.Close(); }
        });
    }
}
