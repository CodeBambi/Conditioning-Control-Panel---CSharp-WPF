using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Core.Services.Progression;

/// <summary>
/// Cross-platform quest service: generates daily/weekly quests, tracks progress,
/// awards XP, and persists state to the user data folder.
/// </summary>
public sealed class QuestService : IQuestService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ISkillTreeService _skillTreeService;
    private readonly IAppEnvironment _appEnvironment;
    private readonly IQuestDefinitionService? _questDefinitions;
    private readonly IProgressionService? _progression;
    private readonly ILogger<QuestService>? _logger;
    private readonly string _progressPath;
    private readonly Random _random = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _refreshTimer;

    private bool _isDirty;
    private bool _isDisposed;

    // Fractional-minute accumulators for time-based quest categories.
    private double _spiralMinutesAccumulator;
    private double _pinkFilterMinutesAccumulator;
    private double _brainDrainMinutesAccumulator;
    private double _videoMinutesAccumulator;
    private double _combinedMinutesAccumulator;
    private double _autonomyMinutesAccumulator;

    /// <inheritdoc />
    public QuestProgress Progress { get; private set; }

    /// <inheritdoc />
    public event EventHandler? QuestsChanged;

    /// <inheritdoc />
    public event EventHandler<QuestCompletedEventArgs>? QuestCompleted;

    public const int MaxDailyQuestsPerDay = 3;

    public QuestService(
        ISettingsService settingsService,
        ISkillTreeService skillTreeService,
        IAppEnvironment appEnvironment,
        IQuestDefinitionService? questDefinitions = null,
        IProgressionService? progression = null,
        ILogger<QuestService>? logger = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _skillTreeService = skillTreeService ?? throw new ArgumentNullException(nameof(skillTreeService));
        _appEnvironment = appEnvironment ?? throw new ArgumentNullException(nameof(appEnvironment));
        _questDefinitions = questDefinitions;
        _progression = progression;
        _logger = logger;

        _progressPath = Path.Combine(appEnvironment.UserDataPath, "quests.json");
        Progress = LoadProgress();

        EnsureGenerated();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _saveTimer.Tick += (_, _) => OnAutoSaveTick();
        _saveTimer.Start();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _refreshTimer.Tick += (_, _) =>
        {
            var dailyExpired = Progress.IsDailyExpired();
            var weeklyExpired = Progress.IsWeeklyExpired();
            if (dailyExpired || weeklyExpired)
            {
                _logger?.LogInformation("Quest rollover detected (daily={Daily}, weekly={Weekly})", dailyExpired, weeklyExpired);
                EnsureGenerated();
            }
        };
        _refreshTimer.Start();

        _logger?.LogInformation("QuestService initialized. Daily: {Daily}, Weekly: {Weekly}",
            Progress.DailyQuest?.DefinitionId ?? "none",
            Progress.WeeklyQuest?.DefinitionId ?? "none");
    }

    #region Persistence

    private QuestProgress LoadProgress()
    {
        var tmpPath = _progressPath + ".tmp";

        if (File.Exists(_progressPath))
        {
            try
            {
                var json = File.ReadAllText(_progressPath);
                var progress = JsonSerializer.Deserialize<QuestProgress>(json);
                if (progress != null)
                {
                    return progress;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load quest progress from {Path}", _progressPath);
            }
        }

        // Recover from an atomic-write temp file if the main file is missing or corrupt.
        if (File.Exists(tmpPath))
        {
            try
            {
                var json = File.ReadAllText(tmpPath);
                var progress = JsonSerializer.Deserialize<QuestProgress>(json);
                if (progress != null)
                {
                    _logger?.LogWarning("Recovered quest progress from temp file {Path}", tmpPath);
                    try { File.Move(tmpPath, _progressPath, overwrite: true); } catch { }
                    return progress;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to recover quest progress from {Path}", tmpPath);
            }
        }

        return new QuestProgress();
    }

    private void SaveProgress()
    {
        try
        {
            var directory = Path.GetDirectoryName(_progressPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // System.Text.Json with WriteIndented, matching the WPF QuestService and the Core
            // AchievementService — both heads share quests.json, so the serializers must agree.
            var json = JsonSerializer.Serialize(Progress, new JsonSerializerOptions { WriteIndented = true });
            var tmpPath = _progressPath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _progressPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save quest progress to {Path}", _progressPath);
        }
    }

    private void OnAutoSaveTick()
    {
        if (!_isDirty) return;
        _isDirty = false;
        _ = Task.Run(() => SaveProgress());
    }

    /// <summary>
    /// Persist immediately (write+rename) and notify listeners. Used on state-changing
    /// events that must survive a crash: quest completion, reroll, and generation.
    /// </summary>
    private void SaveAndNotify()
    {
        _isDirty = false;
        SaveProgress();
        QuestsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Mark progress dirty for the 30s autosave timer and notify listeners WITHOUT a
    /// synchronous disk write. Used for high-frequency incremental progress (per flash,
    /// bubble, minute) so we don't hammer the disk with a write+rename on every tick.
    /// </summary>
    private void MarkDirtyAndNotify()
    {
        _isDirty = true;
        QuestsChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Generation

    /// <inheritdoc />
    public void EnsureGenerated()
    {
        bool changed = false;

        // Daily quest: on a new day (or when missing), preserve a completion that ran
        // past midnight before the counter resets, then generate a fresh daily.
        if (Progress.IsDailyExpired() || Progress.DailyQuest == null)
        {
            // If the expiring quest was completed today (ran past midnight), preserve the
            // completion count so it isn't lost when the daily counter resets on the new day.
            bool wasCompletedToday = Progress.DailyQuest?.IsCompleted == true
                && Progress.DailyQuest.CompletedAt?.Date == DateTime.Today;

            Progress.GetDailyQuestsCompletedToday(); // triggers the new-day reset

            if (wasCompletedToday && Progress.DailyQuestsCompletedToday == 0)
            {
                Progress.DailyQuestsCompletedToday = 1;
                _logger?.LogInformation("Quest day rollover: preserved completion count (quest completed today on expired quest)");
            }

            if (!Progress.AreAllDailyQuestsCompleted())
            {
                GenerateDailyQuest();
            }
            changed = true;
        }

        // Reconcile: daily quest is completed today but the counter doesn't reflect it.
        if (Progress.DailyQuest?.IsCompleted == true
            && Progress.DailyQuest.CompletedAt?.Date == DateTime.Today
            && Progress.GetDailyQuestsCompletedToday() == 0)
        {
            Progress.DailyQuestsCompletedToday = 1;
            changed = true;
        }

        // Auto-generate the next daily quest after completing one (up to 3 per day).
        if (Progress.DailyQuest?.IsCompleted == true
            && Progress.GetDailyQuestsCompletedToday() < MaxDailyQuestsPerDay)
        {
            var completedId = Progress.DailyQuest.DefinitionId;
            GenerateDailyQuest(excludeId: completedId);
            changed = true;
        }

        // Regenerate if the definition is no longer available (removed from server/pool).
        if (Progress.DailyQuest != null && !Progress.DailyQuest.IsCompleted && GetDailyDefinition() == null)
        {
            _logger?.LogInformation("Daily quest definition '{QuestId}' no longer available, regenerating", Progress.DailyQuest.DefinitionId);
            GenerateDailyQuest();
            changed = true;
        }

        // Regenerate if premium access was lost.
        if (Progress.DailyQuest != null && !Progress.DailyQuest.IsCompleted)
        {
            var def = GetDailyDefinition();
            if (def != null && !IsQuestAvailableForTier(def))
            {
                _logger?.LogInformation("Daily quest '{QuestId}' requires premium (access lost), regenerating", Progress.DailyQuest.DefinitionId);
                GenerateDailyQuest();
                changed = true;
            }
        }

        // Weekly quest: generate if missing/expired.
        if (Progress.WeeklyQuest == null || Progress.IsWeeklyExpired())
        {
            GenerateWeeklyQuest();
            changed = true;
        }

        if (Progress.WeeklyQuest != null && !Progress.WeeklyQuest.IsCompleted && GetWeeklyDefinition() == null)
        {
            _logger?.LogInformation("Weekly quest definition '{QuestId}' no longer available, regenerating", Progress.WeeklyQuest.DefinitionId);
            GenerateWeeklyQuest();
            changed = true;
        }

        if (Progress.WeeklyQuest != null && !Progress.WeeklyQuest.IsCompleted)
        {
            var def = GetWeeklyDefinition();
            if (def != null && !IsQuestAvailableForTier(def))
            {
                _logger?.LogInformation("Weekly quest '{QuestId}' requires premium (access lost), regenerating", Progress.WeeklyQuest.DefinitionId);
                GenerateWeeklyQuest();
                changed = true;
            }
        }

        if (changed)
        {
            SaveAndNotify();
        }
    }

    private void GenerateDailyQuest(string? excludeId = null)
    {
        var pool = _questDefinitions?.GetDailyQuests() ?? QuestDefinition.DailyQuests;
        if (pool.Count == 0) return;

        var available = pool
            .Where(q => q.Id != excludeId)
            .Where(IsQuestAvailableForTier)
            .ToList();

        if (available.Count == 0)
        {
            available = pool.Where(IsQuestAvailableForTier).ToList();
        }

        if (available.Count == 0) return;

        var selected = available[_random.Next(available.Count)];
        Progress.DailyQuest = new ActiveQuest(selected.Id);
        Progress.DailyQuestGeneratedAt = DateTime.Today;
        _logger?.LogInformation("Generated new daily quest: {QuestId}", selected.Id);
    }

    private void GenerateWeeklyQuest(string? excludeId = null)
    {
        var pool = _questDefinitions?.GetWeeklyQuests() ?? QuestDefinition.WeeklyQuests;
        if (pool.Count == 0) return;

        var available = pool
            .Where(q => q.Id != excludeId)
            .Where(IsQuestAvailableForTier)
            .ToList();

        if (available.Count == 0)
        {
            available = pool.Where(IsQuestAvailableForTier).ToList();
        }

        if (available.Count == 0) return;

        var selected = available[_random.Next(available.Count)];
        Progress.WeeklyQuest = new ActiveQuest(selected.Id);
        Progress.WeeklyQuestGeneratedAt = DateTime.Today;
        _logger?.LogInformation("Generated new weekly quest: {QuestId}", selected.Id);
    }

    /// <inheritdoc />
    public void ForceRegenerateDailyQuest()
    {
        var oldId = Progress.DailyQuest?.DefinitionId;
        GenerateDailyQuest(excludeId: oldId);
        SaveAndNotify();
        _logger?.LogInformation("Force-regenerated daily quest (old: {OldId}, new: {NewId})",
            oldId, Progress.DailyQuest?.DefinitionId);
    }

    /// <inheritdoc />
    public void ForceRegenerateWeeklyQuest()
    {
        // Don't regenerate if the current weekly quest is still within this week (WPF parity).
        if (Progress.WeeklyQuest != null && !Progress.IsWeeklyExpired())
        {
            _logger?.LogInformation("Skipping weekly quest force-regeneration - quest still within current week");
            return;
        }

        var oldId = Progress.WeeklyQuest?.DefinitionId;
        GenerateWeeklyQuest(excludeId: oldId);
        SaveAndNotify();
        _logger?.LogInformation("Force-regenerated weekly quest (old: {OldId}, new: {NewId})",
            oldId, Progress.WeeklyQuest?.DefinitionId);
    }

    private bool HasPremiumAccess => _settingsService.Current?.HasCachedPremiumAccess == true;

    private bool IsQuestAvailableForTier(QuestDefinition quest)
    {
        return !quest.RequiresPremium || HasPremiumAccess;
    }

    private QuestDefinition? GetDailyDefinition()
    {
        if (Progress.DailyQuest == null) return null;

        var remote = _questDefinitions?.GetDailyQuests();
        if (remote != null)
        {
            var remoteQuest = remote.FirstOrDefault(q => q.Id == Progress.DailyQuest.DefinitionId);
            if (remoteQuest != null) return remoteQuest;
        }

        return QuestDefinition.DailyQuests.FirstOrDefault(q => q.Id == Progress.DailyQuest.DefinitionId);
    }

    private QuestDefinition? GetWeeklyDefinition()
    {
        if (Progress.WeeklyQuest == null) return null;

        var remote = _questDefinitions?.GetWeeklyQuests();
        if (remote != null)
        {
            var remoteQuest = remote.FirstOrDefault(q => q.Id == Progress.WeeklyQuest.DefinitionId);
            if (remoteQuest != null) return remoteQuest;
        }

        return QuestDefinition.WeeklyQuests.FirstOrDefault(q => q.Id == Progress.WeeklyQuest.DefinitionId);
    }

    #endregion

    #region Rerolls

    /// <inheritdoc />
    public int GetRemainingDailyRerolls()
    {
        if (Progress.DailyRerollResetDate?.Date != DateTime.Today)
        {
            Progress.DailyRerollsUsed = 0;
            Progress.DailyRerollResetDate = DateTime.Today;
        }

        int maxRerolls = HasPremiumAccess ? 3 : 1;
        maxRerolls += _skillTreeService.GetDailyFreeRerolls();
        maxRerolls += _settingsService.Current?.BonusDailyRerolls ?? 0;
        return Math.Max(0, maxRerolls - Progress.DailyRerollsUsed);
    }

    /// <inheritdoc />
    public int GetRemainingWeeklyRerolls()
    {
        var startOfWeek = GetStartOfWeek(DateTime.Today);

        if (!Progress.WeeklyRerollResetDate.HasValue || Progress.WeeklyRerollResetDate.Value.Date < startOfWeek)
        {
            Progress.WeeklyRerollsUsed = 0;
            Progress.WeeklyRerollResetDate = DateTime.Today;
        }

        int maxRerolls = HasPremiumAccess ? 3 : 1;
        maxRerolls += _skillTreeService.GetDailyFreeRerolls();
        maxRerolls += _settingsService.Current?.BonusWeeklyRerolls ?? 0;
        return Math.Max(0, maxRerolls - Progress.WeeklyRerollsUsed);
    }

    private static DateTime GetStartOfWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }

    /// <inheritdoc />
    public bool RerollDaily()
    {
        if (GetRemainingDailyRerolls() <= 0)
        {
            _logger?.LogDebug("No daily rerolls remaining");
            return false;
        }

        if (Progress.DailyQuest?.IsCompleted == true)
        {
            _logger?.LogDebug("Cannot reroll completed daily quest");
            return false;
        }

        var oldId = Progress.DailyQuest?.DefinitionId;
        GenerateDailyQuest(excludeId: oldId);
        Progress.DailyRerollsUsed++;
        _logger?.LogInformation("Daily quest rerolled from {OldId} to {NewId}", oldId, Progress.DailyQuest?.DefinitionId);
        SaveAndNotify();
        return true;
    }

    /// <inheritdoc />
    public bool RerollWeekly()
    {
        if (GetRemainingWeeklyRerolls() <= 0)
        {
            _logger?.LogDebug("No weekly rerolls remaining");
            return false;
        }

        if (Progress.WeeklyQuest?.IsCompleted == true)
        {
            _logger?.LogDebug("Cannot reroll completed weekly quest");
            return false;
        }

        var oldId = Progress.WeeklyQuest?.DefinitionId;
        GenerateWeeklyQuest(excludeId: oldId);
        Progress.WeeklyRerollsUsed++;
        _logger?.LogInformation("Weekly quest rerolled from {OldId} to {NewId}", oldId, Progress.WeeklyQuest?.DefinitionId);
        SaveAndNotify();
        return true;
    }

    #endregion

    #region Progress

    /// <inheritdoc />
    public void AddProgress(QuestCategory category, int amount)
    {
        if (amount <= 0) return;

        bool changed = false;

        var dailyDef = GetDailyDefinition();
        if (dailyDef != null && dailyDef.Category == category && Progress.DailyQuest?.IsCompleted == false)
        {
            Progress.DailyQuest.CurrentProgress = Math.Min(
                Progress.DailyQuest.CurrentProgress + amount,
                dailyDef.TargetValue);

            if (Progress.DailyQuest.CurrentProgress >= dailyDef.TargetValue)
            {
                CompleteQuest(Progress.DailyQuest, dailyDef, QuestType.Daily);
                return;
            }
            changed = true;
        }

        var weeklyDef = GetWeeklyDefinition();
        var weeklyHasDedicatedTracking = weeklyDef?.Id is "conditioning_champion_w" or "streak_keeper_w";
        if (weeklyDef != null && weeklyDef.Category == category && Progress.WeeklyQuest?.IsCompleted == false && !weeklyHasDedicatedTracking)
        {
            Progress.WeeklyQuest.CurrentProgress = Math.Min(
                Progress.WeeklyQuest.CurrentProgress + amount,
                weeklyDef.TargetValue);

            if (Progress.WeeklyQuest.CurrentProgress >= weeklyDef.TargetValue)
            {
                CompleteQuest(Progress.WeeklyQuest, weeklyDef, QuestType.Weekly);
                return;
            }
            changed = true;
        }

        if (changed)
        {
            MarkDirtyAndNotify();
        }
    }

    /// <inheritdoc />
    public void TrackMantraCompleted() => AddProgress(QuestCategory.Mantra, 1);

    /// <inheritdoc />
    public void TrackFlashImage() => AddProgress(QuestCategory.Flash, 1);

    /// <inheritdoc />
    public void TrackBubblePopped() => AddProgress(QuestCategory.Bubbles, 1);

    /// <inheritdoc />
    public void TrackSessionCompleted() => AddProgress(QuestCategory.Session, 1);

    /// <inheritdoc />
    public void TrackLockCardCompleted() => AddProgress(QuestCategory.LockCard, 1);

    /// <inheritdoc />
    public void TrackBubbleCountCompleted() => AddProgress(QuestCategory.BubbleCount, 1);

    /// <inheritdoc />
    public void TrackLockdownCompleted() => AddProgress(QuestCategory.Lockdown, 1);

    /// <inheritdoc />
    public void TrackRemoteCommand() => AddProgress(QuestCategory.Remote, 1);

    /// <inheritdoc />
    public void TrackKeywordTrigger() => AddProgress(QuestCategory.KeywordTrigger, 1);

    /// <inheritdoc />
    public void TrackBlinkTrainerBlink() => AddProgress(QuestCategory.BlinkTrainer, 1);

    /// <inheritdoc />
    public void TrackSpiralMinutes(double minutes)
    {
        _spiralMinutesAccumulator += minutes;
        _combinedMinutesAccumulator += minutes;

        if (_spiralMinutesAccumulator >= 1.0)
        {
            int whole = (int)Math.Floor(_spiralMinutesAccumulator);
            AddProgress(QuestCategory.Spiral, whole);
            _spiralMinutesAccumulator -= whole;
        }

        if (_combinedMinutesAccumulator >= 1.0)
        {
            int whole = (int)Math.Floor(_combinedMinutesAccumulator);
            AddProgress(QuestCategory.Combined, whole);
            _combinedMinutesAccumulator -= whole;
        }
    }

    /// <inheritdoc />
    public void TrackPinkFilterMinutes(double minutes)
    {
        _pinkFilterMinutesAccumulator += minutes;
        _combinedMinutesAccumulator += minutes;

        if (_pinkFilterMinutesAccumulator >= 1.0)
        {
            int whole = (int)Math.Floor(_pinkFilterMinutesAccumulator);
            AddProgress(QuestCategory.PinkFilter, whole);
            _pinkFilterMinutesAccumulator -= whole;
        }

        if (_combinedMinutesAccumulator >= 1.0)
        {
            int whole = (int)Math.Floor(_combinedMinutesAccumulator);
            AddProgress(QuestCategory.Combined, whole);
            _combinedMinutesAccumulator -= whole;
        }
    }

    /// <inheritdoc />
    public void TrackBrainDrainMinutes(double minutes)
    {
        _brainDrainMinutesAccumulator += minutes;
        _combinedMinutesAccumulator += minutes;

        if (_brainDrainMinutesAccumulator >= 1.0)
        {
            _brainDrainMinutesAccumulator -= (int)Math.Floor(_brainDrainMinutesAccumulator);
        }

        if (_combinedMinutesAccumulator >= 1.0)
        {
            int whole = (int)Math.Floor(_combinedMinutesAccumulator);
            AddProgress(QuestCategory.Combined, whole);
            _combinedMinutesAccumulator -= whole;
        }
    }

    /// <inheritdoc />
    public void TrackVideoMinutes(double minutes)
    {
        _videoMinutesAccumulator += minutes;

        if (_videoMinutesAccumulator >= 1.0)
        {
            int whole = (int)Math.Floor(_videoMinutesAccumulator);
            AddProgress(QuestCategory.Video, whole);
            _videoMinutesAccumulator -= whole;
        }
    }

    /// <inheritdoc />
    public void TrackAutonomyMinutes(double minutes)
    {
        _autonomyMinutesAccumulator += minutes;

        if (_autonomyMinutesAccumulator >= 1.0)
        {
            int whole = (int)Math.Floor(_autonomyMinutesAccumulator);
            AddProgress(QuestCategory.Autonomy, whole);
            _autonomyMinutesAccumulator -= whole;
        }
    }

    /// <inheritdoc />
    public void TrackXPEarned(int xp)
    {
        if (xp <= 0) return;

        var weeklyDef = GetWeeklyDefinition();
        if (weeklyDef?.Id == "conditioning_champion_w" && Progress.WeeklyQuest?.IsCompleted == false)
        {
            Progress.WeeklyQuest.CurrentProgress += xp;
            if (Progress.WeeklyQuest.CurrentProgress >= weeklyDef.TargetValue)
            {
                CompleteQuest(Progress.WeeklyQuest, weeklyDef, QuestType.Weekly);
            }
            else
            {
                MarkDirtyAndNotify();
            }
        }
    }

    /// <inheritdoc />
    public void TrackStreak(int currentStreak)
    {
        var weeklyDef = GetWeeklyDefinition();
        if (weeklyDef?.Id == "streak_keeper_w" && Progress.WeeklyQuest?.IsCompleted == false)
        {
            Progress.WeeklyQuest.CurrentProgress = Math.Max(Progress.WeeklyQuest.CurrentProgress, currentStreak);
            if (Progress.WeeklyQuest.CurrentProgress >= weeklyDef.TargetValue)
            {
                CompleteQuest(Progress.WeeklyQuest, weeklyDef, QuestType.Weekly);
            }
            else
            {
                MarkDirtyAndNotify();
            }
        }
    }

    /// <inheritdoc />
    public void CompleteDailyQuest()
    {
        var def = GetDailyDefinition();
        if (Progress.DailyQuest != null && def != null && !Progress.DailyQuest.IsCompleted)
        {
            CompleteQuest(Progress.DailyQuest, def, QuestType.Daily);
        }
    }

    /// <inheritdoc />
    public void FixStreakDay(DateTime date)
    {
        var day = date.Date;
        if (!Progress.DailyQuestCompletionDates.Any(d => d.Date == day))
        {
            Progress.DailyQuestCompletionDates.Add(day);
        }
        SaveProgress();
        _logger?.LogInformation("Streak day fixed (Oopsie Insurance): {Date}", day);
    }

    private void CompleteQuest(ActiveQuest quest, QuestDefinition def, QuestType type)
    {
        if (quest.IsCompleted) return;

        quest.IsCompleted = true;
        quest.CompletedAt = DateTime.Now;

        int xp = type == QuestType.Daily ? GetScaledDailyXp() : GetScaledWeeklyXp();

        if (type == QuestType.Daily)
        {
            Progress.GetDailyQuestsCompletedToday();
            Progress.DailyQuestsCompletedToday++;
            Progress.TotalDailyQuestsCompleted++;

            var today = DateTime.Today;
            bool firstCompletionToday = !Progress.DailyQuestCompletionDates.Contains(today);
            if (firstCompletionToday)
            {
                Progress.DailyQuestCompletionDates.Add(today);
            }

            var cutoff = today.AddDays(-90);
            Progress.DailyQuestCompletionDates.RemoveAll(d => d.Date < cutoff);
            _settingsService.Current?.StreakShieldUsedDates?.RemoveAll(d => d.Date < cutoff);

            var yesterday = today.AddDays(-1);
            var settings = _settingsService.Current;
            if (settings != null
                && !Progress.DailyQuestCompletionDates.Any(d => d.Date == yesterday)
                && settings.LastDailyQuestDate?.Date < yesterday)
            {
                if (_skillTreeService.UseStreakShield())
                {
                    Progress.DailyQuestCompletionDates.Add(yesterday);
                    if (!settings.StreakShieldUsedDates.Contains(yesterday))
                        settings.StreakShieldUsedDates.Add(yesterday);
                    _logger?.LogInformation("Quest streak shield used! Filled gap at {Date}", yesterday);
                }
            }

            // Grow the streak only on the first completion of a new day so up to three
            // dailies/day don't triple-count it. The calendar (trimmed to 90 days) stays
            // the source of truth for detecting BREAKS; RecalculateStreak repairs upward.
            if (firstCompletionToday)
                AdvanceQuestStreak();
            RecalculateStreak();
        }
        else
        {
            Progress.TotalWeeklyQuestsCompleted++;
        }

        Progress.TotalXPFromQuests += xp;
        _progression?.AddXP(xp, XPSource.Other);

        // Perfect-week bonus (7/14/30-day daily streaks), awarded on daily completions only.
        if (type == QuestType.Daily)
        {
            var perfectWeekBonus = _skillTreeService.CheckPerfectWeekBonus();
            if (perfectWeekBonus > 0)
            {
                _progression?.AddXP(perfectWeekBonus, XPSource.Other);
                _logger?.LogInformation("Perfect-week bonus awarded: {XP} XP", perfectWeekBonus);
            }
        }

        _logger?.LogInformation(
            "Quest completed: {QuestName} ({Type}) - Awarded {XP} XP",
            def.Name, type, xp);

        SaveAndNotify();

        try
        {
            QuestCompleted?.Invoke(this, new QuestCompletedEventArgs(def.Name, xp, type));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "QuestCompleted subscriber threw");
        }

        if (type == QuestType.Daily && Progress.DailyQuestsCompletedToday < MaxDailyQuestsPerDay)
        {
            GenerateDailyQuest(excludeId: def.Id);
            SaveAndNotify();
        }
    }

    /// <inheritdoc />
    public int GetScaledDailyXp()
    {
        var settings = _settingsService.Current;
        int playerLevel = settings?.PlayerLevel ?? 1;
        int questStreak = settings?.DailyQuestStreak ?? 0;
        int baseXp = GetDailyDefinition()?.XPReward ?? 150;
        double betterQuestsMultiplier = _skillTreeService.GetRerollBonusMultiplier();
        return (int)Math.Round(baseXp * (1 + playerLevel * 0.04) * betterQuestsMultiplier * (1 + questStreak * 0.03));
    }

    /// <inheritdoc />
    public int GetScaledWeeklyXp()
    {
        var settings = _settingsService.Current;
        int playerLevel = settings?.PlayerLevel ?? 1;
        int questStreak = settings?.DailyQuestStreak ?? 0;
        int baseXp = GetWeeklyDefinition()?.XPReward ?? 600;
        double betterQuestsMultiplier = _skillTreeService.GetRerollBonusMultiplier();
        return (int)Math.Round(baseXp * (1 + playerLevel * 0.04) * betterQuestsMultiplier * (1 + questStreak * 0.03));
    }

    private void AdvanceQuestStreak()
    {
        var settings = _settingsService.Current;
        if (settings == null) return;

        var yesterday = DateTime.Today.AddDays(-1);
        bool continuesStreak =
            Progress.DailyQuestCompletionDates.Any(d => d.Date == yesterday)
            || (settings.StreakShieldUsedDates?.Any(d => d.Date == yesterday) ?? false);

        if (continuesStreak || settings.DailyQuestStreak <= 0)
        {
            settings.DailyQuestStreak++;
        }
        else
        {
            _logger?.LogInformation("Quest streak reset to 1 — gap before {Today} (was {Prev})",
                DateTime.Today.ToString("yyyy-MM-dd"), settings.DailyQuestStreak);
            settings.DailyQuestStreak = 1;
        }

        settings.LastDailyQuestDate = DateTime.Today;
        _settingsService.Save();
    }

    /// <inheritdoc />
    public void RecalculateStreak()
    {
        var settings = _settingsService.Current;
        if (settings == null) return;

        var completedDates = new HashSet<DateTime>(
            Progress.DailyQuestCompletionDates.Select(d => d.Date));

        if (settings.StreakShieldUsedDates != null)
        {
            foreach (var shieldDate in settings.StreakShieldUsedDates)
                completedDates.Add(shieldDate.Date);
        }

        int streak = 0;
        var checkDate = DateTime.Today;
        if (!completedDates.Contains(checkDate))
            checkDate = checkDate.AddDays(-1);

        while (completedDates.Contains(checkDate))
        {
            streak++;
            checkDate = checkDate.AddDays(-1);
        }

        if (streak > settings.DailyQuestStreak)
        {
            _logger?.LogDebug("RecalculateStreak: calendar proves {Calculated} > stored {Current} — repairing upward",
                streak, settings.DailyQuestStreak);
            settings.DailyQuestStreak = streak;
        }

        // Keep LastDailyQuestDate in sync with actual quest completions (not shield fills).
        // WPF QuestService.cs:1024-1026 parity (restored per slice-3 adversarial review).
        if (Progress.DailyQuestCompletionDates.Count > 0)
            settings.LastDailyQuestDate = Progress.DailyQuestCompletionDates.Max();
    }

    #endregion

    #region IDisposable

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _saveTimer.Stop();
        _refreshTimer.Stop();

        if (_isDirty)
        {
            SaveProgress();
        }
    }

    #endregion
}
