using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Events;

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
    public const int PointsPerLevel = 1;

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
    /// Purchase a skill via server when online, or locally when offline.
    /// Returns (Success, Error) tuple.
    /// </summary>
    public async Task<(bool Success, string? Error)> PurchaseSkillAsync(string skillId)
    {
        if (!CanPurchaseSkill(skillId)) return (false, Loc.Get("skill_err_cannot_purchase"));

        var settings = App.Settings?.Current;
        if (settings == null) return (false, Loc.Get("skill_err_settings_unavailable"));

        var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
        if (skill == null) return (false, Loc.Get("skill_err_unknown"));

        // Require login — purchases are server-authoritative
        var unifiedId = settings.UnifiedId;
        if (string.IsNullOrEmpty(unifiedId) || App.ProfileSync == null)
            return (false, Loc.Get("skill_err_login_required"));

        var (success, error) = await App.ProfileSync.PurchaseSkillAsync(skillId);
        if (!success)
            return (false, error);

        // Server updated SkillPoints and UnlockedSkills via ProfileSyncService
        // Apply local-only effects (streak shields, pink rush timer)
        ApplySkillEffects(skillId);

        App.Logger?.Information("Skill purchased via server: {SkillId} for {Cost} points, {Remaining} remaining",
            skillId, skill.Cost, settings.SkillPoints);

        SkillUnlocked?.Invoke(this, skillId);
        return (true, null);
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

            case "oopsie_insurance":
                // Grant the purchase's streak-fix charge locally so the UI reflects it immediately.
                // The server grants the same charge on its side and is authoritative on next sync.
                // Saved here because nothing else on the purchase path persists settings — without
                // it the granted charge was lost on the next restart (the quests tab repaints off
                // the settings INPC, which the assignment above already raises).
                settings.StreakFixCharges++;
                App.Settings?.Save();
                break;

            case "pink_rush":
                // Start checking for Pink Rush triggers
                _pinkRushCheckTimer.Start();
                break;
        }
    }

    /// <summary>
    /// Check if a specific skill is unlocked.
    /// </summary>
    public bool HasSkill(string skillId)
    {
        var settings = App.Settings?.Current;
        if (settings == null) return false;
        return settings.UnlockedSkills.Contains(skillId);
    }

    /// <summary>
    /// Returns the highest sparkle boost tier unlocked (0 = none, 1-3 = tier).
    /// </summary>
    public int GetSparkleBoostTier()
    {
        if (HasSkill("sparkle_boost_3")) return 3;
        if (HasSkill("sparkle_boost_2")) return 2;
        if (HasSkill("sparkle_boost_1")) return 1;
        return 0;
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

        // World event boost (additive, capped, and 0.0 unless an event is running).
        // ADDITIVE ON PURPOSE: the one multiplicative term below already triples
        // whatever it lands on, and an event that multiplied instead of added would
        // stack with it into a number nobody costed. Capped inside LiveEventService
        // (MaxXpBoost) so a bad server value can nudge the rate, never mint a level.
        // Dormant in this build — nothing arms LiveEventService, so this adds +0.0.
        multiplier += LiveEventService.ClampXpBoost(App.LiveEvent?.XpBoost ?? 0.0);

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

        breakdown.Add((Loc.Get("skill_mult_base"), 1.0));

        if (HasSkill("sparkle_boost_1"))
            breakdown.Add((Loc.Get("skill_mult_sparkle_boost"), 0.10));
        if (HasSkill("sparkle_boost_2"))
            breakdown.Add((Loc.Get("skill_mult_extra_sparkly"), 0.15));
        if (HasSkill("sparkle_boost_3"))
            breakdown.Add((Loc.Get("skill_mult_maximum_sparkle"), 0.20));

        if (HasSkill("streak_power") && settings.CurrentStreak > 0)
        {
            var streakBonus = Math.Min(settings.CurrentStreak * 0.005, 0.15);
            breakdown.Add((Loc.GetF("skill_mult_streak_power_days", settings.CurrentStreak), streakBonus));
        }

        var hour = DateTime.Now.Hour;
        if (HasSkill("night_shift") && (hour >= 23 || hour < 5))
            breakdown.Add((Loc.Get("skill_mult_night_shift"), 0.50));
        if (HasSkill("early_bird_bimbo") && hour >= 5 && hour < 8)
            breakdown.Add((Loc.Get("skill_mult_early_bird"), 0.50));

        // Kept in step with GetTotalXpMultiplier above. Omitted entirely when no event
        // is running, which is every build until the event engine ships — an "Event +0%"
        // row would advertise a feature that does not exist yet.
        var eventBoost = LiveEventService.ClampXpBoost(App.LiveEvent?.XpBoost ?? 0.0);
        if (eventBoost > 0.0)
            breakdown.Add((Loc.Get("skill_mult_event_boost"), eventBoost));

        if (settings.PinkRushActive && HasSkill("pink_rush"))
            breakdown.Add((Loc.Get("skill_mult_pink_rush_active"), 2.0)); // Shows as +200% (3x total)

        return breakdown;
    }

    #endregion

    #region Lucky Procs

    /// <summary>
    /// Roll for lucky flash (5% chance for 10x XP)
    /// Returns the multiplier (1 for normal, 10 for lucky)
    /// </summary>
    public int RollLuckyFlash()
    {
        if (!HasSkill("lucky_bimbo")) return 1;

        if (_random.NextDouble() < 0.05)
        {
            LuckyProc?.Invoke(this, new LuckyProcEventArgs(App.Mods?.GetLuckyFlashLabel() ?? Loc.Get("skill_lucky_flash"), 10));
            return 10;
        }
        return 1;
    }

    /// <summary>
    /// Roll for lucky bubble (5% chance for 20x points)
    /// Returns the multiplier (1 for normal, 20 for lucky)
    /// </summary>
    public int RollLuckyBubble()
    {
        if (!HasSkill("lucky_bubbles")) return 1;

        if (_random.NextDouble() < 0.05)
        {
            LuckyProc?.Invoke(this, new LuckyProcEventArgs(App.Mods?.GetLuckyBubbleLabel() ?? Loc.Get("skill_lucky_bubble"), 20));
            return 20;
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
    /// Spend one streak-fix charge to restore a broken streak (free — charges are the currency).
    /// This is the automatic trigger from AchievementProgress.UpdateDailyStreak().
    /// For the manual button, MainWindow uses ProfileSyncService.UseOopsieInsuranceAsync() directly.
    /// The "oopsie_insurance" skill gate stays on this path deliberately: everyone earns charges, but
    /// only skill owners have them spent automatically — everyone else banks them and picks the day.
    /// Falls back to local-only if offline (acceptable for passive auto-trigger).
    /// </summary>
    public bool UseOopsieInsurance()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return false;

        if (!HasSkill("oopsie_insurance")) return false;
        // Spends down to the last charge, deliberately. An earlier revision reserved one for the
        // user to spend manually, to stop the skill from eating everything a skill owner banks.
        // That reserve was wrong: this path repairs the LOGIN streak (ConsecutiveDays), while the
        // manual Fix Day button repairs the QUEST streak (DailyQuestCompletionDates ->
        // DailyQuestStreak) and has no way to restore ConsecutiveDays. So a reserved charge could
        // never buy back what this path protects — it just let a login streak die with charges in
        // the bank. Better to spend the last one than to lose a streak that nothing can recover.
        if (settings.StreakFixCharges < 1) return false;

        // Deduct locally first so the balance is correct offline and so the fire-and-forget below
        // cannot race it: the server call adopts the authoritative balance when it answers, and
        // ordering it after this assignment means it can only ever correct, never double-spend.
        settings.StreakFixCharges = Math.Max(0, settings.StreakFixCharges - 1);
        settings.SeasonalStreakRecoveryUsed = true; // back-compat flag, no longer a gate

        // Try server-side validation if online
        var unifiedId = settings.UnifiedId;
        if (!string.IsNullOrEmpty(unifiedId) && App.ProfileSync != null)
        {
            // Fire-and-forget server call for the auto-trigger (non-blocking)
            _ = Task.Run(async () =>
            {
                try
                {
                    // Invariant like every other wire day key — the default format writes the
                    // Buddhist/Umm al-Qura year under those system calendars.
                    var today = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    var (ok, _, _, credits) = await App.ProfileSync.UseOopsieInsuranceAsync(today);
                    var current = App.Settings?.Current;
                    if (ok && credits.HasValue && current != null && current.StreakFixCharges != credits.Value)
                    {
                        // Server balance wins over the optimistic local decrement (it also sees
                        // grants/spends from other devices). The assignment raises the settings
                        // INPC, which repaints the quests tab.
                        current.StreakFixCharges = credits.Value;
                        App.Settings?.Save();
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Auto oopsie server sync failed (local fallback used): {Error}", ex.Message);
                }
            });
        }

        App.Logger?.Information("Streak fix auto-spent! Streak restored, {Remaining} charge(s) left",
            settings.StreakFixCharges);
        App.Settings?.Save();

        return true;
    }

    /// <summary>
    /// Get daily welcome bonus XP based on current streak length.
    /// Flat XP by streak tier: 50 (1-3d), 100 (4-6d), 150 (7-13d), 200 (14-29d), 300 (30+d).
    ///
    /// XP-economy rework (feat/xp-economy, shared with mobile): the bonus is for EVERYONE —
    /// the old milestone_rewards skill gate is gone (showing up every day is the behaviour
    /// being paid for, not a perk to buy), and so is the +3%/level scaling — a daily login
    /// must not be worth thousands of XP at high level while sessions do the real earning.
    /// The milestone_rewards node itself still exists in the tree as a prerequisite step.
    /// </summary>
    public int GetDailyStreakBonus(int streakDays)
    {
        if (streakDays <= 0) return 0;

        var xp = streakDays switch
        {
            <= 3 => 50,
            <= 6 => 100,
            <= 13 => 150,
            <= 29 => 200,
            _ => 300
        };

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
                Name = Loc.GetF("skill_streak_bonus_name", streakDays),
                FlavorText = Loc.GetF("skill_streak_bonus_flavor", xpAwarded, streakDays),
                ImageName = "../skills/milestone_rewards.png",
                Category = Models.AchievementCategory.TimeSessions
            };

            // Delay popup slightly so the main window has time to finish loading
            DispatcherHelper.RunOnUI(() =>
            {
                try
                {
                    var popup = new AchievementPopup(fakeAchievement, headerIcon: "🎁", headerText: Loc.Get("skill_daily_streak_bonus_header"));
                    popup.Show();
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Failed to show daily streak notification");
                }
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to create daily streak notification");
        }
    }

    /// <summary>
    /// Check and award Perfect Bimbo Week bonus (7, 14, 30 day daily quest streaks)
    /// Base XP: 3000, 6000, 10000 for 7/14/30 day milestones, scaled by level (+2% per level)
    ///
    /// GRANTS the XP itself and returns the amount awarded (0 when no milestone is due) — callers
    /// must not add it again. The grant lives in here so it happens before the paid-once latch is
    /// persisted; see the note at the grant.
    /// </summary>
    public int CheckPerfectWeekBonus()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return 0;

        if (!HasSkill("perfect_bimbo_week")) return 0;

        var streak = settings.DailyQuestStreak;
        var playerLevel = settings.PlayerLevel;

        // The streak broke and is being rebuilt, so the same milestone can be earned again.
        // Persisted immediately: nothing else on this path saves settings when no bonus is due.
        if (streak < settings.LastPerfectWeekStreakAwarded)
        {
            settings.LastPerfectWeekStreakAwarded = 0;
            App.Settings?.Save();
        }

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

        // Pay each milestone once. This runs on EVERY daily quest completion, so without the
        // latch the bonus was awarded again for every further quest that day - and again on the
        // next launch, since the streak value itself hadn't changed (#895).
        if (settings.LastPerfectWeekStreakAwarded == streak)
        {
            App.Logger?.Debug("Perfect Bimbo Week {Milestone} bonus already awarded for streak {Streak}; skipping",
                milestone, streak);
            return 0;
        }

        // Scale with player level (+2% per level)
        var scaledXP = (int)Math.Round(baseXP * (1 + playerLevel * 0.02));

        // GRANT FIRST, LATCH SECOND. The latch used to be written (and saved) here before the
        // caller added the XP, so a crash in between burned the milestone permanently. Ordered
        // this way a crash between the grant and the latch can at worst pay the milestone twice
        // — the lesser evil against a bonus the player can never earn again.
        App.Progression?.AddXP(scaledXP, XPSource.Other);

        settings.LastPerfectWeekStreakAwarded = streak;
        App.Settings?.Save();

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
                Name = Loc.Get("skill_perfect_week_name_popup"),
                FlavorText = Loc.GetF("skill_perfect_week_flavor_popup", streak, xpAwarded),
                ImageName = "../skills/perfect_bimbo_week.png",
                Category = Models.AchievementCategory.TimeSessions
            };

            // Show popup using the same system as achievements
            DispatcherHelper.RunOnUISync(() =>
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
        // Mirror into the per-season counter for the Season Recap card (local-only).
        SeasonRecapService.AddConditioningMinutes(minutes);
        App.Settings?.Save();
    }

    /// <summary>
    /// Get formatted total conditioning time for display
    /// </summary>
    public string GetFormattedConditioningTime()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return Loc.Get("skill_time_zero");

        var totalMinutes = settings.TotalConditioningMinutes;
        var hours = (int)(totalMinutes / 60);
        var minutes = (int)(totalMinutes % 60);

        if (hours >= 24)
        {
            var days = hours / 24;
            hours = hours % 24;
            return Loc.GetF("skill_time_dhm", days, hours, minutes);
        }

        return Loc.GetF("skill_time_hm", hours, minutes);
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

        // Season Rewind: remember the season's peak level (PlayerLevel itself is wiped at rollover)
        settings.SeasonPeakLevel = Math.Max(settings.SeasonPeakLevel, newLevel);

        App.Logger?.Information("Level up to {Level}! Awarded {Points} skill points. Total: {Total}",
            newLevel, pointsAwarded, settings.SkillPoints);

        App.Settings?.Save();
    }

    #endregion

    #region Season Reset

    /// <summary>
    /// Mirror the server's seasonal reset locally (called from ProfileSyncService when a
    /// level_reset arrives). THIS NO LONGER TOUCHES THE SKILL TREE. Not one purchased node is
    /// dropped by it, whatever the server sends.
    ///
    /// <para>It used to prune the tree to SkillDefinition.PermanentIds and leave the point
    /// balance alone, so the mechanical nodes had to be re-bought every month and that re-buy
    /// was the Prestige loop. The Descent ended monthly seasons on
    /// <see cref="Services.Descent.DescentEpochs.SeasonsEndUtc"/>, the server suppresses the wipe
    /// permanently, and the ceremony promised in as many words that nothing was wiped and nothing
    /// will be. A prune that only ever fires when that promise has already been broken is not a
    /// safety net, it is the second half of the accident, so it is gone.</para>
    ///
    /// <para>The method and its call sites are kept intact on purpose. The server can still emit
    /// the flag from an old deploy or a replayed response, and there is real work left in here:
    /// the streak and daily-quest latches genuinely do belong to a reset. Keeping the entry point
    /// means that path stays wired and observable rather than silently unhandled, while the tree
    /// it used to cut is simply no longer in scope.</para>
    /// </summary>
    public void OnSeasonReset()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        // Reset seasonal flags. NOTE: StreakFixCharges is deliberately NOT touched here — the
        // fix charges are cumulable, never expire, and are server-authoritative (the server grants
        // +1 on rollover and the next sync adopts the new balance).
        settings.SeasonalStreakRecoveryUsed = false;
        settings.CurrentStreak = 0;
        settings.LastStreakDate = null;
        settings.DailyQuestStreak = 0;
        settings.LastDailyQuestDate = null;
        // The Perfect Week latch tracks "already paid for streak N" — zeroing the streak without
        // it would make the rebuilt streak's first milestone look already-paid.
        settings.LastPerfectWeekStreakAwarded = 0;

        // THE TREE IS NOT TOUCHED. Every skill is permanent now, so there is nothing here to
        // prune and no live effect to tear down: pink_rush and good_girl_streak stay owned, so
        // their timers and shields stay exactly as the player left them. The count is logged so
        // that if this path ever fires again it is obvious from the log what it did and did not
        // do to the tree.
        var owned = settings.UnlockedSkills?.Count ?? 0;

        App.Logger?.Information("Season reset: seasonal flags cleared, skill tree left intact ({Owned} skill(s) kept; skills are permanent since the Descent)",
            owned);
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
