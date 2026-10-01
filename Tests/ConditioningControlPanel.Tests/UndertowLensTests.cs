using System;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Undertow (Brain Drain and melt add-on): the pure lens maths. The layer only steps the
/// state and draws a feathered hole in the blur, so the feel is decided here: the follow
/// smoothing, the constant shrink to nothing, the click kick and its cap, the ripple timings,
/// the mask stops, and the reduced / off motion rules.
/// </summary>
public class UndertowLensTests
{
    private const double W = 1920;
    private const double Dt = 1.0 / 60;

    private static UndertowState Placed(UndertowMotion motion = UndertowMotion.Full, double x = 500, double y = 400)
    {
        var s = new UndertowState();
        UndertowLens.Step(s, 0, x, y, W, motion);
        return s;
    }

    [Fact]
    public void First_step_snaps_to_the_cursor_and_the_lens_opens_instead_of_popping_in()
    {
        var s = Placed();
        Assert.Equal(500, s.X);
        Assert.Equal(400, s.Y);
        Assert.Equal(UndertowLens.MinRadius * W, s.Radius, 6);     // starts at the minimum, never a full lens on frame one
        Assert.Equal(UndertowLens.MaxRadius * W, s.Kick, 6);

        double peak = 0;
        for (int i = 0; i < 60; i++)
        {
            UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
            peak = Math.Max(peak, s.Radius);
        }
        Assert.True(peak > 0.25 * W, $"opened to {peak / W:F3} W");  // a wide lens inside a second
        Assert.True(peak <= UndertowLens.MaxRadius * W + 1e-9);
    }

    [Fact]
    public void Off_opens_at_the_still_lens_at_once()
    {
        var s = Placed(UndertowMotion.Off);
        Assert.Equal(UndertowLens.StillRadius * W, s.Radius, 6);
        Assert.Equal(0, s.Kick);
    }

    [Fact]
    public void Lens_follows_the_cursor_with_exponential_smoothing()
    {
        var s = Placed();
        UndertowLens.Step(s, Dt, 1500, 400, W, UndertowMotion.Full);
        double f = 1 - Math.Exp(-6 * Dt);
        Assert.Equal(500 + 1000 * f, s.X, 6);
        Assert.True(s.X < 1500);
        for (int i = 0; i < 600; i++) UndertowLens.Step(s, Dt, 1500, 400, W, UndertowMotion.Full);
        Assert.Equal(1500, s.X, 2);
    }

    [Fact]
    public void Shrink_matches_the_mockup_formula_and_never_goes_negative()
    {
        double r = 300;
        double next = UndertowLens.Shrink(r, 0.1, W, UndertowMotion.Full);
        Assert.Equal(300 - 0.1 * (UndertowLens.ShrinkLinear * W + UndertowLens.ShrinkProportional * 300), next, 6);
        Assert.Equal(UndertowLens.MinRadius * W, UndertowLens.Shrink(5, 1, W, UndertowMotion.Full));
    }

