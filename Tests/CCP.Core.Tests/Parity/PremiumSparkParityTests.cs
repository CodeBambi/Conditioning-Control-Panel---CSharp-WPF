using System;
using System.Linq;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests.Parity;

/// <summary>
/// Parity with WPF 7.1.5 PremiumSparkRulesTests and PremiumSparkFieldTests (the pure halves):
/// Free is dim and still forever, Basic shimmers gold, Prime glows cyan with diamonds; Reduced
/// keeps a slower sheen and a few particles; Off is the static lit card. Same seeds, same field.
/// </summary>
public class PremiumSparkParityTests
{
    [Theory]
    [InlineData(false, false, SparkTier.Free)]
    [InlineData(true, false, SparkTier.Basic)]
    [InlineData(true, true, SparkTier.Prime)]
    [InlineData(false, true, SparkTier.Prime)]
    public void TierFollowsTheTwoCanonicalGates(bool premium, bool lab, SparkTier expected) =>
        Assert.Equal(expected, PremiumSparkRules.TierFrom(premium, lab));

    [Theory]
    [InlineData(MotionLevel.Full, true, SparkMotion.Full)]
    [InlineData(MotionLevel.Full, false, SparkMotion.Reduced)]
    [InlineData(MotionLevel.Reduced, true, SparkMotion.Reduced)]
    [InlineData(MotionLevel.Off, true, SparkMotion.Off)]
    [InlineData(MotionLevel.Off, false, SparkMotion.Off)]
    public void MotionFoldsTheLevelWithThePerformanceTier(MotionLevel level, bool ambient, SparkMotion expected) =>
        Assert.Equal(expected, PremiumSparkRules.MotionFrom(level, ambient));

    [Theory]
    [InlineData(SparkMotion.Full)]
    [InlineData(SparkMotion.Reduced)]
    [InlineData(SparkMotion.Off)]
    public void FreeIsDimAndNeverMoves(SparkMotion motion)
    {
        Assert.Equal(0.40, PremiumSparkRules.Opacity(SparkTier.Free), 3);
        Assert.False(PremiumSparkRules.Sheen(SparkTier.Free, motion));
        Assert.False(PremiumSparkRules.Breath(SparkTier.Free, motion));
        Assert.Equal(0, PremiumSparkRules.AmbientCap(SparkTier.Free, motion, true));
        Assert.False(PremiumSparkRules.Clock(SparkTier.Free, motion));
        Assert.False(PremiumSparkRules.Halo(SparkTier.Free));
        Assert.False(PremiumSparkRules.Wobble(SparkTier.Free, motion));
        Assert.Equal(0, PremiumSparkRules.BurstCount(SparkTier.Free, motion));
    }

    [Fact]
    public void BasicAndPrimeAtFull()
    {
        Assert.Equal(18, PremiumSparkRules.AmbientCap(SparkTier.Basic, SparkMotion.Full, true));
        Assert.Equal(24, PremiumSparkRules.AmbientCap(SparkTier.Prime, SparkMotion.Full, true));
        Assert.Equal(PremiumSparkRules.FewCap, PremiumSparkRules.AmbientCap(SparkTier.Prime, SparkMotion.Full, particlesAllowed: false));
        Assert.Equal((10, 14), (PremiumSparkRules.BurstCount(SparkTier.Basic, SparkMotion.Full), PremiumSparkRules.BurstCount(SparkTier.Prime, SparkMotion.Full)));
        Assert.True(PremiumSparkRules.Orbits(SparkTier.Basic, SparkMotion.Full));
        Assert.False(PremiumSparkRules.Flares(SparkTier.Basic, SparkMotion.Full));
        Assert.True(PremiumSparkRules.Flares(SparkTier.Prime, SparkMotion.Full));
        Assert.True(PremiumSparkRules.HaloPulse(SparkTier.Prime, SparkMotion.Full));
    }

    [Theory]
    [InlineData(SparkTier.Basic)]
    [InlineData(SparkTier.Prime)]
    public void ReducedKeepsASlowerSheenAndAFewParticles(SparkTier tier)
    {
        Assert.True(PremiumSparkRules.Sheen(tier, SparkMotion.Reduced));
        Assert.Equal(PremiumSparkRules.SheenCycleSec(tier, SparkMotion.Full) * 2, PremiumSparkRules.SheenCycleSec(tier, SparkMotion.Reduced), 9);
        Assert.Equal(PremiumSparkRules.SheenPassSec * 1.6, PremiumSparkRules.SheenPassFor(SparkMotion.Reduced), 9);
        Assert.False(PremiumSparkRules.Breath(tier, SparkMotion.Reduced));
        Assert.False(PremiumSparkRules.HaloPulse(tier, SparkMotion.Reduced));
        Assert.False(PremiumSparkRules.Orbits(tier, SparkMotion.Reduced));
        Assert.False(PremiumSparkRules.Flares(tier, SparkMotion.Reduced));
        Assert.Equal(4, PremiumSparkRules.AmbientCap(tier, SparkMotion.Reduced, true));
        Assert.Equal(0.3, PremiumSparkRules.ReducedPace);
    }

