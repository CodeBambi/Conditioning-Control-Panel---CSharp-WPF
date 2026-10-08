using System;
using System.Linq;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests.Parity;

/// <summary>
/// Parity with WPF 7.1.5 BannerFxRules (MainWindow.BannerFx.cs) and the HUD depth numbers
/// (MainWindow.HudDepth.cs, MainWindow.HeroFx.cs): the marquee drum rolls at Full only, a beat
/// change does nothing at Off or in the background, the sparkle run is seeded, the bead and the
/// meniscus hide below 5 px of fill, and every HUD shade is ink pulled toward the section hue.
/// </summary>
public class HeaderHudParityTests
{
    [Theory]
    [InlineData(MotionLevel.Full, true)]
    [InlineData(MotionLevel.Reduced, false)]
    [InlineData(MotionLevel.Off, false)]
    public void TheDrumRollsAtFullOnly(MotionLevel level, bool rolls) =>
        Assert.Equal(rolls, BannerFxRules.Roll(level));

    [Fact]
    public void ABeatChangeIsSilentAtOffAndInTheBackground()
    {
        Assert.Equal(default, BannerFxRules.OnBeatChange(MotionLevel.Off, true, true));
        Assert.Equal(default, BannerFxRules.OnBeatChange(MotionLevel.Full, false, true));
        Assert.Equal(new BannerBeatFx(true, true, true), BannerFxRules.OnBeatChange(MotionLevel.Reduced, true, true));
        Assert.Equal(new BannerBeatFx(true, true, false), BannerFxRules.OnBeatChange(MotionLevel.Full, true, false));
    }

    [Fact]
    public void TheSparkleRunIsSeededAndStaysOnTheText()
    {
        var a = BannerFxRules.PlanRun(40, 300, 29, BannerFxRules.SparkleCount, 7);
        var b = BannerFxRules.PlanRun(40, 300, 29, BannerFxRules.SparkleCount, 7);
        Assert.Equal(a, b);
        Assert.Equal(7, a.Count);
        Assert.All(a, f =>
        {
            Assert.InRange(f.Size, 11, 18);
            Assert.InRange(f.FromX, 34, 40 + 300 * 0.12);
            Assert.InRange(f.ToX, 40 + 300 * 0.82, 40 + 300 * 1.02);
            Assert.InRange(Math.Abs(f.Spin), 90, 210);
        });
        Assert.Equal(6 * BannerFxRules.SparkleStaggerSeconds, a[^1].DelaySeconds, 9);
        Assert.Empty(BannerFxRules.PlanRun(0, 3, 29, 7, 1));
    }

    [Fact]
    public void SparkleCurvesMatchTheWpfKeyframes()
    {
        Assert.Equal(0, BannerFxRules.TwinkleAt(0));
        Assert.Equal(1, BannerFxRules.TwinkleAt(0.15), 9);
        Assert.Equal(0.5, BannerFxRules.TwinkleAt(0.4), 9);
        Assert.Equal(1.1, BannerFxRules.TwinkleAt(0.65), 9);
        Assert.Equal(0, BannerFxRules.TwinkleAt(1));
        Assert.Equal(1, BannerFxRules.AlphaAt(0.5));
        Assert.Equal(0, BannerFxRules.AlphaAt(1));
    }

    [Fact]
    public void TheSheenIsThrottledUnlessForced()
    {
        var t0 = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(BannerFxRules.SheenDue(t0.AddSeconds(4), t0, force: false));
        Assert.True(BannerFxRules.SheenDue(t0.AddSeconds(4), t0, force: true));
        Assert.True(BannerFxRules.SheenDue(t0.AddSeconds(BannerFxRules.SheenMinGapSeconds), t0, force: false));
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(4.9, false)]
    [InlineData(5.0, true)]
    [InlineData(double.NaN, false)]
    public void TheBeadHidesBelowFivePixels(double fill, bool shows) =>
        Assert.Equal(shows, HudDepthRules.XpBeadVisible(fill));

    [Fact]
    public void TheMeniscusSitsOnTheTipAndRestsWhenAmbientIsOff()
    {
        Assert.Equal(95, HudDepthRules.XpMeniscusX(100));
        Assert.Equal(0, HudDepthRules.XpMeniscusX(2));
        Assert.Equal(0, HudDepthRules.XpMeniscusOpacity(3, true));
        Assert.Equal(HudDepthRules.XpMeniscusRestOpacity, HudDepthRules.XpMeniscusOpacity(50, false));
        Assert.Equal(10, HudDepthRules.XpMeniscusPx);
    }

    [Fact]
    public void HudShadesAreInkPulledTowardTheHue()
    {
        uint studio = Argb.FromRgb(0xFF, 0x6E, 0xC7);
        Assert.Equal(DepthRules.ShadowColor(studio, DepthRules.WellTopAlpha), HudDepthRules.WellTop(studio));
        Assert.Equal(DepthRules.ShadowColor(studio, DepthRules.WellLeftAlpha), HudDepthRules.WellLeft(studio));
        Assert.Equal(DepthRules.ShadowColor(studio), HudDepthRules.ChipDrop(studio));
        var stops = HudDepthRules.DrumStops(studio);
        Assert.Equal(new[] { 0.0, 0.34, 0.70, 1.0 }, stops.Select(s => s.offset));
        Assert.Equal(HudDepthRules.WellTop(studio), stops[0].argb);
        Assert.Equal(0, Argb.A(stops[1].argb));
        Assert.Equal(0, Argb.A(stops[2].argb));
        Assert.Equal(HudDepthRules.WellLeft(studio), stops[3].argb);
    }

    [Fact]
    public void ChipPopNumbersAreWpfs()
    {
        Assert.Equal(600, HudDepthRules.LevelChipPopMs);
        Assert.Equal(1.35, HudDepthRules.LevelChipPopScale);
        Assert.Equal(-4.0, HudDepthRules.LevelChipPopDegrees);
    }
}
