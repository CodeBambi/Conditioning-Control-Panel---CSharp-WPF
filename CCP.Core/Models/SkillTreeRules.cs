using System.Linq;

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
    }
}
