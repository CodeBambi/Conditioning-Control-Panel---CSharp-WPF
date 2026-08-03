using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Models;

/// <summary>
/// Tracks progress towards all achievements
/// </summary>
public class AchievementProgress
{
    // ========== UNLOCKED ACHIEVEMENTS ==========
    public HashSet<string> UnlockedAchievements { get; set; } = new();
    
    // ========== PROGRESSION STATS ==========
    // (Level is tracked in AppSettings.PlayerLevel)
    
    // ========== TIME TRACKING ==========
    /// <summary>Total minutes with Pink Filter active</summary>
    public double TotalPinkFilterMinutes { get; set; }
    
    /// <summary>Total minutes with Spiral Overlay active</summary>
    public double TotalSpiralMinutes { get; set; }
    
    /// <summary>Current continuous spiral minutes (resets when disabled)</summary>
    public double ContinuousSpiralMinutes { get; set; }
    
    /// <summary>Total flash images shown</summary>
    public int TotalFlashImages { get; set; }
    
    /// <summary>Consecutive days app was launched</summary>
    public int ConsecutiveDays { get; set; }
    
    /// <summary>Last date the app was launched (for streak tracking)</summary>
    public DateTime LastLaunchDate { get; set; }
    
    // ========== SESSION TRACKING ==========
    /// <summary>Whether Alt+Tab was pressed during current session</summary>
    public bool AltTabPressedThisSession { get; set; }
    
    /// <summary>Time when ESC/Panic was last pressed (for Relapse tracking)</summary>
    public DateTime? LastPanicPressTime { get; set; }
    
    /// <summary>Longest session completed in minutes</summary>
    public double LongestSessionMinutes { get; set; }
    
    // ========== MINIGAME STATS ==========
    /// <summary>Total bubbles popped</summary>
    public int TotalBubblesPopped { get; set; }
    
    /// <summary>Current streak of correct bubble count guesses</summary>
    public int BubbleCountCorrectStreak { get; set; }
    
    /// <summary>Best bubble count correct streak</summary>
    public int BubbleCountBestStreak { get; set; }
    
    /// <summary>Times attention check failed (for Mercy Beggar)</summary>
    public int AttentionCheckFailures { get; set; }
    
    /// <summary>Current continuous Mind Wipe seconds</summary>
    public double ContinuousMindWipeSeconds { get; set; }
    
    /// <summary>Has achieved 100% accuracy on a Lock Card</summary>
    public bool HasPerfectLockCard { get; set; }
    
    /// <summary>Fastest Lock Card completion time in seconds (3 phrases)</summary>
    public double FastestLockCardSeconds { get; set; } = double.MaxValue;

    /// <summary>Total minutes of video watched</summary>
    public double TotalVideoMinutes { get; set; }

    /// <summary>Total lock cards completed</summary>
    public int TotalLockCardsCompleted { get; set; }

    /// <summary>Whether bouncing text has hit a corner</summary>
    public bool HasHitCorner { get; set; }

    // ========== ATTENTION CHECK STATS ==========
    /// <summary>Total attention checks passed (all types)</summary>
    public int TotalAttentionChecksPassed { get; set; }

    /// <summary>Total video attention checks passed</summary>
    public int VideoAttentionChecksPassed { get; set; }

    /// <summary>Total video attention checks failed</summary>
    public int VideoAttentionChecksFailed { get; set; }

    // ========== BUBBLE COUNT STATS ==========
    /// <summary>Total bubble count games played</summary>
    public int TotalBubbleCountGames { get; set; }

    /// <summary>Total bubble count games completed correctly</summary>
    public int TotalBubbleCountCorrect { get; set; }

    /// <summary>Total bubble count games failed</summary>
    public int TotalBubbleCountFailed { get; set; }

    // ========== SESSION STATS ==========
    /// <summary>Total sessions started (may not be completed)</summary>
    public int TotalSessionsStarted { get; set; }

    /// <summary>Total sessions abandoned (started but not completed)</summary>
    public int TotalSessionsAbandoned { get; set; }

    // ========== XP & PROGRESSION STATS ==========
    /// <summary>All-time total XP earned (across all levels)</summary>
    public double TotalXPEarned { get; set; }

    /// <summary>All-time total skill points earned</summary>
    public int TotalSkillPointsEarned { get; set; }

