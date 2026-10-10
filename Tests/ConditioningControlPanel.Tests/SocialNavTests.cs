using System;
using System.IO;
using System.Linq;
using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>SOCIAL lane wiring (nav rework 2026-10-06): registration, the rail badge lease,
/// the All-Time leaderboard default, the Lobby's empty-state host doors.</summary>
public class SocialNavTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    [Fact]
    public void Friends_and_leash_register_as_social_pages()
    {
        var src = Read("MainWindow", "MainWindow.SocialTabs.cs");
        Assert.Contains("new NavTabHost(\"friends\"", src);
        Assert.Contains("new NavTabHost(\"leash\"", src);
        Assert.Equal(NavSections.Social, NavSections.SectionForTab("friends"));
        Assert.Equal(NavSections.Social, NavSections.SectionForTab("leash"));
    }

    [Theory]
    [InlineData(true, WindowState.Normal, true)]
    [InlineData(true, WindowState.Maximized, true)]
    [InlineData(true, WindowState.Minimized, false)]
    [InlineData(false, WindowState.Normal, false)]
    public void Badge_lease_is_held_only_while_the_panel_is_on_screen(bool visible, WindowState state, bool wanted)
        => Assert.Equal(wanted, MainWindow.LobbyBadgeWanted(visible, state));

    [Fact]
    public void Lobby_feeds_the_social_badge()
    {
        var src = Read("MainWindow", "MainWindow.Lobby.cs");
        Assert.Contains("NavBadges.Set(NavSections.Social, snap.OpenCount)", src);
        Assert.Contains("_lobbyBadgeLease ??= lobby.Watch()", src);
        Assert.Contains("_lobbyBadgeLease?.Dispose()", src);
        Assert.Contains("HookLobbyBadge();", Read("MainWindow", "MainWindow.SocialTabs.cs"));
    }

    [Fact]
    public void Leaderboard_opens_on_all_time_with_no_season_chrome()
    {
        Assert.Equal("all-time", MainWindow.LeaderboardDefaultMode);
        Assert.Contains("CurrentMode { get; private set; } = \"all-time\"",
            Read("Services", "Progression", "LeaderboardService.cs"));
        var tab = Read("Views", "Tabs", "LeaderboardTabView.xaml.cs");
        foreach (var gone in new[] { "lb_season_ends_in", "lb_season_ended", "section_seasons", "SeasonTitle" })
            Assert.DoesNotContain(gone, tab);
        var xaml = Read("Views", "Tabs", "LeaderboardTabView.xaml");
        Assert.Contains("<Border x:Name=\"SeasonRecapHost\" x:FieldModifier=\"internal\" Visibility=\"Collapsed\">", xaml);
    }

    [Fact]
    public void Leaderboard_header_names_the_board()
    {
        Assert.Equal(Loc.Get("lb_all_time_title"), LeaderboardTabView.HeaderText(true).Title);
        Assert.Equal(Loc.Get("social_lb_month_title"), LeaderboardTabView.HeaderText(false).Title);
    }

    [Fact]
    public void Lobby_empty_state_carries_the_host_doors()
    {
        var xaml = Read("Views", "Tabs", "AvailableSubjectsTabView.xaml");
        Assert.Contains("x:Name=\"BtnEmptyHostChess\"", xaml);
        Assert.Contains("x:Name=\"BtnEmptyHostGoon\"", xaml);
    }
}
