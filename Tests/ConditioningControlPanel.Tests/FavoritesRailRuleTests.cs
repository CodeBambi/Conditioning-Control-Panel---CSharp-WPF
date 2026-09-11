using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The FAVORITES + RECENT rail (owner decision 2026-09-11): caps, dedupe, order, unpin, and the
/// join from a ShowTab key to a palette row id. Pure list rules - the chips are MainWindow's.
/// </summary>
public class FavoritesRailRuleTests
{
    [Theory]
    [InlineData("tab.deeper", true)]
    [InlineData("door.play", true)]
    [InlineData("launch.mods", true)]
    [InlineData("card.arcademy", true)]
    [InlineData("section.audio", false)]
    [InlineData("set.master_volume", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_doors_tabs_launchers_and_cards_are_destinations(string? id, bool expected)
        => Assert.Equal(expected, FavoritesRailRule.IsDestination(id));

    [Fact]
    public void Pin_appends_dedupes_and_stops_at_the_cap()
    {
        var favs = new List<string>();
        Assert.True(FavoritesRailRule.TryPin(favs, "tab.deeper"));
        Assert.False(FavoritesRailRule.TryPin(favs, "tab.deeper"));
        Assert.True(FavoritesRailRule.TryPin(favs, "tab.quests"));
        Assert.Equal(new[] { "tab.deeper", "tab.quests" }, favs);

        for (int i = 0; favs.Count < FavoritesRailRule.FavoritesCap; i++)
            Assert.True(FavoritesRailRule.TryPin(favs, "tab.filler" + i));
        Assert.True(FavoritesRailRule.IsFull(favs));
        Assert.False(FavoritesRailRule.TryPin(favs, "tab.one_too_many"));
        Assert.Equal(FavoritesRailRule.FavoritesCap, favs.Count);
    }

    [Fact]
    public void Pin_refuses_a_non_destination()
    {
        var favs = new List<string>();
        Assert.False(FavoritesRailRule.TryPin(favs, "set.master_volume"));
        Assert.Empty(favs);
    }

    [Fact]
    public void Unpin_removes_only_that_id_and_keeps_the_order()
    {
        var favs = new List<string> { "tab.deeper", "tab.quests", "door.play" };
        Assert.True(FavoritesRailRule.Unpin(favs, "tab.quests"));
        Assert.False(FavoritesRailRule.Unpin(favs, "tab.quests"));
        Assert.Equal(new[] { "tab.deeper", "door.play" }, favs);
        Assert.True(FavoritesRailRule.IsPinned(favs, "door.play"));
        Assert.False(FavoritesRailRule.IsPinned(favs, "tab.quests"));
    }

    [Fact]
    public void Recent_is_most_recent_first_deduped_and_capped_at_five()
    {
        var recent = new List<string>();
        foreach (var id in new[] { "tab.a", "tab.b", "tab.c", "tab.a", "tab.d", "tab.e", "tab.f" })
            FavoritesRailRule.NoteOpened(recent, id);

        Assert.Equal(FavoritesRailRule.RecentCap, recent.Count);
        Assert.Equal(new[] { "tab.f", "tab.e", "tab.d", "tab.a", "tab.c" }, recent);
    }

    [Fact]
    public void Reopening_the_front_entry_changes_nothing()
    {
        var recent = new List<string> { "tab.a", "tab.b" };
        Assert.False(FavoritesRailRule.NoteOpened(recent, "tab.a"));
        Assert.Equal(new[] { "tab.a", "tab.b" }, recent);
        Assert.False(FavoritesRailRule.NoteOpened(recent, "section.audio"));
    }

    [Fact]
    public void Recent_display_hides_pinned_and_unknown_ids()
    {
        var recent = new List<string> { "tab.a", "tab.gone", "tab.b", "tab.c" };
        var favs = new List<string> { "tab.b" };
        var shown = FavoritesRailRule.RecentForDisplay(recent, favs, id => id != "tab.gone");
        Assert.Equal(new[] { "tab.a", "tab.c" }, shown);
    }

    [Theory]
    [InlineData("deeper", "tab.deeper")]
    [InlineData("Deeper", "tab.deeper")]
    [InlineData("fyp", "tab.fyp")]
    [InlineData("justdrop", "door.justdrop")]
    [InlineData("settings", null)]
    [InlineData("progression", null)]
    [InlineData("lab", null)]
    [InlineData("patreon", null)]
    [InlineData("no_such_key", null)]
    public void A_show_tab_key_resolves_to_its_palette_row(string tab, string? expected)
        => Assert.Equal(expected, FavoritesRailRule.DestinationIdForTab(tab, SettingsPaletteIndex.All));

    [Fact]
    public void Every_nav_door_and_tab_row_is_a_destination()
    {
        var rows = SettingsPaletteIndex.All
            .Where(e => e.Id.StartsWith("door.") || e.Id.StartsWith("tab.") || e.Id.StartsWith("launch.") || e.Id.StartsWith("card."))
            .ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, e => Assert.True(FavoritesRailRule.IsDestination(e.Id), e.Id));
    }
}
