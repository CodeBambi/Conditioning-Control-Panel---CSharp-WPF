using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Afterglow: the pure maths behind the ghost a subliminal word leaves (mockup mkSubs).
/// Font is 34 px (unit 1, the mockup's own scale) unless a test says so.
/// </summary>
public class AfterglowFieldTests
{
    private const double Fs = 34;
    private static Func<double> Fixed(double v) => () => v;

    private static AfterglowField FieldWithGhost(out AfterglowGhost g, double delay = 0)
    {
        var f = new AfterglowField();
        g = f.Spawn("DROP", 0, 500, 400, Fs, 1, delay);
        return f;
    }

    // Steps with the cursor far away, in small ticks like the engine.
    private static void Run(AfterglowField f, double seconds, MotionLevel level = MotionLevel.Full, Func<double>? rnd = null)
    {
        for (double t = 0; t < seconds - 1e-9; t += 0.02)
            f.Step(0.02, false, 0, 0, level, rnd ?? Fixed(0.99));
    }

    [Fact]
    public void Alpha_HandsOverFromTheCard_ThenSettlesAtThirtyFivePercent_ThenFadesOverFourSeconds()
    {
        Assert.Equal(0, AfterglowField.Alpha(-0.1, 99, MotionLevel.Full, false));
        Assert.Equal(1, AfterglowField.Alpha(0, 99, MotionLevel.Full, false), 6);
        Assert.InRange(AfterglowField.Alpha(0.16, 99, MotionLevel.Full, false), 0.33, 0.351);
        Assert.Equal(0.35 * 0.5, AfterglowField.Alpha(2, 99, MotionLevel.Full, false), 6);
        Assert.Equal(0, AfterglowField.Alpha(4, 99, MotionLevel.Full, false), 6);
    }

    [Fact]
    public void Alpha_WakeFlaresToFull_AndFadesOverPointFour_OrPointOneTwoAtMotionOff()
    {
        Assert.Equal(1, AfterglowField.Alpha(2, 0, MotionLevel.Full, false), 6);
        Assert.Equal(0.5, AfterglowField.Alpha(2, 0.2, MotionLevel.Full, false), 6);
        Assert.Equal(0.35 * 0.5, AfterglowField.Alpha(2, 0.4, MotionLevel.Full, false), 6);
        Assert.Equal(0.35 * 0.5, AfterglowField.Alpha(2, 0.13, MotionLevel.Off, false), 6);
    }

    [Fact]
    public void Alpha_Photosafe_SoftensTheFlarePeak()
    {
        Assert.Equal(AfterglowField.PhotosafeFlarePeak, AfterglowField.Alpha(2, 0, MotionLevel.Full, true), 6);
    }

    [Theory]
    [InlineData(54, 1, 9, 1, true)]      // inside 1.6 x 34 = 54.4
    [InlineData(55, 1, 9, 1, false)]     // just outside
    [InlineData(10, 0.5, 9, 1, false)]   // too young (needs age > 0.5)
    [InlineData(10, 1, 1.5, 1, false)]   // cooling down (needs > 1.5 s)
    [InlineData(10, 1, 9, 0.15, false)]  // mostly gone (needs base > 0.15)
    public void ShouldWake_FollowsTheMockupRule(double dist, double age, double since, double baseLeft, bool expect)
        => Assert.Equal(expect, AfterglowField.ShouldWake(dist, Fs, age, since, baseLeft));

    [Fact]
    public void GhostIsInertUntilTheCardHandsOver_AndLeavesAtFourPointTwoSeconds()
    {
        var f = FieldWithGhost(out var g, delay: 0.3);
        f.Step(0.1, true, g.X, g.Y, MotionLevel.Full, Fixed(0.0));
        Assert.Equal(0, f.ParticleCount);          // not born: no embers, no wake
        Assert.Equal(-9, g.Wake);
        Run(f, 4.3);
        Assert.Single(f.Ghosts);
        Run(f, 0.2);
        Assert.Empty(f.Ghosts);
    }

    [Fact]
    public void CursorNearASettledGhost_WakesIt_WithFourteenSparksAndARing_OncePerCooldown()
    {
        var f = FieldWithGhost(out var g);
        Run(f, 0.6);
        var woken = new List<AfterglowGhost>();
        f.Step(0.02, true, g.X + 20, g.Y, MotionLevel.Full, Fixed(0.99), woken);
        Assert.Single(woken);
        Assert.Equal(14, f.ParticleCount);
        Assert.Single(f.Rings);

        woken.Clear();
        for (int i = 0; i < 50; i++) f.Step(0.02, true, g.X, g.Y, MotionLevel.Full, Fixed(0.99), woken);  // 1.0 s
        Assert.Empty(woken);
        for (int i = 0; i < 30; i++) f.Step(0.02, true, g.X, g.Y, MotionLevel.Full, Fixed(0.99), woken);  // past 1.5 s
        Assert.Single(woken);
    }

    [Fact]
    public void Wake_AtMotionOff_FlaresWithoutSparksOrRings()
    {
        var f = FieldWithGhost(out var g);
        Run(f, 0.6, MotionLevel.Off);
        var woken = new List<AfterglowGhost>();
        f.Step(0.02, true, g.X, g.Y, MotionLevel.Off, Fixed(0.0), woken);
        Assert.Single(woken);
        Assert.Equal(0, f.ParticleCount);
        Assert.Empty(f.Rings);
    }

