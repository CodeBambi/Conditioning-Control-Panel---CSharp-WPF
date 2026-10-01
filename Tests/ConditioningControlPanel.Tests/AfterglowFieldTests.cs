using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Afterglow: the pure maths behind a word popping near the mouse for a split second,
/// keeping a little of its momentum, with a short trail and sparks. Scale 1 unless a test says so.
/// </summary>
public class AfterglowFieldTests
{
    private static Func<double> Fixed(double v) => () => v;

    private static void Run(AfterglowField f, double seconds, MotionLevel level = MotionLevel.Full, bool photosafe = false, Func<double>? rnd = null)
    {
        for (double t = 0; t < seconds - 1e-9; t += 0.01)
            f.Step(0.01, level, photosafe, rnd ?? Fixed(0.5));
    }

    // Feeds a steady cursor motion for long enough that the smoothed velocity settles.
    private static void Swipe(AfterglowField f, double vx, double vy, double seconds = 0.5)
    {
        double x = 500, y = 500;
        f.SampleCursor(0.033, x, y);
        for (double t = 0; t < seconds; t += 0.033)
        {
            x += vx * 0.033; y += vy * 0.033;
            f.SampleCursor(0.033, x, y);
        }
    }

    [Fact]
    public void Alpha_IsASplitSecond_FadeInHoldFadeOut()
    {
        Assert.Equal(0, AfterglowField.Alpha(-0.01, MotionLevel.Full, false));
        Assert.Equal(0.5, AfterglowField.Alpha(0.03, MotionLevel.Full, false), 6);
        Assert.Equal(1, AfterglowField.Alpha(0.1, MotionLevel.Full, false), 6);
        Assert.Equal(0.5, AfterglowField.Alpha(0.31, MotionLevel.Full, false), 6);
        Assert.Equal(0, AfterglowField.Alpha(0.42, MotionLevel.Full, false), 6);
        Assert.InRange(AfterglowField.Life(MotionLevel.Full, false), 0.3, 0.5);
    }

    [Fact]
    public void Alpha_MotionOff_IsOnlyA120msFade()
    {
        Assert.Equal(1, AfterglowField.Alpha(0, MotionLevel.Off, false), 6);
        Assert.Equal(0.5, AfterglowField.Alpha(0.06, MotionLevel.Off, false), 6);
        Assert.Equal(0, AfterglowField.Alpha(0.12, MotionLevel.Off, false), 6);
        Assert.Equal(0.12, AfterglowField.Life(MotionLevel.Off, false), 6);
    }

    [Fact]
    public void Alpha_Photosafe_SoftInAndHalfSecondFadeOut()
    {
        Assert.Equal(0.5, AfterglowField.Alpha(0.06, MotionLevel.Full, true), 6);
        Assert.Equal(1, AfterglowField.Alpha(0.2, MotionLevel.Full, true), 6);
        Assert.Equal(0.5, AfterglowField.Alpha(0.22 + 0.25, MotionLevel.Full, true), 6);
        Assert.Equal(0.72, AfterglowField.Life(MotionLevel.Full, true), 6);
    }

    [Theory]
    [InlineData(0.0, 1.5)]
    [InlineData(0.5, 3.0)]
    [InlineData(0.999999, 4.5)]
    public void NextInterval_RollsBetweenOneAndAHalfAndFourAndAHalfSeconds(double r, double expected)
        => Assert.Equal(expected, AfterglowField.NextInterval(r), 4);

    [Fact]
    public void SpawnPoint_LandsFiftyToOneSixtyDipFromTheCursor_ScaledByDpi()
    {
        AfterglowField.SpawnPoint(1000, 500, 1, 0, 0, 2000, 1000, 10, 10, MotionLevel.Full, 0, 0, out var x, out var y);
        Assert.Equal(1050, x, 6); Assert.Equal(500, y, 6);
        AfterglowField.SpawnPoint(1000, 500, 1.5, 0, 0, 2000, 1000, 10, 10, MotionLevel.Full, 0.25, 1, out x, out y);
        Assert.Equal(1000, x, 6); Assert.Equal(500 + 160 * 1.5, y, 6);
    }

