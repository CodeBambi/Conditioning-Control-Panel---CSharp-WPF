using System;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE CHROME THAT WOULD NOT WAIT.
///
/// <para>Owner report, 2026-08-30: <i>"when we hover the buttons next to emi (the drag buttons, the
/// X to close her, or the arrow to resize), they should show and be clickable. Right now I gotta
/// hover EMI and be fast enough to catch those buttons before they disappear."</i></para>
///
/// <para>The old rule was one pair of handlers on her silhouette: enter lit the chrome, leave put it
/// straight back to nothing over 140 ms. Every way of reaching a corner chip crosses her outline for
/// a moment - the arc of the pointer clips the edge, the squash and wobble transforms shrink the hit
/// rect under the cursor, and a grip drag walks the pointer off her within a few pixels - and each
/// one of those started the fade on the very thing being reached for.</para>
///
/// <para><see cref="EmiChromeHover"/> is the whole of that decision and none of the drawing: the
/// chrome is a REGION (her body plus each chip), leaving the region starts a grace, re-entering any
/// part of it cancels the grace, and a gesture in progress pins it lit however far the pointer has
/// wandered. It is pure and clock-injected for the same reason <c>EmiNudgeMachine</c> and
/// <c>EmiRingLayout</c> are: the failure is a timing one, and a timing test that needs a layered
/// always-on-top tool window on screen is a test nobody runs.</para>
/// </summary>
public class EmiChromeHoverTests
{
    private static readonly DateTime T0 = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime At(int ms) => T0.AddMilliseconds(ms);

    // ---------------------------------------------------------------- the region

    [Fact]
    public void It_starts_dark()
    {
        var h = new EmiChromeHover();
        Assert.False(h.Lit);
        Assert.False(h.GracePending);
    }

    [Fact]
    public void Entering_her_body_lights_it_and_the_change_is_reported_once()
    {
        var h = new EmiChromeHover();

        Assert.True(h.Enter(EmiChromePart.Body, T0));
        Assert.True(h.Lit);

        // A second enter of a part already held is not a transition: the caller animates on
        // transitions, and re-animating from 0.95 to 0.95 restarts the fade every mouse move.
        Assert.False(h.Enter(EmiChromePart.Body, At(10)));
        Assert.True(h.Lit);
    }

    [Fact]
    public void Leaving_her_body_does_not_go_dark_at_once_it_goes_on_grace()
    {
        var h = new EmiChromeHover(graceMs: 750);
        h.Enter(EmiChromePart.Body, T0);

        // THE WHOLE POINT. This is the moment that used to start the fade.
        Assert.False(h.Leave(EmiChromePart.Body, At(100)));
        Assert.True(h.Lit);
        Assert.True(h.GracePending);
        Assert.Equal(750, (int)h.GraceRemainingMs(At(100)));
    }

    [Fact]
    public void The_grace_expires_and_only_then_does_it_go_dark()
    {
        var h = new EmiChromeHover(graceMs: 750);
        h.Enter(EmiChromePart.Body, T0);
        h.Leave(EmiChromePart.Body, At(100));

        Assert.False(h.Tick(At(500)));
        Assert.True(h.Lit);

        Assert.True(h.Tick(At(900)));
        Assert.False(h.Lit);
        Assert.False(h.GracePending);
    }

    [Fact]
    public void Crossing_the_air_between_her_edge_and_a_chip_never_dims_it()
    {
        var h = new EmiChromeHover(graceMs: 750);
        h.Enter(EmiChromePart.Body, T0);

        // The arc toward a corner: off the silhouette for a moment, then onto the gear. WPF can
        // deliver these either way round, so both orders are walked.
        h.Leave(EmiChromePart.Body, At(40));
        Assert.False(h.Enter(EmiChromePart.Gear, At(60)));   // no transition: it never went out
        Assert.True(h.Lit);
        Assert.False(h.GracePending);                         // and the grace was cancelled

        // The other order: the child's enter arrives before the parent's leave.
        h.Enter(EmiChromePart.Body, At(80));
        h.Leave(EmiChromePart.Gear, At(90));
        h.Leave(EmiChromePart.Body, At(95));
        Assert.True(h.Lit);
        Assert.True(h.GracePending);
    }

    [Fact]
    public void Two_parts_at_once_need_both_leaves_before_the_grace_starts()
    {
        var h = new EmiChromeHover(graceMs: 750);
        h.Enter(EmiChromePart.Body, T0);
        h.Enter(EmiChromePart.Close, At(20));

        h.Leave(EmiChromePart.Close, At(40));
        Assert.True(h.Lit);
        Assert.False(h.GracePending);      // still on her body

        h.Leave(EmiChromePart.Body, At(60));
        Assert.True(h.GracePending);
    }

    [Fact]
    public void Coming_back_inside_the_grace_cancels_it_and_reports_no_transition()
    {
        var h = new EmiChromeHover(graceMs: 750);
        h.Enter(EmiChromePart.Body, T0);
        h.Leave(EmiChromePart.Body, At(100));

        Assert.False(h.Enter(EmiChromePart.Close, At(400)));
        Assert.True(h.Lit);
        Assert.False(h.GracePending);

        // And the clock past the ORIGINAL deadline changes nothing: it was cancelled, not paused.
        Assert.False(h.Tick(At(2000)));
        Assert.True(h.Lit);
    }

