using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Progression;

/// <summary>
/// Event args for <see cref="IQuestService.QuestCompleted"/>.
/// </summary>
public sealed class QuestCompletedEventArgs : EventArgs
{
    /// <summary>Localized quest name.</summary>
    public string QuestName { get; }

    /// <summary>XP awarded for completing the quest.</summary>
    public int XpAwarded { get; }

    /// <summary>Type of quest completed.</summary>
    public QuestType QuestType { get; }

    public QuestCompletedEventArgs(string questName, int xpAwarded, QuestType questType)
    {
        QuestName = questName;
        XpAwarded = xpAwarded;
        QuestType = questType;
    }
}

/// <summary>
/// Generates, persists, and tracks progress for daily and weekly quests.
/// </summary>
public interface IQuestService
{
    /// <summary>
    /// Current quest progress (active quests, reroll state, and statistics).
    /// </summary>
    QuestProgress Progress { get; }

    /// <summary>
    /// Raised whenever active quests or reroll state change.
    /// </summary>
    event EventHandler? QuestsChanged;

    /// <summary>
    /// Raised when a daily or weekly quest is completed and XP has been awarded.
    /// </summary>
    event EventHandler<QuestCompletedEventArgs>? QuestCompleted;

    /// <summary>
    /// Ensures daily and weekly quests have been generated for the current period.
    /// </summary>
    void EnsureGenerated();

    /// <summary>
    /// Rerolls the current daily quest if rerolls are available.
    /// </summary>
    /// <returns>True if a reroll was performed.</returns>
    bool RerollDaily();

    /// <summary>
    /// Rerolls the current weekly quest if rerolls are available.
    /// </summary>
    /// <returns>True if a reroll was performed.</returns>
    bool RerollWeekly();

    /// <summary>
    /// Adds progress to any active quest matching the given category.
    /// </summary>
    void AddProgress(QuestCategory category, int amount);

    /// <summary>
    /// Records that a mantra has been completed and applies quest progress.
    /// </summary>
    void TrackMantraCompleted();

    /// <summary>
    /// Records that a flash image was viewed.
    /// </summary>
    void TrackFlashImage();

    /// <summary>
    /// Records spiral overlay time in minutes.
    /// </summary>
    void TrackSpiralMinutes(double minutes);

    /// <summary>
    /// Records pink filter overlay time in minutes.
    /// </summary>
    void TrackPinkFilterMinutes(double minutes);

    /// <summary>
    /// Records brain-drain overlay time in minutes.
    /// </summary>
    void TrackBrainDrainMinutes(double minutes);

    /// <summary>
    /// Records that a bubble was popped.
    /// </summary>
    void TrackBubblePopped();

    /// <summary>
    /// Advance bubble-pop quests by a whole batch at once (DtRH web run reporting its total
    /// on completion). Default no-op so existing fakes compile; the real service overrides.
    /// WPF parity: QuestService.TrackBubblesPopped.
    /// </summary>
    void TrackBubblesPopped(int count) { }

    /// <summary>
    /// Records video watch time in minutes.
    /// </summary>
    void TrackVideoMinutes(double minutes);

    /// <summary>
    /// Records that a session was completed.
    /// </summary>
    void TrackSessionCompleted();

    /// <summary>
    /// Records that a lock card was completed.
    /// </summary>
    void TrackLockCardCompleted();

    /// <summary>
    /// Records that a bubble-count minigame was completed.
    /// </summary>
    void TrackBubbleCountCompleted();

    /// <summary>
    /// Records Bambi Takeover active time in minutes.
    /// </summary>
    void TrackAutonomyMinutes(double minutes);

    /// <summary>
    /// Records that a lockdown was completed.
    /// </summary>
    void TrackLockdownCompleted();

    /// <summary>
    /// Records that a remote-control command was received.
    /// </summary>
    void TrackRemoteCommand();

    /// <summary>
    /// Records that a keyword/OCR trigger fired.
    /// </summary>
    void TrackKeywordTrigger();

    /// <summary>
    /// Records that a blink was logged in the live blink trainer.
    /// </summary>
    void TrackBlinkTrainerBlink();

    /// <summary>
    /// Records XP earned (used by the conditioning_champion_w weekly quest).
    /// </summary>
    void TrackXPEarned(int xp);

    /// <summary>
    /// Records the current streak (used by the streak_keeper_w weekly quest).
    /// </summary>
    void TrackStreak(int currentStreak);

    /// <summary>
    /// Marks the current daily quest as completed, awards XP, and clears the slot
    /// so <see cref="EnsureGenerated"/> can create the next daily quest.
    /// </summary>
    void CompleteDailyQuest();

    /// <summary>
    /// Persist an Oopsie-Insurance streak fix: mark <paramref name="date"/> completed in quest
    /// progress and save to disk. Default no-op; the real service overrides. The caller debits
    /// XP only after this returns so a crash can't spend XP without persisting the fix.
    /// </summary>
    void FixStreakDay(System.DateTime date) { }

    /// <summary>
    /// Recompute the daily-quest streak from the completion calendar, repairing it UPWARD only
    /// (never lowers a stored streak). Called by ProfileSync after unioning cloud completion dates.
    /// Default no-op so existing fakes compile; the real service overrides.
    /// WPF parity: QuestService.RecalculateStreak.
    /// </summary>
    void RecalculateStreak() { }

    /// <summary>
    /// Force-regenerate the current daily quest (server-driven reset flag). Default no-op so
    /// existing fakes compile; the real service overrides.
    /// WPF parity: QuestService.ForceRegenerateDailyQuest.
    /// </summary>
    void ForceRegenerateDailyQuest() { }

    /// <summary>
    /// Force-regenerate the current weekly quest (server-driven reset flag); skips when the
    /// current weekly quest is still within this week. Default no-op so existing fakes compile;
    /// the real service overrides. WPF parity: QuestService.ForceRegenerateWeeklyQuest.
    /// </summary>
    void ForceRegenerateWeeklyQuest() { }

    /// <summary>
    /// Gets the number of daily rerolls still available this period.
    /// </summary>
    int GetRemainingDailyRerolls();

    /// <summary>
    /// Gets the number of weekly rerolls still available this period.
    /// </summary>
    int GetRemainingWeeklyRerolls();

    /// <summary>
    /// Gets the scaled XP reward for the current daily quest.
    /// </summary>
    int GetScaledDailyXp();

    /// <summary>
    /// Gets the scaled XP reward for the current weekly quest.
    /// </summary>
    int GetScaledWeeklyXp();
}
