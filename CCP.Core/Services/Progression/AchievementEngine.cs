using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The head-free half of WPF <c>AchievementService</c>: owns the progress object and its
/// <see cref="AchievementStore"/>, unlocks, the dirty flag and the <see cref="Unlocked"/> event.
/// Timers, Track* counters, haptics, popups and dispatcher marshalling stay in the head.
///
/// <para><see cref="Unlocked"/> is raised synchronously on the caller's thread; the WPF wrapper
/// marshals it to the UI thread exactly as it did before (DispatcherHelper.RunOnUI).</para>
/// </summary>
internal sealed class AchievementEngine
{
    private readonly AchievementStore _store;
    private volatile bool _isDirty;

    public AchievementEngine(AchievementStore store)
    {
        _store = store;
        Progress = store.Load();
    }

    public AchievementProgress Progress { get; private set; }

    /// <summary>Set by any mutation; cleared by a save; re-armed by a failed write.</summary>
    public bool IsDirty { get => _isDirty; set => _isDirty = value; }

    /// <summary>
    /// When true, TryUnlock still records achievements but does not raise <see cref="Unlocked"/>.
    /// Used during post-login sync to silently restore cloud achievements.
    /// </summary>
    public bool SuppressPopups { get; set; }

    public event EventHandler<Achievement>? Unlocked;

    /// <summary>
    /// <see cref="AchievementStore.Write"/> holds the lock and lands the bytes atomically. A failed
    /// write re-arms the dirty flag so the next autosave tick retries.
    /// </summary>
    private void Write()
    {
        if (!_store.Write(() => Progress)) _isDirty = true;
    }

    public void Save()
    {
        _isDirty = false;
        Write();
    }

    /// <summary>The head's 30s autosave tick: write off the calling thread, only when dirty.</summary>
    public Task SaveIfDirtyAsync()
    {
        if (!_isDirty) return Task.CompletedTask;
        _isDirty = false;
        return Task.Run(Write);
    }

    /// <summary>Fresh, empty progress, persisted at once (logout).</summary>
    public void Reset()
    {
        Progress = new AchievementProgress();
        Save();
    }

    private static readonly ConcurrentDictionary<string, byte> _alreadyUnlockedNoted = new();

    /// <summary>True the first time an already-held achievement is re-asked for in this run.</summary>
    internal static bool FirstAlreadyUnlockedNote(string achievementId) =>
        _alreadyUnlockedNoted.TryAdd(achievementId, 0);

    /// <summary>Unlock, persist immediately, then raise <see cref="Unlocked"/> once unless suppressed.</summary>
    public bool TryUnlock(string achievementId)
    {
        if (Progress.IsUnlocked(achievementId))
        {
            // Once per id per run. Minute trackers and every bubble pop re-ask for an achievement
            // they already hold, which wrote a line a second and pushed the useful lines out of
            // bug reports (#1268 #1269).
            if (FirstAlreadyUnlockedNote(achievementId))
                Log.Debug("Achievement {Id} already unlocked (further repeats not logged)", achievementId);
            return false;
        }

        Log.Debug("TryUnlock called for: {Id}", achievementId);

        if (!Achievement.All.TryGetValue(achievementId, out var achievement))
        {
            Log.Warning("Unknown achievement ID: {Id}", achievementId);
            return false;
        }

        Progress.Unlock(achievementId);
        Save(); // Save immediately on unlock

        Log.Information("🏆 Achievement unlocked: {Name} (ID: {Id}){Suppressed}", achievement.Name, achievementId,
            SuppressPopups ? " (popup suppressed)" : "");

        if (SuppressPopups) return true;

        try
        {
            Unlocked?.Invoke(this, achievement);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to fire achievement event");
        }

        return true;
    }

    /// <summary>
    /// Whether the current user may EARN patron-exclusive achievements. Gates only new earns via
    /// <see cref="TryUnlockExclusive"/>; cloud restore uses the ungated <see cref="TryUnlock"/>, so a
    /// user who earned an exclusive and later downgraded keeps it.
    /// </summary>
    public static bool CanUnlockExclusive => CoreEntitlement.HasPremium;

    /// <summary>Unlock every level milestone at or below <paramref name="level"/> (was WPF
    /// AchievementService.CheckLevelAchievements; both heads call this one).</summary>
    public void CheckLevelAchievements(int level)
    {
        foreach (var (id, milestone) in AchievementRules.LevelMilestones)
            if (level >= milestone) TryUnlock(id);
    }

