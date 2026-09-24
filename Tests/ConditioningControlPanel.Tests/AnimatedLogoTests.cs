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
        Assert.False(logo.IsAnimatedArtwork);
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
}
