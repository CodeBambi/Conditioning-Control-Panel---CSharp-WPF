using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Unit tests for input capture region math (bounding box union, overlap detection).
/// These operations are used by the Linux overlay backends to compute the XFixes/Wayland
/// input regions from the compositor's layer capture mask.
/// </summary>
public class InputCaptureRegionMathTests
{
    [Fact]
    public void UnionBoundingBox_SingleRegion_ReturnsSameRegion()
    {
        var regions = new List<PixelRect>
        {
            new(10, 20, 100, 50)
        };

        var union = ComputeUnionBoundingBox(regions);

        Assert.Equal(10, union.X);
        Assert.Equal(20, union.Y);
        Assert.Equal(100, union.Width);
        Assert.Equal(50, union.Height);
    }

    [Fact]
    public void UnionBoundingBox_TwoDisjointRegions_ReturnsBoundingBox()
    {
        var regions = new List<PixelRect>
        {
            new(0, 0, 100, 100),
            new(200, 200, 100, 100)
        };

        var union = ComputeUnionBoundingBox(regions);

        Assert.Equal(0, union.X);
        Assert.Equal(0, union.Y);
        Assert.Equal(300, union.Width);
        Assert.Equal(300, union.Height);
    }

    [Fact]
    public void UnionBoundingBox_OverlappingRegions_ReturnsBoundingBox()
    {
        var regions = new List<PixelRect>
        {
            new(0, 0, 150, 150),
            new(50, 50, 150, 150)
        };

        var union = ComputeUnionBoundingBox(regions);

        Assert.Equal(0, union.X);
        Assert.Equal(0, union.Y);
        Assert.Equal(200, union.Width);
        Assert.Equal(200, union.Height);
    }

    [Fact]
    public void UnionBoundingBox_EmptyList_ReturnsZero()
    {
        var regions = new List<PixelRect>();

        var union = ComputeUnionBoundingBox(regions);

        Assert.Equal(0, union.X);
        Assert.Equal(0, union.Y);
        Assert.Equal(0, union.Width);
        Assert.Equal(0, union.Height);
    }

    [Fact]
    public void UnionBoundingBox_MixedPositions_HandlesNegativeCoords()
    {
        var regions = new List<PixelRect>
        {
            new(-50, -50, 100, 100),
            new(50, 50, 100, 100)
        };

        var union = ComputeUnionBoundingBox(regions);

        Assert.Equal(-50, union.X);
        Assert.Equal(-50, union.Y);
        Assert.Equal(200, union.Width);
        Assert.Equal(200, union.Height);
    }

    [Fact]
    public void RegionsOverlap_DisjointRegions_ReturnsFalse()
    {
        var a = new PixelRect(0, 0, 100, 100);
        var b = new PixelRect(200, 200, 100, 100);

        Assert.False(RegionsOverlap(a, b));
    }

    [Fact]
    public void RegionsOverlap_OverlappingRegions_ReturnsTrue()
    {
        var a = new PixelRect(0, 0, 100, 100);
        var b = new PixelRect(50, 50, 100, 100);

        Assert.True(RegionsOverlap(a, b));
    }

    [Fact]
    public void RegionsOverlap_TouchingEdges_ReturnsFalse()
    {
        var a = new PixelRect(0, 0, 100, 100);
        var b = new PixelRect(100, 0, 100, 100); // Touching at X=100

        Assert.False(RegionsOverlap(a, b));
    }

    [Fact]
    public void RegionsOverlap_ContainedRegion_ReturnsTrue()
    {
        var outer = new PixelRect(0, 0, 200, 200);
        var inner = new PixelRect(50, 50, 50, 50);

        Assert.True(RegionsOverlap(outer, inner));
    }

    [Fact]
    public void PointInRegion_Inside_ReturnsTrue()
    {
        var region = new PixelRect(100, 100, 200, 150);

        Assert.True(PointInRegion(150, 150, region));
        Assert.True(PointInRegion(100, 100, region)); // Top-left corner
        Assert.True(PointInRegion(299, 249, region)); // Just inside bottom-right
    }

    [Fact]
    public void PointInRegion_Outside_ReturnsFalse()
    {
        var region = new PixelRect(100, 100, 200, 150);

        Assert.False(PointInRegion(50, 50, region));
        Assert.False(PointInRegion(300, 250, region)); // On edge (exclusive)
        Assert.False(PointInRegion(100, 99, region)); // Just above
    }

    [Fact]
    public void PointInAnyRegion_MultipleRegions_FindsCorrectRegion()
    {
        var regions = new List<PixelRect>
        {
            new(0, 0, 100, 100),
            new(200, 0, 100, 100),
            new(0, 200, 100, 100)
        };

        Assert.True(PointInAnyRegion(50, 50, regions));   // In first region
        Assert.True(PointInAnyRegion(250, 50, regions));  // In second region
        Assert.True(PointInAnyRegion(50, 250, regions));  // In third region
        Assert.False(PointInAnyRegion(150, 150, regions)); // In the gap
    }

    // --- Helper methods (would normally be in CCP.Core/Platform) ---

    private static PixelRect ComputeUnionBoundingBox(IReadOnlyList<PixelRect> regions)
    {
        if (regions.Count == 0)
            return new PixelRect(0, 0, 0, 0);

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var r in regions)
        {
            if (r.X < minX) minX = r.X;
            if (r.Y < minY) minY = r.Y;
            if (r.X + r.Width > maxX) maxX = r.X + r.Width;
            if (r.Y + r.Height > maxY) maxY = r.Y + r.Height;
        }

        return new PixelRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static bool RegionsOverlap(PixelRect a, PixelRect b)
    {
        // No overlap if one rect is entirely to the left, right, above, or below the other
        if (a.X + a.Width <= b.X) return false;
        if (b.X + b.Width <= a.X) return false;
        if (a.Y + a.Height <= b.Y) return false;
        if (b.Y + b.Height <= a.Y) return false;
        return true;
    }

    private static bool PointInRegion(double x, double y, PixelRect region)
    {
        return x >= region.X && x < region.X + region.Width &&
               y >= region.Y && y < region.Y + region.Height;
    }

    private static bool PointInAnyRegion(double x, double y, IReadOnlyList<PixelRect> regions)
    {
        foreach (var r in regions)
        {
            if (PointInRegion(x, y, r))
                return true;
        }
        return false;
    }
}
