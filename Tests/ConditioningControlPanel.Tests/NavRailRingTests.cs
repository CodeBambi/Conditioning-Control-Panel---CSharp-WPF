using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Xml.Linq;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 9 (2026-10-06): the rail medallions wear the section hue louder. A thicker
/// ring drawn OVER the art (Tag "navring"), a hue wash on the art (Tag "navtint"), a brighter
/// lit ring, and a spur that bridges the window edge to the lit medallion. The tile stays 56
/// and the row 72, so NavFinalRenderTests' 857/865 rail fit still holds.
/// </summary>
public class NavRailRingTests
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

    private static XDocument Window() => XDocument.Parse(AppFile("MainWindow", "MainWindow.xaml"));

    private static XElement Named(XDocument doc, string name) =>
        doc.Descendants().First(e => (string?)e.Attribute(X + "Name") == name);

    [Fact]
    public void TheRingIsThreePixelsAtRestAndThreeAndAHalfLit()
    {
        Assert.Equal(3.0, NavRailRules.RingThickness(false));
        Assert.Equal(3.5, NavRailRules.RingThickness(true));
        Assert.Equal(0xCC, NavRailRules.RingIdleAlpha);
        Assert.Equal(0xF2, NavRailRules.RingHoverAlpha);
        Assert.Equal(0xFF, NavRailRules.RingActiveAlpha);
    }

    [Fact]
    public void TheLitRingIsTheHueLiftedTowardWhite()
    {
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripRules.Accent(s.Key);
            var lit = NavRailRules.RingColor(hue, active: true, hover: false);
            var want = NavStripRules.Mix(hue, Colors.White, 0.25);
            Assert.Equal(0xFF, lit.A);
            Assert.Equal((want.R, want.G, want.B), (lit.R, lit.G, lit.B));

            var idle = NavRailRules.RingColor(hue, active: false, hover: false);
            Assert.Equal(0xCC, idle.A);
            Assert.Equal((hue.R, hue.G, hue.B), (idle.R, idle.G, idle.B));
            Assert.Equal(0xF2, NavRailRules.RingColor(hue, false, true).A);
        }
        Assert.Equal(0.25, NavRailRules.RingActiveLift);
    }

    [Fact]
    public void TheArtWashIsLouderOnTheLitRow()
    {
        // polish wave 11: the idle wash is gone (it greyed the art), the lit row only breathes the hue
        Assert.Equal(0x0D, NavRailRules.ArtTintAlpha(true));
        Assert.Equal(0x00, NavRailRules.ArtTintAlpha(false));
    }

    [Fact]
    public void EveryRowDrawsTintThenRingOverItsArt()
    {
        var rows = Named(Window(), "NavSidebar").Descendants()
            .Where(e => e.Name.LocalName == "Button" && ((string?)e.Attribute("Style") ?? "").Contains("NavSectionButton"))
            .ToList();
        Assert.Equal(8, rows.Count);
        foreach (var row in rows)
        {
            var grid = row.Elements().Single(e => e.Name.LocalName == "Grid");
            var kids = grid.Elements().Where(e => !e.Name.LocalName.Contains('.')).ToList();
            int vb = kids.FindIndex(e => e.Name.LocalName == "Viewbox");
            int tint = kids.FindIndex(e => (string?)e.Attribute("Tag") == "navtint");
            int ring = kids.FindIndex(e => (string?)e.Attribute("Tag") == "navring");
            string name = (string?)row.Attribute(X + "Name") ?? "?";
            Assert.True(vb >= 0 && tint > vb && ring > tint, name + ": Viewbox, navtint, navring in that order");

            var r = kids[ring];
            Assert.Equal("56", (string?)r.Attribute("Width"));
            Assert.Equal("56", (string?)r.Attribute("Height"));
            Assert.Equal("False", (string?)r.Attribute("IsHitTestVisible"));
            var t = kids[tint];
            Assert.Equal("52", (string?)t.Attribute("Width"));
            Assert.Equal("False", (string?)t.Attribute("IsHitTestVisible"));

            // The plate keeps its size and drops its own border: the ring Border draws it now.
            var plate = kids.First(e => e.Name.LocalName == "Border" && e.Attribute("Tag") == null
                                        && (string?)e.Attribute("Width") == "56");
            Assert.Equal("0", (string?)plate.Attribute("BorderThickness"));
        }
    }

    [Fact]
    public void TheSpurMeetsTheTileCentreAndTheBarIsFourPixels()
    {
        var doc = Window();
        var spur = Named(doc, "NavEdgeSpur");
        Assert.Equal("20", (string?)spur.Attribute("Width"));
        Assert.Equal("8", (string?)spur.Attribute("Height"));
        Assert.Equal("0,25,0,0", (string?)spur.Attribute("Margin"));
        Assert.Equal("Left", (string?)spur.Attribute("HorizontalAlignment"));
        Assert.Equal("Collapsed", (string?)spur.Attribute("Visibility"));
        Assert.Equal("False", (string?)spur.Attribute("IsHitTestVisible"));
        Assert.Equal(20, NavRailRules.SpurWidth);
        Assert.Equal(8, NavRailRules.SpurHeight);
        // Spur centre = top 25 + 4 = 29 = ContentPresenter top 1 + half the 56 px tile row.
        Assert.Equal(1 + 56 / 2.0, NavRailRules.SpurTop + NavRailRules.SpurHeight / 2);
        Assert.Equal(0xE6, NavRailRules.SpurEdgeAlpha);
        Assert.Equal(0x99, NavRailRules.SpurRingAlpha);

        var bar = Named(doc, "NavActiveBar");
        Assert.Equal("4", (string?)bar.Attribute("Width"));
        Assert.Equal("0,8", (string?)bar.Attribute("Margin"));
    }
}
