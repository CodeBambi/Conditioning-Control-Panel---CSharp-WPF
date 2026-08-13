using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Velvet Vault's chrome: what a shelf card wears, and what changes on the one day a feature is
/// the daily free unlock. The vault had no render suite before the tier-livery pass (2026-08-13).
///
/// <para><b>Why the assertions run against <see cref="VaultLivery"/> rather than a built card.</b>
/// <c>MainWindow.BuildExclusiveCard</c> needs a live window, which a render harness cannot supply.
/// The chrome DECISION was pulled out into VaultLivery precisely so it could be checked here, and
/// the builder now calls nothing else - so this suite covers the real path, not a copy of it. What
/// it deliberately cannot cover is the assembly (which child sits above which); the tier badge's
/// own behaviour is TierBadgeRenderTests.</para>
///
/// <para><b>The failure this exists to catch</b> is a tiered card quietly falling back to the
/// untiered hairline - the rim, the sign and the re-stamp are three separate writes, and any one of
/// them going missing still compiles, still renders, and just silently stops charging for the
/// feature.</para>
///
/// <para><b>And its mirror, after the mod-aware sweep merged:</b> the untiered edge now follows the
/// active mod, the livery still must not. A tiered card that came back wearing the mod accent would
/// mean the tier mark had started taking colour from the mod chain, which is the one thing commerce
/// chrome may never do.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class ExclusivesRenderTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    /// <summary>
    /// Stands in for the mod-derived resting edge the vault passes in
    /// (<c>MainWindow.Exclusives.cs</c>'s <c>ExclusiveEdgeDefault()</c>). Deliberately NOT the
    /// shipped violet: these tests assert that a tiered card overwrites the untiered edge, and an
    /// arbitrary colour makes that assertion honest on any mod, not just Bambi.
    /// </summary>
    private static readonly SolidColorBrush ModEdge = Frozen(Color.FromArgb(0x4D, 0x33, 0xCC, 0x88));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Border Card() => new()
    {
        Width = 336,
        Height = 200,
        CornerRadius = new CornerRadius(12),
        BorderThickness = new Thickness(1),
        BorderBrush = ModEdge,
    };

    private static void Realize(FrameworkElement element, double width = 336, double height = 200)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();
    }

    // =====================================================================================
    //  the registry
    // =====================================================================================

    [Fact]
    public void EveryFeatureTheDailyWheelCanLandOnCarriesALivery()
    {
        // The re-stamp only exists on a card with a tier sign to stamp over. A pool feature with
        // Tier 0 would fall back to the old text pill on its one day of the year and nobody would
        // notice until that day came round.
        var untiered = ExclusiveFeature.All
            .Where(f => f.DailyFreeKey != null && f.Tier <= 0)
            .Select(f => f.Key)
            .ToList();

        Assert.True(untiered.Count == 0,
            "daily-free pool features with no livery: " + string.Join(", ", untiered));
    }

    [Fact]
    public void TheWeeklyPassFeatureIsTheOneShelfCardWithNoPrice()
    {
        // Graded Intake is legitimately open to a free account holding an unspent pass, so a tier
        // badge on it would be the shelf telling a small lie about the one door not sold by tier.
        var intake = ExclusiveFeature.All.Single(f => f.Key == "gradedintake");
        Assert.Equal(0, intake.Tier);
        Assert.Null(intake.DailyFreeKey);
    }

    [Fact]
    public void NoShelfCardClaimsATierTheLiveryCannotPaint()
    {
        foreach (var feature in ExclusiveFeature.All)
            Assert.InRange(feature.Tier, 0, 2);
    }

    // =====================================================================================
    //  the rim
    // =====================================================================================

    [Fact]
    public void ATieredCardWearsTheSharedLiveryMetalAndAHeavierRim()
    {
        OnStaThread(() =>
        {
            var card = Card();
            var badge = new TierBadge { MotionOverride = false };
            Realize(new Grid { Children = { card, badge } });

            var pill = VaultLivery.Apply(card, badge, tier: 1, freeToday: false, ModEdge);

            // Reference equality with the theme brush, not a colour match: the point of a livery is
            // that the vault wears the SAME metal as the Play wall and the Studio rack, and a
            // hand-mixed lookalike would sail through a colour check.
            Assert.Same(Application.Current!.TryFindResource("Tier1GoldBorderBrush"), card.BorderBrush);
            // ...and it does NOT wear the mod's accent, which is what it was handed as the untiered
            // resting edge. Tier livery is commerce chrome: constant across mods, always.
            Assert.NotSame(ModEdge, card.BorderBrush);
            Assert.Equal(VaultLivery.CardRim, card.BorderThickness.Left, 3);
            Assert.Equal(1, TierFxBorder.GetTier(card));
            // The band must ride the same weight as the stroke or it misses the metal entirely.
            Assert.Equal(card.BorderThickness.Left, TierFxBorder.GetRimThickness(card), 3);
            Assert.False(pill, "a tiered card must not also ask for the old gold pill");
        });
    }

    [Fact]
    public void TheSpotlightWearsTheHeroWeightOfTheSameLivery()
    {
        OnStaThread(() =>
        {
            var band = Card();
            var badge = new TierBadge { MotionOverride = false };
            Realize(new Grid { Children = { band, badge } });

            VaultLivery.Apply(band, badge, tier: 1, freeToday: false,
                              ModEdge, rim: VaultLivery.SpotlightRim);

            Assert.Equal(VaultLivery.SpotlightRim, band.BorderThickness.Left, 3);
            Assert.True(VaultLivery.SpotlightRim > VaultLivery.CardRim,
                "the hero band must out-weigh the cards below it");
        });
    }

    [Fact]
    public void AnUntieredCardKeepsTheHairlineAndSurrendersTheBand()
    {
        OnStaThread(() =>
        {
            var card = Card();
            var pill = VaultLivery.Apply(card, badge: null, tier: 0, freeToday: false, ModEdge);

            Assert.Same(ModEdge, card.BorderBrush);
            Assert.Equal(1, card.BorderThickness.Left, 3);
            Assert.Equal(0, TierFxBorder.GetTier(card));
            Assert.False(pill);
        });
    }

    // =====================================================================================
    //  the free day
    // =====================================================================================

    [Fact]
    public void AFreeDayOnATieredCardIsARestampAndNotAPill()
    {
        OnStaThread(() =>
        {
            var card = Card();
            var badge = new TierBadge { MotionOverride = false };
            Realize(new Grid { Children = { card, badge } });

            var pill = VaultLivery.Apply(card, badge, tier: 1, freeToday: true, ModEdge);

            Assert.False(pill, "the tiered card asked for the pill it was supposed to replace");
            Assert.True(badge.FreeToday);
            Assert.Equal(Visibility.Visible, badge.StampImage.Visibility);

            // And the livery does NOT change colour for the day: a rim that turns gold for
            // twenty-four hours is no longer a livery, it is a state.
            Assert.Same(Application.Current!.TryFindResource("Tier1GoldBorderBrush"), card.BorderBrush);
            Assert.NotSame(VaultLivery.EdgeFree, card.BorderBrush);
        });
    }

    [Fact]
    public void AFreeDayOnAnUntieredSurfaceStillGetsTheGoldPill()
    {
        OnStaThread(() =>
        {
            // Graded Intake shape: no sign to stamp over, so the pill is still the only way this
            // surface can say "open today".
            var card = Card();
            var pill = VaultLivery.Apply(card, badge: null, tier: 0, freeToday: true, ModEdge);

            Assert.True(pill);
            Assert.Same(VaultLivery.EdgeFree, card.BorderBrush);
            Assert.Equal(2, card.BorderThickness.Left, 3);
        });
    }

    [Fact]
    public void ComingBackOffAFreeDayTakesTheStampWithIt()
    {
        OnStaThread(() =>
        {
            var card = Card();
            var badge = new TierBadge { MotionOverride = false };
            Realize(new Grid { Children = { card, badge } });

            VaultLivery.Apply(card, badge, tier: 2, freeToday: true, ModEdge);
            VaultLivery.Apply(card, badge, tier: 2, freeToday: false, ModEdge);

            Assert.False(badge.FreeToday);
            Assert.Equal(Visibility.Collapsed, badge.StampImage.Visibility);
            Assert.Equal(1.0, badge.TierImage.Opacity, 3);
            Assert.Same(Application.Current!.TryFindResource("Tier2DiamondBorderBrush"), card.BorderBrush);
        });
    }

    // =====================================================================================
    //  the layout promise
    // =====================================================================================

    [Fact]
    public void TheEntitlementChipClearsTheBadgeItSitsUnder()
    {
        OnStaThread(() =>
        {
            // Both live in the card's top-right corner, and the badge is TILTED - so the box the
            // chip has to clear is the ROTATED one, which is ~10px taller than the art's own
            // height. This is the check that turns a re-cut of the badge art into a build failure
            // instead of a chip sitting on a neon sign.
            foreach (var tier in new[] { 1, 2 })
            {
                var badge = new TierBadge { MotionOverride = false };
                badge.Tier = tier;
                Realize(badge);

                double w = badge.TierImage.DesiredSize.Width;
                double h = badge.TierImage.DesiredSize.Height;
                Assert.True(h > 0, $"tier {tier} badge measured to nothing - is the art missing?");

                // Static lean plus the 1.2 degree wobble: the worst case the badge ever occupies.
                double theta = (Math.Abs(badge.TierTilt) + 1.2) * Math.PI / 180.0;
                double rotated = (w * Math.Sin(theta)) + (h * Math.Cos(theta));
                double bottom = VaultLivery.CardBadgeTopMargin + h + ((rotated - h) / 2.0);

                Assert.True(VaultLivery.ChipTopWhenTiered >= bottom,
                    $"tier {tier} badge reaches {bottom:F1}px down the card, but the chip starts at "
                    + $"{VaultLivery.ChipTopWhenTiered}px");
            }
        });
    }
}
