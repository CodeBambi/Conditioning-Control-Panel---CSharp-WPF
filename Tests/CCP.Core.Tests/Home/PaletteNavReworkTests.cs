using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests.Home;

/// <summary>WPF 7.1.5 AddNavRework rows: pills, Settings › Monitors, rack modules, launcher games.</summary>
public class PaletteNavReworkTests
{
    [Theory]
    [InlineData("tab.personality", "personality")]
    [InlineData("tab.companionai", "companionai")]
    [InlineData("tab.friends", "friends")]
    [InlineData("tab.leash", "leash")]
    [InlineData("tab.ramp", "ramp")]
    public void Rework_pills_are_rows(string id, string tab)
    {
        var row = SettingsPaletteIndex.All.Single(e => e.Id == id);
        Assert.Equal(tab, row.TabKey);
    }

    [Fact]
    public void Rack_rows_open_their_module_and_drop_the_label_glyph()
    {
        var rows = SettingsPaletteIndex.All.Where(e => e.Id.StartsWith("rack.")).ToList();
        Assert.Equal(12, rows.Count);
        Assert.All(rows, r => Assert.Equal(r.Id.Substring(5), r.RackKey));
        Assert.Equal("Bubble Pop", SettingsPaletteIndex.StripGlyph("🫧 Bubble Pop"));
        Assert.Contains(SettingsPaletteIndex.Search("bubble pop"), e => e.Id == "rack.bubbles");
    }

    [Fact]
    public void Game_rows_list_only_the_games_this_head_can_start()
    {
        var games = SettingsPaletteIndex.All.Where(e => e.Id.StartsWith("game.")).ToList();
        Assert.Equal(9, games.Count);
        Assert.All(games, g => Assert.Equal(g.Id.Substring(5), g.GameId));

        var before = SettingsPaletteIndex.GameAvailableProvider;
        try
        {
            SettingsPaletteIndex.GameAvailableProvider = null;
            Assert.All(games, g => Assert.False(g.Available)); // fail-closed
            SettingsPaletteIndex.GameAvailableProvider = id => id == "intake";
            Assert.True(games.Single(g => g.GameId == "intake").Available);
            Assert.False(games.Single(g => g.GameId == "backroom").Available);
            SettingsPaletteIndex.GameAvailableProvider = _ => throw new System.InvalidOperationException();
            Assert.False(games.Single(g => g.GameId == "intake").Available);
        }
        finally { SettingsPaletteIndex.GameAvailableProvider = before; }
    }
}
