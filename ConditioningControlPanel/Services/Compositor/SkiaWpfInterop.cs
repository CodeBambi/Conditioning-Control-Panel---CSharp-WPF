using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>WPF-imaging &lt;-&gt; Skia conversion helpers for compositor layers.</summary>
internal static class SkiaWpfInterop
{
    /// <summary>
    /// Copy a (frozen) BitmapSource into a persistent SKImage. Converts to Pbgra32 first when
    /// needed; AnimatedWebp frames already arrive as Pbgra32 so the common path is a straight
    /// pixel copy. The returned image owns its pixels - dispose it when replaced.
    /// </summary>
    public static SKImage ToSKImage(BitmapSource source)
    {
        BitmapSource src = source;
        if (src.Format != PixelFormats.Pbgra32)
        {
            var converted = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
            converted.Freeze();
            src = converted;
        }

        int w = src.PixelWidth, h = src.PixelHeight;
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        src.CopyPixels(new Int32Rect(0, 0, w, h), bmp.GetPixels(), info.BytesSize, info.RowBytes);
        // FromBitmap copies, so the SKBitmap can be disposed here and the SKImage lives on.
        return SKImage.FromBitmap(bmp);
    }
}
