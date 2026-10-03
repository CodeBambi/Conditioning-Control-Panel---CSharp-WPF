using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #722: the spiral slider used to paint at a flat tenth, so 100% was barely visible.
/// The bottom half of the slider keeps its old look; 100% now paints at 0.6.
/// </summary>
public class SpiralPaintTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    public void At_or_below_half_is_unchanged(double f)
        => Assert.Equal(f * 0.1, OverlayService.SpiralPaint(f), 10);

    [Fact]
    public void Full_slider_paints_at_sixty_percent()
        => Assert.Equal(0.6, OverlayService.SpiralPaint(1.0), 10);

    [Fact]
    public void Climbs_monotonically_with_no_jump_at_half()
    {
        double prev = -1;
        for (int i = 0; i <= 100; i++)
        {
            var v = OverlayService.SpiralPaint(i / 100.0);
            Assert.True(v >= prev, $"dropped at {i}%");
            if (prev >= 0) Assert.True(v - prev < 0.0111 + 1e-9, $"jumped at {i}%");
            prev = v;
        }
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(2.0, 0.6)]
    [InlineData(double.NaN, 0.0)]
    public void Out_of_range_input_is_clamped(double f, double expected)
        => Assert.Equal(expected, OverlayService.SpiralPaint(f), 10);
}
