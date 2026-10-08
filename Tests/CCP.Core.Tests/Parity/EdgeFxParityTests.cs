using System;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests.Parity;

/// <summary>
/// Parity with WPF 7.1.5 EdgeParticlesTests (gates and drift math), FogPolish11Tests (fog
/// budget and envelope), FogDustTests and the Vault motes: the pure numbers lane C's canvas
/// twin runs on.
/// </summary>
public class EdgeFxParityTests
{
    // ---- the gate ----------------------------------------------------------------------------

    [Fact]
    public void TheGateIsNoneReducedOrFull()
    {
        Assert.Equal(EdgeFogMode.None, EdgeParticleRules.ModeFor(MotionLevel.Off, true));
        Assert.Equal(EdgeFogMode.None, EdgeParticleRules.ModeFor(MotionLevel.Full, false));
        Assert.Equal(EdgeFogMode.None, EdgeParticleRules.ModeFor(MotionLevel.Reduced, false));
        Assert.Equal(EdgeFogMode.Reduced, EdgeParticleRules.ModeFor(MotionLevel.Reduced, true));
        Assert.Equal(EdgeFogMode.Full, EdgeParticleRules.ModeFor(MotionLevel.Full, true));
        Assert.Equal(AmbientFxLayers.EdgeFog | AmbientFxLayers.EdgeDrift, EdgeParticleRules.LayersFor(EdgeFogMode.Full));
        Assert.Equal(AmbientFxLayers.EdgeFog, EdgeParticleRules.LayersFor(EdgeFogMode.Reduced));
        Assert.Equal(AmbientFxLayers.None, EdgeParticleRules.LayersFor(EdgeFogMode.None));
        Assert.Equal(1 << 6, (int)AmbientFxLayers.EdgeDrift);
        Assert.Equal(1 << 7, (int)AmbientFxLayers.EdgeFog);
        Assert.Equal(1 << 8, (int)AmbientFxLayers.VaultMotes);
    }

    [Fact]
    public void TheEmbersMountOnlyAtFullWithParticles()
    {
        Assert.True(EdgeParticleRules.ShouldMount(MotionLevel.Full, true));
        Assert.False(EdgeParticleRules.ShouldMount(MotionLevel.Full, false));
        Assert.False(EdgeParticleRules.ShouldMount(MotionLevel.Reduced, true));
        Assert.False(EdgeParticleRules.ShouldMount(MotionLevel.Off, true));
        Assert.True(EdgeParticleRules.TierAllowsParticles(PerformanceTier.Quality));
        Assert.True(EdgeParticleRules.TierAllowsParticles(PerformanceTier.Balanced));
        Assert.False(EdgeParticleRules.TierAllowsParticles(PerformanceTier.Performance));
    }

    [Fact]
    public void FourStripsFiftySixDeepEmbersInThirty()
    {
        Assert.Equal(new[] { EdgeSide.Top, EdgeSide.Right, EdgeSide.Bottom, EdgeSide.Left }, EdgeParticleRules.Sides);
        Assert.Equal(56, EdgeParticleRules.StripThickness);
        Assert.Equal(30, EdgeParticleRules.EmberBandPx);
        Assert.Equal(0.55, EdgeParticleRules.StripIntensity);
        Assert.Equal(0.35, EdgeParticleRules.StripDustDensity);
        Assert.True(EdgeParticleRules.IsHorizontal(EdgeSide.Top) && EdgeParticleRules.IsHorizontal(EdgeSide.Bottom));
        Assert.False(EdgeParticleRules.IsHorizontal(EdgeSide.Left));
    }

    // ---- embers (EdgeDriftMath) --------------------------------------------------------------

    [Fact]
    public void TheBudgetIsAThirdOfTheLiveBudgetCappedAtSix()
    {
        Assert.Equal(0, EdgeDriftMath.Target(0));
        Assert.Equal(3, EdgeDriftMath.Target(10));
        Assert.Equal(6, EdgeDriftMath.Target(24));
        Assert.Equal(6, EdgeDriftMath.Target(60));
    }

