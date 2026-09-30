using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.PieceByPiece;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Piece by Piece's online pictures: the consent gate, which niches are fetched, the
/// online share of a deal, and the four remembered settings.</summary>
public class PbpMediaRulesTests
{
    private static readonly string[] Pink = { "bimbofication", "Bimbos" };
    private static readonly string[] App = { "EroticHypnosis" };

    [Theory]
    [InlineData("local", true, false)]
    [InlineData("online", false, false)]   // a source without the consent card is still local
    [InlineData("online", true, true)]
    [InlineData("mixed", true, true)]
    [InlineData(null, true, false)]
    public void AppWideOnlineNeedsBothTheSourceAndTheConsent(string? source, bool consent, bool expected)
        => Assert.Equal(expected, PbpMediaRules.AppWideOnline(source, consent));

    [Theory]
    // own switch off: nothing, whatever else is true
    [InlineData(false, true, "online", true, false)]
    // an in-game pick is the opt-in on its own
    [InlineData(true, true, "local", false, true)]
    // no pick: only the app-wide consent opens the door
    [InlineData(true, false, "local", false, false)]
    [InlineData(true, false, "local", true, false)]
    [InlineData(true, false, "mixed", true, true)]
    public void FetchAllowedFollowsTheConsentRule(bool own, bool picked, string source, bool consent, bool expected)
        => Assert.Equal(expected, PbpMediaRules.FetchAllowed(own, picked, source, consent));

    [Fact]
    public void APickedFlavourFetchesItsOwnNiches()
    {
        var subs = PbpMediaRules.ChannelsFor(true, true, "local", false, "pink", Pink, App);
        Assert.Equal(Pink, subs);
    }

    [Fact]
    public void AStoredFlavourWithoutAPickOrConsentFetchesNothing()
        => Assert.Empty(PbpMediaRules.ChannelsFor(true, false, "local", false, "pink", Pink, App));

    [Fact]
    public void AStoredFlavourWithAppWideConsentIsUsedAtBoot()
        => Assert.Equal(Pink, PbpMediaRules.ChannelsFor(true, false, "online", true, "pink", Pink, App));

    [Fact]
    public void NoFlavourWithAppWideConsentUsesTheAppsOwnNiches()
        => Assert.Equal(App, PbpMediaRules.ChannelsFor(true, false, "mixed", true, "", Pink, App));

    [Fact]
    public void OwnPicturesOnlyFetchesNothingEvenWithConsent()
        => Assert.Empty(PbpMediaRules.ChannelsFor(false, true, "online", true, "pink", Pink, App));

    [Fact]
    public void ChannelsAreCleanedAndCapped()
    {
        var raw = new[] { "ok_1", "bad name", "../x", "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9" };
        var subs = PbpMediaRules.ChannelsFor(true, true, "local", false, "mine", raw, App);
        Assert.DoesNotContain("bad name", subs);
        Assert.DoesNotContain("../x", subs);
        Assert.Equal(8, subs.Count);
    }

    [Fact]
    public void AnUnknownFlavourIsNoFlavour()
        => Assert.Empty(PbpMediaRules.ChannelsFor(true, false, "local", false, "nope", Pink, App));

    [Theory]
    [InlineData("online", 30, false, 100)]
    [InlineData("online", 30, true, 100)]
    [InlineData("mixed", 30, false, 30)]
    [InlineData("mixed", 1, false, 5)]
    [InlineData("mixed", 30, true, PbpMediaRules.PickedSharePct)]
    [InlineData("local", 30, true, PbpMediaRules.PickedSharePct)]
    public void ShareFollowsTheSourceAndThePick(string source, int ratio, bool picked, int expected)
        => Assert.Equal(expected, PbpMediaRules.SharePct(source, ratio, picked));

    [Fact]
    public void PbpMediaSettingsRoundTrip()
    {
        var s = new AppSettings
        {
            PbpMediaFlavour = "shiny",
            PbpMediaCustom = "{\"shiny\":{\"on\":[\"rubber\"],\"off\":[],\"added\":[]}}",
            PbpMediaSubs = "ShinyPorn,rubber",
            PbpMediaOnline = false,
        };
        var back = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(s))!;
        Assert.Equal("shiny", back.PbpMediaFlavour);
        Assert.Equal(s.PbpMediaCustom, back.PbpMediaCustom);
        Assert.Equal("ShinyPorn,rubber", back.PbpMediaSubs);
        Assert.False(back.PbpMediaOnline);
    }

    [Fact]
    public void PbpMediaDefaultsAreNoPickAndOnlineAllowed()
    {
        var s = new AppSettings();
        Assert.Equal("", s.PbpMediaFlavour);
        Assert.Equal("", s.PbpMediaSubs);
        Assert.True(s.PbpMediaOnline);
        s.PbpMediaFlavour = null!;
        Assert.Equal("", s.PbpMediaFlavour);
    }
}
