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

/// <summary>Dashboard tiles wear a Super switch only when their Super attribute names an effect.</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class SuperTileRenderTests
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
    public void Tiles_wear_the_switch_only_when_told()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var plain = new FeatureCard();
            Realize(plain);
            Assert.Empty(Descendants<SuperSwitch>(plain));

            var card = new FeatureCard { Super = "FlickerDeck" };
            Realize(card);
            var sw = Assert.Single(Descendants<SuperSwitch>(card));
            Assert.Equal(SuperEffect.FlickerDeck, sw.Effect);

            var split = new SplitFeatureCard { SuperA = "Vortex", SuperB = "Creep" };
            Realize(split);
            var pair = Descendants<SuperSwitch>(split).Select(s => s.Effect).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { SuperEffect.Vortex, SuperEffect.Creep }, pair);

            card.Super = null;
            Assert.Empty(Descendants<SuperSwitch>(card));
        });
    }

    [Fact]
    public void Unknown_names_parse_to_nothing()
    {
        Assert.Null(SuperTileBadge.Parse(null));
        Assert.Null(SuperTileBadge.Parse(""));
        Assert.Null(SuperTileBadge.Parse("Nope"));
        Assert.Equal(SuperEffect.Undertow, SuperTileBadge.Parse("undertow"));
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
