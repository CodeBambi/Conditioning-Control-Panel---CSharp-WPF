using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Bubbles v2 on the Core field (U10): Rain falls and leaves by the bottom, Spiral In walks
/// to the core and leaves as a miss (never a pop), the drain bubble dissolves by itself, and the
/// drain roll keeps WPF's odds and gates.</summary>
public sealed class AmbientBubbleV2Tests
{
    private static AmbientBubble Spawn(BubbleMotionStyle motion, MotionLevel level = MotionLevel.Full, int seed = 7) =>
        AmbientBubble.Spawn(new Random(seed), 0, 0, 0, 1920, 1080, 1.0, new AppSettings(), null, true, motion, level);

    private static (AmbientBubble.Result Result, int Steps) RunOut(AmbientBubble b, int cap = 20000)
    {
        for (var i = 1; i <= cap; i++)
        {
            var r = b.Step();
            if (r != AmbientBubble.Result.Alive) return (r, i);
        }
        return (AmbientBubble.Result.Alive, cap);
    }

    [Fact]
    public void Rain_starts_above_the_top_falls_and_leaves_by_the_bottom_as_a_miss()
    {
        var b = Spawn(BubbleMotionStyle.Rain);
        Assert.Equal(BubbleMotionStyle.Rain, b.Motion);
        Assert.Equal(-b.Size, b.Y, 6);
        var y0 = b.Y;
        b.Step();
        Assert.True(b.Y > y0);
        var (result, _) = RunOut(b);
        Assert.Equal(AmbientBubble.Result.Missed, result);
        Assert.True(b.Y > 1080);
        Assert.False(b.Popping);
    }

    [Fact]
    public void Float_up_is_untouched_by_the_v2_spawn()
    {
        var plain = AmbientBubble.Spawn(new Random(3), 0, 0, 0, 1920, 1080, 1.0, new AppSettings(), null, true);
        var v2 = Spawn(BubbleMotionStyle.FloatUp, seed: 3);
        Assert.Equal((plain.X, plain.Y, plain.Size), (v2.X, v2.Y, v2.Size));
        for (var i = 0; i < 50; i++) { plain.Step(); v2.Step(); }
        Assert.Equal((plain.X, plain.Y), (v2.X, v2.Y));
        Assert.Equal(1.0, v2.DrawOpacity, 6);
    }

    [Fact]
    public void Spiral_in_starts_on_the_ring_fades_into_the_core_and_ends_as_a_miss()
    {
        var b = Spawn(BubbleMotionStyle.SpiralIn);
        double cx = 960, cy = 540;
        double R(AmbientBubble x) => Math.Sqrt(Math.Pow(x.CenterX - cx, 2) + Math.Pow(x.CenterY - cy, 2));
        Assert.Equal(SpiralInPath.StartRadius(1080), R(b), 3);
        var r0 = R(b);
        b.Step();
        Assert.True(R(b) < r0);
        Assert.Equal(1.0, b.DrawOpacity, 6);
        var (result, _) = RunOut(b);
        Assert.Equal(AmbientBubble.Result.Missed, result);   // an exit, never a pop: no sound, XP or lucky roll
        Assert.False(b.Popping);
        Assert.Equal(SpiralInPath.CoreDip, R(b), 3);
        Assert.Equal(0.0, b.DrawOpacity, 6);
    }

    [Fact]
    public void Reduced_motion_halves_the_v2_speed_and_Mix_is_never_flown()
    {
        var full = Spawn(BubbleMotionStyle.Rain, MotionLevel.Full, seed: 11);
        var reduced = Spawn(BubbleMotionStyle.Rain, MotionLevel.Reduced, seed: 11);
        double f0 = full.Y, r0 = reduced.Y;
        full.Step(); reduced.Step();
        Assert.Equal((full.Y - f0) / 2, reduced.Y - r0, 6);
        Assert.Equal(BubbleMotionStyle.FloatUp, Spawn(BubbleMotionStyle.Mix).Motion);
    }

    [Fact]
    public void The_drain_bubble_is_big_breathes_and_dissolves_by_itself_without_a_reward()
    {
        var s = new AppSettings();
        var b = AmbientBubble.SpawnDrain(new Random(5), 0, 0, 0, 1920, 1080, 1.0, s, null, true, BubbleMotionStyle.FloatUp, MotionLevel.Full);
        Assert.True(b.IsDrain);
        Assert.Equal(AmbientBubble.DrainSizeDip, b.Size);
        b.Step();
        Assert.InRange(b.DrawOpacity, 1.0 - BrainDrainBubble.PulseDepth - 1e-9, 1.0);
        var steps = 0;
        while (!b.Popping && steps < 1000) { b.Step(); steps++; }
        Assert.True(b.Dissolving);
        Assert.InRange(steps * AmbientBubbleField.StepMs, AmbientBubble.DrainTreatLifeMs - 60, AmbientBubble.DrainTreatLifeMs + 60);
        var (result, _) = RunOut(b);
        Assert.Equal(AmbientBubble.Result.Done, result);   // faded out: not a miss, not a paid pop
    }

    [Fact]
    public void The_drain_roll_needs_effect_bubbles_on_a_v2_prize_and_its_own_switch()
    {
        var field = new AmbientBubbleField();
        var s = new AppSettings
        {
            BubbleTriggersEnabled = true, BubbleTriggerChance = 100,
            BubbleTriggerVariants = new List<string>(), BubbleBrainDrainEnabled = true,
        };
        int Hits(bool owned) { var n = 0; for (var i = 0; i < 400; i++) if (field.RollDrainBubble(s, owned)) n++; return n; }
        var chance = s.BubbleTriggerChance;                             // the setting caps the chance itself
        Assert.InRange(Hits(true), 400 * chance / 100 - 80, 400 * chance / 100 + 80);   // the only id in the pool
        Assert.Equal(0, Hits(false));                                   // not owned: never, whatever the settings say
        s.BubbleBrainDrainEnabled = false;
        Assert.Equal(0, Hits(true));
        s.BubbleBrainDrainEnabled = true;
        s.BubbleTriggersEnabled = false;
        Assert.Equal(0, Hits(true));
        s.BubbleTriggersEnabled = true;
        s.BubbleTriggerChance = 0;
        Assert.Equal(0, Hits(true));
    }

    [Fact]
    public void The_drain_roll_shares_the_pool_evenly_and_stops_at_the_cap()
    {
        var field = new AmbientBubbleField();
        var s = new AppSettings
        {
            BubbleTriggersEnabled = true, BubbleTriggerChance = 100, BubbleBrainDrainEnabled = true,
            BubbleTriggerVariants = new List<string> { "flash", "pink", "spiral" },
        };
        var hits = 0;
        for (var i = 0; i < 4000; i++) if (field.RollDrainBubble(s, true)) hits++;
        var expected = 4000 * s.BubbleTriggerChance / 100 / 4;   // one id in four, at the chance the setting allows
        Assert.InRange(hits, expected - 150, expected + 150);

        s.BubbleTriggerVariants = new List<string>();
        for (var i = 0; i < AmbientBubbleField.MaxTriggerBubbles; i++)
            field.Bubbles.Add(AmbientBubble.SpawnDrain(field.Random, 0, 0, 0, 1920, 1080, 1.0, s, null, true, BubbleMotionStyle.FloatUp, MotionLevel.Full));
        Assert.False(field.RollDrainBubble(s, true));
    }
}
