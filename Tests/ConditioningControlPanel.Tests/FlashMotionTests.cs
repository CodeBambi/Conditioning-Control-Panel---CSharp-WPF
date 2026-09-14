using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Flashes v2 (Drift and Bounce, Pendulum): the pure motion maths the compositor FlashLayer
/// steps every tick. Bounds are a 1080p monitor at the origin unless a test says otherwise.
/// </summary>
public class FlashMotionTests
{
    private const double BW = 1920, BH = 1080;

    private static FlashMotionState Drift(int seed, MotionLevel level = MotionLevel.Full,
        double x = 400, double y = 300, double w = 768, double h = 432)
        => FlashMotion.Create(FlashMotionStyle.DriftBounce, x, y, w, h, 0, 0, BW, BH, level, new Random(seed));

    private static FlashMotionState Pendulum(int seed, MotionLevel level = MotionLevel.Full,
        double x = 400, double y = 300, double w = 768, double h = 432)
        => FlashMotion.Create(FlashMotionStyle.Pendulum, x, y, w, h, 0, 0, BW, BH, level, new Random(seed));

    private static void AssertInside(FlashMotionState s, double slack = 0.5)
    {
        Assert.True(s.X >= s.BoundsX - slack, $"left {s.X}");
        Assert.True(s.Y >= s.BoundsY - slack, $"top {s.Y}");
        Assert.True(s.X + s.W <= s.BoundsX + s.BoundsW + slack, $"right {s.X + s.W}");
        Assert.True(s.Y + s.H <= s.BoundsY + s.BoundsH + slack, $"bottom {s.Y + s.H}");
    }

    // ---- Drift and Bounce ----

    [Theory]
    [InlineData(1)] [InlineData(7)] [InlineData(42)] [InlineData(1234)] [InlineData(99999)]
    public void Drift_StaysInsideTheMonitor_ForThirtySeconds(int seed)
    {
        var s = Drift(seed);
        for (int i = 0; i < 30 * 60; i++)
        {
            FlashMotion.Step(s, 1.0 / 60);
            AssertInside(s);
        }
    }

    [Fact]
    public void Drift_BounceReflectsOffTheRightEdge_AndKeepsTheRectInside()
    {
        var s = Drift(3);
        s.X = BW - s.W - 1; s.Vx = 600; s.Vy = 0;
        Assert.True(FlashMotion.Step(s, 0.1));
        Assert.True(s.Vx < 0, "velocity must flip");
        AssertInside(s);
    }

    [Fact]
    public void Drift_BounceReflectsOffTheTopEdge_AndKeepsTheRectInside()
    {
        var s = Drift(3);
        s.Y = 1; s.Vx = 0; s.Vy = -600;
        Assert.True(FlashMotion.Step(s, 0.1));
        Assert.True(s.Vy > 0, "velocity must flip");
        AssertInside(s);
    }

