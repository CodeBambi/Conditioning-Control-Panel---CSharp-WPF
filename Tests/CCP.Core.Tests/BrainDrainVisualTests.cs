using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Compositor;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>PORTED from Tests/ConditioningControlPanel.Tests/BrainDrainKeepClearTests.cs and
/// BrainDrainBlurZeroTests.cs (the pure parts), plus the WPF BrainDrainLayer curve pins.</summary>
public sealed class BrainDrainVisualTests
{
    private static BrainDrainKeepClear.BandEntry Ours(int h) => new((IntPtr)h, true, false);
    private static BrainDrainKeepClear.BandEntry Drain(int h) => new((IntPtr)h, true, true);
    private static BrainDrainKeepClear.BandEntry Other(int h) => new((IntPtr)h, false, false);

    [Fact]
    public void A_drain_over_our_window_sinks_under_the_lowest_one_of_ours()
    {
        var band = new List<BrainDrainKeepClear.BandEntry> { Drain(1), Ours(2), Other(3), Ours(4), Other(5) };
        Assert.Equal((IntPtr)4, BrainDrainKeepClear.AnchorIfNeeded(band));
    }

    [Fact]
    public void A_drain_already_under_everything_of_ours_is_left_alone()
    {
        var band = new List<BrainDrainKeepClear.BandEntry> { Ours(2), Other(3), Ours(4), Drain(1) };
        Assert.Equal(IntPtr.Zero, BrainDrainKeepClear.AnchorIfNeeded(band));
    }

    [Fact]
    public void Nothing_of_ours_or_no_drain_means_nothing_to_sort()
    {
        Assert.Equal(IntPtr.Zero, BrainDrainKeepClear.AnchorIfNeeded(new List<BrainDrainKeepClear.BandEntry> { Drain(1), Other(3) }));
        Assert.Equal(IntPtr.Zero, BrainDrainKeepClear.AnchorIfNeeded(new List<BrainDrainKeepClear.BandEntry> { Ours(2), Other(3) }));
        Assert.Equal(IntPtr.Zero, BrainDrainKeepClear.AnchorIfNeeded(new List<BrainDrainKeepClear.BandEntry>()));
    }

    [Fact]
    public void Two_drains_sink_when_the_highest_one_is_over_ours()
    {
        var band = new List<BrainDrainKeepClear.BandEntry> { Drain(1), Ours(2), Drain(6) };
        Assert.Equal((IntPtr)2, BrainDrainKeepClear.AnchorIfNeeded(band));
    }

    [Fact]
    public void Zero_strength_means_no_picture_at_all()
    {
        Assert.True(BrainDrainVisualPolicy.IsSilent(0));
        Assert.False(BrainDrainVisualPolicy.IsSilent(1));
        Assert.False(BrainDrainVisualPolicy.WantsBlur(true, 0));
        Assert.False(BrainDrainVisualPolicy.WantsBlur(false, 50));
        Assert.True(BrainDrainVisualPolicy.WantsBlur(true, 1));
    }

    [Theory]
    [InlineData(1, 0.10)]
    [InlineData(25, 0.40)]
    [InlineData(50, 0.57)]
    [InlineData(75, 0.72)]
    [InlineData(100, 0.85)]
    [InlineData(200, 0.85)]
    public void The_draw_alpha_curve_is_the_WPF_one(int intensity, double expected)
    {
        Assert.Equal(expected, BrainDrainLayerRules.AlphaFor(intensity) / 255.0, 2);
    }

    [Fact]
    public void Sigma_and_melt_amplitude_are_the_WPF_numbers()
    {
        // intensity 100 at downscale 4: radius 3.5 source px, sigma radius / 3.
        Assert.Equal(100 * 0.14 / 4 / 3, BrainDrainLayerRules.SigmaFor(100), 4);
        Assert.Equal(0f, BrainDrainLayerRules.SigmaFor(0));
        Assert.Equal(2.35f, BrainDrainLayerRules.MeltAmplitudeFor(30), 3);
        Assert.Equal(5.5f, BrainDrainLayerRules.MeltAmplitudeFor(100), 3);
        Assert.Equal(10f, BrainDrainLayerRules.MeltAmplitudeFor(900), 3);   // clamped at the legacy 200 ceiling
        Assert.Equal(BrainDrainLayerRules.MeltAmplitudeFor(100) / 2, BrainDrainLayerRules.MeltAmplitudeFor(100, 8), 3);
    }

    [Fact]
    public void Capture_size_is_a_quarter_even_and_never_under_two()
    {
        Assert.Equal((480, 270), BrainDrainLayerRules.CaptureSize(1920, 1080));
        Assert.Equal((640, 360), BrainDrainLayerRules.CaptureSize(2561, 1441));
        Assert.Equal((2, 2), BrainDrainLayerRules.CaptureSize(3, 3));
        Assert.Equal(30, BrainDrainLayerRules.Fps(false));
        Assert.Equal(60, BrainDrainLayerRules.Fps(true));
    }
}
