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

    [Fact]
    public void Blank_title_falls_back_to_the_id_and_the_id_is_trimmed()
    {
        var (file, args) = LauncherShortcuts.Describe(" dtrh ", "   ");
        Assert.Equal("CC Labs - dtrh.lnk", file);
        Assert.Equal("--game dtrh", args);
    }
}
