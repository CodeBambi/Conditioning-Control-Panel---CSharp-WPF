using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Billboard;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The first five tip scenes (quests, awareness, programs, deeper, blink): each paints a whole loop
/// at two card sizes without throwing, and with CCP_TIP_SHOTS_A set to a folder writes PNG strips
/// there for a look check. Nothing here judges the look.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class BillboardTipScenesATests
{
    private static readonly string[] Ids = { "quests", "awareness", "programs", "deeper", "blink" };

    private static Grid Realize(FrameworkElement element, double w, double h)
    {
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)) };
        host.Children.Add(element);
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        host.UpdateLayout();
        return host;
    }

    [Fact]
    public void Every_scene_paints_a_whole_loop_at_two_sizes() => WpfRenderHarness.OnStaThread(() =>
    {
        foreach (var id in Ids)
        {
            var view = TipScenes.Create(id);
            var art = Assert.IsAssignableFrom<TipSceneABase>(view);
            art.Accent = Color.FromRgb(0x9b, 0x7b, 0xff);
            foreach (var (w, h) in new[] { (600.0, 340.0), (1000.0, 420.0) })
            {
                Realize(art, w, h);
                for (double t = 0; t <= 10; t += 0.23)
                {
                    art.SecondsForTests = t;
                    art.Redraw();
                }
                ((Grid)art.Parent).Children.Clear();
            }
            art.Release();
        }
    });

    [Fact]
    public void Shots_when_asked() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_TIP_SHOTS_A");
        if (string.IsNullOrWhiteSpace(dir)) return;
        System.IO.Directory.CreateDirectory(dir);
        var times = new[] { 0.35, 1.0, 1.6, 2.1, 2.6, 3.2, 3.6, 4.4 };
        foreach (var id in Ids)
        {
            foreach (var (w, h) in new[] { (600, 340), (1000, 420) })
            {
                var art = (BillboardVectorArt)TipScenes.Create(id);
                art.Accent = Color.FromRgb(0x9b, 0x7b, 0xff);
                var host = Realize(art, w, h);
                // A strip: every moment side by side, two rows.
                int cols = 4, rows = (times.Length + cols - 1) / cols;
                var strip = new DrawingVisual();
                using (var dc = strip.RenderOpen())
                {
                    for (int i = 0; i < times.Length; i++)
                    {
                        art.SecondsForTests = times[i];
                        art.Redraw();
                        host.UpdateLayout();
                        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                        bmp.Render(host);
                        bmp.Freeze();
                        dc.DrawImage(bmp, new Rect((i % cols) * w, (i / cols) * h, w, h));
                    }
                }
                var all = new RenderTargetBitmap(w * cols, h * rows, 96, 96, PixelFormats.Pbgra32);
                all.Render(strip);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(all));
                using var f = System.IO.File.Create(System.IO.Path.Combine(dir, $"{id}-{w}x{h}.png"));
                enc.Save(f);
                art.Release();
            }
        }
    });
}
