using System;
using System.Linq;
using ConditioningControlPanel.Motion;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests.Parity;

/// <summary>
/// Parity with WPF 7.1.5 AmbientMotionProfileTests (the two gates every ambient loop passes), the
/// MotionFx timing table, the house THUD curve (DashboardBillboardTests) and the WPF easing
/// shapes the heads must reproduce without WPF's easing classes.
/// </summary>
public class MotionParityTests
{
    [Theory]
    [InlineData(PerformanceTier.Quality, true)]
    [InlineData(PerformanceTier.Balanced, true)]
    [InlineData(PerformanceTier.Performance, false)]
    public void AllowAmbientMotion_PerTier(PerformanceTier tier, bool expected)
        => Assert.Equal(expected, MotionGate.AllowAmbientMotion(tier));

    [Theory]
    [InlineData(PerformanceTier.Quality, 60)]
    [InlineData(PerformanceTier.Balanced, 24)]
    [InlineData(PerformanceTier.Performance, 0)]
    public void MaxAmbientParticles_PerTier(PerformanceTier tier, int expected)
        => Assert.Equal(expected, MotionGate.MaxAmbientParticles(tier));

    [Theory]
    [InlineData(PerformanceTier.Quality, 30)]
    [InlineData(PerformanceTier.Balanced, 24)]
    [InlineData(PerformanceTier.Performance, 0)]
    public void FxTargetFps_PerTier(PerformanceTier tier, int expected)
        => Assert.Equal(expected, MotionGate.FxTargetFps(tier));

    [Theory]
    [InlineData(MotionLevel.Full, true, MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced, true, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off, true, MotionLevel.Off)]
    [InlineData(MotionLevel.Full, false, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Reduced, false, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off, false, MotionLevel.Off)]
    public void TheOsFlagOnlyEverRemovesMotion(MotionLevel setting, bool osAnim, MotionLevel expected)
        => Assert.Equal(expected, MotionGate.ResolveLevel(setting, osAnim));

    [Fact]
    public void TheGatesComposeLikeMotionFx()
    {
        Assert.True(MotionGate.AllowTransitions(MotionLevel.Reduced));
        Assert.False(MotionGate.AllowTransitions(MotionLevel.Off));
        Assert.True(MotionGate.AllowAmbientLoops(MotionLevel.Full, PerformanceTier.Balanced));
        Assert.False(MotionGate.AllowAmbientLoops(MotionLevel.Reduced, PerformanceTier.Quality));
        Assert.False(MotionGate.AllowAmbientLoops(MotionLevel.Full, PerformanceTier.Performance));
        Assert.True(MotionGate.AllowParticles(MotionLevel.Full, PerformanceTier.Quality));
        Assert.False(MotionGate.AllowParticles(MotionLevel.Full, PerformanceTier.Performance));
        Assert.Equal(0, (int)MotionLevel.Full);
        Assert.Equal(1, (int)MotionLevel.Reduced);
        Assert.Equal(2, (int)MotionLevel.Off);
    }

    [Fact]
    public void TheTimingTableIsTheWpfTable()
    {
        Assert.Equal((1.02, 150), (MotionTimings.HoverLiftScale, MotionTimings.HoverMs));
        Assert.Equal((0.97, 80), (MotionTimings.PressSquishScale, MotionTimings.PressMs));
        Assert.Equal((40, 6, 220, 260, 10.0), (MotionTimings.StaggerMs, MotionTimings.StaggerCap,
            MotionTimings.StaggerFadeMs, MotionTimings.StaggerRiseMs, MotionTimings.StaggerRisePx));
        Assert.Equal(new[] { 0, 40, 80, 120, 160, 200, 240, 240, 240 }, Enumerable.Range(0, 9).Select(MotionTimings.StaggerDelayMs).ToArray());
        Assert.Equal((0.7, 0.6, 0.45), (MotionTimings.OdometerSeconds, MotionTimings.BarFillSeconds, MotionTimings.BarBloomTailSeconds));
        Assert.Equal((0.6, 1.0, 3.4, 30), (MotionTimings.GlowBreathMin, MotionTimings.GlowBreathMax, MotionTimings.GlowBreathSeconds, MotionTimings.AmbientFrameRate));
        Assert.Equal((1.06, 1.03, 380, 0.35), (MotionTimings.TilePopScale, MotionTimings.CardLiftScale, MotionTimings.PopMs, MotionTimings.PopPeakAt));
        Assert.Equal((480, 40.0, 700.0, 450), (MotionTimings.ShockwaveMs, MotionTimings.ShockwaveFromPx, MotionTimings.ShockwaveToPx, MotionTimings.ExitBeatMs));
        Assert.Equal((340, 250, 620), (MotionTimings.ThudMs, MotionTimings.ShiverMs, MotionTimings.RevealMs));
    }

    [Fact]
    public void TheBarBloomIsDarkUntilEightyPercentThenFlashesOut()
    {
        var bloom = MotionTimings.BarBloom();
        Assert.Equal(0, Keyframes.Sample(bloom, 0, 400), 9);
        Assert.Equal(0.5, Keyframes.Sample(bloom, 0, 540), 9);
        Assert.Equal(1, Keyframes.Sample(bloom, 0, 600), 9);
        Assert.Equal(0, Keyframes.Sample(bloom, 0, 1050), 9);
    }

