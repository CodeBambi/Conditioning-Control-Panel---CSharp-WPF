using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The open launcher redraws its tiles on a mod switch (#1292) and when a grant flips a tile's
/// reveal (#1305). A hidden launcher leaves it to its next show.
/// </summary>
public class LauncherTileRefreshTests
{
    private static Dictionary<string, bool> Built(params (string Id, bool Revealed)[] tiles)
    {
        var d = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, r) in tiles) d[id] = r;
        return d;
    }

    private static IEnumerable<KeyValuePair<string, bool>> Now(params (string Id, bool Revealed)[] tiles)
    {
        foreach (var (id, r) in tiles) yield return new KeyValuePair<string, bool>(id, r);
    }

    [Fact]
    public void Mod_switch_redraws_an_open_launcher_even_when_no_reveal_moved()
    {
        var built = Built(("race", true), ("backroom", true));
        Assert.True(LauncherTileRefresh.ShouldRebuild(LauncherTileTrigger.ModChanged, true, built,
            Now(("race", true), ("backroom", true))));
    }

    [Theory]
    [InlineData(LauncherTileTrigger.ModChanged)]
    [InlineData(LauncherTileTrigger.GrantsChanged)]
    public void A_hidden_launcher_never_redraws(LauncherTileTrigger trigger)
    {
        var built = Built(("race", false));
        Assert.False(LauncherTileRefresh.ShouldRebuild(trigger, false, built, Now(("race", true))));
    }

    [Fact]
    public void A_bought_race_redraws_the_mystery_card()
    {
        var built = Built(("race", false), ("backroom", true));
        Assert.True(LauncherTileRefresh.ShouldRebuild(LauncherTileTrigger.GrantsChanged, true, built,
            Now(("race", true), ("backroom", true))));
    }

    [Fact]
    public void Grants_that_move_no_reveal_leave_the_grid_alone()
    {
        var built = Built(("race", true), ("backroom", true));
        Assert.False(LauncherTileRefresh.ShouldRebuild(LauncherTileTrigger.GrantsChanged, true, built,
            Now(("race", true), ("backroom", true))));
    }

    [Fact]
    public void A_tile_the_last_build_never_drew_counts_as_a_flip()
    {
        var built = Built(("backroom", true));
        Assert.True(LauncherTileRefresh.ShouldRebuild(LauncherTileTrigger.GrantsChanged, true, built,
            Now(("backroom", true), ("race", true))));
    }
}
