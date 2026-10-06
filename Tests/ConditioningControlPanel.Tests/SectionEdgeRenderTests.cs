using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Linq;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 9 (2026-10-06): the section edge host in MainWindow.xaml. Pins its place in
/// the z-order (under the fullscreen browser overlay), that nothing in it can take a click, that
/// it is authored in Home's Lilac (no pink first frame), that the glow is gradients with no
/// Effect, and that the page wash hands the edge its hue. The block is also realised loose (the
/// way NavFinalRenderTests realises the rail: MainWindow is too heavy to build here).
/// </summary>
public class SectionEdgeRenderTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }

    private static string AppFile(params string[] parts) =>
        File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", System.IO.Path.Combine(parts)));

    private static XElement Named(XDocument doc, string name) =>
        doc.Descendants().First(e => (string?)e.Attribute(X + "Name") == name);

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    [Fact]
    public void TheHostSitsUnderTheBrowserOverlayAndTakesNoClicks()
    {
        var xaml = AppFile("MainWindow", "MainWindow.xaml");
        var doc = XDocument.Parse(xaml);
        var host = Named(doc, "SectionEdgeHost");
        Assert.Equal("Grid", host.Name.LocalName);
        Assert.Equal("9997", (string?)host.Attribute("Panel.ZIndex"));
        Assert.Equal("6", (string?)host.Attribute("Grid.RowSpan"));
        Assert.Equal("2", (string?)host.Attribute("Grid.ColumnSpan"));
        Assert.Equal("False", (string?)host.Attribute("IsHitTestVisible"));
        Assert.True(int.Parse((string)Named(doc, "BrowserFullscreenOverlay").Attribute("Panel.ZIndex")!) > 9997);

        // Glow first, then the line on top of it.
        var kids = host.Elements().ToList();
        Assert.Equal("SectionEdgeGlow", (string?)kids[0].Attribute(X + "Name"));
        Assert.Equal("GlassWindowEdge", (string?)kids[^2].Attribute(X + "Name"));
        Assert.Equal("SectionEdgeLift", (string?)kids[^1].Attribute(X + "Name"));
        Assert.All(host.Descendants().Where(e => e.Name.LocalName is "Rectangle" or "Border" or "Grid"),
            e => Assert.Equal("False", (string?)e.Attribute("IsHitTestVisible")));

        // No Effect anywhere on the edge, and no reference to the mod accent.
        Assert.DoesNotContain(host.Descendants(), e => e.Name.LocalName.EndsWith("Effect", StringComparison.Ordinal));
        Assert.DoesNotContain("PinkBrush", host.ToString(), StringComparison.Ordinal);

        // The particles lane's slot sits between the glow and the line.
        int glow = xaml.IndexOf("x:Name=\"SectionEdgeGlow\"", StringComparison.Ordinal);
        int slot = xaml.IndexOf("EdgeParticles slot", StringComparison.Ordinal);
        int line = xaml.IndexOf("x:Name=\"GlassWindowEdge\"", StringComparison.Ordinal);
        Assert.True(glow > 0 && glow < slot && slot < line);
    }

    [Fact]
    public void TheEdgeIsAuthoredInHomesLilac()
    {
        var doc = XDocument.Parse(AppFile("MainWindow", "MainWindow.xaml"));
        var lilac = NavStripRules.Accent(NavSections.Home);
        var glow = SectionEdgeRules.GlowStops(lilac).Select(Hex).ToArray();

        var rects = Named(doc, "SectionEdgeGlow").Elements().Where(e => e.Name.LocalName == "Rectangle").ToList();
        Assert.Equal(4, rects.Count);
        foreach (var r in rects)
        {
            var band = (string?)r.Attribute("Height") ?? (string?)r.Attribute("Width");
            Assert.Equal("28", band);
            var stops = r.Descendants().Where(e => e.Name.LocalName == "GradientStop").ToList();
            Assert.Equal(new[] { "0", "0.45", "1" }, stops.Select(s => (string)s.Attribute("Offset")!));
            Assert.Equal(glow, stops.Select(s => ((string)s.Attribute("Color")!).ToUpperInvariant()));
        }

        var lineBrush = Named(doc, "SectionEdgeLineBrush");
        var lineStops = lineBrush.Elements().Where(e => e.Name.LocalName == "GradientStop")
            .Select(s => ((string)s.Attribute("Color")!).ToUpperInvariant()).ToArray();
        Assert.Equal(SectionEdgeRules.LineStops(lilac, Models.MotionLevel.Full).Select(Hex), lineStops);
        // The line carries NO transform: the lift travels on four 3 px strips of its own, so a
        // tick never dirties the full-window Border (review fix, 2026-10-06).
        Assert.DoesNotContain(lineBrush.Descendants(), e => e.Name.LocalName.EndsWith("Transform", StringComparison.Ordinal));

        var lift = Named(doc, "SectionEdgeLift");
        var strips = lift.Elements().Where(e => e.Name.LocalName == "Rectangle").ToList();
        Assert.Equal(4, strips.Count);
        var liftStops = SectionEdgeRules.LiftStops(lilac, Models.MotionLevel.Full).Select(Hex).ToArray();
        foreach (var s in strips)
        {
            Assert.Equal("3", (string?)s.Attribute("Height") ?? (string?)s.Attribute("Width"));
            var stops = s.Descendants().Where(e => e.Name.LocalName == "GradientStop").ToList();
            Assert.Equal(new[] { "0.4", "0.5", "0.6" }, stops.Select(st => (string)st.Attribute("Offset")!));
            Assert.Equal(liftStops, stops.Select(st => ((string)st.Attribute("Color")!).ToUpperInvariant()));
            Assert.Single(s.Descendants().Where(e => e.Name.LocalName == "TranslateTransform"));
        }
        foreach (var side in new[] { "Top", "Right", "Bottom", "Left" })
            Assert.Equal("TranslateTransform", Named(doc, "SectionEdgeLift" + side).Name.LocalName);

        var edge = Named(doc, "GlassWindowEdge");
        Assert.Equal("3", (string?)edge.Attribute("BorderThickness"));
        Assert.Equal("8", (string?)edge.Attribute("CornerRadius"));
    }

    [Fact]
    public void TheWashHandsTheEdgeItsHueAndTheWindowInitialisesIt()
    {
        var chrome = AppFile("MainWindow", "MainWindow.SectionChrome.cs");
        var body = chrome.Substring(chrome.IndexOf("private void PaintSectionWash", StringComparison.Ordinal));
        body = body.Substring(0, body.IndexOf("void Tint(", StringComparison.Ordinal));
        Assert.Contains("PaintSectionEdge(hue, ms);", body, StringComparison.Ordinal);

        var shell = AppFile("MainWindow", "MainWindow.xaml.cs");
        int rail = shell.IndexOf("InitializeNavRail();", StringComparison.Ordinal);
        Assert.True(rail > 0 && rail < shell.IndexOf("InitializeSectionEdge();", StringComparison.Ordinal));

        var painter = AppFile("MainWindow", "MainWindow.SectionEdge.cs");
        // The particles lane is wired in: the painter retints and mounts the ember strips.
        Assert.Contains("SectionEdgeParticles?.Retint(hue)", painter, StringComparison.Ordinal);
        Assert.Contains("SectionEdgeParticles?.Mount(_edgeHue)", painter, StringComparison.Ordinal);
        Assert.Contains("RetintEdgeParticles(hue);", painter, StringComparison.Ordinal);
        Assert.Contains("SetDesiredFrameRate", painter, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHostRealisesAndPaintsTheFrameAndTheBand()
    {
        var xaml = AppFile("MainWindow", "MainWindow.xaml");
        var start = xaml.IndexOf("<Grid x:Name=\"SectionEdgeHost\"", StringComparison.Ordinal);
        var stop = xaml.IndexOf("<!-- THE FUSE", start, StringComparison.Ordinal);
        Assert.True(start > 0 && stop > start, "the SectionEdgeHost block stopped parsing");
        var block = xaml.Substring(start, stop - start).TrimEnd();
        block = Regex.Replace(block, @"\s(Grid\.(Row|RowSpan|Column|ColumnSpan)|Panel\.ZIndex)=""\d+""", string.Empty);
        var loose = "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\""
                    + " xmlns:navrail=\"clr-namespace:ConditioningControlPanel.Controls.NavRail;assembly=ConditioningControlPanel\">"
                    + block + "</Grid>";

        WpfRenderHarness.OnStaThread(() =>
        {
            var root = (Grid)XamlReader.Parse(loose);
            root.Measure(new Size(1661, 1002));
            root.Arrange(new Rect(0, 0, 1661, 1002));
            root.UpdateLayout();

            var host = (Grid)root.FindName("SectionEdgeHost");
            var glow = (Grid)root.FindName("SectionEdgeGlow");
            var edge = (Border)root.FindName("GlassWindowEdge");
            Assert.Equal(1661, host.ActualWidth, 1);
            Assert.Equal(1002, host.ActualHeight, 1);
            Assert.Equal(new Thickness(3), edge.BorderThickness);
            Assert.IsType<LinearGradientBrush>(edge.BorderBrush);
            Assert.False(edge.BorderBrush.IsFrozen);

            var bands = glow.Children.OfType<Rectangle>().ToList();
            Assert.Equal(4, bands.Count);
            Assert.Equal(2, bands.Count(b => b.ActualHeight == 28 && b.ActualWidth == 1661));
            Assert.Equal(2, bands.Count(b => b.ActualWidth == 28 && b.ActualHeight == 1002));
            Assert.All(bands, b => Assert.False(((LinearGradientBrush)b.Fill).IsFrozen));
            // InputHitTest honours IsHitTestVisible (VisualTreeHelper.HitTest does not).
            Assert.Null(root.InputHitTest(new Point(1, 500)));
            Assert.Null(root.InputHitTest(new Point(800, 14)));
        });
    }
}
