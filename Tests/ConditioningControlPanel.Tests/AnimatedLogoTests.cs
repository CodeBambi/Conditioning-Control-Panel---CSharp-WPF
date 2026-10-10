using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

[Collection(CompanionWpfRenderCollection.Name)]
public class AnimatedLogoTests
{
    [Fact]
    public void BuiltinArtworkKeepsClickRoutingAndTransformsAndOverridesStayUntouched() => WpfRenderHarness.OnStaThread(() =>
    {
        var pulse = new ScaleTransform(1.05, 1.05);
        var logo = new AnimatedLogoImage { Width = 128, Height = 128, RenderTransform = pulse };
        var frame = new Border { Child = logo, ToolTip = "existing tooltip" };
        int clicks = 0;
        frame.MouseDown += (_, _) => clicks++;
        logo.SetArtwork(new BitmapImage(new Uri("pack://application:,,,/Resources/logo2.png")));
        Assert.True(logo.IsAnimatedArtwork);
        Assert.Equal(1024, ((BitmapSource)logo.Source).PixelWidth);
        Assert.Same(pulse, logo.RenderTransform);
        frame.Measure(new Size(128, 128)); frame.Arrange(new Rect(0, 0, 128, 128));
        logo.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
        Assert.Equal(1, clicks);

        using var stream = Application.GetResourceStream(new Uri(AnimatedLogoImage.ArtworkUri)).Stream;
        var custom = new BitmapImage();
        custom.BeginInit(); custom.CacheOption = BitmapCacheOption.OnLoad; custom.StreamSource = stream; custom.EndInit(); custom.Freeze();
        logo.SetArtwork(custom);
        Assert.False(logo.IsAnimatedArtwork);
        Assert.Same(custom, logo.Source);
        Assert.Same(pulse, logo.RenderTransform);
        Assert.False(logo.IsAnimating);
        logo.SetArtwork(new BitmapImage(new Uri("pack://application:,,,/Resources/logo.png")));
        Assert.True(logo.IsAnimatedArtwork);   // the built-in Bambi wordmark takes the new logo too (6.11.0)
    });

    [Fact]
    public void RendererUsesShippedArtAndProducesDistinctIdleAndHoverFrames() => WpfRenderHarness.OnStaThread(() =>
    {
        using var stream = Application.GetResourceStream(new Uri(AnimatedLogoImage.ArtworkUri)).Stream;
        using var renderer = new DashboardLogoRenderer(stream);
        using var surface = SKSurface.Create(new SKImageInfo(360, 360));
        byte[] Render(double phase, double energy, string label)
        {
            renderer.Draw(surface.Canvas, 360, 360, phase, energy);
            using var image = surface.Snapshot();
            using var pixels = SKBitmap.FromImage(image);
            Assert.Equal(0, pixels.GetPixel(0, 0).Alpha);
            Assert.Equal(255, pixels.GetPixel(180, 180).Alpha);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            if (Environment.GetEnvironmentVariable("CCP_LOGO_CAPTURE_DIR") is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "logo-native-" + label + ".png"), encoded.ToArray());
            }
            return pixels.Bytes;
        }
        var still = Render(0, 0, "start");
        var idle = Render(Math.PI / 2, 0, "idle");
        var hover = Render(Math.PI / 2, 1, "hover");
        Assert.False(still.SequenceEqual(idle));
        Assert.False(idle.SequenceEqual(hover));
        // Drawing another frame cannot accumulate transforms or damage the cached artwork.
        Assert.Equal(still, Render(0, 0, "restart"));
    });

    [Fact]
    public void RestDriveSitsOnTheIdleFloorAndHoverRunsThreeTimesFaster()
    {
        Assert.Equal(AnimatedLogoImage.IdleFloor, AnimatedLogoImage.Drive(0), 6);
        Assert.Equal(1.0, AnimatedLogoImage.Drive(1), 6);
        Assert.True(AnimatedLogoImage.IdleFloor >= .3 && AnimatedLogoImage.IdleFloor <= .4);
        Assert.Equal(3.0, AnimatedLogoImage.PhaseRate(1) / AnimatedLogoImage.PhaseRate(0), 6);
        Assert.Equal(Math.Tau / 12, AnimatedLogoImage.PhaseRate(0), 6);
        // Out-of-range energy never pushes the drive past the hover look.
        Assert.Equal(1.0, AnimatedLogoImage.Drive(4), 6);
        Assert.Equal(AnimatedLogoImage.IdleFloor, AnimatedLogoImage.Drive(-1), 6);
    }

    [Fact]
    public void RestFramesHalfASecondApartMoveVisiblyMoreThanTheOldStillRest() => WpfRenderHarness.OnStaThread(() =>
    {
        using var stream = Application.GetResourceStream(new Uri(AnimatedLogoImage.ArtworkUri)).Stream;
        using var renderer = new DashboardLogoRenderer(stream);
        using var surface = SKSurface.Create(new SKImageInfo(360, 360));
        string? folder = Environment.GetEnvironmentVariable("CCP_LOGO_CAPTURE_DIR");
        byte[] Render(double phase, double drive, string? label)
        {
            renderer.Draw(surface.Canvas, 360, 360, phase, drive);
            using var image = surface.Snapshot();
            using var pixels = SKBitmap.FromImage(image);
            if (label != null && folder is { Length: > 0 })
            {
                Directory.CreateDirectory(folder);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(folder, "logo-" + label + ".png"), encoded.ToArray());
            }
            return pixels.Bytes;
        }
        static int Moved(byte[] a, byte[] b)
        {
            int moved = 0;
            for (int i = 0; i < a.Length; i += 4)
                if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 24) moved++;
            return moved;
        }
        const double start = 1.1;
        double later = start + AnimatedLogoImage.PhaseRate(0) * .5;
        var restA = Render(start, AnimatedLogoImage.Drive(0), "rest-a");
        var restB = Render(later, AnimatedLogoImage.Drive(0), "rest-b");
        Render(later, AnimatedLogoImage.Drive(1), "hover");
        var oldA = Render(start, 0, null);
        var oldB = Render(later, 0, null);
        int rest = Moved(restA, restB), old = Moved(oldA, oldB);
        Assert.True(rest > 0, "the logo must move at rest");
        Assert.True(rest > old * 1.3, $"rest motion {rest} px should clearly beat the old still rest {old} px");
    });
}
