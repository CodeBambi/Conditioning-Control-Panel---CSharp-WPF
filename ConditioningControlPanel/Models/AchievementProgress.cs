using System;
using System.Collections.Generic;

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
    
    /// <summary>Whether bouncing text has hit a corner</summary>
    public bool HasHitCorner { get; set; }
    
    /// <summary>Avatar click count for rapid clicking detection</summary>
    public int AvatarClickCount { get; set; }
    
    /// <summary>Time of first avatar click in current rapid sequence</summary>
    public DateTime? AvatarClickStartTime { get; set; }
    
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
    /// Check and update consecutive days streak
    /// </summary>
    public void UpdateDailyStreak()
    {
        var today = DateTime.Today;
        var lastDate = LastLaunchDate.Date;
        
        if (lastDate == today)
        {
            // Already launched today, no change
            return;
        }
        else if (lastDate == today.AddDays(-1))
        {
            // Launched yesterday, increment streak
            ConsecutiveDays++;
        }
        else
        {
            // Streak broken, reset to 1
            ConsecutiveDays = 1;
        }
        
        LastLaunchDate = today;
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
}
