using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// Why a frame was cut (doc 02 §2.3). The kind is scored (<see cref="WorthinessScorer"/>) and
    /// named in the projection, so it is part of the wire contract — new members go at the END.
    /// </summary>
    public enum TransitionKind
    {
        /// <summary>Foreground moved to an app that was not the previous one.</summary>
        NewApp,

        /// <summary>Back on an app that was already visited earlier today.</summary>
        ReturnVisit,

        /// <summary>Same app, different page/tab — the cheapest transition there is.</summary>
        TabChange,

        /// <summary>First activity after a real input-idle stretch (greeting moment).</summary>
        WakeFromIdle,

        /// <summary>Several apps churned through inside the dwell gate — ONE event, not many.</summary>
        RapidCycling,

        /// <summary>Came out of a fullscreen stint. The DND suppression's payoff moment.</summary>
        ExitFullscreen,

        /// <summary>SMTC reported a different track/video while the app stayed put.</summary>
        MediaChanged,

        /// <summary>A cumulative-dwell milestone was crossed without the app changing.</summary>
        Milestone
    }

    /// <summary>Coarse time of day. Also the bucket layout of the ledger histogram (6h wide, 4 of them).</summary>
    public enum TimeBucket
    {
        /// <summary>00:00–05:59 local.</summary>
        LateNight = 0,

        /// <summary>06:00–11:59 local.</summary>
        Morning = 1,

        /// <summary>12:00–17:59 local.</summary>
        Afternoon = 2,

        /// <summary>18:00–23:59 local.</summary>
        Evening = 3
    }

    /// <summary>
    /// Scarcity ladder (doc 02 §3.2). Decided BEFORE the LLM call, from worthiness plus which data is
    /// present, and told to the model so it knows what it is writing.
    ///
    /// <para><b>Train 2 implements Common / Uncommon / Rare.</b> <see cref="Legendary"/> exists as an
    /// enum value and an arbiter slot only — the running-gag state it needs is Train 4. Nothing in
    /// Train 2 ever returns it.</para>
    /// </summary>
    public enum RarityTier
    {
        /// <summary>Canned bark, cluster-gated. Free, instant, voiced.</summary>
        Common = 0,

        /// <summary>LLM quip on a worthy snapshot.</summary>
        Uncommon = 1,

        /// <summary>LLM callback armed with real ledger numbers ("third time today…").</summary>
        Rare = 2,

        /// <summary>Running-gag escalation. Train 4.</summary>
        Legendary = 3
    }

    /// <summary>
    /// What SMTC says is playing (doc 02 §2.2). <paramref name="RepeatCount"/> is OUR local counter of
    /// how many times this track has come round in a row — the single richest cheap signal on Windows
    /// and the source of <see cref="TrendKind.MediaLoop"/>.
    ///
    /// <para><b>Privacy:</b> the title/artist are machine-local. They may ride the local (Ollama)
    /// projection; they never reach the cloud projection and are never written to the ledger file.</para>
    /// </summary>
    public sealed record MediaInfo(string Title, string? Artist, string PlaybackState, int RepeatCount);

    /// <summary>
    /// One line the companion already delivered — the raw material of the recent-reaction ban list
    /// (doc 02 §3.1 item 4), which is the single biggest fix for "she says the same three jokes".
    /// </summary>
    public sealed record ReactionSummary(string Text, string? AppId, RarityTier Tier, DateTime At);

    /// <summary>
    /// The single structured object the rest of the AI stack consumes for an awareness moment
    /// (doc 02 §2.3). Built locally, on an EVENT and never on a tick, from the observer's snapshot +
    /// <see cref="ActivityLedger"/> history + in-app state, then scored.
    ///
    /// <para><b>The frame is not the wire format.</b> Only a projection of it leaves the machine —
    /// see <see cref="AwarenessProjection"/>. The frame itself may legitimately hold things the cloud
    /// must never see (<see cref="PageTitleSanitized"/>, <see cref="NowPlaying"/>), because the local
    /// Ollama path is machine-local by definition: one codepath, two projections (doc 02 §6.2).</para>
    ///
    /// <para>Everything here is computable locally in well under a millisecond except the memory
    /// hooks, which are async and, in Train 2, always empty (<see cref="MatchedHabits"/>).</para>
    /// </summary>
    public sealed record ContextFrame
    {
        // ===================== snapshot =====================

        /// <summary>
        /// Resolved app identity: an <see cref="AppClusterMap"/> bespoke app id ("discord", "hades")
        /// or the foreground process name. NEVER a window title.
        /// </summary>
        public string AppId { get; init; } = "";

        /// <summary><see cref="AppClusterMap"/> cluster id ("site_doomscroll", "site_eh", …), or null.</summary>
        public string? AppCluster { get; init; }

        /// <summary>Broad legacy category. Still useful as a coarse angle selector.</summary>
        public ActivityCategory Category { get; init; } = ActivityCategory.Unknown;

        /// <summary>Display name from OUR dictionaries ("YouTube", "Twitter") — not the title.</summary>
        public string ServiceName { get; init; } = "";

        /// <summary>
        /// The page/tab title, sanitised. <b>Null unless the user allow-listed this app for titles</b>
        /// (doc 02 §6.1); the shipped allow list is empty, so this is null for everyone by default.
        /// </summary>
        public string? PageTitleSanitized { get; init; }

        /// <summary>Foreground window covers a whole monitor. Feeds DND and the exit-fullscreen beat.</summary>
        public bool IsFullscreen { get; init; }

        /// <summary>SMTC now-playing, when there is any.</summary>
        public MediaInfo? NowPlaying { get; init; }

        /// <summary>REAL input idle (GetLastInputInfo), not "the title stopped changing".</summary>
        public int InputIdleSeconds { get; init; }

        // ===================== transition =====================

        /// <summary>Why this frame exists.</summary>
        public TransitionKind Transition { get; init; } = TransitionKind.NewApp;

        /// <summary>
        /// Cumulative dwell on this app for the CURRENT visit, before the frame was cut. Tolerant of
        /// sub-30s excursions, so a Discord title flicker no longer resets the clock (doc 02 §2.4).
        /// </summary>
        public int DwellSeconds { get; init; }

        /// <summary>The app we came from, when there was one.</summary>
        public string? PreviousAppId { get; init; }

        /// <summary>Restlessness metric off the session ring.</summary>
        public int SwitchesLast10Min { get; init; }

        // ===================== history (ActivityLedger) =====================

        /// <summary>Visits to this app today, this one included.</summary>
        public int VisitsToday { get; init; }

        /// <summary>Whole minutes on this app today.</summary>
        public int MinutesToday { get; init; }

        /// <summary>Whole minutes on this app across the trailing 7 local days, today included.</summary>
        public int MinutesThisWeek { get; init; }

        /// <summary>Gap between the END of the previous visit and the start of this one. Null on a first-ever visit.</summary>
        public TimeSpan? SinceLastVisit { get; init; }

        /// <summary>Consecutive local days this app was opened, ending today.</summary>
        public int DayStreak { get; init; }

        /// <summary>"morning: work 2h → afternoon: youtube 40m → now". Ids only, never titles.</summary>
        public string DayArcSummary { get; init; } = "";

        // ===================== in-app state =====================

        /// <summary>A CCP session is running right now.</summary>
        public bool CcpSessionRunning { get; init; }

        /// <summary>Progression level.</summary>
        public int UserLevel { get; init; }

        /// <summary>Login streak in days.</summary>
        public int LoginStreakDays { get; init; }

        /// <summary>Achievement unlocked in the last ~30 minutes, if any.</summary>
        public string? RecentAchievementId { get; init; }

        /// <summary>Local time-of-day bucket at cut time.</summary>
        public TimeBucket TimeOfDay { get; init; }

        /// <summary>Local weekday at cut time.</summary>
        public DayOfWeek Weekday { get; init; }

        // ===================== trends =====================

        /// <summary>
        /// Derived ledger events (doc 02 §2.4) — the callback generators. Empty is the common case;
        /// a frame with trends is what makes the difference between an observation and a joke.
        /// </summary>
        public IReadOnlyList<TrendEvent> Trends { get; init; } = Array.Empty<TrendEvent>();

        // ===================== memory hooks =====================

        /// <summary>
        /// Habits matching this app/cluster. <b>Always empty in Train 2</b> — the seam exists so Train
        /// 4 can fill it without touching the frame builder or the prompt. See <see cref="ICompanionMemory"/>.
        /// </summary>
        public IReadOnlyList<HabitRecord> MatchedHabits { get; init; } = Array.Empty<HabitRecord>();

        /// <summary>The last ~10 lines she said about anything — the ban list.</summary>
        public IReadOnlyList<ReactionSummary> RecentReactions { get; init; } = Array.Empty<ReactionSummary>();

        // ===================== scoring =====================

        /// <summary>0..1 novelty term (first-ever &gt; first-today &gt; seen recently).</summary>
        public double Novelty { get; init; }

        /// <summary>0..1 final worthiness score.</summary>
        public double Worthiness { get; init; }

        /// <summary>Tier decided before any LLM call.</summary>
        public RarityTier Tier { get; init; } = RarityTier.Common;

        /// <summary>Local wall-clock time the frame was cut. Used for staleness checks at delivery.</summary>
        public DateTime CutAt { get; init; }

        /// <summary>
        /// True when this frame is about the adult-content cluster, which has its own reaction and
        /// recording toggles and the tightest cloud projection in the system (doc 02 §6.1).
        /// </summary>
        public bool IsAdultCluster =>
            string.Equals(AppCluster, AwarenessClusters.Adult, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cluster ids the awareness code branches on by name. A constant rather than a literal in four
    /// files: a typo in the adult-cluster string would silently widen the cloud projection, which is
    /// the one bug in this feature that cannot be walked back after it ships.
    /// </summary>
    public static class AwarenessClusters
    {
        /// <summary>Adult content. Cluster id only ever crosses the wire — never a service name or title.</summary>
        public const string Adult = "site_eh";

        /// <summary>The infinite-feed cluster. Source of <see cref="TrendKind.Backslide"/>.</summary>
        public const string Doomscroll = "site_doomscroll";
    }
}
