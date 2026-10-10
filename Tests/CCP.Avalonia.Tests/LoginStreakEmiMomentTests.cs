using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// WPF 7.1.5 AchievementProgress.UpdateDailyStreak tells EMI: the ordinary day that kept the streak
/// is <c>streakKept</c> with the new number; a break is <c>streakBroken</c> with the number the
/// user HAD. Swaps the settings provider and the desk bus sink, so it runs alone.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class LoginStreakEmiMomentTests
{
    private static List<(string Moment, int Streak)> Run(Action<AppSettings> body)
    {
        var fired = new List<(string, int)>();
        var service = new SettingsService();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldSink = EmiDeskBus.Sink;
        var oldShield = CoreQuests.UseStreakShieldProvider;
        CoreSettings.ServiceProvider = () => service;
        CoreQuests.UseStreakShieldProvider = null;
        EmiDeskBus.Sink = (moment, ctx) => fired.Add((moment, (int)(ctx?.GetType().GetProperty("streak")?.GetValue(ctx) ?? -1)));
        try { body(service.Current); }
        finally
        {
            EmiDeskBus.Sink = oldSink;
            CoreQuests.UseStreakShieldProvider = oldShield;
            CoreSettings.ServiceProvider = oldSettings;
            service.SealForReset();
        }
        return fired;
    }

    private static readonly DateTime Today = new(2026, 10, 10);

    [Fact]
    public void AnOrdinaryDay_FiresStreakKept_WithTheNewNumber()
    {
        var fired = Run(_ => LoginStreakRule.UpdateDailyStreak(new AchievementProgress { ConsecutiveDays = 4, LastLaunchDate = Today.AddDays(-1) }, Today));
        Assert.Equal(new[] { ("streakKept", 5) }, fired);
    }

    [Fact]
    public void ABreak_FiresStreakBroken_WithTheNumberTheUserHad()
    {
        var p = new AchievementProgress { ConsecutiveDays = 40, LastLaunchDate = Today.AddDays(-5) };
        var fired = Run(_ => LoginStreakRule.UpdateDailyStreak(p, Today));
        Assert.Equal(new[] { ("streakBroken", 40) }, fired);
        Assert.Equal(1, p.ConsecutiveDays);
    }

    [Fact]
    public void AShieldedGap_IsAKeep_NotABreak()
    {
        var p = new AchievementProgress { ConsecutiveDays = 9, LastLaunchDate = Today.AddDays(-2) };
        var fired = Run(_ =>
        {
            CoreQuests.UseStreakShieldProvider = () => true;
            LoginStreakRule.UpdateDailyStreak(p, Today);
        });
        Assert.Equal(new[] { ("streakKept", 10) }, fired);
    }

    [Fact]
    public void TheSameDay_AndAFirstRun_SayNothing()
    {
        var fired = Run(_ =>
        {
            LoginStreakRule.UpdateDailyStreak(new AchievementProgress { ConsecutiveDays = 3, LastLaunchDate = Today }, Today);
            LoginStreakRule.UpdateDailyStreak(new AchievementProgress(), Today);
        });
        Assert.Empty(fired);
    }
}
