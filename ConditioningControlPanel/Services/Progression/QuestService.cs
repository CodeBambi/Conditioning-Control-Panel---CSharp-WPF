using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Event args for quest completion
/// </summary>
public class QuestCompletedEventArgs : EventArgs
{
    public QuestDefinition QuestDefinition { get; }
    public int XPAwarded { get; }
    public QuestType QuestType { get; }

    public QuestCompletedEventArgs(QuestDefinition def, int xp, QuestType type)
    {
        QuestDefinition = def;
        XPAwarded = xp;
        QuestType = type;
    }
}

/// <summary>
/// Event args for quest progress updates
/// </summary>
public class QuestProgressEventArgs : EventArgs
{
    public QuestType QuestType { get; }
    public int CurrentProgress { get; }
    public int TargetValue { get; }

    public QuestProgressEventArgs(QuestType type, int current, int target)
    {
        QuestType = type;
        CurrentProgress = current;
        TargetValue = target;
    }
}

/// <summary>
/// Service for managing daily and weekly quests
/// </summary>
public class QuestService : IDisposable
{
    private readonly string _progressPath;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Random _random = new();
    private bool _isDirty;

    // #889: Patreon validation is asynchronous, so at launch the entitlement gate can still read
    // "no access" for a paying patron — and the premium-loss rerolls below then destroyed their
    // premium quest on every single launch. A deferred decision costs at most one refresh tick.
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private static readonly TimeSpan EntitlementSettleWindow = TimeSpan.FromSeconds(90);
    private bool _premiumRecheckPending;
    // The other half of the same problem: a slot ROLLED while the entitlement was unresolved
    // drew from the free-only pool (IsQuestAvailableForTier reads the same "no access"), and a
    // patron then wore a free quest for the whole day. Remembered per slot so the recheck can
    // re-roll it — and only it — once the answer lands.
    private bool _dailyRolledUnresolved;
    private bool _weeklyRolledUnresolved;

    // Accumulators for fractional minutes (time-based quests are called with small increments)
    private double _spiralMinutesAccumulator;
    private double _pinkFilterMinutesAccumulator;
    private double _brainDrainMinutesAccumulator;
    private double _videoMinutesAccumulator;
    private double _combinedMinutesAccumulator;
    private double _autonomyMinutesAccumulator;

    public QuestProgress Progress { get; private set; }

    public event EventHandler<QuestCompletedEventArgs>? QuestCompleted;
    public event EventHandler<QuestProgressEventArgs>? QuestProgressChanged;
    public event EventHandler? QuestsRefreshed;

    public QuestService()
    {
        _progressPath = Path.Combine(
            App.UserDataPath,
            "quests.json");

        Progress = LoadProgress();

        // Check for expired quests and generate new ones
        CheckAndGenerateQuests();

        // Auto-save every 30 seconds if dirty (off UI thread)
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _saveTimer.Tick += (s, e) =>
        {
            if (_isDirty)
            {
                _isDirty = false;
                var json = JsonSerializer.Serialize(Progress, new JsonSerializerOptions { WriteIndented = true });
                var path = _progressPath;
                var tmpPath = path + ".tmp";
                _ = Task.Run(() =>
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        // Atomic write: write to .tmp first, then rename
                        File.WriteAllText(tmpPath, json);
                        File.Move(tmpPath, path, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to save quest progress");
                    }
                });
            }
        };
        _saveTimer.Start();

        // Quest refresh timer — detect day/week rollover while app is running
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _refreshTimer.Tick += (s, e) =>
        {
            var dailyExpired = Progress.IsDailyExpired();
            var weeklyExpired = Progress.IsWeeklyExpired();
            // A premium-loss decision deferred at launch (#889) is retried here, once the
            // entitlement answer has landed.
            var premiumRecheck = _premiumRecheckPending && IsEntitlementResolved();
            if (dailyExpired || weeklyExpired || premiumRecheck)
            {
                if (premiumRecheck)
                {
                    _premiumRecheckPending = false;
                    App.Logger?.Information("Quest premium recheck: entitlement resolved, reconciling deferred quests");
                }
                else
                {
                    App.Logger?.Information("Quest rollover detected (daily={Daily}, weekly={Weekly})", dailyExpired, weeklyExpired);
                }
                CheckAndGenerateQuests();
                QuestsRefreshed?.Invoke(this, EventArgs.Empty);
            }
        };
        _refreshTimer.Start();

