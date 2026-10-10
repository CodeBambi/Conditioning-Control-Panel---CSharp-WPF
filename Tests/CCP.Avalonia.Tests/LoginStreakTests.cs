using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>progression#42: the login streak advances, rolls over at midnight once, breaks on a gap
/// and mirrors itself into AppSettings (WPF AchievementProgressStreak).</summary>
public sealed class LoginStreakTests
{
    private static void WithSettings(Action<AppSettings> body)
    {
        var service = new SettingsService();
        var old = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        try { body(service.Current); }
        finally
        {
            CoreSettings.ServiceProvider = old;
            service.SealForReset();
        }
    }

    [Fact]
    public void Yesterday_ExtendsStreak_AndQueuesBonus()
    {
        WithSettings(s =>
        {
            var today = new DateTime(2026, 10, 9);
            var p = new AchievementProgress { ConsecutiveDays = 4, LastLaunchDate = today.AddDays(-1) };
            LoginStreak.UpdateDailyStreak(p, today);
            Assert.Equal(5, p.ConsecutiveDays);
            Assert.True(p.PendingStreakBonus);
            Assert.Equal(today, p.LastLaunchDate);
            Assert.Equal(5, s.CurrentStreak);
            // Same day again: no double count.
            LoginStreak.UpdateDailyStreak(p, today);
            Assert.Equal(5, p.ConsecutiveDays);
        });
    }

    [Fact]
    public void Gap_WithoutShieldOrInsurance_ResetsToOne()
    {
        WithSettings(s =>
        {
            s.StreakShieldsRemaining = 0;
            s.StreakFixCharges = 0;
            var today = new DateTime(2026, 10, 9);
            var p = new AchievementProgress { ConsecutiveDays = 12, LastLaunchDate = today.AddDays(-3) };
            LoginStreak.UpdateDailyStreak(p, today);
            Assert.Equal(1, p.ConsecutiveDays);
            Assert.Equal(today, p.LastLaunchDate);
        });
    }

    [Fact]
    public void Rollover_BanksOnlyForward_AndNeverClaimsFirstRun()
    {
        WithSettings(_ =>
        {
            var today = new DateTime(2026, 10, 9);
            var p = new AchievementProgress { ConsecutiveDays = 2, LastLaunchDate = today.AddDays(-1) };
            Assert.True(LoginStreak.TryAdvanceDayRollover(p, today));
            Assert.Equal(3, p.ConsecutiveDays);
            Assert.False(LoginStreak.TryAdvanceDayRollover(p, today));
            Assert.False(LoginStreak.TryAdvanceDayRollover(p, today.AddDays(-2)));   // clock went back
            Assert.False(LoginStreak.TryAdvanceDayRollover(new AchievementProgress(), today));
        });
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 50)]
    [InlineData(6, 100)]
    [InlineData(13, 150)]
    [InlineData(29, 200)]
    [InlineData(30, 300)]
    public void BonusTable_MatchesWpf(int days, int xp) => Assert.Equal(xp, LoginStreak.DailyStreakBonusXp(days));
}
