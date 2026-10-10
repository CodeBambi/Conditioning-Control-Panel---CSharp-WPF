using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Tests;

/// <summary>Offscreen PNGs of the Social pages for a desk look: set CCP_SOCIAL_SHOTS to a folder.</summary>
internal static class SocialShots
{
    internal static void Shot(FrameworkElement el, string name, double width, double height)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_SOCIAL_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var frame = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x0A, 0x1E)),
            Width = width,
            Height = height,
            Child = el,
        };
        frame.Measure(new Size(width, height));
        frame.Arrange(new Rect(0, 0, width, height));
        frame.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(frame);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, "social-" + name + ".png"));
        enc.Save(fs);
        frame.Child = null;
    }
}
