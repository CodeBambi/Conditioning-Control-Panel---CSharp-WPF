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

    /// <summary>
    /// The words people actually typed at the box and got nothing back (six reporters in the week
    /// of 2026-09-14, all answered by hand in Discord). One row per phrase, asserted against the
    /// destination that really owns the setting - not against "the search returned something",
    /// which would pass on any accidental substring hit.
    /// </summary>
    [Theory]
    // "is there a way to pause my streak" - the streak shield is a skill, bought on the tree.
    [InlineData("pause streak", "tab.enhancements")]
    [InlineData("streak freeze", "tab.enhancements")]
    [InlineData("vacation", "tab.enhancements")]
    // "how do I turn off the pop quiz" - ChkPopQuizEnabled lives on the Graded Intake page.
    [InlineData("pop quiz", "tab.gradedintake")]
    [InlineData("turn off quiz", "tab.gradedintake")]
    // "turn off the screen blur but keep the audio" - Brain Drain is a Studio rack module.
    [InlineData("blur", "tab.studio")]
    [InlineData("screen blur", "tab.studio")]
    [InlineData("melt", "tab.studio")]
    // "the volume keeps dropping" - the ramp pulling the master dial, also a rack module.
    [InlineData("volume gets quiet", "tab.studio")]
    // "it starts by itself" - the Scheduler (rack) and the auto-start switch (Settings) both.
    [InlineData("starts by itself", "tab.studio")]
    [InlineData("auto start", "set.auto_start_engine")]
    // "where in the app is the bug report button?"
    [InlineData("bug report", "chrome.bugreport")]
    [InlineData("feedback", "chrome.bugreport")]
    // The games moved to the launcher, so the Arcademy's door is the CC Labs button.
    [InlineData("arcademy", "chrome.cclabs")]
    [InlineData("campus", "chrome.cclabs")]
    [InlineData("casino", "chrome.cclabs")]
    // "esc closes the app" - rebinding the panic key.
    [InlineData("exit key", "set.panic_key")]
    // Four 40-minute support threads about a missing speech model.
    [InlineData("vosk", "tab.shelistening")]
    [InlineData("speech model", "tab.shelistening")]
    [InlineData("microphone", "tab.shelistening")]
    // "how do I get haptics working on the gaze minigame" - the toy is connected on Haptics.
    [InlineData("haptics gaze", "tab.haptics")]
    [InlineData("vibration", "tab.haptics")]
    public void ThePhrasesPeopleTypeFindTheirSetting(string query, string expectedId)
    {
        var hits = SettingsPaletteIndex.Search(query);
        Assert.True(hits.Any(e => e.Id == expectedId),
            $"typing \"{query}\" into Ctrl+K does not offer \"{expectedId}\" (got: " +
            string.Join(", ", hits.Take(6).Select(e => e.Id)) + ")");
    }

    /// <summary>
    /// The Gaze minigame is a card on the PLAY wall, so a bare "gaze" must offer Play ahead of
    /// Haptics. Both are alias hits, both score 40, and ties break by declaration order - so this
    /// is really a test that the Haptics row is still declared BELOW Play, which is the only thing
    /// keeping the two apart. Haptics keeps its own gaze words on purpose: the toy is connected
    /// there, and "haptics gaze" is a substring of nothing else.
    ///
    /// <para>Not asserted as first place outright: <c>set.restrict_gaze</c> carries the word in its
    /// own caption, and a label hit outranking an alias hit is the matcher working as designed.</para>
    /// </summary>
    [Fact]
    public void BareGazePrefersThePlayWallOverHaptics()
    {
        foreach (var query in new[] { "gaze", "gaze minigame" })
        {
            var ids = SettingsPaletteIndex.Search(query).Select(e => e.Id).ToList();
            var play = ids.IndexOf("tab.play");
            var haptics = ids.IndexOf("tab.haptics");

            Assert.True(play >= 0, $"\"{query}\" does not offer the Play wall at all");
            Assert.True(haptics < 0 || play < haptics,
                $"typing \"{query}\" ranks Haptics above Play, but the Gaze minigame is a card on " +
                "the Play wall (got: " + string.Join(", ", ids.Take(4)) + ")");
        }

        Assert.Contains(SettingsPaletteIndex.Search("haptics gaze"), e => e.Id == "tab.haptics");
    }

    /// <summary>
    /// The title-bar rows are the only entries with no <c>TabKey</c>: they pulse a button that is
    /// always on screen instead of navigating. If one ever loses its element names it becomes a row
    /// that does nothing at all, which is worse than not being listed. Asserted over however many
    /// there are - the property is what matters, not the count.
    /// </summary>
    [Fact]
    public void TitleBarRowsPointAtAButtonInsteadOfNavigating()
    {
        var chrome = SettingsPaletteIndex.All
            .Where(e => e.Id.StartsWith("chrome.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(chrome);
        foreach (var row in chrome)
        {
            Assert.True(string.IsNullOrEmpty(row.TabKey),
                $"{row.Id} carries a TabKey - it would navigate away from the title bar it points at");
            Assert.NotEmpty(row.ElementNames);
        }

        // Both names are in MainWindow.xaml's own namescope, which is what lets the pulse resolve.
        var mainWindow = ReadSource("MainWindow", "MainWindow.xaml");
        foreach (var name in chrome.SelectMany(e => e.ElementNames))
            Assert.Contains("x:Name=\"" + name + "\"", mainWindow, StringComparison.Ordinal);
    }
}
