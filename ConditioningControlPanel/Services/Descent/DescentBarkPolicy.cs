using System;

namespace ConditioningControlPanel.Services.Descent
{
    // ============================================================================
    // THE DESCENT'S BARKS — which moment, if any, is worth saying out loud.
    //
    // This file is PURE. It reads a parsed block plus a small persisted memory and
    // returns at most one moment; it never touches App, the avatar, the network or
    // the disk. That is deliberate: the loop it narrates (tap → jar → banked day →
    // stage) turns over once a day at most, so the only way to be sure the edges
    // fire exactly once is to be able to test them without waiting a week.
    //
    // GARNISH, NEVER THE LESSON. Barks are dropped entirely when the avatar window
    // does not exist (BarkService's own gate), so nothing here may be the only
    // place a mechanic is explained — the tooltips and the help surfaces own the
    // teaching, and these lines sit on top of it.
    //
    // ONE MOMENT PER OBSERVATION. A day that banks can also cross a stage, and
    // firing both would put two bubbles a second apart on a user who was told they
    // would never be nagged. The higher moment wins and the rest are still marked
    // as spent, so the loser does not resurface an hour later out of context.
    // ============================================================================

    /// <summary>The one thing an observation is worth saying, or <see cref="None"/>.</summary>
    public enum DescentBarkMoment
    {
        None = 0,

        /// <summary>Today's jar is climbing toward the line that banks the day.</summary>
        NearBank,

        /// <summary>Today crossed the line and is banked. Once per UTC day, forever.</summary>
        DayBanked,

        /// <summary>A new stage on the seven-rung ladder.</summary>
        StageCrossed,

        /// <summary>Back after days away, with the relapse bonus paying out. NEVER "gravity".</summary>
        LapseReturn,
    }

    /// <summary>
    /// What the watcher has already said, persisted between launches so a restart
    /// cannot replay a day's milestone. Plain settable properties because it round
    /// trips through Newtonsoft; every field is a key, not a count.
    /// </summary>
    public sealed class DescentBarkMemory
    {
        /// <summary>
        /// False until the first block this install has ever seen. The first
        /// observation SEEDS and says nothing: an account that already banked today
        /// before the feature existed must not be congratulated for it on launch.
        /// </summary>
        public bool Seeded { get; set; }

        /// <summary>UTC day (YYYY-MM-DD) whose banking has already been announced.</summary>
        public string? LastBankedDay { get; set; }

        /// <summary>UTC day on which the approaching-the-line nudge has already been spent.</summary>
        public string? LastNearBankDay { get; set; }

        /// <summary>Stage number already announced. Null until seeded.</summary>
        public int? LastStage { get; set; }

        /// <summary>
        /// Identity of the last return already welcomed. The surge's own end
        /// timestamp when the server states one, so a three day surge is welcomed
        /// once rather than on each of its three days; the UTC day otherwise.
        /// </summary>
        public string? LastLapseKey { get; set; }
    }

    /// <summary>The chosen moment plus the numbers its copy is allowed to quote.</summary>
    public sealed class DescentBarkDecision
    {
        public DescentBarkMoment Moment { get; init; } = DescentBarkMoment.None;

        /// <summary>Today's fill as a percent of the daily cap.</summary>
        public double FillPct { get; init; }

        /// <summary>Percent of cap still to pour before the day banks. Zero once banked.</summary>
        public double RemainingPct { get; init; }

        public int TodayXp { get; init; }
        public int Cap { get; init; }
        public int Stage { get; init; }
        public int BankedDays { get; init; }

        /// <summary>Banked days still to go before the next rung, when the server states one.</summary>
        public int? DaysToNext { get; init; }

        public int DaysAway { get; init; }

        /// <summary>The accrued fill-rate bonus, 1.0 upward. Clamped by the server at 2.0.</summary>
        public double Multiplier { get; init; } = 1.0;

        /// <summary>The frozen payout the return surge is actually applying.</summary>
        public double SurgeMultiplier { get; init; } = 1.0;

        public static readonly DescentBarkDecision Nothing = new();
    }

    /// <summary>Edge detection for the four block-driven Descent barks. Pure, and pinned by tests.</summary>
    public static class DescentBarkPolicy
    {
        /// <summary>
        /// Where the "you're nearly there" nudge opens, as a percent of cap. Sits at
        /// three fifths of the way to the bank line so it lands while the user can
        /// still act on it, and closes the moment the day banks so the two lines can
        /// never both be true.
        /// </summary>
        public const double NearBankFloorPct = 12.0;

        /// <summary>
        /// The smallest payout worth welcoming. A surge that pays exactly 1.0x is a
        /// surge that pays nothing, which is what every stamp written before the
        /// server froze the multiplier reads as, and welcoming someone for a bonus
        /// they will never feel is worse than saying nothing at all.
        ///
        /// THIS REPLACED A DAYS-AWAY FLOOR OF 2, and that floor was unreachable in
        /// practice. The server stamps the surge inside the same bank that moves
        /// `devotion_last_day` forward to today (descent.js applyRelapseSurge, one
        /// line before applyDevotionDay), and `days_away` is measured off that same
        /// stamp, so a return reads 0 days away the instant it becomes a surge. A
        /// block carrying `surge_active` alongside a days_away of 2 can only be
        /// composed two further days later, by which point the welcome would land
        /// on somebody who came back and then went away again. The payout is the
        /// honest witness that a return happened, so the payout is what we read.
        /// </summary>
        public const double LapseMinSurgeMultiplier = 1.0;

