using System;
using System.Linq;
using Avalonia;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Flash placement on the Avalonia head: screens + settings -> the physical rect a flash window
/// gets. The expected numbers are WPF's formula worked by hand (FlashService.CalculateGeometry:
/// fit in 40% of the monitor's DIPs, 50-DIP edge padding, #770 centre box), so a change to either
/// the shared math or the head's DIP/pixel conversion fails here.
/// </summary>
public sealed class FlashOverlayPlacementTests
{
    [Fact]
    public void Place_MatchesWpfGeometry_OnAScaledSecondScreen()
    {
        var s = new AppSettings { ImageScale = 100, FlashAvoidCenter = true, FlashCenterExclusionPercent = 25 };
        var screen = new PixelRect(1920, 0, 3840, 2160);   // 4K at 200% = 1920x1080 DIPs, right of the primary
        var rng = new Random(7);

        for (var i = 0; i < 2000; i++)
        {
            var r = FlashOverlay.Place(screen, 2.0, 1000, 500, s, rng, Enumerable.Empty<PixelRect>());

            // 1000x500 in 40% of 1920x1080 DIPs: ratio min(768/1000, 432/500) = 0.768 -> 768x384 DIPs.
            Assert.Equal(new PixelSize(1536, 768), r.Size);
            // 50-DIP padding (100 px) on every edge, on THIS screen.
            Assert.InRange(r.X, 1920 + 100, 1920 + 3840 - 100 - 1536);
            Assert.InRange(r.Y, 100, 2160 - 100 - 768);
            // Never on the crosshair: the adaptive box never shrinks below 5% (54 DIPs, centred).
            var box = new PixelRect(1920 + 933 * 2, 513 * 2, 54 * 2, 54 * 2);
            Assert.False(r.Intersects(box), $"flash {r} covers the centre box {box}");
        }
    }

    [Fact]
    public void Place_ReRollsAwayFromAnOccupiedSpot()
    {
        var s = new AppSettings { ImageScale = 100, FlashAvoidCenter = false };
        var screen = new PixelRect(0, 0, 1920, 1080);
        var first = FlashOverlay.Place(screen, 1.0, 400, 400, s, new Random(1), Enumerable.Empty<PixelRect>());
        Assert.Equal(new PixelSize(432, 432), first.Size);

        // Same seed, spot taken: WPF re-rolls (up to 10 times) away from a >30% overlap.
        var second = FlashOverlay.Place(screen, 1.0, 400, 400, s, new Random(1), new[] { first });
        Assert.NotEqual(first.Position, second.Position);
    }
}
