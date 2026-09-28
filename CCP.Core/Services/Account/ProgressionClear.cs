using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The settings half of WPF <c>MainWindow.ClearProgressionData</c> (logout, account switch,
    /// account deletion): zeroes local progression, quest/usage streaks, season/OG and voids the XP
    /// watermark. Identity fields are untouched; the caller saves and resets quests/achievements.
    /// </summary>
    public static class ProgressionClear
    {
        public static void Apply(AppSettings s)
        {
            // Progression
            s.PlayerXP = 0;
            s.PlayerLevel = 1;
            s.SkillPoints = 0;
            s.UnlockedSkills = new List<string>();
            s.SeasonalStreakRecoveryUsed = false;
            s.StreakFixCharges = 0; // per-account, server-authoritative balance
            s.HighestLevelEver = 0;

            // Quest streak. The Perfect Week latch goes with it: leaving it standing means an
            // incoming account whose cloud streak happens to equal the old account's latch has
            // its milestone silently eaten (the latch is a "already paid for THIS streak" mark
            // and it belongs to the account being cleared).
            s.DailyQuestStreak = 0;
            s.LastDailyQuestDate = null;
            s.LastPerfectWeekStreakAwarded = 0;

            // Mobile quest ledger mirror — per-account, server-authoritative; the next
            // account's sync re-adopts its own figures.
            s.MobileQuestDailyCompleted = 0;
            s.MobileQuestWeeklyCompleted = 0;
            s.MobileQuestXP = 0;

            // Streak shields
            s.StreakShieldsRemaining = 0;
            s.LastStreakShieldResetDate = null;
            s.StreakShieldUsedDates = new List<DateTime>();

            // Usage streaks
            s.CurrentStreak = 0;
            s.LastStreakDate = null;
            s.HighestStreak = 0;

            // Usage stats
            s.NightTimeUsageCount = 0;
            s.EarlyMorningUsageCount = 0;

            // Rerolls
            s.FreeRerollsUsedToday = 0;
            s.LastRerollResetDate = null;

            // Season / OG
            s.IsSeason0Og = false;
            s.CurrentSeason = null;
            s.PatreonTier = 0;

            // This is the ONLY deliberate "yes, really zero me" surface in the app (logout,
            // account switch, account deletion), so it is the one place allowed to void the
            // server-confirmed XP watermark. Everything above just zeroed local progression;
            // leaving the watermark standing would make the sync service refuse to push the
            // new account's numbers as a regression of the old account's. (#865)
            ProfileAdopt.ClearXpWatermark(s, "explicit progression clear (logout / account switch)");
        }
    }
}
