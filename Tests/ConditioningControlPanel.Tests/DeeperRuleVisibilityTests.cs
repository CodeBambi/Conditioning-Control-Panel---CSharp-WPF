using Xunit;
using static ConditioningControlPanel.Views.Deeper.DeeperEditorGeometry;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1245, "I managed to create an invisible rule in deeper editor" (6.10.1).
///
/// <para>The pin lane only ever considered <c>TimeReachedTrigger</c> rules, and drew them
/// at <c>time / total * width</c> with no clamp. The timeline canvas clips, so two kinds of
/// rule had no presence at all: a time past the end of the media (drawn off the right edge)
/// and a band rule whose region had been deleted or set to "(none)" (never considered). Both
/// stayed in the Items list, so nothing was undeletable - but the timeline was lying.</para>
///
/// <para>These pin the classification and the placement. Everything an assertion here touches
/// is pure; the WPF side is the shapes it hangs off the returned x.</para>
/// </summary>
public class DeeperRuleVisibilityTests
{
    // ------------------------------------------------------------ classification

    [Fact]
    public void A_time_inside_the_media_is_an_ordinary_pin()
        => Assert.Equal(RulePinKind.AtTime, ClassifyRulePin(12.5, hasRegionBand: false, totalSeconds: 60));

    [Fact]
    public void A_time_on_the_last_frame_is_not_past_the_end()
        => Assert.Equal(RulePinKind.AtTime, ClassifyRulePin(60, hasRegionBand: false, totalSeconds: 60));

    [Fact]
    public void A_time_past_the_media_is_flagged_rather_than_dropped()
        => Assert.Equal(RulePinKind.PastEnd, ClassifyRulePin(90, hasRegionBand: false, totalSeconds: 60));

    [Fact]
    public void A_band_rule_leaves_the_pin_lane_to_its_region()
        => Assert.Equal(RulePinKind.OnItsBand, ClassifyRulePin(null, hasRegionBand: true, totalSeconds: 60));

    [Fact]
    public void A_band_rule_that_lost_its_region_becomes_a_gutter_marker()
    {
        // The bug's second route: RemoveRegionFromModel nulls the constraint, or the
        // inspector's Region box is set to "(none)". Nothing drew the rule after that.
        Assert.Equal(RulePinKind.Detached, ClassifyRulePin(null, hasRegionBand: false, totalSeconds: 60));
    }

