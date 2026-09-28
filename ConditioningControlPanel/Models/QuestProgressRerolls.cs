using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel;

/// <summary>
/// The reroll budget of <see cref="QuestProgress"/>. The data lives in CCP.Core; these read
/// App.SkillTree / App.Settings, so they stay in the head as extension methods and every call
/// site keeps its old syntax (same split as AchievementProgressStreak).
/// </summary>
public static class QuestProgressRerolls
{
    /// <summary>
    /// Get remaining daily rerolls (1 base + 2 for Patreon + skill tree bonuses)
    /// </summary>
    public static int GetRemainingDailyRerolls(this QuestProgress p, bool hasPatreon)
    {
        // Reset count if it's a new day
        if (p.DailyRerollResetDate?.Date != DateTime.Today)
        {
            p.DailyRerollsUsed = 0;
            p.DailyRerollResetDate = DateTime.Today;
        }

        int maxRerolls = hasPatreon ? 3 : 1;
        maxRerolls += App.SkillTree?.GetDailyFreeRerolls() ?? 0;
        maxRerolls += App.Settings?.Current?.BonusDailyRerolls ?? 0;
        return Math.Max(0, maxRerolls - p.DailyRerollsUsed);
    }

    /// <summary>
    /// Get remaining weekly rerolls (1 base + 2 for Patreon + skill tree bonuses)
    /// </summary>
    public static int GetRemainingWeeklyRerolls(this QuestProgress p, bool hasPatreon)
    {
        var startOfWeek = QuestProgress.GetStartOfWeek(DateTime.Today);

        // Reset count if it's a new week
        if (!p.WeeklyRerollResetDate.HasValue || p.WeeklyRerollResetDate.Value.Date < startOfWeek)
        {
            p.WeeklyRerollsUsed = 0;
            p.WeeklyRerollResetDate = DateTime.Today;
        }

        int maxRerolls = hasPatreon ? 3 : 1;
        // DELIBERATE, not the copy-paste from the daily branch above that it looks like. The skill
        // tree has no weekly reroll node - quest_refresh and reroll_addict are the only two that
        // feed GetDailyFreeRerolls - so the choice is between their bonus applying to both budgets
        // or only to the daily one, and the shipped copy says both: the weekly reroll tooltip
        // (tooltip_reroll_for_a_different_quest_once_per_week) reads "One reroll a week, three with
        // Patreon, plus anything the skill tree adds", in all nine languages. Dropping this line
        // would take rerolls off everyone who spent 15 and 20 skill points on those nodes and make
        // nine translated strings wrong, which is not a thing to do quietly in a patch. If the
        // weekly allowance is ever meant to be flat, the tooltip has to change with it.
        maxRerolls += App.SkillTree?.GetDailyFreeRerolls() ?? 0;
        maxRerolls += App.Settings?.Current?.BonusWeeklyRerolls ?? 0;
        return Math.Max(0, maxRerolls - p.WeeklyRerollsUsed);
    }

    /// <summary>
    /// Check if user can reroll their daily quest
    /// </summary>
    public static bool CanRerollDaily(this QuestProgress p, bool hasPatreon)
    {
        return p.GetRemainingDailyRerolls(hasPatreon) > 0;
    }

    /// <summary>
    /// Check if user can reroll their weekly quest
    /// </summary>
    public static bool CanRerollWeekly(this QuestProgress p, bool hasPatreon)
    {
        return p.GetRemainingWeeklyRerolls(hasPatreon) > 0;
    }
}
