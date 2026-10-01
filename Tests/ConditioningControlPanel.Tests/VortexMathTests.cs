using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Vortex: the pure rules behind the cursor spiral riding the spiral overlay. The layer only
/// feeds time, the cursor and clicks and draws what the sim holds, so how it grows, how a click
/// swells then collapses it, and the gif flicker timeline are all pinned here.
/// </summary>
public class VortexMathTests
{
    private const double Dt = 1 / 60.0;

    private static VortexSim Sim() => new(new Random(7));

    private static void Run(VortexSim sim, double seconds, MotionLevel level = MotionLevel.Full,
        double x = 500, double y = 400, Action<VortexSim>? each = null)
    {
        for (double t = 0; t < seconds; t += Dt) { sim.Step(Dt, x, y, level); each?.Invoke(sim); }
    }

    [Fact]
    public void Stillness_rises_slowly_when_still_and_drops_fast_when_moving()
    {
        Assert.Equal(0.45, VortexMath.StepStillness(0, 0, 1), 6);
        Assert.Equal(1, VortexMath.StepStillness(0.9, 10, 1), 6);
        Assert.Equal(0, VortexMath.StepStillness(0.5, 300, 1), 6);
        Assert.Equal(0.5 - 1.8 * 0.1, VortexMath.StepStillness(0.5, 300, 0.1), 6);
    }

    [Fact]
    public void A_still_cursor_grows_the_vortex_to_full_and_no_further()
    {
        var sim = Sim();
        Assert.Equal(VortexMath.StartSize, sim.Size, 6);
        Run(sim, 12);
        Assert.Equal(1, sim.Target, 6);
        Assert.InRange(sim.Size, 0.98, 1.02);
        Assert.True(sim.Still > 0.99);
        Assert.True(sim.BloomAlpha > 0);
    }

    [Fact]
    public void A_moving_cursor_grows_it_slower_than_a_still_one()
    {
        var still = Sim(); Run(still, 2);
        var moving = Sim();
        double x = 0;
        for (double t = 0; t < 2; t += Dt) { x += 10; moving.Step(Dt, x, 400, MotionLevel.Full); }
        Assert.True(moving.Target < still.Target);
        Assert.Equal(0, moving.Still, 6);
    }

    [Fact]
    public void The_spiral_lags_behind_the_cursor()
    {
        var sim = Sim();
        sim.Step(Dt, 0, 0, MotionLevel.Full);
        sim.Step(Dt, 1000, 0, MotionLevel.Full);
        Assert.InRange(sim.X, 1000 * VortexMath.FollowFactor(Dt) - 1e-6, 1000 * VortexMath.FollowFactor(Dt) + 1e-6);
        Run(sim, 5, x: 1000, y: 0);
        Assert.InRange(sim.X, 999, 1000);
    }

    [Fact]
    public void A_click_halves_the_target_but_never_cuts_the_size()
    {
        var sim = Sim();
        Run(sim, 8);
        double before = sim.Size, target = sim.Target;
        sim.Click(MotionLevel.Full, 0.99);
        Assert.Equal(Math.Max(VortexMath.MinTarget, target * 0.5), sim.Target, 6);
        Assert.Equal(before, sim.Size, 6);
        Assert.Equal(VortexMath.MinTarget, VortexMath.ClickTarget(0.13), 6);
    }

    [Fact]
    public void The_collapse_swells_first_then_overshoots_below_the_target_and_settles()
    {
        var sim = Sim();
        Run(sim, 8);
        double start = sim.Size;
        sim.Click(MotionLevel.Full, 0.99);

        double peak = start, minGap = double.MaxValue, maxCollapse = 0;
        int echoesSeen = 0;
        Run(sim, 0.06, each: s => peak = Math.Max(peak, s.Size));
        Assert.True(peak > start, "the kick swells it for a blink");

        Run(sim, 1.0, each: s =>
        {
            minGap = Math.Min(minGap, s.Size - s.Target);
            maxCollapse = Math.Max(maxCollapse, s.Collapse);
            echoesSeen = Math.Max(echoesSeen, s.Echoes.Count(e => e.Alive));
        });
        Assert.True(minGap < -0.01, $"undershoots below the target (min gap {minGap})");
        Assert.True(maxCollapse > 0.3);
        Assert.True(echoesSeen >= 2, "echo copies trail the collapse");

        Run(sim, 2.5);
        Assert.InRange(sim.Size - sim.Target, -0.01, 0.01);
        Assert.InRange(Math.Abs(sim.Velocity), 0, 0.2);
        Assert.True(sim.Collapse < 0.05);
        Assert.DoesNotContain(sim.Echoes, e => e.Alive);
    }

    [Fact]
    public void The_spring_survives_a_long_engine_frame()
    {
        double v = 0.9, s = 0.5;
        for (int i = 0; i < 40; i++) s = VortexMath.SpringStep(s, ref v, 0.25, 0.1);
        Assert.InRange(s, 0.24, 0.26);
    }

    [Fact]
    public void A_click_pulls_in_thirty_dust_motes_and_starts_a_ring()
    {
        var sim = Sim();
        Run(sim, 1);
        int before = sim.Motes.Count(m => m.Alive);
        sim.Click(MotionLevel.Full, 0.99);
        Assert.Equal(before + VortexMath.ClickMotes, sim.Motes.Count(m => m.Alive));
        Assert.Contains(sim.RingBorn, b => b == sim.Time);
        Assert.True(VortexMath.Ring(0.1, out _, out var a) && a > 0);
        Assert.False(VortexMath.Ring(VortexMath.RingLife, out _, out _));
    }

