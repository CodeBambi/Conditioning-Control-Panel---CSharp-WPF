using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Subliminal card look and bouncing-text placement on the Avalonia head, checked against the WPF
/// numbers: SubliminalService.BuildSubliminalContent/CreateTextBlock/ShowSubliminalVisuals and
/// BouncingTextService.CalculateScreenBounds / BouncingTextWindow.UpdatePosition.
/// </summary>
public sealed class TextOverlayTests
{
    [Fact]
    public async Task SubliminalCard_MatchesWpfLook()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureAvalonia();
            var s = new AppSettings
            {
                SubBackgroundTransparent = false, SubBackgroundColor = "#102030",
                SubTextColor = "#FF00FF", SubBorderColor = "#FFFFFF", SubliminalFont = "DejaVu Sans",
            };
            var grid = SubliminalOverlayWindow.Build("OBEY", s);

            // Background first, then 8 border copies at WPF's offsets, then the main text on top.
            Assert.Equal(Color.Parse("#102030"), ((SolidColorBrush)((Rectangle)grid.Children[0]).Fill!).Color);
            var texts = grid.Children.OfType<TextBlock>().ToList();
            Assert.Equal(9, texts.Count);
            var offsets = texts.Take(8).Select(t => (((TranslateTransform)t.RenderTransform!).X, ((TranslateTransform)t.RenderTransform!).Y));
            Assert.Equal(new[] { (-3.0, -3.0), (3.0, -3.0), (-3.0, 3.0), (3.0, 3.0), (0.0, -4.0), (0.0, 4.0), (-4.0, 0.0), (4.0, 0.0) }, offsets);
            Assert.All(texts.Take(8), t => Assert.Equal(Colors.White, ((SolidColorBrush)t.Foreground!).Color));
            var main = texts[8];
            Assert.Equal(Color.Parse("#FF00FF"), ((SolidColorBrush)main.Foreground!).Color);
            Assert.All(texts, t =>
            {
                Assert.Equal("OBEY", t.Text);
                Assert.Equal(120, t.FontSize);
                Assert.Equal(FontWeight.Bold, t.FontWeight);
                Assert.Equal("DejaVu Sans", t.FontFamily.FamilyNames[0]);
            });

            // Transparent background: no rectangle at all.
            s.SubBackgroundTransparent = true;
            Assert.Empty(SubliminalOverlayWindow.Build("OBEY", s).Children.OfType<Rectangle>());
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void SubliminalHold_IsFramesTimes17_Min100()
    {
        Assert.Equal(100, SubliminalOverlay.HoldMs(2));
        Assert.Equal(85 * 17, SubliminalOverlay.HoldMs(85));
    }

    [Fact]
    public async Task SubliminalCard_ClosesOnTime_EvenWithoutAFirstFrame()
    {
        // A first frame that never comes (measured on XWayland: up to 2 s late). The card must
        // still go, at most MaxLifetime after Show.
        var frame = SubliminalOverlayWindow.RequestFrame;
        SubliminalOverlayWindow.RequestFrame = (_, _) => { };
        try
        {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureAvalonia();
            var w = new SubliminalOverlayWindow("OBEY");
            var closed = false;
            w.Closed += (_, _) => closed = true;
            w.Show();
            w.Run(1, TimeSpan.FromMilliseconds(SubliminalOverlay.HoldMs(2)));
            Assert.Equal(TimeSpan.FromMilliseconds(300), SubliminalOverlayWindow.MaxLifetime(TimeSpan.FromMilliseconds(100)));
            await Task.Delay(600);
            Assert.True(closed);
        });
        }
        finally { SubliminalOverlayWindow.RequestFrame = frame; }
    }

    [Fact]
    public void OverlayOpacity_IsNeverExactlyZero()
    {
        // KWin/XWayland starves an opacity-0 window of frame callbacks and stalls every render 1-2 s.
        Assert.Equal(1u, X11Overlay.OpacityCardinal(0));
        Assert.Equal(1u, X11Overlay.OpacityCardinal(-1));
        Assert.Equal(uint.MaxValue, X11Overlay.OpacityCardinal(1));
        Assert.Equal(uint.MaxValue / 2, X11Overlay.OpacityCardinal(0.5));
    }

    [Fact]
    public void BouncingText_BoundsAndLocalPosition_MatchWpf()
    {
        // Primary 2560x1440 at 125% on the left, a 1920x1080 screen to its right, offset down.
        var primary = new PixelRect(0, 0, 2560, 1440);
        var second = new PixelRect(2560, 200, 1920, 1080);
        const double k = 1.25;

        var (minX, minY, maxX, maxY) = BouncingTextOverlay.Bounds(new[] { primary, second }, k);
        Assert.Equal((0, 0, 4480 / k, 1440 / k), (minX, minY, maxX, maxY));

        // A logo at engine DIP (2100, 300) is on the second screen: WPF subtracts that screen's
        // origin divided by the one DPI scale.
        Assert.Equal(new Point(2100 - 2048, 300 - 160), BouncingTextOverlay.ToLocal(2100, 300, second, k));
        Assert.Equal(new Point(2100, 300), BouncingTextOverlay.ToLocal(2100, 300, primary, k));
    }

    [Fact]
    public void BouncingText_OnlyScreensNearTheLogoAreRepainted()
    {
        var second = new PixelRect(2560, 200, 1920, 1080);   // engine DIPs 2048..3584 x 160..1024 at k=1.25
        // 400x100 logo: pad = 250 + 80 + 156 = 486 DIPs.
        Assert.True(BouncingTextOverlay.IsNear(2100, 300, 400, 100, second, 1.25));        // on it
        Assert.True(BouncingTextOverlay.IsNear(2048 - 400 - 480, 300, 400, 100, second, 1.25)); // 480 short: rotated/burst reach
        Assert.False(BouncingTextOverlay.IsNear(2048 - 400 - 490, 300, 400, 100, second, 1.25));
        Assert.False(BouncingTextOverlay.IsNear(2100, 1024 + 490, 400, 100, second, 1.25));
    }

    private static void EnsureAvalonia()
    {
        Assert.True(AvaloniaTestDispatcher.IsDispatcherThread);
        if (Application.Current is null)
        {
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
    }
}
