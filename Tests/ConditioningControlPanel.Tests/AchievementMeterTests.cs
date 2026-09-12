using System;
using System.Globalization;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The progress meter on a locked achievement card reads the same counter and the same threshold
/// the tracker unlocks on. These pin that: a stat-threshold badge reports its counter, a secret or
/// one-shot badge reports nothing, an unlocked badge reports full, and the table only names real
/// achievements.
/// </summary>
public class AchievementMeterTests
{
    [Fact]
    public void StatThresholdReportsCounterAgainstTheTrackersBar()
    {
        var progress = new AchievementProgress { TotalAttentionChecksPassed = 76 };

        var meter = AchievementMeters.Compute(Achievement.All["eyes_front"], progress, playerLevel: 1);

        Assert.NotNull(meter);
        Assert.Equal(76, meter!.Value.Current);
        Assert.Equal(AchievementService.EyesFrontAttentionChecks, meter.Value.Target);
        Assert.False(meter.Value.IsComplete);
        Assert.Equal(0.76, meter.Value.Fraction, precision: 6);
    }

    [Theory]
    [InlineData("relapse")]           // a timing window, not a counter
    [InlineData("total_lockdown")]    // a flag
    [InlineData("needy_doll")]        // hidden easter egg
    [InlineData("she_remembers")]     // one-shot event
    [InlineData("spiral_eyes")]       // continuous timer that resets
    [InlineData("modder")]            // first-time badge
    public void SecretAndEventAchievementsReportNoProgress(string id)
    {
        var progress = new AchievementProgress { ContinuousSpiralMinutes = 12, ModsInstalled = 1 };

        Assert.Null(AchievementMeters.Compute(Achievement.All[id], progress, playerLevel: 50));
        Assert.False(AchievementMeters.IsCountable(id));
    }

    [Fact]
    public void UnlockedAchievementReportsFull()
    {
        var progress = new AchievementProgress { TotalAttentionChecksPassed = 3 };
        progress.Unlock("eyes_front");

        var meter = AchievementMeters.Compute(Achievement.All["eyes_front"], progress, playerLevel: 1);

        Assert.NotNull(meter);
        Assert.Equal(meter!.Value.Target, meter.Value.Current);
        Assert.True(meter.Value.IsComplete);
        Assert.Equal(1, meter.Value.Fraction);
    }

    [Fact]
    public void LevelMilestonesReadThePlayerLevel()
    {
        var progress = new AchievementProgress();

        var meter = AchievementMeters.Compute(Achievement.All["plastic_initiation"], progress, playerLevel: 4);

        Assert.NotNull(meter);
        Assert.Equal(4, meter!.Value.Current);
        Assert.Equal(10, meter.Value.Target);

        // Every milestone the tracker unlocks on is in the meter table with the same level.
        foreach (var (id, level) in AchievementService.LevelMilestones)
        {
            var m = AchievementMeters.Compute(Achievement.All[id], progress, playerLevel: level);
            Assert.True(m!.Value.IsComplete, $"'{id}' should read complete at level {level}");
            Assert.Contains(level.ToString(CultureInfo.InvariantCulture), Achievement.All[id].Requirement);
        }
    }

    [Fact]
    public void CumulativeHoursShowInHoursNotMinutes()
    {
        var progress = new AchievementProgress { TotalSpiralMinutes = 312 };

        var meter = AchievementMeters.Compute(Achievement.All["threadbare"], progress, playerLevel: 1);

        Assert.NotNull(meter);
        Assert.Equal(5.2, meter!.Value.Current, precision: 6);
        Assert.Equal(10, meter.Value.Target);
        Assert.False(meter.Value.IsComplete);
    }

    [Fact]
    public void CounterPastTheBarClampsToTheTarget()
    {
        // A cloud restore can lift a counter past the bar before the tracker's next tick unlocks.
        var progress = new AchievementProgress { TotalBubblesPopped = 1200 };

        var meter = AchievementMeters.Compute(Achievement.All["pop_the_thought"], progress, playerLevel: 1);

        Assert.Equal(AchievementService.PopTheThoughtBubbles, meter!.Value.Current);
        Assert.True(meter.Value.IsComplete);
    }

    [Fact]
    public void LabelIsPlainCurrentSlashTarget()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal("76 / 100", new AchievementMeter(76, 100).Label);
            Assert.Equal("5.2 / 10", new AchievementMeter(5.2, 10).Label);
            Assert.Equal("0 / 5,000", new AchievementMeter(0, 5000).Label);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// The split is a decision, not an accident: 29 countable, the rest event or secret. A new
    /// lifetime-counter achievement belongs in the table; bump the number when you add it.
    /// </summary>
    [Fact]
    public void CountableTableNamesRealAchievementsOnly()
    {
        var ids = AchievementMeters.CountableIds.ToList();

        Assert.Equal(29, ids.Count);
        Assert.All(ids, id => Assert.True(Achievement.All.ContainsKey(id), $"'{id}' is not in the catalogue"));
        Assert.All(ids, id => Assert.False(Achievement.All[id].IsHidden, $"'{id}' is parked; a meter on it is dead weight"));
    }

    [Fact]
    public void EveryCountableAchievementReadsCompleteAtItsOwnTarget()
    {
        // Unlocking is the one path that must always read full, whatever the counter says.
        var progress = new AchievementProgress();
        foreach (var id in AchievementMeters.CountableIds)
        {
            progress.Unlock(id);
            var meter = AchievementMeters.Compute(Achievement.All[id], progress, playerLevel: 0);
            Assert.True(meter!.Value.IsComplete, $"'{id}' unlocked but not full");
            Assert.True(meter.Value.Target > 0, $"'{id}' has no target");
        }
    }
}
