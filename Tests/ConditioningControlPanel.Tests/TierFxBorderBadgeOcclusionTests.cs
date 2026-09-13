using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The tier livery band is an adorner, and an adorner paints over everything inside the card it
/// decorates - including the tier sign pinned on that card's corner. Owner screenshot (2026-09-13):
/// the gold band on "The Collection" cutting straight through BASIC SUBJECT. The band now clips a
/// hole for every visible <see cref="TierBadge"/>; these pin where that hole is.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class TierFxBorderBadgeOcclusionTests
{
    private static (Border Card, TierBadge? Badge, TierFxBorderAdorner Band) Build(int tier, bool withBadge, bool freeToday = false)
    {
        var inner = new Grid();
        TierBadge? badge = null;
        if (withBadge)
        {
            // Shelf-card posture (MainWindow.Exclusives.cs): top-right, tucked over the corner.
            badge = new TierBadge
            {
                MotionOverride = false,
                Margin = new Thickness(0, -6, -6, 0),
            };
            badge.Tier = tier;
            badge.FreeToday = freeToday;
            inner.Children.Add(badge);
        }

        var card = new Border
        {
            Width = 336,
            Height = 200,
            Margin = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(3),
            BorderBrush = Brushes.Goldenrod,
            Child = inner,
        };
        var decorator = new AdornerDecorator { Child = new Grid { Children = { card } } };
        decorator.Measure(new Size(360, 224));
        decorator.Arrange(new Rect(0, 0, 360, 224));
        decorator.UpdateLayout();

        var band = new TierFxBorderAdorner(card, tier, 12, 3);
        AdornerLayer.GetAdornerLayer(card)!.Add(band);
        decorator.UpdateLayout();
        return (card, badge, band);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TheBandSkipsTheSignButKeepsTheRestOfTheRim(int tier)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var (card, badge, band) = Build(tier, withBadge: true);
            var clip = band.BadgeOccluder(card.RenderSize);
            Assert.NotNull(clip);

            // Where the top rim runs under the sign (the art spans roughly x 191..342 on a 336 card).
            var image = badge!.TierImage;
            var mid = image.TransformToAncestor(card).Transform(
                new Point(image.RenderSize.Width * 0.5, image.RenderSize.Height * 0.12));
            Assert.False(clip!.FillContains(new Point(mid.X, 1.5)),
                $"tier {tier}: the band still paints the top rim under the sign at x={mid.X:F0}");
            Assert.False(clip.FillContains(new Point(334.5, 30)),
                $"tier {tier}: the band still paints the right rim under the sign");

            // ...and everywhere the sign is not, the band still laps.
            Assert.True(clip.FillContains(new Point(40, 1.5)), "top-left rim lost its band");
            Assert.True(clip.FillContains(new Point(334.5, 180)), "bottom-right rim lost its band");
            Assert.True(clip.FillContains(new Point(1.5, 100)), "left rim lost its band");
        });
    }

    [Fact]
    public void TheRestampedSignIsStillCovered()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var (card, _, band) = Build(1, withBadge: true, freeToday: true);
            var clip = band.BadgeOccluder(card.RenderSize);
            Assert.NotNull(clip);
            Assert.False(clip!.FillContains(new Point(334.5, 30)));
        });
    }

    [Fact]
    public void ACardWithNoSignIsNotClipped()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var (card, _, band) = Build(1, withBadge: false);
            Assert.Null(band.BadgeOccluder(card.RenderSize));
        });
    }
}
