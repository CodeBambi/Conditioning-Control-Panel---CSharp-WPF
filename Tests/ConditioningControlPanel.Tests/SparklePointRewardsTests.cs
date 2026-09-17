using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

[CollectionDefinition(nameof(SparklePointRewardsCollection), DisableParallelization = true)]
public sealed class SparklePointRewardsCollection { }

[Collection(nameof(SparklePointRewardsCollection))]
public sealed class SparklePointRewardsTests
{
    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(40, 43, 3, 43)]
    [InlineData(99_998, 100_002, 1, 99_999)]
    public void ReportsOnlyActualClampedCredit(int before, int after, int amount, int balance)
    {
        var awards = new List<SparklePointAward>();
        EventHandler<SparklePointAward> listener = (_, award) => awards.Add(award);
        SparklePointRewards.Awarded += listener;
        try
        {
            SparklePointRewards.PublishCredit(before, after, SparklePointSource.BubbleMilestone);
            var award = Assert.Single(awards);
            Assert.Equal(amount, award.Amount);
            Assert.Equal(balance, award.Balance);
            Assert.Equal(SparklePointSource.BubbleMilestone, award.Source);
        }
        finally { SparklePointRewards.Awarded -= listener; }
    }

    [Theory]
    [InlineData(99_999, 100_003)]
    [InlineData(10, 10)]
    [InlineData(10, 5)]
    [InlineData(0, -5)]
    public void CapNoOpAndDecreaseDoNotCelebrate(int before, int after)
    {
        var count = 0;
        EventHandler<SparklePointAward> listener = (_, _) => count++;
        SparklePointRewards.Awarded += listener;
        try
        {
            SparklePointRewards.PublishCredit(before, after, SparklePointSource.LevelUp);
            Assert.Equal(0, count);
        }
        finally { SparklePointRewards.Awarded -= listener; }
    }

    [Fact]
    public void RestoreSyncAndPurchaseStyleBalanceWritesRemainQuiet()
    {
        var count = 0;
        EventHandler<SparklePointAward> listener = (_, _) => count++;
        SparklePointRewards.Awarded += listener;
        try
        {
            var settings = new AppSettings { SkillPoints = 700 };
            settings.SkillPoints = SparklePoints.MergeMax(900, settings.SkillPoints);
            settings.SkillPoints = 880;
            Assert.Equal(0, count);
            SparklePointRewards.PublishCredit(880, 881, SparklePointSource.LevelUp);
            Assert.Equal(1, count);
            Assert.Equal(880, settings.SkillPoints); // Notifications cannot mutate the ledger.
        }
        finally { SparklePointRewards.Awarded -= listener; }
    }

    [Fact]
    public void ThrowingDecorationDoesNotBlockTheNextSubscriberOrCaller()
    {
        SparklePointAward? observed = null;
        EventHandler<SparklePointAward> broken = (_, _) => throw new InvalidOperationException("test subscriber");
        EventHandler<SparklePointAward> good = (_, award) => observed = award;
        SparklePointRewards.Awarded += broken;
        SparklePointRewards.Awarded += good;
        try
        {
            SparklePointRewards.PublishCredit(3, 4, SparklePointSource.LevelUp);
            Assert.Equal(new SparklePointAward(1, 4, SparklePointSource.LevelUp), observed);
        }
        finally
        {
            SparklePointRewards.Awarded -= broken;
            SparklePointRewards.Awarded -= good;
        }
    }
}
