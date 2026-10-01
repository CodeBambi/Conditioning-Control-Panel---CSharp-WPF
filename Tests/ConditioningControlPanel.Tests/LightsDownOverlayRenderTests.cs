using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Lights Down drawn offscreen over a stand-in video: a 9:16 clip pillarboxed on a
/// 1920x1080 screen, watched until the room is fully down. The room must go dark, the picture
/// must stay clean, and a live attention target must never sit under the dark.
/// Set CCP_LIGHTSDOWN_SHOT to a folder to also save the frame as a PNG for a look by eye.
/// </summary>
public class LightsDownOverlayRenderTests
{
    private const int W = 1920, H = 1080;

    [Fact]
    public void Full_depth_darkens_the_room_keeps_the_picture_clean_and_spares_a_target()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var o = LightsDownOverlay.ForTest(W, H, 9.0 / 16);
            var s = new LightsDownState();
            var f = default(LightsDownFrame);
            for (int i = 0; i < 60 * 30; i++) f = s.Step(1.0 / 60, true, LightsDownMotion.Full, false);
            // A live attention target in the top-left corner of the room.
            o.SetCutouts(new List<Rect> { new Rect(80, 80, 200, 80) });
            o.Apply(f, s.Time);

            var stage = new Grid { Width = W, Height = H };
            stage.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(200, 200, 200)) });
            o.Content = null; // lift the drawing out of the never-shown window
            stage.Children.Add(o.RootForTest);
            stage.Measure(new Size(W, H));
            stage.Arrange(new Rect(0, 0, W, H));
            stage.UpdateLayout();
            var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(stage);

            var shot = Environment.GetEnvironmentVariable("CCP_LIGHTSDOWN_SHOT");
            if (!string.IsNullOrEmpty(shot))
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(System.IO.Path.Combine(shot, "lightsdown.png"));
                enc.Save(fs);
            }

            int Lum(int x, int y)
            {
                var px = new byte[4];
                rtb.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
                return (px[0] + px[1] + px[2]) / 3;
            }

            Assert.True(Lum(W / 2, H / 2) >= 195, "the picture centre must stay clean");
            Assert.True(Lum(W - 40, H - 40) < 60, "a room corner must be well down");
            Assert.True(Lum(300, H / 2) < 140, "the room beside the picture must dim");
            Assert.True(Lum(180, 120) >= 195, "an attention target must never sit under the dark");
        });
    }

    [Fact]
    public void A_shut_aperture_never_darkens_the_corners_of_a_full_screen_picture()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            // 16:9 on 16:9: the picture IS the screen. Shut, the inner hex's apothem (~1064) is
            // under the picture's half-diagonal (~1101), and at rotation 0 an edge midpoint points
            // straight at the top-left corner: this is where the iris used to bite the picture.
            var o = LightsDownOverlay.ForTest(W, H, 16.0 / 9);
            var f = new LightsDownFrame
            {
                Depth = 0.92, DimAlpha = 0.46, ApertureClose = 1, ApertureAlpha = 0.37,
                ApertureRotation = 0, Hue = 300, Swell = 1, LockRun = -1,
            };
            o.Apply(f, 0);

            var stage = new Grid { Width = W, Height = H };
            stage.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(200, 200, 200)) });
            o.Content = null;
            stage.Children.Add(o.RootForTest);
            stage.Measure(new Size(W, H));
            stage.Arrange(new Rect(0, 0, W, H));
            stage.UpdateLayout();
            var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(stage);

            var px = new byte[4];
            rtb.CopyPixels(new Int32Rect(15, 15, 1, 1), px, 4, 0);
            int lum = (px[0] + px[1] + px[2]) / 3;
            Assert.True(lum >= 195, $"the picture's corner must stay clean, was {lum}");
        });
    }

    [Fact]
    public void The_swell_scales_the_picture_surface_and_never_the_attention_plane()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            // The vmem video window: surface first, then the click overlay and the attention plane.
            var surface = new Border();
            var clickOverlay = new Rectangle();
            var attention = new Canvas();
            var grid = new Grid();
            grid.Children.Add(surface);
            grid.Children.Add(clickOverlay);
            grid.Children.Add(attention);
            var video = new Window { Content = grid };

            Assert.Same(surface, LightsDownOverlay.SwellTarget(video));

            var o = new LightsDownOverlay(IntPtr.Zero, new System.Drawing.Rectangle(0, 0, W, H), 1, 16.0 / 9, video, false, 60);
            o.Apply(new LightsDownFrame { Depth = 0.92, Swell = 1.064, LockRun = -1 }, 0);

            var scale = Assert.IsType<ScaleTransform>(surface.RenderTransform);
            Assert.Equal(1.064, scale.ScaleX, 6);
            Assert.True(grid.RenderTransform.Value.IsIdentity, "the grid carries the attention plane and must not scale");
            Assert.True(attention.RenderTransform.Value.IsIdentity);

            o.RestoreVideo();
            Assert.True(surface.RenderTransform.Value.IsIdentity, "the surface gets its own transform back");
            video.Close();
        });
    }

    [Fact]
    public void An_HwndHost_surface_is_never_swollen()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var grid = new Grid();
            grid.Children.Add(new StubHost());
            Assert.Null(LightsDownOverlay.SwellTarget(new Window { Content = grid }));
            Assert.Null(LightsDownOverlay.SwellTarget(null));
            Assert.Null(LightsDownOverlay.SwellTarget(new Window { Content = new Grid() }));
        });
    }

    [Fact]
    public void Video_teardown_with_Lights_Down_down_touches_nothing()
    {
        // CloseAll calls this on EVERY teardown, Super or not: it must be a no-op when idle.
        Assert.False(LightsDownService.IsActive);
        LightsDownService.OnVideoTeardown();
        Assert.False(LightsDownService.IsActive);
    }

    private sealed class StubHost : System.Windows.Interop.HwndHost
    {
        protected override System.Runtime.InteropServices.HandleRef BuildWindowCore(System.Runtime.InteropServices.HandleRef hwndParent)
            => throw new NotSupportedException();
        protected override void DestroyWindowCore(System.Runtime.InteropServices.HandleRef hwnd) { }
    }
}
