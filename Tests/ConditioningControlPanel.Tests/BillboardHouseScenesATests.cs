using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Billboard;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The house scenes Discord, Web App and Remix Room: each paints every moment of its loop
/// and actually moves. With CCP_HOUSE_A_SHOTS set to a folder, contact sheets are written there
/// (a look check for the art lane, not a judge of the look); nothing is written otherwise.
/// </summary>
public class BillboardHouseScenesATests
{
    private static readonly (string Name, Func<BillboardVectorArt> Make, string Hue)[] Scenes =
    {
        ("discord", () => new DiscordHouseArt(), "#7b86ff"),
        ("webapp", () => new WebAppHouseArt(), "#5fe3ff"),
        ("remix", () => new RemixHouseArt(), "#ff4fa8"),
    };

    [Fact]
    public void Each_house_scene_draws_and_moves() => WpfRenderHarness.OnStaThread(() =>
    {
        foreach (var s in Scenes)
        {
            var view = s.Make();
            view.Accent = BillboardVectorArt.ParseHue(s.Hue, Colors.HotPink);
            var host = Realize(view, 600, 340);
            byte[]? first = null;
            bool moved = false;
            foreach (var t in new[] { 0.0, 0.4, 1.6, 2.5, 3.9, 5.2, 7.7, 13.3 })
            {
                view.SecondsForTests = t;
                view.Redraw();
                host.UpdateLayout();
                var px = Pixels(host);
                if (first == null) first = px;
                else if (!moved) moved = !px.AsSpan().SequenceEqual(first);
            }
            Assert.True(moved, $"{s.Name} never changed between moments");
            view.Release();
        }
    });

    [Fact]
    public void House_scenes_are_dispatched_by_poster_name()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            Assert.IsType<DiscordHouseArt>(HouseScenes.Create("billboard/discord.png"));
            Assert.IsType<WebAppHouseArt>(HouseScenes.Create("billboard/webapp.png"));
            Assert.IsType<RemixHouseArt>(HouseScenes.Create("billboard/remix.png"));
        });
    }

    [Fact]
    public void Shots_when_asked() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_HOUSE_A_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var times = new[] { 0.3, 1.1, BillboardVectorArt.StillSeconds, 2.2, 2.9, 3.6, 4.0, 4.5, 5.3, 6.1 };
        foreach (var size in new[] { (W: 1000, H: 420), (W: 1010, H: 700), (W: 600, H: 340) })
            foreach (var s in Scenes)
            {
                var view = s.Make();
                view.Accent = BillboardVectorArt.ParseHue(s.Hue, Colors.HotPink);
                var host = Realize(view, size.W, size.H);
                int cols = 2, rows = (times.Length + 1) / 2;
                var sheet = new DrawingVisual();
                using (var dc = sheet.RenderOpen())
                {
                    for (int i = 0; i < times.Length; i++)
                    {
                        view.SecondsForTests = times[i];
                        view.Redraw();
                        host.UpdateLayout();
                        var bmp = Render(host);
                        if (times[i] == BillboardVectorArt.StillSeconds) Save(bmp, Path.Combine(dir, $"{s.Name}-{size.W}-still.png"));
                        dc.DrawImage(bmp, new Rect((i % cols) * size.W, (i / cols) * size.H, size.W, size.H));
                    }
                }
                var all = new RenderTargetBitmap(cols * size.W, rows * size.H, 96, 96, PixelFormats.Pbgra32);
                all.Render(sheet);
                Save(all, Path.Combine(dir, $"{s.Name}-{size.W}-sheet.png"));
                view.Release();
            }
    });

    private static Grid Realize(FrameworkElement element, double w, double h)
    {
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)), Width = w, Height = h };
        host.Children.Add(element);
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        host.UpdateLayout();
        return host;
    }

    private static RenderTargetBitmap Render(FrameworkElement e)
    {
        var bmp = new RenderTargetBitmap((int)e.ActualWidth, (int)e.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(e);
        bmp.Freeze();
        return bmp;
    }

    private static byte[] Pixels(FrameworkElement e)
    {
        var bmp = Render(e);
        var px = new byte[bmp.PixelWidth * bmp.PixelHeight * 4];
        bmp.CopyPixels(px, bmp.PixelWidth * 4, 0);
        return px;
    }

    private static void Save(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
