using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Services;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia implementation of the skill-tree service.
/// Manages unlocked skills, XP multipliers, skill purchases, streak shields,
/// oopsie insurance, daily streak bonuses, and free rerolls from local settings.
/// Does not port the legacy WPF Pink Rush / Lucky Proc runtime effects.
/// </summary>
public sealed class AvaloniaSkillTreeService : ISkillTreeService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IServiceProvider? _services;
    private readonly ILogger<AvaloniaSkillTreeService>? _logger;

    // R3: debounce the per-second conditioning-time disk write. Minutes are applied to the
    // in-memory settings on every call (readers stay current) but the disk Save() is flushed at
    // most once per minute (or on Stop/Dispose) instead of a full settings write every 1s tick.
    private double _unsavedConditioningMinutes;
    private DateTime _lastConditioningFlushUtc = DateTime.UtcNow;
    private const double ConditioningFlushIntervalSeconds = 60.0;

    public AvaloniaSkillTreeService(ISettingsService settingsService, IServiceProvider? services = null, ILogger<AvaloniaSkillTreeService>? logger = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<string>? SkillUnlocked;

    /// <inheritdoc />
    public event EventHandler? PinkRushStarted;

    /// <inheritdoc />
    public bool HasSkill(string skillId)
    {
        return _settingsService.Current?.UnlockedSkills.Contains(skillId) == true;
    }

    /// <inheritdoc />
    public double GetTotalXpMultiplier()
    {
        var unlocked = _settingsService.Current?.UnlockedSkills;
        if (unlocked == null) return 1.0;

        double multiplier = 1.0;
        foreach (var skill in SkillDefinition.All)
        {
            if (unlocked.Contains(skill.Id) && skill.EffectType == SkillEffectType.XpMultiplier)
            {
                multiplier += skill.EffectValue;
            }
        }

        return multiplier;
    }

    /// <inheritdoc />
    public int TotalPointsSpent
    {
        get
        {
            var unlocked = _settingsService.Current?.UnlockedSkills;
            if (unlocked == null) return 0;

            return SkillDefinition.All
                .Where(s => unlocked.Contains(s.Id))
                .Sum(s => s.Cost);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// ProfileSync slice 7 (WPF SkillTreeService.cs:115-145): purchases are SERVER-authoritative
    /// and require a cloud account — there is no local purchase path. Local guards run first
    /// (WPF CanPurchaseSkill), then the purchase is delegated to
    /// <see cref="IProfileSyncService.PurchaseSkillAsync"/>, which DIRECT-SETs the server's
    /// returned post-purchase balance — that direct-set IS the local deduction. This service
    /// deducts NOTHING (a second deduction here would double-charge; a Math.Max would make
    /// skills free — the economy bug pinned by the slice-6 tests). The sync service also owns
    /// the prestige accrual (TrackSkillPointsSpent + SeasonRecap + ReconcileLifetimePointsSpent),
    /// so no tracking happens here either. Only local-only side effects (streak shields) and the
    /// <see cref="SkillUnlocked"/> event remain this service's job.
    /// </remarks>
    public async Task<(bool Success, string? Error)> PurchaseSkillAsync(string skillId)
    {
        var settings = _settingsService.Current;
        if (settings == null)
            return (false, "Settings are not available.");

        var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
        if (skill == null)
            return (false, "Unknown skill.");

        if (settings.UnlockedSkills.Contains(skillId))
            return (false, "Skill already unlocked.");

        if (!string.IsNullOrEmpty(skill.PrerequisiteId) && !settings.UnlockedSkills.Contains(skill.PrerequisiteId))
            return (false, "Prerequisite skill is not unlocked.");

        if (settings.SkillPoints < skill.Cost)
            return (false, "Not enough skill points.");

        if (skill.IsSecret && !IsSecretSkillAvailable(skillId, settings))
            return (false, "Secret skill requirement is not met.");

        // Server-authoritative purchase requires a cloud account (WPF parity: no offline path).
        var profileSync = _services?.GetService<IProfileSyncService>();
        if (string.IsNullOrEmpty(settings.UnifiedId) || profileSync == null)
            return (false, ConditioningControlPanel.Core.Localization.Loc.Get("skill_err_login_required"));

        var (success, error) = await profileSync.PurchaseSkillAsync(skillId);
        if (!success)
            return (false, error);

        // Server updated SkillPoints + UnlockedSkills (and saved). Apply local-only effects.
        ApplySkillEffects(skillId, settings);
        _settingsService.Save();

        SkillUnlocked?.Invoke(this, skillId);

        // §5 trigger: push the full profile after a successful purchase (WPF SkillTreeService.cs:130).
        // Fire-and-forget; the sync service's gate + cooldown make this harmless.
        _ = Task.Run(async () =>
        {
            try { await profileSync.SyncProfileAsync(); }
            catch (Exception ex) { _logger?.LogDebug("Post-purchase profile sync failed: {Error}", ex.Message); }
        });

        return (true, null);
    }

    private static void ApplySkillEffects(string skillId, AppSettings settings)
    {
        switch (skillId)
        {
            case "good_girl_streak":
                settings.StreakShieldsRemaining = 1;
                settings.LastStreakShieldResetDate = DateTime.UtcNow.Date;
                break;
        }
    }

    private static bool IsSecretSkillAvailable(string skillId, AppSettings settings)
    {
        return skillId switch
        {
            "night_shift" => settings.NightTimeUsageCount >= 10,
            "early_bird_bimbo" => settings.EarlyMorningUsageCount >= 10,
            "eternal_doll" => settings.HighestLevelEver >= 50,
            _ => false,
        };
    }

    /// <inheritdoc />
    public void Start()
    {
        var settings = _settingsService.Current;
        if (settings == null) return;

        // Reset weekly streak shields if 7+ days since last reset.
        if (HasSkill("good_girl_streak"))
        {
            var daysSinceReset = (DateTime.UtcNow.Date - (settings.LastStreakShieldResetDate ?? DateTime.MinValue)).TotalDays;
            if (daysSinceReset >= 7)
            {
                ResetWeeklyShields();
            }
        }

        // Track time-of-day usage for secret skill unlocks.
        TrackTimeOfDayUsage();
    }

    /// <inheritdoc />
    public void Stop()
    {
        // Persist any conditioning minutes accumulated since the last debounced flush.
        FlushConditioningTime();
    }

    public void Dispose() => Stop();

    #region Legacy Core stubs

    /// <inheritdoc />
    public bool UseStreakShield()
    {
        var settings = _settingsService.Current;
        if (settings == null) return false;
        if (!HasSkill("good_girl_streak")) return false;
        if (settings.StreakShieldsRemaining <= 0) return false;

        settings.StreakShieldsRemaining--;
        _settingsService.Save();
        return true;
    }

    private void ResetWeeklyShields()
    {
        var settings = _settingsService.Current;
        if (settings == null) return;
        if (!HasSkill("good_girl_streak")) return;

        settings.StreakShieldsRemaining = 1;
        settings.LastStreakShieldResetDate = DateTime.UtcNow.Date;
        _settingsService.Save();
    }

    /// <inheritdoc />
    public bool UseOopsieInsurance()
    {
        var settings = _settingsService.Current;
        if (settings == null) return false;
        if (!HasSkill("oopsie_insurance")) return false;
        if (settings.SeasonalStreakRecoveryUsed) return false;
        if (settings.PlayerXP < 500) return false;

        settings.PlayerXP -= 500;
        settings.SeasonalStreakRecoveryUsed = true;
        _settingsService.Save();
        return true;
    }

    /// <inheritdoc />
    public int GetDailyStreakBonus(int consecutiveDays)
    {
        if (!HasSkill("milestone_rewards")) return 0;
        if (consecutiveDays <= 0) return 0;

        var baseXp = consecutiveDays switch
        {
            <= 3 => 50,
            <= 6 => 100,
            <= 13 => 150,
            <= 29 => 200,
            _ => 300
        };

        var level = _settingsService.Current?.PlayerLevel ?? 1;
        var levelMultiplier = 1.0 + (level - 1) * 0.03;
        return (int)Math.Round(baseXp * levelMultiplier);
    }

    /// <inheritdoc />
    public int GetDailyFreeRerolls()
    {
        int total = 0;
        if (HasSkill("quest_refresh")) total += 1;
        if (HasSkill("reroll_addict")) total += 2;
        return total;
    }

    /// <inheritdoc />
    public void AddConditioningTime(double minutes)
    {
        var settings = _settingsService.Current;
        if (settings == null || minutes <= 0) return;

        // Apply to the in-memory model on every call so live readers stay current.
        settings.TotalConditioningMinutes += minutes;
        settings.SeasonConditioningMinutes += minutes;

        // R3: AddConditioningTime is called on a ~1s cadence during a session; a full settings
        // Save() every second is wasteful. Accumulate and flush the disk write at most once per
        // minute; Stop()/Dispose() flush the remainder so a graceful exit loses nothing.
        _unsavedConditioningMinutes += minutes;
        if ((DateTime.UtcNow - _lastConditioningFlushUtc).TotalSeconds >= ConditioningFlushIntervalSeconds)
        {
            FlushConditioningTime();
        }
    }

    private void FlushConditioningTime()
    {
        _lastConditioningFlushUtc = DateTime.UtcNow;
        if (_unsavedConditioningMinutes <= 0) return;
        _unsavedConditioningMinutes = 0;
        _settingsService.Save();
    }

    private void TrackTimeOfDayUsage()
    {
        var settings = _settingsService.Current;
        if (settings == null) return;

        var hour = DateTime.Now.Hour;
        if (hour >= 23 || hour < 5)
            settings.NightTimeUsageCount++;
        if (hour >= 5 && hour < 8)
            settings.EarlyMorningUsageCount++;

        _settingsService.Save();
    }

    /// <inheritdoc />
    public void TriggerPinkRush()
    {
        var settings = _settingsService.Current;
        if (settings == null) return;

        // Mirror the legacy Pink Rush activation: mark the window and notify listeners.
        settings.PinkRushActive = true;
        settings.PinkRushEndTime = DateTime.UtcNow.AddSeconds(60);
        _settingsService.Save();

        try
        {
            PinkRushStarted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "PinkRushStarted subscriber threw");
        }
    }

    /// <inheritdoc />
    public double GetRerollBonusMultiplier() => HasSkill("better_quests") ? 1.25 : 1.0;

    /// <inheritdoc />
    public int CheckPerfectWeekBonus()
    {
        var settings = _settingsService.Current;
        if (settings == null) return 0;
        if (!HasSkill("perfect_bimbo_week")) return 0;

        var streak = settings.DailyQuestStreak;
        var playerLevel = settings.PlayerLevel;

        int baseXP;
        if (streak == 30) baseXP = 10000;
        else if (streak == 14) baseXP = 6000;
        else if (streak % 7 == 0 && streak >= 7) baseXP = 3000;
        else return 0;

        // Scale with player level (+2% per level), mirroring WPF SkillTreeService.CheckPerfectWeekBonus.
        var scaledXP = (int)Math.Round(baseXP * (1 + playerLevel * 0.02));
        _logger?.LogInformation(
            "Perfect Bimbo Week bonus awarded! {XP} XP (base {BaseXP}, streak {Streak}, level {Level})",
            scaledXP, baseXP, streak, playerLevel);
        return scaledXP;
    }

    /// <inheritdoc />
    public void OnSeasonReset()
    {
        var settings = _settingsService.Current;
        if (settings == null) return;

        // Reset seasonal flags.
        settings.SeasonalStreakRecoveryUsed = false;
        settings.CurrentStreak = 0;
        settings.LastStreakDate = null;
        settings.DailyQuestStreak = 0;
        settings.LastDailyQuestDate = null;

        // Prune the tree to permanent nodes (fallback when the server didn't send the
        // post-rollover list). Mechanical/XP-economy nodes are removed and must be re-purchased —
        // that re-buy is the Prestige loop. Mirrors WPF SkillTreeService.OnSeasonReset.
        var owned = settings.UnlockedSkills ?? new List<string>();
        var kept = owned.Where(id => SkillDefinition.PermanentIds.Contains(id)).ToList();
        var removed = owned.Count - kept.Count;
        if (removed > 0)
        {
            settings.UnlockedSkills = kept;

            // Tear down live effects whose skills were just dropped.
            if (!HasSkill("pink_rush") && settings.PinkRushActive)
            {
                settings.PinkRushActive = false;
                settings.PinkRushEndTime = null;
            }
            if (!HasSkill("good_girl_streak"))
                settings.StreakShieldsRemaining = 0;
        }

        _logger?.LogInformation(
            "Season reset: seasonal flags cleared, {Removed} mechanical skill(s) removed, {Kept} permanent kept",
            removed, kept.Count);
        _settingsService.Save();
    }

    #endregion
}
