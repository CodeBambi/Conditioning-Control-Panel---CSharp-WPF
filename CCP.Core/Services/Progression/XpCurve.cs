using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The XP curve maths, moved verbatim out of the WPF <c>ProgressionService</c> so the V2
    /// client and every head price levels the same way. Pure: no settings, no clock, no App.
    /// <c>ProgressionService</c> keeps its members and delegates here; the per-account
    /// <c>ActiveCurveEpoch</c> stays in the head because it reads the live settings.
    /// </summary>
    public static class XpCurve
    {
        /// <summary>The curve every account has always been on. See <see cref="XpForLevelV1"/>.</summary>
        public const int CurveEpochLegacy = 0;

        /// <summary>The Descent recurve, live only after the migration ceremony. See <see cref="XpForLevelV2"/>.</summary>
        public const int CurveEpochDescent = 1;

        /// <summary>
        /// Ceiling on any level a lifetime-XP figure may be derived to. Curve v2's cumulative
        /// cost past L300 is a quarter of a billion XP, so this can only ever be hit by garbage
        /// input — and a derive loop that trusts its input is a hang.
        /// </summary>
        public const int MaxDerivableLevel = 500;

        /// <summary>
        /// XP to clear <paramref name="level"/> on an explicitly named curve. Pure and static so
        /// the recurve can be tested, and so the ceremony can price BOTH curves in the same breath
        /// (it shows the user what their lifetime XP is worth under the new one).
        /// </summary>
        public static double GetXPForLevel(int level, int epoch) =>
            epoch >= CurveEpochDescent ? XpForLevelV2(level) : XpForLevelV1(level);

        /// <summary>
        /// CURVE v1 — the original. Untouched, and it must stay untouched: every un-migrated
        /// account's stored level was earned against these numbers.
        ///
        /// Progressive XP curve designed around session rewards:
        /// Easy: 400 XP, Medium: 800 XP, Hard: 1200 XP, Extreme: 2000 XP
        ///
        /// Target progression:
        /// Level 1-80: Easy (~800-2500 XP, 1-2 hard sessions per level)
        /// Level 80-100: Harder (~2500-4000 XP, 2-3 hard sessions per level)
        /// Level 100-125: Harder still (~4000-6000 XP, 3-5 hard sessions per level)
        /// Level 125-150: Even harder (~6000-10000 XP, 5-8 hard sessions per level)
        /// Level 150+: 3% compound growth per level
        /// </summary>
        public static double XpForLevelV1(int level)
        {
            if (level <= 80)
            {
                // Easy progression: linear growth from 800 to 2500
                // ~1-2 hard sessions per level
                return Math.Round(800 + (level - 1) * (1700.0 / 79));
            }
            else if (level <= 100)
            {
                // Harder: linear growth from 2500 to 4000
                // ~2-3 hard sessions per level
                double baseAt80 = 2500;
                return Math.Round(baseAt80 + (level - 80) * (1500.0 / 20));
            }
            else if (level <= 125)
            {
                // Harder still: linear growth from 4000 to 6000
                // ~3-5 hard sessions per level
                double baseAt100 = 4000;
                return Math.Round(baseAt100 + (level - 100) * (2000.0 / 25));
            }
            else if (level <= 150)
            {
                // Even harder: linear growth from 6000 to 10000
                // ~5-8 hard sessions per level
                double baseAt125 = 6000;
                return Math.Round(baseAt125 + (level - 125) * (4000.0 / 25));
            }
            else
            {
                // Level 150+: 3% compound growth per level
                // Starts at 10000, grows exponentially
                double baseAt150 = 10000;
                return Math.Round(baseAt150 * Math.Pow(1.03, level - 150));
            }
        }

        /// <summary>
        /// CURVE v2 — THE RECURVE (CONTRACTS-0812 §3, master design doc §13). Canonical source:
        /// <c>planning/descent-sim4.py</c> <c>cost_hybrid</c>. The literals below are that
        /// function's literals; they are not re-derived here and must not be "tidied".
        ///
        /// <list type="bullet">
        /// <item>L1-40 is BYTE-IDENTICAL to v1 — the honeymoon is protected on purpose, so a new
        /// or newish subject feels nothing change.</item>
        /// <item>L41-80 ramps to 4,200/level, L81-100 to 9,400, L101-125 to 20,400,
        /// L126-150 to 50,400, then x1.035 compounding.</item>
        /// </list>
        ///
        /// <para><b>The 1639 seam is deliberate.</b> v1's formula at L40 gives 1639.24; the sim
        /// restarts the next segment from the flat literal 1639, a 0.24 XP discontinuity nobody
        /// can perceive. The server implements the same literal. Matching the sim beats matching
        /// the algebra, because the two implementations only have to agree with EACH OTHER.</para>
        ///
        /// <para><b>Rounding is part of the contract.</b> <see cref="MidpointRounding.AwayFromZero"/>
        /// and not the framework default: every cost here is positive, so away-from-zero is
        /// exactly JavaScript's <c>Math.round</c>, which is what the server rounds with. The
        /// default (banker's, to-even) would disagree with the server on any cost landing on a
        /// half — and a one-XP disagreement is a one-level disagreement at a boundary.</para>
        /// </summary>
        public static double XpForLevelV2(int level)
        {
            if (level <= 40)
            {
                // UNCHANGED from v1. Same expression, deliberately duplicated rather than
                // delegated: v1 must remain editable-in-theory without silently moving v2.
                return Math.Round(800 + (level - 1) * (1700.0 / 79), MidpointRounding.AwayFromZero);
            }
            else if (level <= 80)
            {
                // 1639 -> 4200 across 40 levels
                return Math.Round(1639 + (level - 40) * ((4200.0 - 1639.0) / 40), MidpointRounding.AwayFromZero);
            }
            else if (level <= 100)
            {
                // 4200 -> 9400
                return Math.Round(4200 + (level - 80) * 260.0, MidpointRounding.AwayFromZero);
            }
            else if (level <= 125)
            {
                // 9400 -> 20400
                return Math.Round(9400 + (level - 100) * 440.0, MidpointRounding.AwayFromZero);
            }
            else if (level <= 150)
            {
                // 20400 -> 50400
                return Math.Round(20400 + (level - 125) * 1200.0, MidpointRounding.AwayFromZero);
            }
            else
            {
                // 3.5% compound growth per level from 50,400
                return Math.Round(50400 * Math.Pow(1.035, level - 150), MidpointRounding.AwayFromZero);
            }
        }

        /// <summary>
        /// Per-level quest XP scale factor. v1 is the runaway +4%/level the balance audit caught
        /// (74% of a casual's year came from quest claims, and "log in, claim, quit" out-earned
        /// playing); v2 corrects it to +1.2%/level. The 600 base is NOT here — it lives in each
        /// quest definition's own XPReward, which is where an author can tune it.
        /// </summary>
        public static double QuestLevelScale(int level, int epoch) =>
            1 + level * (epoch >= CurveEpochDescent ? 0.012 : 0.04);

        /// <summary>
        /// Cumulative XP a subject must have earned to STAND at <paramref name="level"/> — i.e.
        /// the sum of every level cost below it. Pure, uncached, iterative (the recursive cached
        /// sibling is for hot UI paths; this one is for the derive loop and for tests, where a
        /// 500-deep recursion would be a stack risk for no gain).
        /// </summary>
        public static double CumulativeXpToReachLevel(int level, int epoch)
        {
            if (level <= 1) return 0;
            if (level > MaxDerivableLevel) level = MaxDerivableLevel;

            double total = 0;
            for (int l = 1; l < level; l++) total += GetXPForLevel(l, epoch);
            return total;
        }

        /// <summary>
        /// THE RELEVEL. Turns a lifetime XP total back into "what level does that buy on this
        /// curve, and how far into it are you" — the exact arithmetic the migration ceremony's
        /// "Take it all back" performs, and the arithmetic the server re-performs independently
        /// before clamping the client's claim to within one level of its own answer
        /// (CONTRACTS-0812 §2.5).
        ///
        /// <para>Deterministic and side-effect free: no App statics, no settings, no clock. The
        /// same lifetime figure yields the same level every time it is asked, on any machine —
        /// which is what makes a ceremony that crashes mid-flight safe to re-run.</para>
        /// </summary>
        /// <param name="lifetimeXp">Total XP ever earned. Negative and NaN are treated as zero.</param>
        /// <param name="epoch">Curve to price it against.</param>
        public static (int Level, double XpIntoLevel) DeriveLevelFromLifetimeXp(double lifetimeXp, int epoch)
        {
            if (double.IsNaN(lifetimeXp) || lifetimeXp <= 0) return (1, 0);

            int level = 1;
            double remaining = lifetimeXp;

            while (level < MaxDerivableLevel)
            {
                double cost = GetXPForLevel(level, epoch);
                if (cost <= 0 || remaining < cost) break;   // cost <= 0 is impossible; a guard, not a case
                remaining -= cost;
                level++;
            }

            return (level, Math.Max(0, remaining));
        }

        /// <summary>XP needed to reach <paramref name="level"/> from level 1 on a named curve. Pure.</summary>
        public static double CumulativeXpBeforeLevel(int level, int epoch)
        {
            double sum = 0;
            for (int l = 1; l < level && l < MaxDerivableLevel; l++)
                sum += GetXPForLevel(l, epoch);
            return sum;
        }

        /// <summary>
        /// Moves a (level, xp into level) ledger from one curve to another by its CUMULATIVE total.
        /// Never keeps (level, remainder): re-pricing the same rungs on the other curve invents or
        /// loses XP (ccp-bugs #1270 / #1274, the phone's twin of this bug). Pure.
        /// </summary>
        public static (int Level, double XpIntoLevel) RepriceLedger(int level, double xpIntoLevel, int fromEpoch, int toEpoch)
        {
            if (double.IsNaN(xpIntoLevel) || xpIntoLevel < 0) xpIntoLevel = 0;
            var total = CumulativeXpBeforeLevel(Math.Max(1, level), fromEpoch) + xpIntoLevel;
            return DeriveLevelFromLifetimeXp(total, toEpoch);
        }
        /// <summary>
        /// Lifetime XP of a (level, xp into level) ledger on a named curve: every level cost below
        /// <paramref name="level"/> plus <paramref name="currentXP"/>. Uncapped on purpose - this
        /// is WPF's memoized <c>GetTotalXP</c> sum, same left-to-right order, same answer.
        /// </summary>
        public static double GetTotalXP(int level, double currentXP, int epoch) =>
            CumulativeXp(level - 1, epoch) + currentXP;

        /// <summary>Inverse of <see cref="GetTotalXP"/>: XP into <paramref name="level"/>, never negative.</summary>
        public static double GetCurrentLevelXP(int level, double totalXP, int epoch) =>
            Math.Max(0, totalXP - CumulativeXp(level - 1, epoch));

        private static double CumulativeXp(int level, int epoch)
        {
            double sum = 0;
            for (int l = 1; l <= level; l++) sum += GetXPForLevel(l, epoch);
            return sum;
        }
    }
}
