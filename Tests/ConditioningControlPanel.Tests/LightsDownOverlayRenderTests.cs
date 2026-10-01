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
}