        /// <summary>YYYY-MM-DD in UTC, the same shape the server stamps days with.</summary>
        public static string TodayUtc(DateTime utcNow) =>
            utcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// True when the server considers today already banked. The last-banked-day
        /// stamp is the authority; the vat's own fill is a fallback for the window
        /// between crossing the line and the stamp catching up.
        /// </summary>
        public static bool IsBankedToday(DescentBlock block, string todayUtc) =>
            string.Equals(block.DevotionLastDay, todayUtc, StringComparison.Ordinal)
            || (block.Vat is not null && block.Vat.FillPct >= DescentReader.BankThresholdPct);

        /// <summary>
        /// Decide what this observation is worth saying and MARK EVERY MOMENT IT
        /// COVERS AS SPENT, whether or not it won. Mutating the memory here rather
        /// than at the speak site is what makes "once per day" a property of the
        /// data instead of a property of whether the companion happened to be on
        /// screen; a bark the gate drops is a bark that is gone, which is the
        /// correct trade for garnish.
        /// </summary>
        public static DescentBarkDecision Decide(DescentBlock? block, DescentBarkMemory memory, string todayUtc)
        {
            if (block is null || memory is null || string.IsNullOrWhiteSpace(todayUtc))
                return DescentBarkDecision.Nothing;

            var vat = block.Vat;
            var stage = block.Stage;
            var relapse = block.Relapse;

            bool banked = IsBankedToday(block, todayUtc);

            // First sight of a block on this install: record where the user already
            // stands and say nothing at all about how they got there.
            if (!memory.Seeded)
            {
                memory.Seeded = true;
                memory.LastStage = stage?.N ?? 0;
                if (banked) memory.LastBankedDay = todayUtc;
                if (banked) memory.LastNearBankDay = todayUtc;
                memory.LastLapseKey = LapseKeyOf(relapse, todayUtc);
                return DescentBarkDecision.Nothing;
            }

            bool stageCrossed = stage is not null
                                && memory.LastStage is int last
                                && stage.N > last;

            string? lapseKey = LapseKeyOf(relapse, todayUtc);
            bool lapseReturn = relapse is not null
                               && relapse.SurgeActive
                               && relapse.SurgeMultiplier > LapseMinSurgeMultiplier
                               && lapseKey is not null
                               && !string.Equals(lapseKey, memory.LastLapseKey, StringComparison.Ordinal);

            bool dayBanked = banked
                             && !string.Equals(memory.LastBankedDay, todayUtc, StringComparison.Ordinal);

            bool nearBank = !banked
                            && vat is not null
                            && vat.FillPct >= NearBankFloorPct
                            && vat.FillPct < DescentReader.BankThresholdPct
                            && !string.Equals(memory.LastNearBankDay, todayUtc, StringComparison.Ordinal);

            // Spend everything this observation covered, then pick one to say.
            if (stage is not null) memory.LastStage = stage.N;
            if (lapseReturn) memory.LastLapseKey = lapseKey;
            if (dayBanked)
            {
                memory.LastBankedDay = todayUtc;
                // A day that banked can no longer be approaching the line, and the
                // nudge must not go off later the same day on a rounding wobble.
                memory.LastNearBankDay = todayUtc;
            }
            if (nearBank) memory.LastNearBankDay = todayUtc;

            var moment =
                stageCrossed ? DescentBarkMoment.StageCrossed :
                lapseReturn ? DescentBarkMoment.LapseReturn :
                dayBanked ? DescentBarkMoment.DayBanked :
                nearBank ? DescentBarkMoment.NearBank :
                DescentBarkMoment.None;

            if (moment == DescentBarkMoment.None) return DescentBarkDecision.Nothing;

            double fill = vat?.FillPct ?? 0;
            return new DescentBarkDecision
            {
                Moment = moment,
                FillPct = Math.Round(fill, 1),
                RemainingPct = Math.Max(0, Math.Round(DescentReader.BankThresholdPct - fill, 1)),
                TodayXp = vat?.TodayXp ?? 0,
                Cap = vat?.Cap ?? 0,
                Stage = stage?.N ?? 0,
                BankedDays = stage?.BankedDays ?? block.DevotionDays,
                DaysToNext = stage?.DaysToNext,
                DaysAway = relapse?.DaysAway ?? 0,
                Multiplier = relapse?.Multiplier ?? 1.0,
                SurgeMultiplier = relapse?.SurgeMultiplier ?? 1.0,
            };
        }

        /// <summary>
        /// Identity of a return, so the three day surge is welcomed once. The
        /// surge's stated end is stable across all three days; without one we fall
        /// back to the calendar day, which is still only one welcome per day.
        /// </summary>
        private static string? LapseKeyOf(DescentRelapse? relapse, string todayUtc)
        {
            if (relapse is null || !relapse.SurgeActive) return null;
            return string.IsNullOrWhiteSpace(relapse.SurgeEndsAt) ? todayUtc : relapse.SurgeEndsAt;
        }
    }
}
