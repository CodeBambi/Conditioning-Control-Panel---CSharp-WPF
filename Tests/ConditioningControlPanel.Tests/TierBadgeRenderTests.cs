using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Controls;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The stamped tier sign (owner, 2026-08-13) - the neon "BASIC SUBJECT" / "PRIME SUBJECT" plate a
/// tiered card wears on its art, and the FREE TODAY re-stamp that lands over it on a daily-free day.
///
/// <para><b>What can rot here, silently.</b> The badge is art plus five clocks, and every one of
/// them survives a clean compile if it stops running - or, far worse, if it keeps running when the
/// user has asked for no motion. Two failures this suite exists to catch:</para>
/// <list type="bullet">
/// <item>The reduced-motion path degrading to a SLOWER loop instead of a static badge. Nothing else
/// in the build would notice; the setting would simply stop meaning what it says.</item>
/// <item>The re-stamp coming apart - a dimmed tier sign with no stamp over it reads as a bug, and a
/// stamp over a full-brightness sign reads as two badges fighting.</item>
/// </list>
///
/// <para>The art itself is asserted too: these PNGs are reached through the csproj's
/// <c>Resources\features\*.png</c> glob, so a badge that silently stops loading is one build-file
/// edit away and looks like nothing at all in a diff.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class TierBadgeRenderTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    /// <summary>
    /// Builds a badge with the motion decision pinned BEFORE the tier is set: the tier setter is
    /// what applies the state and starts the clocks, so the override has to be in place first -
    /// exactly the ordering trap a caller could fall into too.
    /// </summary>
    private static TierBadge Badge(int tier, bool freeToday = false, bool motion = false)
    {
        var badge = new TierBadge { MotionOverride = motion };
        badge.Tier = tier;
        badge.FreeToday = freeToday;
        return badge;
    }

    private static void Realize(FrameworkElement element, double width = 336, double height = 200)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();
    }

    private static RotateTransform Rotate(Image image) =>
        Assert.IsType<RotateTransform>(((TransformGroup)image.RenderTransform).Children[1]);

    private static ScaleTransform Scale(Image image) =>
        Assert.IsType<ScaleTransform>(((TransformGroup)image.RenderTransform).Children[0]);

    // =====================================================================================
    //  the sign itself
    // =====================================================================================

    [Fact]
    public void BothTiersFindTheirArt()
    {
        OnStaThread(() =>
        {
            foreach (var tier in new[] { 1, 2 })
            {
                var badge = Badge(tier);
                Realize(badge);
                Assert.NotNull(badge.TierImage.Source);
                Assert.Equal(Visibility.Visible, badge.Visibility);
            }
        });
    }

    [Fact]
    public void TheTwoTiersLeanOppositeWaysAndWearDifferentSigns()
    {
        OnStaThread(() =>
        {
            var gold = Badge(1);
            var diamond = Badge(2);
            Realize(gold);
            Realize(diamond);

            // Mirrored lean is the whole reason a wall of mixed tiers reads hand-stamped rather
            // than templated; two badges leaning the same way is the regression.
            Assert.True(gold.TierTilt < 0, "the gold sign must lean left");
            Assert.True(diamond.TierTilt > 0, "the diamond sign must lean right");
            Assert.Equal(gold.TierTilt, Rotate(gold.TierImage).Angle, 3);
            Assert.Equal(diamond.TierTilt, Rotate(diamond.TierImage).Angle, 3);

            Assert.NotSame(gold.TierImage.Source, diamond.TierImage.Source);
        });
    }

    [Fact]
    public void TierZeroWearsNothingAtAll()
    {
        OnStaThread(() =>
        {
            var badge = Badge(0);
            Realize(badge);
            Assert.Equal(Visibility.Collapsed, badge.Visibility);
        });
    }

    [Fact]
    public void TheBadgeSizesItselfFromWhateverIsHostingIt()
    {
        OnStaThread(() =>
        {
            // A 336px shelf card: the 45% rule, unclamped.
            var onCard = Badge(1);
            Realize(onCard, 336, 200);
            Assert.Equal(151.2, onCard.TierImage.Width, 1);

            // A hero band: clamped, or the sign becomes a billboard.
            var onHero = Badge(2);
            Realize(onHero, 1300, 250);
            Assert.Equal(190, onHero.TierImage.Width, 1);

            // A mosaic-sized tile: clamped the other way, or the sign becomes unreadable.
            var onTile = Badge(1);
            Realize(onTile, 120, 120);
            Assert.Equal(88, onTile.TierImage.Width, 1);
        });
    }

    // =====================================================================================
    //  the re-stamp
    // =====================================================================================

    [Fact]
    public void TheFreeTodayStampLandsOverADimmedSignRatherThanReplacingIt()
    {
        OnStaThread(() =>
        {
            var badge = Badge(1, freeToday: true);
            Realize(badge);

            Assert.Equal(Visibility.Visible, badge.StampImage.Visibility);
            Assert.NotNull(badge.StampImage.Source);

            // "Slightly visible behind" (owner): the tier sign stays, dimmed. A collapsed sign
            // would lose the whole point - that this normally costs something.
            Assert.Equal(Visibility.Visible, badge.TierImage.Visibility);
            Assert.True(badge.TierImage.Opacity > 0.2 && badge.TierImage.Opacity < 0.5,
                $"the re-stamped sign sits at {badge.TierImage.Opacity}, which is not 'dimmed but readable'");

            // A papered-over neon sign does not keep humming.
            Assert.Null(badge.TierImage.Effect);

            // The stamp is offset down-left of the sign, not centred on it - a literal second pass.
            Assert.True(badge.StampImage.Margin.Top > 0, "the stamp must sit lower than the sign");
            Assert.True(badge.StampImage.Margin.Right > 0, "the stamp must sit left of the sign");
        });
    }

    [Fact]
    public void TheStampCountersTheSignsLeanWithoutFightingThePreTiltedArt()
    {
        OnStaThread(() =>
        {
            // The art is already tilted ~8 degrees, so code adds a token counter-lean and no more.
            // Anything past a couple of degrees means somebody tilted it twice.
            foreach (var tier in new[] { 1, 2 })
            {
                var badge = Badge(tier, freeToday: true);
                Realize(badge);
                var angle = Rotate(badge.StampImage).Angle;
                Assert.True(Math.Abs(angle) <= 2.001,
                    $"tier {tier} stamp adds {angle} degrees on top of art that is already tilted");
                // ...and it leans back against the sign it covers.
                Assert.True(Math.Sign(angle) != Math.Sign(badge.TierTilt),
                    $"tier {tier} stamp leans the same way as the sign, so it reads as one object");
            }
        });
    }

    [Fact]
    public void ClearingTheFreeDayGivesTheSignBackAtFullBrightness()
    {
        OnStaThread(() =>
        {
            var badge = Badge(1, freeToday: true);
            Realize(badge);
            badge.FreeToday = false;

            Assert.Equal(Visibility.Collapsed, badge.StampImage.Visibility);
            Assert.Equal(1.0, badge.TierImage.Opacity, 3);
        });
    }

    // =====================================================================================
    //  motion, and the absence of it
    // =====================================================================================

    [Fact]
    public void ReducedMotionLeavesAStaticTiltedBadgeAndNoClocksAtAll()
    {
        OnStaThread(() =>
        {
            var badge = Badge(2, motion: false);
            Realize(badge);

            Assert.False(badge.IsAnimating, "a badge must not run ambient clocks under reduced motion");

            var rotate = Rotate(badge.TierImage);
            var scale = Scale(badge.TierImage);
            Assert.False(rotate.HasAnimatedProperties, "the wobble is still animating the angle");
            Assert.False(scale.HasAnimatedProperties, "the wobble is still animating the scale");

            // The degrade is a STATIC TILTED badge - not an upright one, and not a slower loop.
            Assert.Equal(badge.TierTilt, rotate.Angle, 3);
            Assert.Equal(1.0, scale.ScaleX, 3);
        });
    }

    [Fact]
    public void WithMotionAllowedTheSignBreathesAndSways()
    {
        OnStaThread(() =>
        {
            var badge = Badge(2, motion: true);
            Realize(badge);

            Assert.True(badge.IsAnimating);
            Assert.True(Rotate(badge.TierImage).HasAnimatedProperties, "the sign is not swaying");
            Assert.True(Scale(badge.TierImage).HasAnimatedProperties, "the sign is not breathing");
        });
    }

    [Fact]
    public void ParkingABadgeReturnsItToExactlyTheReducedMotionRestingState()
    {
        OnStaThread(() =>
        {
            var badge = Badge(1, motion: true);
            Realize(badge);
            Assert.True(badge.IsAnimating);

            // The park hook the vault's StopExclusivesMotion calls. A tab that hides must leave a
            // badge in its resting state, not wherever its clock happened to be.
            badge.StopMotion();

            Assert.False(badge.IsAnimating);
            Assert.False(Rotate(badge.TierImage).HasAnimatedProperties);
            Assert.False(Scale(badge.TierImage).HasAnimatedProperties);
            Assert.Equal(badge.TierTilt, Rotate(badge.TierImage).Angle, 3);
            Assert.Equal(1.0, Scale(badge.TierImage).ScaleX, 3);
            Assert.Equal(1.0, badge.TierImage.Opacity, 3);
        });
    }

    [Fact]
    public void StartingTwiceDoesNotStackASecondSetOfClocks()
    {
        OnStaThread(() =>
        {
            // Every repaint calls StartMotion, so this is the ordinary case, not an edge one: the
            // method has to park before it begins or a card picks up a clock per refresh.
            var badge = Badge(2, motion: true);
            Realize(badge);
            badge.StartMotion();
            badge.StartMotion();

            Assert.True(badge.IsAnimating);
            badge.StopMotion();
            Assert.False(Rotate(badge.TierImage).HasAnimatedProperties,
                "a second Begin left a clock behind that Stop could not reach");
        });
    }

    [Fact]
    public void ADimmedSignUnderAStampDoesNotKeepHumming()
    {
        OnStaThread(() =>
        {
            var badge = Badge(1, freeToday: true, motion: true);
            Realize(badge);

            // The stamp is the message on a free day; a tier sign glowing away underneath it would
            // be two things asking for the same attention.
            Assert.Null(badge.TierImage.Effect);
        });
    }

    [Fact]
    public void AnUnstampedSignGlowsLikeTheNeonItIs()
    {
        OnStaThread(() =>
        {
            var badge = Badge(2, motion: true);
            Realize(badge);

            var glow = Assert.IsType<DropShadowEffect>(badge.TierImage.Effect);
            Assert.Equal(0, glow.ShadowDepth);
            Assert.True(glow.BlurRadius > 0);
        });
    }
}
