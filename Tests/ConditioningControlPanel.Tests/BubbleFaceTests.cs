using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class BubbleFaceTests
{
    [Fact]
    public void PictureLeavesVisibleGlassAroundItsEdge()
    {
        Assert.Equal(120, BubbleFace.Diameter(200, false));
        Assert.Equal(172, BubbleFace.Diameter(200, true));
    }

    [Fact]
    public void AnimatedFaceAdvancesAndLoopsOnTheBubbleClock()
    {
        Assert.Equal(0, BubbleFace.FrameAt(0, 100, 3, MotionLevel.Full));
        Assert.Equal(1, BubbleFace.FrameAt(100, 100, 3, MotionLevel.Full));
        Assert.Equal(2, BubbleFace.FrameAt(299, 100, 3, MotionLevel.Full));
        Assert.Equal(0, BubbleFace.FrameAt(300, 100, 3, MotionLevel.Full));
        Assert.Equal(0, BubbleFace.FrameAt(200, 100, 3, MotionLevel.Off));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public void PictureBubbleUsesConfiguredBurstRatherThanStrength(int count)
    {
        var settings = new AppSettings { SimultaneousImages = count, ImageScale = 150, FlashDuration = 7 };
        var payload = new FlashPayload { Strength = 0, UseFlashSettings = true };
        var burst = payload.ResolveBurst(settings);
        Assert.Equal(count, burst.Amount);
        Assert.Equal(150, burst.Size);
        Assert.Equal(7000, burst.DurationMs);
    }

    [Fact]
    public void AmbientFallbackWithoutAReadyPictureAlsoUsesSettings()
    {
        var payload = new FlashPayload { Ambient = true, Strength = 0 };
        Assert.Equal(12, payload.ResolveBurst(new AppSettings { SimultaneousImages = 12 }).Amount);
    }
}
