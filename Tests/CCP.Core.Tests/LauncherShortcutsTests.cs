using System.IO;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// PORTED from WPF 7.1.5 LauncherShortcutsTests: the pure half of the desktop shortcut writer. The
/// file name is what the user sees on the desktop and the arguments are what LauncherBoot.Decide
/// reads back, so a typo in either is a shortcut that opens the wrong surface. Plus the Linux twin.
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
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "launcher-icons")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var folder = Path.Combine(dir!.FullName, "Assets", "launcher-icons");
        foreach (var card in LauncherCards.All)
        {
            var name = LauncherShortcuts.IconFileName(card.Id);
            Assert.True(name != null && File.Exists(Path.Combine(folder, name)), "missing icon for " + card.Id);
        }
    }

    [Fact]
    public void Blank_title_falls_back_to_the_id_and_the_id_is_trimmed()
    {
        var (file, args) = LauncherShortcuts.Describe(" dtrh ", "   ");
        Assert.Equal("CC Labs - dtrh.lnk", file);
        Assert.Equal("--game dtrh", args);
    }

    [Fact]
    public void Linux_entry_carries_the_same_arguments_and_a_stable_file_name()
    {
        const string exec = "\"/opt/ccp/CCP.Avalonia\"";
        var (file, text) = LauncherShortcuts.DescribeDesktopEntry("backroom", "The Back Room", exec,
            "/opt/ccp/Resources/launcher-icons/backroom.ico");
        Assert.Equal("cclabs-backroom.desktop", file);
        Assert.Contains("[Desktop Entry]\n", text);
        Assert.Contains("Type=Application\n", text);
        Assert.Contains("Name=CC Labs - The Back Room\n", text);
        Assert.Contains("Exec=" + exec + " --game backroom\n", text);
        Assert.Contains("Icon=/opt/ccp/Resources/launcher-icons/backroom.ico\n", text);
        Assert.DoesNotContain("\r", text);

        var (panelFile, panelText) = LauncherShortcuts.DescribeDesktopEntry(null, null, exec, null);
        Assert.Equal("cclabs-panel.desktop", panelFile);
        Assert.Contains("Name=Conditioning Control Panel\n", panelText);
        Assert.Contains(" --panel\n", panelText);
        Assert.DoesNotContain("Icon=", panelText);
    }
}
