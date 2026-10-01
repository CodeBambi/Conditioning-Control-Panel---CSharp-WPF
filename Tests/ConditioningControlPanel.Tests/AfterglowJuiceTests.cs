using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Afterglow juice round: the arrival (THUD), the exit drift, the pop-instant halo and chromatic
/// split, falling sparks that cool, eased echoes. Feel only: life, counts and placement never move.
/// </summary>
public class AfterglowJuiceTests
{
    private static Func<double> Fixed(double v) => () => v;

    private static double MaxScale(MotionLevel level, bool photosafe, out double first)
    {
        first = AfterglowField.PopScale(0, level, photosafe);
        double max = 0;
        for (double t = 0; t <= AfterglowField.ArriveS; t += 0.002) max = Math.Max(max, AfterglowField.PopScale(t, level, photosafe));
        return max;
    }

    [Fact]
    public void Arrival_GrowsInFromSixtyPercent_WithOneSmallOvershoot_ThenSettles()
    {
        double max = MaxScale(MotionLevel.Full, false, out var first);
        Assert.Equal(AfterglowField.ArriveFrom, first, 6);
        Assert.InRange(max, 1.03, 1.08);   // the house overshoot band, 3-8%
        Assert.Equal(1, AfterglowField.PopScale(AfterglowField.ArriveS, MotionLevel.Full, false), 6);
        // Holds still at full size until the fade-out starts.
        Assert.Equal(1, AfterglowField.PopScale(AfterglowField.FadeInS + AfterglowField.HoldS, MotionLevel.Full, false), 6);
    }

    [Fact]
    public void Arrival_Reduced_HalfTheAmplitude_Photosafe_NoOvershoot_Off_Still()
    {
        double full = MaxScale(MotionLevel.Full, false, out _);
        double half = MaxScale(MotionLevel.Reduced, false, out var first);
        Assert.Equal(1 - (1 - AfterglowField.ArriveFrom) / 2, first, 6);
        Assert.True(half - 1 < (full - 1) / 2 + 1e-9, $"reduced overshoot {half} vs full {full}");
        Assert.True(MaxScale(MotionLevel.Full, true, out _) <= 1 + 1e-9, "photosafe never overshoots");
        for (double t = 0; t < 0.2; t += 0.01)
        {
            Assert.Equal(1, AfterglowField.PopScale(t, MotionLevel.Off, false));
            Assert.Equal(0, AfterglowField.FromCursor(t, MotionLevel.Off, false));
        }
    }

    [Fact]
    public void Arrival_FitsAShortDuration()
    {
        double f = AfterglowOptions.DurationFactor(AfterglowOptions.DurationMin);
        Assert.True(AfterglowField.ArriveTime(f) < AfterglowField.Life(MotionLevel.Full, false, f));
        Assert.Equal(AfterglowField.ArriveS, AfterglowField.ArriveTime(AfterglowOptions.DurationFactor(AfterglowOptions.DurationMax)), 9);
    }

    [Fact]
    public void Arrival_ComesFromTheCursor()
    {
        Assert.Equal(AfterglowField.FromCursorShare, AfterglowField.FromCursor(0, MotionLevel.Full, false), 6);
        Assert.Equal(AfterglowField.FromCursorShare / 2, AfterglowField.FromCursor(0, MotionLevel.Reduced, false), 6);
        Assert.Equal(0, AfterglowField.FromCursor(AfterglowField.ArriveS, MotionLevel.Full, false), 6);

        var f = new AfterglowField();
        var p = f.Spawn("DROP", 600, 500, 1, MotionLevel.Full, false, Fixed(0.5), 500, 500);
        Assert.Equal(-100, p.FromDx, 6);
        Assert.Equal(0, p.FromDy, 6);
        var q = f.Spawn("SINK", 600, 500, 1, MotionLevel.Full, false, Fixed(0.5));
        Assert.Equal(0, q.FromDx);   // no cursor given: no travel
    }

    [Fact]
    public void Exit_ShrinksALittle_EasingIn()
    {
        double life = AfterglowField.Life(MotionLevel.Full, false);
        double end = AfterglowField.PopScale(life, MotionLevel.Full, false);
        Assert.Equal(1 - AfterglowField.ExitShrink, end, 4);
        double outStart = AfterglowField.FadeInS + AfterglowField.HoldS;
        double mid = AfterglowField.PopScale(outStart + AfterglowField.FadeOutS / 2, MotionLevel.Full, false);
        Assert.True(1 - mid < AfterglowField.ExitShrink / 2, "ease-in: less than half the shrink at the half-way mark");
    }

    [Fact]
    public void Exit_WordAtRest_RisesAsItFades_Off_StaysPut()
    {
        var f = new AfterglowField();
        var p = f.Spawn("DROP", 300, 300, 1, MotionLevel.Full, false, Fixed(0.5));
        Assert.Equal(0, p.Vx);   // no momentum, no jitter at rnd 0.5
        double life = AfterglowField.Life(MotionLevel.Full, false);
        for (double t = 0; t < life - 0.011; t += 0.01) f.Step(0.01, MotionLevel.Full, false, Fixed(0.5));
        Assert.Equal(300, p.X, 6);
        Assert.InRange(300 - p.Y, AfterglowField.ExitDriftDip * 0.7, AfterglowField.ExitDriftDip + 1e-6);
    }