    [Fact]
    public void SpawnPoint_Reduced_GoesHalfAsFar()
    {
        AfterglowField.SpawnPoint(1000, 500, 1, 0, 0, 2000, 1000, 10, 10, MotionLevel.Reduced, 0, 1, out var x, out _);
        Assert.Equal(1080, x, 6);
    }

    [Fact]
    public void SpawnPoint_ClampsTheWordOnScreen_AndCentresOnATinyScreen()
    {
        // Cursor in the top-right corner of a second monitor, offset pointing off-screen.
        AfterglowField.SpawnPoint(2550, 5, 1, 1920, 0, 2560, 1440, 60, 20, MotionLevel.Full, 0, 1, out var x, out var y);
        Assert.Equal(2560 - 60, x, 6);
        Assert.Equal(20, y, 6);
        AfterglowField.SpawnPoint(50, 50, 1, 0, 0, 100, 100, 80, 20, MotionLevel.Full, 0, 1, out x, out _);
        Assert.Equal(50, x, 6);
    }

    [Fact]
    public void Cursor_VelocityIsSmoothed_AndATeleportGapRestartsFromRest()
    {
        var f = new AfterglowField();
        Swipe(f, 1000, 0);
        Assert.InRange(f.CursorVx, 950, 1001);
        Assert.Equal(0, f.CursorVy, 3);
        f.SampleCursor(0.5, 9000, 9000);   // window lost focus, came back elsewhere
        Assert.Equal(0, f.CursorVx);
    }

    [Fact]
    public void Cursor_OneJerkDoesNotFling()
    {
        var f = new AfterglowField();
        f.SampleCursor(0.033, 0, 0);
        f.SampleCursor(0.033, 0, 0);
        f.SampleCursor(0.033, 3000, 0);   // a 90000 px/s jump
        Assert.True(f.CursorVx <= AfterglowField.MaxCursorSpeedPx);
        Assert.True(f.CursorVx < AfterglowField.MaxCursorSpeedPx * 0.4);
    }

    [Fact]
    public void Spawn_KeepsSomeOfTheMousesMomentum_ThenDragSlowsIt()
    {
        var f = new AfterglowField();
        Swipe(f, 1000, 0);
        var p = f.Spawn("DROP", 500, 500, 1, MotionLevel.Full, true, Fixed(0.5));
        Assert.InRange(p.Vx, 0.55 * 950, 0.65 * 1001);
        var v0 = p.Vx;
        Run(f, 0.1, photosafe: true);
        Assert.True(p.X > 520, $"drifted with the mouse, x={p.X}");
        Assert.True(p.Vx < v0 * 0.7, "drag slows it");
    }

    [Fact]
    public void Spawn_Reduced_HalfTheMomentum_Off_NoneAndNoMovement()
    {
        var f = new AfterglowField();
        Swipe(f, 1000, 0);
        var full = f.Spawn("A", 0, 0, 1, MotionLevel.Full, true, Fixed(0.5)).Vx;
        var half = f.Spawn("B", 0, 0, 1, MotionLevel.Reduced, true, Fixed(0.5)).Vx;
        Assert.Equal(full / 2, half, 6);

        var g = new AfterglowField();
        Swipe(g, 1000, 0);
        var p = g.Spawn("C", 300, 300, 1, MotionLevel.Off, false, Fixed(0.9));
        Assert.Equal(0, p.Vx);
        Run(g, 0.08, MotionLevel.Off);
        Assert.Equal(300, p.X);
        Assert.Equal(1, p.TrailCount);       // no echoes
        Assert.Equal(0, g.ParticleCount);    // no sparks
        Run(g, 0.05, MotionLevel.Off);
        Assert.True(g.IsEmpty);              // gone after the 120 ms fade
    }