    [Fact]
    public void Leaving_again_after_a_cancel_starts_a_fresh_full_grace()
    {
        var h = new EmiChromeHover(graceMs: 750);
        h.Enter(EmiChromePart.Body, T0);
        h.Leave(EmiChromePart.Body, At(100));
        h.Enter(EmiChromePart.Close, At(400));
        h.Leave(EmiChromePart.Close, At(500));

        Assert.Equal(750, (int)h.GraceRemainingMs(At(500)));
        Assert.False(h.Tick(At(1200)));
        Assert.True(h.Lit);
        Assert.True(h.Tick(At(1260)));
        Assert.False(h.Lit);
    }

    // ---------------------------------------------------------------- the holds

    [Fact]
    public void A_grip_drag_pins_it_lit_however_far_the_pointer_wanders()
    {
        var h = new EmiChromeHover(graceMs: 750);
        h.Enter(EmiChromePart.Grip, T0);
        h.Hold(EmiChromeHold.Resize, true, At(10));

        // Growing her walks the cursor down and right, off everything, in the first ten pixels.
        h.Leave(EmiChromePart.Grip, At(20));
        Assert.False(h.Tick(At(60_000)));
        Assert.True(h.Lit);
        Assert.False(h.GracePending);

        // The release is what hands it back to the grace, not the leave.
        Assert.False(h.Hold(EmiChromeHold.Resize, false, At(60_100)));
        Assert.True(h.Lit);
        Assert.True(h.GracePending);
        Assert.True(h.Tick(At(61_000)));
        Assert.False(h.Lit);
    }

    [Fact]
    public void A_hold_lights_it_from_dark_and_reports_the_transition()
    {
        var h = new EmiChromeHover();
        Assert.True(h.Hold(EmiChromeHold.Menu, true, T0));
        Assert.True(h.Lit);
    }

    [Fact]
    public void Holds_are_counted_separately_and_the_last_one_out_starts_the_grace()
    {
        var h = new EmiChromeHover(graceMs: 750);
        h.Enter(EmiChromePart.Body, T0);
        h.Hold(EmiChromeHold.Drag, true, At(10));
        h.Hold(EmiChromeHold.Press, true, At(20));
        h.Leave(EmiChromePart.Body, At(30));

        h.Hold(EmiChromeHold.Drag, false, At(40));
        Assert.False(h.GracePending);        // Press is still down

        h.Hold(EmiChromeHold.Press, false, At(50));
        Assert.True(h.GracePending);
    }

    [Fact]
    public void Her_options_panel_keeps_the_gear_that_opened_it_on_screen()
    {
        var h = new EmiChromeHover(graceMs: 750);
        h.Enter(EmiChromePart.Gear, T0);
        h.Hold(EmiChromeHold.Press, true, At(5));
        h.Hold(EmiChromeHold.Menu, true, At(10));      // the panel opened
        h.Hold(EmiChromeHold.Press, false, At(15));
        h.Leave(EmiChromePart.Gear, At(400));          // the pointer travels to the panel

        Assert.False(h.Tick(At(30_000)));
        Assert.True(h.Lit);

        // The panel folds; now the grace runs and she tidies herself up.
        h.Hold(EmiChromeHold.Menu, false, At(30_100));
        Assert.True(h.Tick(At(31_000)));
        Assert.False(h.Lit);
    }

    // ---------------------------------------------------------------- housekeeping

    [Fact]
    public void Reset_drops_everything_and_reports_whether_that_changed_anything()
    {
        var h = new EmiChromeHover();
        h.Enter(EmiChromePart.Body, T0);
        h.Hold(EmiChromeHold.Drag, true, At(10));

        Assert.True(h.Reset());
        Assert.False(h.Lit);
        Assert.False(h.GracePending);
        Assert.Equal(EmiChromePart.None, h.Over);
        Assert.Equal(EmiChromeHold.None, h.Holds);

        // Idempotent: hiding her twice is not a transition.
        Assert.False(h.Reset());
    }

    [Fact]
    public void An_unmatched_leave_is_harmless()
    {
        var h = new EmiChromeHover();

        // Under mouse capture WPF can deliver a leave whose enter never arrived. It must not put
        // the region into a state where the next real enter is treated as "already here".
        Assert.False(h.Leave(EmiChromePart.Grip, T0));
        Assert.False(h.Lit);

        Assert.True(h.Enter(EmiChromePart.Grip, At(10)));
        Assert.True(h.Lit);
    }

    [Fact]
    public void A_zero_grace_goes_dark_on_the_leave_itself()
    {
        // The old behaviour, kept reachable, and the edge that used to arm a grace of no length and
        // then report it as lit until something ticked.
        var h = new EmiChromeHover(graceMs: 0);
        h.Enter(EmiChromePart.Body, T0);

        Assert.True(h.Leave(EmiChromePart.Body, At(100)));
        Assert.False(h.Lit);
        Assert.False(h.GracePending);
    }

    [Fact]
    public void The_default_grace_is_long_enough_to_cross_to_a_corner_and_short_enough_not_to_linger()
    {
        // 750 ms. Fitts' law puts a deliberate correction at roughly half a second on a target this
        // size; below ~500 ms the owner's original complaint comes straight back, and much past a
        // second the chrome reads as stuck on rather than as following the pointer.
        Assert.Equal(750, EmiChromeHover.DefaultGraceMs);
        Assert.Equal(750, new EmiChromeHover().GraceMs);
    }
}
