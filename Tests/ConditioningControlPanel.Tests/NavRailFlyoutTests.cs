using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The nav rail's flyout, held to the two owner calls of 2026-08-13: it must start closing the
/// instant the pointer leaves it, and the door art must fill its medallion instead of floating in
/// a grey plate.
///
/// <para><b>Why these are source reads.</b> Realizing MainWindow in this suite is not affordable
/// (same reason the nav half of ModAwareDecodedArtTests is a scrape), and neither contract here is
/// observable from a rendered tree anyway: "no delay" is the ABSENCE of a timer between MouseLeave
/// and the collapse, and a re-introduced one would look identical in a static screenshot. What can
/// rot is the wiring, so the wiring is what is pinned.</para>
///
/// <para>Deliberately loose where the design is: any collapse tween up to 250ms passes, because
/// the ask was to kill the WAIT, not the animation, and picking the exact frame count for the
/// designer is how a test starts losing arguments it should not be in.</para>
/// </summary>
public class NavRailFlyoutTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string AppFile(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine("ConditioningControlPanel", Path.Combine(parts))));

    private static string NavRailSource() => AppFile("MainWindow", "MainWindow.NavRail.cs");

    /// <summary>Reads a numeric const straight out of the rail's source.</summary>
    private static double ConstValue(string src, string name)
    {
        var m = Regex.Match(src, @"const\s+(?:int|double)\s+" + Regex.Escape(name) + @"\s*=\s*(-?[\d.]+)");
        Assert.True(m.Success, name + " is gone from MainWindow.NavRail.cs");
        return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void LeavingTheRailCollapsesItOnTheSpot()
    {
        var src = NavRailSource();

        // The delay timer and its constant are deleted, not merely unused. (Both identifiers are
        // absent from the surviving comments on purpose, so this scan cannot pass on prose.)
        Assert.DoesNotContain("_navRailCollapseTimer", src, StringComparison.Ordinal);
        Assert.DoesNotContain("NavRailCollapseDelayMs", src, StringComparison.Ordinal);

        var start = src.IndexOf("NavSidebar.MouseLeave", StringComparison.Ordinal);
        Assert.True(start > 0, "nothing hangs off NavSidebar.MouseLeave - the rail no longer knows the pointer left");
        var end = src.IndexOf("PreviewMouseDown", start, StringComparison.Ordinal);
        Assert.True(end > start, "the MouseLeave handler's neighbourhood moved - re-read it, then fix this scrape");
        var handler = src.Substring(start, end - start);

        Assert.Contains("SetNavRailExpanded(false)", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Timer", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Delay", handler, StringComparison.Ordinal);

        // The leave is measured on the RAIL, not on a row: per-item leave would collapse the rail
        // every time the pointer crossed from one door to the next.
        Assert.Contains("NavSidebar.MouseLeave", src, StringComparison.Ordinal);

        // ...and the tween survived the cull.
        var ms = ConstValue(src, "NavRailCollapseAnimMs");
        Assert.InRange(ms, 1, 250);
    }

    [Fact]
    public void DoorArtFillsItsMedallionInsteadOfFloatingInIt()
    {
        var src = NavRailSource();

        double iconShut = ConstValue(src, "NavDoorIconCollapsed");
        double iconOpen = ConstValue(src, "NavDoorIconExpanded");
        double tileShut = ConstValue(src, "NavDoorTileCollapsed");
        double tileOpen = ConstValue(src, "NavDoorTileExpanded");

        // Owner call: the art was 28/44 and read as a thumbnail on a plate.
        Assert.True(iconShut >= 40, $"the shut door art shrank back to {iconShut}");
        Assert.True(iconOpen >= 52, $"the open door art shrank back to {iconOpen}");

        // But it still has to leave the tile's own 1px edge showing - that edge is the active
        // door's hue ring (RefreshNavDoorActive), the only "you are here" mark left on the row.
        Assert.True(iconShut <= tileShut - 2, "the shut art covers the tile's hue ring");
        Assert.True(iconOpen <= tileOpen - 2, "the open art covers the tile's hue ring");

        // Decode headroom for the bigger render: the medallions are 64px native, and the resolver
        // cap has to stay at least 2x the largest size they are drawn at.
        double decode = ConstValue(src, "NavDoorArtDecodeWidth");
        Assert.True(decode >= iconOpen * 2, $"decode cap {decode} is under 2x the {iconOpen}px open medallion");
    }

    [Fact]
    public void TheMedallionsAuthoredStateMatchesTheCodeAndCarriesNoGreyRing()
    {
        var xaml = AppFile("MainWindow", "MainWindow.xaml");
        double iconShut = ConstValue(NavRailSource(), "NavDoorIconCollapsed");

        // MainWindow.xaml authors the COLLAPSED state, so a rail whose Loaded hook never ran still
        // renders correctly shut - which only holds while the two halves agree.
        var authored = Regex.Matches(
            xaml,
            "<Viewbox Grid\\.Column=\"0\" Width=\"" + iconShut + "\" Height=\"" + iconShut + "\"");
        Assert.Equal(7, authored.Count);

        // The permanent grey hairline is gone; the 1px edge is Transparent until the door is active.
        Assert.DoesNotContain(
            "BorderThickness=\"1\" BorderBrush=\"{DynamicResource GlassBorderBrush}\"",
            xaml, StringComparison.Ordinal);
    }
}
