using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1194 - the flash GIF speed slider. The multiplier scales the per-frame delay the
/// flash heartbeat divides by, so the two things worth pinning are the direction of the maths
/// (faster = SHORTER delay) and the floor that stops a 4x on an already-fast GIF from asking for
/// a new frame on every single heartbeat tick.
/// </summary>
public class FlashGifSpeedTests
{
    [Fact]
    public void DefaultMultiplier_LeavesTheFileTimingAlone()
        => Assert.Equal(100, FlashService.ScaleFrameDelay(TimeSpan.FromMilliseconds(100), 1.0).TotalMilliseconds, 3);

    [Theory]
    [InlineData(2.0, 50)]      // twice as fast -> half the delay
    [InlineData(4.0, 25)]      // ceiling
    [InlineData(0.5, 200)]     // half speed -> twice the delay
    [InlineData(0.25, 400)]    // floor of the RANGE (not of the delay)
    public void MultiplierDividesTheDelay(double multiplier, double expectedMs)
        => Assert.Equal(expectedMs, FlashService.ScaleFrameDelay(TimeSpan.FromMilliseconds(100), multiplier).TotalMilliseconds, 3);

    [Theory]
    [InlineData(8.0)]          // above the 4x ceiling
    [InlineData(-1.0)]         // nonsense
    [InlineData(0.01)]         // below the 0.25x floor
    public void MultiplierIsClampedBeforeItDivides(double multiplier)
    {
        var d = FlashService.ScaleFrameDelay(TimeSpan.FromMilliseconds(100), multiplier).TotalMilliseconds;
        Assert.InRange(d, 25, 400);
    }

    [Fact]
    public void FastGifAtMaxSpeed_StopsAtTheFloor()
    {
        // A 20ms/frame GIF at 4x wants 5ms, which is under one heartbeat tick on every live window.
        var d = FlashService.ScaleFrameDelay(TimeSpan.FromMilliseconds(20), 4.0);
        Assert.Equal(FlashService.MIN_GIF_FRAME_DELAY_MS, d.TotalMilliseconds, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-40)]
    public void NonPositiveSourceDelay_FallsBackToTheDecoderDefault(double sourceMs)
    {
        // Never zero: the heartbeat divides by this value to pick a frame index.
        var d = FlashService.ScaleFrameDelay(TimeSpan.FromMilliseconds(sourceMs), 1.0);
        Assert.Equal(100, d.TotalMilliseconds, 3);
    }

    [Fact]
    public void ScaledDelayIsNeverBelowTheFloor()
    {
        foreach (var src in new[] { 1.0, 5.0, 10.0, 16.0, 33.0, 100.0, 1000.0 })
            foreach (var mult in new[] { 0.25, 0.5, 1.0, 2.0, 4.0 })
                Assert.True(FlashService.ScaleFrameDelay(TimeSpan.FromMilliseconds(src), mult).TotalMilliseconds
                            >= FlashService.MIN_GIF_FRAME_DELAY_MS);
    }

    [Fact]
    public void SettingClampsToTheSliderRange()
    {
        var s = new AppSettings();
        Assert.Equal(1.0, s.FlashGifSpeedMultiplier);

        s.FlashGifSpeedMultiplier = 9.0;
        Assert.Equal(4.0, s.FlashGifSpeedMultiplier);

        s.FlashGifSpeedMultiplier = 0.0;
        Assert.Equal(0.25, s.FlashGifSpeedMultiplier);
    }
}
