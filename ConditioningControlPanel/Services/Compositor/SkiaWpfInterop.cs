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
    /// pixel copy. ONE copy total: CopyPixels lands in an SKBitmap and the SKImage wraps that
    /// buffer zero-copy, taking ownership (the release delegate disposes the SKBitmap when the
    /// image is disposed). The returned image owns its pixels - dispose it when replaced.
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
        SKBitmap? bmp = null;
        try
        {
            bmp = new SKBitmap(info);
            src.CopyPixels(new Int32Rect(0, 0, w, h), bmp.GetPixels(), info.BytesSize, info.RowBytes);
            bmp.SetImmutable();

            // Zero-copy wrap (FromBitmap here used to be a SECOND full copy per frame - the
            // per-spawn UI hitch at flash-burst scale): the image adopts the bitmap's pixel
            // buffer and the release delegate frees it with the image.
            using var pixmap = bmp.PeekPixels();
            var image = SKImage.FromPixels(pixmap,
                static (_, ctx) => ((SKBitmap)ctx).Dispose(), bmp);
            if (image != null)
            {
                bmp = null;   // ownership transferred to the SKImage's release delegate
                return image;
            }
            // Defensive fallback: wrap refused (shouldn't happen for a valid pixmap) - copy.
            return SKImage.FromBitmap(bmp);
        }
        finally
        {
            bmp?.Dispose();
        }
    }
}
