using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Flicker Deck juice round: the press squashes before the lift, the flip pops a little wide
/// and settles, a lit band sweeps the face once per flip, cracks run out from their impact and shed
/// a few dust motes, the card lands with one soft bounce, and an exit eases out of the pose.
/// </summary>
public class FlickerDeckJuiceTests
{
    private const double Dt = 1.0 / 120;

    private static FlickerDeckState Card(int breakAt = 4) => new() { BreakAt = breakAt, Index = 1 };

    [Fact]
    public void The_press_squashes_the_card_flat_before_it_lifts()
    {
        var s = Card();
        FlickerDeck.Press(s, MotionLevel.Full);
        FlickerDeck.Step(s, FlickerDeck.AnticipationSec / 2, true, MotionLevel.Full);
        var p = FlickerDeck.Sample(s, MotionLevel.Full);
        Assert.Equal(1 - FlickerDeck.AnticipationSquash, p.SquashY, 6);
        Assert.True(p.ScaleX > 1);
        FlickerDeck.Step(s, FlickerDeck.AnticipationSec, true, MotionLevel.Full);
        Assert.Equal(1, FlickerDeck.Sample(s, MotionLevel.Full).SquashY, 9);
    }

    [Fact]
    public void The_flip_closes_on_the_cosine_and_opens_with_a_small_overshoot_then_settles_on_one()
    {
        Assert.Equal(1, FlickerDeck.FlipWidth(0), 9);
        Assert.Equal(FlickerDeck.MinScaleX, FlickerDeck.FlipWidth(FlickerDeck.FlipSec / 2), 9);
        Assert.Equal(1, FlickerDeck.FlipWidth(FlickerDeck.FlipSec), 9);
        var peak = Enumerable.Range(0, 301).Select(i => FlickerDeck.FlipWidth(FlickerDeck.FlipSec * i / 300.0)).Max();
        // House overshoot is 3-8 percent.
        Assert.InRange(peak, 1.03, 1.08);
        Assert.InRange(peak, 1, 1 + FlickerDeck.FlipOvershoot + 0.002);
    }

    [Fact]
    public void The_sheen_is_one_tinted_pulse_per_flip_brightest_edge_on()
    {
        Assert.Equal(0, FlickerDeck.SheenAt(0).Alpha, 9);
        Assert.Equal(FlickerDeck.SheenMax, FlickerDeck.SheenAt(FlickerDeck.FlipSec / 2).Alpha, 9);
        Assert.Equal(0, FlickerDeck.SheenAt(FlickerDeck.FlipSec).Alpha, 9);
        Assert.True(FlickerDeck.SheenMax <= 0.35);   // never a white flash
        // One rise and one fall across the flip: the band never blinks.
        var rises = 0;
        var prev = 0.0;
        var rising = false;
        for (var i = 1; i <= 300; i++)
        {
            var a = FlickerDeck.SheenAt(FlickerDeck.FlipSec * i / 300.0).Alpha;
            if (a > prev + 1e-12 && !rising) { rises++; rising = true; }
            if (a < prev - 1e-12) rising = false;
            prev = a;
        }
        Assert.Equal(1, rises);
    }

    [Fact]
    public void Flips_stay_under_three_hertz_because_a_busy_card_swallows_clicks()
    {
        var s = Card(breakAt: 5);
        FlickerDeck.Press(s, MotionLevel.Full);
        var t = 0.0;
        while (s.Busy) { Assert.Equal(FlickerPress.Ignored, FlickerDeck.Press(s, MotionLevel.Full)); FlickerDeck.Step(s, Dt, true, MotionLevel.Full); t += Dt; }
        Assert.True(t >= 1.0 / 3);
    }

    [Fact]
    public void The_card_lands_with_one_soft_bounce_after_the_lift()
    {
        Assert.Equal(0, FlickerDeck.Land(0), 9);
        Assert.Equal(0, FlickerDeck.Land(FlickerDeck.LandSec), 9);
        var samples = Enumerable.Range(0, 100).Select(i => FlickerDeck.Land(FlickerDeck.LandSec * i / 100.0)).ToArray();
        Assert.InRange(samples.Max(), 0.01, FlickerDeck.LandAmp);
        Assert.True(Math.Abs(samples.Min()) < samples.Max());   // the return swing is smaller

        var s = Card();
        FlickerDeck.Press(s, MotionLevel.Full);
        while (s.Busy) FlickerDeck.Step(s, Dt, true, MotionLevel.Full);
        Assert.Equal(0, s.SinceLand, 9);
        FlickerDeck.Step(s, 0.07, true, MotionLevel.Full);
        var full = FlickerDeck.Sample(s, MotionLevel.Full).Scale;
        Assert.True(full > 1.005);
        Assert.Equal((full - 1) / 2, FlickerDeck.Sample(s, MotionLevel.Reduced).Scale - 1, 9);
    }

