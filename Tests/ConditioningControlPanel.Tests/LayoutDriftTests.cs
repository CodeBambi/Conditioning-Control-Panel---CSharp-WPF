using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Owner report on 6.11.5 (2026-09-29): after the panel moved between a 100% and a 125% monitor,
/// its window frame was 1825x1161 px while WPF kept the root laid out smaller, and the uncovered
/// strip showed as bare black. <see cref="LayoutDrift"/> decides when the panel forces a re-layout.
/// </summary>
public class LayoutDriftTests
{
    [Fact]
    public void The_reported_window_is_drift()
    {
        // 1825x1161 px frame at 125%, root laid out 1360x882 DIP (1700x1102 px).
        Assert.True(LayoutDrift.Drifted(1825, 1161, 1360, 882, 1.25, 1.25));
    }

    [Fact]
    public void A_layout_that_covers_the_window_is_not_drift()
    {
        Assert.False(LayoutDrift.Drifted(1825, 1161, 1460, 928.8, 1.25, 1.25));
        Assert.False(LayoutDrift.Drifted(1563, 943, 1563, 943, 1.0, 1.0));
    }

    [Fact]
    public void The_bigger_default_window_is_not_drift()
    {
        // Nav polish wave 7 default (1954x1179 DIP): laid out in full at 100% and at 125%.
        Assert.False(LayoutDrift.Drifted(1954, 1179, 1954, 1179, 1.0, 1.0));
        Assert.False(LayoutDrift.Drifted(2443, 1474, 1954, 1179, 1.25, 1.25));
        // ...and a frame that outgrew it by a 125% seam is.
        Assert.True(LayoutDrift.Drifted(2443, 1474, 1563, 943, 1.25, 1.25));
    }

    [Fact]
    public void Rounding_within_the_tolerance_is_not_drift()
    {
        Assert.False(LayoutDrift.Drifted(1000, 700, 1000 + LayoutDrift.TolerancePx - 0.5, 700, 1.0, 1.0));
        Assert.True(LayoutDrift.Drifted(1000, 700, 1000 + LayoutDrift.TolerancePx + 1, 700, 1.0, 1.0));
    }

    [Fact]
    public void Only_one_axis_off_is_still_drift()
    {
        Assert.True(LayoutDrift.Drifted(1600, 900, 1600, 850, 1.0, 1.0));
    }

    [Theory]
    [InlineData(0, 900, 1600, 900, 1.0)]
    [InlineData(1600, 900, 0, 900, 1.0)]
    [InlineData(1600, 900, double.NaN, 900, 1.0)]
    [InlineData(1600, 900, double.PositiveInfinity, 900, 1.0)]
    [InlineData(1600, 900, 1600, 900, 0.0)]
    public void Unknown_sizes_never_count(int cw, int ch, double lw, double lh, double scale)
    {
        Assert.False(LayoutDrift.Drifted(cw, ch, lw, lh, scale, scale));
    }
}
