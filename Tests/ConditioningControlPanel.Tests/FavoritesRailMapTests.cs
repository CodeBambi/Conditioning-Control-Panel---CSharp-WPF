using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The favorites rail's pin map (MainWindow.FavoritesRail.cs, FavoritePinMap) joins x:Names in
/// MainWindow.xaml / PlayTabView.xaml to Ctrl+K palette row ids. Both halves can rot silently:
/// a renamed button compiles and just loses its right-click, and a retired palette row leaves
/// a chip nobody can build. Source-text reads, because MainWindow cannot be instantiated here.
/// </summary>
public class FavoritesRailMapTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    private static List<(string Element, string Id)> PinMap()
    {
        var src = Read("MainWindow", "MainWindow.FavoritesRail.cs");
        var start = src.IndexOf("FavoritePinMap =", StringComparison.Ordinal);
        Assert.True(start > 0, "FavoritePinMap not found");
        var end = src.IndexOf("};", start, StringComparison.Ordinal);
        var block = src.Substring(start, end - start);
        var rows = Regex.Matches(block, @"\(""(\w+)"",\s*""([\w.]+)""\)")
                        .Cast<Match>().Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
        Assert.True(rows.Count >= 30, "FavoritePinMap parsed as only " + rows.Count + " rows - the regex has rotted");
        return rows;
    }

    [Fact]
    public void Every_mapped_element_exists_in_its_view()
    {
        var main = Read("MainWindow", "MainWindow.xaml");
        var play = Read("Views", "Tabs", "PlayTabView.xaml");
        foreach (var (element, _) in PinMap())
        {
            var view = element.StartsWith("BtnPlay", StringComparison.Ordinal) ? play : main;
            Assert.True(view.Contains("x:Name=\"" + element + "\"", StringComparison.Ordinal),
                $"FavoritePinMap names \"{element}\" but no such x:Name exists in its view");
        }
    }

    [Fact]
    public void Every_mapped_id_is_a_palette_destination()
    {
        var ids = new HashSet<string>(SettingsPaletteIndex.All.Select(e => e.Id), StringComparer.Ordinal);
        foreach (var (element, id) in PinMap())
        {
            Assert.True(FavoritesRailRule.IsDestination(id), $"{element} -> {id} is not a destination id");
            Assert.True(ids.Contains(id), $"{element} -> {id} has no Ctrl+K palette row");
        }
    }

    [Fact]
    public void Every_rail_entry_button_is_pinnable()
    {
        // Every NavEntryButton / NavDoorButton in the rail should have a row, except the launcher
        // doors (WebApp opens a browser, no palette row) and the search button.
        var main = Read("MainWindow", "MainWindow.xaml");
        var names = Regex.Matches(main, @"<Button x:Name=""(\w+)""[^>]*Style=""\{StaticResource Nav(?:Entry|Door)Button\}""")
                         .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        Assert.True(names.Count >= 30, "rail buttons parsed as only " + names.Count);
        var mapped = new HashSet<string>(PinMap().Select(r => r.Element), StringComparer.Ordinal);
        // DoorWebApp has no tab to pin (expand-only door); BtnNavGoon is a launcher with no
        // palette row yet (follow-up: a launch verb in the registry).
        var exempt = new HashSet<string>(StringComparer.Ordinal) { "DoorWebApp", "BtnNavGoon" };
        var missing = names.Where(n => !mapped.Contains(n) && !exempt.Contains(n)).ToList();
        Assert.True(missing.Count == 0, "rail buttons with no pin entry: " + string.Join(", ", missing));
    }
}
