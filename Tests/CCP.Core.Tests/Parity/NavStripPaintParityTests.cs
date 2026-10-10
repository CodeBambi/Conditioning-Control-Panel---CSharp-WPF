using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Nav;
using Xunit;

namespace CCP.Core.Tests.Parity;

/// <summary>
/// Parity with WPF 7.1.5 section hues and pill paint (NavStripHueTests, SectionTabStripTests hue
/// rules, NavStripPolishTests, TypeScaleContrastTests): every contrast floor, the per-tab tints,
/// the bevel stops and the section ink recipe, on ARGB colours.
/// </summary>
public class NavStripPaintParityTests
{
    private static readonly uint[] Hues =
    {
        NavStripRules.Lilac, NavStripRules.Pink, NavStripRules.Orchid, NavStripRules.VioletBlue,
        NavStripRules.Sky, NavStripRules.Coral, NavStripRules.Sage,
    };

    [Fact]
    public void TheHueTableIsTheWpfTable()
    {
        Assert.Equal(new uint[] { 0xFFB79CFF, 0xFFFF69B4, 0xFFE070FF, 0xFF7A86FF, 0xFF5FB0FF, 0xFFFF9A6B, 0xFFA8D8A0 }, Hues);
        Assert.Equal(NavStripRules.Lilac, NavStripRules.Accent(NavSections.Home));
        Assert.Equal(NavStripRules.Lilac, NavStripRules.Accent(NavSections.Settings));
        Assert.Equal(NavStripRules.Lilac, NavStripRules.Accent(null));
        Assert.Equal(NavStripRules.Pink, NavStripRules.Accent(NavSections.Studio));
        Assert.Equal(NavStripRules.Orchid, NavStripRules.Accent(NavSections.Companion));
        Assert.Equal(NavStripRules.VioletBlue, NavStripRules.Accent(NavSections.Play));
        Assert.Equal(NavStripRules.Sky, NavStripRules.Accent(NavSections.Social));
        Assert.Equal(NavStripRules.Coral, NavStripRules.Accent(NavSections.You));
        Assert.Equal(NavStripRules.Sage, NavStripRules.Accent(NavSections.Library));
        Assert.Equal(0xFF15121Fu, NavStripRules.DarkInk);
        Assert.Equal(0xFF1A1230u, NavStripRules.PageGround);
        Assert.Equal(0xFFF0F0F5u, NavStripRules.TextLight);
    }