    [Theory]
    [InlineData(EdgeSide.Top, 1, 0)]
    [InlineData(EdgeSide.Right, 0, 1)]
    [InlineData(EdgeSide.Bottom, -1, 0)]
    [InlineData(EdgeSide.Left, 0, -1)]
    public void AlongRunsClockwiseAndDepthStaysInTheStrip(EdgeSide side, int dx, int dy)
    {
        var (x0, y0) = EdgeDriftMath.Position(side, 0.3, 0.5);
        var (x1, y1) = EdgeDriftMath.Position(side, 0.3 + EdgeDriftMath.SpeedMin, 0.5);
        Assert.Equal(dx, Math.Sign(Math.Round(x1 - x0, 6)));
        Assert.Equal(dy, Math.Sign(Math.Round(y1 - y0, 6)));
        var (ox, oy) = EdgeDriftMath.Position(side, 0.5, 0.0);
        switch (side)
        {
            case EdgeSide.Top: Assert.Equal(0.0, oy); break;
            case EdgeSide.Right: Assert.Equal(1.0, ox); break;
            case EdgeSide.Bottom: Assert.Equal(1.0, oy); break;
            case EdgeSide.Left: Assert.Equal(0.0, ox); break;
        }
    }

    [Fact]
    public void AlphaFollowsTheSpec()
    {
        Assert.Equal(0.30, EdgeDriftMath.Alpha(0.5, 5, 10, Math.PI / 2, 1.0), 6);
        Assert.Equal(0.30 * 0.85, EdgeDriftMath.Alpha(0.5, 5, 10, -Math.PI / 2, 1.0), 6);
        Assert.Equal(0.0, EdgeDriftMath.Alpha(0.5, 10, 10, 0, 1.0), 6);
        Assert.Equal(0.0, EdgeDriftMath.Alpha(0.5, 0, 10, 0, 1.0), 6);
        Assert.Equal(0.0, EdgeDriftMath.Alpha(1.0, 5, 10, 0, 1.0), 6);
        Assert.True(EdgeDriftMath.IsSpent(1.0, 3));
        Assert.True(EdgeDriftMath.IsSpent(0.4, 0));
        Assert.False(EdgeDriftMath.IsSpent(0.4, 3));
        Assert.Equal(0.3 + 0.03 * 2, EdgeDriftMath.Advance(0.3, 0.03, 2), 9);
        Assert.Equal(0.3, EdgeDriftMath.Advance(0.3, -1, 2), 9);   // never anticlockwise
    }

    // ---- fog (EdgeFogMath) -------------------------------------------------------------------

    [Fact]
    public void TheWholeRingStaysUnderSixtyLiveElements()
    {
        int fog = 0;
        foreach (bool longSide in new[] { true, true, false, false })
            fog += EdgeFogMath.Target(EdgeFogMath.FullCount(true, longSide), 60, false)
                 + EdgeFogMath.Target(EdgeFogMath.FullCount(false, longSide), 60, false);
        Assert.Equal(34, fog);
        Assert.True(fog + 4 * EdgeDriftMath.MaxPerStrip < 60);
        int reduced = 0;
        foreach (bool longSide in new[] { true, true, false, false })
            reduced += EdgeFogMath.Target(EdgeFogMath.FullCount(true, longSide), 60, true)
                     + EdgeFogMath.Target(EdgeFogMath.FullCount(false, longSide), 60, true);
        Assert.InRange(reduced, 15, 19);
        Assert.Equal(0, EdgeFogMath.Target(4, 0, false));
        Assert.Equal(3, EdgeFogMath.Target(5, 24, false));
    }

    [Fact]
    public void ThePuffEnvelopeFadesInHoldsAndFadesOut()
    {
        Assert.Equal(0, EdgeFogMath.Envelope(0, 6));
        Assert.Equal(0, EdgeFogMath.Envelope(6, 6));
        Assert.Equal(1, EdgeFogMath.Envelope(3, 6));
        Assert.InRange(EdgeFogMath.Envelope(0.6, 6), 0.05, 0.6);
        Assert.Equal(EdgeFogMath.Envelope(1, 6), EdgeFogMath.Envelope(5, 6), 9);
        Assert.Equal(EdgeFogMath.SmallAlphaMax, EdgeFogMath.Alpha(0.22, 3, 6, 1.5), 9);
        Assert.InRange(EdgeFogMath.BigAlphaMax, 0.05, 0.22);
        double deep = EdgeFogMath.MaxDepth(EdgeFogMath.BigSizeMaxPx, EdgeFogMath.BreathePxMax);
        Assert.True(deep + EdgeFogMath.BreathePxMax + EdgeFogMath.BigSizeMaxPx / 2 <= EdgeFogMath.StripPx);
    }

