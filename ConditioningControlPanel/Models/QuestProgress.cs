using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Models;

/// <summary>
/// Tracks the current progress of active quests and reroll state.
/// Persisted to quests.json
/// </summary>
public class QuestProgress
{
    // Active quests
    public ActiveQuest? DailyQuest { get; set; }
    public ActiveQuest? WeeklyQuest { get; set; }

    // Reroll tracking - counts how many rerolls used in current period
    public int DailyRerollsUsed { get; set; }
    public int WeeklyRerollsUsed { get; set; }
    public DateTime? DailyRerollResetDate { get; set; }
    public DateTime? WeeklyRerollResetDate { get; set; }

    // Quest generation timestamps
    public DateTime? DailyQuestGeneratedAt { get; set; }
    public DateTime? WeeklyQuestGeneratedAt { get; set; }

    // Daily quest refresh tracking (up to 3 daily quests per day)
    public int DailyQuestsCompletedToday { get; set; }
    public DateTime? DailyCompletionResetDate { get; set; }

    // Statistics
    public int TotalDailyQuestsCompleted { get; set; }
    public int TotalWeeklyQuestsCompleted { get; set; }
    public int TotalXPFromQuests { get; set; }

    // Streak calendar - dates when daily quests were completed (last 30 days)
    public List<DateTime> DailyQuestCompletionDates { get; set; } = new();

    /// <summary>
    /// Get remaining daily rerolls (1 base + 2 for Patreon + skill tree bonuses)
    /// </summary>
    public int GetRemainingDailyRerolls(bool hasPatreon)
    {
        // Reset count if it's a new day
        if (DailyRerollResetDate?.Date != DateTime.Today)
        {
            DailyRerollsUsed = 0;
            DailyRerollResetDate = DateTime.Today;
        }

        int maxRerolls = hasPatreon ? 3 : 1;
        maxRerolls += App.SkillTree?.GetDailyFreeRerolls() ?? 0;
        maxRerolls += App.Settings?.Current?.BonusDailyRerolls ?? 0;
        return Math.Max(0, maxRerolls - DailyRerollsUsed);
    }

    /// <summary>
    /// Get remaining weekly rerolls (1 base + 2 for Patreon + skill tree bonuses)
    /// </summary>
    public int GetRemainingWeeklyRerolls(bool hasPatreon)
    {
        var startOfWeek = GetStartOfWeek(DateTime.Today);

        // Reset count if it's a new week
        if (!WeeklyRerollResetDate.HasValue || WeeklyRerollResetDate.Value.Date < startOfWeek)
        {
            WeeklyRerollsUsed = 0;
            WeeklyRerollResetDate = DateTime.Today;
        }

        int maxRerolls = hasPatreon ? 3 : 1;
        maxRerolls += App.SkillTree?.GetDailyFreeRerolls() ?? 0;
        maxRerolls += App.Settings?.Current?.BonusWeeklyRerolls ?? 0;
        return Math.Max(0, maxRerolls - WeeklyRerollsUsed);
    }

    /// <summary>
    /// Check if user can reroll their daily quest
    /// </summary>
    public bool CanRerollDaily(bool hasPatreon)
    {
        return GetRemainingDailyRerolls(hasPatreon) > 0;
    }

    /// <summary>
    /// Check if user can reroll their weekly quest
    /// </summary>
    public bool CanRerollWeekly(bool hasPatreon)
    {
        return GetRemainingWeeklyRerolls(hasPatreon) > 0;
    }

    /// <summary>
    /// Get how many daily quests have been completed today (resets on new day)
    /// </summary>
    public int GetDailyQuestsCompletedToday()
    {
        if (DailyCompletionResetDate?.Date != DateTime.Today)
        {
            DailyQuestsCompletedToday = 0;
            DailyCompletionResetDate = DateTime.Today;
        }
        return DailyQuestsCompletedToday;
    }

    /// <summary>
    /// Check if all daily quests for today are completed (3/3)
    /// </summary>
    public bool AreAllDailyQuestsCompleted()
    {
        return GetDailyQuestsCompletedToday() >= 3;
    }

    /// <summary>
    /// Check if daily quest has expired (new day)
    /// </summary>
    public bool IsDailyExpired()
    {
        if (!DailyQuestGeneratedAt.HasValue) return true;
        return DailyQuestGeneratedAt.Value.Date != DateTime.Today;
    }

    /// <summary>
    /// Check if weekly quest has expired (new week - resets Sunday)
    /// </summary>
    public bool IsWeeklyExpired()
    {
        if (!WeeklyQuestGeneratedAt.HasValue) return true;
        var startOfWeek = GetStartOfWeek(DateTime.Today);
        return WeeklyQuestGeneratedAt.Value.Date < startOfWeek;
    }

    /// <summary>
    /// Get the start of the current week (Sunday)
    /// </summary>
    private static DateTime GetStartOfWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Sunday)) % 7;
        return date.AddDays(-diff).Date;
    }
}

/// <summary>
/// Represents an active quest with progress tracking
/// </summary>
public class ActiveQuest
{
    public string DefinitionId { get; set; } = "";
    public int CurrentProgress { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ActiveQuest() { }

    public ActiveQuest(string definitionId)
    {
        DefinitionId = definitionId;
        CurrentProgress = 0;
        IsCompleted = false;
        CompletedAt = null;
    }
}
