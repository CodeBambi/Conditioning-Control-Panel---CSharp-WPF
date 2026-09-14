using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Bubbles v2 (Back Room prizes): the pure motion choice, the Spiral In path maths, the
/// MotionLevel degradation and the PrizeGrants matcher. No WPF, no App.
/// </summary>
public class BubbleMotionV2Tests
{
    private static IEnumerable<double> Rolls()
    {
        for (int i = 0; i < 100; i++) yield return i / 100.0;
    }

    // ---- ownership + resolve ----

    [Theory]
    [InlineData(BubbleMotionStyle.Rain, false, true)]
    [InlineData(BubbleMotionStyle.SpiralIn, true, false)]
    [InlineData(BubbleMotionStyle.Mix, false, false)]
    public void Unowned_selection_degrades_to_FloatUp(BubbleMotionStyle picked, bool rain, bool spiral)
    {
        foreach (var roll in Rolls())
            Assert.Equal(BubbleMotionStyle.FloatUp, AmbientBubbleMotion.Resolve(picked, rain, spiral, MotionLevel.Full, roll));
    }

    [Theory]
    [InlineData(BubbleMotionStyle.Rain)]
    [InlineData(BubbleMotionStyle.SpiralIn)]
    public void Owned_selection_is_kept(BubbleMotionStyle picked)
    {
        Assert.Equal(picked, AmbientBubbleMotion.Resolve(picked, true, true, MotionLevel.Full, 0.5));
        Assert.Equal(picked, AmbientBubbleMotion.Resolve(picked, true, true, MotionLevel.Reduced, 0.5));
    }

    [Fact]
    public void Mix_only_picks_owned_styles_and_never_Mix()
    {
        // Rain only: FloatUp + Rain, never SpiralIn.
        var seen = new HashSet<BubbleMotionStyle>();
        foreach (var roll in Rolls())
            seen.Add(AmbientBubbleMotion.Resolve(BubbleMotionStyle.Mix, true, false, MotionLevel.Full, roll));
        Assert.Equal(new HashSet<BubbleMotionStyle> { BubbleMotionStyle.FloatUp, BubbleMotionStyle.Rain }, seen);

        // Spiral only: FloatUp + SpiralIn, never Rain.
        seen.Clear();
        foreach (var roll in Rolls())
            seen.Add(AmbientBubbleMotion.Resolve(BubbleMotionStyle.Mix, false, true, MotionLevel.Full, roll));
        Assert.Equal(new HashSet<BubbleMotionStyle> { BubbleMotionStyle.FloatUp, BubbleMotionStyle.SpiralIn }, seen);

        // Both: all three, roughly equal weight.
        var counts = new Dictionary<BubbleMotionStyle, int>();
        for (int i = 0; i < 3000; i++)
        {
            var r = AmbientBubbleMotion.Resolve(BubbleMotionStyle.Mix, true, true, MotionLevel.Full, i / 3000.0);
            counts[r] = counts.GetValueOrDefault(r) + 1;
        }
        Assert.Equal(3, counts.Count);
        Assert.DoesNotContain(BubbleMotionStyle.Mix, counts.Keys);
        foreach (var c in counts.Values) Assert.InRange(c, 900, 1100);
    }

    [Fact]
    public void Roll_edges_stay_in_range()
    {
        Assert.Equal(BubbleMotionStyle.FloatUp, AmbientBubbleMotion.Resolve(BubbleMotionStyle.Mix, true, true, MotionLevel.Full, 0.0));
        Assert.Equal(BubbleMotionStyle.SpiralIn, AmbientBubbleMotion.Resolve(BubbleMotionStyle.Mix, true, true, MotionLevel.Full, 1.0));
        Assert.Equal(BubbleMotionStyle.SpiralIn, AmbientBubbleMotion.Resolve(BubbleMotionStyle.Mix, true, true, MotionLevel.Full, 7.0));
        Assert.Equal(BubbleMotionStyle.FloatUp, AmbientBubbleMotion.Resolve(BubbleMotionStyle.Mix, true, true, MotionLevel.Full, -3.0));
    }

