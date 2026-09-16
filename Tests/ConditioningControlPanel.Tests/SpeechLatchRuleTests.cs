using System;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests
{
    /// <summary>
    /// The companion-went-silent bug. <c>_isGiggling</c> is lowered only by the bubble's hide tick,
    /// that tick refuses to fire while the hover latch is up, and a collapsed bubble raises no
    /// MouseLeave - so one missed leave silences her for the rest of the session (no idle dispatch,
    /// every bark queued behind a bubble that never hides). These cover both escape hatches.
    /// </summary>
    public class SpeechLatchRuleTests
    {
        // ---- HoldForHover ----

        [Fact]
        public void HoldForHover_holds_while_the_pointer_is_on_a_freshly_hovered_bubble()
        {
            Assert.True(SpeechLatchRule.HoldForHover(mouseIsOver: true, heldFor: TimeSpan.Zero));
            Assert.True(SpeechLatchRule.HoldForHover(mouseIsOver: true, heldFor: TimeSpan.FromSeconds(30)));
        }

        [Fact]
        public void HoldForHover_releases_as_soon_as_the_pointer_is_gone()
        {
            Assert.False(SpeechLatchRule.HoldForHover(mouseIsOver: false, heldFor: TimeSpan.Zero));
            Assert.False(SpeechLatchRule.HoldForHover(mouseIsOver: false, heldFor: TimeSpan.FromHours(1)));
        }

        [Fact]
        public void HoldForHover_gives_up_on_a_hover_that_outlives_the_cap()
        {
            // The stuck-latch case: the flag says "over it" and never goes false again.
            Assert.False(SpeechLatchRule.HoldForHover(
                mouseIsOver: true, heldFor: SpeechLatchRule.MaxHoverHold));
            Assert.False(SpeechLatchRule.HoldForHover(
                mouseIsOver: true, heldFor: SpeechLatchRule.MaxHoverHold + TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public void MaxHoverHold_is_long_enough_to_reread_a_long_reply()
        {
            Assert.True(SpeechLatchRule.MaxHoverHold >= TimeSpan.FromSeconds(60));
        }

        // ---- IsLatchStale ----

        private static bool Stale(
            bool isGiggling = true,
            bool bubbleVisible = false,
            bool waitingForAi = false,
            bool queueEmpty = true,
            bool leadInPending = false,
            bool delayPending = false)
            => SpeechLatchRule.IsLatchStale(
                isGiggling, bubbleVisible, waitingForAi, queueEmpty, leadInPending, delayPending);

        [Fact]
        public void IsLatchStale_true_when_the_latch_is_up_with_nothing_left_to_lower_it()
        {
            Assert.True(Stale());
        }

        [Fact]
        public void IsLatchStale_false_when_the_latch_is_already_down()
        {
            Assert.False(Stale(isGiggling: false));
        }

        [Fact]
        public void IsLatchStale_false_while_a_bubble_is_actually_on_screen()
        {
            // This is the ordinary case and the one the watchdog must never touch: the hide tick
            // owns it.
            Assert.False(Stale(bubbleVisible: true));
        }

        [Fact]
        public void IsLatchStale_false_while_a_reply_or_a_timer_is_still_coming()
        {
            Assert.False(Stale(waitingForAi: true));
            Assert.False(Stale(queueEmpty: false));
            Assert.False(Stale(leadInPending: true));
            Assert.False(Stale(delayPending: true));
        }

        [Fact]
        public void IsLatchStale_false_during_the_lead_in_gap_before_the_bubble_renders()
        {
            // _isGiggling goes up before the bubble paints; the lead-in timer is what says so.
            Assert.False(Stale(bubbleVisible: false, leadInPending: true));
        }
    }
}