    // ---- THUD and the curves ------------------------------------------------------------------

    [Fact]
    public void TheThudOvershootsAndSettles()
    {
        Assert.Equal((0.2, 1.5, 0.4, 1.0), Easings.ThudCurve);
        Assert.Equal(0, Easings.Thud(0));
        Assert.Equal(1, Easings.Thud(1));
        double peak = Enumerable.Range(1, 99).Max(i => Easings.Thud(i / 100.0));
        Assert.True(peak > 1.05, "the thud should overshoot, got " + peak);
        double last = 0;
        for (int i = 1; i <= 100; i++)
        {
            double v = CubicBezier.Ease(i / 100.0, 0.25, 0.1, 0.25, 1);
            Assert.True(v >= last - 1e-9);
            last = v;
        }
        // CSS "ease-in-out" (.42,0,.58,1) is symmetric about the middle.
        Assert.Equal(0.5, CubicBezier.Ease(0.5, 0.42, 0, 0.58, 1), 4);
    }

    [Fact]
    public void TheWpfEasesMirrorTheWayWpfMirrorsThem()
    {
        foreach (var t in new[] { 0.0, 0.1, 0.25, 0.5, 0.8, 1.0 })
        {
            Assert.Equal(1 - (1 - t) * (1 - t), Easings.QuadOut(t), 12);
            Assert.Equal(Math.Sin(Math.PI / 2 * t), Easings.SineOut(t), 12);
            Assert.Equal(1 - Math.Pow(1 - t, 3), Easings.CubicOut(t), 12);
            Assert.Equal(1 - Easings.QuadInOut(1 - t), Easings.QuadInOut(t), 12);
            Assert.Equal(1 - Easings.SineInOut(1 - t), Easings.SineInOut(t), 12);
        }
        Assert.Equal(0.5, Easings.QuadInOut(0.5), 12);
        Assert.Equal(0.125, Easings.QuadInOut(0.25), 12);
        // BackEase EaseOut overshoots past 1 (the Leash explainer's thud).
        Assert.True(Enumerable.Range(1, 99).Max(i => Easings.BackOut(i / 100.0, Easings.LeashThudAmplitude)) > 1.0);
        Assert.Equal(0.5, Easings.Smoothstep(0.5), 12);
    }

    [Fact]
    public void ThePopPeaksAtThirtyFivePercentAndSettlesOnOne()
    {
        var pop = Keyframes.Pop(MotionTimings.TilePopScale);
        Assert.Equal(MotionTimings.PopMs * 0.35, pop[0].TimeMs, 9);
        Assert.Equal(1.06, Keyframes.Sample(pop, 1.0, MotionTimings.PopMs * 0.35), 9);
        Assert.Equal(1.0, Keyframes.Sample(pop, 1.0, MotionTimings.PopMs), 9);
        Assert.Equal(1.0, Keyframes.Sample(pop, 1.0, 0), 9);
        Assert.Equal(1.0, Keyframes.Sample(pop, 1.0, 10_000), 9);
        for (int ms = 0; ms <= MotionTimings.PopMs; ms += 10)
            Assert.InRange(Keyframes.Sample(pop, 1.0, ms), 1.0, 1.06 + 1e-9);
    }

    [Fact]
    public void TheBreakoutSpringsRingAndRest()
    {
        // feedback.js reads t as the share of the effect still to run: 1 = the hit, 0 = at rest.
        Assert.Equal((1.0, 1.0), Easings.Jelly(0));
        var hit = Easings.Jelly(1);
        Assert.Equal(0.8, hit.Along, 9);
        Assert.Equal(1.16, hit.Across, 9);
        var flat = Easings.Squash(1);
        Assert.Equal(1 - .38, flat.Along, 9);
        Assert.Equal(1 + .24, flat.Across, 9);
        var rest = Easings.Squash(0);
        Assert.Equal(1.0, rest.Along, 9);
        Assert.Equal(1.0, rest.Across, 9);
        Assert.Equal(1.0, Easings.PushIn(0), 9);
        Assert.Equal(1.06, Easings.PushIn(0.2), 9);
        Assert.Equal(1.0, Easings.PushIn(Easings.PushInSeconds));
        Assert.Equal(1.0, Easings.DampedSpring(0, 4.2, 5), 12);
        Assert.True(Math.Abs(Easings.DampedSpring(1, 4.2, 5)) < 0.02);
    }

    [Fact]
    public void DiscreteHoldsAndTheTrackHoldsItsEnd()
    {
        var track = new[]
        {
            new Keyframe(100, 5, EaseKind.Discrete),
            new Keyframe(200, 9, EaseKind.Linear),
        };
        Assert.Equal(2, Keyframes.Sample(track, 2, 99.9), 9);
        Assert.Equal(5, Keyframes.Sample(track, 2, 100), 9);
        Assert.Equal(7, Keyframes.Sample(track, 2, 150), 9);
        Assert.Equal(9, Keyframes.Sample(track, 2, 999), 9);
        Assert.Equal(2, Keyframes.Sample(Array.Empty<Keyframe>(), 2, 50), 9);
    }
}
