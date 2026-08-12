using System;
using System.Collections.Generic;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles XP, leveling, and unlockables.
    /// Will be expanded in future sessions.
    /// </summary>
    public class ProgressionService
    {
        public event EventHandler<int>? LevelUp;
        public event EventHandler<double>? XPChanged;

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

        // Memoization cache for cumulative XP calculations, one per curve epoch. Two caches and
        // not one keyed pair because the curve NEVER changes mid-run for a given epoch, but it
        // DOES change under a user's feet at the migration ceremony — a single cache would hand
        // out v1 sums to a v2 client for the rest of the session.
        private readonly Dictionary<int, double> _cumulativeXPCacheV1 = new();
        private readonly Dictionary<int, double> _cumulativeXPCacheV2 = new();

        /// <summary>
        /// Awards XP to both the user (for feature unlocks) and the active companion (for companion leveling).
        /// </summary>
        /// <param name="amount">Base XP amount to award.</param>
        /// <param name="source">What action generated this XP (for companion bonuses).</param>
        /// <param name="context">Context about how the XP was earned (for companion modifiers).</param>
        public void AddXP(double amount, XPSource source = XPSource.Other, XPContext? context = null)
        {
            // Allow progression in offline mode with local-only storage
            var isOfflineMode = App.Settings?.Current?.OfflineMode == true;
            var hasOfflineUsername = !string.IsNullOrWhiteSpace(App.Settings?.Current?.OfflineUsername);

            // Require either: logged in (for cloud sync) OR offline mode with username (local only)
            if (!App.IsLoggedIn && !(isOfflineMode && hasOfflineUsername))
            {
                App.Logger?.Debug("XP not awarded - user not logged in and not in offline mode");
                return;
            }

            // Anti-cheat: Suppress passive XP sources when user is idle (AFK farming prevention)
            if (App.ActivityTracker?.IsIdle == true)
            {
                // Only suppress passive (non-interaction) sources
                if (source == XPSource.Flash || source == XPSource.Subliminal || source == XPSource.BouncingText)
                {
                    App.Logger?.Debug("XP suppressed (idle): +{Amount} from {Source}", amount, source);
                    return;
                }
            }

            // Track whether we're in offline mode for skipping cloud features
            var inOfflineMode = isOfflineMode && hasOfflineUsername;

            var settings = App.Settings.Current;

            // Apply skill tree XP multiplier (sparkle boost, time bonuses, pink rush, etc.)
            // times the Cycle bonus — the lasting reward for having chosen "Descend again" at
            // the migration ceremony. DescentCycleXpBonus is 1.0 for everybody who has not, so
            // this is arithmetically invisible until a Cycle mark exists.
            var skillMultiplier = App.SkillTree?.GetTotalXpMultiplier() ?? 1.0;
            var cycleMultiplier = Descent.DescentMigration.ActiveCycleXpBonus;
            var adjustedAmount = amount * skillMultiplier * cycleMultiplier;

            var previousXP = settings.PlayerXP;
            settings.PlayerXP += adjustedAmount;

            // Track total XP earned (for stats)
            App.Achievements?.TrackXPEarned(adjustedAmount);

            if (Math.Abs(skillMultiplier - 1.0) > 0.001)
            {
                App.Logger?.Information("XP awarded: +{Amount} (base {Base} x {Mult:P0} skill bonus) - was {Prev}, now {Now}",
                    adjustedAmount, amount, skillMultiplier, previousXP, settings.PlayerXP);
            }
            else
            {
                App.Logger?.Information("XP awarded: +{Amount} (was {Prev}, now {Now})", adjustedAmount, previousXP, settings.PlayerXP);
            }

            // Route XP to the active companion (v5.3 companion leveling system)
            App.Companion?.AddCompanionXP(amount, source, context);

            // Track for quests (only for "earn X XP" type quests)
            App.Quests?.TrackXPEarned((int)amount);

            // Check for level up
            SpendXPOnLevels(settings, inOfflineMode);

            XPChanged?.Invoke(this, settings.PlayerXP);
        }

        /// <summary>
        /// Awards XP that the server already decided the exact size of - currently only the web XP
        /// claim settled through /v2/user/sync (see ProfileSyncService's claim handshake).
        ///
        /// Deliberately NOT AddXP: the amount is the server's, so it takes no skill-tree multiplier,
        /// no idle suppression, no companion routing and no quest credit - doubling it here would let
        /// a web action out-earn the same action in the app. What it DOES share is the level-up loop,
        /// so a claim that pushes the player over the line gets the full level-up experience
        /// (celebration, haptics, level achievements, skill points, leaderboard sync).
        /// </summary>
        /// <param name="amount">Exact XP to add, as minted by the server.</param>
        public void AddClaimedXP(double amount)
        {
            if (amount <= 0) return;

            // Same gate as AddXP: logged in, or offline mode with a local username
            var isOfflineMode = App.Settings?.Current?.OfflineMode == true;
            var hasOfflineUsername = !string.IsNullOrWhiteSpace(App.Settings?.Current?.OfflineUsername);

            if (!App.IsLoggedIn && !(isOfflineMode && hasOfflineUsername))
            {
                App.Logger?.Debug("Web XP claim not applied - user not logged in and not in offline mode");
                return;
            }

            var inOfflineMode = isOfflineMode && hasOfflineUsername;
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var previousXP = settings.PlayerXP;
            settings.PlayerXP += amount;

            // Track total XP earned (for stats)
            App.Achievements?.TrackXPEarned(amount);

            App.Logger?.Information("Web XP claim applied: +{Amount} (was {Prev}, now {Now})", amount, previousXP, settings.PlayerXP);

            SpendXPOnLevels(settings, inOfflineMode);

            XPChanged?.Invoke(this, settings.PlayerXP);
        }

        /// <summary>
        /// Drains banked XP into levels for as long as the player can afford the next one, running
        /// the full level-up experience for each level gained. Shared by AddXP and AddClaimedXP so
        /// the two ways XP can arrive can never drift apart.
        /// </summary>
        private void SpendXPOnLevels(Models.AppSettings settings, bool inOfflineMode)
        {
            var xpNeeded = GetXPForLevel(settings.PlayerLevel);
            while (settings.PlayerXP >= xpNeeded)
            {
                settings.PlayerXP -= xpNeeded;
                settings.PlayerLevel++;

                // Track highest level ever for permanent unlocks across seasons
                if (settings.PlayerLevel > settings.HighestLevelEver)
                {
                    settings.HighestLevelEver = settings.PlayerLevel;
                }

                xpNeeded = GetXPForLevel(settings.PlayerLevel);
                LevelUp?.Invoke(this, settings.PlayerLevel);
                _ = App.Haptics?.LevelUpPatternAsync();
                App.Logger.Information("Level up! Now level {Level}", settings.PlayerLevel);

                // Check level-based achievements immediately on level up
                App.Achievements?.CheckLevelAchievements(settings.PlayerLevel);

                // Award skill points for the enhancement tree (SkillTreeService.PointsPerLevel)
                App.SkillTree?.OnLevelUp(settings.PlayerLevel);

                // Skip cloud/network features in offline mode
                if (!inOfflineMode)
                {
                    // Update Discord Rich Presence level
                    App.DiscordRpc?.UpdateLevel(settings.PlayerLevel);

                    // Send Discord webhook for level milestones (10, 25, 50, 100, etc.)
                    // Always use CustomDisplayName for privacy - never expose real Discord/Patreon names
                    if (settings.DiscordShareLevelUps && IsLevelMilestone(settings.PlayerLevel))
                    {
                        var displayName = App.Discord?.CustomDisplayName ?? App.Patreon?.DisplayName ?? "Someone";
                        _ = App.Discord?.SendLevelUpWebhookAsync(settings.PlayerLevel, displayName);
                    }

                    // Sync profile to cloud on level up so leaderboard updates
                    _ = App.ProfileSync?.SyncProfileAsync();
                }
                else
                {
                    App.Logger?.Debug("Offline mode: Skipped cloud sync for level up to {Level}", settings.PlayerLevel);
                }
            }
        }

        /// <summary>
        /// Which XP curve THIS install is on right now. <see cref="CurveEpochLegacy"/> for every
        /// account that has not been through the Descent migration ceremony — which today is all
        /// of them, because nothing sets <c>DescentEpoch</c> until the ceremony's submit runs.
        ///
        /// <para>This is the per-USER epoch (AppSettings), deliberately NOT the per-BUILD wire
        /// constant <see cref="Descent.DescentEpochs.ClientEpoch"/>. The build says "I am a client
        /// that understands epoch 1"; this says "this account has crossed over". Conflating them
        /// would recurve everybody the moment they installed the new version.</para>
        /// </summary>
        public static int ActiveCurveEpoch =>
            App.Settings?.Current?.DescentEpoch == CurveEpochDescent ? CurveEpochDescent : CurveEpochLegacy;

        /// <summary>XP to clear <paramref name="level"/> on the curve this account is currently on.</summary>
        public double GetXPForLevel(int level) => GetXPForLevel(level, ActiveCurveEpoch);

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

        /// <summary>Quest scale on the curve this account is currently on.</summary>
        public static double QuestLevelScale(int level) => QuestLevelScale(level, ActiveCurveEpoch);

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

        /// <summary>
        /// Gets the XP multiplier for session rewards based on player level.
        /// Higher level players earn more XP from sessions to compensate for increased requirements.
        /// </summary>
        public double GetSessionXPMultiplier(int level)
        {
            if (level < 30) return 1.0;
            if (level < 80) return 1.0 + ((level - 30) * 0.01);   // 1.0x → 1.5x
            if (level < 125) return 1.5 + ((level - 80) * 0.02);  // 1.5x → 2.4x
            if (level < 150) return 2.4 + ((level - 125) * 0.03); // 2.4x → 3.15x
            return Math.Min(5.0, 3.15 + ((level - 150) * 0.03));   // 3.15x → 5.0x cap
        }

        /// <summary>
        /// Calculates the total accumulated XP across all levels.
        /// This is the sum of XP required for all previous levels plus current level progress.
        /// Uses memoization to avoid recalculating cumulative XP for previously seen levels.
        /// </summary>
        public double GetTotalXP(int level, double currentXP)
        {
            return GetCumulativeXPForLevel(level - 1) + currentXP;
        }

        /// <summary>
        /// Converts total accumulated XP back to current level progress.
        /// This is the inverse of GetTotalXP - used when loading from cloud sync.
        /// </summary>
        public double GetCurrentLevelXP(int level, double totalXP)
        {
            var cumulativeForPreviousLevels = GetCumulativeXPForLevel(level - 1);
            return Math.Max(0, totalXP - cumulativeForPreviousLevels);
        }

        /// <summary>
        /// Gets the cumulative XP required to reach a given level (sum of all previous levels).
        /// Results are memoized for performance.
        /// </summary>
        private double GetCumulativeXPForLevel(int level)
        {
            if (level <= 0) return 0;

            // The cache is per epoch — a migrated account asking for a sum it already asked for
            // under the old curve must not be handed the old answer.
            var cache = ActiveCurveEpoch == CurveEpochDescent ? _cumulativeXPCacheV2 : _cumulativeXPCacheV1;

            // Check cache first
            if (cache.TryGetValue(level, out double cached))
            {
                return cached;
            }

            // Calculate: cumulative XP for previous level + XP for current level
            double cumulative = GetCumulativeXPForLevel(level - 1) + GetXPForLevel(level);
            cache[level] = cumulative;
            return cumulative;
        }

        public string GetTitle(int level)
        {
            return level switch
            {
                < 5 => Loc.GetF("rank_beginner", App.Mods?.GetRankSubject() ?? "Subject"),
                < 10 => Loc.GetF("rank_training", App.Mods?.GetRankSubject() ?? "Subject"),
                < 20 => Loc.GetF("rank_eager", App.Mods?.GetRankSubject() ?? "Subject"),
                < 30 => Loc.GetF("rank_devoted", App.Mods?.GetRankSubject() ?? "Subject"),
                < 50 => Loc.GetF("rank_advanced", App.Mods?.GetRankSubject() ?? "Subject"),
                _ => Loc.GetF("rank_perfect", App.Mods?.GetRankSubject() ?? "Subject")
            };
        }

        public bool IsUnlocked(string feature, int currentLevel)
        {
            return feature switch
            {
                "spiral" => currentLevel >= 10,
                "pink_filter" => currentLevel >= 10,
                "bubbles" => currentLevel >= 20,
                _ => true
            };
        }

        /// <summary>
        /// Check if a level is a milestone worth announcing (10, 25, 50, 100, 125, 150)
        /// </summary>
        private static bool IsLevelMilestone(int level)
        {
            return level == 10 || level == 25 || level == 50 ||
                   level == 100 || level == 125 || level == 150 ||
                   (level > 150 && level % 25 == 0); // Every 25 levels after 150
        }
    }
}
