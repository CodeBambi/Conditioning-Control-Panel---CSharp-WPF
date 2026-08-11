using System;
using System.Globalization;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The derived ledger events a snapshot can never produce (doc 02 §2.4) — the "how did she know?!"
    /// moments. New members go at the END: the kind name is written into the cloud projection.
    /// </summary>
    public enum TrendKind
    {
        /// <summary>Nth visit to the same app today (n ≥ 3 is where it starts being funny).</summary>
        ReturnVisit,

        /// <summary>Continuous cumulative dwell crossed 30m / 1h / 2h / 3h.</summary>
        LongHaul,

        /// <summary>Same app opened d days running.</summary>
        Streak,

        /// <summary>Same track k times in a row (SMTC).</summary>
        MediaLoop,

        /// <summary>Closed a doomscroll site and reopened it within five minutes.</summary>
        Backslide,

        /// <summary>First activity after ≥3h of REAL input idle.</summary>
        GhostTown,

        /// <summary>Active past this machine's learned typical-bedtime boundary.</summary>
        NightShift
    }

    /// <summary>
    /// One trend, carrying the ledger numbers that produced it (doc 02 §2.4: "the line generator gets
    /// 'Amazon, visit #4, 27 total minutes today, last visit 22 minutes ago' — pre-chewed material").
    ///
    /// <para><b>The numbers must be right.</b> "Fifth visit" when it was the second does not read as a
    /// small bug, it reads as the character being fake, and that is the whole trick gone. This record
    /// is why the ledger is unit-tested to the boundary.</para>
    /// </summary>
    /// <param name="Magnitude">
    /// The number the joke is about, per kind: visit number (ReturnVisit), milestone MINUTES
    /// (LongHaul: 30/60/120/180), consecutive days (Streak), consecutive plays (MediaLoop), seconds
    /// away before relapse (Backslide), whole hours idle (GhostTown), whole hours past the learned
    /// bedtime (NightShift, minimum 1).
    /// </param>
    public sealed record TrendEvent(
        TrendKind Kind,
        string AppId,
        string? Cluster,
        int Magnitude,
        int VisitsToday,
        int MinutesToday,
        int DwellSeconds,
        TimeSpan? SinceLastVisit)
    {
        /// <summary>
        /// True when the event is backed by persisted history rather than by a live signal alone.
        /// This is exactly the test the tier ladder uses: a trend with ledger history behind it earns
        /// <see cref="RarityTier.Rare"/> (a callback with real numbers in it), while a live-signal
        /// trend is a good snapshot and no more.
        ///
        /// <para><see cref="TrendKind.GhostTown"/> is the odd one out: it comes from the input-idle
        /// clock, and "welcome back, sleepyhead" is a greeting, not a callback.</para>
        /// </summary>
        public bool CarriesLedgerHistory => Kind != TrendKind.GhostTown;

        /// <summary>Compact form for the <c>[AWARE]</c> log line and the projection: "ReturnVisit(4)".</summary>
        public string Label => string.Create(CultureInfo.InvariantCulture, $"{Kind}({Magnitude})");
    }
}
