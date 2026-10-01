using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Super switch builds offscreen and reads LOCKED with no account (App.Patreon null).</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class SuperSwitchRenderTests
{
    private static void Realize(FrameworkElement element, double width = 240, double height = 180)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();
    }

    [Fact]
    public void Switch_is_a_54_by_30_pill_and_locked_without_an_account()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            if (Environment.GetEnvironmentVariable("CCP_SUPER_ALL") == "1") return;
            var sw = new SuperSwitch { Effect = SuperEffect.Vortex };
            Realize(sw, 100, 60);
            Assert.Equal(54, sw.ActualWidth);
            Assert.Equal(30, sw.ActualHeight);
            Assert.True(sw.IsLockedNow);
            Assert.False(SuperAccess.IsOn(SuperEffect.Vortex));
        });
    }

    private static System.Collections.Generic.List<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var found = new System.Collections.Generic.List<T>();
        void Walk(DependencyObject d)
        {
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                if (c is T t) found.Add(t);
                Walk(c);
            }
        }
        Walk(root);
        return found;
    }
}
