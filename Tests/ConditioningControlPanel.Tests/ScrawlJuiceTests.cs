using System;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Super Scrawl juice round: landing punch, bounce sparks, ink splat, eased fade, idle breath, drip sway, label entrance.</summary>
public class ScrawlJuiceTests
{
    private const double Life = ScrawlRules.StampLife;
    private static readonly ScrawlBounds Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void The_stamp_drops_into_the_wall_then_squashes_and_settles_once()
    {
        Assert.Equal((1.5, 1.5), ScrawlRules.LandPunch(0, false, MotionLevel.Full));
        double contact = ScrawlRules.ContactTime(false);
        var mid = ScrawlRules.LandPunch(contact / 2, false, MotionLevel.Full);
        Assert.InRange(mid.Along, 1.3, 1.5);   // ease-in: still high halfway down

        var hit = ScrawlRules.LandPunch(contact, false, MotionLevel.Full);
        Assert.InRange(1 - hit.Along, 0.03, 0.08);    // flattened along the wall normal, 3-8 percent
        Assert.True(hit.Across > 1);                   // widened across it
        Assert.True(hit.Across - 1 < 1 - hit.Along);   // area roughly kept

        var settled = ScrawlRules.LandPunch(contact + ScrawlRules.SettleLife + 0.001, false, MotionLevel.Full);
        Assert.Equal((1.0, 1.0), settled);
    }

    [Fact]
    public void The_landing_punch_obeys_motion()
    {
        double contact = ScrawlRules.ContactTime(false);
        var full = ScrawlRules.LandPunch(contact, false, MotionLevel.Full);
        var reduced = ScrawlRules.LandPunch(contact, false, MotionLevel.Reduced);
        Assert.Equal((1 - full.Along) / 2, 1 - reduced.Along, 9);
        Assert.Equal(1.25, ScrawlRules.LandPunch(0, false, MotionLevel.Reduced).Along, 9);
        Assert.Equal((1.0, 1.0), ScrawlRules.LandPunch(0, false, MotionLevel.Off));
        Assert.Equal((1.0, 1.0), ScrawlRules.LandPunch(contact, true, MotionLevel.Off));
    }

    [Fact]
    public void Leftovers_hold_then_let_go_and_reach_zero()
    {
        Assert.Equal(1, ScrawlRules.LeftoverAlpha(0, Life, -1));
        Assert.True(ScrawlRules.LeftoverAlpha(Life / 2, Life, -1) > ScrawlRules.StampAlpha(Life / 2, Life, -1));
        Assert.Equal(0, ScrawlRules.LeftoverAlpha(Life, Life, -1), 9);
        double prev = 1;
        for (double t = 0; t <= Life; t += 0.5)
        {
            double a = ScrawlRules.LeftoverAlpha(t, Life, -1);
            Assert.True(a <= prev + 1e-12);
            prev = a;
        }
        Assert.Equal(1, ScrawlRules.LeftoverAlpha(Life / 2, Life, 0));   // a slam flare still lifts it, capped at one
    }

    [Fact]
    public void The_wall_glow_comes_up_then_fades_never_one_frame()
    {
        Assert.Equal(0, ScrawlRules.GlowAlphaAt(0));
        Assert.InRange(ScrawlRules.GlowAlphaAt(0.016), 0.3, 0.7);
        Assert.Equal(1, ScrawlRules.GlowAlphaAt(ScrawlRules.GlowIn), 9);
        Assert.True(ScrawlRules.GlowAlphaAt(0.3) < ScrawlRules.GlowAlphaAt(0.1));
        Assert.Equal(0, ScrawlRules.GlowAlphaAt(ScrawlRules.GlowLife));
    }

    [Theory]
    [InlineData(ScrawlWall.Left, 1, 0)]
    [InlineData(ScrawlWall.Right, -1, 0)]
    [InlineData(ScrawlWall.Top, 0, 1)]
    [InlineData(ScrawlWall.Bottom, 0, -1)]
    public void Bounce_sparks_fly_into_the_field(ScrawlWall wall, int nx, int ny)
    {
        for (double u = -1; u <= 1; u += 0.25)
        {
            double a = ScrawlRules.BounceSparkAngle(wall, u);
            Assert.True(Math.Cos(a) * nx + Math.Sin(a) * ny > 0.5);   // inside the cone, off the wall
        }
    }

