using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1091: For You ghost mode showed a blank screen. The page-side cause is fixed in
/// fyp/main.js, but the host's own mitigation for the same class of failure was one edit away
/// from silently evaporating.
///
/// Chromium's command line keys switches by NAME (base::CommandLine holds a map), so naming
/// --disable-features twice does NOT union the values - the last occurrence wins and every
/// feature listed in the earlier one is dropped without a warning. ChaosWebViewHost emits
/// --disable-features=CalculateNativeWinOcclusion for every page it hosts, and FypHostService
/// appends its own --disable-features for autoplay-adjacent switches; whichever one lost would
/// take the occlusion opt-out with it. ComposeBrowserArguments unions them into a single switch
/// so the ghost mirror never depends on argument ordering.
/// </summary>
public class BrowserArgumentMergeTests
{
    [Fact]
    public void TwoDisableFeatureSwitches_UnionIntoOne()
    {
        var args = ChaosWebViewHost.ComposeBrowserArguments(
            "--disable-features=CalculateNativeWinOcclusion",
            "--disable-features=AutoplayIgnoreWebAudio");

        Assert.Equal("--disable-features=CalculateNativeWinOcclusion,AutoplayIgnoreWebAudio", args);
    }

    [Fact]
    public void TheOcclusionOptOutSurvivesACallersOwnFeatureList()
    {
        // The exact pairing that ships: host switches + FypHostService's ghost arguments.
        var args = ChaosWebViewHost.ComposeBrowserArguments(
            "--disable-direct-composition-video-overlays --disable-features=CalculateNativeWinOcclusion",
            "--autoplay-policy=no-user-gesture-required --disable-features=CalculateNativeWinOcclusion "
            + "--disable-backgrounding-occluded-windows");

        Assert.Contains("CalculateNativeWinOcclusion", args);
        // Once, not twice: a repeated switch name is the whole failure mode.
        Assert.Equal(1, CountOccurrences(args, "--disable-features="));
        Assert.Contains("--autoplay-policy=no-user-gesture-required", args);
        Assert.Contains("--disable-backgrounding-occluded-windows", args);
        Assert.Contains("--disable-direct-composition-video-overlays", args);
    }

    [Fact]
    public void ARepeatedFeatureIsNotListedTwice()
    {
        var args = ChaosWebViewHost.ComposeBrowserArguments(
            "--disable-features=A,B", "--disable-features=B,C");

        Assert.Equal("--disable-features=A,B,C", args);
    }

    [Fact]
    public void EnableAndDisableStayApart()
    {
        var args = ChaosWebViewHost.ComposeBrowserArguments(
            "--disable-features=A", "--enable-features=B");

        Assert.Contains("--disable-features=A", args);
        Assert.Contains("--enable-features=B", args);
    }

    [Fact]
    public void PlainSwitchesKeepTheirOrderAndDeduplicate()
    {
        var args = ChaosWebViewHost.ComposeBrowserArguments(
            "--first --second", "--second --third");

        Assert.Equal("--first --second --third", args);
    }

    [Theory]
    [InlineData(null, null, "")]
    [InlineData("--only", null, "--only")]
    [InlineData(null, "--only", "--only")]
    [InlineData("", "   ", "")]
    public void MissingSidesAreHarmless(string? hostArgs, string? extra, string expected)
    {
        Assert.Equal(expected, ChaosWebViewHost.ComposeBrowserArguments(hostArgs, extra));
    }

    [Fact]
    public void AnEmptyFeatureListIsDropped()
    {
        // A switch with nothing after the '=' would disable nothing and only add noise.
        Assert.Equal("--keep", ChaosWebViewHost.ComposeBrowserArguments("--disable-features=", "--keep"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, at = 0;
        while ((at = haystack.IndexOf(needle, at, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }
        return count;
    }
}