    [Fact]
    public void TheFogNumbersAreTheWpfNumbers()
    {
        Assert.Equal(56, EdgeFogMath.StripPx);
        Assert.Equal((34.0, 60.0, 4.0, 9.0), (EdgeFogMath.BigSizeMinPx, EdgeFogMath.BigSizeMaxPx, EdgeFogMath.BigSpeedMinPx, EdgeFogMath.BigSpeedMaxPx));
        Assert.Equal((18.0, 32.0, 10.0, 20.0), (EdgeFogMath.SmallSizeMinPx, EdgeFogMath.SmallSizeMaxPx, EdgeFogMath.SmallSpeedMinPx, EdgeFogMath.SmallSpeedMaxPx));
        Assert.Equal((5, 5, 4, 3), (EdgeFogMath.BigLong, EdgeFogMath.SmallLong, EdgeFogMath.BigShort, EdgeFogMath.SmallShort));
        Assert.Equal((90, 56), (EdgeFogMath.DustLong, EdgeFogMath.DustShort));
        Assert.Equal((0.35, 0.3, 0.35, 0.10), (EdgeFogMath.FadeShare, EdgeFogMath.CounterShare, EdgeFogMath.CornerShare, EdgeFogMath.CornerZone));
        Assert.Equal((0.35, 0.04), (EdgeFogMath.SpawnEverySeconds, EdgeFogMath.DustSpawnEverySeconds));
    }

    [Theory]
    [InlineData(EdgeSide.Top, 100, 10, 100, 10)]
    [InlineData(EdgeSide.Right, 100, 10, 46, 100)]
    [InlineData(EdgeSide.Bottom, 100, 10, 1561, 46)]
    [InlineData(EdgeSide.Left, 100, 10, 10, 902)]
    public void APuffSitsOnItsSideClockwise(EdgeSide side, double along, double depth, double x, double y)
    {
        double w = EdgeParticleRules.IsHorizontal(side) ? 1661 : EdgeFogMath.StripPx;
        double h = EdgeParticleRules.IsHorizontal(side) ? EdgeFogMath.StripPx : 1002;
        Assert.Equal((x, y), EdgeFogMath.Position(side, along, depth, w, h));
    }

    [Fact]
    public void SpawnsFavourTheCorners()
    {
        Assert.Equal(0, EdgeFogMath.SpawnAlong(0, 0, 1000));
        Assert.Equal(1000, EdgeFogMath.SpawnAlong(0.5, 0, 1000));
        Assert.Equal(500, EdgeFogMath.SpawnAlong(0.5, 0.9, 1000));
        Assert.True(EdgeFogMath.IsSpent(1, 1, 10, 100, 40));
        Assert.True(EdgeFogMath.IsSpent(0, 1, -41, 100, 40));
        Assert.False(EdgeFogMath.IsSpent(0, 1, 50, 100, 40));
    }

    // ---- dust (FogDustTests) -----------------------------------------------------------------

    [Fact]
    public void Dust_is_tiny_and_plentiful()
    {
        Assert.True(EdgeFogMath.DustSizeMaxPx <= 3.0);
        Assert.True(EdgeFogMath.DustLong > EdgeFogMath.BigLong + EdgeFogMath.SmallLong);
        Assert.True(EdgeFogMath.DustShort > EdgeFogMath.BigShort + EdgeFogMath.SmallShort);
    }

