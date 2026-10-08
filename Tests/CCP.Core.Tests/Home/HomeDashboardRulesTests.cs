using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Core.Tests.Home;

/// <summary>The Home dashboard's numbers (parity lane E3), pinned to WPF 7.1.5.</summary>
public class HomeDashboardRulesTests
{
    [Fact]
    public void Drawer_ClosedByDefault_AndTheSettingRoundTrips()
    {
        Assert.False(new AppSettings().FavoritesDrawerOpen);
        var back = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(new AppSettings { FavoritesDrawerOpen = true }))!;
        Assert.True(back.FavoritesDrawerOpen);
        Assert.Contains("\"favorites_drawer_open\"", JsonConvert.SerializeObject(new AppSettings()));
    }

    [Fact]
    public void Drawer_Arithmetic_MatchesTheRail()
    {
        Assert.Equal(92, HomeDashboardRules.FavoritesDrawerWidth);
        Assert.Equal(22, HomeDashboardRules.FavoritesHandleWidth);
        Assert.Equal(HomeDashboardRules.FavoritesDrawerWidth - 5, HomeDashboardRules.FavoritesRailWidth);
        Assert.Equal(HomeDashboardRules.FavoritesRailWidth - 2 - 8 - 8, HomeDashboardRules.FavoritesChipWidth);
        Assert.Equal(0, HomeDashboardRules.DrawerBodyWidth(false));
        Assert.Equal(92, HomeDashboardRules.DrawerBodyWidth(true));
        Assert.Equal(200, HomeDashboardRules.FavoritesDrawerSlideMs);
        Assert.Equal(2500, HomeDashboardRules.FavoritesDrawerPeekMs);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void Drawer_APeekShowsItOpenWithoutBeingThePreference(bool saved, bool peeking, bool shown) =>
        Assert.Equal(shown, HomeDashboardRules.DrawerShownOpen(saved, peeking));

    [Fact]
    public void Drawer_ChevronPointsTheWayTheClickGoes()
    {
        Assert.Equal("\u2039", HomeDashboardRules.DrawerChevron(open: false));
        Assert.Equal("\u203A", HomeDashboardRules.DrawerChevron(open: true));
    }

    [Fact]
    public void Logo_LivesAtRest_AndRunsThreeTimesFasterOnHover()
    {
        Assert.Equal(0.35, HomeDashboardRules.LogoDrive(0));
        Assert.Equal(1.0, HomeDashboardRules.LogoDrive(1), 6);
        Assert.Equal(1.0, HomeDashboardRules.LogoDrive(5), 6);
        Assert.Equal(Math.Tau / 12, HomeDashboardRules.LogoPhaseRate(0), 9);
        Assert.Equal(3 * HomeDashboardRules.LogoPhaseRate(0), HomeDashboardRules.LogoPhaseRate(1), 9);
    }

    [Fact]
    public void Logo_EnergyRisesFasterThanItFalls()
    {
        double up = HomeDashboardRules.LogoEnergyStep(0, 1, 0.05);
        double down = 1 - HomeDashboardRules.LogoEnergyStep(1, 0, 0.05);
        Assert.True(up > down);
        Assert.InRange(up, 0, 1);
        // A long stall is clamped to one 100 ms step: no jump to the target after a hitch.
        Assert.Equal(HomeDashboardRules.LogoEnergyStep(0, 1, 0.1), HomeDashboardRules.LogoEnergyStep(0, 1, 3), 9);
    }

    [Theory]
    [InlineData("avares://CCP.Avalonia/Resources/logo2.png", true)]
    [InlineData("avares://CCP.Avalonia/Resources/logo.png", true)]
    [InlineData("C:/mods/mine/logo_custom.png", false)]
    [InlineData(null, false)]
    public void Logo_OnlyTheBundledWordmarksTakeTheDial(string? path, bool dial) =>
        Assert.Equal(dial, HomeDashboardRules.LogoTakesDial(path));

    [Fact]
    public void Bubbles_PaintAsWpf()
    {
        const uint lilac = 0xFFB79CFF;
        Assert.Equal(34, HomeDashboardRules.BubbleSize);
        Assert.Equal(8, HomeDashboardRules.BubbleGap);
        Assert.Equal(160, HomeDashboardRules.BubbleExpandMs);
        Assert.Equal(TimeSpan.FromSeconds(9), HomeDashboardRules.BubblePulseEvery);
        Assert.Equal(0x8CB79CFFu, HomeDashboardRules.BubbleBorder(lilac, 0));
        // Plain: the hue at 30% over the open base, opaque.
        uint hover = HomeDashboardRules.BubbleHover(lilac, 0);
        Assert.Equal(0xFFu, hover >> 24);
        Assert.Equal(HomeDashboardRules.Over(HomeDashboardRules.WithAlpha(lilac, 0.30), HomeDashboardRules.BubbleOpenBase), hover);
        // Filled (Logout's pink): lighten its own fill.
        const uint pink = 0xFFFF69B4;
        Assert.Equal(HomeDashboardRules.Lighten(pink, 0.14), HomeDashboardRules.BubbleHover(lilac, pink));
        Assert.Equal(HomeDashboardRules.Lighten(pink, 0.25), HomeDashboardRules.BubbleBorder(lilac, pink));
    }

    [Fact]
    public void Bubbles_ThePulseWalksTheVisibleOnes()
    {
        var vis = new[] { true, false, true };
        Assert.Equal((0, 1), HomeDashboardRules.NextPulse(0, vis));
        Assert.Equal((2, 0), HomeDashboardRules.NextPulse(1, vis));
        Assert.Equal((-1, 0), HomeDashboardRules.NextPulse(0, Array.Empty<bool>()));
        Assert.Equal(-1, HomeDashboardRules.NextPulse(1, new[] { false, false }).Pick);
    }

    [Theory]
    [InlineData(false, false, false)] // left opens
    [InlineData(true, false, true)]   // right toggles
    [InlineData(false, true, true)]   // swapped: left toggles
    [InlineData(true, true, false)]   // swapped: right opens
    public void Tiles_RightClickTogglesLeftClickOpens_UnlessSwapped(bool right, bool invert, bool toggles) =>
        Assert.Equal(toggles, HomeDashboardRules.GestureToggles(right, invert));
}