    [Fact]
    public void Drift_HeadingIsNeverPurelyHorizontalOrVertical()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var s = Drift(seed);
            var speed = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
            Assert.True(Math.Abs(s.Vx) > 0.3 * speed, $"seed {seed} vx");
            Assert.True(Math.Abs(s.Vy) > 0.3 * speed, $"seed {seed} vy");
        }
    }

    [Fact]
    public void Drift_CrossesTheScreenInSixToEightSeconds()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var s = Drift(seed);
            var speed = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
            var secs = BW / speed;
            Assert.InRange(secs, FlashMotion.CrossSecondsMin - 1e-6, FlashMotion.CrossSecondsMax + 1e-6);
        }
    }

    [Fact]
    public void Drift_OversizedMediaParksInsteadOfJittering()
    {
        var s = FlashMotion.Create(FlashMotionStyle.DriftBounce, 0, 0, BW + 100, 300, 0, 0, BW, BH,
            MotionLevel.Full, new Random(5));
        FlashMotion.Step(s, 0.5);
        Assert.Equal(0, s.X);
        Assert.Equal(0, s.Vx);
    }

    // ---- Pendulum ----

    [Theory]
    [InlineData(1, 0)] [InlineData(2, 200)] [InlineData(3, 600)] [InlineData(4, 1000)] [InlineData(5, 5000)]
    public void Pendulum_RotatedBoundsNeverLeaveTheWorkArea(int seed, double spawnY)
    {
        var s = Pendulum(seed, y: spawnY);
        AssertInside(s);
        for (int i = 0; i < 10 * 100; i++)
        {
            FlashMotion.Step(s, 0.01);
            AssertInside(s);
        }
    }

    [Fact]
    public void Pendulum_PivotIsTheTopCentreOfTheMonitor()
    {
        var s = FlashMotion.Create(FlashMotionStyle.Pendulum, 2100, 300, 400, 300, 1920, 0, 2560, 1440,
            MotionLevel.Full, new Random(1));
        Assert.Equal(1920 + 1280, s.PivotX);
        Assert.Equal(0, s.PivotY);
    }

    [Fact]
    public void Pendulum_BoundsFollowTheRotatedRect()
    {
        var flat = FlashMotion.HangingBounds(960, 0, 500, 0, 300, 200);
        Assert.Equal(300, flat.W, 6);
        Assert.Equal(200, flat.H, 6);
        Assert.Equal(960 - 150, flat.X, 6);
        Assert.Equal(500 - 100, flat.Y, 6);

        var quarter = FlashMotion.HangingBounds(960, 0, 500, Math.PI / 2, 300, 200);
        Assert.Equal(200, quarter.W, 6);
        Assert.Equal(300, quarter.H, 6);

        var a = 15 * Math.PI / 180;
        var tilted = FlashMotion.HangingBounds(960, 0, 500, a, 300, 200);
        Assert.Equal(300 * Math.Cos(a) + 200 * Math.Sin(a), tilted.W, 6);
        Assert.Equal(300 * Math.Sin(a) + 200 * Math.Cos(a), tilted.H, 6);
    }

    [Fact]
    public void Pendulum_AmplitudeAndPeriod_AreInTheDesignedRange()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var s = Pendulum(seed);
            var ampDeg = s.AmpRad * 180 / Math.PI;
            Assert.InRange(ampDeg, FlashMotion.PendulumAmpMinDeg - 1e-6, FlashMotion.PendulumAmpMaxDeg + 1e-6);
            var period = 2 * Math.PI / s.Omega;
            Assert.InRange(period, FlashMotion.PendulumPeriodMinSec - 1e-6, FlashMotion.PendulumPeriodMaxSec + 1e-6);
        }
    }

    [Fact]
    public void Pendulum_AngleSwingsBothWays()
    {
        var s = Pendulum(11);
        double min = 0, max = 0;
        for (int i = 0; i < 400; i++)
        {
            FlashMotion.Step(s, 0.01);
            min = Math.Min(min, s.AngleRad);
            max = Math.Max(max, s.AngleRad);
        }
        Assert.True(min < -0.9 * s.AmpRad, "swings left");
        Assert.True(max > 0.9 * s.AmpRad, "swings right");
    }

    // ---- MotionLevel ----

    [Fact]
    public void Reduced_HalvesDriftSpeed_AndPendulumAmplitude()
    {
        var full = Drift(21); var half = Drift(21, MotionLevel.Reduced);
        Assert.Equal(full.Vx / 2, half.Vx, 9);
        Assert.Equal(full.Vy / 2, half.Vy, 9);

        var pf = Pendulum(21); var ph = Pendulum(21, MotionLevel.Reduced);
        Assert.Equal(pf.AmpRad / 2, ph.AmpRad, 9);
        Assert.Equal(pf.Omega, ph.Omega, 9);
    }

    [Fact]
    public void Off_CreatesAStillItem_OnTheSpawnRect()
    {
        var s = Pendulum(4, MotionLevel.Off, x: 123, y: 456);
        Assert.Equal(FlashMotionStyle.Still, s.Style);
        Assert.Equal(123, s.X);
        Assert.Equal(456, s.Y);
        Assert.False(FlashMotion.Step(s, 1.0));
    }

    [Theory]
    [InlineData(MotionLevel.Full, 1.0)] [InlineData(MotionLevel.Reduced, 0.5)] [InlineData(MotionLevel.Off, 0.0)]
    public void LevelScale_Table(MotionLevel level, double expected)
        => Assert.Equal(expected, FlashMotion.LevelScale(level));

    // ---- Resolve (ownership) ----

    [Fact]
    public void Mix_RollsOnlyAmongStillAndOwnedStyles()
    {
        var rng = new Random(8);
        bool sawDrift = false;
        for (int i = 0; i < 300; i++)
        {
            var r = FlashMotion.Resolve(FlashMotionStyle.Mix, ownsDriftBounce: true, ownsPendulum: false, MotionLevel.Full, rng);
            Assert.NotEqual(FlashMotionStyle.Pendulum, r);
            Assert.NotEqual(FlashMotionStyle.Mix, r);
            sawDrift |= r == FlashMotionStyle.DriftBounce;
        }
        Assert.True(sawDrift);
        for (int i = 0; i < 50; i++)
            Assert.Equal(FlashMotionStyle.Still,
                FlashMotion.Resolve(FlashMotionStyle.Mix, false, false, MotionLevel.Full, rng));
    }

    [Fact]
    public void UnownedPick_DegradesToStill_OwnedPickPlays()
    {
        var rng = new Random(1);
        Assert.Equal(FlashMotionStyle.Still, FlashMotion.Resolve(FlashMotionStyle.Pendulum, true, false, MotionLevel.Full, rng));
        Assert.Equal(FlashMotionStyle.Still, FlashMotion.Resolve(FlashMotionStyle.DriftBounce, false, true, MotionLevel.Full, rng));
        Assert.Equal(FlashMotionStyle.Pendulum, FlashMotion.Resolve(FlashMotionStyle.Pendulum, false, true, MotionLevel.Full, rng));
        Assert.Equal(FlashMotionStyle.DriftBounce, FlashMotion.Resolve(FlashMotionStyle.DriftBounce, true, false, MotionLevel.Full, rng));
    }

    [Theory]
    [InlineData(FlashMotionStyle.Still)] [InlineData(FlashMotionStyle.DriftBounce)]
    [InlineData(FlashMotionStyle.Pendulum)] [InlineData(FlashMotionStyle.Mix)]
    public void MotionOff_IsAlwaysStill(FlashMotionStyle picked)
        => Assert.Equal(FlashMotionStyle.Still, FlashMotion.Resolve(picked, true, true, MotionLevel.Off, new Random(2)));

    // ---- dirty contract: Step's answer is the layer's dirty flag ----

    [Fact]
    public void Step_StillItemNeverReportsMovement()
    {
        var s = FlashMotion.Create(FlashMotionStyle.Still, 10, 20, 300, 200, 0, 0, BW, BH, MotionLevel.Full, new Random(1));
        for (int i = 0; i < 100; i++) Assert.False(FlashMotion.Step(s, 0.016));
        Assert.Equal(10, s.X); Assert.Equal(20, s.Y);
    }

    [Fact]
    public void Step_MovingItemsReportMovement_ZeroDeltaDoesNot()
    {
        Assert.True(FlashMotion.Step(Drift(1), 0.016));
        Assert.True(FlashMotion.Step(Pendulum(1), 0.016));
        Assert.False(FlashMotion.Step(Drift(1), 0));
        Assert.False(FlashMotion.Step(Pendulum(1), 0));
    }

    [Fact]
    public void Setting_DefaultsToStill()
        => Assert.Equal(FlashMotionStyle.Still, new AppSettings().FlashMotionStyle);
}
