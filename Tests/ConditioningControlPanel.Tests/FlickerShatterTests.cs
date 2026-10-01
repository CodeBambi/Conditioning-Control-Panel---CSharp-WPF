using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Flicker Deck's own break: 40 triangles from a 5x4 jittered grid, thrown out and pulled
/// down, with 26 white and 18 pink sparks, gone at 1.4 s. Not the Back Room prize Shatter.
/// </summary>
public class FlickerShatterTests
{
    private static FlickerShatterState Break(MotionLevel level = MotionLevel.Full, int seed = 11)
        => FlickerShatter.Create(500, 400, 300, 200, 0.05, level, new Random(seed));

    [Fact]
    public void Forty_triangles_and_forty_four_sparks()
    {
        var s = Break();
        Assert.Equal(40, s.Shards.Length);
        Assert.Equal(26, s.Sparks.Count(p => !p.Pink));
        Assert.Equal(18, s.Sparks.Count(p => p.Pink));
        Assert.False(s.Done);
    }

    [Fact]
    public void The_triangles_tile_the_whole_picture()
    {
        var s = Break();
        double area = 0;
        foreach (var t in s.Shards)
            area += Math.Abs((t.U1 - t.U0) * (t.V2 - t.V0) - (t.U2 - t.U0) * (t.V1 - t.V0)) / 2;
        Assert.Equal(1.0, area, 6);
        Assert.All(s.Shards, t => Assert.All(new[] { t.U0, t.V0, t.U1, t.V1, t.U2, t.V2 }, v => Assert.InRange(v, 0, 1)));
    }

    [Fact]
    public void Shards_fly_out_from_the_middle_and_spin_within_four_and_a_half()
    {
        var s = Break();
        foreach (var t in s.Shards)
        {
            // vx = cx * 3.2 + random(+-45): the outward part dominates away from the middle.
            Assert.InRange(t.Vx - t.Cx * 3.2, -45, 45);
            Assert.InRange(t.Vy - (t.Cy * 2 - 90), -150, 0);
            Assert.InRange(t.SpinRadPerSec, -4.5, 4.5);
        }
    }

    [Fact]
    public void Gravity_pulls_and_it_is_over_at_one_point_four_seconds()
    {
        var s = Break();
        var vy0 = s.Shards[0].Vy;
        FlickerShatter.Step(s, 0.1);
        Assert.Equal(vy0 + 52, s.Shards[0].Vy, 6);
        for (int i = 0; i < 200 && !s.Done; i++) FlickerShatter.Step(s, 1.0 / 60);
        Assert.True(s.Done);
        Assert.InRange(s.ElapsedSec, 1.4, 1.42);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.5, 1.0)]
    [InlineData(0.95, 0.5)]
    [InlineData(1.4, 0.0)]
    public void Shards_hold_then_fade_over_point_nine(double t, double alpha)
        => Assert.Equal(alpha, FlickerShatter.ShardAlpha(t), 6);

    [Fact]
    public void Reduced_halves_speed_and_Off_is_the_plain_cut()
    {
        var full = Break(MotionLevel.Full, 5);
        var reduced = Break(MotionLevel.Reduced, 5);
        for (int i = 0; i < full.Shards.Length; i++)
        {
            Assert.Equal(full.Shards[i].Vx / 2, reduced.Shards[i].Vx, 6);
            Assert.Equal(full.Shards[i].SpinRadPerSec / 2, reduced.Shards[i].SpinRadPerSec, 6);
        }
        var off = Break(MotionLevel.Off);
        Assert.True(off.Done);
        Assert.Empty(off.Shards);
        Assert.False(FlickerShatter.Step(off, 0.1));
    }

    [Fact]
    public void Swap_puff_is_ten_sparks_that_die_inside_point_six_seconds()
    {
        var sparks = FlickerShatter.SwapSparks(MotionLevel.Full, new Random(3));
        Assert.Equal(FlickerShatter.SwapSparkCount, sparks.Length);
        Assert.All(sparks, sp => Assert.InRange(Math.Sqrt(sp.Vx * sp.Vx + sp.Vy * sp.Vy), 0, FlickerShatter.SwapSparkSpeed + 1e-9));
        var alive = true;
        double t = 0;
        while (alive && t < 2) { alive = FlickerShatter.StepSparks(sparks, 1.0 / 60); t += 1.0 / 60; }
        Assert.False(alive);
        Assert.InRange(t, 0.2, FlickerShatter.SwapSparkLife * 1.2 + 0.05);
    }

    [Fact]
    public void Swap_puff_is_nothing_under_off_and_half_speed_under_reduced()
    {
        Assert.Empty(FlickerShatter.SwapSparks(MotionLevel.Off, new Random(3)));
        var full = FlickerShatter.SwapSparks(MotionLevel.Full, new Random(5));
        var reduced = FlickerShatter.SwapSparks(MotionLevel.Reduced, new Random(5));
        for (int i = 0; i < full.Length; i++)
            Assert.Equal(full[i].Vx * 0.5, reduced[i].Vx, 9);
    }
}
