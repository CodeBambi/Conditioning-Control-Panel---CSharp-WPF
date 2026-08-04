using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #601 — flash count capped at 10 regardless of the SimultaneousImages slider. The cap must be
/// mode-aware: only the classic layered-window path keeps the memory-driven cap of 10; the cheap
/// compositor-layer and solid-host paths get the higher cap so the slider (max 20) is honored.
/// </summary>
public class FlashCapTests
{
    [Fact]
    public void CompositorLayer_UsesHostCap()
        => Assert.Equal(30, FlashService.ResolveFlashCap(useLayer: true, useHost: false));

    [Fact]
    public void SolidHost_UsesHostCap()
        => Assert.Equal(30, FlashService.ResolveFlashCap(useLayer: false, useHost: true));

    [Fact]
    public void LayerTakesPrecedenceOverHost_StillHostCap()
        => Assert.Equal(30, FlashService.ResolveFlashCap(useLayer: true, useHost: true));

    [Fact]
    public void ClassicLayeredWindow_KeepsLowCap()
        => Assert.Equal(10, FlashService.ResolveFlashCap(useLayer: false, useHost: false));

    [Fact]
    public void HostCap_ClearsTheSliderMaximum()
    {
        // The regression: a user setting SimultaneousImages to 11-20 only ever saw 10.
        var settings = new AppSettings { SimultaneousImages = 20 };
        Assert.Equal(20, settings.SimultaneousImages); // slider clamp allows up to 20
        int hostCap = FlashService.ResolveFlashCap(useLayer: true, useHost: false);
        Assert.True(hostCap >= settings.SimultaneousImages,
            $"host cap {hostCap} must not silently cap the slider max {settings.SimultaneousImages}");
    }

    [Theory]
    [InlineData(0, 1)]    // clamps up to the 1 floor
    [InlineData(25, 20)]  // clamps down to the 20 ceiling
    [InlineData(11, 11)]  // the previously-broken band 11-20 is preserved
    public void SimultaneousImages_ClampedTo_1_20(int input, int expected)
    {
        var settings = new AppSettings { SimultaneousImages = input };
        Assert.Equal(expected, settings.SimultaneousImages);
    }
}
