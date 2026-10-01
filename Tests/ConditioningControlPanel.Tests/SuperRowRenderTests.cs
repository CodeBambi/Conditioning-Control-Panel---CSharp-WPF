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

/// <summary>The Super strip inside a feature panel builds for every effect.</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class SuperRowRenderTests
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
    public void Panel_strip_builds_for_every_effect()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var e in Enum.GetValues<SuperEffect>())
            {
                var row = new SuperRow { Effect = e };
                Realize(row, 700, 90);
                row.Refresh();
                Assert.True(row.ActualHeight > 0);
            }
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