    [Theory]
    [InlineData(0.0, 2.0)]
    [InlineData(0.5, 7.0)]
    [InlineData(1.0, 7.0)]
    [InlineData(1.0, 2.0)]
    public void A_speck_wanders_inside_the_strip(double u, double wander)
    {
        double depth = EdgeFogMath.DustDepth(u, wander);
        Assert.True(depth - wander >= 0.9);
        Assert.True(depth + wander <= EdgeFogMath.StripPx - 1.9);
    }

    [Fact]
    public void Dust_hugs_the_frame_and_twinkles_without_blacking_out()
    {
        double deepest = EdgeFogMath.DustDepth(1.0, EdgeFogMath.DustWanderPxMax) + EdgeFogMath.DustWanderPxMax;
        Assert.True(deepest <= 35);
        Assert.True(EdgeFogMath.DustDepth(0.5, 3) < 10);
        Assert.Equal(0, EdgeFogMath.DustAlpha(0.85, 0, 4, 0, 1));
        Assert.Equal(0, EdgeFogMath.DustAlpha(0.85, 4, 4, 0, 1));
        for (double tw = 0; tw < Math.PI * 2; tw += 0.3)
            Assert.InRange(EdgeFogMath.DustAlpha(EdgeFogMath.DustAlphaMax, 2, 4, tw, 1.5), 0, 0.95);
        double lo = EdgeFogMath.DustAlpha(0.6, 2, 4, -Math.PI / 2, 1);
        double hi = EdgeFogMath.DustAlpha(0.6, 2, 4, Math.PI / 2, 1);
        Assert.True(lo > 0.3 && lo < hi);
    }

    // ---- Vault motes -------------------------------------------------------------------------

    [Fact]
    public void VaultMotesFollowTheMotionLevelAndTheBudget()
    {
        Assert.Equal((64, 14, 0), (VaultMoteMath.Cap(MotionLevel.Full), VaultMoteMath.Cap(MotionLevel.Reduced), VaultMoteMath.Cap(MotionLevel.Off)));
        Assert.Equal((26.0, 4.0, 0.0), (VaultMoteMath.SpawnPerSecond(MotionLevel.Full), VaultMoteMath.SpawnPerSecond(MotionLevel.Reduced), VaultMoteMath.SpawnPerSecond(MotionLevel.Off)));
        Assert.Equal(0.5, VaultMoteMath.SpeedScale(MotionLevel.Reduced));
        Assert.Equal(48, VaultMoteMath.Target(MotionLevel.Full, 24));
        Assert.Equal(64, VaultMoteMath.Target(MotionLevel.Full, 60));
        Assert.Equal(0, VaultMoteMath.Target(MotionLevel.Off, 60));
        Assert.Equal((0.78, 12.0, 4.0, 1.6, 3.4), (VaultMoteMath.ZoneShare, VaultMoteMath.RingOut, VaultMoteMath.RingIn, VaultMoteMath.LifeMin, VaultMoteMath.LifeMax));
    }

    [Fact]
    public void VaultMotesAreBornOnTheRingAroundAZone()
    {
        var zone = new RectD(100, 200, 80, 40);
        var rng = new Random(9);
        for (int i = 0; i < 500; i++)
        {
            var p = VaultMoteMath.SpawnOnRing(zone, rng.NextDouble(), rng.NextDouble(), rng.NextDouble());
            bool inOuter = p.X >= zone.Left - VaultMoteMath.RingOut - 1e-9 && p.X <= zone.Right + VaultMoteMath.RingOut + 1e-9
                        && p.Y >= zone.Top - VaultMoteMath.RingOut - 1e-9 && p.Y <= zone.Bottom + VaultMoteMath.RingOut + 1e-9;
            bool inCore = p.X > zone.Left + VaultMoteMath.RingIn + 1e-9 && p.X < zone.Right - VaultMoteMath.RingIn - 1e-9
                       && p.Y > zone.Top + VaultMoteMath.RingIn + 1e-9 && p.Y < zone.Bottom - VaultMoteMath.RingIn - 1e-9;
            Assert.True(inOuter && !inCore, $"{p} is off the ring");
        }
        Assert.Equal(new Vec2(100, 204), VaultMoteMath.SpawnOnRing(zone, 0, 0, 0));    // top side, 4 px inside
    }
}