    private static double Hue(uint c)
    {
        double r = Argb.R(c) / 255.0, g = Argb.G(c) / 255.0, b = Argb.B(c) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d == 0) return 0;
        double h = max == r ? ((g - b) / d) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60;
        return h < 0 ? h + 360 : h;
    }

    [Fact]
    public void SectionHuesStayClearOfTheReservedColours()
    {
        var reserved = new Dictionary<string, double> { ["gold"] = 45, ["cyan"] = 190, ["red"] = 0, ["mint"] = 155 };
        foreach (var c in Hues)
            foreach (var (name, h) in reserved)
                Assert.True(NavStripRules.HueDistance(Hue(c), h) >= 15, $"{Argb.ToHex(c)} sits near {name}");
        Assert.Equal(7, NavSections.Order.Select(s => NavStripRules.Accent(s.Key)).Distinct().Count());
        for (var i = 0; i < Hues.Length; i++)
            for (var j = i + 1; j < Hues.Length; j++)
                Assert.True(NavStripRules.HueDistance(Hue(Hues[i]), Hue(Hues[j])) >= 18);
    }

    [Fact]
    public void TheActivePillTextReadsOnEverySectionHue()
    {
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripRules.Accent(s.Key);
            var ink = NavStripRules.ActiveTextOn(hue);
            Assert.True(NavStripRules.Contrast(ink, hue) >= 4.5, s.Key);
            Assert.True(ink == NavStripRules.DarkInk || ink == Argb.White);
        }
    }

    [Fact]
    public void ContrastMatchesTheWcagReferencePoints()
    {
        Assert.Equal(21.0, NavStripRules.Contrast(Argb.Black, Argb.White), 2);
        Assert.Equal(1.0, NavStripRules.Contrast(NavStripRules.Lilac, NavStripRules.Lilac), 3);
        Assert.Equal(NavStripRules.DarkInk, NavStripRules.ActiveTextOn(Argb.White));
        Assert.Equal(Argb.White, NavStripRules.ActiveTextOn(Argb.Black));
    }

    [Fact]
    public void RestPillTextReadsOnTheTintedTrackForEverySection()
    {
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripRules.Accent(s.Key);
            var text = NavStripRules.RestTextOn(hue);
            var ground = NavStripRules.RestGround(hue);
            Assert.True(NavStripRules.Contrast(NavStripRules.Over(text, ground), ground) >= 4.5, s.Key);
            Assert.Equal((byte)Math.Round(NavStripRules.RestTextAlpha * 255), Argb.A(text));
        }
    }

    [Fact]
    public void EveryTabTintReadsAndNeighboursStepNineDegrees()
    {
        foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
        {
            var hue = NavStripRules.Accent(s.Key);
            var pills = NavStripRules.Pills(s.Key);
            double? prevHue = null;
            for (int i = 0; i < pills.Count; i++)
            {
                var tint = NavStripRules.TabTint(s.Key, pills[i].Key, i, pills.Count);
                var ground = NavStripRules.RestGround(hue, tint);
                Assert.True(NavStripRules.Contrast(NavStripRules.Over(NavStripRules.RestTextOn(hue, tint), ground), ground) >= 4.5);
                Assert.True(NavStripRules.Contrast(NavStripRules.Over(NavStripRules.RestGlyphOn(hue, tint), ground), ground) >= 4.5);
                Assert.True(NavStripRules.Contrast(NavStripRules.ActiveGlyphOn(hue, tint), hue) >= 3.0);
                var h = NavStripRules.ToHsl(tint).H;
                if (prevHue != null)
                    Assert.True(NavStripRules.HueDistance(prevHue.Value, h) >= NavStripRules.TabHueStep - 0.5);
                prevHue = h;
            }
            if (pills.Count % 2 == 1)
                Assert.Equal(hue, NavStripRules.TabTint(s.Key, pills[pills.Count / 2].Key, pills.Count / 2, pills.Count));
        }
    }

    [Fact]
    public void HslRoundTripsAndRotationKeepsSaturationAndLightness()
    {
        foreach (var c in new[] { NavStripRules.Pink, NavStripRules.Orchid, NavStripRules.Sky, NavStripRules.Sage, NavStripRules.Coral })
        {
            var (h, sat, l) = NavStripRules.ToHsl(c);
            Assert.Equal(c, NavStripRules.FromHsl(h, sat, l));
            var turned = NavStripRules.ToHsl(NavStripRules.RotateHue(c, 27));
            Assert.Equal(sat, turned.S, 1);
            Assert.Equal(l, turned.L, 1);
            Assert.Equal(27, NavStripRules.HueDistance(h, turned.H), 0);
        }
    }

    [Fact]
    public void ThePlateOutlineRingAndTrayStopsAreTheWpfBrushes()
    {
        foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
        {
            var hue = NavStripRules.Accent(s.Key);
            var tray = NavStripRules.TrackBorderStops(hue);
            Assert.Equal(NavStripRules.WithAlpha(NavStripRules.DarkInk, NavStripRules.TrackInsetAlpha), tray[0].Color);
            Assert.Equal(NavStripRules.WithAlpha(hue, NavStripRules.TrackBorderAlpha), tray[^1].Color);
            Assert.Equal(new[] { 0.0, 0.45, 1.0 }, tray.Select(t => t.Offset).ToArray());

            var plate = NavStripRules.PlateStops(hue, NavStripRules.RestFillAlpha);
            Assert.Equal(NavStripRules.WithAlpha(hue, NavStripRules.RestFillAlpha + NavStripRules.PlateLift), plate[0].Color);
            Assert.Equal(NavStripRules.WithAlpha(hue, NavStripRules.RestFillAlpha - NavStripRules.PlateLift), plate[^1].Color);

            var ring = NavStripRules.ActiveRingStops(hue);
            Assert.Equal(NavStripRules.ActiveRingColor(hue, NavStripRules.ActiveRingTopAlpha), ring[0].Color);
        }
        Assert.Equal(new[] { 0.0, 0.35, 0.65, 0.85 }, NavStripRules.OutlineStops(NavStripRules.Lilac).Select(g => g.Offset).ToArray());
    }

    [Fact]
    public void TheBevelIsVisibleOnEveryTint()
    {
        foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
        {
            var keys = s.Tabs.Select(t => t.Key).ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                var tint = NavStripRules.TabTint(s.Key, keys[i], i, keys.Count);
                var stops = NavStripRules.OutlineStops(tint);
                double delta = NavStripRules.Luminance(Argb.Opaque(stops[0].Color)) - NavStripRules.Luminance(Argb.Opaque(stops[^1].Color));
                Assert.True(delta >= 0.20, $"{s.Key}/{keys[i]}: bevel luminance delta {delta:F3}");
            }
        }
    }

    [Fact]
    public void ThePillNumbersAreTheWpfNumbers()
    {
        Assert.Equal(1.5, NavStripRules.RestFaceThickness);
        Assert.Equal(2.0, NavStripRules.ActiveFaceThickness);
        Assert.Equal(0.85, NavStripRules.OutlineFootOffset);
        Assert.Equal((38.0, 14.5, 16.0, 6.0), (NavStripRules.PillHeight, NavStripRules.PillFontSize, NavStripRules.PillPadding, NavStripRules.PillGap));
        Assert.Equal((0.95, 0.24, 0.10, 0.95), (NavStripRules.RestTextAlpha, NavStripRules.RestFillAlpha, NavStripRules.PlateLift, NavStripRules.RestOutlineAlpha));
        Assert.Equal((0.70, 0.75, 0.38), (NavStripRules.BevelLight, NavStripRules.BevelShade, NavStripRules.HoverFillAlpha));
        Assert.Equal((0.40, 0.10, 0.40, 0.70), (NavStripRules.TrackInkAlpha, NavStripRules.TrackFillAlpha, NavStripRules.TrackBorderAlpha, NavStripRules.TrackInsetAlpha));
        Assert.Equal((16.0, 0.75), (NavStripRules.ActiveGlowBlur, NavStripRules.ActiveGlowOpacity));
        Assert.Equal((0.55, 0.80, 0.30, 0.75, 0.30), (NavStripRules.ActiveRingAlpha, NavStripRules.ActiveRingTopAlpha,
            NavStripRules.ActiveRingFootAlpha, NavStripRules.ActiveRingWhite, NavStripRules.ActiveGlyphTint));
        Assert.Equal((50.0, 52.0, 24.0, 8.0, 4.0), (NavStripRules.BadgeMaxWidth, NavStripRules.BadgePlateWidth,
            NavStripRules.BadgePlateHeight, NavStripRules.BadgeGap, NavStripRules.BadgePadExtra));
        Assert.Equal((9.0, 16.0, 0.90), (NavStripRules.TabHueStep, NavStripRules.GlyphSize, NavStripRules.NoteTextAlpha));
    }

    // ---- section ink (TypeScaleContrastTests) ------------------------------------------------

    // Colors.xaml 7.1.5 tokens, copied: PanelBg, ElevatedSurface, TextLight / Secondary / Muted.
    private static readonly uint PanelBg = 0xFF1C1C35, ElevatedSurface = 0xFF28284C;

    private static IEnumerable<(string Name, uint Ground)> Grounds()
    {
        foreach (var s in NavSections.Order)
            yield return ($"{s.Key} page", NavStripRules.Over(Argb.WithAlpha(NavStripRules.Accent(s.Key), SectionChromeRules.SectionWashAlpha), PanelBg));
        yield return ("card", ElevatedSurface);
    }

    [Theory]
    [InlineData(0xFFF0F0F5u, 10.0)]   // TextLight
    [InlineData(0xFFCCCCE0u, 7.5)]    // TextSecondary
    [InlineData(0xFFA7A7C1u, 5.0)]    // TextMuted
    public void EveryTextTierReadsOnEveryWashedPageAndOnTheCard(uint ink, double floor)
    {
        foreach (var (name, ground) in Grounds())
            Assert.True(NavStripRules.Contrast(ink, ground) >= floor, $"{Argb.ToHex(ink)} on {name}");
    }

    [Fact]
    public void SectionInkReadsAndTintStaysNearWhiteForEverySection()
    {
        foreach (var s in NavSections.Order)
            foreach (var (name, ground) in Grounds())
            {
                Assert.True(NavStripRules.Contrast(NavStripRules.Ink(s.Key), ground) >= 4.5, $"Ink({s.Key}) on {name}");
                Assert.True(NavStripRules.Contrast(NavStripRules.Tint(s.Key), ground) >= 9.0, $"Tint({s.Key}) on {name}");
            }
    }

    [Fact]
    public void TheSectionHelpersFollowTheirRecipe()
    {
        // Colors.xaml static defaults (Lilac): SectionInk #CBB9FC, SectionTint #E7E3F6,
        // SectionRule #59B79CFF, SectionOutline #40B79CFF.
        Assert.Equal(0xFFCBB9FCu, NavStripRules.Ink(NavSections.Home));
        Assert.Equal(0xFFE7E3F6u, NavStripRules.Tint(NavSections.Home));
        Assert.Equal(0x59B79CFFu, NavStripRules.Rule(NavSections.Home));
        Assert.Equal(0x40B79CFFu, NavStripRules.Outline(NavSections.Home));
        Assert.Equal(0xFFA3ABFCu, NavStripRules.Ink(NavSections.Play));
        Assert.Equal(0xFFC1E0BEu, NavStripRules.Ink(NavSections.Library));
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripRules.Accent(s.Key);
            Assert.Equal(Argb.WithAlpha(hue, (byte)0x59), NavStripRules.Rule(s.Key));
            Assert.Equal(Argb.WithAlpha(hue, (byte)0x40), NavStripRules.Outline(s.Key));
            Assert.Equal(255, Argb.A(NavStripRules.Ink(s.Key)));
            Assert.Equal(255, Argb.A(NavStripRules.Tint(s.Key)));
        }
    }

    [Fact]
    public void TheCardLiftsOffThePageWithoutFlatteningTheInset()
        => Assert.InRange(NavStripRules.Contrast(ElevatedSurface, PanelBg), 1.15, 1.25);

    [Fact]
    public void TheWashNumbersAreTheWpfNumbers()
    {
        Assert.Equal(0x24, SectionChromeRules.SectionWashAlpha);
        Assert.Equal(0x59, SectionChromeRules.SectionWashLineAlpha);
        Assert.Equal(250, SectionChromeRules.SectionWashMs);
    }

    [Fact]
    public void CompositeAndOverAgreeOnAnOpaqueGround()
    {
        var top = Argb.WithAlpha(NavStripRules.Sky, 0.4);
        Assert.Equal(NavStripRules.Over(top, NavStripRules.PageGround), NavStripRules.Composite(top, NavStripRules.PageGround));
        Assert.Equal(0u, NavStripRules.Composite(0x00FFFFFF, 0x00000000));
    }
}
