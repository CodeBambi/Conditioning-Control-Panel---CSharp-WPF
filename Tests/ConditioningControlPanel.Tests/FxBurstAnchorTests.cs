using System.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// PR-5 event moments — the pure half of the burst plumbing. The WPF half (mapping an anchor with
/// TransformToVisual so the Viewbox scale is carried) can only be judged by eye in a live app;
/// what CAN go wrong silently is the decision made on the resulting rectangle: firing a burst for
/// an anchor that is scrolled out of the viewport (particles nobody sees, on a clock nobody asked
/// for) or emitting outside the canvas, where the sim runs and draws nothing at all.
/// </summary>
public class FxBurstAnchorTests
{
    private static readonly Size Layer = new(1600, 900);

    [Fact]
    public void Center_IsTheMiddleOfTheAnchor()
    {
        Assert.True(FxBurstAnchor.TryResolve(new Rect(100, 200, 60, 40), Layer,
                                             FxBurstSpot.Center, out var p));
        Assert.Equal(130, p.X, 3);
        Assert.Equal(220, p.Y, 3);
    }

    [Fact]
    public void RightEdge_IsTheBarsCap()
    {
        // The level-up and quest-complete bursts both ride this: the cap of a progress bar is the
        // middle of its right edge, not its centre.
        Assert.True(FxBurstAnchor.TryResolve(new Rect(40, 300, 500, 8), Layer,
                                             FxBurstSpot.RightEdge, out var p));
        Assert.Equal(540, p.X, 3);
        Assert.Equal(304, p.Y, 3);
    }

    [Theory]
    [InlineData(FxBurstSpot.LeftEdge, 40, 304)]
    [InlineData(FxBurstSpot.TopCenter, 290, 300)]
    [InlineData(FxBurstSpot.BottomCenter, 290, 308)]
    public void EverySpot_LandsOnTheEdgeItNames(FxBurstSpot spot, double x, double y)
    {
        Assert.True(FxBurstAnchor.TryResolve(new Rect(40, 300, 500, 8), Layer, spot, out var p));
        Assert.Equal(x, p.X, 3);
        Assert.Equal(y, p.Y, 3);
    }

    [Fact]
    public void UnmeasuredAnchor_FiresNothing()
    {
        // A collapsed tab's controls have zero size: there is nothing on screen to burst from.
        Assert.False(FxBurstAnchor.TryResolve(new Rect(10, 10, 0, 0), Layer,
                                              FxBurstSpot.Center, out _));
        Assert.False(FxBurstAnchor.TryResolve(new Rect(10, 10, 120, 0), Layer,
                                              FxBurstSpot.Center, out _));
        Assert.False(FxBurstAnchor.TryResolve(Rect.Empty, Layer, FxBurstSpot.Center, out _));
    }

    [Fact]
    public void UnmeasuredLayer_FiresNothing()
    {
        // The burst host before its first arrange. Emitting here would start a clock that paints
        // into a zero-pixel surface.
        Assert.False(FxBurstAnchor.TryResolve(new Rect(10, 10, 40, 40), new Size(0, 0),
                                              FxBurstSpot.Center, out _));
        Assert.False(FxBurstAnchor.TryResolve(new Rect(10, 10, 40, 40),
                                              new Size(double.NaN, double.NaN),
                                              FxBurstSpot.Center, out _));
    }

    [Theory]
    // Scrolled off the top / left of the viewport, and past the right / bottom of it.
    [InlineData(200, -60, 300, 40)]
    [InlineData(-400, 100, 300, 40)]
    [InlineData(1700, 100, 300, 40)]
    [InlineData(200, 1000, 300, 40)]
    public void OffScreenAnchor_FiresNothing(double x, double y, double w, double h)
        => Assert.False(FxBurstAnchor.TryResolve(new Rect(x, y, w, h), Layer,
                                                 FxBurstSpot.Center, out _));

    [Fact]
    public void PartiallyClippedAnchor_StillBursts_ClampedIntoTheLayer()
    {
        // A quest bar half-scrolled off the bottom of its list is still a celebration worth
        // showing - but the origin has to come back inside the surface or nothing is drawn.
        Assert.True(FxBurstAnchor.TryResolve(new Rect(1500, 860, 400, 200), Layer,
                                             FxBurstSpot.RightEdge, out var p));
        Assert.InRange(p.X, 0, Layer.Width);
        Assert.InRange(p.Y, 0, Layer.Height);
        Assert.Equal(Layer.Width, p.X, 3);
    }

    [Fact]
    public void GarbageBounds_FireNothing()
    {
        // TransformToVisual through a degenerate transform can hand back NaN/Infinity rather
        // than throwing; a NaN origin would put every spark nowhere.
        Assert.False(FxBurstAnchor.TryResolve(new Rect(double.NaN, 10, 40, 40), Layer,
                                              FxBurstSpot.Center, out _));
        Assert.False(FxBurstAnchor.TryResolve(new Rect(10, 10, double.PositiveInfinity, 40), Layer,
                                              FxBurstSpot.Center, out _));
    }
}
