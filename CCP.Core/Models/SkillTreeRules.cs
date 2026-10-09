using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Events;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Owned-skill queries over <see cref="AppSettings"/>, moved from the WPF head's
    /// SkillTreeService so every head answers them the same way. Purchasing (server HTTP),
    /// Pink Rush timers and persistence stay head-side.
    /// </summary>
    public static class SkillTreeRules
    {
        public static bool HasSkill(AppSettings settings, string skillId) =>
            settings.UnlockedSkills.Contains(skillId);

        /// <summary>Whether a secret skill's unlock requirement has been met.</summary>
        public static bool IsSecretSkillAvailable(AppSettings settings, string skillId) => skillId switch
        {
            "night_shift" => settings.NightTimeUsageCount >= 10,
            "early_bird_bimbo" => settings.EarlyMorningUsageCount >= 10,
            "eternal_doll" => settings.HighestLevelEver >= 50,
            _ => false
        };

        /// <summary>Not owned, affordable, prerequisite owned, and (for secrets) requirement met.</summary>
        public static bool CanPurchaseSkill(AppSettings settings, string skillId)
        {
            var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
            if (skill == null || HasSkill(settings, skillId) || settings.SkillPoints < skill.Cost) return false;
            if (!string.IsNullOrEmpty(skill.PrerequisiteId) && !HasSkill(settings, skill.PrerequisiteId)) return false;
            return !skill.IsSecret || IsSecretSkillAvailable(settings, skillId);
        }

        /// <summary>Highest sparkle boost tier unlocked (0 = none, 1-3).</summary>
        public static int GetSparkleBoostTier(AppSettings settings) =>
            HasSkill(settings, "sparkle_boost_3") ? 3
            : HasSkill(settings, "sparkle_boost_2") ? 2
            : HasSkill(settings, "sparkle_boost_1") ? 1 : 0;

        /// <summary>Free quest rerolls per day: quest_refresh +1, reroll_addict +2.</summary>
        public static int GetDailyFreeRerolls(AppSettings settings) =>
            (HasSkill(settings, "quest_refresh") ? 1 : 0) + (HasSkill(settings, "reroll_addict") ? 2 : 0);

        /// <summary>XP multiplier for rerolled quests: better_quests gives +25%.</summary>
        public static double GetRerollBonusMultiplier(AppSettings settings) =>
            HasSkill(settings, "better_quests") ? 1.25 : 1.0;

        /// <summary>Streak Power: 0.5% per streak day, max 15%.</summary>
        private static double StreakBonus(AppSettings settings) => Math.Min(settings.CurrentStreak * 0.005, 0.15);

        private static bool NightShift(AppSettings s, int hour) => HasSkill(s, "night_shift") && (hour >= 23 || hour < 5);
        private static bool EarlyBird(AppSettings s, int hour) => HasSkill(s, "early_bird_bimbo") && hour >= 5 && hour < 8;

        /// <summary>
        /// Total XP multiplier from all active skills (moved from WPF SkillTreeService). Sparkle
        /// boosts, streak power, the time-of-day secrets and the world-event boost add; Pink Rush
        /// multiplies. <paramref name="hour"/> is the local hour, <paramref name="eventBoost"/> the
        /// raw live-event XP boost (clamped here).
        /// </summary>
        public static double GetTotalXpMultiplier(AppSettings settings, int hour, double eventBoost)
        {
            double multiplier = 1.0;
            if (HasSkill(settings, "sparkle_boost_1")) multiplier += 0.10;
            if (HasSkill(settings, "sparkle_boost_2")) multiplier += 0.15;
            if (HasSkill(settings, "sparkle_boost_3")) multiplier += 0.20;
            if (HasSkill(settings, "streak_power")) multiplier += StreakBonus(settings);
            if (NightShift(settings, hour)) multiplier += 0.50;
            if (EarlyBird(settings, hour)) multiplier += 0.50;
            multiplier += LiveEventService.ClampXpBoost(eventBoost);
            return multiplier * PinkRushRules.XpFactor(settings);
        }

        /// <summary>The breakdown the Enhancements tab shows, kept in step with
        /// <see cref="GetTotalXpMultiplier"/>. The first entry is always the base 1.0.</summary>
        public static List<(string Source, double Value)> GetMultiplierBreakdown(AppSettings settings, int hour, double eventBoost)
        {
            var breakdown = new List<(string Source, double Value)> { (Loc.Get("skill_mult_base"), 1.0) };
            if (HasSkill(settings, "sparkle_boost_1")) breakdown.Add((Loc.Get("skill_mult_sparkle_boost"), 0.10));
            if (HasSkill(settings, "sparkle_boost_2")) breakdown.Add((Loc.Get("skill_mult_extra_sparkly"), 0.15));
            if (HasSkill(settings, "sparkle_boost_3")) breakdown.Add((Loc.Get("skill_mult_maximum_sparkle"), 0.20));
            if (HasSkill(settings, "streak_power") && settings.CurrentStreak > 0)
                breakdown.Add((Loc.GetF("skill_mult_streak_power_days", settings.CurrentStreak), StreakBonus(settings)));
            if (NightShift(settings, hour)) breakdown.Add((Loc.Get("skill_mult_night_shift"), 0.50));
            if (EarlyBird(settings, hour)) breakdown.Add((Loc.Get("skill_mult_early_bird"), 0.50));
            // Omitted when no event runs: an "Event +0%" row would advertise a dormant feature.
            var boost = LiveEventService.ClampXpBoost(eventBoost);
            if (boost > 0.0) breakdown.Add((Loc.Get("skill_mult_event_boost"), boost));
            if (settings.PinkRushActive && HasSkill(settings, "pink_rush"))
                breakdown.Add((Loc.Get("skill_mult_pink_rush_active"), 2.0)); // +200% (3x total)
            return breakdown;
        }

        /// <summary>Total conditioning time as "Xh Ym", or "Xd Yh Zm" from a day up.</summary>
        public static string FormatConditioningTime(AppSettings settings)
        {
            var totalMinutes = settings.TotalConditioningMinutes;
            var hours = (int)(totalMinutes / 60);
            var minutes = (int)(totalMinutes % 60);
            return hours >= 24
                ? Loc.GetF("skill_time_dhm", hours / 24, hours % 24, minutes)
                : Loc.GetF("skill_time_hm", hours, minutes);
        }
    }
}
