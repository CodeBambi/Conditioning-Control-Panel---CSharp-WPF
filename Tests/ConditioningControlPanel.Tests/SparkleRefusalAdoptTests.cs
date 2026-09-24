using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1268 #1269: the wallet showed 10 while the server refused a 10-point purchase at 9.
/// The client credits a bubble milestone at its own lifetime 100-boundary, the server at its
/// seasonal one on the next sync, so local runs a point ahead for part of every cycle. A refusal
/// for balance now adopts the server's number, and only then.
/// </summary>
public class SparkleRefusalAdoptTests
{
    [Fact]
    public void BalanceRefusalBelowLocal_AdoptsServer()
        => Assert.Equal(9, SparklePoints.AdoptAfterRefusal(local: 10, server: 9, cost: 10));

    [Fact]
    public void NoServerNumber_KeepsLocal()
        => Assert.Null(SparklePoints.AdoptAfterRefusal(local: 10, server: null, cost: 10));

    [Fact]
    public void ServerCouldAfford_RefusalWasNotForBalance_KeepsLocal()
        // "Skill already owned" / "Prerequisite not met" carry a balance that covers the cost.
        => Assert.Null(SparklePoints.AdoptAfterRefusal(local: 12, server: 11, cost: 10));

    [Fact]
    public void ServerAtOrAboveLocal_NeverRaisesThroughThisPath()
    {
        Assert.Null(SparklePoints.AdoptAfterRefusal(local: 9, server: 9, cost: 10));
        Assert.Null(SparklePoints.AdoptAfterRefusal(local: 5, server: 8, cost: 10));
    }

    [Fact]
    public void ServerValueIsClamped()
        => Assert.Equal(0, SparklePoints.AdoptAfterRefusal(local: 3, server: -4, cost: 10));

    [Fact]
    public void AlreadyUnlockedRepeat_IsNotedOncePerId()
    {
        var id = "test_repeat_" + System.Guid.NewGuid().ToString("N");
        Assert.True(AchievementService.FirstAlreadyUnlockedNote(id));
        Assert.False(AchievementService.FirstAlreadyUnlockedNote(id));
        Assert.False(AchievementService.FirstAlreadyUnlockedNote(id));
        Assert.True(AchievementService.FirstAlreadyUnlockedNote(id + "_other"));
    }
}