    [Theory]
    [InlineData(SparkTier.Basic)]
    [InlineData(SparkTier.Prime)]
    public void OffIsTheStaticLitCard(SparkTier tier)
    {
        Assert.Equal(1.0, PremiumSparkRules.Opacity(tier));
        Assert.False(PremiumSparkRules.Sheen(tier, SparkMotion.Off));
        Assert.False(PremiumSparkRules.Wobble(tier, SparkMotion.Off));
        Assert.Equal(0, PremiumSparkRules.BurstCount(tier, SparkMotion.Off));
        Assert.Equal(0, PremiumSparkRules.AmbientCap(tier, SparkMotion.Off, true));
        Assert.False(PremiumSparkRules.Clock(tier, SparkMotion.Off));
        Assert.Equal(tier == SparkTier.Prime, PremiumSparkRules.Halo(tier));
    }

    [Fact]
    public void TheNumbersAreTheWpfNumbers()
    {
        Assert.Equal((30.0, 54.0, 42.0, 4.0, -4.0), (PremiumSparkRules.StarSize, PremiumSparkRules.ControlWidth,
            PremiumSparkRules.ControlHeight, PremiumSparkRules.LayoutBleed, PremiumSparkRules.Tilt));
        Assert.Equal(34, PremiumSparkRules.ControlHeight - 2 * PremiumSparkRules.LayoutBleed);
        Assert.Equal((0.9, 3.6, 2.8), (PremiumSparkRules.SheenPassSec, PremiumSparkRules.SheenRestBasicSec, PremiumSparkRules.SheenRestPrimeSec));
        Assert.Equal((2.2, 1.04), (PremiumSparkRules.BreathHalfSec, PremiumSparkRules.BreathScale));
        Assert.Equal((0.45, 0.95, 0.7, 1.6), (PremiumSparkRules.HaloLow, PremiumSparkRules.HaloHigh, PremiumSparkRules.HaloStill, PremiumSparkRules.HaloHalfSec));
        Assert.Equal((5.0, 460, 30), (PremiumSparkRules.WobbleDegrees, PremiumSparkRules.WobbleMs, PremiumSparkRules.FrameRate));
        Assert.Equal("M15,0 C15.9,10 18.8,14 28,15 C18.8,16 15.9,20 15,30 C14.1,20 11.2,16 2,15 C11.2,14 14.1,10 15,0 Z", PremiumSparkRules.StarPathData);
    }

    [Fact]
    public void TheLiveryIsCommerceChrome()
    {
        var gold = PremiumSparkRules.LiveryFor(SparkTier.Basic);
        Assert.Equal(0xFFFFD27Au, gold.Ink);     // tier_badge_t1 glow
        Assert.Equal(0xFFFFE79Au, gold.Mote);
        var ice = PremiumSparkRules.LiveryFor(SparkTier.Prime);
        Assert.Equal(0xFFBDEFFFu, ice.Mote);     // tier_badge_t2
        Assert.Equal(0xFF5EE6FFu, ice.Ink);
        Assert.Equal(0xFF777284u, PremiumSparkRules.LiveryFor(SparkTier.Free).FaceMid);
    }

    [Fact]
    public void DepthComesFromDepthRules()
    {
        Assert.Equal(0, PremiumSparkRules.Travel(pressed: false, hovered: false));
        Assert.Equal(-2, PremiumSparkRules.Travel(pressed: false, hovered: true));
        Assert.Equal(2, PremiumSparkRules.Travel(pressed: true, hovered: true));
        Assert.Equal(0, PremiumSparkRules.ShadowLength(pressed: true, hovered: true));
        Assert.True(PremiumSparkRules.ShadowLength(false, true) > PremiumSparkRules.ShadowLength(false, false));
    }

    [Fact]
    public void ClickOpensPremiumOrFallsBackToTheOldKey()
    {
        Assert.Equal("premium", PremiumSparkRules.TargetTab(k => k == "premium" ? "home" : null));
        Assert.Equal("exclusives", PremiumSparkRules.TargetTab(_ => null));
        Assert.Equal("premium", PremiumSparkRules.TargetTab(ConditioningControlPanel.Nav.NavSections.SectionForTab));
    }

