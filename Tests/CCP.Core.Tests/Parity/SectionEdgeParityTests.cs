using System;
using System.Linq;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests.Parity;

/// <summary>
/// Parity with WPF 7.1.5 SectionEdgeRulesTests, the fog band of FogPolish11Tests, the NavGlow
/// timings (NavStripPolishTests / SectionTabStripTests) and WindowDefaultSizeTests' arithmetic.
/// </summary>
public class SectionEdgeParityTests
{
    [Theory]
    [InlineData(NavSections.Home, 0x32)]
    [InlineData(NavSections.Settings, 0x32)]
    [InlineData(NavSections.Studio, 0x37)]
    [InlineData(NavSections.Companion, 0x37)]
    [InlineData(NavSections.Play, 0x3D)]
    [InlineData(NavSections.Social, 0x33)]
    [InlineData(NavSections.You, 0x30)]
    [InlineData(NavSections.Library, 0x2A)]
    public void GlowAlphaPerSection(string section, int expected)
        => Assert.Equal(expected, SectionEdgeRules.GlowAlpha(NavStripRules.Accent(section)));

    [Fact]
    public void GlowAlphaIsClampedForExtremeHues()
    {
        Assert.Equal((byte)Math.Round(0.14 * 255), SectionEdgeRules.GlowAlpha(Argb.White));
        Assert.Equal((byte)Math.Round(0.26 * 255), SectionEdgeRules.GlowAlpha(Argb.FromRgb(0x20, 0x10, 0x60)));
        Assert.Equal((byte)Math.Round(0.26 * 255), SectionEdgeRules.GlowAlpha(Argb.Black));
    }

