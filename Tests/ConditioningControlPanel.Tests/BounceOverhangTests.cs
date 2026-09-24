using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Bouncing text walls are inset by how far the effect transform reaches past the measured box,
/// so the drawn line stays on screen (ticket 2026-09-24).
/// </summary>
public class BounceOverhangTests
{
    [Fact]
    public void NoEffect_NoOverhang()
    {
        var (x, y) = BounceOverhang.Of(400, 90, 1, 1, 0);
        Assert.Equal(0, x, 6);
        Assert.Equal(0, y, 6);
    }

    [Fact]
    public void Breathing_GrowsBothSidesByHalfTheGrowth()
    {
        var (x, y) = BounceOverhang.Of(400, 100, 1.08, 1.08, 0);
        Assert.Equal(16, x, 6);
        Assert.Equal(4, y, 6);
    }

    [Fact]
    public void Tilt_MakesALongLineTaller()
    {
        var (_, y) = BounceOverhang.Of(600, 90, 1, 1, 12);
        Assert.True(y > 50, $"a 12 degree tilt of a 600 wide line reaches only {y} past the box");
    }

    [Fact]
    public void Squash_NeverGivesANegativeOverhang()
    {
        var (x, _) = BounceOverhang.Of(400, 90, 0.7, 1.2, 0);
        Assert.Equal(0, x, 6);
    }

    [Fact]
    public void Capped_LeavesTheTextARangeToTravel()
    {
        // Screen 1000 tall, spun line 600: the overhang of 300 would pin it, cap is 200.
        Assert.Equal(200, BounceOverhang.Capped(300, 1000, 600), 6);
        Assert.Equal(0, BounceOverhang.Capped(50, 500, 800), 6);
    }
}
