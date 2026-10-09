using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Billboard;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The second set of tip scenes (haptics, folders, lockdown, remote): each one paints at every
/// moment of its loop without throwing. With CCP_TIP_B_SHOTS set to a folder, a contact sheet of
/// each loop (and its Motion Off still) is written there to look at. Nothing here judges the look.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class TipScenesBRenderTests
{
    private static readonly (string Name, Func<BillboardVectorArt> Make)[] Scenes =
    {
        ("haptics", () => new HapticsTipArt()),
        ("folders", () => new FoldersTipArt()),
        ("lockdown", () => new LockdownTipArt()),
        ("remote", () => new RemoteTipArt()),
    };

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
    public void Every_scene_paints_through_its_whole_loop() => WpfRenderHarness.OnStaThread(() =>
    {
        foreach (var (name, make) in Scenes)
        {
            var view = make();
            view.Accent = BillboardVectorArt.ParseHue("#9b7bff", Colors.HotPink);
            Realize(view, 600, 340);
            for (double t = 0; t < 8; t += 0.1)
            {
                view.SecondsForTests = t;
                view.Redraw();
            }
            view.Release();
            Assert.False(string.IsNullOrEmpty(name));
        }
    });

    [Fact]
    public void Shots_when_asked() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_TIP_B_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        System.IO.Directory.CreateDirectory(dir);
        const int W = 600, H = 340, Cols = 3;
        foreach (var (name, make) in Scenes)
        {
            var view = make();
            view.Accent = BillboardVectorArt.ParseHue("#9b7bff", Colors.HotPink);
            var host = Realize(view, W, H);
            var times = new[] { 0.3, 0.8, 1.0, 1.25, BillboardVectorArt.StillSeconds, 2.2, 2.9, 3.4, 3.7, 4.2, 4.8, 5.5 };
            int rows = (times.Length + Cols - 1) / Cols;
            var sheet = new DrawingVisual();
            using (var dc = sheet.RenderOpen())
            {
                for (int i = 0; i < times.Length; i++)
                {
                    view.SecondsForTests = times[i];
                    view.Redraw();
                    host.UpdateLayout();
                    var bmp = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(host);
                    bmp.Freeze();
                    dc.DrawImage(bmp, new Rect((i % Cols) * W, (i / Cols) * H, W, H));
                    if (times[i] == BillboardVectorArt.StillSeconds) Save(bmp, System.IO.Path.Combine(dir, $"{name}-still.png"));
                    var label = new FormattedText(times[i].ToString("0.00", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.White, 1.0);
                    dc.DrawText(label, new Point((i % Cols) * W + 6, (i / Cols) * H + 4));
                }
            }
            var all = new RenderTargetBitmap(W * Cols, H * rows, 96, 96, PixelFormats.Pbgra32);
            all.Render(sheet);
            Save(all, System.IO.Path.Combine(dir, $"{name}-sheet.png"));
            view.Release();
        }
    });

    private static void Save(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = System.IO.File.Create(path);
        enc.Save(f);
    }
}
