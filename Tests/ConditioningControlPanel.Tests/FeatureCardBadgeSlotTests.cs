using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Where a tile's marks sit (tier price tag, "v2" ownership pill) versus the one CONTROL it
/// carries (the "?" help chip).
///
/// <para><b>Why this suite exists.</b> The v2 pill shipped pinned to the top-RIGHT corner at a
/// hand-counted 34px inset, which is the help chip's corner. Two testers read the result as one
/// crowded cluster within a day of the pre-release (Wobberjockey's screenshot, and Tock's "V2
/// showing under the LED indicator", tier2 2026-09-19). Nothing about that is visible in a diff:
/// the two margins are both legal numbers, and only their sum says whether the pills touch.</para>
///
/// <para>The rule now is a rule and not a number: the marks stack in the top-LEFT column and the
/// help chip owns the top-right, so they cannot reach each other at any tile size. These tests
/// measure the realized rectangles at every size the wall uses, so a future pill added to either
/// corner has to answer to them.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class FeatureCardBadgeSlotTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    /// <summary>
    /// The tile sizes the Home mosaic actually hands a FeatureCard: a 1x1 cell of the 4x4 grid on
    /// the fixed 1489x901 design canvas, the double-wide Vault cell, the 2x2 logo cell, and a
    /// deliberately cramped one for the day the canvas changes.
    /// </summary>
    public static TheoryData<double, double> TileSizes() => new()
    {
        { 212, 190 }, { 424, 190 }, { 424, 380 }, { 140, 120 },
    };

    private static void Realize(FrameworkElement element, double width, double height)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();
    }

    /// <summary>The element's box in the card's own coordinates. Visible elements only.</summary>
    private static Rect BoxIn(FrameworkElement element, Visual ancestor)
    {
        Assert.Equal(Visibility.Visible, element.Visibility);
        var origin = element.TransformToAncestor(ancestor).Transform(new Point(0, 0));
        return new Rect(origin, new Size(element.ActualWidth, element.ActualHeight));
    }

    private static FeatureCard Card(bool v2, string? tier, double w, double h)
    {
        var card = new FeatureCard
        {
            Title = "Bubble Pop",
            HelpSectionId = "BubblePop",
            ShowV2Badge = v2,
            TierBadge = tier,
        };
        Realize(card, w, h);
        return card;
    }

    // =====================================================================================
    //  the two corners never meet
    // =====================================================================================

    [Theory]
    [MemberData(nameof(TileSizes))]
    public void TheV2PillNeverTouchesTheHelpChip(double w, double h)
    {
        OnStaThread(() =>
        {
            var card = Card(v2: true, tier: null, w, h);

            var pill = BoxIn(card.V2BadgeHost, card);
            var help = BoxIn(card.BtnHelp, card);

            Assert.False(pill.IntersectsWith(help),
                $"{w}x{h}: the v2 pill {pill} lands on the help chip {help}");

            // Not merely "not overlapping": they must read as two separate things, which means
            // clear air between them. The gap the crowded build shipped with was 8px and still
            // read as one cluster, so the floor here is the pill's own height.
            var gap = help.Left - pill.Right;
            Assert.True(gap >= pill.Height,
                $"{w}x{h}: only {gap:0.#}px between the v2 pill and the help chip");
        });
    }

    [Theory]
    [MemberData(nameof(TileSizes))]
    public void TheV2PillAndThePriceTagStackInsteadOfOverlapping(double w, double h)
    {
        OnStaThread(() =>
        {
            var card = Card(v2: true, tier: "TIER 1", w, h);

            var tierTag = BoxIn(card.TierBadgeHost, card);
            var pill = BoxIn(card.V2BadgeHost, card);

            Assert.False(tierTag.IntersectsWith(pill),
                $"{w}x{h}: the price tag {tierTag} and the v2 pill {pill} overlap");
            Assert.True(pill.Top >= tierTag.Bottom,
                $"{w}x{h}: the v2 pill must hang UNDER the price tag, not beside it");
            Assert.Equal(tierTag.Left, pill.Left, 3);

            // And the chip is still on its own side of the card.
            Assert.False(pill.IntersectsWith(BoxIn(card.BtnHelp, card)));
            Assert.False(tierTag.IntersectsWith(BoxIn(card.BtnHelp, card)));
        });
    }

    [Theory]
    [MemberData(nameof(TileSizes))]
    public void EveryMarkStaysInsideTheTile(double w, double h)
    {
        OnStaThread(() =>
        {
            var card = Card(v2: true, tier: "LAB", w, h);
            var tile = new Rect(0, 0, w, h);

            foreach (var (name, box) in new[]
            {
                ("price tag", BoxIn(card.TierBadgeHost, card)),
                ("v2 pill", BoxIn(card.V2BadgeHost, card)),
                ("help chip", BoxIn(card.BtnHelp, card)),
            })
            {
                Assert.True(tile.Contains(box), $"{w}x{h}: the {name} {box} hangs off the tile");
            }
        });
    }

    // =====================================================================================
    //  the free FX tiles - the only two wearing the pill today
    // =====================================================================================

    [Fact]
    public void WithNoPriceTagTheV2PillTakesTheTierBadgesOwnSlot()
    {
        OnStaThread(() =>
        {
            // Flashes and Bubble Pop are free FX, so they carry no tier badge at all. A collapsed
            // element is not measured - margin included - so the pill must land exactly where the
            // price tag would, not 4px lower.
            var tiered = Card(v2: false, tier: "TIER 1", 212, 190);
            var free = Card(v2: true, tier: null, 212, 190);

            var tierTag = BoxIn(tiered.TierBadgeHost, tiered);
            var pill = BoxIn(free.V2BadgeHost, free);

            Assert.Equal(Visibility.Collapsed, free.TierBadgeHost.Visibility);
            Assert.Equal(tierTag.Left, pill.Left, 3);
            Assert.Equal(tierTag.Top, pill.Top, 3);
        });
    }

    [Fact]
    public void AnUnownedTileWearsNoPillAtAll()
    {
        OnStaThread(() =>
        {
            var card = Card(v2: false, tier: null, 212, 190);
            Assert.Equal(Visibility.Collapsed, card.V2BadgeHost.Visibility);
            Assert.Equal(Visibility.Collapsed, card.TierBadgeHost.Visibility);
            Assert.Equal(Visibility.Visible, card.BtnHelp.Visibility);
        });
    }

    // =====================================================================================
    //  the Studio rack, where the pill met the state dot
    // =====================================================================================

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    /// <summary>
    /// Tock's report, in the only place it could come from: the rack row's v2 pill and its state
    /// LED both resolved to <c>ColumnDefinitions.Count - 1</c>, which is the SAME column once the
    /// pill has added one - so the dot, added second, drew straight over the pill. Flash and
    /// Bubble Pop are the only two rows that carry both.
    /// </summary>
    [Fact]
    public void TheStudioRackPillAndTheStateLedDoNotShareAColumn()
    {
        OnStaThread(() =>
        {
            var page = new StudioTabView();
            Realize(page, 1209, 700);

            var v2Text = ConditioningControlPanel.Localization.Loc.Get("badge_v2");
            var rows = Descendants(page).OfType<Grid>()
                .Select(g => (Grid: g,
                              Pill: g.Children.OfType<Border>().FirstOrDefault(
                                  b => b.Child is TextBlock t && t.Text == v2Text),
                              Dot: g.Children.OfType<System.Windows.Shapes.Ellipse>().FirstOrDefault(
                                  e => Math.Abs(e.Width - 7) < 0.01)))
                .Where(r => r.Pill != null && r.Dot != null)
                .ToList();

            // Each of the two rows is built twice: a resting STRIP laid out in columns, and a
            // checked art TILE where the three marks are right-anchored over one picture. Both
            // shapes carry the pill and the dot, and each has its own way of keeping them apart.
            var strips = rows.Where(r => r.Grid.ColumnDefinitions.Count > 0).ToList();
            var tiles = rows.Where(r => r.Grid.ColumnDefinitions.Count == 0).ToList();

            // Flash and Bubble Pop. If the rack stops building either, this suite has stopped
            // watching the thing it was written for.
            Assert.True(strips.Count >= 2,
                $"expected the flash and bubbles rack strips to carry both marks, found {strips.Count}");
            Assert.True(tiles.Count >= 2,
                $"expected the flash and bubbles art tiles to carry both marks, found {tiles.Count}");

            foreach (var (_, pill, dot) in tiles)
            {
                // No columns here - the caption, the pill and the dot are three right-anchored
                // children, so the insets are the only thing holding them apart.
                Assert.True(pill!.Margin.Right >= dot!.Margin.Right + dot.Width,
                    $"the tile's v2 pill sits {pill.Margin.Right}px in, over a state LED at "
                    + $"{dot.Margin.Right}px");
            }

            foreach (var (grid, pill, dot) in strips)
            {
                var pillColumn = Grid.GetColumn(pill!);
                var dotColumn = Grid.GetColumn(dot!);
                Assert.True(pillColumn != dotColumn,
                    $"the v2 pill and the state LED are both in column {pillColumn}, "
                    + "so the dot draws over the pill");

                // Both columns have to exist, or the one past the end silently collapses onto
                // the last real one and we are back where we started.
                Assert.True(grid.ColumnDefinitions.Count > Math.Max(pillColumn, dotColumn),
                    $"a rack row declares {grid.ColumnDefinitions.Count} columns but places a mark "
                    + $"in column {Math.Max(pillColumn, dotColumn)}");
            }
        });
    }
}
