using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.1.5 lane A: a red "7" on Social read as seven unread messages (tier-2). The count badge wears
/// the section hue, dims once seen and brightens only when the count rises past what was seen.
/// Also pins the whole-card launcher click and the window that grows to seat every tile row.
/// </summary>
public class NavBadgeSeenTests
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

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(3, 0, true)]
    [InlineData(3, 3, false)]
    [InlineData(2, 3, false)]
    [InlineData(4, 3, true)]
    public void Fresh_only_when_the_count_rose_past_what_was_seen(int count, int seen, bool fresh)
        => Assert.Equal(fresh, NavBadges.IsFresh(count, seen));

    [Theory]
    [InlineData(7, 0, 0)]
    [InlineData(3, 7, 3)]
    [InlineData(5, 2, 5)]
    public void A_cleared_badge_forgets_what_was_seen(int seen, int count, int expected)
        => Assert.Equal(expected, NavBadges.SeenAfterCount(seen, count));

    [Fact]
    public void Seen_badge_dims_and_brightens_again_on_a_rise()
    {
        const string key = "test-715-seen";   // own key: NavBadges is process-wide
        NavBadges.Set(key, 7);
        Assert.True(NavBadges.IsFresh(key));
        NavBadges.MarkSeen(key);
        Assert.False(NavBadges.IsFresh(key));
        NavBadges.Set(key, 6);
        Assert.False(NavBadges.IsFresh(key));   // a table closed: nothing new
        NavBadges.Set(key, 8);
        Assert.True(NavBadges.IsFresh(key));    // more than they saw
        NavBadges.MarkSeen(key);
        NavBadges.Set(key, 0);
        NavBadges.Set(key, 1);
        Assert.True(NavBadges.IsFresh(key));    // cleared, then a new one
        NavBadges.Set(key, 0);
    }

    [Fact]
    public void Seen_opacity_is_dim_fresh_is_full()
    {
        Assert.Equal(1.0, NavRailRules.BadgeOpacity(true));
        Assert.True(NavRailRules.BadgeOpacity(false) < 0.6);
    }

    [Fact]
    public void Rail_badge_wears_the_section_hue_and_visiting_marks_it_seen()
    {
        var src = Read("MainWindow", "MainWindow.NavRail.cs");
        Assert.Contains("NavStripRules.Accent(row.Section)", src);
        Assert.Contains("NavBadges.MarkSeen(section)", src);
        Assert.Contains("NavRailRules.BadgeOpacity(fresh)", src);
        var lobby = Read("Windows", "Launcher", "LauncherWindow.Lobby.cs");
        Assert.Contains("NavBadges.MarkSeen(NavSections.Social)", lobby);
        Assert.DoesNotContain("0x33, 0xFF, 0x5F, 0xA2", lobby);
    }

    [Fact]
    public void Whole_launcher_tile_launches_through_the_play_path()
    {
        var src = Read("Windows", "Launcher", "LauncherWindow.Tiles.cs");
        Assert.Contains("play.Click += (_, _) => Launch();", src);
        Assert.Contains("tile.MouseLeftButtonUp += (_, e) =>", src);
        Assert.DoesNotContain("The whole card is the ask", src);
    }

    [Theory]
    // 9 tiles at the default 1280x800: the overflow is added, under a 1080p work area.
    [InlineData(800, 658, 852, 1032, 995)]
    // A short screen (125% at 1080p): capped at the work area, the scroller stays.
    [InlineData(800, 658, 852, 826, 826)]
    // Already fits: unchanged.
    [InlineData(800, 658, 568, 1032, 800)]
    // Never shrinks, even if the work area reads smaller than the window.
    [InlineData(800, 658, 852, 700, 800)]
    public void Window_grows_to_seat_every_tile_row(double window, double available, double grid, double workArea, double expected)
        => Assert.Equal(expected, LauncherGridLayout.FitWindowHeight(window, available, grid, workArea));
}
