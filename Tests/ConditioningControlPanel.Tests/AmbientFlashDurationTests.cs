using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #593 — a popped dashboard "Trigger Bubble" flash lingered ~3-4s (the chaos Strength × LINGER band)
/// instead of honoring the user's global Flash Duration. Ambient flashes now derive their duration
/// from the Flash Duration setting (seconds, clamped 1..30) → milliseconds.
/// </summary>
public class AmbientFlashDurationTests
{
    [Fact]
    public void DefaultFiveSeconds_IsFiveThousandMs()
        => Assert.Equal(5000, FlashPayload.AmbientFlashDurationMs(5));

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(30, 30000)]
    [InlineData(12, 12000)]
    public void SecondsConvertedToMs(int seconds, int expectedMs)
        => Assert.Equal(expectedMs, FlashPayload.AmbientFlashDurationMs(seconds));

    [Theory]
    [InlineData(0, 1000)]     // below the 1s floor -> 1s
    [InlineData(-5, 1000)]    // negative -> 1s
    [InlineData(45, 30000)]   // above the 30s ceiling -> 30s
    public void OutOfRangeSeconds_AreClamped(int seconds, int expectedMs)
        => Assert.Equal(expectedMs, FlashPayload.AmbientFlashDurationMs(seconds));

    [Fact]
    public void AmbientDuration_TracksTheClampedSetting()
    {
        // Whatever the user picks (after the setting's own 1..30 clamp), the ambient flash matches it —
        // never the old fixed ~4s burst.
        var settings = new AppSettings { FlashDuration = 8 };
        Assert.Equal(8000, FlashPayload.AmbientFlashDurationMs(settings.FlashDuration));

        settings.FlashDuration = 100;                 // setting clamps to 30
        Assert.Equal(30, settings.FlashDuration);
        Assert.Equal(30000, FlashPayload.AmbientFlashDurationMs(settings.FlashDuration));
    }
}
