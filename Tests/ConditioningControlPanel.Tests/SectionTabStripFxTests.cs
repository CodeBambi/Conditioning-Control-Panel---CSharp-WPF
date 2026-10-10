using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 7, FX lane: the pills' motion (SectionTabStrip.Fx.cs). Hover lifts and grows
/// a pill and settles back on leave, Off adds nothing, choosing asks the spark overlay for a
/// burst, the overlay is the track's last child and never takes a click, and the idle sheen
/// clock only runs on Full with ambient motion allowed.
/// </summary>
public class SectionTabStripFxTests
{
    private const string Section = "companion";
    private const string Active = "companion";      // Chat
    private const string Other = "personality";

    private static SectionTabStrip Laid(MotionLevel level, double width = 1480)
    {
        var strip = new SectionTabStrip { MotionOverride = level, Width = width };
        strip.Show(Section, Active);
        strip.Measure(new Size(width, 200));
        strip.Arrange(new Rect(0, 0, width, strip.DesiredSize.Height));
        strip.UpdateLayout();
        return strip;
    }

    private static (ScaleTransform Hover, TranslateTransform Lift) HoverParts(FrameworkElement ring)
    {
        var group = Assert.IsType<TransformGroup>(ring.RenderTransform);
        var scales = group.Children.OfType<ScaleTransform>().ToList();
        Assert.True(scales.Count >= 2, "the squish keeps its own scale slot ahead of the hover scale");
        return (scales.Last(), group.Children.OfType<TranslateTransform>().Last());
    }

    [Fact]
    public void HoverLiftsAndGrowsAPillAndLeaveSettlesIt()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = Laid(MotionLevel.Full);
            var ring = SectionTabStrip.RingOf(strip.PillFor(Other));
            Assert.NotNull(ring);

            strip.FxHoverForTests(Other, true);
            var (hover, lift) = HoverParts(ring!);
            Assert.Equal(-NavStripFxRules.HoverLiftPx, lift.Y, 3);
            Assert.Equal(NavStripFxRules.HoverScale, hover.ScaleX, 3);
            Assert.Equal(NavStripFxRules.HoverScale, hover.ScaleY, 3);

            strip.FxHoverForTests(Other, false);
            Assert.True(ring!.RenderTransform.Value.IsIdentity, "leave settles the pill back to rest");
        });
    }

    [Fact]
    public void ThePressSquishKeepsTheFirstScaleSlot()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = Laid(MotionLevel.Full);
            var ring = SectionTabStrip.RingOf(strip.PillFor(Other))!;
            strip.FxHoverForTests(Other, true);
            var group = Assert.IsType<TransformGroup>(ring.RenderTransform);
            var first = Assert.IsType<ScaleTransform>(group.Children[0]);
            Assert.NotSame(HoverParts(ring).Hover, first);
        });
    }

    [Fact]
    public void ReducedLiftsOnlyAndOffAddsNoTransform()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var reduced = Laid(MotionLevel.Reduced);
            reduced.FxHoverForTests(Other, true);
            var (hover, lift) = HoverParts(SectionTabStrip.RingOf(reduced.PillFor(Other))!);
            Assert.Equal(-NavStripFxRules.HoverLiftPx, lift.Y, 3);
            Assert.Equal(1.0, hover.ScaleX, 3);

            var off = Laid(MotionLevel.Off);
            var ring = SectionTabStrip.RingOf(off.PillFor(Other))!;
            off.FxHoverForTests(Other, true);
            Assert.IsNotType<TransformGroup>(ring.RenderTransform);
            Assert.True(ring.RenderTransform == null || ring.RenderTransform.Value.IsIdentity);
        });
    }

    [Fact]
    public void TheActivePillDoesNotLiftOffItsFill()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = Laid(MotionLevel.Full);
            var ring = SectionTabStrip.RingOf(strip.PillFor(Active))!;
            strip.FxHoverForTests(Active, true);
            Assert.True(ring.RenderTransform.Value.IsIdentity);
        });
    }

    [Fact]
    public void TheOverlayIsTheTracksLastChildAndNeverTakesAClick()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = Laid(MotionLevel.Full);
            var layer = strip.FxLayer;
            Assert.NotNull(layer);
            var track = Assert.IsType<Grid>(layer!.Parent);
            Assert.Same(layer, track.Children[track.Children.Count - 1]);
            Assert.False(layer.IsHitTestVisible);
            Assert.NotNull(strip.FxSparks);
            Assert.False(strip.FxSparks!.IsHitTestVisible);
        });
    }

    [Fact]
    public void ChoosingAPillAsksTheOverlayForSparksUnlessMotionIsOff()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var full = Laid(MotionLevel.Full);
            full.FxChooseForTests(Other);
            Assert.Equal(1, full.BurstsFired);

            var off = Laid(MotionLevel.Off);
            off.FxChooseForTests(Other);
            Assert.Equal(0, off.BurstsFired);
            Assert.Equal(0, off.GlowsFired);
        });
    }

    [Fact]
    public void TheSheenClockRunsOnlyWhenAmbientLoopsAreAllowed()
    {
        Assert.True(NavStripFxRules.SheenRuns(MotionLevel.Full, true, true, true));
        Assert.False(NavStripFxRules.SheenRuns(MotionLevel.Reduced, true, true, true));
        Assert.False(NavStripFxRules.SheenRuns(MotionLevel.Off, true, true, true));
        Assert.False(NavStripFxRules.SheenRuns(MotionLevel.Full, false, true, true));
        Assert.False(NavStripFxRules.SheenRuns(MotionLevel.Full, true, false, true));
        Assert.False(NavStripFxRules.SheenRuns(MotionLevel.Full, true, true, false));

        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = Laid(MotionLevel.Off);
            strip.VisibleOverride = true;
            strip.AmbientTierOverride = true;
            strip.UpdateSheenClock();
            Assert.False(strip.SheenClockRunning);

            strip.MotionOverride = MotionLevel.Full;
            strip.AmbientTierOverride = false;
            strip.UpdateSheenClock();
            Assert.False(strip.SheenClockRunning, "the Performance tier never runs the sheen");

            strip.AmbientTierOverride = true;
            strip.UpdateSheenClock();
            Assert.True(strip.SheenClockRunning);

            strip.VisibleOverride = false;
            strip.UpdateSheenClock();
            Assert.False(strip.SheenClockRunning, "off screen parks the clock");
        });
    }

    [Fact]
    public void ASheenSweepsTheActivePill()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = Laid(MotionLevel.Full);
            Assert.True(strip.SweepSheen());
            Assert.Equal(1, strip.SheensRun);
        });
    }
}
