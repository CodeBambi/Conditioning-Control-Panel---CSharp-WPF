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
/// least 36 px tall (polish wave 3: filled plates on a real tab bar), and every section's header
/// row keeps one height.
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
            _out.WriteLine($"{s.Key,-10} rest text #{text.R:X2}{text.G:X2}{text.B:X2}@{text.A} {ratio:0.00}:1 (plain hue at 95% {plain:0.00}:1)");
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
                // Polish wave 7: the track is a tray (the hue over deep ink) with a shaded top edge.
                Assert.Equal(NavStripRules.TrackFill(hue), ColorOf(strip.TrackFill));
                var tray = Assert.IsType<LinearGradientBrush>(strip.TrackBorder);
                Assert.Equal(NavStripRules.WithAlpha(NavStripRules.DarkInk, NavStripRules.TrackInsetAlpha), tray.GradientStops[0].Color);
                Assert.Equal(NavStripRules.WithAlpha(hue, NavStripRules.TrackBorderAlpha), tray.GradientStops[^1].Color);

                var active = strip.ActivePillKey;
                var keys = strip.PillKeys;
                for (int i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
                    var tint = NavStripRules.TabTint(s.Key, key, i, keys.Count);
                    Assert.Equal(tint, strip.PillTint(key));
                    var (text, outline) = strip.PillPaint(key);
                    var glyph = strip.PillGlyphBrush(key);
                    if (key == active)
                    {
                        Assert.Equal(NavStripRules.ActiveTextOn(hue), ColorOf(text));
                        // A near-white gloss ring in the tab's tint, strong at the top.
                        var ring = Assert.IsType<LinearGradientBrush>(outline);
                        Assert.Equal(NavStripRules.ActiveRingColor(tint, NavStripRules.ActiveRingTopAlpha), ring.GradientStops[0].Color);
                        Assert.Equal(Colors.Transparent, ColorOf(strip.PillFill(key)));
                        if (glyph != null) Assert.Equal(NavStripRules.ActiveGlyphOn(hue, tint), ColorOf(glyph));
                    }
                    else
                    {
                        Assert.Equal(NavStripRules.RestTextOn(hue, tint), ColorOf(text));
                        // A bevelled outline in the tint: lit top, the tint at 75% in the middle, shaded foot.
                        var bevel = Assert.IsType<LinearGradientBrush>(outline);
                        Assert.Equal(NavStripRules.WithAlpha(tint, NavStripRules.RestOutlineAlpha), bevel.GradientStops[1].Color);
                        Assert.True(NavStripRules.Luminance(Opaque(bevel.GradientStops[0].Color)) > NavStripRules.Luminance(Opaque(bevel.GradientStops[^1].Color)),
                            $"{s.Key}/{key}: the outline's top edge is not lighter than its foot");
                        // Every inactive pill is a raised plate: the tint, lit at the top, centred on 24%.
                        var plate = Assert.IsType<LinearGradientBrush>(strip.PillFill(key));
                        Assert.Equal(NavStripRules.WithAlpha(tint, NavStripRules.RestFillAlpha + NavStripRules.PlateLift), plate.GradientStops[0].Color);
                        Assert.Equal(NavStripRules.WithAlpha(tint, NavStripRules.RestFillAlpha - NavStripRules.PlateLift), plate.GradientStops[^1].Color);
                        if (glyph != null) Assert.Equal(NavStripRules.RestGlyphOn(hue, tint), ColorOf(glyph));
                    }
                }

                strip.ShowMovedNote("Moved");
                Assert.Equal(NavStripRules.WithAlpha(hue, NavStripRules.NoteTextAlpha), ColorOf(strip.MovedNoteBrush));
            }
        });
    }

    private static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);

    [Fact]
    public void EveryTabTintReadsAndNeighboursStepNineDegrees()
    {
        // Polish wave 7 (owner: "subtle identity to the subtabs, maybe a colour coding"): each
        // pill wears the section hue turned by (index - middle) x 9 degrees. Every label on every
        // tint reads at 4.5:1, every rest glyph at 4.5:1, every active glyph at 3:1 on the solid
        // section hue (the WCAG graphics floor), and neighbouring tints sit 9 degrees apart
        // (8.5 measured: 8-bit rounding moves a hue by a fraction of a degree).
        foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
        {
            var hue = NavStripRules.Accent(s.Key);
            var pills = NavStripRules.Pills(s.Key);
            double? prevHue = null;
            for (int i = 0; i < pills.Count; i++)
            {
                var tint = NavStripRules.TabTint(s.Key, pills[i].Key, i, pills.Count);
                var ground = NavStripRules.RestGround(hue, tint);
                var label = NavStripRules.Contrast(NavStripRules.Over(NavStripRules.RestTextOn(hue, tint), ground), ground);
                var glyph = NavStripRules.Contrast(NavStripRules.Over(NavStripRules.RestGlyphOn(hue, tint), ground), ground);
                var lit = NavStripRules.Contrast(NavStripRules.ActiveGlyphOn(hue, tint), hue);
                var h = NavStripRules.ToHsl(tint).H;
                _out.WriteLine($"{s.Key,-10} {pills[i].Key,-18} #{tint.R:X2}{tint.G:X2}{tint.B:X2} h {h,6:0.0} label {label:0.00} glyph {glyph:0.00} lit glyph {lit:0.00}");
                Assert.True(label >= 4.5, $"{s.Key}/{pills[i].Key}: label {label:0.00}:1 on its tint");
                Assert.True(glyph >= 4.5, $"{s.Key}/{pills[i].Key}: rest glyph {glyph:0.00}:1 on its tint");
                Assert.True(lit >= 3.0, $"{s.Key}/{pills[i].Key}: active glyph {lit:0.00}:1 on the section hue");
                if (prevHue != null)
                    Assert.True(NavStripRules.HueDistance(prevHue.Value, h) >= NavStripRules.TabHueStep - 0.5,
                        $"{s.Key}: pill {i} is only {NavStripRules.HueDistance(prevHue.Value, h):0.0} degrees from its neighbour");
                prevHue = h;
            }
            // The bar still reads as one section: the middle of the bar is the section hue.
            if (pills.Count % 2 == 1)
                Assert.Equal(hue, NavStripRules.TabTint(s.Key, pills[pills.Count / 2].Key, pills.Count / 2, pills.Count));
        }
    }

    [Fact]
    public void HslRoundTripsAndRotationKeepsSaturationAndLightness()
    {
        foreach (var c in new[] { NavStripRules.Pink, NavStripRules.Orchid, NavStripRules.Sky, NavStripRules.Sage, NavStripRules.Coral })
        {
            Assert.Equal(c, NavStripRules.FromHsl(NavStripRules.ToHsl(c).H, NavStripRules.ToHsl(c).S, NavStripRules.ToHsl(c).L));
            var turned = NavStripRules.ToHsl(NavStripRules.RotateHue(c, 27));
            var was = NavStripRules.ToHsl(c);
            Assert.Equal(was.S, turned.S, 1);
            Assert.Equal(was.L, turned.L, 1);
            Assert.Equal(27, NavStripRules.HueDistance(was.H, turned.H), 0);
        }
    }

    [Fact]
    public void PillsAreAtLeast36TallAndEveryHeaderRowIsOneHeight()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            double? row = null;
            foreach (var s in NavSections.Order.Where(s => s.Key != NavSections.Home))
            {
                var strip = Laid(s.Key, s.Key == NavSections.Settings ? "appsettings" : s.DefaultTab);
                foreach (var key in strip.PillKeys)
                    Assert.True(strip.PillHeight(key) >= NavStripRules.PillHeight, $"{s.Key}/{key} pill is {strip.PillHeight(key)} px tall");
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
