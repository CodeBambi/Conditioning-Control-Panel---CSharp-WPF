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
        Assert.Equal(0, s.Radius);                                 // never a full lens on frame one
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
        Assert.Equal(0.18 * W, s.Radius, 6);
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
        Assert.Equal(300 - 0.1 * (0.045 * W + 0.3 * 300), next, 6);
        Assert.Equal(0, UndertowLens.Shrink(5, 1, W, UndertowMotion.Full));
    }

    [Fact]
    public void Left_alone_the_lens_shrinks_to_zero_and_stops_animating()
    {
        var s = Placed();
        s.Kick = 0;
        s.Radius = UndertowLens.MaxRadius * W;
        double last = s.Radius;
        for (int i = 0; i < 60 * 10; i++)
        {
            UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
            Assert.True(s.Radius <= last);
            last = s.Radius;
        }
        Assert.Equal(0, s.Radius);
        Assert.False(s.Animating);
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
        s.Radius = 0;
        s.Kick = 0;
        UndertowLens.Click(s, 500, 400, W, UndertowMotion.Full);
        Assert.Equal(0.15 * W, s.Kick, 6);
        UndertowLens.Step(s, 0.1, 500, 400, W, UndertowMotion.Full);
        // shrink from 0 stays 0, then at most 0.75 W per second of kick lands
        Assert.Equal(0.075 * W, s.Radius, 6);
        Assert.Equal(0.075 * W, s.Kick, 6);
    }

    [Fact]
    public void Many_clicks_never_push_the_lens_past_the_cap()
    {
        var s = Placed();
        for (int c = 0; c < 20; c++) UndertowLens.Click(s, 500, 400, W, UndertowMotion.Full);
        for (int i = 0; i < 300; i++)
        {
            UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
            Assert.True(s.Radius <= 0.36 * W + 1e-9);
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
    public void Ripple_sizes_scale_with_dpi_because_the_layer_draws_in_physical_pixels()
    {
        Assert.True(UndertowLens.RingAt(0.05, 0, W, UndertowMotion.Full, out double r1, out _, out double w1));
        Assert.True(UndertowLens.RingAt(0.05, 0, W, UndertowMotion.Full, out double r2, out _, out double w2, 2.0));
        Assert.Equal(w1 * 2, w2, 6);
        Assert.Equal(r1 + UndertowLens.RippleBase, r2, 6);   // only the birth radius is in px; the reach is a screen share

        Assert.True(UndertowLens.RingAt(0.06, 0, W, UndertowMotion.Off, out _, out _, out double wo, 1.5));
        Assert.Equal(4.5, wo, 6);
    }

    [Fact]
    public void Step_asks_for_a_repaint_only_when_something_drawn_moved()
    {
        var s = new UndertowState();
        Assert.True(UndertowLens.Step(s, 0, 500, 400, W, UndertowMotion.Off));   // placement
        Assert.False(UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Off)); // still lens, still cursor
        Assert.True(UndertowLens.Step(s, Dt, 520, 400, W, UndertowMotion.Off));  // cursor moved

        UndertowLens.Click(s, 520, 400, W, UndertowMotion.Off);
        Assert.True(UndertowLens.Step(s, Dt, 520, 400, W, UndertowMotion.Off));  // fade ring alive

        // closed lens: a moving cursor alone changes nothing on screen
        var c = Placed();
        c.Kick = 0; c.Radius = 0;
        Assert.False(UndertowLens.Step(c, Dt, 900, 700, W, UndertowMotion.Full));

        // a shrinking open lens repaints every tick
        var o = Placed();
        o.Kick = 0; o.Radius = 0.3 * W;
        Assert.True(UndertowLens.Step(o, Dt, 500, 400, W, UndertowMotion.Full));
    }

    [Fact]
    public void A_click_drops_a_double_ripple_that_dies_after_point_eight_seconds()
    {
        var s = Placed();
        UndertowLens.Click(s, 700, 300, W, UndertowMotion.Full);
        Assert.True(s.Ripples[0].Live);
        Assert.Equal(700, s.Ripples[0].X);

        // ring 1 starts 0.1 s after ring 0
        Assert.True(UndertowLens.RingAt(0.05, 0, W, UndertowMotion.Full, out _, out _, out _));
        Assert.False(UndertowLens.RingAt(0.05, 1, W, UndertowMotion.Full, out _, out _, out _));
        Assert.True(UndertowLens.RingAt(0.2, 1, W, UndertowMotion.Full, out _, out _, out double w1));
        Assert.Equal(1.5, w1, 6);

        UndertowLens.RingAt(0.35, 0, W, UndertowMotion.Full, out double r, out double a, out _);
        Assert.Equal(0.5 * 0.18 * W + 6, r, 6);
        Assert.Equal(0.65 * 0.5, a, 6);

        for (int i = 0; i < 49; i++) UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
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
    public void Reduced_widens_but_draws_no_ripple()
    {
        var s = Placed(UndertowMotion.Reduced);
        UndertowLens.Click(s, 500, 400, W, UndertowMotion.Reduced);
        Assert.True(s.Kick > 0);
        foreach (var r in s.Ripples) Assert.False(r.Live);
        Assert.False(UndertowLens.RingAt(0.2, 0, W, UndertowMotion.Reduced, out _, out _, out _));
    }

    [Fact]
    public void Off_keeps_a_still_lens_and_answers_a_click_with_a_120ms_fade()
    {
        var s = Placed(UndertowMotion.Off);
        for (int i = 0; i < 600; i++) UndertowLens.Step(s, Dt, 800, 600, W, UndertowMotion.Off);
        Assert.Equal(0.18 * W, s.Radius, 6);
        Assert.Equal(800, s.X);

        UndertowLens.Click(s, 800, 600, W, UndertowMotion.Off);
        Assert.Equal(0, s.Kick);
        Assert.True(UndertowLens.RingAt(0.06, 0, W, UndertowMotion.Off, out double r, out double a, out _));
        Assert.Equal(0.18 * W, r, 6);   // it never grows
        Assert.Equal(0.65 * 0.5, a, 6);
        Assert.False(UndertowLens.RingAt(0.06, 1, W, UndertowMotion.Off, out _, out _, out _));
        Assert.False(UndertowLens.RingAt(0.12, 0, W, UndertowMotion.Off, out _, out _, out _));

        for (int i = 0; i < 10; i++) UndertowLens.Step(s, Dt, 800, 600, W, UndertowMotion.Off);
        Assert.False(s.Ripples[0].Live);
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
        s.Reset();
        Assert.Equal(0, s.Radius);
        Assert.Equal(0, s.Kick);
        Assert.False(s.Placed);
        Assert.False(s.Animating);
    }
}