    [Fact]
    public void EveryTierNamesItselfInTheTooltip()
    {
        var keys = Enum.GetValues<SparkTier>().Select(PremiumSparkRules.TooltipKey).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void TheArmsAreSlimAndLitFromTheTopLeft()
    {
        foreach (var flank in PremiumSparkRules.Flanks)
        {
            var mid = PremiumSparkRules.Split(flank, 0.5).a.Item4;
            var d = Math.Sqrt(Math.Pow(mid.X - 15, 2) + Math.Pow(mid.Y - 15, 2));
            Assert.InRange(d, 3.5, 5.5);
        }
        double Shade(int flank, bool firstHalf)
        {
            var f = PremiumSparkRules.Flanks[flank];
            var (a, _) = PremiumSparkRules.Split(f, 0.5);
            return firstHalf ? PremiumSparkRules.FacetShade(f.p0, a.Item4) : PremiumSparkRules.FacetShade(a.Item4, f.p3);
        }
        Assert.True(Shade(3, firstHalf: false) > 0.2);
        Assert.True(Shade(1, firstHalf: false) < -0.2);
        Assert.True(Math.Abs(Shade(0, true) - Shade(3, false)) > 0.2);
    }

    [Fact]
    public void TheClockValuesStayInRange()
    {
        for (double t = 0; t < 12; t += 0.07)
        {
            Assert.InRange(PremiumSparkRules.SheenAt(t, SparkTier.Prime, SparkMotion.Full), -20, 42);
            Assert.InRange(PremiumSparkRules.BreathAt(t, SparkTier.Basic, SparkMotion.Full), 1.0, PremiumSparkRules.BreathScale);
            Assert.InRange(PremiumSparkRules.HaloAt(t, SparkTier.Prime, SparkMotion.Full, 1.0), PremiumSparkRules.HaloLow, 1.0);
        }
        Assert.Equal(-20, PremiumSparkRules.SheenAt(3, SparkTier.Free, SparkMotion.Full));
        Assert.Equal(1.0, PremiumSparkRules.BreathAt(3, SparkTier.Basic, SparkMotion.Reduced));
        Assert.Equal(PremiumSparkRules.HaloStill, PremiumSparkRules.HaloAt(3, SparkTier.Prime, SparkMotion.Off, 1));
        Assert.Equal(0, PremiumSparkRules.HaloAt(3, SparkTier.Basic, SparkMotion.Full, 1));
    }

    // ---- the field -----------------------------------------------------------------------------

    private static PremiumSparkField Run(SparkTier tier, SparkMotion motion, bool particles, double seconds,
        int seed = 11, Action<PremiumSparkField>? each = null)
    {
        var field = new PremiumSparkField(new Random(seed));
        field.Configure(tier, motion, particles);
        for (double t = 0; t < seconds; t += 1.0 / 30)
        {
            field.Step(1.0 / 30);
            Assert.True(field.Alive.Count <= PremiumSparkField.PoolSize);
            each?.Invoke(field);
        }
        return field;
    }

    private static int Ambient(PremiumSparkField f) =>
        f.Alive.Count(m => m.Kind != MoteKind.Burst && m.Kind != MoteKind.Flare);

    [Theory]
    [InlineData(SparkTier.Free, SparkMotion.Full)]
    [InlineData(SparkTier.Free, SparkMotion.Reduced)]
    [InlineData(SparkTier.Basic, SparkMotion.Off)]
    [InlineData(SparkTier.Prime, SparkMotion.Off)]
    public void FreeAndOffSpawnNothing(SparkTier tier, SparkMotion motion)
        => Assert.Empty(Run(tier, motion, true, 6).Alive);

    [Fact]
    public void BasicSpillsASteadyTrickleOfGlitter()
    {
        int minSteady = int.MaxValue, maxAmbient = 0, frame = 0;
        bool orbit = false, glint = false, glitter = false;
        Run(SparkTier.Basic, SparkMotion.Full, true, 8, each: f =>
        {
            frame++;
            var n = Ambient(f);
            maxAmbient = Math.Max(maxAmbient, n);
            if (frame > 60) minSteady = Math.Min(minSteady, n);
            orbit |= f.Alive.Any(m => m.Kind == MoteKind.Orbit);
            glint |= f.Alive.Any(m => m.Kind == MoteKind.Glint);
            glitter |= f.Alive.Any(m => m.Kind == MoteKind.Glitter);
            Assert.DoesNotContain(f.Alive, m => m.Kind == MoteKind.Diamond || m.Kind == MoteKind.Flare);
        });
        Assert.True(glitter && orbit && glint);
        Assert.True(maxAmbient <= PremiumSparkRules.BasicCap);
        Assert.True(minSteady >= 9, $"the trickle ran dry: {minSteady}");
    }

    [Fact]
    public void PrimeRunsADenserDiamondFieldWithFlares()
    {
        int minSteady = int.MaxValue, flares = 0, frame = 0;
        bool glint = false, wasFlare = false;
        Run(SparkTier.Prime, SparkMotion.Full, true, 14, each: f =>
        {
            frame++;
            if (frame > 60) minSteady = Math.Min(minSteady, Ambient(f));
            glint |= f.Alive.Any(m => m.Kind == MoteKind.Glint);
            var isFlare = f.Alive.Any(m => m.Kind == MoteKind.Flare);
            if (isFlare && !wasFlare) flares++;
            wasFlare = isFlare;
            Assert.True(Ambient(f) <= PremiumSparkRules.PrimeCap);
            Assert.DoesNotContain(f.Alive, m => m.Kind == MoteKind.Glitter || m.Kind == MoteKind.Orbit);
        });
        Assert.True(glint);
        Assert.True(minSteady >= 14, $"Prime field too thin: {minSteady}");
        Assert.InRange(flares, 2, 5);
    }

    [Theory]
    [InlineData(SparkTier.Basic)]
    [InlineData(SparkTier.Prime)]
    public void ReducedKeepsAFew(SparkTier tier)
    {
        bool any = false;
        Run(tier, SparkMotion.Reduced, false, 10, each: f =>
        {
            any |= f.Alive.Count > 0;
            Assert.True(f.Alive.Count <= PremiumSparkRules.FewCap);
            Assert.DoesNotContain(f.Alive, m => m.Kind == MoteKind.Orbit || m.Kind == MoteKind.Flare);
        });
        Assert.True(any);
    }

    [Fact]
    public void BurstsRideOnTopButNeverOutgrowThePool()
    {
        var f = Run(SparkTier.Prime, SparkMotion.Full, true, 3);
        for (int i = 0; i < 5; i++) f.Burst(14);
        Assert.True(f.Alive.Count(m => m.Kind == MoteKind.Burst) <= PremiumSparkField.BurstHeadroom);
        Assert.True(f.Alive.Count <= PremiumSparkField.PoolSize);
        for (int i = 0; i < 40; i++) f.Step(1.0 / 30);
        Assert.DoesNotContain(f.Alive, m => m.Kind == MoteKind.Burst && m.Age > m.Life);
    }

    [Fact]
    public void ConfiguringToFreeClearsTheField()
    {
        var f = Run(SparkTier.Prime, SparkMotion.Full, true, 2);
        Assert.NotEmpty(f.Alive);
        f.Configure(SparkTier.Free, SparkMotion.Full, true);
        Assert.Empty(f.Alive);
        f.Step(1);
        Assert.Empty(f.Alive);
    }

    [Fact]
    public void TheSameSeedDrawsTheSameField()
    {
        var a = Run(SparkTier.Basic, SparkMotion.Full, true, 2, seed: 5);
        var b = Run(SparkTier.Basic, SparkMotion.Full, true, 2, seed: 5);
        Assert.Equal(a.Alive.Select(m => (m.Kind, Math.Round(m.X, 6), Math.Round(m.Y, 6))),
                     b.Alive.Select(m => (m.Kind, Math.Round(m.X, 6), Math.Round(m.Y, 6))));
    }

    [Fact]
    public void GlitterFallsAndEveryMoteFadesInAndOut()
    {
        var f = new PremiumSparkField(new Random(3));
        f.Configure(SparkTier.Basic, SparkMotion.Full, true);
        for (int i = 0; i < 120; i++)
        {
            f.Step(1.0 / 30);
            foreach (var m in f.Alive)
            {
                Assert.InRange(PremiumSparkField.OpacityOf(m), 0, 1);
                Assert.InRange(PremiumSparkField.ScaleOf(m), 0, 1.0001);
            }
        }
        var old = f.Alive.Where(m => m.Kind == MoteKind.Glitter && m.Age > 0.6).ToList();
        Assert.NotEmpty(old);
        Assert.True(old.Average(m => m.Vy) > 0, "glitter should fall");
    }

    [Fact]
    public void PoolCoversTheBiggestLook() =>
        Assert.Equal(PremiumSparkRules.MaxAmbientCap + PremiumSparkField.BurstHeadroom + 1, PremiumSparkField.PoolSize);
}