    [Theory]
    [InlineData(BubbleMotionStyle.FloatUp, false, false, true)]
    [InlineData(BubbleMotionStyle.Rain, false, false, false)]
    [InlineData(BubbleMotionStyle.Rain, true, false, true)]
    [InlineData(BubbleMotionStyle.SpiralIn, true, false, false)]
    [InlineData(BubbleMotionStyle.Mix, false, false, false)]
    [InlineData(BubbleMotionStyle.Mix, false, true, true)]
    public void IsOwned_follows_grants(BubbleMotionStyle style, bool rain, bool spiral, bool expected)
        => Assert.Equal(expected, AmbientBubbleMotion.IsOwned(style, rain, spiral));

    // ---- MotionLevel degradation ----

    [Theory]
    [InlineData(BubbleMotionStyle.Rain)]
    [InlineData(BubbleMotionStyle.SpiralIn)]
    [InlineData(BubbleMotionStyle.Mix)]
    public void MotionLevel_Off_is_plain_FloatUp_regardless_of_the_picker(BubbleMotionStyle picked)
    {
        foreach (var roll in Rolls())
            Assert.Equal(BubbleMotionStyle.FloatUp, AmbientBubbleMotion.Resolve(picked, true, true, MotionLevel.Off, roll));
    }

    [Fact]
    public void MotionLevel_Reduced_halves_speed_and_spiral_turns()
    {
        Assert.Equal(1.0, AmbientBubbleMotion.SpeedMult(MotionLevel.Full));
        Assert.Equal(0.5, AmbientBubbleMotion.SpeedMult(MotionLevel.Reduced));
        Assert.Equal(SpiralInPath.FullTurns, SpiralInPath.Turns(MotionLevel.Full));
        Assert.Equal(SpiralInPath.FullTurns * 0.5, SpiralInPath.Turns(MotionLevel.Reduced));
    }

    // ---- Spiral In path ----

    private static (List<SpiralInPath.State> trace, double r0) RunSpiral(double shortDim, double speed, double turns, double dir, double ts = 1.0)
    {
        double r0 = SpiralInPath.StartRadius(shortDim);
        var s = SpiralInPath.Start(r0, 1.0);
        var trace = new List<SpiralInPath.State> { s };
        for (int i = 0; i < 100000 && !s.Done; i++)
        {
            s = SpiralInPath.Step(s, r0, speed * SpiralInPath.RadialPerSpeed, turns, dir, ts);
            trace.Add(s);
        }
        return (trace, r0);
    }

    [Fact]
    public void Spiral_starts_on_the_ring_and_ends_inside_the_core()
    {
        var (trace, r0) = RunSpiral(1080, 1.5, SpiralInPath.FullTurns, 1);
        Assert.Equal(1080 * SpiralInPath.StartRadiusFrac, r0, 6);
        Assert.Equal(r0, trace[0].Radius);
        Assert.True(trace[^1].Done);
        Assert.True(trace[^1].Radius <= SpiralInPath.CoreDip + 1e-9);
        Assert.Equal(0.0, trace[^1].Fade, 9);
        Assert.Equal(1.0, trace[0].Fade);
    }