    [Fact]
    public void Embers_RiseEighteenToThirtyTwoPxPerSecond_AndLiveOnePointTwoSeconds_ScaledByFont()
    {
        var f = FieldWithGhost(out _);
        Run(f, 0.32);                                    // past the 0.3 s ember gate
        f.Step(0.02, false, 0, 0, MotionLevel.Full, Fixed(0.0));   // rnd 0 always emits
        Assert.True(f.ParticleCount >= 1);
        var p = f.Particles[f.ParticleCount - 1];
        Assert.InRange(-p.Vy, 17.9 * 0.96, 32.1);         // one tick of drag already applied
        Assert.Equal(1.2, p.Life, 6);

        var big = new AfterglowField();
        big.Spawn("DROP", 0, 0, 0, Fs * 2, 1, 0);
        for (int i = 0; i < 17; i++) big.Step(0.02, false, 0, 0, MotionLevel.Full, Fixed(0.99));
        big.Step(0.02, false, 0, 0, MotionLevel.Full, Fixed(0.0));
        Assert.Equal(2, big.Particles[big.ParticleCount - 1].Unit, 6);
        Assert.InRange(-big.Particles[big.ParticleCount - 1].Vy, 2 * 17.9 * 0.96, 2 * 18.1);
    }

    [Fact]
    public void Reduced_HalvesEmberSpeedAndRingGrowth_Off_HasNoEmbers()
    {
        var f = FieldWithGhost(out _);
        Run(f, 0.32, MotionLevel.Reduced);
        f.Step(0.02, false, 0, 0, MotionLevel.Reduced, Fixed(0.0));
        Assert.InRange(-f.Particles[f.ParticleCount - 1].Vy, 8.6, 9.01);

        var r = new AfterglowRing { FontPx = Fs, T0 = 0 };
        Assert.Equal(Fs * 2.8, AfterglowField.RingRadius(r, 0.6, MotionLevel.Full), 6);
        Assert.Equal(Fs * 1.7, AfterglowField.RingRadius(r, 0.6, MotionLevel.Reduced), 6);

        var off = FieldWithGhost(out _);
        Run(off, 3, MotionLevel.Off, Fixed(0.0));
        Assert.Equal(0, off.ParticleCount);
    }

    [Fact]
    public void AtMostSixGhostsPerScreen_TheOldestGoes_OtherScreensUntouched()
    {
        var f = new AfterglowField();
        f.Spawn("other", 1, 0, 0, Fs, 1, 0);
        for (int i = 0; i < 7; i++) { f.Spawn("w" + i, 0, 0, 0, Fs, 1, 0); f.Step(0.1, false, 0, 0, MotionLevel.Full, Fixed(0.99)); }
        Assert.Equal(6, f.Ghosts.Count(g => g.Screen == 0));
        Assert.DoesNotContain(f.Ghosts, g => g.Text == "w0");
        Assert.Contains(f.Ghosts, g => g.Text == "other");
    }

    [Fact]
    public void Clear_DropsEverythingAtOnce()
    {
        var f = FieldWithGhost(out var g);
        Run(f, 0.6);
        f.Step(0.02, true, g.X, g.Y, MotionLevel.Full, Fixed(0.5));
        Assert.False(f.IsEmpty);
        f.Clear();
        Assert.True(f.IsEmpty);
    }

    [Fact]
    public void ParticlePool_NeverGrowsPastFourHundred()
    {
        var f = new AfterglowField();
        for (int i = 0; i < 40; i++)
        {
            var g = f.Spawn("w", i, i * 1000, 0, Fs, 1, 0);
            g.Born = -1;
        }
        f.Step(0.02, true, 0, 0, MotionLevel.Full, Fixed(0.0));
        for (int i = 0; i < 40; i++) f.Step(0.001, false, 0, 0, MotionLevel.Full, Fixed(0.0));
        Assert.True(f.ParticleCount <= AfterglowField.MaxParticles);
    }

    [Fact]
    public void PickWeighted_NoBonusIsUniform_AWokenWordWeighsOneMore()
    {
        var words = new[] { "A", "B", "C" };
        Assert.Equal(0, AfterglowField.PickWeighted(words, null, 0.0));
        Assert.Equal(1, AfterglowField.PickWeighted(words, null, 0.5));
        Assert.Equal(2, AfterglowField.PickWeighted(words, null, 0.99));
        var bonus = new Dictionary<string, int> { ["B"] = 1 };
        // weights 1,2,1 over 4: B owns [0.25, 0.75)
        Assert.Equal(0, AfterglowField.PickWeighted(words, bonus, 0.24));
        Assert.Equal(1, AfterglowField.PickWeighted(words, bonus, 0.26));
        Assert.Equal(1, AfterglowField.PickWeighted(words, bonus, 0.74));
        Assert.Equal(2, AfterglowField.PickWeighted(words, bonus, 0.76));
        Assert.Equal(-1, AfterglowField.PickWeighted(Array.Empty<string>(), bonus, 0.5));
    }
}
