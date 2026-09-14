using System;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The two rules that keep the avatar's speech latch from wedging shut.
    ///
    /// <para><b>The bug these exist for.</b> <c>AvatarTubeWindow._isGiggling</c> is the "a bubble is
    /// on screen" latch, and the ONLY thing that lowers it is the bubble's own hide tick draining the
    /// queue (<c>ProcessNextSpeech</c> with nothing left). That tick refuses to fire while the pointer
    /// is considered to be over the bubble - and WPF raises no <c>MouseLeave</c> when an element is
    /// collapsed out from under the cursor, which several paths do (the chat-history exit, panic,
    /// the listening bubble). One missed leave and the latch is up forever: <c>IsSpeechReady</c>
    /// returns false on every idle tick, so <c>BarkService.DispatchIdle</c> is never called again and
    /// every ordinary bark queues behind a bubble that will never hide. The companion goes completely
    /// silent with nothing in the log to say why.</para>
    ///
    /// <para>It reads as "her voice is gated on something else", because the thing that unwedges it is
    /// any bark at priority >= 100: those route through <c>GigglePriority</c>, which force-clears the
    /// latch and flushes the queue. Opening her eyes fires exactly such a bark
    /// (<c>set_awareness_on</c>, priority 250), which is why a user can reasonably conclude that the
    /// companion's voice needs Awareness on. It does not.</para>
    ///
    /// <para>Pure and static so the decisions are testable without a <c>Window</c>.</para>
    /// </summary>
    public static class SpeechLatchRule
    {
        /// <summary>
        /// How long a hover may hold a bubble open before the hold is treated as a stuck latch rather
        /// than a reader. Generous on purpose: someone genuinely re-reading a long reply with the
        /// pointer parked on it is doing nothing wrong, and this only has to beat "forever".
        /// </summary>
        public static readonly TimeSpan MaxHoverHold = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Should the bubble stay open for this hover? Yes while the pointer is over it AND the hold
        /// has not outlived <see cref="MaxHoverHold"/>. A negative or unset elapsed reads as "just
        /// started", so a caller that has not stamped the hold yet still gets the hold.
        /// </summary>
        public static bool HoldForHover(bool mouseIsOver, TimeSpan heldFor)
        {
            if (!mouseIsOver) return false;
            return heldFor < MaxHoverHold;
        }

        /// <summary>
        /// Is the speech latch stale - up, with nothing that could ever lower it?
        ///
        /// <para>Every legitimate reason for the latch to be up without a bubble on screen is a
        /// pending timer: the pre-speech lead-in, the inter-bubble delay, an in-flight AI reply, or a
        /// queued line waiting its turn. With all four quiet and no bubble visible, nothing will
        /// arrive to call <c>ProcessNextSpeech</c>, so the latch is debris and can be dropped.</para>
        /// </summary>
        public static bool IsLatchStale(
            bool isGiggling,
            bool bubbleVisible,
            bool waitingForAi,
            bool queueEmpty,
            bool leadInPending,
            bool delayPending)
            => isGiggling
               && !bubbleVisible
               && !waitingForAi
               && queueEmpty
               && !leadInPending
               && !delayPending;
    }
}
