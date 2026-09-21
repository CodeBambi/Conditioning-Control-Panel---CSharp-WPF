using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Deeper library listed nothing on 6.10.1 for anyone who did not arrive by the side
/// rail: the pills read All 0 / Video 0 / Audio 0 over a blank list with no empty state,
/// while the folder held the files and "show in folder" opened the right place.
///
/// <para>The hub's lazy init (<c>InitializeDeeperHub</c>) is what clears the
/// <c>_deeperHubInitDone</c> guard at the top of <c>ApplyDeeperFilterAndSort</c>, and only
/// <c>BtnDeeper_Click</c> ever called it. Every other door - the Home mosaic's editor tile,
/// the Ctrl+K row, the Settings jumps, the catalogue toast - is a bare
/// <c>ShowTab("deeper")</c>, which scanned the library into <c>_deeperAllEntries</c> and
/// then returned before projecting a single row. So the init moved into ShowTab, where
/// every door goes through it.</para>
///
/// <para>Source-text reads: MainWindow cannot be instantiated in a unit test.</para>
/// </summary>
public class DeeperLibraryDoorTests
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

    /// <summary>ShowTab's "deeper" case alone, so an assertion cannot be satisfied by some
    /// other tab's arm further down the switch.</summary>
    private static string DeeperCase()
    {
        var nav = ReadSource("MainWindow", "MainWindow.TabNavigation.cs");
        var start = nav.IndexOf("case \"deeper\":", StringComparison.Ordinal);
        Assert.True(start > 0, "ShowTab has no \"deeper\" case");
        var end = nav.IndexOf("case \"", start + 8, StringComparison.Ordinal);
        Assert.True(end > start, "the \"deeper\" case is the last arm of the switch - re-anchor this test");
        return nav.Substring(start, end - start);
    }

    [Fact]
    public void Every_door_into_the_tab_initialises_the_hub()
    {
        // The one line that made the list appear for the other five doors.
        Assert.Contains("InitializeDeeperHub()", DeeperCase(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_tab_still_pulls_a_fresh_scan_on_a_later_show()
    {
        // Init does the first scan itself; a show after that has to ask for one, or an
        // import made from another window never shows up.
        Assert.Contains("RefreshDeeperLibraryUI()", DeeperCase(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_init_reports_whether_it_did_the_scan()
    {
        // ShowTab reads the answer so the first open does not scan the folder twice.
        var hub = ReadSource("MainWindow", "MainWindow.DeeperHub.cs");
        Assert.Contains("private bool InitializeDeeperHub()", hub, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rail_door_no_longer_scans_on_its_own()
    {
        // It used to call InitializeDeeperHub + ReloadDeeperLibraryFromDisk after ShowTab,
        // which is three scans of the library for one click now that ShowTab does both.
        var tab = ReadSource("MainWindow", "MainWindow.DeeperTab.cs");
        var at = tab.IndexOf("internal void BtnDeeper_Click", StringComparison.Ordinal);
        Assert.True(at > 0, "MainWindow has no BtnDeeper_Click");
        var body = tab.Substring(at, Math.Min(700, tab.Length - at));
        var end = body.IndexOf("\n        private", StringComparison.Ordinal);
        if (end > 0) body = body.Substring(0, end);

        Assert.Contains("ShowTab(\"deeper\")", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InitializeDeeperHub()", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadDeeperLibraryFromDisk()", body, StringComparison.Ordinal);
    }

    // -------------------------------------------------- the second way to a blank list

    [Fact]
    public void A_missing_theme_brush_costs_a_colour_and_not_the_library()
    {
        // BuildRowVm ran every row through FindResource, which THROWS on a miss, straight
        // into ApplyDeeperFilterAndSort's catch - and that catch leaves DeeperFilteredEntries
        // empty with the counts and the empty state never updated: the same blank list, from
        // a mod theme that ships a dictionary without one of the DeeperHub* keys.
        var hub = ReadSource("MainWindow", "MainWindow.DeeperHub.cs");
        Assert.DoesNotContain("(Brush)FindResource(", hub, StringComparison.Ordinal);
        Assert.Contains("private Brush RowBrush(string key, Brush fallback)", hub, StringComparison.Ordinal);
        Assert.Contains("TryFindResource(key) as Brush ?? fallback", hub, StringComparison.Ordinal);
    }

    [Fact]
    public void A_swallowed_projection_failure_is_findable_in_the_log()
    {
        // Debug level in a release build is silence, and the symptom (an empty list) is
        // indistinguishable from an empty folder. Warning, with the exception.
        var hub = ReadSource("MainWindow", "MainWindow.DeeperHub.cs");
        var at = hub.IndexOf("private void ApplyDeeperFilterAndSort()", StringComparison.Ordinal);
        Assert.True(at > 0, "MainWindow has no ApplyDeeperFilterAndSort");
        var body = hub.Substring(at, Math.Min(1600, hub.Length - at));
        var catchAt = body.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(catchAt > 0, "ApplyDeeperFilterAndSort no longer swallows - re-anchor this test");
        var tail = body.Substring(catchAt);
        Assert.Contains("App.Logger?.Warning(ex,", tail, StringComparison.Ordinal);
    }
}
