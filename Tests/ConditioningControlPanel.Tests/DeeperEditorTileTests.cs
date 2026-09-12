using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The dashboard mosaic's DEEPER EDITOR tile (owner, 2026-09-12: "get rid of the just drop tile,
/// put there the Deeper editor").
///
/// <para>It took the slot the nameless Just Drop tease tile held. Three things about that swap are
/// worth a test rather than a comment, because all three compile either way:</para>
/// <list type="number">
/// <item>the tile carries no right-click toggle and no dim-when-off opt-in - an editor has no
/// "on", and a card that greys out when nothing is running would be telling the user something
/// untrue;</item>
/// <item>its click NAVIGATES to the Deeper page and does nothing else. The first cut opened the
/// editor on a blank enhancement and the owner corrected it on the desk run, 2026-09-12: "the
/// deeper editor should link and open the deeper page, not the editor". A front-page tile that
/// puts a modal dialog on screen is a tile you cannot browse past, so this is pinned;</item>
/// <item>the retired tease wiring has to be gone rather than merely unreferenced - a leftover
/// ApplyTeaseCard call against a card that no longer exists is a silent no-op forever.</item>
/// </list>
/// Source-text reads throughout: MainWindow cannot be instantiated in a unit test.
/// </summary>
public class DeeperEditorTileTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string AppDir() => Path.Combine(RepoRoot(), "ConditioningControlPanel");

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { AppDir() }.Concat(parts).ToArray()));

    private static string Home() => ReadSource("Views", "Tabs", "SettingsTabView.xaml");

    /// <summary>The tile's whole XAML element, so an attribute assertion cannot be satisfied by
    /// some other card further down the wall.</summary>
    private static string TileElement()
    {
        var home = Home();
        var start = home.IndexOf("x:Name=\"CardDeeperEditor\"", StringComparison.Ordinal);
        Assert.True(start > 0, "the Home mosaic has no CardDeeperEditor tile");
        var open = home.LastIndexOf("<features:FeatureCard", start, StringComparison.Ordinal);
        var end = home.IndexOf("</features:FeatureCard>", start, StringComparison.Ordinal);
        Assert.True(open >= 0 && end > open, "CardDeeperEditor's element never closes");
        return home.Substring(open, end - open);
    }

    // ---------------------------------------------------------------- the slot

    [Fact]
    public void The_editor_tile_holds_the_slot_the_tease_tile_had()
    {
        var tile = TileElement();
        Assert.Contains("Grid.Row=\"1\"", tile, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"0\"", tile, StringComparison.Ordinal);
        Assert.Contains("features/deeper_editor.png", tile, StringComparison.Ordinal);
        Assert.Contains("Click=\"CardDeeperEditor_Click\"", tile, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_on_the_wall_is_called_CardJustDrop_any_more()
    {
        // The tile was Just Drop's LAST dashboard surface. The rail row, the Exclusives shelf and
        // the Ctrl+K row are untouched by this change and are not what this asserts.
        Assert.DoesNotContain("CardJustDrop", Home(), StringComparison.Ordinal);
        Assert.DoesNotContain("CardJustDrop", ReadSource("Views", "Tabs", "SettingsTabView.xaml.cs"),
                              StringComparison.Ordinal);
        Assert.DoesNotContain("CardJustDrop", ReadSource("MainWindow", "MainWindow.Presets.cs"),
                              StringComparison.Ordinal);
    }

    [Fact]
    public void The_tile_takes_no_toggle()
    {
        var tile = TileElement();
        // FeatureCard.OnRightClick raises ToggleRequested on every card. With no handler the
        // gesture is a no-op here, which is the intended answer for a tool: nothing to switch on.
        Assert.DoesNotContain("ToggleRequested", tile, StringComparison.Ordinal);
        // No dim-when-off opt-in either, so CardMuteRule.ShouldMute is false for this tile
        // whatever the session is doing.
        Assert.DoesNotContain("DimWhenInactive", tile, StringComparison.Ordinal);
        // And no tease costume: this is a named tile with a legible picture.
        Assert.DoesNotContain("TeaseTier", tile, StringComparison.Ordinal);
    }

    [Fact]
    public void The_click_opens_the_Deeper_page_and_nothing_else()
    {
        Assert.Contains("mw.CardDeeperEditor_Click", ReadSource("Views", "Tabs", "SettingsTabView.xaml.cs"),
                        StringComparison.Ordinal);

        var presets = ReadSource("MainWindow", "MainWindow.Presets.cs");
        var at = presets.IndexOf("internal void CardDeeperEditor_Click", StringComparison.Ordinal);
        Assert.True(at > 0, "MainWindow has no CardDeeperEditor_Click");
        var body = presets.Substring(at, Math.Min(200, presets.Length - at));

        // Whitespace-insensitive, but the destination is pinned: this is a door to a page.
        Assert.Contains("ShowTab(\"deeper\")", body, StringComparison.Ordinal);
        // The owner's correction, kept honest. Opening the editor from the wall put a modal
        // dialog on the front page; the tile lands on the room and lets the user choose.
        Assert.DoesNotContain("BtnDeeperNewEnhancement_Click", body, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the retired tease

    [Fact]
    public void The_tease_tile_wiring_is_gone_rather_than_merely_unused()
    {
        Assert.False(File.Exists(Path.Combine(AppDir(), "MainWindow", "MainWindow.TeaseCard.cs")),
                     "MainWindow.TeaseCard.cs is still here - the tile it painted no longer exists");

        foreach (var file in Directory.EnumerateFiles(AppDir(), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.False(text.Contains("ApplyTeaseCard", StringComparison.Ordinal),
                         $"{Path.GetFileName(file)} still calls ApplyTeaseCard");
        }
    }

    [Fact]
    public void The_generic_tease_costume_survived_the_tile()
    {
        // Deliberately kept (and still covered by TeaseCardRenderTests): the costume is a card
        // property plus a generic popup, so the next unannounced feature does not need either
        // written again. Only the Just-Drop-shaped wiring went.
        Assert.Contains("TeaseTier", ReadSource("Features", "FeatureCard.xaml.cs"), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(AppDir(), "Windows", "TeaseRevealPopup.xaml.cs")));
    }

    // ---------------------------------------------------------------- the art

    [Fact]
    public void The_tiles_art_ships()
    {
        var art = Path.Combine(AppDir(), "Resources", "features", "deeper_editor.png");
        Assert.True(File.Exists(art), "Resources/features/deeper_editor.png is missing");

        // There is no Resources\**\*.png glob in this project, so a file on disk that no
        // <Resource> item covers resolves to nothing at runtime and the tile paints empty.
        var csproj = File.ReadAllText(Path.Combine(AppDir(), "ConditioningControlPanel.csproj"));
        Assert.True(csproj.Contains("Resources\\features\\*.png", StringComparison.OrdinalIgnoreCase)
                    || csproj.Contains("Resources\\features\\deeper_editor.png", StringComparison.OrdinalIgnoreCase),
                    "no <Resource> item in the csproj covers the tile's art");
    }

    // ---------------------------------------------------------------- the palette and the rail

    [Fact]
    public void The_tile_and_Ctrl_K_land_in_the_same_room()
    {
        // tab.deeper already exists and already carries the page's picture, the rail chip and the
        // RECENT entry. The tile navigating there means there is nothing else to register: a
        // second row for the same destination would put two chips in the rail for one room.
        var row = SettingsPaletteIndex.All.FirstOrDefault(e => e.Id == "tab.deeper");
        Assert.True(row != null, "the Deeper page lost its Ctrl+K row");
        Assert.Equal("deeper", row!.TabKey);
        Assert.True(FavoritesRailArt.Knows("tab.deeper"), "the Deeper chip has no art decision");
    }

    [Fact]
    public void No_second_palette_row_survived_the_correction()
    {
        // The first cut registered launch.deepereditor, which only made sense while the tile
        // pressed BtnDeeperNewEnhancement. Once it navigates, the row, its art entry and its pin
        // map entry are all a duplicate of tab.deeper.
        Assert.DoesNotContain(SettingsPaletteIndex.All, e => e.Id == "launch.deepereditor");
        Assert.False(FavoritesRailArt.Knows("launch.deepereditor"));
        Assert.DoesNotContain("deepereditor", ReadSource("MainWindow", "MainWindow.FavoritesRail.cs"),
                              StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the copy

    [Fact]
    public void The_tiles_three_strings_reached_all_nine_languages()
    {
        var keys = new[] { "dash_deeper_editor_title", "dash_deeper_editor_blurb", "dash_deeper_editor_tip" };
        var missing = new List<string>();
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var key in keys)
                if (!file.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                    missing.Add($"{lang}.json:{key}");
        }
        Assert.True(missing.Count == 0, "Deeper editor tile copy missing from: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_retired_tease_tooltip_left_all_nine_files()
    {
        // It was read by ApplyTeaseCard and nowhere else. The popup's three keys stayed, because
        // the popup stayed - see TeaseCardRenderTests.
        var left = CompanionLocMasters.Languages
            .Where(lang => CompanionLocMasters.For(lang).ContainsKey("tease_card_tooltip"))
            .ToList();
        Assert.True(left.Count == 0, "tease_card_tooltip is still in: " + string.Join(", ", left));
    }
}
