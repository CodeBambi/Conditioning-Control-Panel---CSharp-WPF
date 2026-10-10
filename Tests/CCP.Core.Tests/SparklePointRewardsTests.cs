using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF SparklePointRewards: a settled local credit is announced; zero, negative and capped credits stay quiet.</summary>
public class SparklePointRewardsTests
{
    [Fact]
    public void ACreditIsAnnouncedOnceWithItsAmount_AndAThrowingListenerBlocksNobody()
    {
        var seen = new List<SparklePointAward>();
        EventHandler<SparklePointAward> broken = (_, _) => throw new InvalidOperationException("decoration");
        EventHandler<SparklePointAward> good = (_, a) => seen.Add(a);
        SparklePointRewards.Awarded += broken;
        SparklePointRewards.Awarded += good;
        try
        {
            SparklePointRewards.PublishCredit(5, 8, SparklePointSource.LevelUp);
            SparklePointRewards.PublishCredit(8, 8, SparklePointSource.BubbleMilestone);   // nothing gained
            SparklePointRewards.PublishCredit(8, 3, SparklePointSource.LevelUp);           // never a debit
        }
        finally
        {
            SparklePointRewards.Awarded -= broken;
            SparklePointRewards.Awarded -= good;
        }
        var award = Assert.Single(seen);
        Assert.Equal(3, award.Amount);
        Assert.Equal(8, award.Balance);
        Assert.Equal(SparklePointSource.LevelUp, award.Source);
    }
}
