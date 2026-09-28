using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>ProgressionClear.Apply = the settings half of WPF MainWindow.ClearProgressionData.</summary>
public class ProgressionClearTests
{
    [Fact]
    public void ApplyClearsEveryProgressionFieldAndKeepsIdentity()
    {
        var day = new DateTime(2026, 9, 1);
        var s = new AppSettings
        {
            UnifiedId = "u_abc123",
            PlayerXP = 500, PlayerLevel = 40, SkillPoints = 3, UnlockedSkills = new List<string> { "a" },
            SeasonalStreakRecoveryUsed = true, StreakFixCharges = 2, HighestLevelEver = 41,
            DailyQuestStreak = 5, LastDailyQuestDate = day, LastPerfectWeekStreakAwarded = 7,
            MobileQuestDailyCompleted = 1, MobileQuestWeeklyCompleted = 2, MobileQuestXP = 30,
            StreakShieldsRemaining = 2, LastStreakShieldResetDate = day, StreakShieldUsedDates = new List<DateTime> { day },
            CurrentStreak = 4, LastStreakDate = day, HighestStreak = 9,
            NightTimeUsageCount = 6, EarlyMorningUsageCount = 8,
            FreeRerollsUsedToday = 1, LastRerollResetDate = day,
            IsSeason0Og = true, CurrentSeason = "2026-09", PatreonTier = 2,
            LastConfirmedServerXp = 12345, LastConfirmedServerXpAccount = "u_abc123", LastConfirmedServerXpSeason = "2026-09",
        };

        ProgressionClear.Apply(s);

        Assert.Equal(0, s.PlayerXP);
        Assert.Equal(1, s.PlayerLevel);
        Assert.Equal(0, s.SkillPoints);
        Assert.Empty(s.UnlockedSkills);
        Assert.False(s.SeasonalStreakRecoveryUsed);
        Assert.Equal(0, s.StreakFixCharges);
        Assert.Equal(0, s.HighestLevelEver);
        Assert.Equal(0, s.DailyQuestStreak);
        Assert.Null(s.LastDailyQuestDate);
        Assert.Equal(0, s.LastPerfectWeekStreakAwarded);
        Assert.Equal(0, s.MobileQuestDailyCompleted);
        Assert.Equal(0, s.MobileQuestWeeklyCompleted);
        Assert.Equal(0, s.MobileQuestXP);
        Assert.Equal(0, s.StreakShieldsRemaining);
        Assert.Null(s.LastStreakShieldResetDate);
        Assert.Empty(s.StreakShieldUsedDates);
        Assert.Equal(0, s.CurrentStreak);
        Assert.Null(s.LastStreakDate);
        Assert.Equal(0, s.HighestStreak);
        Assert.Equal(0, s.NightTimeUsageCount);
        Assert.Equal(0, s.EarlyMorningUsageCount);
        Assert.Equal(0, s.FreeRerollsUsedToday);
        Assert.Null(s.LastRerollResetDate);
        Assert.False(s.IsSeason0Og);
        Assert.Null(s.CurrentSeason);
        Assert.Equal(0, s.PatreonTier);
        Assert.Equal(0, s.LastConfirmedServerXp);
        Assert.Null(s.LastConfirmedServerXpAccount);
        Assert.Null(s.LastConfirmedServerXpSeason);
        Assert.Equal("u_abc123", s.UnifiedId);
    }
}