    /// <summary>WPF AchievementService.BubbleSaveEveryNPops (#1071): the pop count flushes every 50 pops.</summary>
    internal const int BubbleSaveEveryNPops = 50;

    /// <summary>WPF AchievementService.TrackBubblePopped (AchievementService.cs:596): the lifetime count,
    /// pop_the_thought, 1 Sparkle Point every 100 bubbles. Quest credit stays with the caller (the head
    /// already calls QuestService.TrackBubblePopped beside this).
    /// ponytail: no ShowBubbleMilestoneNotification popup.</summary>
    public void TrackBubblePopped() => TrackBubblesPopped(1);

    /// <summary>WPF TrackBubblesPopped (a whole run at once): every 100-boundary crossed pays in one save.</summary>
    public void TrackBubblesPopped(int count)
    {
        if (count <= 0) return;
        int before = Progress.TotalBubblesPopped;
        Progress.TotalBubblesPopped += count;
        int after = Progress.TotalBubblesPopped;
        _isDirty = true;

        if (after >= AchievementRules.PopTheThoughtBubbles) TryUnlock("pop_the_thought");

        var s = CoreSettings.Current;
        if (SkillPointsBank.CreditBubbleMilestones(s, before, after) > 0) CoreSettings.Save();

        if (count > 1 || after % BubbleSaveEveryNPops == 0) Save();
    }

    /// <summary>A bubble-count answer: the correct-answer streak (mathematicians_nightmare) and the totals.</summary>
    public void TrackBubbleCountResult(bool correct)
    {
        if (correct)
        {
            Progress.BubbleCountCorrectStreak++;
            if (Progress.BubbleCountCorrectStreak > Progress.BubbleCountBestStreak)
                Progress.BubbleCountBestStreak = Progress.BubbleCountCorrectStreak;
            if (Progress.BubbleCountCorrectStreak >= AchievementRules.MathematiciansNightmareStreak)
                TryUnlock("mathematicians_nightmare");
        }
        else
        {
            Progress.BubbleCountCorrectStreak = 0;
        }
        TrackBubbleCountGameResult(correct);
    }

    /// <summary>WPF AchievementService.TrackVideoWatched: minutes to the total, screen_time at 10 h.
    /// Returns the minutes so the caller can credit quests (WPF App.Quests.TrackVideoMinutes).</summary>
    public double TrackVideoWatched(double seconds)
    {
        if (seconds <= 0) return 0;
        var minutes = seconds / 60.0;
        Progress.TotalVideoMinutes += minutes;
        _isDirty = true;
        Save();
        if (Progress.TotalVideoMinutes >= AchievementRules.ScreenTimeVideoMinutes) TryUnlock("screen_time");
        return minutes;
    }

    /// <summary>WPF AchievementService.TrackMindWipeDuration: clean_slate at 60 s of continuous loop.</summary>
    public void TrackMindWipeDuration(double seconds)
    {
        Progress.ContinuousMindWipeSeconds = seconds;
        _isDirty = true;
        if (seconds >= 60) TryUnlock("clean_slate");
    }

    /// <summary>WPF AchievementService.TrackBubbleCountGameStarted.</summary>
    public void TrackBubbleCountGameStarted()
    {
        Progress.TotalBubbleCountGames++;
        _isDirty = true;
    }

    /// <summary>WPF AchievementService.TrackCornerHit: the first bouncing-text corner hit unlocks corner_hit.</summary>
    public void TrackCornerHit()
    {
        if (Progress.HasHitCorner) return;
        Progress.HasHitCorner = true;
        _isDirty = true;
        TryUnlock("corner_hit");
    }

    /// <summary>WPF TrackAttentionCheckPassed: totals and eyes_front.</summary>
    public void TrackAttentionCheckPassed(bool isVideo = false)
    {
        Progress.TotalAttentionChecksPassed++;
        if (isVideo) Progress.VideoAttentionChecksPassed++;
        _isDirty = true;
        if (Progress.TotalAttentionChecksPassed >= AchievementRules.EyesFrontAttentionChecks) TryUnlock("eyes_front");
    }

