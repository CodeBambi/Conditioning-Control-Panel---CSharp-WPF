using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Progression;

/// <summary>
/// Cross-platform surface for tracking achievement progress and unlocking achievements.
/// </summary>
public interface IAchievementService : IAsyncDisposable
{
    /// <summary>
    /// Current achievement progress and unlock state.
    /// </summary>
    AchievementProgress Progress { get; }

    /// <summary>
    /// Raised when an achievement is unlocked. Heads should subscribe to show a popup/toast.
    /// </summary>
    event EventHandler<Achievement>? AchievementUnlocked;

    /// <summary>
    /// When true, <see cref="TryUnlock"/> records achievements but suppresses popup notifications.
    /// Used during post-login sync to silently restore cloud achievements.
    /// </summary>
    bool SuppressPopups { get; set; }

    /// <summary>
    /// Attempt to unlock an achievement. Returns true if newly unlocked.
    /// </summary>
    bool TryUnlock(string achievementId);

    /// <summary>
    /// Generic feature-use tracking. The <paramref name="featureId"/> is head-specific.
    /// </summary>
    void TrackFeatureUsed(string featureId, double amount = 1);

    /// <summary>
    /// One-second tick for time-based achievement progress. Called by the internal scheduler.
    /// </summary>
    void TrackTimeProgress();

    /// <summary>
    /// Check and unlock level-based achievements.
    /// </summary>
    void CheckLevelAchievements(int level);

    /// <summary>
    /// Check the Daily Maintenance achievement (7 day streak).
    /// </summary>
    void CheckDailyMaintenance();

    /// <summary>
    /// Track that a flash image was shown.
    /// </summary>
    void TrackFlashImage();

    /// <summary>
    /// Track that a bubble was popped.
    /// </summary>
    void TrackBubblePopped();

    /// <summary>
    /// Credit a whole run's worth of popped bubbles at once (the DtRH web port reports its
    /// <c>bubblesPopped</c> total on run-end rather than per-pop). Same accounting as
    /// <see cref="TrackBubblePopped"/> but collapsed: one increment, every 100-bubble
    /// sparkle-point milestone crossed is granted in a single save, and at most one milestone
    /// popup fires so a big run can't spam notifications. Default no-op so existing fakes
    /// compile; the real service overrides.
    /// WPF parity: AchievementService.TrackBubblesPopped.
    /// </summary>
    void TrackBubblesPopped(int count) { }

    /// <summary>
    /// Track a bubble-count game result.
    /// </summary>
    void TrackBubbleCountResult(bool correct);

    /// <summary>
    /// Track a completed lock card.
    /// </summary>
    void TrackLockCardCompletion(double seconds, int totalChars, int errors, int phrases);

    /// <summary>
    /// Track video watch time in seconds.
    /// </summary>
    void TrackVideoWatched(double durationSeconds);

    /// <summary>
    /// Track a failed attention check.
    /// </summary>
    void TrackAttentionCheckFailed();

    /// <summary>
    /// Track Mind Wipe duration in seconds.
    /// </summary>
    void TrackMindWipeDuration(double seconds);

    /// <summary>
    /// Track that bouncing text hit a corner.
    /// </summary>
    void TrackCornerHit();

    /// <summary>
    /// Track an avatar click.
    /// </summary>
    void TrackAvatarClick();

    /// <summary>
    /// Track Alt+Tab during a session.
    /// </summary>
    void TrackAltTab();

    /// <summary>
    /// Track panic/ESC button press.
    /// </summary>
    void TrackPanicPressed();

    /// <summary>
    /// Track session start.
    /// </summary>
    void TrackSessionStart();

    /// <summary>
    /// Check for Relapse achievement (started within 10 seconds of panic/ESC).
    /// </summary>
    void CheckRelapse();

    /// <summary>
    /// Track session completion.
    /// </summary>
    void TrackSessionComplete(string sessionName, double durationMinutes, bool noPanicEnabled, bool strictLockEnabled);

    /// <summary>
    /// Track a passed attention check.
    /// </summary>
    void TrackAttentionCheckPassed(bool isVideo = false);

    /// <summary>
    /// Track a failed video attention check.
    /// </summary>
    void TrackVideoAttentionCheckFailed();

    /// <summary>
    /// Track that a bubble-count game was started.
    /// </summary>
    void TrackBubbleCountGameStarted();

    /// <summary>
    /// Track a bubble-count game success/failure for statistics.
    /// </summary>
    void TrackBubbleCountGameResult(bool success);

    /// <summary>
    /// Track that a session was started.
    /// </summary>
    void TrackSessionStarted();

    /// <summary>
    /// Track that a session was abandoned.
    /// </summary>
    void TrackSessionAbandoned();

    /// <summary>
    /// Track XP earned.
    /// </summary>
    void TrackXPEarned(double amount);

    /// <summary>
    /// Track skill points earned.
    /// </summary>
    void TrackSkillPointsEarned(int amount);

    /// <summary>
    /// Track skill points SPENT (Prestige): monotonic accumulate into LifetimeSkillPointsSpent.
    /// Default no-op so existing fakes compile; the real service overrides.
    /// WPF parity: AchievementService.TrackSkillPointsSpent.
    /// </summary>
    void TrackSkillPointsSpent(int amount) { }

    /// <summary>
    /// Adopt the server's authoritative lifetime_points_spent when it is ahead of ours
    /// (Prestige is monotonic; never lowers). Default no-op; the real service overrides.
    /// WPF parity: AchievementService.ReconcileLifetimePointsSpent.
    /// </summary>
    void ReconcileLifetimePointsSpent(long serverValue) { }

    /// <summary>
    /// Mark progress dirty so the next autosave persists it.
    /// </summary>
    void MarkDirty();

    /// <summary>
    /// Reset all achievement progress.
    /// </summary>
    void ResetProgress();

    /// <summary>
    /// Get total unlocked achievement count.
    /// </summary>
    int GetUnlockedCount();

    /// <summary>
    /// Get total earnable achievement count.
    /// </summary>
    int GetTotalCount();

    /// <summary>
    /// Get unlocked count filtered by exclusivity.
    /// </summary>
    int GetUnlockedCount(bool exclusive);

    /// <summary>
    /// Get total earnable count filtered by exclusivity.
    /// </summary>
    int GetTotalCount(bool exclusive);

    /// <summary>
    /// Whether the current user can earn patron-exclusive achievements.
    /// </summary>
    bool CanUnlockExclusive { get; }

    /// <summary>
    /// Entitlement-gated unlock for patron-exclusive achievements.
    /// </summary>
    bool TryUnlockExclusive(string achievementId);

    /// <summary>
    /// Persist progress immediately.
    /// </summary>
    void Save();
}
