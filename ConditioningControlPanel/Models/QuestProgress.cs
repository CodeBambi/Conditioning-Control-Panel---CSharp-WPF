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
    //
    // THREE DAILY SLOTS, ALL AT ONCE. Until 2026-08-27 a day handed out its three daily quests
    // one after another: <see cref="DailyQuest"/> held the single live one and finishing it rolled
    // the next. Now all three are dealt up front and live side by side in <see cref="DailyQuests"/>,
    // each one independently progressable and independently rerollable, and the day is over when
    // all three slots are stamped.
    //
    // <see cref="DailyQuest"/> survives as a LEGACY MIRROR, not as state: QuestService seeds the
    // slots from it once (a player mid-day through an update keeps the quest they were working on)
    // and thereafter keeps it pointing at the first unfinished slot purely so a downgrade to an
    // older build finds a quest where it expects one. Nothing in the app should read it - use
    // QuestService.GetDailySlots().
    public ActiveQuest? DailyQuest { get; set; }

    /// <summary>Today's three daily quests. Owned by QuestService (see EnsureDailySlots).</summary>
    public List<ActiveQuest> DailyQuests { get; set; } = new();

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

    // #889: a slot rolled while the Patreon/SubscribeStar entitlement was still unresolved drew
    // from the free-only pool and must be re-rolled once the answer lands. Persisted rather than
    // in-memory because quitting inside the ~90s settle window would otherwise lose the flag and
    // leave a patron wearing a free quest all day. QuestService owns the semantics.
    public bool DailyRolledUnresolved { get; set; }
    public bool WeeklyRolledUnresolved { get; set; }

    // BUG-BN8X9B9SZ5: the unified account this quest file belongs to. Active quest progress is
    // LOCAL-ONLY state (the server carries only aggregate quest stats/streaks), so it must
    // survive a logout + same-account re-login — wiping it at logout destroyed a day's 3/3
    // progress with nothing to restore it from. Logout now stamps the owner and keeps the file;
    // the wipe happens on the NEXT login, and only when the account proves to be a different
    // one (QuestService.EnsureOwnedBy). Null = never associated (pre-fix file or never logged
    // in): treated as owned by whoever logs in next, matching the old behavior at worst.
    public string? OwnerUnifiedId { get; set; }

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

        // Once the slots exist they ARE the count - a stamped card on screen and a counter that
        // disagrees with it is the one bug a three-up board can't hide. The stored field is kept
        // in step (and still written out) so an older build reading quests.json sees a sane
        // number, and it remains the answer on the one pass before migration has run.
        if (DailyQuests.Count > 0)
        {
            int done = 0;
            foreach (var q in DailyQuests) if (q?.IsCompleted == true) done++;
            DailyQuestsCompletedToday = done;
        }
        return DailyQuestsCompletedToday;
    }

    /// <summary>
    /// Check if all daily quests for today are completed (3/3)
    /// </summary>
    public bool AreAllDailyQuestsCompleted()
    {
        return GetDailyQuestsCompletedToday() >= MaxDailySlots;
    }

    /// <summary>How many daily quests a day deals. Mirrored by QuestService.MaxDailyQuestsPerDay,
    /// which is the constant the UI reads; this copy exists so the model can answer
    /// <see cref="AreAllDailyQuestsCompleted"/> without reaching into a service.</summary>
    public const int MaxDailySlots = 3;

    /// <summary>
    /// Check if daily quest has expired (new day)
    /// </summary>
    public bool IsDailyExpired()
    {
        if (!DailyQuestGeneratedAt.HasValue) return true;
        return DailyQuestGeneratedAt.Value.Date != DateTime.Today;
    }

    /// <summary>
    /// Check if weekly quest has expired (new week - resets Monday)
    /// </summary>
    public bool IsWeeklyExpired()
    {
        if (!WeeklyQuestGeneratedAt.HasValue) return true;
        var startOfWeek = GetStartOfWeek(DateTime.Today);
        return WeeklyQuestGeneratedAt.Value.Date < startOfWeek;
    }

    /// <summary>
    /// Get the start of the current week (Monday)
    /// </summary>
    private static DateTime GetStartOfWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
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
