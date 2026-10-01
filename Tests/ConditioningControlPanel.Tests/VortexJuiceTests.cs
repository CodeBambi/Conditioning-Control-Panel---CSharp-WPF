using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Vortex juice: it gathers and opens with a small overshoot, breathes while it runs, and on
/// the way out swells, spins up and is sucked in. Off is a 120 ms fade, Reduced half the amplitude.
/// </summary>
public class VortexJuiceTests
{
    private const double Dt = 1 / 60.0;

    private static double Peak(Func<double, double> f, double from, double to)
    {
        double p = double.NegativeInfinity;
        for (double t = from; t <= to; t += 0.001) p = Math.Max(p, f(t));
        return p;
    }

    private static double OpenScale(double age, MotionLevel l) { VortexMath.Opening(age, l, out var s, out _, out _); return s; }
    private static double CloseScale(double age, MotionLevel l) { VortexMath.Closing(age, l, out var s, out _, out _); return s; }

    [Fact]
    public void It_gathers_small_then_opens_past_full_and_settles()
    {
        // Anticipation: the seed draws in a touch during the gather.
        Assert.True(OpenScale(VortexMath.GatherSec * 0.9, MotionLevel.Full) < OpenScale(0, MotionLevel.Full));
        Assert.True(OpenScale(0, MotionLevel.Full) <= VortexMath.SeedScale + 1e-9);
        double end = VortexMath.GatherSec + VortexMath.OpenSec;
        double full = Peak(a => OpenScale(a, MotionLevel.Full), 0, end);
        Assert.InRange(full, 1.03, 1.08);
        double half = Peak(a => OpenScale(a, MotionLevel.Reduced), 0, end);
        Assert.InRange(half - 1, (full - 1) * 0.4, (full - 1) * 0.6);
        Assert.False(VortexMath.Opening(end, MotionLevel.Full, out var s, out var a, out var g));
        Assert.Equal(1, s); Assert.Equal(1, a); Assert.Equal(0, g);
    }

    [Fact]
    public void The_arms_fade_up_and_the_core_flares_as_it_opens()
    {
        VortexMath.Opening(0, MotionLevel.Full, out _, out var a0, out var g0);
        VortexMath.Opening(VortexMath.GatherSec, MotionLevel.Full, out _, out var a1, out var g1);
        VortexMath.Opening(VortexMath.GatherSec + VortexMath.OpenSec * 0.99, MotionLevel.Full, out _, out var a2, out var g2);
        Assert.Equal(0, a0, 6); Assert.Equal(0, g0, 6);
        Assert.InRange(g1, 0.45, 0.55);              // flare peaks at the open
        Assert.InRange(a2, 0.99, 1.0); Assert.True(g2 < 0.05);
        Assert.True(a1 < a2);
    }

    [Fact]
    public void Motion_off_opens_and_closes_as_a_plain_120ms_fade()
    {
        Assert.True(VortexMath.Opening(0.06, MotionLevel.Off, out var s, out var a, out _));
        Assert.Equal(1, s); Assert.Equal(0.5, a, 6);
        Assert.False(VortexMath.Opening(VortexMath.OffFadeSec, MotionLevel.Off, out _, out _, out _));
        Assert.True(VortexMath.Closing(0.06, MotionLevel.Off, out s, out a, out var spin));
        Assert.Equal(1, s); Assert.Equal(0.5, a, 6); Assert.Equal(1, spin);
        Assert.False(VortexMath.Closing(VortexMath.OffFadeSec, MotionLevel.Off, out _, out a, out _));
        Assert.Equal(0, a);
    }

    [Fact]
    public void The_exit_swells_then_is_sucked_in_spinning_up_and_fades()
    {
        double swell = Peak(a => CloseScale(a, MotionLevel.Full), 0, VortexMath.CloseSec);
        Assert.InRange(swell, 1.03, 1.05);
        double halfSwell = Peak(a => CloseScale(a, MotionLevel.Reduced), 0, VortexMath.CloseSec);
        Assert.Equal(1 + VortexMath.CloseSwell * 0.5, halfSwell, 3);
        VortexMath.Closing(VortexMath.CloseSec * 0.95, MotionLevel.Full, out var s, out var a, out var spin);
        Assert.True(s < 0.15); Assert.True(a < 0.05); Assert.True(spin > 5);
        Assert.False(VortexMath.Closing(VortexMath.CloseSec, MotionLevel.Full, out s, out a, out _));
        Assert.Equal(0, s); Assert.Equal(0, a);
    }

    [Fact]
    public void Breath_is_slow_phased_and_halved_under_reduced_and_zero_when_off()
    {
        Assert.True(1 / VortexMath.BreathPeriod < 3);   // photosafe
        Assert.Equal(0, VortexMath.Breath(1.3, 0.4, MotionLevel.Off));
        double full = Peak(t => VortexMath.Breath(t, 0, MotionLevel.Full), 0, 10);
        double half = Peak(t => VortexMath.Breath(t, 0, MotionLevel.Reduced), 0, 10);
        Assert.InRange(full, 0.99, 1); Assert.InRange(half, 0.49, 0.5);
        Assert.NotEqual(VortexMath.Breath(1, 0, MotionLevel.Full), VortexMath.Breath(1, 1.5, MotionLevel.Full));
        var a = new VortexSim(new Random(1)); var b = new VortexSim(new Random(2));
        Assert.NotEqual(a.Phase, b.Phase);
    }