    /// <summary>
    /// All-time sparkle points SPENT on enhancements — the Prestige metric. Monotonic:
    /// never reset by seasons, logout-safe via achievements.json, server-reconciled
    /// upward from lifetime_points_spent. Re-buying seasonal nodes raises it.
    /// </summary>
    public long LifetimeSkillPointsSpent { get; set; }
    
    /// <summary>Avatar click count for rapid clicking detection</summary>
    public int AvatarClickCount { get; set; }
    
    /// <summary>Time of first avatar click in current rapid sequence</summary>
    public DateTime? AvatarClickStartTime { get; set; }

    /// <summary>Click count toward the "needy doll" easter egg (150 clicks in 60 seconds)</summary>
    public int NeedyDollClickCount { get; set; }

    /// <summary>Start of the current needy-doll click window</summary>
    public DateTime? NeedyDollClickStartTime { get; set; }
    
    // ========== SESSION COMPLETION TRACKING ==========
    public HashSet<string> CompletedSessions { get; set; } = new();
    
    /// <summary>Sessions completed with specific conditions</summary>
    public bool CompletedGoodGirlsWithStrictLock { get; set; }
    public bool CompletedMorningDriftInMorning { get; set; }
    public bool CompletedGamerGirlNoAltTab { get; set; }
    public bool CompletedSessionWithNoPanic { get; set; }
    
    // ========== COMBINATION TRACKING ==========
    /// <summary>Has had Strict Lock + No Panic + Pink Filter all active</summary>
    public bool HasTotalLockdown { get; set; }

    /// <summary>Has had Bubbles + Bouncing Text + Spiral all active</summary>
    public bool HasSystemOverload { get; set; }

    // ========== GAMIFICATION BRIDGE STATS (achievements v2) ==========
    // Persisted lifetime counters fed by GamificationBridge subscriptions.

    /// <summary>Deeper enhancements played to completion (Phase 2)</summary>
    public int EnhancementsPlayed { get; set; }

    /// <summary>Total minutes spent in the Deeper player (Phase 2)</summary>
    public double DeeperMinutes { get; set; }

    /// <summary>Enhancements built/saved in the Deeper editor</summary>
    public int EnhancementsBuilt { get; set; }

    /// <summary>Mods installed (proxied by first activation today)</summary>
    public int ModsInstalled { get; set; }

    /// <summary>Distinct mod ids ever activated (for the Curator count)</summary>
    public HashSet<string> ActivatedModIds { get; set; } = new();

    /// <summary>Distinct community (non-builtin) mod ids activated (for Community Supported)</summary>
    public HashSet<string> CommunityModIds { get; set; } = new();

    /// <summary>Quiz category ids the user has perfected (for Honor Roll)</summary>
    public HashSet<string> PerfectedQuizCategories { get; set; } = new();

    /// <summary>Lifetime keyword triggers fired</summary>
    public int KeywordTriggersFired { get; set; }

    /// <summary>Messages the user has sent to the companion</summary>
    public int CompanionMessages { get; set; }

    /// <summary>Quizzes passed (Phase 2)</summary>
    public int QuizzesPassed { get; set; }

    /// <summary>Consecutive quizzes failed; resets to 0 on a pass (Phase 2)</summary>
    public int QuizFailStreak { get; set; }

    /// <summary>Blinks logged while the Blink Trainer is running</summary>
    public int BlinkTrainerBlinks { get; set; }

    /// <summary>Bubbles/flashes popped by gaze dwell (Phase 2 — needs GazePopped event)</summary>
    public int GazePops { get; set; }

    // ----- transient per-run trackers (not persisted) -----

    /// <summary>Remote commands received in the current remote session</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int RemoteCommandsThisSession { get; set; }

    /// <summary>Distinct Deeper trigger types fired during the current play (Phase 2)</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public HashSet<string> DistinctTriggerTypesThisPlay { get; set; } = new();

    // ========== HELPER METHODS ==========
    
    public bool IsUnlocked(string achievementId) => UnlockedAchievements.Contains(achievementId);
    
    public void Unlock(string achievementId)
    {
        if (!UnlockedAchievements.Contains(achievementId))
        {
            UnlockedAchievements.Add(achievementId);
        }
    }
    
    /// <summary>
    /// Whether a streak bonus needs to be awarded after SkillTree is initialized.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PendingStreakBonus { get; set; }

