using ConditioningControlPanel.Core.Platform;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Tests for the XRectangle clamp math (linux-overlay-contract.md §3.3): XRectangle fields
/// are short (x, y) and ushort (width, height); capture rects must be clamped to that
/// domain before narrowing, clipping — never wrapping — rects that extend past ±32767.
/// </summary>
public class X11InputShapeMathTests
{
    [Fact]
    public void ClampRect_InRangeRect_Unchanged()
    {
        var (x, y, w, h) = X11InputShapeMath.ClampRect(100, 200, 300, 400);

        Assert.Equal(100, x);
        Assert.Equal(200, y);
        Assert.Equal(300, w);
        Assert.Equal(400, h);
    }

    [Fact]
    public void ClampRect_RoundsFractionalCoordinates()
    {
        var (x, y, w, h) = X11InputShapeMath.ClampRect(10.6, 20.4, 100.5, 50.5);

        Assert.Equal(11, x);
        Assert.Equal(20, y);
        // Width/height derive from rounded edges: round(10.6+100.5)=111, 111-11=100.
        Assert.Equal(100, w);
        Assert.Equal(71 - 20, h); // round(20.4+50.5)=71
    }

    [Fact]
    public void ClampRect_RectExtendingPastMaxCoordinate_IsClippedNotWrapped()
    {
        // Starts in range, extends past +32767 (exotic multi-monitor virtual desktop).
        var (x, y, w, h) = X11InputShapeMath.ClampRect(32000, 0, 5000, 100);

        Assert.Equal(32000, x);
        Assert.Equal(0, y);
        Assert.Equal(X11InputShapeMath.MaxCoordinate - 32000, w); // clipped at 32767
        Assert.Equal(100, h);
    }

    [Fact]
    public void ClampRect_NegativeOriginBelowMin_IsClipped()
    {
        var (x, y, w, h) = X11InputShapeMath.ClampRect(-40000, -40000, 80000, 80000);

        Assert.Equal(X11InputShapeMath.MinCoordinate, x);
        Assert.Equal(X11InputShapeMath.MinCoordinate, y);
        // Both edges clamp: width = 32767 - (-32768) = 65535 = MaxExtent.
        Assert.Equal(X11InputShapeMath.MaxExtent, w);
        Assert.Equal(X11InputShapeMath.MaxExtent, h);
    }

    [Fact]
    public void ClampRect_EntirelyOutOfRange_CollapsesToZeroArea()
    {
        // Fully beyond +32767: both edges clamp to MaxCoordinate → zero width.
        var (_, _, w, h) = X11InputShapeMath.ClampRect(40000, 40000, 100, 100);

        Assert.Equal(0, w);
        Assert.Equal(0, h);
    }

    [Theory]
    [InlineData(double.NaN, 0, 10, 10)]
    [InlineData(0, double.PositiveInfinity, 10, 10)]
    [InlineData(0, 0, double.NaN, 10)]
    [InlineData(0, 0, 10, double.NegativeInfinity)]
    public void ClampRect_NonFiniteInput_CollapsesToZeroArea(double x, double y, double w, double h)
    {
        var result = X11InputShapeMath.ClampRect(x, y, w, h);

        Assert.Equal((0, 0, 0, 0), result);
    }

    [Theory]
    [InlineData(0, 0, 0, 100)]
    [InlineData(0, 0, 100, 0)]
    [InlineData(0, 0, -50, 100)]
    [InlineData(0, 0, 100, -50)]
    public void ClampRect_NonPositiveSize_CollapsesToZeroArea(double x, double y, double w, double h)
    {
        var result = X11InputShapeMath.ClampRect(x, y, w, h);

        Assert.Equal(0, result.Width);
        Assert.Equal(0, result.Height);
    }

    [Fact]
    public void ClampRect_HugeExtentWithinCoordinateRange_CappedAtMaxExtent()
    {
        // left clamps to -32768, right to 32767 → width capped exactly at ushort.MaxValue.
        var (x, _, w, _) = X11InputShapeMath.ClampRect(-100000, 0, 300000, 10);

        Assert.Equal(X11InputShapeMath.MinCoordinate, x);
        Assert.Equal(X11InputShapeMath.MaxExtent, w);
    }
}
