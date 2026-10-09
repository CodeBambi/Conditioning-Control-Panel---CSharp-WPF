using System;
using ConditioningControlPanel;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Wave A progression feed (WPF 7.1.5): passive XP dropped only while idle (ActivityTracker,
/// ProgressionService.cs:70-78), 1 SP per level (SkillTreeService.OnLevelUp), 1 SP per 100 bubbles
/// (AchievementService.TrackBubblePopped) and conditioning time credited once (MainWindow.UiUpdates.cs:746-840).</summary>
[Collection("CoreSettings")]
public sealed class ProgressionFeedTests
{
    [Fact]
    public void PassiveXpIsBankedWhileActiveAndDroppedWhileIdle()
    {
        var s = CoreSettings.Current;
        var saved = (s.PlayerLevel, s.PlayerXP, s.OfflineMode, s.OfflineUsername, s.SkillPoints, ActivityIdle.IdleSecondsProvider);
        try
        {
            (s.PlayerLevel, s.PlayerXP, s.OfflineMode, s.OfflineUsername) = (1, 0, true, "feed-test");
            ActivityIdle.IdleSecondsProvider = () => 5;
            ProgressionBank.Add(1, "Flash");
            Assert.True(s.PlayerXP > 0);

            var before = s.PlayerXP;
            ActivityIdle.IdleSecondsProvider = () => ActivityIdle.IdleThresholdSeconds;
            ProgressionBank.Add(1, "Subliminal");
            ProgressionBank.Add(1, "BouncingText");
            Assert.Equal(before, s.PlayerXP);
            ProgressionBank.Add(1, "Bubble");   // an interaction source is never suppressed
            Assert.True(s.PlayerXP > before);

            // A probe that throws reads as active (WPF: a failed GetLastInputInfo assumes active).
            ActivityIdle.IdleSecondsProvider = () => throw new InvalidOperationException();
            Assert.False(ActivityIdle.IsIdle);
        }
        finally
        {
            (s.PlayerLevel, s.PlayerXP, s.OfflineMode, s.OfflineUsername, s.SkillPoints, ActivityIdle.IdleSecondsProvider) = saved;
        }
    }

    [Fact]
    public void EachLevelGainedPaysOneSkillPoint()
    {
        var s = CoreSettings.Current;
        var saved = (s.PlayerLevel, s.PlayerXP, s.OfflineMode, s.OfflineUsername, s.SkillPoints, s.HighestLevelEver, s.SeasonPeakLevel);
        try
        {
            (s.PlayerLevel, s.PlayerXP, s.OfflineMode, s.OfflineUsername, s.SkillPoints) = (1, 0, true, "feed-test", 7);
            var gained = 0;
            Action<int> onLevel = _ => gained++;
            ProgressionBank.LevelUp += onLevel;
            try { ProgressionBank.Add(1_000_000, "Other"); }
            finally { ProgressionBank.LevelUp -= onLevel; }
            Assert.True(gained >= 2);
            Assert.Equal(7 + gained, s.SkillPoints);
            Assert.True(s.SeasonPeakLevel >= s.PlayerLevel);
        }
        finally
        {
            (s.PlayerLevel, s.PlayerXP, s.OfflineMode, s.OfflineUsername, s.SkillPoints, s.HighestLevelEver, s.SeasonPeakLevel) = saved;
        }
    }

    [Fact]
    public void BubbleMilestonesPayOncePerHundredCrossed()
    {
        var s = CoreSettings.Current;
        var saved = s.SkillPoints;
        try
        {
            s.SkillPoints = 0;
            Assert.Equal(0, SkillPointsBank.CreditBubbleMilestones(s, 98, 99));
            Assert.Equal(1, SkillPointsBank.CreditBubbleMilestones(s, 99, 100));
            Assert.Equal(0, SkillPointsBank.CreditBubbleMilestones(s, 100, 101));
            Assert.Equal(3, SkillPointsBank.CreditBubbleMilestones(s, 150, 420));
            Assert.Equal(4, s.SkillPoints);
        }
        finally { s.SkillPoints = saved; }
    }

    [Fact]
    public void ConditioningTimeCreditsAMinutePerSixtyTicksAndTheRestOnStopNeverTwice()
    {
        var s = CoreSettings.Current;
        var saved = s.TotalConditioningMinutes;
        try
        {
            ConditioningTime.OnEngineStopped(DateTime.Now);   // clear anything a previous test left running
            s.TotalConditioningMinutes = 10;
            var t0 = new DateTime(2026, 10, 9, 12, 0, 0);
            ConditioningTime.Tick(t0);   // not tracking: nothing
            Assert.Equal(10, s.TotalConditioningMinutes);

            ConditioningTime.OnEngineStarted(t0);
            ConditioningTime.OnEngineStarted(t0.AddSeconds(5));   // a second start keeps the first baseline
            for (var i = 1; i <= 59; i++) ConditioningTime.Tick(t0.AddSeconds(i));
            Assert.Equal(10, s.TotalConditioningMinutes);
            ConditioningTime.Tick(t0.AddSeconds(60));
            Assert.Equal(11, s.TotalConditioningMinutes);

            // Stop at 90 s: the half minute the ticks have not credited lands once.
            ConditioningTime.OnEngineStopped(t0.AddSeconds(90));
            Assert.Equal(11.5, s.TotalConditioningMinutes, 6);
            ConditioningTime.OnEngineStopped(t0.AddSeconds(200));
            ConditioningTime.Tick(t0.AddSeconds(260));
            Assert.Equal(11.5, s.TotalConditioningMinutes, 6);
            Assert.False(ConditioningTime.IsTracking);
        }
        finally
        {
            ConditioningTime.OnEngineStopped(DateTime.Now);
            s.TotalConditioningMinutes = saved;
        }
    }
}
