using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The section rail (nav rework, 2026-10-06), replacing NavRailFlyoutTests and
/// NavRailHoldWatchdogTests: the flyout, its hold latch and its watchdog no longer exist.
/// Pins what the owner decided: seven labelled sections in a frozen order + a gear at the foot,
/// inside the 96px column, never over the page, never scrolling; medallion art kept; the rules
/// (badge cap, last tab, Ctrl+N, motion timing) pure and tested here.
/// </summary>
public class NavSectionRailTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string AppFile(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", Path.Combine(parts)));

    private static XElement Sidebar()
    {
        var doc = XDocument.Parse(AppFile("MainWindow", "MainWindow.xaml"));
        var rail = doc.Descendants().FirstOrDefault(e => (string?)e.Attribute(X + "Name") == "NavSidebar");
        Assert.NotNull(rail);
        return rail!;
    }

    private static List<XElement> RailButtons(XElement rail) =>
        rail.Descendants().Where(e => e.Name.LocalName == "Button"
            && ((string?)e.Attribute(X + "Name"))?.StartsWith("Door", StringComparison.Ordinal) == true).ToList();

    [Fact]
    public void RowsFollowTheSectionTableAndTheGearSitsAtTheFoot()
    {
        var rows = RailButtons(Sidebar());
        var tags = rows.Select(b => (string?)b.Attribute("Tag")).ToArray();
        var expected = NavRailRules.RailSections.Select(s => s.Key).Append("appsettings").ToArray();
        Assert.Equal(expected, tags);
        Assert.Equal(new[] { "home", "studio", "companion", "play", "social", "you", "library", "appsettings" }, tags);
        Assert.Equal(7, NavRailRules.RailSections.Count);
    }

    [Fact]
    public void OldDoorNamesStayForTheirCallers()
    {
        var names = RailButtons(Sidebar()).Select(b => (string?)b.Attribute(X + "Name")).ToArray();
        foreach (var n in new[] { "DoorHome", "DoorStudio", "DoorCompanion", "DoorPlay", "DoorSocial",
                                  "DoorYou", "DoorLibrary", "DoorSettings" })
            Assert.Contains(n, names);
        Assert.DoesNotContain("DoorWebApp", names);
    }

    [Fact]
    public void EveryRowIsLabelledUnderItsTileAndKeepsItsMedallion()
    {
        foreach (var row in RailButtons(Sidebar()))
        {
            var name = (string?)row.Attribute(X + "Name");
            var grid = row.Elements().First(e => e.Name.LocalName == "Grid");
            var kids = grid.Elements().Where(e => !e.Name.LocalName.Contains('.')).ToList();
            // The icon Viewbox is a DIRECT child (ChromeFx's hover nudge takes the first one).
            Assert.Contains(kids, k => k.Name.LocalName == "Viewbox");
            Assert.Contains(kids, k => k.Name.LocalName == "Ellipse");
            var label = kids.SingleOrDefault(k => k.Name.LocalName == "TextBlock");
            Assert.True(label != null, name + " has no section name");
            Assert.Equal("1", (string?)label!.Attribute("Grid.Row"));
            Assert.Contains("NavSectionLabel", (string?)label.Attribute("Style"));
            Assert.Contains("loc:Str", (string?)label.Attribute("Text"));
            Assert.Contains(kids, k => k.Name.LocalName == "Border"
                && ((string?)k.Attribute("Style"))?.Contains("NavSectionBadge") == true);
            Assert.Equal("NavDoor_Click", (string?)row.Attribute("Click"));
            Assert.Contains("ImgDoor", grid.ToString());
        }
    }

    [Fact]
    public void TheRailStaysInItsColumnAndNeverScrolls()
    {
        var rail = Sidebar();
        Assert.Equal("0", (string?)rail.Attribute("Grid.Column"));
        Assert.Null(rail.Attribute("Grid.ColumnSpan"));
        Assert.Null(rail.Attribute("Width"));
        Assert.DoesNotContain(rail.Descendants(), e => e.Name.LocalName == "ScrollViewer");

        // Height budget at the fixed 1585x901 canvas: 865px under the title row minus the
        // rail's own 16px margin. Seven rows + gear at Height+Margin from the style, plus the
        // search pill; the foot chips are measured at their authored heights where they have one.
        var xaml = AppFile("MainWindow", "MainWindow.xaml");
        double rowH = double.Parse(Regex.Match(xaml,
            @"x:Key=""NavSectionButton""[\s\S]*?Property=""Height"" Value=""([\d.]+)""").Groups[1].Value,
            CultureInfo.InvariantCulture);
        // Polish wave 2 (2026-10-06): 76px rows with no margin carry a 60px medallion and its
        // label; the rail's own margin is 4+4 and the two pills shrank to buy the height. The
        // foot chips (Circe's tab, divider, EMI, Friends) measure about 167px; the rendered
        // check in NavFinalRenderTests is the authority, this is the cheap static guard.
        double rowMargin = double.Parse(Regex.Match(xaml,
            @"x:Key=""NavSectionButton""[\s\S]*?Property=""Margin"" Value=""([\d.]+)""").Groups[1].Value,
            CultureInfo.InvariantCulture);
        Assert.True(rowH >= 74, $"the section rows shrank back to {rowH}px; the medallions need 74+");
        double rows = 8 * (rowH + 2 * rowMargin);
        double top = 26 + 4 + 22 + 2;     // search pill + back pill (when shown)
        Assert.True(rows + top <= 857 - 170,
            $"rows ({rows}) + top ({top}) leave under 170px for the foot chips");
    }

    [Fact]
    public void TheFlyoutMachineryIsGone()
    {
        var src = AppFile("MainWindow", "MainWindow.NavRail.cs");
        foreach (var gone in new[] { "NavRailExpandedWidth", "SetNavRailExpanded", "NavRailHoldLatch",
                                     "NavRailWatchdogTick", "ForceCollapseNavRail", "ApplyNavRailAirspace",
                                     "HoldOverlappingBrowsers", "NavSidebar.MouseLeave" })
            Assert.DoesNotContain(gone, src, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "UI", "NavRailHoldLatch.cs")));
        var nav = AppFile("MainWindow", "MainWindow.TabNavigation.cs");
        Assert.DoesNotContain("SetExpandedDoor", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("NavEntryRowHeight", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("NavLauncherDoors", nav, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePossessionSeamAndModArtSurvive()
    {
        var src = AppFile("MainWindow", "MainWindow.NavRail.cs");
        Assert.Contains("PreviewMouseLeftButtonDown += NavDoor_PossessionReroute", src, StringComparison.Ordinal);
        foreach (var door in new[] { "home", "studio", "companion", "play", "social", "you", "library", "settings" })
            Assert.Contains($"\"nav/door_{door}.png\"", src, StringComparison.Ordinal);
        var decode = int.Parse(Regex.Match(src, @"NavDoorArtDecodeWidth\s*=\s*(\d+)").Groups[1].Value);
        Assert.True(decode >= 80, "decode cap under 2x the 40px icon");
    }

    [Fact]
    public void ArtFillsTheTileButLeavesTheHueRing()
    {
        foreach (var row in RailButtons(Sidebar()))
        {
            var grid = row.Elements().First(e => e.Name.LocalName == "Grid");
            var tile = grid.Elements().First(e => e.Name.LocalName == "Border" && e.Attribute("CornerRadius") != null);
            var icon = grid.Elements().First(e => e.Name.LocalName == "Viewbox");
            double t = double.Parse((string)tile.Attribute("Width")!, CultureInfo.InvariantCulture);
            double i = double.Parse((string)icon.Attribute("Width")!, CultureInfo.InvariantCulture);
            Assert.InRange(i, t - 6, t - 2);
        }
    }

    [Fact]
    public void ShortcutsAreBoundForTheSevenSections()
    {
        var src = AppFile("MainWindow", "MainWindow.NavRail.cs");
        Assert.Contains("Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7", src, StringComparison.Ordinal);
        Assert.Contains("ModifierKeys.Control", src, StringComparison.Ordinal);
        for (int i = 0; i < NavRailRules.RailSections.Count; i++)
            Assert.Equal(i + 1, NavRailRules.ShortcutNumber(NavRailRules.RailSections[i].Key));
        Assert.Equal(0, NavRailRules.ShortcutNumber(NavSections.Settings));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-3, null)]
    [InlineData(1, "1")]
    [InlineData(9, "9")]
    [InlineData(10, "9+")]
    [InlineData(250, "9+")]
    public void BadgeTextCapsAndClears(int count, string? expected)
        => Assert.Equal(expected, NavRailRules.BadgeText(count));

    [Fact]
    public void ARowOpensItsLastTabOrItsDefault()
    {
        Assert.Equal("availablesubjects", NavRailRules.TargetTab("social", null));
        Assert.Equal("availablesubjects", NavRailRules.TargetTab("social", "not json"));
        Assert.Equal("friends", NavRailRules.TargetTab("social", "{\"social\":\"friends\"}"));
        Assert.Equal("quests", NavRailRules.TargetTab("you", "{\"social\":\"friends\",\"you\":\"quests\"}"));
        // A tab from another section, a window or a launcher never sticks.
        Assert.Equal("studio", NavRailRules.TargetTab("studio", "{\"studio\":\"quests\"}"));
        Assert.Equal("studio", NavRailRules.TargetTab("studio", "{\"studio\":\"justdrop\"}"));
        Assert.Equal("assets", NavRailRules.TargetTab("library", "{\"library\":\"mods\"}"));
        // Zones are pages too.
        Assert.Equal("playeyes", NavRailRules.TargetTab("play", "{\"play\":\"playeyes\"}"));
        Assert.Equal("appsettings", NavRailRules.TargetTab("settings", null));
        Assert.Null(NavRailRules.TargetTab("nope", null));
    }

    [Fact]
    public void GearTagMapsToSettings()
    {
        Assert.Equal(NavSections.Settings, NavRailRules.SectionForDoorTag("appsettings"));
        Assert.Equal("social", NavRailRules.SectionForDoorTag("social"));
        Assert.Equal("appsettings", NavRailRules.DoorTagForSection(NavSections.Settings));
    }

    [Fact]
    public void MotionLevelScalesStateChanges()
    {
        Assert.Equal(80, NavRailRules.Ms(80, MotionLevel.Full));
        Assert.Equal(40, NavRailRules.Ms(80, MotionLevel.Reduced));
        Assert.Equal(0, NavRailRules.Ms(80, MotionLevel.Off));
    }

    [Fact]
    public void CapsConverterUppercasesTheInnerResult()
    {
        var c = new NavCapsConverter(null);
        Assert.Equal("STUDIO", c.Convert("Studio", typeof(string), null, CultureInfo.InvariantCulture));
    }
}