    /// <summary>WPF TrackAttentionCheckFailed: failures and mercy_beggar.</summary>
    public void TrackAttentionCheckFailed()
    {
        Progress.AttentionCheckFailures++;
        _isDirty = true;
        if (Progress.AttentionCheckFailures >= AchievementRules.MercyBeggarFailures) TryUnlock("mercy_beggar");
    }

    public void TrackVideoAttentionCheckFailed()
    {
        Progress.VideoAttentionChecksFailed++;
        _isDirty = true;
    }

    public void TrackBubbleCountGameResult(bool success)
    {
        if (success) Progress.TotalBubbleCountCorrect++;
        else Progress.TotalBubbleCountFailed++;
        _isDirty = true;
    }

    /// <summary>A graded run finished (WPF GamificationBridge.OnQuizCompleted, the QuizService.QuizCompleted
    /// subscriber): breaks the walk-out streak and feeds teachers_pet / held_back / top_of_the_class / honor_roll.</summary>
    public void TrackQuizCompleted(bool passed, bool perfect, string? category)
    {
        Progress.IntakeQuitStreak = 0;
        if (passed)
        {
            Progress.QuizFailStreak = 0;
            if (++Progress.QuizzesPassed >= AchievementRules.TeachersPetPasses) TryUnlockExclusive("teachers_pet");
        }
        else if (++Progress.QuizFailStreak >= AchievementRules.HeldBackFailStreak) TryUnlockExclusive("held_back");

        if (perfect)
        {
            TryUnlockExclusive("top_of_the_class");
            if (!string.IsNullOrEmpty(category) && Progress.PerfectedQuizCategories.Add(category)
                && Progress.PerfectedQuizCategories.Count >= AchievementRules.HonorRollCategories)
                TryUnlockExclusive("honor_roll");
        }
        _isDirty = true;
    }

    /// <summary>A graded run was walked out of (WPF GamificationBridge.OnQuizAbandoned).</summary>
    public void TrackQuizAbandoned()
    {
        if (++Progress.IntakeQuitStreak >= AchievementRules.HeldBackQuitStreak) TryUnlockExclusive("held_back");
        _isDirty = true;
    }

    /// <summary>
    /// Unlock count filtered by exclusivity. The free (false) and patron (true) counts are
    /// deliberately separate and must never be summed. An earned IsPremiumFeature badge counts
    /// unconditionally - it is a receipt, and a lapse never takes it back.
    /// </summary>
    public int GetUnlockedCount(bool exclusive)
    {
        var count = 0;
        foreach (var id in Progress.UnlockedAchievements)
            if (Achievement.All.TryGetValue(id, out var a) && a.IsExclusive == exclusive) count++;
        return count;
    }

    /// <summary>
    /// Total filtered by exclusivity. THE PREMIUM-PROGRAM RULE lives here: a locked
    /// IsPremiumFeature badge is in the total only for a user who could earn it; an unlocked one
    /// always. Parked (IsHidden) achievements are never counted.
    /// </summary>
    public int GetTotalCount(bool exclusive)
    {
        var hasPremium = CoreEntitlement.HasPremium;
        var count = 0;
        foreach (var a in Achievement.All.Values)
        {
            if (a.IsHidden || a.IsExclusive != exclusive) continue;
            if (a.IsPremiumFeature && !hasPremium && !Progress.IsUnlocked(a.Id)) continue;
            count++;
        }
        return count;
    }

    /// <summary>Blended pair for one-line surfaces (profile bubble); never for the tab's counters.</summary>
    public (int Unlocked, int Total) GetReachableCounts()
    {
        var hasPremium = CoreEntitlement.HasPremium;
        int unlocked = 0, total = 0;
        foreach (var a in Achievement.All.Values)
        {
            if (Progress.IsUnlocked(a.Id)) { unlocked++; total++; continue; }
            if (a.IsHidden) continue;
            if ((a.IsExclusive || a.IsPremiumFeature) && !hasPremium) continue;
            total++;
        }
        return (unlocked, total);
    }

    /// <summary>No-op (false) for non-entitled users; otherwise exactly <see cref="TryUnlock"/>.</summary>
    public bool TryUnlockExclusive(string achievementId)
    {
        if (Progress.IsUnlocked(achievementId)) return false;
        if (!CanUnlockExclusive)
        {
            Log.Debug("Exclusive achievement {Id} withheld — user not entitled", achievementId);
            return false;
        }
        return TryUnlock(achievementId);
    }
}
