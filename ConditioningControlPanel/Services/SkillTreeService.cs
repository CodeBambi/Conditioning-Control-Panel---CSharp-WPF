using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Service for managing the bimbo enhancement skill tree system.
/// Handles skill unlocking, bonus calculations, and all skill effects.
/// </summary>
public class SkillTreeService : IDisposable
{
    private readonly Random _random = new();
    private readonly DispatcherTimer _pinkRushTimer;
    private readonly DispatcherTimer _pinkRushCheckTimer;
    private bool _disposed;

    /// <summary>
    /// Points awarded per level up
    /// </summary>
    public const int PointsPerLevel = 5;

    /// <summary>
    /// Event fired when a skill is unlocked
    /// </summary>
    public event EventHandler<string>? SkillUnlocked;

    /// <summary>
    /// Event fired when Pink Rush activates
    /// </summary>
    public event EventHandler? PinkRushStarted;

    /// <summary>
    /// Event fired when Pink Rush ends
    /// </summary>
    public event EventHandler? PinkRushEnded;

    /// <summary>
    /// Event fired when a lucky proc occurs (flash or bubble)
    /// </summary>
    public event EventHandler<LuckyProcEventArgs>? LuckyProc;

    public SkillTreeService()
    {
        // Timer for ending Pink Rush windows
        _pinkRushTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pinkRushTimer.Tick += PinkRushTimer_Tick;

        // Timer for randomly triggering Pink Rush (if skill is unlocked)
        _pinkRushCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        _pinkRushCheckTimer.Tick += PinkRushCheckTimer_Tick;

        App.Logger?.Information("SkillTreeService initialized");
    }

    #region Skill Management

    /// <summary>
    /// Check if a skill can be purchased (has prereq, enough points, not already owned)
    /// </summary>
    public bool CanPurchaseSkill(string skillId)
    {
        var settings = App.Settings?.Current;
        if (settings == null) return false;

        var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
        if (skill == null) return false;

        // Already owned
        if (settings.UnlockedSkills.Contains(skillId)) return false;

        // Not enough points
        if (settings.SkillPoints < skill.Cost) return false;

        // Check prerequisite
        if (!string.IsNullOrEmpty(skill.PrerequisiteId))
        {
            if (!settings.UnlockedSkills.Contains(skill.PrerequisiteId))
                return false;
        }

        // Secret skills have special unlock requirements
        if (skill.IsSecret && !IsSecretSkillAvailable(skillId))
            return false;

        return true;
    }

    /// <summary>
    /// Check if a secret skill's unlock requirement has been met
    /// </summary>
    public bool IsSecretSkillAvailable(string skillId)
    {
        var settings = App.Settings?.Current;
        if (settings == null) return false;

        return skillId switch
        {
            "night_shift" => settings.NightTimeUsageCount >= 10,
            "early_bird_bimbo" => settings.EarlyMorningUsageCount >= 10,
            "eternal_doll" => settings.HighestLevelEver >= 50,
            _ => false
        };
    }

    /// <summary>
    /// Purchase a skill if possible
    /// </summary>
    public bool PurchaseSkill(string skillId)
    {
        if (!CanPurchaseSkill(skillId)) return false;

        var settings = App.Settings?.Current;
        if (settings == null) return false;

        var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
        if (skill == null) return false;

        settings.SkillPoints -= skill.Cost;
        settings.UnlockedSkills.Add(skillId);

        // Apply immediate effects for certain skills
        ApplySkillEffects(skillId);

        App.Logger?.Information("Skill purchased: {SkillId} for {Cost} points, {Remaining} remaining",
            skillId, skill.Cost, settings.SkillPoints);

        SkillUnlocked?.Invoke(this, skillId);
        App.Settings?.Save();

        return true;
    }

