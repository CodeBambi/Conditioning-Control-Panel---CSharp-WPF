namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// How close the ceremony is, named. The boundaries are the contract's
    /// (CONTRACT-FUSE-0816 §2.1) and every surface keys off this enum rather than off raw
    /// arithmetic, so "when does the spark appear" has exactly one answer in the codebase.
    ///
    /// <para>Ordered from far to near ON PURPOSE: surfaces ask
    /// <c>phase &gt;= DescentFusePhase.Clock</c> ("from the Clock phase onward"), which is how the
    /// contract phrases every rule. <see cref="Dark"/> sorts below everything, so that comparison
    /// is also the "is anything showing at all" check.</para>
    /// </summary>
    public enum DescentFusePhase
    {
        /// <summary>No fuse: no cached timestamp, or more than seven days out. Nothing renders.</summary>
        Dark = 0,
        /// <summary>≤7 days. The spark appears. No digits, no explanation.</summary>
        Whisper = 1,
        /// <summary>≤72 hours. Hovering the spark reads out T-minus.</summary>
        Clock = 2,
        /// <summary>≤24 hours. Neutral chrome starts darkening, one step per 6h.</summary>
        Dimming = 3,
        /// <summary>≤12 hours. A candle beside the companion.</summary>
        Candle = 4,
        /// <summary>≤1 hour. The readout is promoted to a persistent corner element.</summary>
        Vigil = 5,
        /// <summary>≤10 minutes. Gold digits, then silence.</summary>
        Terminal = 6,
        /// <summary>The instant has passed.</summary>
        Zero = 7,
    }
}
