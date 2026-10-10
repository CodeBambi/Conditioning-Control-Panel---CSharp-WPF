using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>hunt3 IC4 (WPF ProfileSyncService.cs:2040-2046, :3382): Sparkles earned elsewhere reach
/// this install on profile load and on sync. WALLET RULE: a snapshot only raises the balance; only a
/// debited receipt lowers it, plus the one adoption after a balance refusal, which this path never runs.</summary>
public sealed class ProfileAdoptSkillPointsTests
{
    private static AppSettings With(int local) => new() { SkillPoints = local };

    [Fact]
    public void AHigherServerBalanceRaisesTheWallet()
    {
        var s = With(4);
        Assert.True(ProfileAdopt.AdoptSkillPoints(s, new JValue(31), "test"));
        Assert.Equal(31, s.SkillPoints);
    }

    [Theory]
    [InlineData(10, 3)]
    [InlineData(10, 0)]
    [InlineData(10, 10)]
    [InlineData(10, -5)]
    public void ALowerOrEqualSnapshotNeverLowersTheWallet(int local, int server)
    {
        var s = With(local);
        Assert.False(ProfileAdopt.AdoptSkillPoints(s, new JValue(server), "test"));
        Assert.Equal(local, s.SkillPoints);
    }

    [Fact]
    public void OnlyAJsonIntegerCounts_AndTheCapHolds()
    {
        var s = With(7);
        Assert.False(ProfileAdopt.AdoptSkillPoints(s, null, "test"));
        Assert.False(ProfileAdopt.AdoptSkillPoints(s, JValue.CreateNull(), "test"));
        Assert.False(ProfileAdopt.AdoptSkillPoints(s, new JValue("99"), "test"));
        Assert.False(ProfileAdopt.AdoptSkillPoints(s, new JValue(99.5), "test"));
        Assert.Equal(7, s.SkillPoints);
        Assert.True(ProfileAdopt.AdoptSkillPoints(s, new JValue(long.MaxValue), "test"));
        Assert.Equal(SparklePoints.Cap, s.SkillPoints);
    }

    [Fact]
    public void TheSyncReplyRaises_NeverLowers()
    {
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var s = With(5);
        ProfileAdopt.ApplySyncResponse(s, JObject.Parse("{\"skill_points\":12}"), now);   // no user node: the wallet still follows
        Assert.Equal(12, s.SkillPoints);
        ProfileAdopt.ApplySyncResponse(s, JObject.Parse("{\"skill_points\":2}"), now);
        Assert.Equal(12, s.SkillPoints);
        ProfileAdopt.ApplySyncResponse(s, JObject.Parse("{}"), now);
        Assert.Equal(12, s.SkillPoints);
    }

    /// <summary>The two paths that MAY lower are untouched: a debited receipt, and the adoption after a
    /// balance refusal. A snapshot after either does not undo it unless the server really holds more.</summary>
    [Fact]
    public void TheReceiptAndTheRefusalExceptionStillLower_AndASnapshotDoesNotUndoThem()
    {
        Assert.True(V2WalletAdoption.Decide(sameAccount: true, fromBuy: true, serverSp: 3, localSp: 10, out var afterBuy));
        Assert.Equal(3, afterBuy);
        Assert.False(V2WalletAdoption.Decide(sameAccount: true, fromBuy: false, serverSp: 3, localSp: 10, out _));

        Assert.Equal(9, SparklePoints.AdoptAfterRefusal(local: 10, server: 9, cost: 10));
        Assert.Equal(SparklePoints.RefusalStep.Adopt, SparklePoints.AfterBalanceRefusal(10, 9, 10, alreadySynced: true));
        Assert.Equal(SparklePoints.RefusalStep.SyncAndRetry, SparklePoints.AfterBalanceRefusal(10, 9, 10, alreadySynced: false));

        var s = With(9);                                    // the wallet after the refusal adopted the server's 9
        Assert.False(ProfileAdopt.AdoptSkillPoints(s, new JValue(9), "test"));
        Assert.Equal(9, s.SkillPoints);
    }
}
