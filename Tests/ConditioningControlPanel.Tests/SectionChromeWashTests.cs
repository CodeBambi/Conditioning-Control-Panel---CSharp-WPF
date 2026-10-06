using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 2 (2026-10-06): the section hue reaches the rail medallions, the page ground
/// and the HUD band from ONE table (NavStripRules.Accent). Pins that no second colour table
/// grows back in the XAML and that the paint stays paint (never hit-testable).
/// </summary>
public class SectionChromeWashTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string AppFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", Path.Combine(parts)));
    }

    private static XElement Named(XDocument doc, string name) =>
        doc.Descendants().First(e => (string?)e.Attribute(X + "Name") == name);

    [Fact]
    public void RailRowsCarryNoHardCodedHue()
    {
        var doc = XDocument.Parse(AppFile("MainWindow", "MainWindow.xaml"));
        var rows = Named(doc, "NavSidebar").Descendants()
            .Where(e => e.Name.LocalName == "Button" && ((string?)e.Attribute("Style") ?? "").Contains("NavSectionButton"))
            .ToList();
        Assert.Equal(8, rows.Count);
        foreach (var row in rows)
        {
            Assert.Null(row.Attribute("Background"));
            Assert.Null(row.Attribute("BorderBrush"));
        }
        var painter = AppFile("MainWindow", "MainWindow.NavRail.cs");
        Assert.Contains("NavStripRules.Accent(row.Section)", painter, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRingIsQuietAtRestBrighterOnHoverAndSolidWhenActive()
    {
        Assert.InRange(NavRailRules.RingAlpha(false, false), 0x66, 0x80);   // about 45%
        Assert.True(NavRailRules.RingAlpha(false, true) > NavRailRules.RingAlpha(false, false));
        Assert.Equal(0xFF, NavRailRules.RingAlpha(true, false));
        Assert.Equal(0xFF, NavRailRules.RingAlpha(true, true));
        var c = NavRailRules.WithAlpha(NavStripRules.Accent(NavSections.Social), 0x40);
        Assert.Equal(0x40, c.A);
        Assert.Equal(NavStripRules.Sky.R, c.R);
    }

    [Fact]
    public void TheWashSitsBehindThePageAndTheBandBehindTheHud()
    {
        var xaml = AppFile("MainWindow", "MainWindow.xaml");
        var doc = XDocument.Parse(xaml);

        var wash = Named(doc, "SectionPageWash");
        Assert.Equal("4", (string?)wash.Attribute("Grid.Row"));
        Assert.Equal("1", (string?)wash.Attribute("Grid.Column"));
        Assert.Equal("False", (string?)wash.Attribute("IsHitTestVisible"));
        // Drawn before (under) the page's AdornerDecorator.
        Assert.True(xaml.IndexOf("x:Name=\"SectionPageWash\"", StringComparison.Ordinal)
                    < xaml.IndexOf("<AdornerDecorator Grid.Row=\"4\"", StringComparison.Ordinal));

        var band = Named(doc, "HudBand");
        Assert.Equal("1", (string?)band.Attribute("Grid.Row"));
        Assert.Equal("3", (string?)band.Attribute("Grid.RowSpan"));
        Assert.Equal("False", (string?)band.Attribute("IsHitTestVisible"));
        Assert.True(xaml.IndexOf("x:Name=\"HudBand\"", StringComparison.Ordinal)
                    < xaml.IndexOf("<!-- Header Bar - Row 1 -->", StringComparison.Ordinal));

        var chrome = AppFile("MainWindow", "MainWindow.SectionChrome.cs");
        Assert.Contains("PaintSectionWash(section)", chrome, StringComparison.Ordinal);
        Assert.Contains("NavRailRules.Ms(SectionWashMs, MotionFx.Level)", chrome, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"SectionWashAlpha = 0x2[0-9A-F]"), chrome);
    }
}
