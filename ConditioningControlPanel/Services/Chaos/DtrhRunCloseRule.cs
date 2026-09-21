namespace ConditioningControlPanel.Services.Chaos
{
    /// <summary>
    /// Whether the DtRH host should bank a descent from its own records when the window goes.
    ///
    /// <para>A run is normally banked by the page: the clock runs out (or the pause menu surfaces),
    /// <c>endRun</c> sends <c>run-ended</c>, and the host pays it. A TIMED descent always reaches
    /// its own clock, so that covered everything - until The Bottomless Fall. An endless descent
    /// has no clock, so LEAVING is how it always ends, and holding Escape or closing the frame
    /// banked nothing: no XP, no sparks, and the run counter never moved (Beppu, 2026-09-20).</para>
    ///
    /// <para>The Escape hold is fixed on the page, which now books before it winds down. Closing
    /// the frame cannot be: the window dies and the page gets no chance to say anything. So the
    /// page ships a run snapshot every ten seconds and the teardown banks the last one - but only
    /// when a descent really was in flight and really did go unbanked, or a clean exit would pay
    /// the same run twice.</para>
    /// </summary>
    public static class DtrhRunCloseRule
    {
        /// <summary>
        /// May THIS booking spend the descent? The gate both producers pass through, and the
        /// reason there are two: the page's <c>run-ended</c> and the teardown's synthetic one.
        ///
        /// <para>The host clears <paramref name="runActive"/> as it takes the claim, so whichever
        /// arrives first pays and every later arrival is dropped. That is not theoretical - a
        /// <c>run-ended</c> already queued on the dispatcher can be pumped after the teardown has
        /// banked, and paying it again would double the Sparks and the run counter.</para>
        /// </summary>
        public static bool ShouldPayBooking(bool runActive) => runActive;

        /// <param name="runActive">A descent is still between run-started and run-ended. The
        /// page's own booking clears this, which is what makes the two paths exclusive.</param>
        /// <param name="haveSnapshot">A run-progress snapshot arrived at some point. A descent
        /// abandoned inside its first ten seconds has none, and is worth nothing anyway.</param>
        public static bool ShouldBookOnClose(bool runActive, bool haveSnapshot)
            => ShouldPayBooking(runActive) && haveSnapshot;
    }
}
