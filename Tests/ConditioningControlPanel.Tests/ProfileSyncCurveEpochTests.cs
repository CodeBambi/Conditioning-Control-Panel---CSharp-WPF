using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1270 / #1274: the server now says which curve an account's level is priced on
/// (<c>user.curve_epoch</c>, CCP-Server #210). The desktop follows it both ways, re-priced by the
/// CUMULATIVE total so no watermark or take-higher compare ever sees a loss, and does nothing on
/// an older server that sends no field.
/// </summary>
public class ProfileSyncCurveEpochTests
{
    private const int V1 = ProgressionService.CurveEpochLegacy;
    private const int V2 = ProgressionService.CurveEpochDescent;

    private static double Total(int level, double into, int epoch)
        => ProgressionService.CumulativeXpBeforeLevel(level, epoch) + into;

    [Theory]
    [InlineData("{\"curve_epoch\":0}", 0)]
    [InlineData("{\"curve_epoch\":1}", 1)]
    public void TheTwoEpochsParse(string json, int expected)
        => Assert.Equal(expected, ProfileSyncService.ParseCurveEpoch(JObject.Parse(json)["curve_epoch"]));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"curve_epoch\":null}")]
    [InlineData("{\"curve_epoch\":2}")]
    [InlineData("{\"curve_epoch\":-1}")]
    [InlineData("{\"curve_epoch\":\"1\"}")]
    [InlineData("{\"curve_epoch\":1.5}")]
    public void AnythingElseIsNoSignal(string json)
        => Assert.Null(ProfileSyncService.ParseCurveEpoch(JObject.Parse(json)["curve_epoch"]));

    [Fact]
    public void AnOlderServerMovesNothing()
        => Assert.Null(ProfileSyncService.CurveEpochReprice(V1, null, false, 61, 500));

    [Fact]
    public void TheSameCurveMovesNothing()
    {
        Assert.Null(ProfileSyncService.CurveEpochReprice(V1, V1, false, 61, 500));
        Assert.Null(ProfileSyncService.CurveEpochReprice(V2, V2, false, 61, 500));
    }

    [Fact]
    public void AnUnackedCeremonyMovesNothing()
        => Assert.Null(ProfileSyncService.CurveEpochReprice(V1, V2, true, 61, 500));

    [Fact]
    public void AnOutOfRangeEpochMovesNothing()
        => Assert.Null(ProfileSyncService.CurveEpochReprice(V1, 7, false, 61, 500));

    [Fact]
    public void V1ToV2KeepsTheTotalAndMovesTheLevel()
    {
        var before = Total(61, 500, V1);
        var r = ProfileSyncService.CurveEpochReprice(V1, V2, false, 61, 500);
        Assert.NotNull(r);
        Assert.NotEqual(61, r!.Value.Level);                       // v2 prices level 61's rungs differently
        Assert.Equal(before, Total(r.Value.Level, r.Value.XpIntoLevel, V2), 3);
        Assert.True(r.Value.XpIntoLevel < ProgressionService.GetXPForLevel(r.Value.Level, V2));
    }

    [Fact]
    public void V2ToV1KeepsTheTotalToo()
    {
        var before = Total(57, 1234, V2);
        var r = ProfileSyncService.CurveEpochReprice(V2, V1, false, 57, 1234);
        Assert.NotNull(r);
        Assert.Equal(before, Total(r!.Value.Level, r.Value.XpIntoLevel, V1), 3);
    }

    [Fact]
    public void ARoundTripIsLossless()
    {
        var into = ProgressionService.GetXPForLevel(88, V1) / 3;   // a valid remainder for level 88
        var there = ProgressionService.RepriceLedger(88, into, V1, V2);
        var back = ProgressionService.RepriceLedger(there.Level, there.XpIntoLevel, V2, V1);
        Assert.Equal(88, back.Level);
        Assert.Equal(into, back.XpIntoLevel, 3);
    }

    [Fact]
    public void TheHoneymoonLevelsDoNotMove()
    {
        // L1-40 are identical on both curves, so nobody below 40 sees anything change.
        var r = ProgressionService.RepriceLedger(30, 200, V1, V2);
        Assert.Equal(30, r.Level);
        Assert.Equal(200, r.XpIntoLevel, 3);
    }

    [Fact]
    public void CumulativeMatchesTheInstanceTotal()
    {
        // GetTotalXP(level, 0) on the active (v1, no settings) curve is the same sum.
        var p = new ProgressionService();
        Assert.Equal(p.GetTotalXP(45, 0), ProgressionService.CumulativeXpBeforeLevel(45, V1), 3);
    }
}