    [Fact]
    public void GlowRunsFromTheEdgeAlphaToNothing()
    {
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripRules.Accent(s.Key);
            var stops = SectionEdgeRules.GlowStops(hue);
            Assert.Equal(3, stops.Length);
            Assert.Equal(SectionEdgeRules.GlowAlpha(hue), Argb.A(stops[0]));
            Assert.Equal((byte)Math.Round(Argb.A(stops[0]) * 0.42), Argb.A(stops[1]));
            Assert.Equal(0, Argb.A(stops[2]));
            Assert.All(stops, c => Assert.Equal(hue & 0xFFFFFF, c & 0xFFFFFF));
        }
        Assert.Equal(new[] { 0.0, 0.45, 1.0 }, SectionEdgeRules.GlowOffsets);
        Assert.Equal(28, SectionEdgeRules.GlowBand);
    }

    [Fact]
    public void LineIsTheHueAndTheLiftRidesItsOwnStripsExceptAtOff()
    {
        var hue = NavStripRules.Sky;
        var line = Argb.WithAlpha(hue, (byte)0xE6);
        foreach (var level in new[] { MotionLevel.Full, MotionLevel.Reduced, MotionLevel.Off })
            Assert.All(SectionEdgeRules.LineStops(hue, level), c => Assert.Equal(line, c));

        var lift = SectionEdgeRules.LiftStops(hue, MotionLevel.Full);
        Assert.Equal(NavStripRules.Mix(hue, Argb.White, 0.35), lift[1]);
        Assert.Equal(0xFF, Argb.A(lift[1]));
        Assert.True(NavStripRules.Luminance(lift[1]) > NavStripRules.Luminance(hue));
        Assert.Equal(0, Argb.A(lift[0]));
        Assert.Equal(0, Argb.A(lift[2]));
        Assert.Equal(lift, SectionEdgeRules.LiftStops(hue, MotionLevel.Reduced));
        Assert.All(SectionEdgeRules.LiftStops(hue, MotionLevel.Off), c => Assert.Equal(0, Argb.A(c)));
        Assert.Equal(new[] { 0.4, 0.5, 0.6 }, SectionEdgeRules.LiftOffsets);
        // The WPF XAML default lift for Lilac is #FFD0BFFF.
        Assert.Equal(0xFFD0BFFFu, SectionEdgeRules.LiftColor(NavStripRules.Lilac));
        Assert.Equal((3.0, 8.0, 3.0, 8.0), (SectionEdgeRules.LineThickness, SectionEdgeRules.CornerRadius,
            SectionEdgeRules.LiftStripPx, SectionEdgeRules.LiftStripInset));
    }

    [Fact]
    public void TheLiftLapsClockwiseOneSideAtATimeAndParksOffTheStrip()
    {
        Assert.Equal(SectionEdgeRules.SpinSeconds / 4, SectionEdgeRules.LiftLegSeconds);
        Assert.Equal(0, SectionEdgeRules.LiftLegStart(EdgeSide.Top));
        Assert.Equal(3, SectionEdgeRules.LiftLegStart(EdgeSide.Right));
        Assert.Equal(6, SectionEdgeRules.LiftLegStart(EdgeSide.Bottom));
        Assert.Equal(9, SectionEdgeRules.LiftLegStart(EdgeSide.Left));
        Assert.Equal((-1.0, 1.0), SectionEdgeRules.LiftTravel(EdgeSide.Top));
        Assert.Equal((-1.0, 1.0), SectionEdgeRules.LiftTravel(EdgeSide.Right));
        Assert.Equal((1.0, -1.0), SectionEdgeRules.LiftTravel(EdgeSide.Bottom));
        Assert.Equal((1.0, -1.0), SectionEdgeRules.LiftTravel(EdgeSide.Left));
        Assert.Equal(0, SectionEdgeRules.LiftRest(EdgeSide.Top, SectionEdgeMotion.Fixed));
        Assert.Equal(-1, SectionEdgeRules.LiftRest(EdgeSide.Right, SectionEdgeMotion.Fixed));
        Assert.Equal(1, SectionEdgeRules.LiftRest(EdgeSide.Bottom, SectionEdgeMotion.Fixed));
        Assert.Equal(-1, SectionEdgeRules.LiftRest(EdgeSide.Top, SectionEdgeMotion.Spin));
    }

    [Fact]
    public void OnlyFullWithAmbientLoopsSpins()
    {
        Assert.Equal(SectionEdgeMotion.Spin, SectionEdgeRules.MotionFor(MotionLevel.Full, true));
        Assert.Equal(SectionEdgeMotion.Fixed, SectionEdgeRules.MotionFor(MotionLevel.Full, false));
        Assert.Equal(SectionEdgeMotion.Fixed, SectionEdgeRules.MotionFor(MotionLevel.Reduced, true));
        Assert.Equal(SectionEdgeMotion.Solid, SectionEdgeRules.MotionFor(MotionLevel.Off, true));
        Assert.Equal(12, SectionEdgeRules.SpinSeconds);
        Assert.Equal(24, SectionEdgeRules.SpinFps);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public void TheSpinRunsOnlyInAnActiveShownWindow(bool active, bool visible, bool minimized, bool runs)
    {
        Assert.Equal(runs, SectionEdgeRules.SpinShouldRun(SectionEdgeMotion.Spin, active, visible, minimized));
        Assert.False(SectionEdgeRules.SpinShouldRun(SectionEdgeMotion.Fixed, active, visible, minimized));
        Assert.False(SectionEdgeRules.SpinShouldRun(SectionEdgeMotion.Solid, active, visible, minimized));
    }

    [Fact]
    public void TheStaticBandThinsUnderTheFogAndKeepsItsWeightWithout()
    {
        Assert.Equal(28, SectionEdgeRules.BandDepth(false));
        Assert.Equal(16, SectionEdgeRules.BandDepth(true));
        foreach (var hue in new[] { NavStripRules.Lilac, NavStripRules.Sky, NavStripRules.Pink, NavStripRules.Sage, NavStripRules.VioletBlue })
        {
            Assert.Equal(SectionEdgeRules.GlowStops(hue), SectionEdgeRules.GlowStops(hue, false));
            var fog = SectionEdgeRules.GlowStops(hue, true);
            Assert.Equal((byte)Math.Round(SectionEdgeRules.GlowAlpha(hue) * 0.7), Argb.A(fog[0]));
            Assert.True(Argb.A(fog[0]) < SectionEdgeRules.GlowAlpha(hue));
            Assert.Equal(0, Argb.A(fog[2]));
            Assert.InRange(SectionEdgeRules.FogGain(hue), 0.69, 1.31);
        }
        Assert.True(SectionEdgeRules.FogGain(NavStripRules.Sage) < SectionEdgeRules.FogGain(NavStripRules.VioletBlue));
    }

    // ---- NavGlow -----------------------------------------------------------------------------

    [Fact]
    public void TheGlowLastsTwoSecondsAndIsOffWithMotionOff()
    {
        Assert.Equal(0, NavGlowRules.TotalMs(MotionLevel.Off));
        Assert.Equal(2000, NavGlowRules.TotalMs(MotionLevel.Reduced));
        Assert.Equal(3000, NavGlowRules.TotalMs(MotionLevel.Full));
        Assert.Equal((600, 2000, 400), (NavGlowRules.SheenMs, NavGlowRules.HoldMs, NavGlowRules.FadeMs));
    }

    [Fact]
    public void TheGlowOpacityTrackIsTheWpfKeyframes()
    {
        Assert.Equal(0, NavGlowRules.OpacityAt(MotionLevel.Full, 0), 9);
        Assert.Equal(1.0, NavGlowRules.OpacityAt(MotionLevel.Full, 300), 9);
        Assert.Equal(0.8, NavGlowRules.OpacityAt(MotionLevel.Full, 600), 9);
        Assert.Equal(0.8, NavGlowRules.OpacityAt(MotionLevel.Full, 2599), 9);
        Assert.Equal(0.4, NavGlowRules.OpacityAt(MotionLevel.Full, 2800), 9);
        Assert.Equal(0, NavGlowRules.OpacityAt(MotionLevel.Full, 3000), 9);
        // Sine ease-out (WPF's default SineEase mode) is ahead of linear on the way in.
        Assert.True(NavGlowRules.OpacityAt(MotionLevel.Full, 150) > 0.5);

        Assert.Equal(0.85, NavGlowRules.OpacityAt(MotionLevel.Reduced, 0), 9);
        Assert.Equal(0.85, NavGlowRules.OpacityAt(MotionLevel.Reduced, 2000), 9);
        Assert.Equal(0, NavGlowRules.OpacityAt(MotionLevel.Reduced, 2001), 9);
        Assert.Empty(NavGlowRules.OpacityTrack(MotionLevel.Off));
    }

    [Fact]
    public void TheGlowGeometryIsTheAdornerGeometry()
    {
        Assert.Equal((3.0, 2.5, 14.0), (NavGlowRules.RingOutsetPx, NavGlowRules.RingThickness, NavGlowRules.MaxRadius));
        Assert.Equal(new byte[] { 90, 70, 50, 30 }, Enumerable.Range(0, NavGlowRules.HaloCount).Select(NavGlowRules.HaloAlpha).ToArray());
        Assert.Equal(new[] { 3.0, 6.0, 9.0, 12.0 }, Enumerable.Range(0, NavGlowRules.HaloCount).Select(NavGlowRules.HaloInflate).ToArray());
        Assert.Equal(0x26, NavGlowRules.FillAlpha);
        Assert.Equal(14, NavGlowRules.RingRadius(38));    // a pill: capped
        Assert.Equal(8, NavGlowRules.RingRadius(10));     // a short target: half its ring height
    }

    // ---- WindowFitRule (WindowDefaultSizeTests, the arithmetic) -----------------------------

    [Fact]
    public void TheDefaultSizeIsTheWpfDefault()
    {
        Assert.Equal(1661, WindowFitRule.DefaultWidthDip);
        Assert.Equal(1002, WindowFitRule.DefaultHeightDip);
        Assert.Equal(WindowFitRule.DefaultWidthDip, Math.Round(1563 * 1.25 * 0.85));
        Assert.Equal(WindowFitRule.DefaultHeightDip, Math.Round(943 * 1.25 * 0.85));
    }

    [Fact]
    public void A_window_that_fits_is_never_touched_and_never_grown()
    {
        Assert.Equal((1954.0, 1179.0), WindowFitRule.Fit(1954, 1179, 2560, 1392));
        Assert.Equal((1000.0, 600.0), WindowFitRule.Fit(1000, 600, 2560, 1392));
        Assert.Equal((1954.0, 1179.0), WindowFitRule.Fit(1954, 1179, 1954, 1179));
    }

    [Fact]
    public void A_1080p_desk_shrinks_both_axes_by_one_factor()
    {
        var (w, h) = WindowFitRule.Fit(1954, 1179, 1920, 1032);
        Assert.Equal(1032, h, 6);
        Assert.Equal(1954 * 1032.0 / 1179, w, 6);
        Assert.True(w < 1920);
    }

    [Fact]
    public void A_width_bound_area_lands_the_width_and_scales_the_height()
    {
        var (w, h) = WindowFitRule.Fit(1954, 1179, 1500, 1400);
        Assert.Equal(1500, w, 6);
        Assert.Equal(1179 * 1500.0 / 1954, h, 6);
    }

    [Theory]
    [InlineData(1920, 1032)]
    [InlineData(1536, 826)]
    [InlineData(1280, 688)]
    [InlineData(1707, 928)]
    [InlineData(1280, 672)]
    public void Every_common_desk_keeps_the_aspect_and_fits(double areaW, double areaH)
    {
        var (w, h) = WindowFitRule.Fit(1954, 1179, areaW, areaH);
        Assert.True(w <= areaW + 1e-9 && h <= areaH + 1e-9);
        Assert.Equal(1954.0 / 1179, w / h, 6);
        Assert.True(Math.Abs(w - areaW) < 1e-6 || Math.Abs(h - areaH) < 1e-6);
    }

    [Fact]
    public void Broken_measurements_never_collapse_the_window()
    {
        Assert.Equal((1954.0, 1179.0), WindowFitRule.Fit(1954, 1179, 0, 1032));
        Assert.Equal((1954.0, 1179.0), WindowFitRule.Fit(1954, 1179, double.NaN, 1032));
        Assert.Equal((1954.0, 1179.0), WindowFitRule.Fit(1954, 1179, 1920, double.PositiveInfinity));
        Assert.Equal((double.NaN, 1179.0), WindowFitRule.Fit(double.NaN, 1179, 1920, 1032));
    }

    [Fact]
    public void The_pixel_path_rounds_and_never_passes_the_area()
    {
        var (w, h) = WindowFitRule.FitPx(2443, 1474, 1920, 1032);
        Assert.Equal(1032, h);
        Assert.Equal((int)Math.Round(2443 * 1032.0 / 1474), w);
        Assert.True(w <= 1920);
        Assert.Equal((1954, 1179), WindowFitRule.FitPx(1954, 1179, 2560, 1392));
        Assert.Equal((1954, 1179), WindowFitRule.FitPx(1954, 1179, 0, 1392));
        var first = WindowFitRule.FitPx(2443, 1474, 1920, 1032);
        Assert.Equal(first, WindowFitRule.FitPx(first.Width, first.Height, 1920, 1032));
    }
}
