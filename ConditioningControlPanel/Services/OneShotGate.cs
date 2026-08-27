namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// #1045 - the pure decisions behind cancelling point-fired ("one-shot") effects.
    ///
    /// A point-fired flash or subliminal (a Deeper timeline effect, a remote-control trigger, an
    /// Autonomy nudge) is dispatched through an <c>async void</c> loader and then renders for the
    /// authored segment's own duration, which is routinely longer than what is left of the media
    /// that asked for it. Cancelling one cannot key off the services' <c>_oneShotActive</c> latch,
    /// because every arrival guard reads <c>!_isRunning &amp;&amp; !_oneShotActive</c>: while the
    /// user has the ambient Flashes/Subliminals feature running, <c>_isRunning</c> is true and the
    /// latch decides nothing at all.
    ///
    /// So each point-fired effect carries the one-shot GENERATION that was current when it was
    /// dispatched. Cancelling bumps the generation; anything still carrying an older one is stale,
    /// whatever the ambient scheduler is doing. The ambient scheduler's own work carries no
    /// generation (null) and is therefore never stale.
    /// </summary>
    internal static class OneShotGate
    {
        /// <summary>
        /// True when <paramref name="dispatchGeneration"/> belongs to a point-fired effect that has
        /// since been cancelled. Null (ambient work) is never retired.
        /// </summary>
        internal static bool IsRetired(int? dispatchGeneration, int currentGeneration)
            => dispatchGeneration is int gen && gen != currentGeneration;

        /// <summary>
        /// Should a cancel take the subliminal card currently on screen down?
        /// Unlike flashes there is no per-card window to tag: the service reuses one keep-alive
        /// window per screen, so the caller remembers which generation put the visible card up
        /// (<paramref name="visibleGeneration"/>, null when the ambient scheduler did).
        ///
        /// With the ambient scheduler stopped the only thing on screen can be the one-shot, so
        /// blank unconditionally. With it running, blank only when the visible card is the very
        /// generation being retired - anything else belongs to the user's own rhythm.
        /// </summary>
        internal static bool ShouldBlankOnCancel(bool ambientRunning, int? visibleGeneration, int retiredGeneration)
            => !ambientRunning || visibleGeneration == retiredGeneration;
    }
}
