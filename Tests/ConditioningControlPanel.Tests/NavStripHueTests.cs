using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 2 (2026-10-06, lane PILLS): the pills wear their section's hue. The active
/// pill's text reads at 4.5:1 or better on its solid fill for every section, rest pills, the
/// track, the crumb word and the "Moved" note all come from the one hue table, pills are at
/// least 34 px tall, and every section's header row keeps one height.
/// Set NAV_STRIP_SHOTS to a folder to save an offscreen render of every section's strip.
/// </summary>
public class NavStripHueTests
{
    private readonly ITestOutputHelper _out;
    public NavStripHueTests(ITestOutputHelper output) => _out = output;

    private static SectionTabStrip Laid(string section, string tab, double width = 1480)
    {
        var strip = new SectionTabStrip { MotionOverride = MotionLevel.Off, Width = width };
        strip.Show(section, tab);
        strip.Measure(new Size(width, 200));
        strip.Arrange(new Rect(0, 0, width, strip.DesiredSize.Height));
        strip.UpdateLayout();
        return strip;
    }

    private static Color ColorOf(Brush b) => Assert.IsType<SolidColorBrush>(b).Color;

    [Fact]
    public void TheActivePillTextReadsOnEverySectionHue()
    {
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripRules.Accent(s.Key);
            var ink = NavStripRules.ActiveTextOn(hue);
            var ratio = NavStripRules.Contrast(ink, hue);
            _out.WriteLine($"{s.Key,-10} #{hue.R:X2}{hue.G:X2}{hue.B:X2} text #{ink.R:X2}{ink.G:X2}{ink.B:X2} {ratio:0.00}:1 " +
                           $"(dark {NavStripRules.Contrast(NavStripRules.DarkInk, hue):0.00}, white {NavStripRules.Contrast(Colors.White, hue):0.00})");
            Assert.True(ratio >= 4.5, $"{s.Key}: active pill text is only {ratio:0.00}:1 on its hue");
            Assert.True(ink == NavStripRules.DarkInk || ink == Colors.White);
        }
    }

    [Fact]
    public void ContrastMatchesTheWcagReferencePoints()
    {
        Assert.Equal(21.0, NavStripRules.Contrast(Colors.Black, Colors.White), 2);
        Assert.Equal(1.0, NavStripRules.Contrast(NavStripRules.Lilac, NavStripRules.Lilac), 3);
        Assert.Equal(NavStripRules.DarkInk, NavStripRules.ActiveTextOn(Colors.White));
        Assert.Equal(Colors.White, NavStripRules.ActiveTextOn(Colors.Black));
    }

    [Fact]
    public void RestPillTextReadsOnTheTintedTrackForEverySection()
    {
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripRules.Accent(s.Key);
            var text = NavStripRules.RestTextOn(hue);
            var ground = NavStripRules.RestGround(hue);
            var ratio = NavStripRules.Contrast(NavStripRules.Over(text, ground), ground);
            var plain = NavStripRules.Contrast(NavStripRules.Over(NavStripRules.WithAlpha(hue, NavStripRules.RestTextAlpha), ground), ground);
            _out.WriteLine($"{s.Key,-10} rest text #{text.R:X2}{text.G:X2}{text.B:X2}@{text.A} {ratio:0.00}:1 (plain hue at 85% {plain:0.00}:1)");
            Assert.True(ratio >= 4.5, $"{s.Key}: rest pill text is only {ratio:0.00}:1 on its track");
            Assert.Equal((byte)Math.Round(NavStripRules.RestTextAlpha * 255), text.A);
        }
    }

    [Fact]
    public void ThePillsTrackCrumbAndNoteWearTheSectionHue()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
            {
                var hue = NavStripRules.Accent(s.Key);
                var strip = Laid(s.Key, s.DefaultTab);
                Assert.Equal(hue, ColorOf(strip.CrumbWordBrush));
                Assert.Equal(NavStripRules.WithAlpha(hue, NavStripRules.TrackFillAlpha), ColorOf(strip.TrackFill));
                Assert.Equal(NavStripRules.WithAlpha(hue, NavStripRules.TrackBorderAlpha), ColorOf(strip.TrackBorder));

                var active = strip.ActivePillKey;
                foreach (var key in strip.PillKeys)
                {
                    var (text, outline) = strip.PillPaint(key);
                    if (key == active)
                    {
                        Assert.Equal(NavStripRules.ActiveTextOn(hue), ColorOf(text));
                        Assert.Equal(Colors.Transparent, ColorOf(outline));
                    }
                    else
                    {
                        Assert.Equal(NavStripRules.RestTextOn(hue), ColorOf(text));
                        Assert.Equal(NavStripRules.WithAlpha(hue, NavStripRules.RestOutlineAlpha), ColorOf(outline));
                    }
                }

                strip.ShowMovedNote("Moved");
                Assert.Equal(NavStripRules.WithAlpha(hue, NavStripRules.NoteTextAlpha), ColorOf(strip.MovedNoteBrush));
            }
        });
    }

    [Fact]
    public void PillsAreAtLeast34TallAndEveryHeaderRowIsOneHeight()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            double? row = null;
            foreach (var s in NavSections.Order.Where(s => s.Key != NavSections.Home))
            {
                var strip = Laid(s.Key, s.Key == NavSections.Settings ? "appsettings" : s.DefaultTab);
                foreach (var key in strip.PillKeys)
                    Assert.True(strip.PillHeight(key) >= 34, $"{s.Key}/{key} pill is {strip.PillHeight(key)} px tall");
                _out.WriteLine($"{s.Key,-10} header row {strip.DesiredSize.Height:0.0} px");
                row ??= strip.DesiredSize.Height;
                Assert.Equal(row.Value, strip.DesiredSize.Height, 1);
            }
        });
    }

    [Fact]
    public void RenderEverySectionsHuedStrip()
    {
        var dir = Environment.GetEnvironmentVariable("NAV_STRIP_SHOTS");
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
            {
                // Light the second pill so the shot shows rest pills on both sides of the active one.
                var tab = s.Tabs.Where(t => !t.Hidden && t.Kind is NavTabKind.Tab or NavTabKind.Zone)
                                .Skip(1).FirstOrDefault()?.Key ?? s.DefaultTab;
                var strip = Laid(s.Key, tab, 1180);
                strip.ShowMovedNote("Moved: " + s.Key);
                strip.UpdateLayout();
                _out.WriteLine($"{s.Key,-10} {tab,-14} actual {strip.ActualHeight:0.0} desired {strip.DesiredSize.Height:0.0} track {strip.TrackHeightForTests:0.0}");
                if (!string.IsNullOrEmpty(dir)) Shot(strip, dir!, $"pills-{s.Key}.png");

                // A locked page lit: its tier sign sits on the solid hue on its dark plate.
                var locked = NavStripRules.Pills(s.Key).FirstOrDefault(t => t.Tier > 0 && t.Kind is NavTabKind.Tab or NavTabKind.Zone);
                if (locked != null && !string.IsNullOrEmpty(dir))
                {
                    var lit = Laid(s.Key, locked.Key, 1180);
                    Assert.Equal(locked.Key, lit.ActivePillKey);
                    Shot(lit, dir!, $"pills-{s.Key}-locked.png");
                }
            }
        });
    }

    private static void Shot(FrameworkElement element, string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var w = (int)Math.Ceiling(element.ActualWidth) + 40;
        var h = (int)Math.Ceiling(element.ActualHeight) + 40;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x30)), null, new Rect(0, 0, w, h));
            // A fixed viewbox: the tier sign draws past its box, and a VisualBrush sized to its
            // descendant bounds would shift the whole strip down in the shot.
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, element.ActualWidth, element.ActualHeight) },
                null, new Rect(20, 20, element.ActualWidth, element.ActualHeight));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }
}
