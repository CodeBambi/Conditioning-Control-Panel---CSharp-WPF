using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Ctrl+K palette vs. the nav rail — the drift guard.
///
/// <para>The palette index (<c>Services/SettingsPaletteIndex.cs</c>) was authored in Phase 2 and
/// then had to keep up with four more phases of rooms moving. It did not: Phase 4 gave the Studio
/// door a first entry of its own (the effects rack), so <c>NavDoorMap</c>'s default tab for that
/// door became <c>"studio"</c> while the palette's Studio row still navigated to <c>"presets"</c>,
/// and the rack had no row at all — 15 modules unreachable from the search box. Both failures
/// compile, both are invisible in review, and the index's own comment ("a door's entry navigates
/// to that door's DEFAULT tab, exactly like clicking its header does") went quietly false.</para>
///
/// <para>These are source-text reads for the rail halves (NavDoorMap and ShowTab's switch live in
/// MainWindow, which cannot be instantiated in a unit test) crossed against the real
/// <see cref="SettingsPaletteIndex"/>, which is a pure static list with no WPF dependency.</para>
/// </summary>
public class PaletteDoorParityTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    private static string TabNavigationSource() => ReadSource("MainWindow", "MainWindow.TabNavigation.cs");

    /// <summary>
    /// The palette names the pinned Settings door "settings" (its id is cosmetic); NavDoorMap keys
    /// the same door "appsettings", because that is its ShowTab key. One rename, declared once.
    /// </summary>
    private static string DoorIdToRailDoor(string paletteDoorId) =>
        paletteDoorId == "settings" ? "appsettings" : paletteDoorId;

    /// <summary>
    /// ShowTab keys that deliberately have no palette row: each is a legacy alias that resolves
    /// onto another row's destination, and giving it a row would claim the rail has a room for it.
    /// "lab" lands on the Play wall; "progression" rides with the Dashboard.
    /// </summary>
    private static readonly HashSet<string> AliasTabKeys =
        new(StringComparer.Ordinal) { "lab", "progression" };

    /// <summary>(door, defaultTab) straight out of MainWindow.TabNavigation.cs's NavDoorMap.</summary>
    private static List<(string Door, string DefaultTab)> RailDoors()
    {
        var src = TabNavigationSource();
        var start = src.IndexOf("NavDoorMap =", StringComparison.Ordinal);
        Assert.True(start > 0, "NavDoorMap not found in MainWindow.TabNavigation.cs");
        var end = src.IndexOf("};", start, StringComparison.Ordinal);
        Assert.True(end > start, "NavDoorMap's initializer never closes");

        var block = src.Substring(start, end - start);
        var doors = new List<(string, string)>();
        foreach (Match m in Regex.Matches(block, @"\(""(\w+)"",\s*""(\w+)"",\s*new\[\]"))
            doors.Add((m.Groups[1].Value, m.Groups[2].Value));

        Assert.True(doors.Count >= 6, "NavDoorMap parsed as only " + doors.Count + " doors — the regex has rotted");
        return doors;
    }

    [Fact]
    public void EveryDoorRowNavigatesWhereItsHeaderDoes()
    {
        var paletteDoors = SettingsPaletteIndex.All
            .Where(e => e.Id.StartsWith("door.", StringComparison.Ordinal))
            .ToDictionary(e => DoorIdToRailDoor(e.Id.Substring("door.".Length)), e => e.TabKey, StringComparer.Ordinal);

        foreach (var (door, defaultTab) in RailDoors())
        {
            Assert.True(paletteDoors.ContainsKey(door),
                $"the Ctrl+K palette has no door row for the \"{door}\" door");
            Assert.True(string.Equals(paletteDoors[door], defaultTab, StringComparison.Ordinal),
                $"palette door row \"{door}\" navigates to \"{paletteDoors[door]}\" but clicking its rail header " +
                $"goes to \"{defaultTab}\" — the two must agree (NavDoorMap is the authority)");
        }
    }

    [Fact]
    public void EveryLiveShowTabKeyIsReachableFromThePalette()
    {
        // Line-anchored so commented-out and doc-comment mentions of `case "lab"` are not counted;
        // ShowTab's switch is the only `case "..."` block in this file.
        var cases = Regex.Matches(TabNavigationSource(), @"(?m)^\s*case ""(\w+)"":")
                         .Cast<Match>()
                         .Select(m => m.Groups[1].Value)
                         .Distinct(StringComparer.Ordinal)
                         .ToList();
        Assert.True(cases.Count >= 20, "ShowTab parsed as only " + cases.Count + " cases — the regex has rotted");

        var reachable = new HashSet<string>(SettingsPaletteIndex.All.Select(e => e.TabKey), StringComparer.Ordinal);

        var orphans = cases.Where(c => !AliasTabKeys.Contains(c) && !reachable.Contains(c)).ToList();
        Assert.True(orphans.Count == 0,
            "ShowTab keys with no Ctrl+K palette row (a room nobody can search for): " + string.Join(", ", orphans));
    }

    [Fact]
    public void TheStudioRackIsSearchableByModuleName()
    {
        // The regression that started this file: the rack had no row, so every module in it was
        // findable only by opening the door that happens to contain it.
        Assert.Contains(SettingsPaletteIndex.All, e => e.Id == "tab.studio" && e.TabKey == "studio");

        foreach (var term in new[] { "rack", "brain drain", "spiral", "scheduler", "ramp" })
            Assert.True(SettingsPaletteIndex.Search(term).Any(e => e.TabKey == "studio"),
                $"searching the palette for \"{term}\" does not find the Studio rack");
    }
}