    [Fact]
    public void Spiral_radius_shrinks_monotonically_and_angle_advances()
    {
        var (trace, _) = RunSpiral(1080, 1.5, SpiralInPath.FullTurns, 1);
        for (int i = 1; i < trace.Count; i++)
        {
            Assert.True(trace[i].Radius <= trace[i - 1].Radius, $"radius grew at frame {i}");
            Assert.True(trace[i].Angle > trace[i - 1].Angle, $"angle stalled at frame {i}");
            Assert.True(trace[i].Fade <= trace[i - 1].Fade + 1e-12, $"fade rose at frame {i}");
        }
        // Counter-clockwise direction advances the other way.
        var (ccw, _) = RunSpiral(1080, 1.5, SpiralInPath.FullTurns, -1);
        Assert.True(ccw[^1].Angle < ccw[0].Angle);
    }

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    public void Spiral_lands_the_requested_turns_at_the_core(MotionLevel level)
    {
        double turns = SpiralInPath.Turns(level);
        var (trace, _) = RunSpiral(1080, 1.5, turns, 1);
        double swept = (trace[^1].Angle - trace[0].Angle) / (2 * Math.PI);
        Assert.InRange(swept, turns * 0.98, turns * 1.02);
    }

    [Fact]
    public void Spiral_lifetime_sits_in_the_ambient_range_at_full_speed()
    {
        // 1080p, natural speed 1..2 px/frame, 30 fps: a plain float-up crosses in roughly 18-36 s.
        var (slow, _) = RunSpiral(1080, 1.0, SpiralInPath.FullTurns, 1);
        var (fast, _) = RunSpiral(1080, 2.0, SpiralInPath.FullTurns, 1);
        Assert.InRange(slow.Count / 30.0, 12, 40);
        Assert.InRange(fast.Count / 30.0, 6, 20);
    }

    [Fact]
    public void Spiral_time_scale_slows_the_walk_but_keeps_the_shape()
    {
        var (full, _) = RunSpiral(900, 1.5, SpiralInPath.FullTurns, 1, ts: 1.0);
        var (half, _) = RunSpiral(900, 1.5, SpiralInPath.FullTurns, 1, ts: 0.5);
        Assert.InRange(half.Count, full.Count * 2 - 3, full.Count * 2 + 3);
        double sweptFull = full[^1].Angle - full[0].Angle, sweptHalf = half[^1].Angle - half[0].Angle;
        Assert.InRange(sweptHalf, sweptFull * 0.98, sweptFull * 1.02);
    }

    [Fact]
    public void Spiral_done_state_is_sticky()
    {
        var (trace, r0) = RunSpiral(600, 1.5, 2, 1);
        var again = SpiralInPath.Step(trace[^1], r0, 1, 2, 1, 1);
        Assert.Equal(trace[^1], again);
    }

    [Fact]
    public void Spiral_start_radius_never_collapses_on_a_tiny_screen()
        => Assert.True(SpiralInPath.StartRadius(10) >= SpiralInPath.CoreDip * 4);

    // ---- PrizeGrants matcher ----

    [Theory]
    [InlineData("fx.*", "fx.bubble.rain", true)]
    [InlineData("fx.*", "fx.bubble.spiral_in", true)]
    [InlineData("fx.*", "rt.original.00", false)]
    [InlineData("fx.bubble.*", "fx.bubble.rain", true)]
    [InlineData("fx.bubble.*", "fx.flash.pendulum", false)]
    [InlineData("fx.bubble.rain", "fx.bubble.rain", true)]
    [InlineData("fx.bubble.rain", "fx.bubble.rain2", false)]
    [InlineData("fx.bubble.rain", "fx.bubble.spiral_in", false)]
    [InlineData("", "fx.bubble.rain", false)]
    [InlineData("fx.*", "", false)]
    public void PrizeGrants_Matches_wildcard_and_exact(string pattern, string grantId, bool expected)
        => Assert.Equal(expected, PrizeGrants.Matches(pattern, grantId));

    [Fact]
    public void PrizeGrants_bubble_ids_are_the_contract_ids()
    {
        Assert.Equal("fx.bubble.rain", PrizeGrants.BubbleRain);
        Assert.Equal("fx.bubble.spiral_in", PrizeGrants.BubbleSpiralIn);
        Assert.True(PrizeGrants.Matches("fx.*", PrizeGrants.BubbleRain));
        Assert.True(PrizeGrants.Matches("fx.*", PrizeGrants.BubbleSpiralIn));
    }
}
