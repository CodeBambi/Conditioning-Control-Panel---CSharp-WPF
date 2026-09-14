using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Back Room CONTRACT.md 3.4: the Sparkle Points cap rose from 9,999 to 99,999, client and server
/// together. The client has one constant (SparklePoints.Cap) and one clamp (the AppSettings
/// setter), and these tests pin that a five-digit balance survives every path that writes it:
/// settings load, both sync merges, and a skill purchase response.
/// </summary>
public class SparklePointsCapTests
{
    private const int FiftyK = 50_000;

    [Fact]
    public void CapMatchesTheServer()
        => Assert.Equal(99_999, SparklePoints.Cap);

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(9_999, 9_999)]
    [InlineData(10_000, 10_000)]
    [InlineData(FiftyK, FiftyK)]
    [InlineData(99_999, 99_999)]
    [InlineData(100_000, 99_999)]
    [InlineData(int.MaxValue, 99_999)]
    public void SetterClampsIntoZeroToCap(int written, int expected)
    {
        var s = new AppSettings { SkillPoints = written };
        Assert.Equal(expected, s.SkillPoints);
    }

    [Fact]
    public void FiftyThousandSurvivesSettingsLoad()
    {
        var json = JsonConvert.SerializeObject(new AppSettings { SkillPoints = FiftyK });
        // Same object-creation rule SettingsService.Load uses.
        var loaded = JsonConvert.DeserializeObject<AppSettings>(json,
            new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace })!;

        Assert.Equal(FiftyK, loaded.SkillPoints);
    }

    [Fact]
    public void OverCapFileLoadsAtTheCap()
    {
        var loaded = JsonConvert.DeserializeObject<AppSettings>("{\"SkillPoints\": 250000}")!;
        Assert.Equal(SparklePoints.Cap, loaded.SkillPoints);
    }

    [Theory]
    [InlineData(FiftyK, 9_999)]   // server ahead of an old local
    [InlineData(9_999, FiftyK)]   // local ahead of an old server still clamping at 9,999
    [InlineData(FiftyK, FiftyK)]
    public void FiftyThousandSurvivesSyncMerge(int server, int local)
    {
        var s = new AppSettings { SkillPoints = local };
        s.SkillPoints = SparklePoints.MergeMax(server, s.SkillPoints);

        Assert.Equal(FiftyK, s.SkillPoints);
    }

    [Fact]
    public void SyncMergeNeverExceedsTheCap()
        => Assert.Equal(SparklePoints.Cap, SparklePoints.MergeMax(150_000, 20));

    [Fact]
    public void FiftyThousandSurvivesASkillPurchase()
    {
        var s = new AppSettings { SkillPoints = FiftyK, UnlockedSkills = new List<string> { "a" } };

        // Server answers with the balance after a 10-point cost.
        ProfileSyncService.ApplyPurchaseResult(s, FiftyK - 10, new List<string> { "b" });

        Assert.Equal(FiftyK - 10, s.SkillPoints);
        Assert.Contains("a", s.UnlockedSkills);
        Assert.Contains("b", s.UnlockedSkills);
    }

    [Fact]
    public void PurchaseWithoutABalanceKeepsLocal()
    {
        var s = new AppSettings { SkillPoints = FiftyK };
        ProfileSyncService.ApplyPurchaseResult(s, null, null);
        Assert.Equal(FiftyK, s.SkillPoints);
    }
}