    /// <summary>
    /// Check and update consecutive days streak.
    /// Integrates streak shields, oopsie insurance, milestone rewards, and CurrentStreak sync.
    /// </summary>
    public void UpdateDailyStreak()
    {
        // Season Recap (local-only): mark today as an active day this season, and capture the
        // current streak as a season peak. Done BEFORE the same-day early-return below so a
        // relaunch on a day we already counted still records the peak (the end-of-method call
        // only fires when the streak actually changes).
        SeasonRecapService.MarkActiveToday();
        SeasonRecapService.TrackStreakPeak(ConsecutiveDays);

        var today = DateTime.Today;
        var lastDate = LastLaunchDate.Date;

        // No recorded launch history (LastLaunchDate == default/MinValue). This is either a
        // genuine first run OR a fresh/reset achievements.json after a reinstall, failed update
        // migration, or logout that cleared local data. We cannot tell the two apart here, and
        // the cloud profile hasn't loaded yet — so DON'T break the streak. Stamp today and defer
        // to LoadProfileAsync's take-higher restore (ProfileSyncService.cs:1096/1602), which pulls
        // the real ConsecutiveDays back from the server. A true new user simply starts at 1.
        // Without this guard the gap math sees a ~739771-day gap and resets the streak to 1,
        // which then risks being synced UP over the real cloud value (#344, #345, #331).
        if (lastDate == default)
        {
            App.Logger?.Information("Login streak: no local launch history (fresh/reset install) — deferring to cloud restore, not breaking streak");
            if (ConsecutiveDays < 1) ConsecutiveDays = 1;
            LastLaunchDate = today;
            SyncCurrentStreak();
            SeasonRecapService.TrackStreakPeak(ConsecutiveDays);
            return;
        }

        if (lastDate == today)
        {
            // Already launched today, no change
            return;
        }
        else if (lastDate == today.AddDays(-1))
        {
            // Launched yesterday, increment streak
            ConsecutiveDays++;
            PendingStreakBonus = true;
        }
        else
        {
            var daysMissed = (today - lastDate).Days;
            App.Logger?.Information("Login streak gap detected: {Days} day(s) missed (last launch: {LastDate}, today: {Today}, streak was: {Streak})",
                daysMissed, lastDate.ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"), ConsecutiveDays);

            // Streak would break - try streak shield first
            if (App.SkillTree?.UseStreakShield() == true)
            {
                // Shield saved the streak! Increment as normal
                ConsecutiveDays++;
                App.Logger?.Information("Streak shield protected streak! Now at {Days} days", ConsecutiveDays);
                PendingStreakBonus = true;

                // Record the missed day(s) that were shielded
                var settings = App.Settings?.Current;
                if (settings != null)
                {
                    for (var d = lastDate.AddDays(1); d < today; d = d.AddDays(1))
                    {
                        if (!settings.StreakShieldUsedDates.Contains(d.Date))
                            settings.StreakShieldUsedDates.Add(d.Date);
                    }
                }
            }
            else if (App.SkillTree?.UseOopsieInsurance() == true)
            {
                // A streak fix charge was spent automatically — keep current streak
                App.Logger?.Information("Oopsie Insurance auto-spent a streak fix, saving streak at {Days} days", ConsecutiveDays);
            }
            else
            {
                // Streak broken, reset to 1
                App.Logger?.Warning("Login streak RESET from {OldStreak} to 1 — gap of {Days} day(s), no shield/insurance available (last launch: {LastDate})",
                    ConsecutiveDays, daysMissed, lastDate.ToString("yyyy-MM-dd"));
                ConsecutiveDays = 1;
            }
        }

        LastLaunchDate = today;

        // Sync CurrentStreak in AppSettings with ConsecutiveDays
        SyncCurrentStreak();

        // Season Recap (local-only): keep the season peak streak. Tracked separately from
        // CurrentStreak because the server-driven season reset can zero CurrentStreak before
        // the recap snapshot runs — the peak must survive that.
        SeasonRecapService.TrackStreakPeak(ConsecutiveDays);
    }

    /// <summary>
    /// Pure decision function for the midnight-rollover path: given the last banked day and the
    /// current day, should the day-advance path run right now?
    ///
    /// Deliberately pure (no clock, no settings, no services) so the correctness argument is a
    /// property of two dates rather than of wall-clock timing, which cannot be tested by waiting.
    ///
    /// Returns false when:
    ///  - <paramref name="lastBankedDate"/> is default/MinValue. That is the "no local launch
    ///    history" case which <see cref="UpdateDailyStreak"/>'s startup path owns exclusively
    ///    (it defers to the cloud restore). The rollover path must never claim a first run.
    ///  - the day has not advanced (<c>today == lastBanked</c>) — today is already banked, so
    ///    running again would be the double-count we must never produce.
    ///  - the day went BACKWARDS (<c>today &lt; lastBanked</c>) — a clock correction or a
    ///    timezone change. Re-running the gap logic there would compute a negative gap and reset
    ///    a healthy streak to 1, so we simply stand still and let the next real day advance it.
    /// </summary>
    public static bool ShouldBankDayRollover(DateTime lastBankedDate, DateTime today)
    {
        var last = lastBankedDate.Date;
        if (last == default) return false;
        return today.Date > last;
    }

    /// <summary>
    /// Day-advance entry point for a calendar rollover that happens while the app is RUNNING
    /// (PC left asleep, or CCP simply left open across midnight). Both cases previously lost the
    /// day entirely because <see cref="LastLaunchDate"/> was only ever written on the startup path.
    ///
    /// This intentionally delegates to <see cref="UpdateDailyStreak"/> rather than reimplementing
    /// the day-advance: that keeps Streak Shields, Oopsie Insurance, the shielded-date bookkeeping
    /// and the Season Recap hooks on exactly one code path, and it means <see cref="LastLaunchDate"/>
    /// still has exactly one writer. Since UpdateDailyStreak early-returns when the day is already
    /// banked, startup and rollover can never both bank the same date.
    ///
    /// Returns true if a day was banked (caller is then responsible for persisting).
    /// </summary>
    public bool TryAdvanceDayRollover()
    {
        if (!ShouldBankDayRollover(LastLaunchDate, DateTime.Today)) return false;

        var before = LastLaunchDate.Date;
        UpdateDailyStreak();

        // UpdateDailyStreak is the sole writer of LastLaunchDate; if it did not move, nothing was
        // banked (defensive — with the guard above it always moves) and the caller should not save.
        return LastLaunchDate.Date != before;
    }

    /// <summary>
    /// Called after SkillTree is initialized to award streak bonus that was deferred during startup.
    /// </summary>
    public void AwardDeferredStreakBonus()
    {
        if (!PendingStreakBonus) return;
        PendingStreakBonus = false;

        var streakXP = App.SkillTree?.GetDailyStreakBonus(ConsecutiveDays) ?? 0;
        if (streakXP > 0)
        {
            App.Progression?.AddXP(streakXP, XPSource.Other);
            App.Logger?.Information("Daily streak bonus! {Days} days - awarded {XP} XP", ConsecutiveDays, streakXP);
        }
    }

    /// <summary>
    /// Sync AppSettings.CurrentStreak with this.ConsecutiveDays
    /// </summary>
    public void SyncCurrentStreak()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        settings.CurrentStreak = ConsecutiveDays;
        settings.LastStreakDate = LastLaunchDate;
    }
    
