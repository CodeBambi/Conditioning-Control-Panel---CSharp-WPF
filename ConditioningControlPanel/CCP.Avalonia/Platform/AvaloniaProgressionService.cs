using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Services.Companion;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia progression service that persists XP, levels, and skill points through
/// <see cref="ISettingsService"/> and applies the skill-tree XP multiplier.
/// Mirrors the legacy WPF level curve and session multiplier.
/// </summary>
public sealed class AvaloniaProgressionService : IProgressionService
{
    private readonly ISettingsService _settingsService;
    private readonly ISkillTreeService _skillTreeService;
    private readonly IServiceProvider _services;
    private readonly Dictionary<int, double> _cumulativeXPCache = new();

    public AvaloniaProgressionService(
        ISettingsService settingsService,
        ISkillTreeService skillTreeService,
        IServiceProvider services)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _skillTreeService = skillTreeService ?? throw new ArgumentNullException(nameof(skillTreeService));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public event EventHandler<int>? LevelUp;

    // WPF SkillTreeService.PointsPerLevel = 1 (verified: Services/Progression/SkillTreeService.cs).
    private const int PointsPerLevel = 1;

    /// <inheritdoc />
    public void AddXP(int amount, XPSource source)
    {
        // A10: a NEGATIVE amount is a penalty (e.g. attention-fail AddXP(-FailXpPenalty)) and
        // must apply. Only a zero delta is a no-op. Signature stays int to match IProgressionService.
        if (amount == 0) return;

        var settings = _settingsService.Current;
        if (settings == null) return;

        double multiplier = _skillTreeService.GetTotalXpMultiplier();
        double adjusted = amount * multiplier;
        settings.PlayerXP += adjusted;

        // Clamp so a penalty never drives XP (and therefore the level curve) negative.
        if (settings.PlayerXP < 0)
            settings.PlayerXP = 0;

        // Mirror WPF ProgressionService.AddXP: achievements track the skill-adjusted amount,
        // while companion and quests track the pre-multiplier BASE amount.
        // Resolve lazily to avoid a DI cycle (QuestService also consumes IProgressionService).
        try { _services.GetService<IAchievementService>()?.TrackXPEarned(adjusted); }
        catch (Exception) { /* stats must not break progression */ }
        try { _services.GetService<ICompanionService>()?.AddCompanionXP(amount, source); }
        catch (Exception) { /* companion must not break progression */ }
        try { _services.GetService<IQuestService>()?.TrackXPEarned((int)amount); }
        catch (Exception) { /* quests must not break progression */ }

        // Only positive XP can trigger a level-up; a non-positive delta never loops.
        if (amount > 0)
        {
            double xpNeeded = GetXPForLevel(settings.PlayerLevel);
            while (settings.PlayerXP >= xpNeeded)
            {
                settings.PlayerXP -= xpNeeded;
                settings.PlayerLevel++;

                // WPF SkillTreeService.OnLevelUp: +1 skill point per level gained (was 5x here).
                settings.SkillPoints += PointsPerLevel;

                if (settings.PlayerLevel > settings.HighestLevelEver)
                    settings.HighestLevelEver = settings.PlayerLevel;

                var achievements = _services.GetService<IAchievementService>();

                // WPF SkillTreeService.OnLevelUp: track skill points earned for stats.
                try { achievements?.TrackSkillPointsEarned(PointsPerLevel); }
                catch (Exception) { /* stats must not break progression */ }

                // WPF SkillTreeService.OnLevelUp: remember the season's peak level.
                settings.SeasonPeakLevel = Math.Max(settings.SeasonPeakLevel, settings.PlayerLevel);

                try { achievements?.CheckLevelAchievements(settings.PlayerLevel); }
                catch (Exception) { /* stats must not break progression */ }
                LevelUp?.Invoke(this, settings.PlayerLevel);
                xpNeeded = GetXPForLevel(settings.PlayerLevel);
            }
        }

        _settingsService.Save();
    }

    /// <inheritdoc />
    public double GetSessionXPMultiplier(int playerLevel)
    {
        if (playerLevel < 30) return 1.0;
        if (playerLevel < 80) return 1.0 + ((playerLevel - 30) * 0.01);   // 1.0x → 1.5x
        if (playerLevel < 125) return 1.5 + ((playerLevel - 80) * 0.02);  // 1.5x → 2.4x
        if (playerLevel < 150) return 2.4 + ((playerLevel - 125) * 0.03); // 2.4x → 3.15x
        return Math.Min(5.0, 3.15 + ((playerLevel - 150) * 0.03));         // 3.15x → 5.0x cap
    }

    /// <inheritdoc />
    public double GetXPForLevel(int level)
    {
        if (level <= 0) return 100.0;

        if (level <= 80)
        {
            // Linear growth from 800 to 2500.
            return Math.Round(800 + (level - 1) * (1700.0 / 79));
        }
        else if (level <= 100)
        {
            // Linear growth from 2500 to 4000.
            return Math.Round(2500 + (level - 80) * (1500.0 / 20));
        }
        else if (level <= 125)
        {
            // Linear growth from 4000 to 6000.
            return Math.Round(4000 + (level - 100) * (2000.0 / 25));
        }
        else if (level <= 150)
        {
            // Linear growth from 6000 to 10000.
            return Math.Round(6000 + (level - 125) * (4000.0 / 25));
        }
        else
        {
            // 3% compound growth per level beyond 150.
            return Math.Round(10000 * Math.Pow(1.03, level - 150));
        }
    }

    /// <inheritdoc />
    public double GetTotalXP(int level, double currentXP)
    {
        return GetCumulativeXPForLevel(level - 1) + currentXP;
    }

    /// <inheritdoc />
    public double GetCurrentLevelXP(int level, double totalXP)
    {
        var cumulativeForPreviousLevels = GetCumulativeXPForLevel(level - 1);
        return Math.Max(0, totalXP - cumulativeForPreviousLevels);
    }

    /// <summary>
    /// Gets the cumulative XP required to reach a given level (sum of all previous levels).
    /// Results are memoized for performance.
    /// </summary>
    private double GetCumulativeXPForLevel(int level)
    {
        if (level <= 0) return 0;

        if (_cumulativeXPCache.TryGetValue(level, out double cached))
            return cached;

        double cumulative = GetCumulativeXPForLevel(level - 1) + GetXPForLevel(level);
        _cumulativeXPCache[level] = cumulative;
        return cumulative;
    }
}
