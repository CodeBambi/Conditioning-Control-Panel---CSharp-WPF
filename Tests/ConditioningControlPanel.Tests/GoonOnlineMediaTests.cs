using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Goon Game's online pictures: what a niche may be, how a pick is stored and
/// echoed, which state the page is told, and how a temp file becomes a page url.</summary>
public class GoonOnlineMediaTests
{
    [Theory]
    [InlineData("bimbofication", true)]
    [InlineData("Bimbos", true)]
    [InlineData("a_b_9", true)]
    [InlineData("ab", true)]
    [InlineData("a", false)]
    [InlineData("has space", false)]
    [InlineData("dash-name", false)]
    [InlineData("../etc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void NicheGrammarMatchesThePage(string? name, bool ok)
        => Assert.Equal(ok, GoonOnlineMediaRules.IsNiche(name));

    [Fact]
    public void NicheLongerThan40IsRefused()
    {
        Assert.True(GoonOnlineMediaRules.IsNiche(new string('a', 40)));
        Assert.False(GoonOnlineMediaRules.IsNiche(new string('a', 41)));
    }

    [Fact]
    public void CleanSubsDropsJunkDedupesAndCapsAtEight()
    {
        var raw = new[] { "Bimbos", "bimbos", "bad name", "r/sissyhypno", null, "x",
            "a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8" };
        var clean = GoonOnlineMediaRules.CleanSubs(raw);
        Assert.Equal(GoonOnlineMediaRules.MaxSubs, clean.Count);
        Assert.Equal("Bimbos", clean[0]);           // first spelling wins
        Assert.Equal("sissyhypno", clean[1]);       // a leading r/ is forgiven
        Assert.DoesNotContain("bad name", clean);
        Assert.DoesNotContain("x", clean);
    }

    [Fact]
    public void StoredSubsRoundTrip()
    {
        var subs = GoonOnlineMediaRules.CleanSubs(new[] { "EroticHypnosis", "HypnoHentai" });
        var stored = GoonOnlineMediaRules.JoinSubs(subs);
        Assert.Equal(subs, GoonOnlineMediaRules.SplitSubs(stored));
        Assert.Empty(GoonOnlineMediaRules.SplitSubs(""));
        Assert.Empty(GoonOnlineMediaRules.SplitSubs(null));
        // A hand-edited settings file cannot smuggle a bad name past the split.
        Assert.Equal(new[] { "ok_name" }, GoonOnlineMediaRules.SplitSubs("ok_name,../bad,,"));
    }

    [Theory]
    [InlineData("pink", "pink")]
    [InlineData("TRANCE", "trance")]
    [InlineData("mine", "mine")]
    [InlineData("censored", "censored")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("evil", "")]
    public void OnlyKnownFlavoursAreStored(string? raw, string expected)
        => Assert.Equal(expected, GoonOnlineMediaRules.CleanFlavour(raw));

    [Fact]
    public void CustomBlobIsAnObjectOrNothing()
    {
        var o = JObject.Parse("{\"pink\":{\"on\":[\"Bimbos\"],\"off\":[],\"added\":[\"bimbo\"]}}");
        var stored = GoonOnlineMediaRules.CleanCustom(o);
        Assert.True(JToken.DeepEquals(o, GoonOnlineMediaRules.ParseCustom(stored)));

        Assert.Equal("", GoonOnlineMediaRules.CleanCustom(new JArray(1, 2)));
        Assert.Equal("", GoonOnlineMediaRules.CleanCustom(null));
        var huge = new JObject { ["pink"] = new string('a', GoonOnlineMediaRules.MaxCustomChars) };
        Assert.Equal("", GoonOnlineMediaRules.CleanCustom(huge));

        Assert.Empty(GoonOnlineMediaRules.ParseCustom(""));
        Assert.Empty(GoonOnlineMediaRules.ParseCustom("not json"));
        Assert.Empty(GoonOnlineMediaRules.ParseCustom("[1]"));
    }

    [Fact]
    public void NothingIsFetchedWithoutAPickNichesAndTheSwitch()
    {
        var subs = new[] { "Bimbos" };
        Assert.True(GoonOnlineMediaRules.ShouldFetch(true, "pink", subs));
        Assert.False(GoonOnlineMediaRules.ShouldFetch(false, "pink", subs));
        Assert.False(GoonOnlineMediaRules.ShouldFetch(true, "", subs));
        Assert.False(GoonOnlineMediaRules.ShouldFetch(true, "pink", new string[0]));
    }

    [Theory]
    [InlineData(false, 3, 10, true, false, "off")]
    [InlineData(true, 0, 0, false, false, "empty")]
    [InlineData(true, 3, 0, true, false, "loading")]
    [InlineData(true, 3, 5, true, false, "loading")]
    [InlineData(true, 3, 5, false, false, "ready")]
    [InlineData(true, 3, 5, false, true, "ready")]
    [InlineData(true, 3, 0, false, true, "error")]
    [InlineData(true, 3, 0, false, false, "empty")]
    public void StateTable(bool online, int subs, int have, bool running, bool failed, string state)
        => Assert.Equal(state, GoonOnlineMediaRules.StateFor(online, subs, have, running, failed));

    [Fact]
    public void TempFileUnderTheAssetsRootBecomesACcpAssetsUrl()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp assets root");
        var file = Path.Combine(root, ".temp", "ccp_temp_remote_abc.webp");
        Assert.Equal("https://ccp.assets/.temp/ccp_temp_remote_abc.webp",
            GoonOnlineMediaRules.UrlFor(file, root));
        Assert.Equal("https://ccp.assets/.temp/ccp_temp_remote_abc.webp",
            GoonOnlineMediaRules.UrlFor(file, root + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void FileOutsideTheAssetsRootHasNoUrl()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp_assets");
        Assert.Null(GoonOnlineMediaRules.UrlFor(Path.Combine(Path.GetTempPath(), "ccp_temp_x.webp"), root));
        // A sibling that only shares the prefix is not inside.
        Assert.Null(GoonOnlineMediaRules.UrlFor(Path.Combine(root + "2", "a.webp"), root));
        Assert.Null(GoonOnlineMediaRules.UrlFor(null, root));
        Assert.Null(GoonOnlineMediaRules.UrlFor("x.webp", ""));
    }

    [Fact]
    public void TargetsStaySmallEnoughToLandFast()
    {
        Assert.InRange(GoonOnlineMediaRules.StillTarget, 12, 30);
        Assert.InRange(GoonOnlineMediaRules.ClipTarget, 6, 16);
        Assert.InRange(GoonOnlineMediaRules.Concurrency, 1, 4);
        // The shared temp tracker sweeps above 50 files; the deck must fit well under it.
        Assert.True(GoonOnlineMediaRules.StillTarget + GoonOnlineMediaRules.ClipTarget < 50);
    }

    [Fact]
    public void RefillsStayBoundedAtThreeWaves()
    {
        Assert.Equal(3, GoonOnlineMediaRules.MaxWaves);
        Assert.Equal(72, GoonOnlineMediaRules.Cap(GoonOnlineMediaRules.StillTarget));
        Assert.Equal(36, GoonOnlineMediaRules.Cap(GoonOnlineMediaRules.ClipTarget));
    }

    [Fact]
    public void MoreIsANoOpWithNoWaveToExtend()
    {
        using var m = new GoonOnlineMedia(_ => { });
        Assert.False(m.More());
    }

    [Theory]
    [InlineData(true, "pink", true)]
    [InlineData(true, "mine", true)]
    [InlineData(true, "", false)]
    [InlineData(false, "pink", false)]
    public void OnlyAPickWithTheSwitchOnOptsTheSessionIn(bool online, string flavour, bool expected)
        => Assert.Equal(expected, GoonOnlineMediaRules.IsSessionOptIn(online, flavour));
}
