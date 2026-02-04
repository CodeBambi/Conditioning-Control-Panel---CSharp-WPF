using System;
using System.Collections.Generic;

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

        // Memoization cache for cumulative XP calculations
        private readonly Dictionary<int, double> _cumulativeXPCache = new();

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

            // Track whether we're in offline mode for skipping cloud features
            var inOfflineMode = isOfflineMode && hasOfflineUsername;

            var settings = App.Settings.Current;
            var previousXP = settings.PlayerXP;
            settings.PlayerXP += amount;
            App.Logger?.Information("XP awarded: +{Amount} (was {Prev}, now {Now})", amount, previousXP, settings.PlayerXP);

            // Route XP to the active companion (v5.3 companion leveling system)
            App.Companion?.AddCompanionXP(amount, source, context);

            // Track for quests (only for "earn X XP" type quests)
            App.Quests?.TrackXPEarned((int)amount);
            
            // Check for level up
            var xpNeeded = GetXPForLevel(settings.PlayerLevel);
            while (settings.PlayerXP >= xpNeeded)
            {
                settings.PlayerXP -= xpNeeded;
                settings.PlayerLevel++;
                xpNeeded = GetXPForLevel(settings.PlayerLevel);
                LevelUp?.Invoke(this, settings.PlayerLevel);
                _ = App.Haptics?.LevelUpPatternAsync();
                _ = App.Haptics?.LevelUpPatternAsync();
                App.Logger.Information("Level up! Now level {Level}", settings.PlayerLevel);

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
            
            XPChanged?.Invoke(this, settings.PlayerXP);
        }

        public double GetXPForLevel(int level)
        {
            // Progressive XP curve designed around session rewards:
            // Easy: 400 XP, Medium: 800 XP, Hard: 1200 XP, Extreme: 2000 XP
            //
            // Target progression:
            // Level 1-80: Easy (~800-2500 XP, 1-2 hard sessions per level)
            // Level 80-100: Harder (~2500-4000 XP, 2-3 hard sessions per level)
            // Level 100-125: Harder still (~4000-6000 XP, 3-5 hard sessions per level)
            // Level 125-150: Even harder (~6000-10000 XP, 5-8 hard sessions per level)
            // Level 150+: 3% compound growth per level

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
        /// Gets the XP multiplier for session rewards based on player level.
        /// Higher level players earn more XP from sessions to compensate for increased requirements.
        /// </summary>
        public double GetSessionXPMultiplier(int level)
        {
            if (level < 100) return 1.0;
            if (level < 125) return 1.0 + ((level - 100) * 0.02); // 1.0x to 1.5x
            if (level < 150) return 1.5 + ((level - 125) * 0.02); // 1.5x to 2.0x
            return 2.0 + ((level - 150) * 0.02); // 2.0x+ for 150+
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

            // Check cache first
            if (_cumulativeXPCache.TryGetValue(level, out double cached))
            {
                return cached;
            }

            // Calculate: cumulative XP for previous level + XP for current level
            double cumulative = GetCumulativeXPForLevel(level - 1) + GetXPForLevel(level);
            _cumulativeXPCache[level] = cumulative;
            return cumulative;
        }

        public string GetTitle(int level)
        {
            return level switch
            {
                < 5 => "Beginner Bimbo",
                < 10 => "Training Bimbo",
                < 20 => "Eager Bimbo",
                < 30 => "Devoted Bimbo",
                < 50 => "Advanced Bimbo",
                _ => "Perfect Bimbo"
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