    [Fact]
    public void Left_alone_the_lens_shrinks_to_the_minimum_and_stops_repainting()
    {
        var s = Placed();
        s.Kick = 0;
        s.Radius = UndertowLens.MaxRadius * W;
        double last = s.Radius;
        for (int i = 0; i < 60 * 40; i++)
        {
            UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
            Assert.True(s.Radius <= last);
            Assert.True(s.Radius >= UndertowLens.MinRadius * W - 1e-9);
            last = s.Radius;
        }
        Assert.Equal(UndertowLens.MinRadius * W, s.Radius, 6);
        Assert.False(UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full));
    }

    [Fact]
    public void Reduced_shrinks_at_half_speed()
    {
        double full = 300 - UndertowLens.Shrink(300, 0.1, W, UndertowMotion.Full);
        double reduced = 300 - UndertowLens.Shrink(300, 0.1, W, UndertowMotion.Reduced);
        Assert.Equal(full / 2, reduced, 6);
    }

    [Fact]
    public void A_click_widens_the_lens_at_the_apply_rate()
    {
        var s = Placed();
        s.Radius = UndertowLens.MinRadius * W;
        s.Kick = 0;
        UndertowLens.Click(s, 500, 400, W, UndertowMotion.Full);
        Assert.Equal(UndertowLens.KickPerClick * W, s.Kick, 6);
        UndertowLens.Step(s, 0.1, 500, 400, W, UndertowMotion.Full);
        // shrink from 0 stays 0, then at most 0.75 W per second of kick lands
        Assert.Equal((UndertowLens.MinRadius + 0.075) * W, s.Radius, 6);
        Assert.Equal((UndertowLens.KickPerClick - 0.075) * W, s.Kick, 6);
    }

    [Fact]
    public void Many_clicks_never_push_the_lens_past_the_cap()
    {
        var s = Placed();
        for (int c = 0; c < 20; c++) UndertowLens.Click(s, 500, 400, W, UndertowMotion.Full);
        for (int i = 0; i < 300; i++)
        {
            UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
            Assert.True(s.Radius <= UndertowLens.MaxRadius * W + 1e-9);
        }
    }

    [Fact]
    public void Click_spam_queues_at_most_one_full_lens_so_it_sinks_soon_after()
    {
        var s = Placed();
        s.Kick = 0;
        for (int c = 0; c < 20; c++) UndertowLens.Click(s, 500, 400, W, UndertowMotion.Full);
        Assert.Equal(UndertowLens.MaxKick * W, s.Kick, 6);

        // all owed widening is paid inside MaxKick / KickApplyRate seconds, then the lens sinks
        double payout = UndertowLens.MaxKick / UndertowLens.KickApplyRate;
        int ticks = (int)Math.Ceiling(payout / Dt) + 1;
        for (int i = 0; i < ticks; i++) UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
        Assert.Equal(0, s.Kick, 6);
        double r = s.Radius;
        UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
        Assert.True(s.Radius < r);
    }

    [Fact]
    public void Wave_amplitude_scales_with_dpi_because_the_layer_draws_in_physical_pixels()
    {
        Assert.True(UndertowLens.WaveAt(0.1, W, UndertowMotion.Full, 1.0, out double f1, out double a1, out double wl1, out _));
        Assert.True(UndertowLens.WaveAt(0.1, W, UndertowMotion.Full, 2.0, out double f2, out double a2, out double wl2, out _));
        Assert.Equal(a1 * 2, a2, 6);
        Assert.Equal(f1, f2, 6);     // the front and the crest spacing are screen shares
        Assert.Equal(wl1, wl2, 6);
    }

    [Fact]
    public void Step_asks_for_a_repaint_only_when_something_drawn_moved()
    {
        var s = new UndertowState();
        Assert.True(UndertowLens.Step(s, 0, 500, 400, W, UndertowMotion.Off));   // placement
        Assert.False(UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Off)); // still lens, still cursor
        Assert.True(UndertowLens.Step(s, Dt, 520, 400, W, UndertowMotion.Off));  // cursor moved

        UndertowLens.Click(s, 520, 400, W, UndertowMotion.Off);
        Assert.False(UndertowLens.Step(s, Dt, 520, 400, W, UndertowMotion.Off));  // Off: no wave, still screen

        // a lens resting at its minimum, cursor still: nothing on screen changes
        var c = Placed();
        c.Kick = 0; c.Radius = UndertowLens.MinRadius * W;
        Assert.False(UndertowLens.Step(c, Dt, c.X, c.Y, W, UndertowMotion.Full));

        // a shrinking open lens repaints every tick
        var o = Placed();
        o.Kick = 0; o.Radius = 0.3 * W;
        Assert.True(UndertowLens.Step(o, Dt, 500, 400, W, UndertowMotion.Full));
    }

    [Fact]
    public void A_click_sends_a_wave_front_that_runs_out_and_dies_after_the_wave_life()
    {
        var s = Placed();
        UndertowLens.Click(s, 700, 300, W, UndertowMotion.Full);
        Assert.True(s.Ripples[0].Live);
        Assert.Equal(700, s.Ripples[0].X);

        Assert.True(UndertowLens.WaveAt(0.2, W, UndertowMotion.Full, 1.0, out double early, out double aEarly, out _, out _));
        Assert.True(UndertowLens.WaveAt(0.9, W, UndertowMotion.Full, 1.0, out double late, out double aLate, out _, out double band));
        Assert.True(late > early);                       // the front keeps travelling
        Assert.True(aLate < aEarly);                     // and fades
        Assert.True(band > 0);
        Assert.False(UndertowLens.WaveAt(UndertowLens.WaveLife, W, UndertowMotion.Full, 1.0, out _, out _, out _, out _));

        int ticks = (int)(UndertowLens.WaveLife / Dt) + 2;
        for (int i = 0; i < ticks; i++) UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
        Assert.False(s.Ripples[0].Live);
    }

    [Fact]
    public void Ripple_pool_reuses_the_oldest_slot_instead_of_growing()
    {
        var s = Placed();
        for (int c = 0; c < UndertowState.MaxRipples + 3; c++)
        {
            UndertowLens.Click(s, c, 0, W, UndertowMotion.Full);
            UndertowLens.Step(s, 0.01, 500, 400, W, UndertowMotion.Full);
        }
        Assert.Equal(UndertowState.MaxRipples, s.Ripples.Length);
        foreach (var r in s.Ripples) Assert.True(r.Live);
    }

    [Fact]
    public void Reduced_widens_and_waves_at_half_speed_and_half_amplitude()
    {
        var s = Placed(UndertowMotion.Reduced);
        UndertowLens.Click(s, 500, 400, W, UndertowMotion.Reduced);
        Assert.True(s.Kick > 0);
        Assert.True(s.Ripples[0].Live);
        UndertowLens.WaveAt(0.4, W, UndertowMotion.Full, 1.0, out double ff, out double fa, out _, out _);
        UndertowLens.WaveAt(0.4, W, UndertowMotion.Reduced, 1.0, out double rf, out double ra, out _, out _);
        Assert.Equal(ff / 2, rf, 6);
        Assert.Equal(fa / 2, ra, 6);
    }

    [Fact]
    public void Off_keeps_a_still_lens_and_a_click_makes_no_wave()
    {
        var s = Placed(UndertowMotion.Off);
        for (int i = 0; i < 600; i++) UndertowLens.Step(s, Dt, 800, 600, W, UndertowMotion.Off);
        Assert.Equal(UndertowLens.StillRadius * W, s.Radius, 6);
        Assert.Equal(800, s.X);

        UndertowLens.Click(s, 800, 600, W, UndertowMotion.Off);
        Assert.Equal(0, s.Kick);
        Assert.False(s.Ripples[0].Live);
        Assert.False(UndertowLens.WaveAt(0.06, W, UndertowMotion.Off, 1.0, out _, out _, out _, out _));
    }

    [Fact]
    public void Mask_is_feathered_not_a_hard_circle()
    {
        Assert.Equal(new[] { 0f, 0.5f, 1f }, UndertowLens.MaskPositions);
        Assert.Equal(new[] { 1f, 0.95f, 0f }, UndertowLens.MaskAlpha);
    }

    [Fact]
    public void Reset_clears_the_lens_for_panic_and_stop()
    {
        var s = Placed();
        UndertowLens.Click(s, 1, 2, W, UndertowMotion.Full);
        UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
        s.Reset();
        Assert.Equal(0, s.Radius);
        Assert.Equal(0, s.Kick);
        Assert.False(s.Placed);
        Assert.False(s.Animating);
        Assert.Equal(0, s.DrawRadius);
        Assert.Equal(-1, s.KnockAge);
        foreach (var d in s.Drops) Assert.False(d.Live);
        Assert.Equal(0, UndertowLens.RimAlpha(s, W, UndertowMotion.Full));
    }

    // ---- juice ----

    private static double PeakDrawOverRadius(UndertowMotion motion)
    {
        var s = Placed(motion);
        double peakDraw = 0, peakR = 0;
        for (int i = 0; i < 180; i++)
        {
            UndertowLens.Step(s, Dt, 500, 400, W, motion);
            peakDraw = Math.Max(peakDraw, UndertowLens.DrawnRadius(s, W, motion));
            peakR = Math.Max(peakR, s.Radius);
        }
        return peakDraw / peakR;
    }

    [Fact]
    public void Opening_overshoots_a_few_percent_then_settles_on_the_logical_radius()
    {
        double full = PeakDrawOverRadius(UndertowMotion.Full);
        Assert.InRange(full, 1.03, 1.08);
        double reduced = PeakDrawOverRadius(UndertowMotion.Reduced);
        Assert.True(reduced - 1 < (full - 1) * 0.6, $"reduced {reduced:F3} vs full {full:F3}");

        var s = Placed();
        s.Kick = 0;
        for (int i = 0; i < 60 * 40; i++) UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
        Assert.Equal(s.Radius, s.DrawRadius, 6);
    }

    [Fact]
    public void Drawn_lens_never_closes_under_the_minimum_clear_area()
    {
        var s = Placed();
        for (int i = 0; i < 60 * 30; i++)
        {
            if (i % 37 == 0) UndertowLens.Click(s, 500, 400, W, UndertowMotion.Full);
            UndertowLens.Step(s, Dt, 500 + (i % 90) * 7, 400, W, UndertowMotion.Full);
            Assert.True(UndertowLens.DrawnRadius(s, W, UndertowMotion.Full) >= UndertowLens.MinRadius * W - 1e-9);
        }
    }

    [Fact]
    public void A_click_knocks_the_rim_out_and_back_with_one_small_settle()
    {
        Assert.Equal(0, UndertowLens.KnockShape(0));
        Assert.Equal(1, UndertowLens.KnockShape(0.2), 6);
        Assert.Equal(-0.25, UndertowLens.KnockShape(0.7), 6);
        Assert.Equal(0, UndertowLens.KnockShape(1));

        double peak = UndertowLens.KnockPulse(UndertowLens.KnockLife * 0.2, W, UndertowMotion.Full);
        Assert.Equal(UndertowLens.KnockAmp * W, peak, 6);
        Assert.Equal(peak / 2, UndertowLens.KnockPulse(UndertowLens.KnockLife * 2 * 0.2, W, UndertowMotion.Reduced), 6);
        Assert.Equal(0, UndertowLens.KnockPulse(0.05, W, UndertowMotion.Off));

        var s = Placed();
        UndertowLens.Click(s, 500, 400, W, UndertowMotion.Full);
        Assert.Equal(0, s.KnockAge);
        for (int i = 0; i < 30; i++) UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
        Assert.Equal(-1, s.KnockAge);   // spent inside the life
    }

    [Fact]
    public void Rim_fades_in_breathes_slowly_and_stays_soft()
    {
        var s = Placed();
        Assert.Equal(0, UndertowLens.RimAlpha(s, W, UndertowMotion.Full), 6);   // never pops in
        double lo = 1, hi = 0;
        for (int i = 0; i < 60 * 12; i++)
        {
            UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
            double a = UndertowLens.RimAlpha(s, W, UndertowMotion.Full);
            Assert.InRange(a, 0, UndertowLens.RimMax);
            if (s.Age > 3) { lo = Math.Min(lo, a); hi = Math.Max(hi, a); }
        }
        Assert.True(hi - lo > 0.08, $"breath {lo:F3}..{hi:F3}");
        Assert.True(UndertowLens.RimBreathHz < 3);

        // Off: a steady glow after a 120 ms fade, no breath
        var o = Placed(UndertowMotion.Off);
        UndertowLens.Step(o, 0.06, 500, 400, W, UndertowMotion.Off);
        Assert.Equal(UndertowLens.RimBase / 2, UndertowLens.RimAlpha(o, W, UndertowMotion.Off), 6);
        UndertowLens.Step(o, 1, 500, 400, W, UndertowMotion.Off);
        double steady = UndertowLens.RimAlpha(o, W, UndertowMotion.Off);
        UndertowLens.Step(o, 1.3, 500, 400, W, UndertowMotion.Off);
        Assert.Equal(steady, UndertowLens.RimAlpha(o, W, UndertowMotion.Off), 9);
    }

    [Fact]
    public void Rim_stretches_only_along_fast_motion_and_relaxes_when_the_cursor_stops()
    {
        Assert.Equal(0, UndertowLens.Stretch(0, 0, W, UndertowMotion.Full));
        Assert.Equal(UndertowLens.MaxStretch, UndertowLens.Stretch(W * 10, 0, W, UndertowMotion.Full), 9);
        Assert.Equal(UndertowLens.MaxStretch / 2, UndertowLens.Stretch(W * 10, 0, W, UndertowMotion.Reduced), 9);
        Assert.Equal(0, UndertowLens.Stretch(W * 10, 0, W, UndertowMotion.Off));

        var s = Placed();
        for (int i = 0; i < 30; i++) UndertowLens.Step(s, Dt, 500 + i * 30, 400, W, UndertowMotion.Full);
        Assert.True(UndertowLens.Stretch(s.Vx, s.Vy, W, UndertowMotion.Full) > 0.02);
        for (int i = 0; i < 240; i++) UndertowLens.Step(s, Dt, 1400, 400, W, UndertowMotion.Full);
        Assert.Equal(0, UndertowLens.Stretch(s.Vx, s.Vy, W, UndertowMotion.Full));
    }

    [Fact]
    public void A_click_throws_a_few_droplets_up_that_fall_and_end_with_the_pool_capped()
    {
        var s = Placed();
        UndertowLens.Click(s, 700, 500, W, UndertowMotion.Full);
        int live = 0;
        foreach (var d in s.Drops) if (d.Live) { live++; Assert.True(d.Vy < 0); }   // thrown upward
        Assert.Equal(UndertowLens.DropsFull, live);

        for (int c = 0; c < 20; c++) UndertowLens.Click(s, 700, 500, W, UndertowMotion.Full);
        Assert.Equal(UndertowState.MaxDrops, s.Drops.Length);

        int ticks = (int)(UndertowLens.DropLife / Dt) + 2;
        for (int i = 0; i < ticks; i++) UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
        foreach (var d in s.Drops) Assert.False(d.Live);

        Assert.Equal(UndertowLens.DropsReduced, UndertowLens.DropsFor(UndertowMotion.Reduced));
        Assert.Equal(0, UndertowLens.DropsFor(UndertowMotion.Off));
    }

    [Fact]
    public void Crest_fades_once_and_spam_clicks_get_fainter_crests_photosafe()
    {
        Assert.Equal(UndertowLens.CrestPeak, UndertowLens.CrestAt(0, 1, UndertowMotion.Full), 9);
        Assert.True(UndertowLens.CrestAt(0.5, 1, UndertowMotion.Full) < UndertowLens.CrestAt(0.1, 1, UndertowMotion.Full));
        Assert.Equal(UndertowLens.CrestPeak / 2, UndertowLens.CrestAt(0, 1, UndertowMotion.Reduced), 9);
        Assert.Equal(0, UndertowLens.CrestAt(0, 1, UndertowMotion.Off));

        var s = Placed();
        UndertowLens.Click(s, 500, 400, W, UndertowMotion.Full);
        Assert.Equal(1, s.Ripples[0].Crest, 9);
        UndertowLens.Step(s, 0.1, 500, 400, W, UndertowMotion.Full);   // 10 clicks a second
        UndertowLens.Click(s, 500, 400, W, UndertowMotion.Full);
        Assert.Equal(0.3, s.Ripples[1].Crest, 6);
    }

    [Fact]
    public void Off_has_no_knock_no_droplets_and_a_resting_lens_still_stops_repainting()
    {
        var s = Placed(UndertowMotion.Off);
        UndertowLens.Click(s, 800, 600, W, UndertowMotion.Off);
        Assert.Equal(-1, s.KnockAge);
        foreach (var d in s.Drops) Assert.False(d.Live);
        Assert.Equal(s.Radius, UndertowLens.DrawnRadius(s, W, UndertowMotion.Off));

        // Full, settled: the rim breath alone never forces a full-screen repaint
        var f = Placed();
        f.Kick = 0;
        for (int i = 0; i < 60 * 40; i++) UndertowLens.Step(f, Dt, 500, 400, W, UndertowMotion.Full);
        for (int i = 0; i < 120; i++) Assert.False(UndertowLens.Step(f, Dt, 500, 400, W, UndertowMotion.Full));
    }
}