    [Fact]
    public void Collapse_drives_turns_rotation_and_alpha()
    {
        Assert.Equal(2.4, VortexMath.Turns(0, 0), 6);
        Assert.Equal(2.4 + 4 + 3, VortexMath.Turns(1, 1), 6);
        Assert.Equal(1 + 3.2 + 16, VortexMath.RotationSpeed(1, 1), 6);
        Assert.Equal(0.45, VortexMath.ArmsAlpha(0, 0), 6);
        Assert.Equal(1, VortexMath.ArmsAlpha(1, 1), 6);
        Assert.Equal(0.9, VortexMath.Collapse(-0.9 / 2.6), 6);
        Assert.Equal(0, VortexMath.Collapse(0.5), 6);
    }

    [Fact]
    public void One_click_in_ten_rolls_a_gif()
    {
        Assert.True(VortexMath.RollsGif(0.05));
        Assert.False(VortexMath.RollsGif(0.1));
        var sim = Sim();
        Run(sim, 0.5);
        Assert.True(sim.Click(MotionLevel.Full, 0.01));
        Assert.Equal(sim.Time, sim.GifBorn, 6);
        Assert.Equal(sim.X, sim.GifX, 6);
    }

    [Fact]
    public void The_gif_flickers_three_frames_with_a_glitch_then_fades_by_point_nine()
    {
        var frames = Enumerable.Range(0, 9).Select(i =>
        {
            VortexMath.GifFrame(i / 24.0 + 0.001, true, out var f, out _, out _);
            return f;
        }).ToArray();
        Assert.Equal(new[] { 0, 1, 2, 0, 1, 2, 0, 1, 2 }, frames);

        Assert.True(VortexMath.GifFrame(0.1, true, out _, out var a0, out var g0));
        Assert.Equal(1, a0, 6); Assert.True(g0);
        Assert.True(VortexMath.GifFrame(0.5, true, out var held, out var a1, out var g1));
        Assert.False(g1); Assert.InRange(a1, 0.01, 0.99);
        Assert.Equal((int)Math.Floor(0.35 * 24) % 3, held);
        Assert.False(VortexMath.GifFrame(0.9, true, out _, out _, out _));
    }

    [Fact]
    public void Photosafe_gif_is_one_soft_fade_with_no_frame_cycling()
    {
        for (double t = 0; t < 0.4; t += 0.01)
        {
            Assert.True(VortexMath.GifFrame(t, false, out var f, out var a, out var g));
            Assert.Equal(0, f); Assert.False(g); Assert.InRange(a, 0, 0.8);
        }
        Assert.False(VortexMath.GifFrame(0.4, false, out _, out _, out _));
    }

    [Fact]
    public void Motion_off_is_still_no_spin_no_spring_no_dust()
    {
        var sim = Sim();
        Run(sim, 2, MotionLevel.Off);
        Assert.Equal(0, sim.Rot, 6);
        Assert.DoesNotContain(sim.Motes, m => m.Alive);
        // Off does not swell either, and a click neither jumps nor squeezes it.
        Assert.Equal(VortexMath.StartSize, sim.Size, 6);
        Assert.Equal(VortexMath.StartSize, sim.Target, 6);
        sim.Click(MotionLevel.Off, 0.99);
        Assert.Equal(VortexMath.StartSize, sim.Size, 6);
        Assert.Equal(VortexMath.StartSize, sim.Target, 6);
        Assert.Equal(0, sim.Velocity, 6);
        Assert.DoesNotContain(sim.Motes, m => m.Alive);
        Assert.DoesNotContain(sim.RingBorn, b => b > -1e8);
    }

    [Fact]
    public void Motion_off_still_lets_a_rolled_gif_through_as_a_soft_fade()
    {
        var sim = Sim();
        Run(sim, 0.5, MotionLevel.Off);
        Assert.True(sim.Click(MotionLevel.Off, 0.01));
        // The layer never cycles frames below Full motion, so this is the one soft fade.
        Assert.True(VortexMath.GifFrame(0.2, false, out var f, out var a, out var g));
        Assert.Equal(0, f); Assert.False(g); Assert.InRange(a, 0.01, 0.8);
    }

    [Fact]
    public void Quality_repaints_every_frame_and_the_cheaper_tiers_cap_at_thirty()
    {
        Assert.True(VortexMath.ShouldRepaint(1 / 144.0, PerformanceTier.Quality));
        Assert.False(VortexMath.ShouldRepaint(1 / 60.0, PerformanceTier.Balanced));
        Assert.False(VortexMath.ShouldRepaint(1 / 60.0, PerformanceTier.Performance));
        Assert.True(VortexMath.ShouldRepaint(2 / 60.0, PerformanceTier.Balanced));
        Assert.True(VortexMath.ShouldRepaint(1 / 30.0, PerformanceTier.Performance));
    }

    [Fact]
    public void Reduced_motion_spins_at_half_speed_and_kicks_at_half_amplitude()
    {
        var full = Sim(); Run(full, 1);
        var half = Sim(); Run(half, 1, MotionLevel.Reduced);
        Assert.Equal(full.Rot * 0.5, half.Rot, 3);
        double vf = full.Velocity, vh = half.Velocity;
        full.Click(MotionLevel.Full, 0.99); half.Click(MotionLevel.Reduced, 0.99);
        Assert.Equal(VortexMath.ClickKick, full.Velocity - vf, 6);
        Assert.Equal(VortexMath.ClickKick * 0.5, half.Velocity - vh, 6);
    }
}
