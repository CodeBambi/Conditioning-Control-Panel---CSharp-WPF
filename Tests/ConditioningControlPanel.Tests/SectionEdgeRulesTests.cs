using System.Linq;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 9 (2026-10-06): the window edge wears the section hue. The glow alpha rule is
/// the source; the seven bytes are the proof that every section reads about as bright on the dark
/// page. Motion: Full spins the lift, Reduced holds it still, Off is the plain hue.
/// </summary>
public class SectionEdgeRulesTests
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
    {
        Assert.Equal(expected, SectionEdgeRules.GlowAlpha(NavStripRules.Accent(section)));
    }

    [Fact]
    public void GlowAlphaIsClampedForExtremeHues()
    {
        Assert.Equal((byte)System.Math.Round(0.14 * 255), SectionEdgeRules.GlowAlpha(Colors.White));
        Assert.Equal((byte)System.Math.Round(0.26 * 255), SectionEdgeRules.GlowAlpha(Color.FromRgb(0x20, 0x10, 0x60)));
        Assert.Equal((byte)System.Math.Round(0.26 * 255), SectionEdgeRules.GlowAlpha(Colors.Black));
    }

    [Fact]
    public void GlowRunsFromTheEdgeAlphaToNothing()
    {
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripRules.Accent(s.Key);
            var stops = SectionEdgeRules.GlowStops(hue);
            Assert.Equal(3, stops.Length);
            Assert.Equal(SectionEdgeRules.GlowAlpha(hue), stops[0].A);
            Assert.Equal((byte)System.Math.Round(stops[0].A * 0.42), stops[1].A);
            Assert.Equal(0, stops[2].A);
            // The glow is the hue, never the ink.
            Assert.All(stops, c => Assert.Equal((hue.R, hue.G, hue.B), (c.R, c.G, c.B)));
        }
        Assert.Equal(new[] { 0.0, 0.45, 1.0 }, SectionEdgeRules.GlowOffsets);
        Assert.Equal(28, SectionEdgeRules.GlowBand);
    }

    [Fact]
    public void LineIsTheHueAndTheLiftRidesItsOwnStripsExceptAtOff()
    {
        var hue = NavStripRules.Sky;
        var line = Color.FromArgb(0xE6, hue.R, hue.G, hue.B);
        foreach (var level in new[] { MotionLevel.Full, MotionLevel.Reduced, MotionLevel.Off })
            Assert.All(SectionEdgeRules.LineStops(hue, level), c => Assert.Equal(line, c));

        var lift = SectionEdgeRules.LiftStops(hue, MotionLevel.Full);
        Assert.Equal(NavStripRules.Mix(hue, Colors.White, 0.35), lift[1]);
        Assert.Equal(0xFF, lift[1].A);
        Assert.True(NavStripRules.Luminance(lift[1]) > NavStripRules.Luminance(hue));
        Assert.Equal(0, lift[0].A);
        Assert.Equal(0, lift[2].A);
        Assert.Equal(lift, SectionEdgeRules.LiftStops(hue, MotionLevel.Reduced));
        Assert.All(SectionEdgeRules.LiftStops(hue, MotionLevel.Off), c => Assert.Equal(0, c.A));
        Assert.Equal(new[] { 0.4, 0.5, 0.6 }, SectionEdgeRules.LiftOffsets);
    }

    [Fact]
    public void TheLiftLapsClockwiseOneSideAtATimeAndParksOffTheStrip()
    {
        Assert.Equal(SectionEdgeRules.SpinSeconds / 4, SectionEdgeRules.LiftLegSeconds);
        Assert.Equal(0, SectionEdgeRules.LiftLegStart(EdgeSide.Top));
        Assert.Equal(3, SectionEdgeRules.LiftLegStart(EdgeSide.Right));
        Assert.Equal(6, SectionEdgeRules.LiftLegStart(EdgeSide.Bottom));
        Assert.Equal(9, SectionEdgeRules.LiftLegStart(EdgeSide.Left));
        // Clockwise: left to right along the top, down the right, right to left along the
        // bottom, up the left. Every From and To is a full width off the strip.
        Assert.Equal((-1.0, 1.0), SectionEdgeRules.LiftTravel(EdgeSide.Top));
        Assert.Equal((-1.0, 1.0), SectionEdgeRules.LiftTravel(EdgeSide.Right));
        Assert.Equal((1.0, -1.0), SectionEdgeRules.LiftTravel(EdgeSide.Bottom));
        Assert.Equal((1.0, -1.0), SectionEdgeRules.LiftTravel(EdgeSide.Left));
        // Reduced holds one lift at the top centre and parks the rest; Spin parks all at From.
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
    [InlineData(false, true, false, false)]   // deactivated
    [InlineData(true, false, false, false)]   // hidden to the tray
    [InlineData(true, true, true, false)]     // minimised
    public void TheSpinRunsOnlyInAnActiveShownWindow(bool active, bool visible, bool minimized, bool runs)
    {
        Assert.Equal(runs, SectionEdgeRules.SpinShouldRun(SectionEdgeMotion.Spin, active, visible, minimized));
        Assert.False(SectionEdgeRules.SpinShouldRun(SectionEdgeMotion.Fixed, active, visible, minimized));
        Assert.False(SectionEdgeRules.SpinShouldRun(SectionEdgeMotion.Solid, active, visible, minimized));
    }

    [Fact]
    public void TheEdgeOnlyEverWearsTheSevenSectionHues()
    {
        // Gold (T1), cyan (T2), red (Circe) and mint (credit) stay reserved: the edge only ever
        // wears the seven section hues.
        var hues = NavSections.Order.Select(s => NavStripRules.Accent(s.Key)).Distinct().ToArray();
        Assert.Equal(7, hues.Length);
    }
}
