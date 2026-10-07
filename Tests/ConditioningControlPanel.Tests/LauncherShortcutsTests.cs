using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The pure half of the desktop shortcut writer. The file name is what the user sees on the
/// desktop and the arguments are what <c>LauncherBoot.Decide</c> reads back, so a typo in either
/// is a shortcut that opens the wrong surface. Neither touches COM or the disk.
/// </summary>
public class LauncherShortcutsTests
{
    [Fact]
    public void Panel_shortcut_has_the_classic_name_and_the_panel_flag()
    {
        Assert.Equal(("Conditioning Control Panel.lnk", "--panel"), LauncherShortcuts.Describe(null, null));
        Assert.Equal(("Conditioning Control Panel.lnk", "--panel"), LauncherShortcuts.Describe("panel", "ignored"));
        Assert.Equal(("Conditioning Control Panel.lnk", "--panel"), LauncherShortcuts.Describe("  ", null));
    }

    [Fact]
    public void Game_shortcut_carries_the_title_and_the_game_flag()
    {
        var (file, args) = LauncherShortcuts.Describe("backroom", "The Back Room");
        Assert.Equal("CC Labs - The Back Room.lnk", file);
        Assert.Equal("--game backroom", args);
    }

    [Fact]
    public void Title_characters_a_file_name_cannot_carry_are_dropped()
    {
        var (file, _) = LauncherShortcuts.Describe("race", "Racing: Thoughts / Round <2>");
        Assert.Equal("CC Labs - Racing Thoughts  Round 2.lnk", file);
    }

    [Theory]
    [InlineData("backroom", "backroom.ico")]
    [InlineData(" piecebypiece ", "piecebypiece.ico")]
    [InlineData("intake", "intake.ico")]
    public void Game_shortcut_wears_its_own_icon_file(string id, string file)
    {
        Assert.Equal(file, LauncherShortcuts.IconFileName(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("panel")]
    [InlineData("  ")]
    [InlineData("a/b")]
    public void Panel_and_unsafe_ids_keep_the_exe_icon(string? id)
    {
        Assert.Null(LauncherShortcuts.IconFileName(id));
    }

    [Fact]
    public void Every_launcher_game_ships_an_icon_file()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", LauncherShortcuts.IconFolder)))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var folder = System.IO.Path.Combine(dir!.FullName, "ConditioningControlPanel", LauncherShortcuts.IconFolder);
        foreach (var game in LauncherCatalogue.Games)
        {
            var name = LauncherShortcuts.IconFileName(game.Id);
            Assert.True(name != null && System.IO.File.Exists(System.IO.Path.Combine(folder, name)),
                "missing icon for " + game.Id);
        }
    }

    [Fact]
    public void Blank_title_falls_back_to_the_id_and_the_id_is_trimmed()
    {
        var (file, args) = LauncherShortcuts.Describe(" dtrh ", "   ");
        Assert.Equal("CC Labs - dtrh.lnk", file);
        Assert.Equal("--game dtrh", args);
    }
}
