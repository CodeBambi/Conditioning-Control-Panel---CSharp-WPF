using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The strip colour math both heads wrap (moved from WPF NavStripRules). Goldens are the
/// authored Colors.xaml section ink (Home's Lilac) and WPF's documented rules.</summary>
public sealed class NavStripPaintTests
{
    [Fact]
    public void HomeInkFamilyIsTheAuthoredThemeColours()
    {
        Assert.Equal(0xFFB79CFFu, NavStripPaint.Accent(NavSections.Home));
        Assert.Equal(0xFFCBB9FCu, NavStripPaint.Ink(NavSections.Home));     // Colors.xaml SectionInk
        Assert.Equal(0xFFE7E3F6u, NavStripPaint.Tint(NavSections.Home));    // SectionTint
        Assert.Equal(0x59B79CFFu, NavStripPaint.Rule(NavSections.Home));    // SectionRule
        Assert.Equal(0x40B79CFFu, NavStripPaint.Outline(NavSections.Home)); // SectionOutline
    }

    [Fact]
    public void TintsTurnNineDegreesPerPillAroundTheMiddleAndTextReads()
    {
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripPaint.Accent(s.Key);
            Assert.Equal(hue, NavStripPaint.TabTint(s.Key, 2, 5));          // the middle pill wears the hue
            var (h0, _, _) = NavStripPaint.ToHsl(NavStripPaint.TabTint(s.Key, 1, 5));
            var (h1, _, _) = NavStripPaint.ToHsl(NavStripPaint.TabTint(s.Key, 3, 5));
            var d = System.Math.Abs(h1 - h0) % 360;
            Assert.InRange(System.Math.Min(d, 360 - d), 17, 19);            // 2 x 9 degrees
            Assert.True(NavStripPaint.Contrast(NavStripPaint.ActiveTextOn(hue), hue) >= 4.5, s.Key);
            var tint = NavStripPaint.TabTint(s.Key, 0, 5);
            var ground = NavStripPaint.RestGround(hue, tint);
            var text = NavStripPaint.RestTextOn(hue, tint);
            Assert.True(NavStripPaint.Contrast(NavStripPaint.Over(text, ground), ground) >= 4.5, s.Key);
            Assert.InRange(NavStripPaint.EdgeGlowAlpha(hue), 36, 66);         // 0.14..0.26 x 255
        }
    }
}