        App.Logger?.Information("QuestService initialized. Daily: {Daily}, Weekly: {Weekly}",
            Progress.DailyQuest?.DefinitionId ?? "none",
            Progress.WeeklyQuest?.DefinitionId ?? "none");
    }

    #region Persistence

    private QuestProgress LoadProgress()
    {
        var tmpPath = _progressPath + ".tmp";

        // Try loading from main file first
        if (File.Exists(_progressPath))
        {
            try
            {
                var json = File.ReadAllText(_progressPath);
                return JsonSerializer.Deserialize<QuestProgress>(json) ?? new QuestProgress();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Quest progress file corrupted, attempting recovery from .tmp");
            }
        }

        // Main file missing or corrupt — try recovering from .tmp
        if (File.Exists(tmpPath))
        {
            try
            {
                var json = File.ReadAllText(tmpPath);
                var progress = JsonSerializer.Deserialize<QuestProgress>(json);
                if (progress != null)
                {
                    App.Logger?.Warning("Recovered quest progress from .tmp file");
                    // Promote .tmp to main file so future loads succeed normally
                    try { File.Move(tmpPath, _progressPath, overwrite: true); } catch { }
                    return progress;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to recover quest progress from .tmp file");
            }
        }

        return new QuestProgress();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_progressPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(Progress, new JsonSerializerOptions { WriteIndented = true });

            // Atomic write: write to .tmp first, then rename to prevent corruption on crash
            var tmpPath = _progressPath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _progressPath, overwrite: true);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to save quest progress");
        }
    }

    #endregion

    #region Quest Generation

    /// <summary>
    /// Check for expired quests and generate new ones if needed.
    /// Also regenerates quests whose definitions are no longer available (removed from server).
    /// </summary>
    public void CheckAndGenerateQuests()
    {
        bool changed = false;

        // Check daily quest - reset counter on new day
        if (Progress.IsDailyExpired() || Progress.DailyQuest == null)
        {
            // Before replacing: if the expired quest was completed today (ran past midnight),
            // preserve the completion count so it isn't lost when the counter resets
            bool wasCompletedToday = Progress.DailyQuest?.IsCompleted == true
                && Progress.DailyQuest.CompletedAt?.Date == DateTime.Today;

            // New day resets the daily completion counter
            Progress.GetDailyQuestsCompletedToday(); // triggers reset if new day

            // Restore the count if the quest was completed after midnight on the "old" quest
            if (wasCompletedToday && Progress.DailyQuestsCompletedToday == 0)
            {
                Progress.DailyQuestsCompletedToday = 1;
                App.Logger?.Information("Quest day rollover: preserved completion count (quest completed today on expired quest)");
            }

            GenerateNewDailyQuest();
            changed = true;
        }

        // Reconcile: if daily quest is completed today but counter doesn't reflect it
        if (Progress.DailyQuest?.IsCompleted == true
            && Progress.DailyQuest.CompletedAt?.Date == DateTime.Today
            && Progress.GetDailyQuestsCompletedToday() == 0)
        {
            Progress.DailyQuestsCompletedToday = 1;
            changed = true;
        }

        // If daily quest is already completed and we still have slots, generate next one
        if (Progress.DailyQuest?.IsCompleted == true
            && Progress.GetDailyQuestsCompletedToday() < MaxDailyQuestsPerDay)
        {
            var completedId = Progress.DailyQuest.DefinitionId;
            GenerateNewDailyQuest(excludeId: completedId);
            changed = true;
            App.Logger?.Information("Startup: generated next daily quest ({Completed}/{Max})",
                Progress.GetDailyQuestsCompletedToday(), MaxDailyQuestsPerDay);
        }

        // If daily quest definition is missing (removed from server), regenerate
        if (Progress.DailyQuest != null && !Progress.DailyQuest.IsCompleted && GetCurrentDailyDefinition() == null)
        {
            App.Logger?.Information("Daily quest definition '{QuestId}' no longer available, regenerating",
                Progress.DailyQuest.DefinitionId);
            GenerateNewDailyQuest();
            changed = true;
        }

        // Check weekly quest
        if (Progress.IsWeeklyExpired() || Progress.WeeklyQuest == null)
        {
            GenerateNewWeeklyQuest();
            changed = true;
        }

        // If weekly quest definition is missing (removed from server), regenerate
        if (Progress.WeeklyQuest != null && !Progress.WeeklyQuest.IsCompleted && GetCurrentWeeklyDefinition() == null)
        {
            App.Logger?.Information("Weekly quest definition '{QuestId}' no longer available, regenerating",
                Progress.WeeklyQuest.DefinitionId);
            GenerateNewWeeklyQuest();
            changed = true;
        }

        // If daily quest requires premium but access was lost, regenerate a free one
        if (Progress.DailyQuest != null && !Progress.DailyQuest.IsCompleted)
        {
            var dailyDef = GetCurrentDailyDefinition();
            if (dailyDef != null && !IsQuestAvailableForTier(dailyDef)
                && CanDropPremiumQuest(Progress.DailyQuest, "daily"))
            {
                App.Logger?.Information("Daily quest '{QuestId}' requires premium (access lost), regenerating",
                    Progress.DailyQuest.DefinitionId);
                GenerateNewDailyQuest();
                changed = true;
            }
        }

        // If daily quest's feature is locked at current level, regenerate
        if (Progress.DailyQuest != null && !Progress.DailyQuest.IsCompleted)
        {
            var dailyDef = GetCurrentDailyDefinition();
            if (dailyDef != null && !IsQuestAvailableForLevel(dailyDef.Category))
            {
                App.Logger?.Information("Daily quest '{QuestId}' requires locked feature ({Category}), regenerating",
                    Progress.DailyQuest.DefinitionId, dailyDef.Category);
                GenerateNewDailyQuest();
                changed = true;
            }
        }

        // If weekly quest requires premium but access was lost, regenerate a free one
        if (Progress.WeeklyQuest != null && !Progress.WeeklyQuest.IsCompleted)
        {
            var weeklyDef = GetCurrentWeeklyDefinition();
            if (weeklyDef != null && !IsQuestAvailableForTier(weeklyDef)
                && CanDropPremiumQuest(Progress.WeeklyQuest, "weekly"))
            {
                App.Logger?.Information("Weekly quest '{QuestId}' requires premium (access lost), regenerating",
                    Progress.WeeklyQuest.DefinitionId);
                GenerateNewWeeklyQuest();
                changed = true;
            }
        }

        // If weekly quest's feature is locked at current level, regenerate
        if (Progress.WeeklyQuest != null && !Progress.WeeklyQuest.IsCompleted)
        {
            var weeklyDef = GetCurrentWeeklyDefinition();
            if (weeklyDef != null && !IsQuestAvailableForLevel(weeklyDef.Category))
            {
                App.Logger?.Information("Weekly quest '{QuestId}' requires locked feature ({Category}), regenerating",
                    Progress.WeeklyQuest.DefinitionId, weeklyDef.Category);
                GenerateNewWeeklyQuest();
                changed = true;
            }
        }

        // The premium-loss rerolls above only defend a quest that already EXISTS. A slot rolled
        // while the entitlement was unresolved (launch on a fresh day is the common case) drew
        // from the free pool with nothing to defend, and no recheck was ever armed. Now that the
        // answer has landed as premium, re-roll the slot against the blended pool — but only
        // while it is untouched, so a quest the player has already worked on is never taken away.
        if (_dailyRolledUnresolved && IsEntitlementResolved())
        {
            _dailyRolledUnresolved = false;
            if (App.Patreon?.HasPremiumAccess == true && Progress.DailyQuest != null
                && !Progress.DailyQuest.IsCompleted && Progress.DailyQuest.CurrentProgress == 0)
            {
                App.Logger?.Information("Daily quest '{QuestId}' was rolled before the entitlement resolved — re-rolling with premium access",
                    Progress.DailyQuest.DefinitionId);
                GenerateNewDailyQuest();
                changed = true;
            }
        }

        if (_weeklyRolledUnresolved && IsEntitlementResolved())
        {
            _weeklyRolledUnresolved = false;
            if (App.Patreon?.HasPremiumAccess == true && Progress.WeeklyQuest != null
                && !Progress.WeeklyQuest.IsCompleted && Progress.WeeklyQuest.CurrentProgress == 0)
            {
                App.Logger?.Information("Weekly quest '{QuestId}' was rolled before the entitlement resolved — re-rolling with premium access",
                    Progress.WeeklyQuest.DefinitionId);
                GenerateNewWeeklyQuest();
                changed = true;
            }
        }

        if (changed)
        {
            _isDirty = true;
            Save();
        }
    }

    private void GenerateNewDailyQuest(string? excludeId = null)
    {
        // Rolling before the entitlement answer lands means rolling from the free-only pool.
        // Flag the slot (and arm the refresh tick) so it is re-rolled if the answer is premium.
        _dailyRolledUnresolved = !IsEntitlementResolved();
        if (_dailyRolledUnresolved) _premiumRecheckPending = true;

        // Use remote quests from QuestDefinitionService if available, fall back to embedded
        var questPool = App.QuestDefinitions?.GetDailyQuests() ?? QuestDefinition.DailyQuests.ToList();
        var availableQuests = questPool
            .Where(q => q.Id != excludeId)
            .Where(q => IsQuestAvailableForLevel(q.Category))
            .Where(IsQuestAvailableForTier)
            .Where(IsQuestInDateWindow)
            .ToList();

        // THE WINDOW MUST NEVER STARVE THE PLAYER. If a date window emptied the pool,
        // fall back to the undated pool rather than leaving the day questless: an event
        // that ends at midnight UTC-somewhere must not be able to take the daily quest
        // with it. See IsQuestInDateWindow.
        if (availableQuests.Count == 0)
        {
            availableQuests = questPool
                .Where(q => q.Id != excludeId)
                .Where(q => IsQuestAvailableForLevel(q.Category))
                .Where(IsQuestAvailableForTier)
                .ToList();
        }

        if (availableQuests.Count == 0) return;

        var selectedQuest = availableQuests[_random.Next(availableQuests.Count)];

        Progress.DailyQuest = new ActiveQuest(selectedQuest.Id);
        Progress.DailyQuestGeneratedAt = DateTime.Now;

        App.Logger?.Information("Generated new daily quest: {QuestId} (from {Source})",
            selectedQuest.Id, App.QuestDefinitions != null ? "server" : "embedded");
    }

    private void GenerateNewWeeklyQuest(string? excludeId = null)
    {
        // Same deferred-tier bookkeeping as the daily generator — see the note there.
        _weeklyRolledUnresolved = !IsEntitlementResolved();
        if (_weeklyRolledUnresolved) _premiumRecheckPending = true;

        // Use remote quests from QuestDefinitionService if available, fall back to embedded
        var questPool = App.QuestDefinitions?.GetWeeklyQuests() ?? QuestDefinition.WeeklyQuests.ToList();
        var availableQuests = questPool
            .Where(q => q.Id != excludeId)
            .Where(q => IsQuestAvailableForLevel(q.Category))
            .Where(IsQuestAvailableForTier)
            .Where(IsQuestInDateWindow)
            .ToList();

        // Same starvation guard as the daily generator — see the note there.
        if (availableQuests.Count == 0)
        {
            availableQuests = questPool
                .Where(q => q.Id != excludeId)
                .Where(q => IsQuestAvailableForLevel(q.Category))
                .Where(IsQuestAvailableForTier)
                .ToList();
        }

        if (availableQuests.Count == 0) return;

        var selectedQuest = availableQuests[_random.Next(availableQuests.Count)];

        Progress.WeeklyQuest = new ActiveQuest(selectedQuest.Id);
        Progress.WeeklyQuestGeneratedAt = DateTime.Now;

        App.Logger?.Information("Generated new weekly quest: {QuestId} (from {Source})",
            selectedQuest.Id, App.QuestDefinitions != null ? "server" : "embedded");
    }

    /// <summary>
    /// Guards the "requires premium, access lost" rerolls (#889). Throwing a premium quest away is
    /// only correct once we actually KNOW the entitlement, and a Patreon answer arrives seconds
    /// after launch — before it does, every patron looks free. Progress is also protected: a quest
    /// the player has already worked on is never taken away mid-run. A deferred decision is retried
    /// by the refresh timer, so a genuinely lapsed patron still gets a free quest a tick later.
    /// </summary>
    private bool CanDropPremiumQuest(ActiveQuest quest, string slot)
    {
        if (quest.CurrentProgress > 0)
        {
            App.Logger?.Information(
                "Premium {Slot} quest '{QuestId}' kept: it already has progress ({Progress})",
                slot, quest.DefinitionId, quest.CurrentProgress);
            return false;
        }

        if (!IsEntitlementResolved())
        {
            _premiumRecheckPending = true;
            App.Logger?.Information(
                "Premium {Slot} quest '{QuestId}' kept: entitlement not resolved yet, deferring the decision",
                slot, quest.DefinitionId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// True once the premium gate can be trusted this session: an entitled read is self-evidently
    /// resolved, and an unentitled one is only believed after validation has had time to land (and
    /// is not still in flight). Never permanently "unresolved" — the settle window guarantees a
    /// lapsed patron's quest is reconciled shortly after launch instead of never.
    /// </summary>
    private bool IsEntitlementResolved()
    {
        var patreon = App.Patreon;
        if (patreon == null) return false;
        if (patreon.IsVerifying) return false;
        if (patreon.HasPremiumAccess) return true;
        return DateTime.UtcNow - _startedUtc >= EntitlementSettleWindow;
    }

    /// <summary>
    /// Feature level gating has been removed — every quest category is available from level 1.
    /// </summary>
    private static bool IsQuestAvailableForLevel(QuestCategory category)
    {
        return true;
    }

    /// <summary>
    /// Premium-gated quests (RequiresPremium) are only offered to users with Patreon
    /// premium access. Free users never roll a quest tied to an exclusive feature they
    /// can't complete. Premium users get the blended pool (free + premium).
    /// </summary>
    private static bool IsQuestAvailableForTier(QuestDefinition quest)
    {
        return !quest.RequiresPremium || App.Patreon?.HasPremiumAccess == true;
    }

    /// <summary>
    /// THE DATE WINDOW, finally read. QuestDefinitionService has parsed, cached and
    /// refreshed `activeFrom`/`activeUntil` since the definitions channel shipped, and
    /// nothing has ever looked at them — a seasonal quest went live the moment it was
    /// published and stayed live forever. This predicate is that missing read, and it
    /// doubles as the channel's kill switch: publishing a quest with an `activeUntil`
    /// in the past retires it on the next roll without a client update.
    ///
    /// NO-OP BY CONSTRUCTION FOR EVERYTHING THAT SHIPS TODAY. Both fields are null on
    /// every embedded quest (QuestDefinition's ctor never sets them) and on every quest
    /// the live channel currently publishes, and a null/blank/unparseable bound is
    /// treated as "no constraint". A quest can only be hidden by a bound that is both
    /// present and a valid yyyy-MM-dd — anything else fails OPEN, because a server typo
    /// must cost a scheduling window, never the player's daily quest.
    ///
    /// LOCAL DATES, DELIBERATELY. The rest of quest scheduling turns over on
    /// DateTime.Today (QuestProgress.IsDailyExpired, GetStartOfWeek), so the window
    /// uses the same boundary — a quest that appears at "the start of the day" must
    /// appear at the start of the day the player's other quests reset on, not eight
    /// hours off it. `activeUntil` is INCLUSIVE: the last day named is a day it runs.
    /// </summary>
    private static bool IsQuestInDateWindow(QuestDefinition quest)
        => IsQuestInDateWindow(quest.ActiveFrom, quest.ActiveUntil, DateTime.Today);

    /// <summary>Pure form of the window test, split out so it is testable without a live App.</summary>
    internal static bool IsQuestInDateWindow(string? activeFrom, string? activeUntil, DateTime today)
    {
        if (TryParseQuestDate(activeFrom, out var from) && today.Date < from) return false;
        if (TryParseQuestDate(activeUntil, out var until) && today.Date > until) return false;
        return true;
    }

    /// <summary>
    /// Strict yyyy-MM-dd only. The channel is hand-authored JSON, so a loose parse would
    /// read "03/04" differently depending on the machine's culture and retire a quest on
    /// the wrong continent. False means "no usable bound" — the caller then ignores it.
    /// </summary>
    private static bool TryParseQuestDate(string? value, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);
    }

    /// <summary>
    /// Force regenerate the weekly quest (called by server-side reset flag)
    /// </summary>
    public void ForceRegenerateWeeklyQuest()
    {
        // Don't regenerate if current quest is still within this week
        if (Progress.WeeklyQuest != null && !Progress.IsWeeklyExpired())
        {
            App.Logger?.Information("Skipping weekly quest force-regeneration - quest still within current week");
            return;
        }

        var oldId = Progress.WeeklyQuest?.DefinitionId;
        GenerateNewWeeklyQuest(excludeId: oldId);
        _isDirty = true;
        Save();
        App.Logger?.Information("Force-regenerated weekly quest (old: {OldId}, new: {NewId})",
            oldId, Progress.WeeklyQuest?.DefinitionId);
    }

    /// <summary>
    /// Force regenerate the daily quest (called by server-side reset flag)
    /// </summary>
    public void ForceRegenerateDailyQuest()
    {
        var oldId = Progress.DailyQuest?.DefinitionId;
        GenerateNewDailyQuest(excludeId: oldId);
        _isDirty = true;
        Save();
        App.Logger?.Information("Force-regenerated daily quest (old: {OldId}, new: {NewId})",
            oldId, Progress.DailyQuest?.DefinitionId);
    }

    private static DateTime GetStartOfWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }

    #endregion

    #region Quest Definitions

    /// <summary>
    /// Get the definition for the current daily quest
    /// </summary>
    public QuestDefinition? GetCurrentDailyDefinition()
    {
        if (Progress.DailyQuest == null) return null;

        // Try remote quests first, fall back to embedded
        var remoteQuests = App.QuestDefinitions?.GetDailyQuests();
        if (remoteQuests != null)
        {
            var remoteQuest = remoteQuests.FirstOrDefault(q => q.Id == Progress.DailyQuest.DefinitionId);
            if (remoteQuest != null) return remoteQuest;
        }

        return QuestDefinition.DailyQuests.FirstOrDefault(q => q.Id == Progress.DailyQuest.DefinitionId);
    }

    /// <summary>
    /// Get the definition for the current weekly quest
    /// </summary>
    public QuestDefinition? GetCurrentWeeklyDefinition()
    {
        if (Progress.WeeklyQuest == null) return null;

        // Try remote quests first, fall back to embedded
        var remoteQuests = App.QuestDefinitions?.GetWeeklyQuests();
        if (remoteQuests != null)
        {
            var remoteQuest = remoteQuests.FirstOrDefault(q => q.Id == Progress.WeeklyQuest.DefinitionId);
            if (remoteQuest != null) return remoteQuest;
        }

        return QuestDefinition.WeeklyQuests.FirstOrDefault(q => q.Id == Progress.WeeklyQuest.DefinitionId);
    }

    #endregion

    #region Reroll

    /// <summary>
    /// Check if user has Patreon premium access
    /// </summary>
    private bool HasPatreonAccess => App.Patreon?.HasPremiumAccess == true;

    /// <summary>
    /// Get remaining daily rerolls (1 base + 2 for Patreon = 3 max)
    /// </summary>
    public int GetRemainingDailyRerolls() => Progress.GetRemainingDailyRerolls(HasPatreonAccess);

    /// <summary>
    /// Get remaining weekly rerolls (1 base + 2 for Patreon = 3 max)
    /// </summary>
    public int GetRemainingWeeklyRerolls() => Progress.GetRemainingWeeklyRerolls(HasPatreonAccess);

    /// <summary>
    /// Reroll the daily quest (1 base + 2 extra for Patreon users)
    /// </summary>
    /// <returns>True if reroll succeeded, false if no rerolls remaining</returns>
    public bool RerollDailyQuest()
    {
        if (!Progress.CanRerollDaily(HasPatreonAccess))
        {
            App.Logger?.Debug("No daily rerolls remaining");
            return false;
        }

        if (Progress.DailyQuest?.IsCompleted == true)
        {
            App.Logger?.Debug("Cannot reroll completed daily quest");
            return false;
        }

        var oldId = Progress.DailyQuest?.DefinitionId;
        GenerateNewDailyQuest(excludeId: oldId);
        Progress.DailyRerollsUsed++;
        _isDirty = true;
        Save();

        App.Logger?.Information("Daily quest rerolled from {OldId} to {NewId} (rerolls used: {Used})",
            oldId, Progress.DailyQuest?.DefinitionId, Progress.DailyRerollsUsed);
        return true;
    }

    /// <summary>
    /// Reroll the weekly quest (1 base + 2 extra for Patreon users)
    /// </summary>
    /// <returns>True if reroll succeeded, false if no rerolls remaining</returns>
    public bool RerollWeeklyQuest()
    {
        if (!Progress.CanRerollWeekly(HasPatreonAccess))
        {
            App.Logger?.Debug("No weekly rerolls remaining");
            return false;
        }

        if (Progress.WeeklyQuest?.IsCompleted == true)
        {
            App.Logger?.Debug("Cannot reroll completed weekly quest");
            return false;
        }

        var oldId = Progress.WeeklyQuest?.DefinitionId;
        GenerateNewWeeklyQuest(excludeId: oldId);
        Progress.WeeklyRerollsUsed++;
        _isDirty = true;
        Save();

        App.Logger?.Information("Weekly quest rerolled from {OldId} to {NewId} (rerolls used: {Used})",
            oldId, Progress.WeeklyQuest?.DefinitionId, Progress.WeeklyRerollsUsed);
        return true;
    }

    #endregion

    #region Progress Tracking

    /// <summary>
    /// Track flash image viewed
    /// </summary>
    public void TrackFlashImage()
    {
        UpdateQuestProgress(QuestCategory.Flash, 1);
    }

    /// <summary>
    /// Track spiral overlay time (called periodically with elapsed minutes)
    /// </summary>
    public void TrackSpiralMinutes(double minutes)
    {
        // Accumulate fractional minutes until we have at least 1 full minute
        _spiralMinutesAccumulator += minutes;
        _combinedMinutesAccumulator += minutes;

        if (_spiralMinutesAccumulator >= 1.0)
        {
            int wholeMinutes = (int)Math.Floor(_spiralMinutesAccumulator);
            UpdateQuestProgress(QuestCategory.Spiral, wholeMinutes);
            _spiralMinutesAccumulator -= wholeMinutes;
        }

        if (_combinedMinutesAccumulator >= 1.0)
        {
            int wholeMinutes = (int)Math.Floor(_combinedMinutesAccumulator);
            UpdateQuestProgress(QuestCategory.Combined, wholeMinutes);
            _combinedMinutesAccumulator -= wholeMinutes;
        }
    }

    /// <summary>
    /// Track pink filter time (called periodically with elapsed minutes)
    /// </summary>
    public void TrackPinkFilterMinutes(double minutes)
    {
        // Accumulate fractional minutes until we have at least 1 full minute
        _pinkFilterMinutesAccumulator += minutes;
        _combinedMinutesAccumulator += minutes;

        if (_pinkFilterMinutesAccumulator >= 1.0)
        {
            int wholeMinutes = (int)Math.Floor(_pinkFilterMinutesAccumulator);
            UpdateQuestProgress(QuestCategory.PinkFilter, wholeMinutes);
            _pinkFilterMinutesAccumulator -= wholeMinutes;
        }

        if (_combinedMinutesAccumulator >= 1.0)
        {
            int wholeMinutes = (int)Math.Floor(_combinedMinutesAccumulator);
            UpdateQuestProgress(QuestCategory.Combined, wholeMinutes);
            _combinedMinutesAccumulator -= wholeMinutes;
        }
    }

    /// <summary>
    /// Track BrainDrain overlay time (called periodically with elapsed minutes)
    /// </summary>
    public void TrackBrainDrainMinutes(double minutes)
    {
        // BrainDrain feeds into Combined category only (no dedicated BrainDrain quest category)
        _brainDrainMinutesAccumulator += minutes;
        _combinedMinutesAccumulator += minutes;

        if (_brainDrainMinutesAccumulator >= 1.0)
        {
            _brainDrainMinutesAccumulator -= (int)Math.Floor(_brainDrainMinutesAccumulator);
        }

        if (_combinedMinutesAccumulator >= 1.0)
        {
            int wholeMinutes = (int)Math.Floor(_combinedMinutesAccumulator);
            UpdateQuestProgress(QuestCategory.Combined, wholeMinutes);
            _combinedMinutesAccumulator -= wholeMinutes;
        }
    }

    /// <summary>
    /// Track bubble popped
    /// </summary>
    public void TrackBubblePopped()
    {
        UpdateQuestProgress(QuestCategory.Bubbles, 1);
    }

    /// <summary>
    /// Advance bubble-pop quests by a whole batch at once (DtRH web run reporting
    /// its total on completion).
    /// </summary>
    public void TrackBubblesPopped(int count)
    {
        if (count <= 0) return;
        UpdateQuestProgress(QuestCategory.Bubbles, count);
    }

    /// <summary>
    /// Track video minutes watched
    /// </summary>
    public void TrackVideoMinutes(double minutes)
    {
        // Accumulate fractional minutes until we have at least 1 full minute
        _videoMinutesAccumulator += minutes;

        if (_videoMinutesAccumulator >= 1.0)
        {
            int wholeMinutes = (int)Math.Floor(_videoMinutesAccumulator);
            UpdateQuestProgress(QuestCategory.Video, wholeMinutes);
            _videoMinutesAccumulator -= wholeMinutes;
        }
    }

    /// <summary>
    /// Track session completed
    /// </summary>
    public void TrackSessionCompleted()
    {
        UpdateQuestProgress(QuestCategory.Session, 1);
    }

    /// <summary>
    /// Track lock card completed
    /// </summary>
    public void TrackLockCardCompleted()
    {
        UpdateQuestProgress(QuestCategory.LockCard, 1);
    }

    /// <summary>
    /// Track bubble count game completed
    /// </summary>
    public void TrackBubbleCountCompleted()
    {
        UpdateQuestProgress(QuestCategory.BubbleCount, 1);
    }

    public void TrackMantraCompleted()
    {
        UpdateQuestProgress(QuestCategory.Mantra, 1);
    }

    /// <summary>
    /// Track Bambi Takeover (autonomy) active time (called periodically with elapsed minutes).
    /// Patreon-exclusive category.
    /// </summary>
    public void TrackAutonomyMinutes(double minutes)
    {
        // Accumulate fractional minutes until we have at least 1 full minute
        _autonomyMinutesAccumulator += minutes;

        if (_autonomyMinutesAccumulator >= 1.0)
        {
            int wholeMinutes = (int)Math.Floor(_autonomyMinutesAccumulator);
            UpdateQuestProgress(QuestCategory.Autonomy, wholeMinutes);
            _autonomyMinutesAccumulator -= wholeMinutes;
        }
    }

    /// <summary>
    /// Track a completed lockdown. Patreon-exclusive category.
    /// </summary>
    public void TrackLockdownCompleted()
    {
        UpdateQuestProgress(QuestCategory.Lockdown, 1);
    }

    /// <summary>
    /// Track a remote-control command received. Patreon-exclusive category.
    /// </summary>
    public void TrackRemoteCommand()
    {
        UpdateQuestProgress(QuestCategory.Remote, 1);
    }

    /// <summary>
    /// Track a keyword/OCR trigger firing. Patreon-exclusive category.
    /// </summary>
    public void TrackKeywordTrigger()
    {
        UpdateQuestProgress(QuestCategory.KeywordTrigger, 1);
    }

    /// <summary>
    /// Track a blink logged in the live blink trainer. Patreon-exclusive category.
    /// </summary>
    public void TrackBlinkTrainerBlink()
    {
        UpdateQuestProgress(QuestCategory.BlinkTrainer, 1);
    }

    /// <summary>
    /// Track XP earned (for "earn X XP" quests)
    /// </summary>
    public void TrackXPEarned(int xp)
    {
        // Check if there's a quest tracking XP earned specifically
        var dailyDef = GetCurrentDailyDefinition();
        var weeklyDef = GetCurrentWeeklyDefinition();

        // Only the conditioning_champion_w quest tracks XP earned
        if (weeklyDef?.Id == "conditioning_champion_w" && Progress.WeeklyQuest?.IsCompleted == false)
        {
            Progress.WeeklyQuest.CurrentProgress += xp;
            _isDirty = true;

            if (Progress.WeeklyQuest.CurrentProgress >= weeklyDef.TargetValue)
            {
                CompleteQuest(Progress.WeeklyQuest, weeklyDef, QuestType.Weekly);
            }
            else
            {
                QuestProgressChanged?.Invoke(this, new QuestProgressEventArgs(
                    QuestType.Weekly, Progress.WeeklyQuest.CurrentProgress, weeklyDef.TargetValue));
            }
        }
    }

    /// <summary>
    /// Track daily streak (called when streak updates)
    /// </summary>
    public void TrackStreak(int currentStreak)
    {
        var weeklyDef = GetCurrentWeeklyDefinition();

        // streak_keeper_w tracks maintaining a streak
        if (weeklyDef?.Id == "streak_keeper_w" && Progress.WeeklyQuest?.IsCompleted == false)
        {
            Progress.WeeklyQuest.CurrentProgress = Math.Max(Progress.WeeklyQuest.CurrentProgress, currentStreak);
            _isDirty = true;

            if (Progress.WeeklyQuest.CurrentProgress >= weeklyDef.TargetValue)
            {
                CompleteQuest(Progress.WeeklyQuest, weeklyDef, QuestType.Weekly);
            }
            else
            {
                QuestProgressChanged?.Invoke(this, new QuestProgressEventArgs(
                    QuestType.Weekly, Progress.WeeklyQuest.CurrentProgress, weeklyDef.TargetValue));
            }
        }
    }

    /// <summary>
    /// Update quest progress for a specific category
    /// </summary>
    private void UpdateQuestProgress(QuestCategory category, int amount)
    {
        if (amount <= 0) return;

        // Training Programs observe the same signals as quests, from the same choke point, so the two
        // can never disagree about what the user actually did. One tracking pass, not two.
        try { App.Programs?.TrackVerifier(category, amount); } catch { /* a program must never break quests */ }

        // Check daily quest
        var dailyDef = GetCurrentDailyDefinition();
        if (dailyDef != null && dailyDef.Category == category && Progress.DailyQuest?.IsCompleted == false)
        {
            Progress.DailyQuest.CurrentProgress += amount;
            _isDirty = true;

            if (Progress.DailyQuest.CurrentProgress >= dailyDef.TargetValue)
            {
                CompleteQuest(Progress.DailyQuest, dailyDef, QuestType.Daily);
            }
            else
            {
                QuestProgressChanged?.Invoke(this, new QuestProgressEventArgs(
                    QuestType.Daily, Progress.DailyQuest.CurrentProgress, dailyDef.TargetValue));
            }
        }

        // Check weekly quest
        var weeklyDef = GetCurrentWeeklyDefinition();
        // Skip quests that have dedicated tracking methods (conditioning_champion_w tracks XP via TrackXPEarned, not overlay minutes)
        var weeklyHasDedicatedTracking = weeklyDef?.Id is "conditioning_champion_w" or "streak_keeper_w";
        if (weeklyDef != null && weeklyDef.Category == category && Progress.WeeklyQuest?.IsCompleted == false && !weeklyHasDedicatedTracking)
        {
            Progress.WeeklyQuest.CurrentProgress += amount;
            _isDirty = true;

            if (Progress.WeeklyQuest.CurrentProgress >= weeklyDef.TargetValue)
            {
                CompleteQuest(Progress.WeeklyQuest, weeklyDef, QuestType.Weekly);
            }
            else
            {
                QuestProgressChanged?.Invoke(this, new QuestProgressEventArgs(
                    QuestType.Weekly, Progress.WeeklyQuest.CurrentProgress, weeklyDef.TargetValue));
            }
        }
    }

    public const int MaxDailyQuestsPerDay = 3;

    /// <summary>
    /// Get how many daily quests have been completed today
    /// </summary>
    public int GetDailyQuestsCompletedToday() => Progress.GetDailyQuestsCompletedToday();

    /// <summary>
    /// Check if all daily quests for today are done (3/3)
    /// </summary>
    public bool AreAllDailyQuestsCompleted() => Progress.AreAllDailyQuestsCompleted();

    /// <summary>
    /// Complete a quest and award rewards
    /// </summary>
    private void CompleteQuest(ActiveQuest quest, QuestDefinition def, QuestType type)
    {
        if (quest.IsCompleted) return;

        quest.IsCompleted = true;
        quest.CompletedAt = DateTime.Now;

        // Update statistics
        if (type == QuestType.Daily)
        {
            Progress.TotalDailyQuestsCompleted++;
            Progress.GetDailyQuestsCompletedToday(); // ensure reset if new day
            Progress.DailyQuestsCompletedToday++;

            // Record completion date for streak calendar (only once per day)
            var today = DateTime.Today;
            bool firstCompletionToday = !Progress.DailyQuestCompletionDates.Contains(today);
            if (firstCompletionToday)
            {
                Progress.DailyQuestCompletionDates.Add(today);
            }

            // Trim entries older than 90 days (matches cloud sync window)
            var cutoff = today.AddDays(-90);
            Progress.DailyQuestCompletionDates.RemoveAll(d => d.Date < cutoff);
            App.Settings?.Current?.StreakShieldUsedDates?.RemoveAll(d => d.Date < cutoff);

            // Apply streak shield if yesterday is missing (would break streak)
            var yesterday = today.AddDays(-1);
            if (!Progress.DailyQuestCompletionDates.Any(d => d.Date == yesterday)
                && App.Settings?.Current?.LastDailyQuestDate?.Date < yesterday)
            {
                if (App.SkillTree?.UseStreakShield() == true)
                {
                    Progress.DailyQuestCompletionDates.Add(yesterday);
                    var settings = App.Settings?.Current;
                    if (settings != null && !settings.StreakShieldUsedDates.Contains(yesterday))
                        settings.StreakShieldUsedDates.Add(yesterday);
                    App.Logger?.Information("Quest streak shield used! Filled gap at {Date}", yesterday);
                }
            }

            // Grow the streak incrementally on the first completion of a new day.
            // Tracked independently of the 90-day completion calendar so the streak
            // is NOT capped at the calendar's trim window (~91 days). The calendar
            // stays the source of truth for detecting BREAKS (via AdvanceQuestStreak's
            // yesterday check) and for repairing the streak upward after a sync.
            if (firstCompletionToday)
                AdvanceQuestStreak();

            // Repair the streak upward if the calendar proves a longer run than the
            // stored counter (e.g. after a cloud merge). Never decreases it.
            RecalculateStreak();
        }
        else
        {
            Progress.TotalWeeklyQuestsCompleted++;
        }

        // Scale XP reward based on player level. +4%/level pre-Descent, +1.2%/level once the
        // account has been through the migration ceremony (CONTRACTS-0812 §3) — the coefficient
        // moves with the XP curve, because it was the curve that made the old one a runaway.
        // ProgressionService.QuestLevelScale reads the per-user epoch, so this is a no-op for
        // every un-migrated account.
        var playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
        var betterQuestsMultiplier = App.SkillTree?.GetRerollBonusMultiplier() ?? 1.0;
        // Quest streak bonus: +3% per consecutive day
        var questStreak = App.Settings?.Current?.DailyQuestStreak ?? 0;
        var streakMultiplier = 1.0 + (questStreak * 0.03);
        var scaledXP = (int)Math.Round(def.XPReward * ProgressionService.QuestLevelScale(playerLevel) * betterQuestsMultiplier * streakMultiplier);

        Progress.TotalXPFromQuests += scaledXP;

        _isDirty = true;
        Save();

        // Award XP (use a different source to avoid recursion with TrackXPEarned)
        App.Progression?.AddXP(scaledXP, XPSource.Other);

        // Check for Perfect Bimbo Week bonus (7, 14, 30 day daily quest streaks)
        if (type == QuestType.Daily)
        {
            var bonusXP = App.SkillTree?.CheckPerfectWeekBonus() ?? 0;
            if (bonusXP > 0)
            {
                App.Progression?.AddXP(bonusXP, XPSource.Other);
            }
        }

        // Play celebration effects
        PlayCompletionEffects();

        App.Logger?.Information("Quest completed: {QuestName} ({Type}) - Awarded {XP} XP (base: {BaseXP}, level: {Level}, streak: {Streak}x{StreakPct}%)",
            def.Name, type, scaledXP, def.XPReward, playerLevel, questStreak, questStreak * 3);

        // Fire event
        QuestCompleted?.Invoke(this, new QuestCompletedEventArgs(def, scaledXP, type));

        // Auto-generate next daily quest if under the daily limit (3 per day)
        if (type == QuestType.Daily && Progress.DailyQuestsCompletedToday < MaxDailyQuestsPerDay)
        {
            GenerateNewDailyQuest(excludeId: def.Id);
            _isDirty = true;
            Save();

            App.Logger?.Information("Auto-generated next daily quest ({Completed}/{Max})",
                Progress.DailyQuestsCompletedToday, MaxDailyQuestsPerDay);
        }
    }

    /// <summary>
    /// Play sound effect and haptic feedback on quest completion
    /// </summary>
    private void PlayCompletionEffects()
    {
        try
        {
            // Play Windows notification sound
            SystemSounds.Exclamation.Play();

            // Quest completion posts its OWN event kind, so the Haptics tab's "Quest complete"
            // routing row (enable / intensity / pattern / target toy) is what decides how this
            // feels. It used to call AchievementPatternAsync(), which reads the ACHIEVEMENT row —
            // leaving the quest row on screen doing nothing at all.
            // ONE call only: this fired twice, which stacked two overlapping copies of the
            // same pattern on the toy rather than making it play any stronger.
            _ = App.Haptics?.PostEvent(Services.Haptics.Core.HapticEventKind.QuestComplete);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Error playing quest completion effects: {Error}", ex.Message);
        }
    }

    #endregion

    /// <summary>
    /// Recalculate daily quest streak from the completion calendar (single source of truth).
    /// Replaces fragile LastDailyQuestDate-based comparison.
    /// </summary>
    public void RecalculateStreak()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        var completedDates = new HashSet<DateTime>(
            Progress.DailyQuestCompletionDates.Select(d => d.Date));

        // Also include streak-shielded dates as "completed" for streak calculation
        if (settings.StreakShieldUsedDates != null)
        {
            foreach (var shieldDate in settings.StreakShieldUsedDates)
                completedDates.Add(shieldDate.Date);
        }

        int streak = 0;
        var checkDate = DateTime.Today;

        // If today isn't completed yet, start checking from yesterday
        if (!completedDates.Contains(checkDate))
            checkDate = checkDate.AddDays(-1);

        while (completedDates.Contains(checkDate))
        {
            streak++;
            checkDate = checkDate.AddDays(-1);
        }

        // Repair upward only. Day-to-day growth is handled incrementally by
        // AdvanceQuestStreak (so the streak is not capped at the calendar's ~90-day
        // trim window). Recalculation may only RAISE the streak — e.g. when a cloud
        // sync merges in completion dates that prove a longer run than the stored
        // counter — and must never lower it (dates may have been trimmed/lost).
        if (streak > settings.DailyQuestStreak)
        {
            App.Logger?.Debug("RecalculateStreak: calendar proves {Calculated} > stored {Current} — repairing upward",
                streak, settings.DailyQuestStreak);
            settings.DailyQuestStreak = streak;
        }

        // Keep LastDailyQuestDate in sync with actual quest completions (not shield fills)
        if (Progress.DailyQuestCompletionDates.Count > 0)
            settings.LastDailyQuestDate = Progress.DailyQuestCompletionDates.Max();
    }

    /// <summary>
    /// Grow (or reset) the persisted daily quest streak on the first daily-quest
    /// completion of a new day. Growth is tracked incrementally HERE rather than
    /// recomputed from the completion calendar, because the calendar is trimmed to a
    /// rolling 90-day window (see CompleteQuest) — recomputing from it caps the streak
    /// at ~91 and freezes it there permanently. Call exactly once per day, AFTER the
    /// streak shield has had a chance to fill yesterday.
    /// </summary>
    private void AdvanceQuestStreak()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        var yesterday = DateTime.Today.AddDays(-1);
        bool continuesStreak =
            Progress.DailyQuestCompletionDates.Any(d => d.Date == yesterday)
            || (settings.StreakShieldUsedDates?.Any(d => d.Date == yesterday) ?? false);

        if (continuesStreak || settings.DailyQuestStreak <= 0)
        {
            // Yesterday completed/shielded (chain continues) — or first streak ever.
            settings.DailyQuestStreak++;
        }
        else
        {
            // Gap with no shield: the streak broke; today is day 1 of a new streak.
            App.Logger?.Information("Quest streak reset to 1 — gap before {Today} (was {Prev})",
                DateTime.Today.ToString("yyyy-MM-dd"), settings.DailyQuestStreak);
            settings.DailyQuestStreak = 1;
        }
    }

    /// <summary>
    /// Reset all quest progress (used on logout to clear account-specific data)
    /// </summary>
    /// <param name="generateQuests">If false, skip quest generation (caller will generate after cloud sync)</param>
    public void ResetProgress(bool generateQuests = true)
    {
        Progress = new QuestProgress();
        _isDirty = false;
        Save();

        if (generateQuests)
        {
            // Generate fresh quests so the UI doesn't show "Loading..."
            CheckAndGenerateQuests();
        }

        App.Logger?.Information("QuestService progress reset (generateQuests={Generate})", generateQuests);
    }

    #region IDisposable

    public void Dispose()
    {
        _saveTimer.Stop();
        _refreshTimer.Stop();
        if (_isDirty)
        {
            Save();
        }
    }

    #endregion
}