    /// <summary>
    /// Reset session-specific tracking
    /// </summary>
    public void ResetSessionTracking()
    {
        AltTabPressedThisSession = false;
    }
    
    /// <summary>
    /// Track avatar click for rapid clicking achievement
    /// </summary>
    public bool TrackAvatarClick()
    {
        var now = DateTime.Now;
        
        // 20 clicks in 10 seconds (instead of 5 - more achievable)
        if (AvatarClickStartTime == null || (now - AvatarClickStartTime.Value).TotalSeconds > 10)
        {
            // Start new sequence
            AvatarClickStartTime = now;
            AvatarClickCount = 1;
        }
        else
        {
            // Continue sequence
            AvatarClickCount++;
        }
        
        // Check if 20 clicks in 10 seconds
        return AvatarClickCount >= 20;
    }

    /// <summary>
    /// Track avatar click for the "needy doll" easter egg (150 clicks in 60 seconds).
    /// Independent window from the 20-in-10s neon-obsession tracker.
    /// </summary>
    public bool TrackNeedyDollClick()
    {
        var now = DateTime.Now;
        if (NeedyDollClickStartTime == null || (now - NeedyDollClickStartTime.Value).TotalSeconds > 60)
        {
            NeedyDollClickStartTime = now;
            NeedyDollClickCount = 1;
        }
        else
        {
            NeedyDollClickCount++;
        }
        return NeedyDollClickCount >= 150;
    }
}