    [Fact]
    public void Opening_gathers_motes_from_far_out_and_draws_scaled_down_then_full()
    {
        var sim = new VortexSim(new Random(3));
        sim.Open(MotionLevel.Full, 500, 400);
        Assert.Equal(VortexMath.GatherMotes, sim.Motes.Count(m => m.Alive));
        Assert.All(sim.Motes.Where(m => m.Alive), m => Assert.True(m.Radius > sim.Radius * 2));
        sim.Step(Dt, 500, 400, MotionLevel.Full);
        Assert.True(sim.IsOpening);
        Assert.True(sim.DrawScale < 0.5); Assert.True(sim.DrawAlpha < 0.1);
        for (int i = 0; i < 60; i++) sim.Step(Dt, 500, 400, MotionLevel.Full);
        Assert.False(sim.IsOpening);
        Assert.InRange(sim.DrawScale, 1 - VortexMath.BreathScale, 1 + VortexMath.BreathScale);
        Assert.Equal(1, sim.DrawAlpha);
        Assert.True(sim.CoreGlow > 0);

        var off = new VortexSim(new Random(3));
        off.Open(MotionLevel.Off, 500, 400);
        Assert.DoesNotContain(off.Motes, m => m.Alive);
    }

    [Fact]
    public void Closing_stops_dust_and_clicks_pulls_motes_in_and_finishes()
    {
        var sim = new VortexSim(new Random(4));
        sim.Open(MotionLevel.Full, 500, 400);
        for (int i = 0; i < 60; i++) sim.Step(Dt, 500, 400, MotionLevel.Full);
        sim.Close();
        sim.Close();   // idempotent
        Assert.True(sim.IsClosing);
        Assert.All(sim.Motes.Where(m => m.Alive), m => Assert.True(m.BoostUntil > sim.Time));
        Assert.False(sim.Click(MotionLevel.Full, 0.0));   // no gif, no kick on the way out
        Assert.DoesNotContain(sim.RingBorn, b => b > -1e8);
        int steps = 0;
        while (!sim.Closed && steps++ < 120) sim.Step(Dt, 500, 400, MotionLevel.Full);
        Assert.True(sim.Closed);
        Assert.InRange(steps * Dt, VortexMath.CloseSec - 2 * Dt, VortexMath.CloseSec + 2 * Dt);
        Assert.Equal(0, sim.DrawAlpha);

        // Start again on the way out: it opens instead of closing.
        var again = new VortexSim(new Random(4));
        again.Close();
        again.Open(MotionLevel.Full, 0, 0);
        Assert.False(again.IsClosing); Assert.True(again.IsOpening);
    }

    [Fact]
    public void Motes_fade_in_and_carry_their_speed_for_a_tail()
    {
        Assert.Equal(0, VortexMath.MoteFade(0), 6);
        Assert.Equal(1, VortexMath.MoteFade(VortexMath.MoteFadeIn), 6);
        var sim = new VortexSim(new Random(5));
        sim.Click(MotionLevel.Full, 0.99);
        sim.Step(Dt, 500, 400, MotionLevel.Full);
        Assert.All(sim.Motes.Where(m => m.Alive), m => { Assert.True(m.Vr > 0); Assert.True(m.Va > 0); });
    }

    [Fact]
    public void The_gif_grows_out_with_a_small_overshoot_and_soft_mode_is_untouched()
    {
        VortexMath.GifPop(0, true, MotionLevel.Full, out var s0, out var m0);
        Assert.Equal(VortexMath.GifPopFrom, s0, 6); Assert.Equal(0, m0, 6);
        double peak = Peak(a => { VortexMath.GifPop(a, true, MotionLevel.Full, out var s, out _); return s; }, 0, VortexMath.GifPopSec);
        Assert.InRange(peak, 1.02, 1.04);
        VortexMath.GifPop(VortexMath.GifPopSec, true, MotionLevel.Full, out var s1, out var m1);
        Assert.Equal(1, s1, 6); Assert.Equal(1, m1, 6);
        VortexMath.GifPop(0, false, MotionLevel.Full, out var ss, out var sm);
        Assert.Equal(1, ss); Assert.Equal(1, sm);
        VortexMath.GifPop(0, true, MotionLevel.Off, out ss, out sm);
        Assert.Equal(1, ss); Assert.Equal(1, sm);
    }

    [Fact]
    public void Panic_still_stops_at_once_and_only_the_super_switch_takes_the_slow_exit()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName,
            "ConditioningControlPanel", "Services", "Notifications", "OverlayService.cs"));
        // BeginExit appears once, in SyncVortex, gated on a spiral that is still showing.
        int first = src.IndexOf("BeginExit()", StringComparison.Ordinal);
        Assert.True(first > 0);
        Assert.Equal(first, src.LastIndexOf("BeginExit()", StringComparison.Ordinal));
        int sync = src.IndexOf("private void SyncVortex()", StringComparison.Ordinal);
        Assert.InRange(first, sync, sync + 1200);
        Assert.Contains("else if (SpiralShowing && !_isDisposed) _vortexLayer?.BeginExit();", src);
    }
}
