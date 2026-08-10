using System;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// PR-1 chrome — the pure half of the tab transition: which way a tab slides in, and which tabs
/// must not slide at all. The choreography itself is UI and is checked by eye; these are the two
/// decisions that can silently go wrong (a direction that fights the nav rail, or a WebView2 tab
/// getting a RenderTransform its native HWND cannot follow).
///
/// UX restructure Phase 1 replaced the two-row header strip with the vertical door rail, so
/// NavOrder is now the rail top-to-bottom and every reachable tab key is on it. The indices
/// below follow that order; a -1 lookup now means a genuine ghost ("patreon", "fyp"), which is
/// what the off-the-rail cases assert.
/// </summary>
public class ChromeFxNavTests
{
    [Fact]
    public void NavOrder_HasNoDuplicates()
        => Assert.Equal(ChromeFxNav.NavOrder.Length,
                        ChromeFxNav.NavOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count());

    [Theory]
    [InlineData("settings", 0)]     // Home
    [InlineData("presets", 1)]      // Studio
    [InlineData("lab", 7)]          // Play, first entry
    [InlineData("assets", 21)]      // Library, last row on the rail
    [InlineData("appsettings", 22)] // Settings, pinned below everything
    public void IndexOf_FollowsTheNavStrip(string tab, int expected)
        => Assert.Equal(expected, ChromeFxNav.IndexOf(tab));

    [Fact]
    public void IndexOf_IsCaseInsensitive()
        => Assert.Equal(ChromeFxNav.IndexOf("settings"), ChromeFxNav.IndexOf("SETTINGS"));

    [Fact]
    public void IndexOf_ResolvesTheLegacyProgressionAliasToTheDashboard()
        => Assert.Equal(ChromeFxNav.IndexOf("settings"), ChromeFxNav.IndexOf("progression"));

    [Theory]
    [InlineData("patreon")]   // ghost: ShowTab opens the App Info popup and returns
    [InlineData("fyp")]       // ghost: ShowTab opens a window and returns
    [InlineData("")]
    [InlineData(null)]
    public void IndexOf_IsNegative_ForTabsOffTheStrip(string? tab)
        => Assert.Equal(-1, ChromeFxNav.IndexOf(tab));

    [Fact]
    public void EntranceOffset_SlidesInFromTheRight_WhenMovingRightAlongTheStrip()
    {
        var (x, y) = ChromeFxNav.EntranceOffset("settings", "quests", 12);
        Assert.Equal(12, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void EntranceOffset_SlidesInFromTheLeft_WhenMovingLeftAlongTheStrip()
    {
        var (x, y) = ChromeFxNav.EntranceOffset("lab", "presets", 12);
        Assert.Equal(-12, x);
        Assert.Equal(0, y);
    }

    [Theory]
    [InlineData("settings", "patreon")]   // destination is off the rail
    [InlineData("patreon", "settings")]   // origin is off the rail
    [InlineData("quests", "quests")]      // same tab: no direction to imply
    public void EntranceOffset_FallsBackToARise_WhenThereIsNoHonestDirection(string from, string to)
    {
        var (x, y) = ChromeFxNav.EntranceOffset(from, to, 12);
        Assert.Equal(0, x);
        Assert.Equal(12, y);
    }

    [Fact]
    public void EntranceOffset_NeverMovesOnBothAxes()
    {
        foreach (var from in ChromeFxNav.NavOrder)
        foreach (var to in ChromeFxNav.NavOrder)
        {
            var (x, y) = ChromeFxNav.EntranceOffset(from, to, 12);
            Assert.True(x == 0 || y == 0, $"{from} -> {to} slid diagonally");
        }
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("progression")]
    [InlineData("gradedintake")]
    public void AirspaceTabs_AreExcludedFromTheSlide(string tab)
        => Assert.True(ChromeFxNav.IsAirspaceTab(tab));

    [Theory]
    [InlineData("quests")]
    [InlineData("presets")]
    [InlineData("")]
    [InlineData(null)]
    public void EveryOtherTab_MaySlide(string? tab)
        => Assert.False(ChromeFxNav.IsAirspaceTab(tab));
}
