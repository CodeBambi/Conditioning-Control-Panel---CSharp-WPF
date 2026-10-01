using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Flicker Deck: every flash a card. A click lifts it, raises it in the air (never on the
/// click frame), flips it to a new picture at the flip's edge, and the click that reaches BreakAt
/// shatters it instead. These drive the same Press / Step / Sample calls the layer does.
/// </summary>
public class FlickerDeckTests
{
    private const double Dt = 1.0 / 120;

    private static FlickerDeckState Card(int breakAt = 4) => new() { BreakAt = breakAt, Index = 1 };

    /// <summary>Run the step until idle, recording when each event fired (seconds since the press).</summary>
    private static (double raise, double swap, int frames) RunFlip(FlickerDeckState s, MotionLevel level,
        Func<double, bool> ready)
    {
        double t = 0, raise = -1, swap = -1;
        int n = 0;
        while (s.Busy && n < 10000)
        {
            var ev = FlickerDeck.Step(s, Dt, ready(t), level);
            t += Dt;
            n++;
            if ((ev & FlickerEvents.Raise) != 0) raise = t;
            if ((ev & FlickerEvents.Swap) != 0) swap = t;
        }
        return (raise, swap, n);
    }

    [Fact]
    public void BreakAt_rolls_three_to_five()
    {
        var rng = new Random(7);
        var seen = Enumerable.Range(0, 400).Select(i => FlickerDeck.Create(i, rng).BreakAt).Distinct().OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 3, 4, 5 }, seen);
    }

    [Fact]
    public void The_click_reaching_BreakAt_shatters_instead_of_flipping()
    {
        var s = Card(breakAt: 3);
        Assert.Equal(FlickerPress.Flip, FlickerDeck.Press(s, MotionLevel.Full));
        RunFlip(s, MotionLevel.Full, _ => true);
        Assert.Equal(FlickerPress.Flip, FlickerDeck.Press(s, MotionLevel.Full));
        RunFlip(s, MotionLevel.Full, _ => true);
        Assert.Equal(2, s.Flips);
        Assert.Equal(FlickerPress.Shatter, FlickerDeck.Press(s, MotionLevel.Full));
    }

    [Fact]
    public void A_busy_card_swallows_clicks()
    {
        var s = Card();
        FlickerDeck.Press(s, MotionLevel.Full);
        FlickerDeck.Step(s, 0.05, false, MotionLevel.Full);
        Assert.Equal(FlickerPress.Ignored, FlickerDeck.Press(s, MotionLevel.Full));
    }

    [Fact]
    public void Raise_happens_in_the_air_never_on_the_click_frame()
    {
        var s = Card();
        FlickerDeck.Press(s, MotionLevel.Full);
        var first = FlickerDeck.Step(s, Dt, true, MotionLevel.Full);
        Assert.Equal(FlickerEvents.None, first & FlickerEvents.Raise);
        s = Card();
        FlickerDeck.Press(s, MotionLevel.Full);
        var (raise, swap, _) = RunFlip(s, MotionLevel.Full, _ => true);
        Assert.InRange(raise, FlickerDeck.RaiseAtSec, FlickerDeck.RaiseAtSec + 2 * Dt);
        // The picture swaps at the flip's midpoint, after the raise.
        Assert.InRange(swap, FlickerDeck.LiftLeadSec + FlickerDeck.FlipSec / 2 - Dt, FlickerDeck.LiftLeadSec + FlickerDeck.FlipSec / 2 + 3 * Dt);
        Assert.True(raise < swap);
    }

    [Fact]
    public void Flip_waits_at_the_edge_for_the_picture_then_opens()
    {
        var s = Card();
        FlickerDeck.Press(s, MotionLevel.Full);
        var (_, swap, _) = RunFlip(s, MotionLevel.Full, t => t > 0.8);
        Assert.InRange(swap, 0.8, 0.8 + 2 * Dt);
        Assert.Equal(1, s.Flips);
        Assert.False(s.Busy);
    }

    [Fact]
    public void Flip_gives_up_on_a_picture_that_never_lands()
    {
        var s = Card();
        FlickerDeck.Press(s, MotionLevel.Full);
        var gaveUp = false;
        for (int i = 0; i < 1000 && s.Busy; i++)
            gaveUp |= (FlickerDeck.Step(s, Dt, false, MotionLevel.Full) & FlickerEvents.GaveUp) != 0;
        Assert.True(gaveUp);
        Assert.False(s.Busy);
        Assert.Equal(1, s.Flips);
    }

    [Fact]
    public void ScaleX_squashes_through_the_flip_and_never_mirrors()
    {
        var s = Card();
        FlickerDeck.Press(s, MotionLevel.Full);
        double min = 1;
        while (s.Busy)
        {
            FlickerDeck.Step(s, Dt, true, MotionLevel.Full);
            var p = FlickerDeck.Sample(s, MotionLevel.Full);
            // Juice: the opening half pops a few percent wide, the press and the landing nudge the scale.
            Assert.InRange(p.ScaleX, FlickerDeck.MinScaleX, 1 + FlickerDeck.FlipOvershoot + 0.002);
            Assert.InRange(p.Scale, 1 - FlickerDeck.LandAmp, 1 + FlickerDeck.LiftScale + 1e-9);
            min = Math.Min(min, p.ScaleX);
        }
        Assert.True(min < 0.1);
    }

    [Fact]
    public void Wobble_grows_with_the_flips_and_follows_the_formula()
    {
        var s = Card(breakAt: 5);
        Assert.Equal(0, FlickerDeck.Wobble(s));
        s.Flips = 4;
        s.SinceFlip = 99;
        s.Clock = 0.37;
        var a = Math.Pow(4.0 / 5, 1.4) * 0.15;
        Assert.Equal(Math.Sin(0.37 * (9 + 1.3)) * a, FlickerDeck.Wobble(s), 9);
        // After a flip the 40 rad/s ring rides on top and is gone after 0.7 s.
        s.SinceFlip = 0.1;
        Assert.Equal(Math.Sin(0.37 * 10.3) * a + Math.Sin(0.1 * 40) * a * 0.9 * (1 - 0.1 / 0.7), FlickerDeck.Wobble(s), 9);
    }

    [Fact]
    public void Reduced_halves_amplitude_and_Off_is_still()
    {
        var s = Card(breakAt: 3);
        s.Flips = 2;
        s.Clock = 0.2;
        var full = FlickerDeck.Sample(s, MotionLevel.Full).WobbleRad;
        Assert.Equal(full / 2, FlickerDeck.Sample(s, MotionLevel.Reduced).WobbleRad, 9);
        var off = FlickerDeck.Sample(s, MotionLevel.Off);
        Assert.Equal(new FlickerPose(1, 1, 0, 0, 0, 0, 0.6), off);
    }

    [Fact]
    public void Off_raises_at_once_and_swaps_when_the_picture_lands()
    {
        var s = Card();
        FlickerDeck.Press(s, MotionLevel.Off);
        Assert.Equal(1, s.Flips);
        var ev = FlickerDeck.Step(s, Dt, false, MotionLevel.Off);
        Assert.True((ev & FlickerEvents.Raise) != 0);
        ev = FlickerDeck.Step(s, Dt, true, MotionLevel.Off);
        Assert.True((ev & FlickerEvents.Swap) != 0);
        Assert.False(s.Busy);
    }

    [Theory]
    [InlineData(3, 0, 0.0)]
    [InlineData(3, 1, 0.25)]
    [InlineData(3, 2, 0.6)]
    [InlineData(5, 2, 0.0)]
    [InlineData(5, 3, 0.25)]
    [InlineData(5, 4, 0.6)]
    public void Cracks_show_on_the_flip_before_the_last_and_faintly_one_earlier(int breakAt, int flips, double crack)
        => Assert.Equal(crack, FlickerDeck.CrackAmount(flips, breakAt));

    [Fact]
    public void A_buried_card_peeks_outward_from_the_cluster_over_three_tenths_of_a_second()
    {
        var (dx, dy) = FlickerDeck.PeekDirection(300, 100, 200, 100);
        Assert.Equal(1, dx, 9);
        Assert.Equal(0, dy, 9);
        var s = Card();
        s.Hover = true;
        FlickerDeck.Step(s, 0.15, false, MotionLevel.Full);
        Assert.Equal(0.5, s.Peek, 6);
        FlickerDeck.Step(s, 0.2, false, MotionLevel.Full);
        Assert.Equal(1, s.Peek, 6);
        Assert.Equal(FlickerDeck.PeekFrac, FlickerDeck.Sample(s, MotionLevel.Full).PeekFrac, 9);
        s.Hover = false;
        FlickerDeck.Step(s, 0.3, false, MotionLevel.Full);
        Assert.Equal(0, s.Peek, 6);
    }

    [Fact]
    public void Owned_v2_and_special_flashes_keep_Super_out()
    {
        Assert.True(FlickerDeck.Applies(true, true, true, false, false, false, false, false));
        Assert.False(FlickerDeck.Applies(false, true, true, false, false, false, false, false));
        Assert.False(FlickerDeck.Applies(true, false, true, false, false, false, false, false)); // classic path
        Assert.False(FlickerDeck.Applies(true, true, false, false, false, false, false, false)); // not clickable
        Assert.False(FlickerDeck.Applies(true, true, true, true, false, false, false, false));  // v2 preview
        Assert.False(FlickerDeck.Applies(true, true, true, false, true, false, false, false));  // remix
        Assert.False(FlickerDeck.Applies(true, true, true, false, false, true, false, false));  // natasha
        Assert.False(FlickerDeck.Applies(true, true, true, false, false, false, true, false));  // owned drag
        Assert.False(FlickerDeck.Applies(true, true, true, false, false, false, false, true));  // owned shatter
    }

    [Fact]
    public void Crack_lines_stay_inside_the_picture()
    {
        var lines = FlickerDeck.CrackLines(new Random(3));
        Assert.InRange(lines.Length, 3, 4);
        Assert.All(lines, l => Assert.All(l, v => Assert.InRange(v, 0, 1)));
    }

    [Fact]
    public void No_picture_opens_on_the_old_face_at_the_edge_without_the_long_hold()
    {
        var s = Card();
        Assert.Equal(FlickerPress.Flip, FlickerDeck.Press(s, MotionLevel.Full));
        s.NoPicture = true;
        double t = 0;
        var gaveUp = -1.0;
        int n = 0;
        while (s.Busy && n++ < 10000)
        {
            var ev = FlickerDeck.Step(s, Dt, false, MotionLevel.Full);
            t += Dt;
            if ((ev & FlickerEvents.GaveUp) != 0) gaveUp = t;
        }
        Assert.InRange(gaveUp, 0, FlickerDeck.LiftLeadSec + FlickerDeck.FlipSec / 2 + 0.05);
        Assert.False(s.Busy);
        Assert.Equal(1, s.Flips);
    }

    [Fact]
    public void A_new_press_clears_no_picture()
    {
        var s = Card();
        s.NoPicture = true;
        FlickerDeck.Press(s, MotionLevel.Full);
        Assert.False(s.NoPicture);
    }
}