    [Fact]
    public void AtMostTwoAlive_TheOldestGoes()
    {
        var f = new AfterglowField();
        f.Spawn("ONE", 0, 0, 1, MotionLevel.Full, true, Fixed(0.5));
        f.Spawn("TWO", 0, 0, 1, MotionLevel.Full, true, Fixed(0.5));
        f.Spawn("THREE", 0, 0, 1, MotionLevel.Full, true, Fixed(0.5));
        Assert.Equal(2, f.Pops.Count);
        Assert.Equal("TWO", f.Pops[0].Text);
    }

    [Fact]
    public void PopsDieAtTheEndOfTheirLife()
    {
        var f = new AfterglowField();
        f.Spawn("DROP", 0, 0, 1, MotionLevel.Full, true, Fixed(0.5));
        Run(f, 0.4, photosafe: false);
        Assert.Single(f.Pops);
        Run(f, 0.03, photosafe: false);
        Assert.Empty(f.Pops);
    }

    [Fact]
    public void Trail_RecordsAShortRingBufferOfPositions_NewestFirst()
    {
        var f = new AfterglowField();
        Swipe(f, 1000, 0);
        var p = f.Spawn("DROP", 0, 0, 1, MotionLevel.Full, true, Fixed(0.5));
        Run(f, 0.3, photosafe: true);
        Assert.Equal(AfterglowField.TrailLen, p.TrailCount);
        p.TrailAt(0, out var x0, out _);
        p.TrailAt(AfterglowField.TrailLen - 1, out var xOld, out _);
        Assert.True(x0 > xOld, "the echoes trail behind the direction of travel");
        Assert.True(AfterglowField.TrailAlpha(0) > AfterglowField.TrailAlpha(4));
        Assert.Equal(0, AfterglowField.TrailAlpha(AfterglowField.TrailLen));
    }

    [Fact]
    public void Sparks_BurstAtBirth_AndAlongThePath_InheritingTheWordsVelocity()
    {
        var f = new AfterglowField();
        Swipe(f, 1500, 0);
        f.Spawn("DROP", 0, 0, 1, MotionLevel.Full, false, Fixed(0.5));
        Assert.Equal(AfterglowField.BirthBurst, f.ParticleCount);
        // rnd 0.5: burst sparks sit at angle pi; their velocity carries the word's share forward.
        Run(f, 0.1);
        Assert.True(f.ParticleCount > AfterglowField.BirthBurst, "path sparks along the travel");
        double avgVx = 0;
        for (int i = AfterglowField.BirthBurst; i < f.ParticleCount; i++) avgVx += f.Particles[i].Vx;
        Assert.True(avgVx > 0, "path sparks drift the way the word goes");
    }

    [Fact]
    public void Sparks_Reduced_Fewer_Photosafe_NoBirthBurst()
    {
        var f = new AfterglowField();
        f.Spawn("A", 0, 0, 1, MotionLevel.Reduced, false, Fixed(0.5));
        Assert.Equal(AfterglowField.BirthBurst / 2, f.ParticleCount);
        var g = new AfterglowField();
        g.Spawn("B", 0, 0, 1, MotionLevel.Full, true, Fixed(0.5));
        Assert.Equal(0, g.ParticleCount);
    }

    [Fact]
    public void Particles_ArePooled_AndCapped()
    {
        var f = new AfterglowField();
        for (int i = 0; i < 40; i++) f.Spawn("X", 0, 0, 1, MotionLevel.Full, false, Fixed(0.3));
        Assert.Equal(AfterglowField.MaxParticles, f.ParticleCount);
        Run(f, 1);
        Assert.Equal(0, f.ParticleCount);
        Assert.True(f.IsEmpty);
    }

    [Fact]
    public void Clear_DropsEverythingAtOnce()
    {
        var f = new AfterglowField();
        Swipe(f, 800, 300);
        f.Spawn("DROP", 0, 0, 1, MotionLevel.Full, false, Fixed(0.5));
        Run(f, 0.05);
        Assert.False(f.IsEmpty);
        f.Clear();
        Assert.True(f.IsEmpty);
        Assert.Empty(f.Pops);
        Assert.Equal(0, f.ParticleCount);
    }
}
