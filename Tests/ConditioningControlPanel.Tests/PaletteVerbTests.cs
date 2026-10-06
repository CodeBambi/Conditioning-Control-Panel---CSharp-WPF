using System;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish (2026-10-06): a palette row's breadcrumb names its verb honestly. Rows that start a
/// game say Launch, rows that open a dialog say Open, and no row shows a bare "Go to" with no path.
/// </summary>
public class PaletteVerbTests
{
    [Fact]
    public void Game_rows_launch_and_never_say_go_to()
    {
        var games = SettingsPaletteIndex.All.Where(e => e.Id.StartsWith("game.", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(games);
        foreach (var g in games)
        {
            Assert.Equal("launcher_panel_launch", g.ContextKeys[0]);
            Assert.DoesNotContain("set2_palette_group_go_to", g.ContextKeys);
        }
    }

    [Fact]
    public void Library_launchers_say_open()
    {
        foreach (var e in SettingsPaletteIndex.All.Where(e => e.Id.StartsWith("launch.", StringComparison.Ordinal)))
            Assert.Equal("btn_open", e.ContextKeys[0]);
    }

    [Fact]
    public void No_row_has_a_bare_go_to_breadcrumb()
    {
        var bare = SettingsPaletteIndex.All
            .Where(e => e.ContextKeys.Length == 1 && e.ContextKeys[0] == "set2_palette_group_go_to"
                        && string.IsNullOrEmpty(e.TabKey))
            .Select(e => e.Id).ToList();
        Assert.True(bare.Count == 0, "rows with an empty path: " + string.Join(", ", bare));
    }

    [Fact]
    public void The_games_row_navigates_to_the_games_zone()
    {
        var row = SettingsPaletteIndex.All.Single(e => e.Id == "tab.play");
        Assert.Equal("play", row.TabKey);
        Assert.Equal("games", row.PlayZone);
        Assert.Equal("nav_tab_games", row.LabelKey);
        Assert.Null(row.GameId);
    }

    [Fact]
    public void The_cc_labs_row_opens_the_launcher()
    {
        var row = SettingsPaletteIndex.All.Single(e => e.Id == "chrome.cclabs");
        Assert.True(row.OpensLauncher);
        Assert.Equal("btn_open", row.ContextKeys[0]);
    }

    [Fact]
    public void Old_name_clones_keep_their_launcher_key()
    {
        foreach (var e in SettingsPaletteIndex.All.Where(e => e.Id.StartsWith("launch.", StringComparison.Ordinal)))
            Assert.False(string.IsNullOrEmpty(e.LauncherKey), e.Id + " lost its LauncherKey");
    }
}