    [Fact]
    public void A_first_crack_waits_for_the_new_face_then_runs_out_from_its_impact()
    {
        var s = Card(breakAt: 3);   // flip 1 brings the faint crack
        FlickerDeck.Press(s, MotionLevel.Full);
        var sawReach = false;
        while (s.Busy)
        {
            FlickerDeck.Step(s, Dt, true, MotionLevel.Full);
            var p = FlickerDeck.Sample(s, MotionLevel.Full);
            if (!s.Swapped) Assert.Equal(0, p.Crack, 9);
            else if (p.CrackReach > 0 && p.CrackReach < 1) sawReach = true;
        }
        Assert.True(sawReach);
        FlickerDeck.Step(s, FlickerDeck.CrackGrowSec, true, MotionLevel.Full);
        var done = FlickerDeck.Sample(s, MotionLevel.Full);
        Assert.Equal(0.25, done.Crack, 9);
        Assert.Equal(1, done.CrackReach, 9);
    }

    [Fact]
    public void Reduced_grows_the_crack_at_half_speed_and_Off_shows_it_at_once()
    {
        var s = Card(breakAt: 3);
        s.Flips = 1;
        s.SinceSwap = FlickerDeck.CrackGrowSec / 2;
        var full = FlickerDeck.CrackShown(s, MotionLevel.Full).Amount;
        var reduced = FlickerDeck.CrackShown(s, MotionLevel.Reduced).Amount;
        Assert.True(reduced < full);
        Assert.Equal((0.25, 1.0), FlickerDeck.CrackShown(s, MotionLevel.Off));
    }

    [Fact]
    public void Dust_falls_only_when_the_crack_deepens_and_never_under_Off()
    {
        Assert.Equal(0, FlickerDeck.CrackMotes(1, 5, MotionLevel.Full));
        Assert.Equal(FlickerDeck.MotesFaint, FlickerDeck.CrackMotes(3, 5, MotionLevel.Full));
        Assert.Equal(FlickerDeck.MotesDeep, FlickerDeck.CrackMotes(4, 5, MotionLevel.Full));
        Assert.Equal(FlickerDeck.MotesDeep / 2, FlickerDeck.CrackMotes(4, 5, MotionLevel.Reduced));
        Assert.Equal(0, FlickerDeck.CrackMotes(4, 5, MotionLevel.Off));

        var sparks = FlickerShatter.SwapSparks(MotionLevel.Full, new Random(4), 7, 20, -10);
        var motes = sparks.Where(x => x.Mote).ToArray();
        Assert.Equal(FlickerShatter.SwapSparkCount, sparks.Count(x => !x.Mote));
        Assert.Equal(7, motes.Length);
        Assert.All(motes, m => Assert.InRange(m.X, 17, 23));
        Assert.All(motes, m => Assert.True(m.Vy > 0));   // they fall out of the crack
        Assert.Empty(FlickerShatter.SwapSparks(MotionLevel.Off, new Random(4), 7, 0, 0));
        var startY = motes.Select(m => m.Y).ToArray();
        for (var i = 0; i < 30; i++) FlickerShatter.StepSparks(sparks, Dt);
        Assert.All(motes.Select((m, i) => m.Y - startY[i]), dy => Assert.True(dy > 0));
    }

    [Fact]
    public void An_exiting_card_eases_out_of_its_pose_to_rest_and_keeps_its_crack()
    {
        var p = new FlickerPose(0.5, 1.08, 0.8, -0.08, 0.1, 0.07, 0.6, 0.97, 0.3, 0.5, 1);
        var start = FlickerDeck.Settle(p, 0);
        Assert.Equal(p.ScaleX, start.ScaleX, 9);
        Assert.Equal(p.Scale, start.Scale, 9);
        Assert.Equal(p.SquashY, start.SquashY, 9);
        Assert.Equal(p.WobbleRad, start.WobbleRad, 9);
        Assert.Equal(p.Sheen, start.Sheen, 9);
        var mid = FlickerDeck.Settle(p, FlickerDeck.ExitSettleSec / 2);
        Assert.InRange(mid.WobbleRad, 0.001, 0.05);
        var rest = FlickerDeck.Settle(p, FlickerDeck.ExitSettleSec);
        Assert.Equal(new FlickerPose(1, 1, 0, 0, 0, 0, 0.6, 1, 0, 0.5, 1), rest);
    }

    [Fact]
    public void The_break_fades_on_an_eased_curve()
    {
        var early = FlickerShatter.ShardAlpha(FlickerShatter.FadeStartSec + 0.1);
        var linearEarly = 1 - 0.1 / FlickerShatter.FadeSec;
        Assert.True(early > linearEarly);   // the fade starts gently
        Assert.Equal(0.5, FlickerShatter.ShardAlpha(FlickerShatter.FadeStartSec + FlickerShatter.FadeSec / 2), 6);
    }

    [Fact]
    public void Off_draws_still_with_no_juice()
    {
        var s = Card(breakAt: 3);
        FlickerDeck.Press(s, MotionLevel.Off);
        var p = FlickerDeck.Sample(s, MotionLevel.Off);
        Assert.Equal(1, p.SquashY);
        Assert.Equal(0, p.Sheen);
        Assert.Equal(1, p.Scale);
    }
}