    /// <summary>
    /// Apply immediate effects when a skill is purchased
    /// </summary>
    private void ApplySkillEffects(string skillId)
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        switch (skillId)
        {
            case "good_girl_streak":
                // Grant initial streak shield
                settings.StreakShieldsRemaining = 1;
                settings.LastStreakShieldResetDate = DateTime.UtcNow.Date;
                break;

            case "pink_rush":
                // Start checking for Pink Rush triggers
                _pinkRushCheckTimer.Start();
                break;
        }
    }

    /// <summary>
    /// Check if a specific skill is unlocked.
    /// OG users with OgLevelUnlockEnabled bypass all skill requirements.
    /// </summary>
    public bool HasSkill(string skillId)
    {
        var settings = App.Settings?.Current;
        if (settings == null) return false;
        if (settings.IsSeason0Og && settings.OgLevelUnlockEnabled) return true;
        return settings.UnlockedSkills.Contains(skillId);
    }

    /// <summary>
    /// Get all unlocked skills
    /// </summary>
    public List<SkillDefinition> GetUnlockedSkills()
    {
        var unlockedIds = App.Settings?.Current?.UnlockedSkills ?? new List<string>();
        return SkillDefinition.All.Where(s => unlockedIds.Contains(s.Id)).ToList();
    }

    /// <summary>
    /// Get all available skills (can be purchased now)
    /// </summary>
    public List<SkillDefinition> GetAvailableSkills()
    {
        return SkillDefinition.All.Where(s => CanPurchaseSkill(s.Id)).ToList();
    }

    #endregion

    #region XP Multiplier Calculation

    /// <summary>
    /// Calculate the total XP multiplier from all active skills
    /// </summary>
    public double GetTotalXpMultiplier()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return 1.0;

        double multiplier = 1.0;

        // Sparkle Boost skills (additive)
        if (HasSkill("sparkle_boost_1")) multiplier += 0.10;
        if (HasSkill("sparkle_boost_2")) multiplier += 0.15;
        if (HasSkill("sparkle_boost_3")) multiplier += 0.20;

        // Streak Power (0.5% per streak day, max 15%)
        if (HasSkill("streak_power"))
        {
            var streakBonus = Math.Min(settings.CurrentStreak * 0.005, 0.15);
            multiplier += streakBonus;
        }

        // Time-based bonuses
        var hour = DateTime.Now.Hour;

        // Night Shift (11pm-5am = 23:00-5:00)
        if (HasSkill("night_shift") && (hour >= 23 || hour < 5))
        {
            multiplier += 0.50;
        }

        // Early Bird (5am-8am)
        if (HasSkill("early_bird_bimbo") && hour >= 5 && hour < 8)
        {
            multiplier += 0.50;
        }

        // Pink Rush (active window)
        if (settings.PinkRushActive && HasSkill("pink_rush"))
        {
            multiplier *= 3.0; // 3x during Pink Rush
        }

        return multiplier;
    }

    /// <summary>
    /// Get multiplier breakdown for display
    /// </summary>
    public List<(string Source, double Value)> GetMultiplierBreakdown()
    {
        var breakdown = new List<(string Source, double Value)>();
        var settings = App.Settings?.Current;
        if (settings == null) return breakdown;

        breakdown.Add(("Base", 1.0));

        if (HasSkill("sparkle_boost_1"))
            breakdown.Add(("Sparkle Boost", 0.10));
        if (HasSkill("sparkle_boost_2"))
            breakdown.Add(("Extra Sparkly", 0.15));
        if (HasSkill("sparkle_boost_3"))
            breakdown.Add(("Maximum Sparkle", 0.20));

        if (HasSkill("streak_power") && settings.CurrentStreak > 0)
        {
            var streakBonus = Math.Min(settings.CurrentStreak * 0.005, 0.15);
            breakdown.Add(($"Streak Power ({settings.CurrentStreak} days)", streakBonus));
        }

        var hour = DateTime.Now.Hour;
        if (HasSkill("night_shift") && (hour >= 23 || hour < 5))
            breakdown.Add(("Night Shift", 0.50));
        if (HasSkill("early_bird_bimbo") && hour >= 5 && hour < 8)
            breakdown.Add(("Early Bird Bimbo", 0.50));

        if (settings.PinkRushActive && HasSkill("pink_rush"))
            breakdown.Add(("PINK RUSH ACTIVE!", 2.0)); // Shows as +200% (3x total)

        return breakdown;
    }

    #endregion

    #region Lucky Procs

    /// <summary>
    /// Roll for lucky flash (1% chance for 5x XP)
    /// Returns the multiplier (1 for normal, 5 for lucky)
    /// </summary>
    public int RollLuckyFlash()
    {
        if (!HasSkill("lucky_bimbo")) return 1;

        if (_random.NextDouble() < 0.01)
        {
            LuckyProc?.Invoke(this, new LuckyProcEventArgs("Lucky Flash", 5));
            return 5;
        }
        return 1;
    }

    /// <summary>
    /// Roll for lucky bubble (5% chance for 10x points)
    /// Returns the multiplier (1 for normal, 10 for lucky)
    /// </summary>
    public int RollLuckyBubble()
    {
        if (!HasSkill("lucky_bubbles")) return 1;

        if (_random.NextDouble() < 0.05)
        {
            LuckyProc?.Invoke(this, new LuckyProcEventArgs("Lucky Bubble", 10));
            return 10;
        }
        return 1;
    }

    #endregion

    #region Pink Rush

    private void PinkRushCheckTimer_Tick(object? sender, EventArgs e)
    {
        var settings = App.Settings?.Current;
        if (settings == null || !HasSkill("pink_rush")) return;

        // Don't trigger if already active
        if (settings.PinkRushActive) return;

        // 50% chance every 5 minutes (~once per 10 min)
        if (_random.NextDouble() < 0.50)
        {
            StartPinkRush();
        }
    }

    /// <summary>
    /// Start a Pink Rush bonus window (60 seconds of 3x XP)
    /// </summary>
    public void StartPinkRush()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        settings.PinkRushActive = true;
        settings.PinkRushEndTime = DateTime.Now.AddSeconds(60);

        _pinkRushTimer.Start();
        PinkRushStarted?.Invoke(this, EventArgs.Empty);

        App.Logger?.Information("Pink Rush activated! 60 seconds of 3x XP");
    }

    private void PinkRushTimer_Tick(object? sender, EventArgs e)
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        if (settings.PinkRushEndTime.HasValue && DateTime.Now >= settings.PinkRushEndTime.Value)
        {
            EndPinkRush();
        }
    }

    private void EndPinkRush()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        settings.PinkRushActive = false;
        settings.PinkRushEndTime = null;

        _pinkRushTimer.Stop();
        PinkRushEnded?.Invoke(this, EventArgs.Empty);

        App.Logger?.Information("Pink Rush ended");
    }

    #endregion

    #region Streak Management

    /// <summary>
    /// Use a streak shield to protect the streak
    /// Returns true if shield was available and used
    /// </summary>
    public bool UseStreakShield()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return false;

        if (!HasSkill("good_girl_streak")) return false;
        if (settings.StreakShieldsRemaining <= 0) return false;

        settings.StreakShieldsRemaining--;
        App.Logger?.Information("Streak shield used! {Remaining} remaining", settings.StreakShieldsRemaining);
        App.Settings?.Save();

        return true;
    }

    /// <summary>
    /// Reset weekly streak shields (call on week change)
    /// </summary>
    public void ResetWeeklyShields()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        if (!HasSkill("good_girl_streak")) return;

        settings.StreakShieldsRemaining = 1;
        settings.LastStreakShieldResetDate = DateTime.UtcNow.Date;
        App.Settings?.Save();

        App.Logger?.Information("Weekly streak shields reset");
    }

    /// <summary>
    /// Use oopsie insurance to restore a broken streak for 500 XP.
    /// This is the automatic trigger from AchievementProgress.UpdateDailyStreak().
    /// For the manual button, MainWindow uses ProfileSyncService.UseOopsieInsuranceAsync() directly.
    /// Falls back to local-only if offline (acceptable for passive auto-trigger).
    /// </summary>
    public bool UseOopsieInsurance()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return false;

        if (!HasSkill("oopsie_insurance")) return false;
        if (settings.SeasonalStreakRecoveryUsed) return false;
        if (settings.PlayerXP < 500) return false;

        // Try server-side validation if online
        var unifiedId = settings.UnifiedId;
        if (!string.IsNullOrEmpty(unifiedId) && App.ProfileSync != null)
        {
            // Fire-and-forget server call for the auto-trigger (non-blocking)
            // The local deduction happens immediately; server sync catches up
            _ = Task.Run(async () =>
            {
                try
                {
                    var today = DateTime.Today.ToString("yyyy-MM-dd");
                    await App.ProfileSync.UseOopsieInsuranceAsync(today);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Auto oopsie server sync failed (local fallback used): {Error}", ex.Message);
                }
            });
        }

        settings.PlayerXP -= 500;
        settings.SeasonalStreakRecoveryUsed = true;

        App.Logger?.Information("Oopsie Insurance used! Streak restored for 500 XP");
        App.Settings?.Save();

        return true;
    }

    /// <summary>
    /// Get daily welcome bonus XP based on current streak length.
    /// Base XP by streak tier: 50 (1-3d), 100 (4-6d), 150 (7-13d), 200 (14-29d), 300 (30+d)
    /// Scales with player level: +3% per level (e.g. level 50 = +150% = 2.5x)
    /// </summary>
    public int GetDailyStreakBonus(int streakDays)
    {
        if (!HasSkill("milestone_rewards")) return 0;
        if (streakDays <= 0) return 0;

        var baseXp = streakDays switch
        {
            <= 3 => 50,
            <= 6 => 100,
            <= 13 => 150,
            <= 29 => 200,
            _ => 300
        };

        // Scale with player level: +3% per level
        var level = App.Settings?.Current?.PlayerLevel ?? 1;
        var levelMultiplier = 1.0 + (level - 1) * 0.03;
        var xp = (int)Math.Round(baseXp * levelMultiplier);

        ShowDailyStreakNotification(xp, streakDays);
        return xp;
    }

    /// <summary>
    /// Show a popup notification for the daily streak bonus
    /// </summary>
    private void ShowDailyStreakNotification(int xpAwarded, int streakDays)
    {
        try
        {
            var fakeAchievement = new Models.Achievement
            {
                Id = "daily_streak_bonus",
                Name = $"Day {streakDays} Streak Bonus!",
                FlavorText = $"Welcome back! +{xpAwarded} XP for your {streakDays}-day streak~",
                ImageName = "../skills/milestone_rewards.png",
                Category = Models.AchievementCategory.TimeSessions
            };

            // Delay popup slightly so the main window has time to finish loading
            Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
            {
                try
                {
                    var popup = new AchievementPopup(fakeAchievement, headerIcon: "🎁", headerText: "DAILY STREAK BONUS!");
                    popup.Show();
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Failed to show daily streak notification");
                }
            });
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to create daily streak notification");
        }
    }

    /// <summary>
    /// Check and award Perfect Bimbo Week bonus (7, 14, 30 day daily quest streaks)
    /// Base XP: 3000, 6000, 10000 for 7/14/30 day milestones, scaled by level (+2% per level)
    /// </summary>
    public int CheckPerfectWeekBonus()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return 0;

        if (!HasSkill("perfect_bimbo_week")) return 0;

        var streak = settings.DailyQuestStreak;
        var playerLevel = settings.PlayerLevel;

        // Determine base XP reward based on milestone
        int baseXP = 0;
        string milestone = "";

        if (streak == 30)
        {
            baseXP = 10000;
            milestone = "30-Day";
        }
        else if (streak == 14)
        {
            baseXP = 6000;
            milestone = "14-Day";
        }
        else if (streak % 7 == 0 && streak >= 7)
        {
            baseXP = 3000;
            milestone = "7-Day";
        }

        if (baseXP == 0) return 0;

        // Scale with player level (+2% per level)
        var scaledXP = (int)Math.Round(baseXP * (1 + playerLevel * 0.02));

        App.Logger?.Information("Perfect Bimbo Week {Milestone} bonus awarded! {XP} XP (base: {BaseXP}, level: {Level})",
            milestone, scaledXP, baseXP, playerLevel);

        // Trigger notification
        ShowPerfectWeekNotification(scaledXP, streak);

        return scaledXP;
    }

    /// <summary>
    /// Show a notification popup for Perfect Week bonus
    /// </summary>
    private void ShowPerfectWeekNotification(int xpAwarded, int streak)
    {
        try
        {
            // Create a fake Achievement object for the notification
            var fakeAchievement = new Models.Achievement
            {
                Id = "perfect_week_bonus",
                Name = $"Perfect Princess Streak! 🎀",
                FlavorText = $"{streak} days in a row! You've earned {xpAwarded:N0} bonus XP! ✨",
                ImageName = "../skills/perfect_bimbo_week.png",
                Category = Models.AchievementCategory.TimeSessions
            };

            // Show popup using the same system as achievements
            Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    var popup = new AchievementPopup(fakeAchievement);
                    popup.Show();
                    App.Logger?.Information("Perfect Week notification shown");
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Failed to show Perfect Week notification");
                }
            });
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to create Perfect Week notification");
        }
    }

    #endregion

    #region Quest Rerolls

    /// <summary>
    /// Get total free rerolls available per day
    /// </summary>
    public int GetDailyFreeRerolls()
    {
        int total = 0;

        if (HasSkill("quest_refresh"))
            total += 1;

        if (HasSkill("reroll_addict"))
            total += 2;

        return total;
    }

    /// <summary>
    /// Get remaining free rerolls for today
    /// </summary>
    public int GetRemainingFreeRerolls()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return 0;

        // Check if we need to reset daily rerolls
        var today = DateTime.UtcNow.Date;
        if (settings.LastRerollResetDate == null || settings.LastRerollResetDate.Value.Date != today)
        {
            settings.FreeRerollsUsedToday = 0;
            settings.LastRerollResetDate = today;
            App.Settings?.Save();
        }

        return Math.Max(0, GetDailyFreeRerolls() - settings.FreeRerollsUsedToday);
    }

    /// <summary>
    /// Use a free reroll if available
    /// Returns true if a free reroll was used
    /// </summary>
    public bool UseFreeReroll()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return false;

        if (GetRemainingFreeRerolls() <= 0) return false;

        settings.FreeRerollsUsedToday++;
        App.Settings?.Save();

        App.Logger?.Information("Free reroll used. {Remaining} remaining", GetRemainingFreeRerolls());
        return true;
    }

    /// <summary>
    /// Get bonus XP multiplier for rerolled quests
    /// </summary>
    public double GetRerollBonusMultiplier()
    {
        if (HasSkill("better_quests"))
            return 1.25; // +25%
        return 1.0;
    }

    #endregion

    #region Time Tracking

    /// <summary>
    /// Track time-of-day usage for secret skill unlocks
    /// Call this when a session starts
    /// </summary>
    public void TrackTimeOfDayUsage()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        var hour = DateTime.Now.Hour;

        // Night time (11pm-5am)
        if (hour >= 23 || hour < 5)
        {
            settings.NightTimeUsageCount++;
            App.Logger?.Debug("Night time usage tracked: {Count}", settings.NightTimeUsageCount);
        }

        // Early morning (5am-8am)
        if (hour >= 5 && hour < 8)
        {
            settings.EarlyMorningUsageCount++;
            App.Logger?.Debug("Early morning usage tracked: {Count}", settings.EarlyMorningUsageCount);
        }

        App.Settings?.Save();
    }

    /// <summary>
    /// Add conditioning time (call periodically during sessions)
    /// </summary>
    public void AddConditioningTime(double minutes)
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        settings.TotalConditioningMinutes += minutes;
        App.Settings?.Save();
    }

    /// <summary>
    /// Get formatted total conditioning time for display
    /// </summary>
    public string GetFormattedConditioningTime()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return "0h 0m";

        var totalMinutes = settings.TotalConditioningMinutes;
        var hours = (int)(totalMinutes / 60);
        var minutes = (int)(totalMinutes % 60);

        if (hours >= 24)
        {
            var days = hours / 24;
            hours = hours % 24;
            return $"{days}d {hours}h {minutes}m";
        }

        return $"{hours}h {minutes}m";
    }

    #endregion

    #region Level Up Integration

    /// <summary>
    /// Award skill points for level up
    /// Call this from ProgressionService when player levels up
    /// </summary>
    public void OnLevelUp(int newLevel, int levelsGained = 1)
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        var pointsAwarded = levelsGained * PointsPerLevel;
        settings.SkillPoints += pointsAwarded;

        // Track total skill points earned (for stats)
        App.Achievements?.TrackSkillPointsEarned(pointsAwarded);

        App.Logger?.Information("Level up to {Level}! Awarded {Points} skill points. Total: {Total}",
            newLevel, pointsAwarded, settings.SkillPoints);

        App.Settings?.Save();
    }

    #endregion

    #region Season Reset

    /// <summary>
    /// Reset seasonal skill data (called on season change)
    /// Note: Skill points and unlocked skills persist across seasons
    /// </summary>
    public void OnSeasonReset()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        // Reset seasonal flags
        settings.SeasonalStreakRecoveryUsed = false;
        settings.CurrentStreak = 0;
        settings.LastStreakDate = null;
        settings.DailyQuestStreak = 0;
        settings.LastDailyQuestDate = null;

        App.Logger?.Information("Seasonal skill data reset");
        App.Settings?.Save();

        // Sync reset streak to server
        if (App.ProfileSync?.IsSyncEnabled == true)
        {
            _ = App.ProfileSync.SyncProfileAsync();
        }
    }

    #endregion

    public void Start()
    {
        // Start Pink Rush checker if skill is unlocked
        if (HasSkill("pink_rush"))
        {
            _pinkRushCheckTimer.Start();
        }

        // Reset weekly streak shields if 7+ days since last reset
        if (HasSkill("good_girl_streak"))
        {
            var settings = App.Settings?.Current;
            if (settings != null)
            {
                var daysSinceReset = (DateTime.UtcNow.Date - (settings.LastStreakShieldResetDate ?? DateTime.MinValue)).TotalDays;
                if (daysSinceReset >= 7)
                {
                    ResetWeeklyShields();
                }
            }
        }

        // Track time of day for secret skills
        TrackTimeOfDayUsage();
    }

    public void Stop()
    {
        _pinkRushTimer.Stop();
        _pinkRushCheckTimer.Stop();

        if (App.Settings?.Current?.PinkRushActive == true)
        {
            EndPinkRush();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        App.Logger?.Debug("SkillTreeService disposed");
    }
}

/// <summary>
/// Event args for lucky proc events
/// </summary>
public class LuckyProcEventArgs : EventArgs
{
    public string ProcType { get; }
    public int Multiplier { get; }

    public LuckyProcEventArgs(string procType, int multiplier)
    {
        ProcType = procType;
        Multiplier = multiplier;
    }
}
