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
    public void With_no_media_length_even_a_time_rule_goes_to_the_gutter(double total)
    {
        // Audio, or a video whose duration has not landed yet. The ruler means nothing,
        // so there is no honest x - but the rule still has to be clickable.
        Assert.Equal(RulePinKind.Detached, ClassifyRulePin(5, hasRegionBand: false, totalSeconds: total));
    }

    // ------------------------------------------------------------ placement

    [Fact]
    public void An_ordinary_pin_sits_at_its_share_of_the_width()
        => Assert.Equal(250, RulePinX(RulePinKind.AtTime, 15, 60, 1000, 0), 6);

    [Fact]
    public void A_pin_past_the_end_parks_fully_inside_the_clipped_canvas()
    {
        var x = RulePinX(RulePinKind.PastEnd, 9999, 60, 1000, 0);
        Assert.Equal(1000 - RulePinEdgeInset, x, 6);
        Assert.InRange(x, RulePinEdgeInset, 1000 - RulePinEdgeInset);
    }

    [Fact]
    public void A_time_at_the_very_end_no_longer_lands_on_the_clip_line()
    {
        // The old maths put this at exactly x = width, which the canvas clips away
        // along with its 14 px hit rect.
        var x = RulePinX(RulePinKind.AtTime, 60, 60, 1000, 0);
        Assert.Equal(1000 - RulePinEdgeInset, x, 6);
    }

    [Fact]
    public void A_negative_time_lands_on_the_left_edge_not_off_it()
        => Assert.Equal(RulePinEdgeInset, RulePinX(RulePinKind.AtTime, -30, 60, 1000, 0), 6);

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
        var x = RulePinX(RulePinKind.Detached, 0, 60, 200, 400);
        Assert.Equal(200 - RulePinEdgeInset, x, 6);
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
    public void A_zero_width_canvas_answers_zero_instead_of_dividing_by_it()
        => Assert.Equal(0, RulePinX(RulePinKind.AtTime, 5, 60, 0, 0));
}
