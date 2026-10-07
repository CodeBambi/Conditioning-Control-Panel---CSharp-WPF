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
/// Nav polish wave 3 (2026-10-06, lane STRIP): the strip reads as a tab bar. Every pill wears a
/// leading glyph from one table, every glyph in that table exists in the font on this machine
/// (a missing glyph would draw a box), the lit tab floats on a soft glow that Motion Off drops,
/// pills sit 6 px apart, and the crumb steps down to 14 px so the pills are the loudest thing.
/// Set NAV_STRIP_SHOTS to a folder to save strip-&lt;section&gt;.png and strip-&lt;section&gt;-locked.png
/// (rendered at Motion Full, so the glow is in the shot).
/// </summary>
public class NavStripPolishGlyphTests
{
    private readonly ITestOutputHelper _out;
    public NavStripPolishGlyphTests(ITestOutputHelper output) => _out = output;

    private static SectionTabStrip Laid(string section, string tab, MotionLevel level = MotionLevel.Off, double width = 1480)
    {
        var strip = new SectionTabStrip { MotionOverride = level, Width = width };
        strip.Show(section, tab);
        strip.Measure(new Size(width, 200));
        strip.Arrange(new Rect(0, 0, width, strip.DesiredSize.Height));
        strip.UpdateLayout();
        return strip;
    }

    [Fact]
    public void EveryGlyphInTheTableRendersWithTheFontOnThisMachine()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var typeface = new Typeface(new FontFamily(NavStripRules.GlyphFont), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            Assert.True(typeface.TryGetGlyphTypeface(out var gt), NavStripRules.GlyphFont + " is not installed");
            foreach (var (key, ch) in NavStripRules.Glyphs)
            {
                Assert.True(gt.CharacterToGlyphMap.TryGetValue(ch, out var index) && index != 0,
                    $"{key}: U+{(int)ch:X4} is not in {NavStripRules.GlyphFont}");
                // A real outline, not an empty glyph.
                var geometry = gt.GetGlyphOutline(index, 16, 16);
                Assert.False(geometry.IsEmpty(), $"{key}: U+{(int)ch:X4} has no outline");
                Assert.True(SectionTabStrip.GlyphRenders(ch.ToString()), key);
                _out.WriteLine($"{key,-18} U+{(int)ch:X4} glyph {index}");
            }
        });
    }

    [Fact]
    public void TheGlyphTableOnlyNamesRealTabsAndAnUnknownKeyHasNoGlyph()
    {
        var keys = NavSections.AllTabs.Select(t => t.Key).ToHashSet();
        foreach (var key in NavStripRules.Glyphs.Keys)
            Assert.Contains(key, keys);
        Assert.Null(NavStripRules.Glyph("no-such-tab"));
        Assert.Null(NavStripRules.Glyph(null));
        Assert.Equal(NavStripRules.Glyph("friends"), NavStripRules.Glyph("FRIENDS"));
    }

    [Fact]
    public void EveryPillShowsItsGlyphAndTheGlyphWearsTheLabelsColour()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
            {
                var strip = Laid(s.Key, NavStripRules.HeaderTab(s.Key));
                foreach (var key in strip.PillKeys)
                {
                    Assert.Equal(NavStripRules.Glyph(key), strip.PillGlyph(key));
                    Assert.NotNull(strip.PillGlyph(key));   // every visible pill is in the table
                }
            }
        });
    }

    [Fact]
    public void TheLitTabGlowsAndMotionOffDropsTheGlow()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var full = Laid(NavSections.Social, "friends", MotionLevel.Full);
            var glow = full.ActiveGlow;
            Assert.NotNull(glow);
            Assert.Equal(0, glow!.ShadowDepth);
            Assert.Equal(NavStripRules.ActiveGlowBlur, glow.BlurRadius);
            Assert.Equal(NavStripRules.ActiveGlowOpacity, glow.Opacity);
            Assert.Equal(NavStripRules.Accent(NavSections.Social), glow.Color);

            Assert.Null(Laid(NavSections.Social, "friends", MotionLevel.Off).ActiveGlow);
        });
    }

    [Fact]
    public void PillsSitSixApartAndArePaddedSixteen()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = Laid(NavSections.Library, "assets");
            var keys = strip.PillKeys;
            for (int i = 1; i < keys.Count; i++)
            {
                var prev = strip.PillFor(keys[i - 1])!;
                var next = strip.PillFor(keys[i])!;
                var prevRight = prev.TranslatePoint(new Point(prev.ActualWidth, 0), strip).X;
                var nextLeft = next.TranslatePoint(new Point(0, 0), strip).X;
                Assert.Equal(NavStripRules.PillGap, nextLeft - prevRight, 1);
            }
        });
    }

    [Fact]
    public void EveryTabBarFitsTheHeaderAtTheDefaultWindowWidth()
    {
        // 1585 window - 96 rail - 2 x 10 strip margin (NavFinalRenderTests renders the strip at 1489).
        const double width = 1469;
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
            {
                var strip = Laid(s.Key, NavStripRules.HeaderTab(s.Key), MotionLevel.Off, width);
                var last = strip.PillFor(strip.PillKeys.Last())!;
                var right = last.TranslatePoint(new Point(last.ActualWidth, 0), strip).X;
                _out.WriteLine($"{s.Key,-10} last pill ends at {right:0} of {width}");
                Assert.True(right <= width, $"{s.Key}: the tab bar runs to {right:0} px, past {width}");
            }
        });
    }

    [Fact]
    public void RenderEverySectionsTabBar()
    {
        var dir = Environment.GetEnvironmentVariable("NAV_STRIP_SHOTS");
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
            {
                var tab = s.Tabs.Where(t => !t.Hidden && t.Kind is NavTabKind.Tab or NavTabKind.Zone)
                                .Skip(1).FirstOrDefault()?.Key ?? NavStripRules.HeaderTab(s.Key);
                var strip = Laid(s.Key, tab, MotionLevel.Full, 1180);
                _out.WriteLine($"{s.Key,-10} {tab,-18} row {strip.DesiredSize.Height:0.0} track {strip.TrackHeightForTests:0.0}");
                if (!string.IsNullOrEmpty(dir)) Shot(strip, dir!, $"strip-{s.Key}.png");

                var locked = NavStripRules.Pills(s.Key).FirstOrDefault(t => t.Tier > 0 && t.Kind is NavTabKind.Tab or NavTabKind.Zone);
                if (locked != null)
                {
                    var lit = Laid(s.Key, locked.Key, MotionLevel.Full, 1180);
                    Assert.Equal(locked.Key, lit.ActivePillKey);
                    if (!string.IsNullOrEmpty(dir)) Shot(lit, dir!, $"strip-{s.Key}-locked.png");
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
            dc.DrawRectangle(new SolidColorBrush(NavStripRules.PageGround), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(-12, -12, element.ActualWidth + 24, element.ActualHeight + 24) },
                null, new Rect(8, 8, element.ActualWidth + 24, element.ActualHeight + 24));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }
}
