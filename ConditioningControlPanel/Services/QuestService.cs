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
    private readonly Random _random = new();
    private bool _isDirty;

    // Accumulators for fractional minutes (time-based quests are called with small increments)
    private double _spiralMinutesAccumulator;
    private double _pinkFilterMinutesAccumulator;
    private double _videoMinutesAccumulator;
    private double _combinedMinutesAccumulator;

    public QuestProgress Progress { get; private set; }

    public event EventHandler<QuestCompletedEventArgs>? QuestCompleted;
    public event EventHandler<QuestProgressEventArgs>? QuestProgressChanged;

    public QuestService()
    {
        _progressPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel",
            "quests.json");

        Progress = LoadProgress();

        // Check for expired quests and generate new ones
        CheckAndGenerateQuests();

        // Auto-save every 30 seconds if dirty
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _saveTimer.Tick += (s, e) =>
        {
            if (_isDirty)
            {
                Save();
                _isDirty = false;
            }
        };
        _saveTimer.Start();

        App.Logger?.Information("QuestService initialized. Daily: {Daily}, Weekly: {Weekly}",
            Progress.DailyQuest?.DefinitionId ?? "none",
            Progress.WeeklyQuest?.DefinitionId ?? "none");
    }

    #region Persistence

    private QuestProgress LoadProgress()
    {
        try
        {
            if (File.Exists(_progressPath))
            {
                var json = File.ReadAllText(_progressPath);
                return JsonSerializer.Deserialize<QuestProgress>(json) ?? new QuestProgress();
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to load quest progress");
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
            File.WriteAllText(_progressPath, json);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to save quest progress");
        }
    }

    #endregion

    #region Quest Generation

    /// <summary>
    /// Check for expired quests and generate new ones if needed
    /// </summary>
    public void CheckAndGenerateQuests()
    {
        bool changed = false;

        // Check daily quest
        if (Progress.IsDailyExpired() || Progress.DailyQuest == null)
        {
            GenerateNewDailyQuest();
            changed = true;
        }

        // Check weekly quest
        if (Progress.IsWeeklyExpired() || Progress.WeeklyQuest == null)
        {
            GenerateNewWeeklyQuest();
            changed = true;
        }

        if (changed)
        {
            _isDirty = true;
            Save();
        }
    }

    private void GenerateNewDailyQuest(string? excludeId = null)
    {
        var availableQuests = QuestDefinition.DailyQuests
            .Where(q => q.Id != excludeId)
            .ToList();

        if (availableQuests.Count == 0) return;

        var selectedQuest = availableQuests[_random.Next(availableQuests.Count)];

        Progress.DailyQuest = new ActiveQuest(selectedQuest.Id);
        Progress.DailyQuestGeneratedAt = DateTime.Now;

        App.Logger?.Information("Generated new daily quest: {QuestId}", selectedQuest.Id);
    }

    private void GenerateNewWeeklyQuest(string? excludeId = null)
    {
        var availableQuests = QuestDefinition.WeeklyQuests
            .Where(q => q.Id != excludeId)
            .ToList();

        if (availableQuests.Count == 0) return;

        var selectedQuest = availableQuests[_random.Next(availableQuests.Count)];

        Progress.WeeklyQuest = new ActiveQuest(selectedQuest.Id);
        Progress.WeeklyQuestGeneratedAt = DateTime.Now;

        App.Logger?.Information("Generated new weekly quest: {QuestId}", selectedQuest.Id);
    }

    private static DateTime GetStartOfWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Sunday)) % 7;
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
        return QuestDefinition.DailyQuests.FirstOrDefault(q => q.Id == Progress.DailyQuest.DefinitionId);
    }

    /// <summary>
    /// Get the definition for the current weekly quest
    /// </summary>
    public QuestDefinition? GetCurrentWeeklyDefinition()
    {
        if (Progress.WeeklyQuest == null) return null;
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
    /// Track bubble popped
    /// </summary>
    public void TrackBubblePopped()
    {
        UpdateQuestProgress(QuestCategory.Bubbles, 1);
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
        if (weeklyDef != null && weeklyDef.Category == category && Progress.WeeklyQuest?.IsCompleted == false)
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
        }
        else
        {
            Progress.TotalWeeklyQuestsCompleted++;
        }

        // Scale XP reward based on player level (+2% per level)
        var playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
        var scaledXP = (int)Math.Round(def.XPReward * (1 + playerLevel * 0.02));

        Progress.TotalXPFromQuests += scaledXP;

        _isDirty = true;
        Save();

        // Award XP (use a different source to avoid recursion with TrackXPEarned)
        App.Progression?.AddXP(scaledXP, XPSource.Other);

        // Play celebration effects
        PlayCompletionEffects();

        App.Logger?.Information("Quest completed: {QuestName} ({Type}) - Awarded {XP} XP (base: {BaseXP}, level: {Level})",
            def.Name, type, scaledXP, def.XPReward, playerLevel);

        // Fire event
        QuestCompleted?.Invoke(this, new QuestCompletedEventArgs(def, scaledXP, type));
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

            // Trigger haptic feedback (using achievement pattern - feels celebratory)
            _ = App.Haptics?.AchievementPatternAsync();
            _ = App.Haptics?.AchievementPatternAsync();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Error playing quest completion effects: {Error}", ex.Message);
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _saveTimer.Stop();
        if (_isDirty)
        {
            Save();
        }
    }

    #endregion
}