    [Fact]
    public void A_time_rule_wins_over_its_region_constraint()
    {
        // A time rule can also carry a constraint. Its time is the more specific
        // statement, so it keeps its pin instead of hiding behind the band.
        Assert.Equal(RulePinKind.AtTime, ClassifyRulePin(5, hasRegionBand: true, totalSeconds: 60));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void With_no_media_length_a_time_rule_draws_nothing(double total)
    {
        // The file has not loaded (audio takes its length from the waveform) or it has moved.
        // Parking these in the gutter painted a comb of markers on every editor open, and left
        // one standing for good when the media never arrived. The pin comes back with the
        // duration, which is the only moment its x means anything.
        Assert.Equal(RulePinKind.None, ClassifyRulePin(5, hasRegionBand: false, totalSeconds: total));
    }

    [Fact]
    public void A_bandless_rule_still_takes_its_marker_with_no_media_length()
    {
        // The gutter is for rules that have no place on the ruler at all. Those are not
        // waiting on anything, and hiding them is how #1245 started.
        Assert.Equal(RulePinKind.Detached, ClassifyRulePin(null, hasRegionBand: false, totalSeconds: 0));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_time_that_is_not_a_number_is_a_rule_you_can_still_reach(double time)
    {
        // TryParseDouble accepts "NaN" and Newtonsoft reads one out of a hand-edited file.
        // Left alone it flowed through the placement into Canvas.SetLeft, and the swallowed
        // throw took EVERY pin down with it. It counts as a rule with no usable time.
        Assert.Equal(RulePinKind.Detached, ClassifyRulePin(time, hasRegionBand: false, totalSeconds: 60));
        Assert.Equal(RulePinKind.OnItsBand, ClassifyRulePin(time, hasRegionBand: true, totalSeconds: 60));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void No_placement_hands_the_canvas_a_number_it_cannot_draw(double time)
    {
        var kind = ClassifyRulePin(time, hasRegionBand: false, totalSeconds: 60);
        var x = RulePinX(kind, time, 60, 1000, 0);
        Assert.True(double.IsFinite(x), "a pin x reached the canvas as " + x);
        Assert.True(double.IsFinite(RulePinHitLeft(x, 1000)));
    }

    // ------------------------------------------------------------ placement

    [Fact]
    public void An_ordinary_pin_sits_at_its_share_of_the_width()
        => Assert.Equal(250, RulePinX(RulePinKind.AtTime, 15, 60, 1000, 0), 6);

    [Fact]
    public void An_ordinary_pin_tells_the_truth_about_its_time()
    {
        // The line IS the claim "the rule fires here". Clamping it inboard moved a pin at 0 s
        // and a pin on the last frame by up to the inset, so both lied by a few pixels against
        // a ruler right above them. Only the click target is allowed to move.
        Assert.Equal(0, RulePinX(RulePinKind.AtTime, 0, 60, 1000, 0), 6);
        Assert.Equal(1000, RulePinX(RulePinKind.AtTime, 60, 60, 1000, 0), 6);
    }

    [Fact]
    public void A_pin_on_either_edge_keeps_a_click_target_on_the_canvas()
    {
        // TimelineCanvas clips, so a 14 px target centred on x = 0 or x = width is half gone
        // or all gone. The target slides; the line does not.
        Assert.Equal(0, RulePinHitLeft(0, 1000), 6);
        Assert.Equal(1000 - RulePinHitWidth, RulePinHitLeft(1000, 1000), 6);
        // Anywhere else it stays centred.
        Assert.Equal(250 - RulePinEdgeInset, RulePinHitLeft(250, 1000), 6);
    }

    [Fact]
    public void A_click_target_survives_a_canvas_narrower_than_itself()
        => Assert.Equal(0, RulePinHitLeft(4, 8), 6);

    [Fact]
    public void A_pin_past_the_end_parks_fully_inside_the_clipped_canvas()
    {
        var x = RulePinX(RulePinKind.PastEnd, 9999, 60, 1000, 0);
        Assert.Equal(1000 - RulePinEdgeInset, x, 6);
        Assert.InRange(x, RulePinEdgeInset, 1000 - RulePinEdgeInset);
    }

    [Fact]
    public void Several_rules_past_the_end_do_not_stack_on_one_pixel()
    {
        // They all used to land on the same x, so only the last one built was clickable.
        // They walk LEFT, back towards the timeline they overran.
        var first = RulePinX(RulePinKind.PastEnd, 9999, 60, 1000, 0);
        var second = RulePinX(RulePinKind.PastEnd, 9999, 60, 1000, 1);
        var third = RulePinX(RulePinKind.PastEnd, 9999, 60, 1000, 2);
        Assert.Equal(DetachedRulePinSpacing, first - second, 6);
        Assert.Equal(DetachedRulePinSpacing, second - third, 6);
    }

    [Fact]
    public void The_two_parked_runs_start_at_opposite_edges()
    {
        // Each kind counts its own index, so a detached rule never inherits a past-end
        // rule's place in the queue.
        Assert.Equal(RulePinEdgeInset, RulePinX(RulePinKind.Detached, 0, 60, 1000, 0), 6);
        Assert.Equal(1000 - RulePinEdgeInset, RulePinX(RulePinKind.PastEnd, 99, 60, 1000, 0), 6);
    }

    [Fact]
    public void Gutter_markers_are_spaced_so_each_one_is_its_own_target()
    {
        var first = RulePinX(RulePinKind.Detached, 0, 60, 1000, 0);
        var second = RulePinX(RulePinKind.Detached, 0, 60, 1000, 1);
        Assert.Equal(RulePinEdgeInset, first, 6);
        Assert.Equal(DetachedRulePinSpacing, second - first, 6);
    }

    [Fact]
    public void A_long_run_of_gutter_markers_stacks_rather_than_walks_off()
    {
        Assert.Equal(200 - RulePinEdgeInset, RulePinX(RulePinKind.Detached, 0, 60, 200, 400), 6);
        Assert.Equal(RulePinEdgeInset, RulePinX(RulePinKind.PastEnd, 999, 60, 200, 400), 6);
    }

    [Fact]
    public void A_canvas_narrower_than_the_inset_still_returns_a_point_on_it()
    {
        // Degenerate, but it happens for one layout pass on open. lo must not
        // overshoot hi or Math.Clamp throws.
        var x = RulePinX(RulePinKind.PastEnd, 5, 60, 6, 0);
        Assert.InRange(x, 0, 6);
    }

    [Fact]
    public void Nothing_to_draw_is_placed_nowhere()
        => Assert.Equal(0, RulePinX(RulePinKind.None, 5, 60, 1000, 0), 6);

    [Fact]
    public void Only_the_parked_kinds_read_as_stray()
    {
        // The livery (dotted, dimmer) and the short click target both hang off this.
        Assert.True(IsStrayPin(RulePinKind.PastEnd));
        Assert.True(IsStrayPin(RulePinKind.Detached));
        Assert.False(IsStrayPin(RulePinKind.AtTime));
        Assert.False(IsStrayPin(RulePinKind.OnItsBand));
        Assert.False(IsStrayPin(RulePinKind.None));
    }

    [Fact]
    public void A_zero_width_canvas_answers_zero_instead_of_dividing_by_it()
        => Assert.Equal(0, RulePinX(RulePinKind.AtTime, 5, 60, 0, 0));
}