    [Fact]
    public void Exit_DriftsAlongTheMomentum()
    {
        var f = new AfterglowField();
        f.SampleCursor(0.033, 0, 0);
        for (int i = 1; i <= 15; i++) f.SampleCursor(0.033, i * 33, 0);   // about 1000 px/s to the right
        var p = f.Spawn("DROP", 0, 0, 1, MotionLevel.Full, false, Fixed(0.5));
        Assert.Equal(1, p.ExitUx, 6);
        Assert.Equal(0, p.ExitUy, 6);

        // Same momentum, no exit drift: the drag-only path. The juiced word goes further.
        double v = p.Vx, dragOnly = 0;
        double life = AfterglowField.Life(MotionLevel.Full, false);
        for (double t = 0; t < life - 0.011; t += 0.01) { dragOnly += v * 0.01; v *= Math.Exp(-AfterglowField.Drag * 0.01); }
        for (double t = 0; t < life - 0.011; t += 0.01) f.Step(0.01, MotionLevel.Full, false, Fixed(0.5));
        Assert.True(p.X > dragOnly + AfterglowField.ExitDriftDip * 0.6, $"x={p.X} drag-only={dragOnly}");
    }

    [Fact]
    public void PopInstant_HaloAndChroma_OpenThenCloseFast_NoneOffOrPhotosafe()
    {
        double peak = 0;
        for (double t = 0; t < AfterglowField.HaloS; t += 0.002) peak = Math.Max(peak, AfterglowField.HaloAlpha(t, MotionLevel.Full, false));
        Assert.InRange(peak, AfterglowField.HaloPeak * 0.9, AfterglowField.HaloPeak + 1e-9);
        Assert.Equal(0, AfterglowField.HaloAlpha(AfterglowField.HaloS, MotionLevel.Full, false));
        Assert.True(AfterglowField.HaloSpread(AfterglowField.HaloS) > AfterglowField.HaloSpread(0));

        Assert.Equal(AfterglowField.ChromaDip, AfterglowField.ChromaOffset(0, MotionLevel.Full, false), 6);
        Assert.Equal(AfterglowField.ChromaDip / 2, AfterglowField.ChromaOffset(0, MotionLevel.Reduced, false), 6);
        Assert.Equal(0, AfterglowField.ChromaOffset(AfterglowField.ChromaS, MotionLevel.Full, false));

        foreach (var (level, safe) in new[] { (MotionLevel.Off, false), (MotionLevel.Full, true), (MotionLevel.Reduced, true) })
            for (double t = 0; t < 0.3; t += 0.01)
            {
                Assert.Equal(0, AfterglowField.HaloAlpha(t, level, safe));
                Assert.Equal(0, AfterglowField.ChromaOffset(t, level, safe));
                Assert.Equal(0, AfterglowField.ChromaAlpha(t, level, safe));
            }
    }

    [Fact]
    public void Tilt_SwingsInWithTheArrival()
    {
        Assert.Equal(0, AfterglowField.TiltSwing(0, MotionLevel.Full, false), 6);
        Assert.Equal(1, AfterglowField.TiltSwing(AfterglowField.ArriveS, MotionLevel.Full, false), 6);
        Assert.Equal(1, AfterglowField.TiltSwing(0, MotionLevel.Off, false));
    }

    [Fact]
    public void Sparks_FallUnderGravity_AndCoolToTheGlowColour()
    {
        var f = new AfterglowField();
        f.Spawn("DROP", 0, 0, 1, MotionLevel.Full, false, Fixed(0.5));   // rnd 0.5: no jitter, every burst spark flies flat left
        for (int i = 0; i < f.ParticleCount; i++) Assert.Equal(0, f.Particles[i].Vy, 9);
        f.Step(0.05, MotionLevel.Full, false, Fixed(0.5));
        for (int i = 0; i < f.ParticleCount; i++) Assert.True(f.Particles[i].Vy > 0 && f.Particles[i].Y > 0, "sparks arc down");

        var hot = new AfterglowParticle { T = 0, Life = 0.5 };
        var mid = new AfterglowParticle { T = 0.25, Life = 0.5 };
        var cold = new AfterglowParticle { T = 0.5, Life = 0.5 };
        Assert.Equal(1, AfterglowField.SparkHeat(hot));
        Assert.Equal(0.25, AfterglowField.SparkHeat(mid), 6);
        Assert.Equal(0, AfterglowField.SparkHeat(cold));
    }

    [Fact]
    public void Echoes_FadeWithEase_AndShrink()
    {
        for (int k = 0; k < AfterglowField.TrailLen - 1; k++)
        {
            Assert.True(AfterglowField.TrailAlpha(k) > AfterglowField.TrailAlpha(k + 1));
            Assert.True(AfterglowField.TrailScale(k) > AfterglowField.TrailScale(k + 1));
        }
        Assert.InRange(AfterglowField.TrailAlpha(0), 0.45, 0.5);   // the nearest echo stays close to full
        Assert.True(AfterglowField.TrailScale(AfterglowField.TrailLen) >= 0.6);
    }
}