    [Fact]
    public void Spark_counts_halve_under_reduced_and_vanish_under_off()
    {
        Assert.Equal(7, ScrawlRules.SparkCount(7, MotionLevel.Full));
        Assert.Equal(4, ScrawlRules.SparkCount(7, MotionLevel.Reduced));
        Assert.Equal(0, ScrawlRules.SparkCount(7, MotionLevel.Off));
    }

    [Fact]
    public void Idle_breath_waits_for_the_landing_is_phased_and_tiny()
    {
        Assert.Equal(1, ScrawlRules.IdleBreath(0.2, 1.3, MotionLevel.Full));   // still settling
        double a = ScrawlRules.IdleBreath(3, 0, MotionLevel.Full), b = ScrawlRules.IdleBreath(3, 2, MotionLevel.Full);
        Assert.NotEqual(a, b, 6);
        for (double t = 0; t < 14; t += 0.37)
            Assert.InRange(ScrawlRules.IdleBreath(t, 0.7, MotionLevel.Full), 1 - ScrawlRules.BreathAmp, 1 + ScrawlRules.BreathAmp);
        Assert.Equal((a - 1) / 2, ScrawlRules.IdleBreath(3, 0, MotionLevel.Reduced) - 1, 9);
        Assert.Equal(1, ScrawlRules.IdleBreath(3, 0, MotionLevel.Off));
    }

    [Fact]
    public void Drips_sway_gently_once_falling_and_never_under_off()
    {
        Assert.Equal(0, ScrawlRules.DripSway(5, 1, 0, MotionLevel.Full));
        for (double t = 0.05; t <= 1; t += 0.05)
            Assert.InRange(Math.Abs(ScrawlRules.DripSway(5 + t, 1, t, MotionLevel.Full)), 0, ScrawlRules.DripWobble);
        Assert.Equal(0, ScrawlRules.DripSway(5, 1, 0.5, MotionLevel.Off));
    }

    [Fact]
    public void The_corner_label_grows_in_with_a_small_overshoot()
    {
        Assert.Equal(ScrawlRules.LabelFrom, ScrawlRules.LabelScale(0, MotionLevel.Full), 9);
        double peak = 0;
        for (double t = 0; t < ScrawlRules.LabelIn; t += 0.005) peak = Math.Max(peak, ScrawlRules.LabelScale(t, MotionLevel.Full));
        Assert.InRange(peak, 1.02, 1.08);
        Assert.Equal(1, ScrawlRules.LabelScale(ScrawlRules.LabelIn, MotionLevel.Full));
        Assert.Equal(0.8, ScrawlRules.LabelScale(0, MotionLevel.Reduced), 9);
        Assert.Equal(1, ScrawlRules.LabelScale(0, MotionLevel.Off));
    }

    [Fact]
    public void A_wall_hit_throws_bounce_sparks_then_splats_at_contact()
    {
        var s = new ScrawlScene(new FontFamily("Segoe UI"), 72, 1.0);
        s.Tick(0.001, MotionLevel.Full);
        s.WallHit("SINK", ScrawlWall.Top, 800, 0, 1000, 80, Screen, new Point(960, 540));
        Assert.Equal(ScrawlRules.BounceSparks, s.LiveSparks);
        s.Tick(ScrawlRules.ContactTime(false) + 0.005, MotionLevel.Full);
        Assert.Equal(ScrawlRules.BounceSparks + ScrawlRules.SplatSparks, s.LiveSparks);
        s.Clear();
        Assert.Equal(0, s.LiveSparks);
    }

    [Fact]
    public void Motion_off_throws_no_sparks_at_all()
    {
        var s = new ScrawlScene(new FontFamily("Segoe UI"), 72, 1.0);
        s.Tick(0.001, MotionLevel.Off);
        s.WallHit("SINK", ScrawlWall.Top, 800, 0, 1000, 80, Screen, new Point(960, 540));
        s.SlamLanded("DEEP", 960, 540, 1920);
        s.Tick(0.2, MotionLevel.Off);
        Assert.Equal(0, s.LiveSparks);
    }
}
