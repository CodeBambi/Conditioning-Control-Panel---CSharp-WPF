using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Billboard;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Loom and Support house scenes paint across a whole loop at card sizes without throwing,
/// and the still frame draws. With CCP_HOUSE_B_SHOTS set to a folder they also write contact
/// sheets (a dozen moments of each loop) there for a look; nothing here judges the look.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class HouseScenesBTests
{
    private static Grid Realize(FrameworkElement element, double w, double h)
    {
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)) };
        host.Children.Add(element);
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        host.UpdateLayout();
        return host;
    }

    private static (string Name, BillboardVectorArt View, string Hue)[] Scenes() => new (string, BillboardVectorArt, string)[]
    {
        ("loom", new LoomHouseArt(), "#9b7bff"),
        ("support", new SupportHouseArt(), "#ffc94a"),
    };

    [Fact]
    public void Both_scenes_paint_a_whole_loop_and_release() => WpfRenderHarness.OnStaThread(() =>
    {
        foreach (var (_, view, hue) in Scenes())
        {
            view.Accent = BillboardVectorArt.ParseHue(hue, Colors.HotPink);
            foreach (var (w, h) in new[] { (600.0, 340.0), (1000.0, 420.0) })
            {
                var host = Realize(view, w, h);
                for (double t = 0; t < 15; t += 0.1)
                {
                    view.SecondsForTests = t;
                    view.Redraw();
                }
                host.Children.Clear();
            }
            view.SecondsForTests = null;
            view.Redraw();
            view.Release();
        }
    });

    [Fact]
    public void The_loom_bead_sits_on_the_free_end_of_the_laid_thread() => WpfRenderHarness.OnStaThread(() =>
    {
        // The thread from the bobbin must run to the very end of what is wound so far: the bead
        // and the end of the revealed path come from one fill value and never drift apart.
        var loom = new LoomHouseArt();
        foreach (var h in new[] { 340.0, 420.0, 630.0 })
        {
            for (double k = 0; k <= 1.0001; k += 0.01)
            {
                var (bead, end) = loom.TipForTests(k, h);
                Assert.True((bead - end).Length < h * 0.004, $"h {h} k {k:0.00}: bead {bead} vs path end {end}");
            }
        }
    });

    [Fact]
    public void Shots_when_asked() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_HOUSE_B_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        const int cols = 4;
        var times = new[] { 0.3, 0.9, 1.6, 2.4, 3.0, 3.6, 4.2, 4.8, 5.4, 6.0, 6.6, 7.4 };
        foreach (var (fw, fh, tag) in new[] { (600, 340, ""), (1000, 630, "-1000x630") })
        foreach (var (name, view, hue) in Scenes())
        {
            view.Accent = BillboardVectorArt.ParseHue(hue, Colors.HotPink);
            var host = Realize(view, fw, fh);
            int rows = (times.Length + cols - 1) / cols;
            var sheet = new DrawingVisual();
            using (var dc = sheet.RenderOpen())
            {
                for (int i = 0; i < times.Length; i++)
                {
                    view.SecondsForTests = times[i];
                    view.Redraw();
                    host.UpdateLayout();
                    var bmp = new RenderTargetBitmap(fw, fh, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(host);
                    bmp.Freeze();
                    dc.DrawImage(bmp, new Rect(i % cols * fw, i / cols * fh, fw, fh));
                    if (Math.Abs(times[i] - BillboardVectorArt.StillSeconds) < 0.01) Save(bmp, Path.Combine(dir, $"{name}{tag}-still.png"));
                }
            }
            var all = new RenderTargetBitmap(fw * cols, fh * rows, 96, 96, PixelFormats.Pbgra32);
            all.Render(sheet);
            Save(all, Path.Combine(dir, $"{name}{tag}-sheet.png"));

            // One wide frame at the size the dashboard card uses.
            host.Children.Clear();
            var wide = Realize(view, 1000, 420);
            view.SecondsForTests = 3.1;
            view.Redraw();
            wide.UpdateLayout();
            var bw = new RenderTargetBitmap(1000, 420, 96, 96, PixelFormats.Pbgra32);
            bw.Render(wide);
            Save(bw, Path.Combine(dir, $"{name}-wide-{3.1.ToString("0.0", CultureInfo.InvariantCulture)}.png"));
            view.Release();
        }
    });

    private static void Save(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
    }
}
