using System.Runtime.InteropServices;
using CcpClient.Desktop.Overlay;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// Turns one image path into the pixels a flash puts on screen.
///
/// <para>A seam, so the draw path can be proven with a synthetic frame and no decoder, and so a
/// file that cannot be decoded is an ORDINARY outcome rather than an exception: WPF loads its
/// candidates with retries and simply uses whichever ones decode, because "a file is missing,
/// corrupted, or uses an unsupported codec" is normal in a user's own folder
/// (<c>Services/Flash/FlashService.cs:1245-1250</c> and <c>LoadImagesUntilAsync</c>'s own
/// summary).</para>
/// </summary>
public interface IFlashFrameSource
{
    /// <summary>
    /// Decode <paramref name="path"/> and render it at the size <paramref name="targetSize"/> asks
    /// for given the decoded pixel size. Returns null — never throws — when the file is missing,
    /// unreadable, or in a format this build cannot decode.
    /// </summary>
    OverlayFrame? Render(string path, Func<int, int, (int Width, int Height)> targetSize);
}

/// <summary>
/// The product frame source: GDI+ (<c>gdiplus.dll</c>), decoding at display size straight into the
/// frame buffer.
///
/// <para><b>Why GDI+ and not the UI toolkit's decoder.</b> The surface this feeds is a Win32
/// window filled by a GDI blit, and the pixels have to arrive as a B,G,R,X buffer whatever decodes
/// them. GDI+ is part of Windows, needs no package, and — the reason that decided it — it works in
/// a process with NO Avalonia runtime, which is what lets the whole draw path be proven in the
/// pure-logic test project instead of only where a UI toolkit has been initialised. It is
/// Windows-only, exactly like the surface it feeds; the overlay refuses on every other platform
/// anyway (<see cref="UnsupportedOverlayPresence"/>), so no capability is lost by the decoder being
/// Windows-only, and none is claimed either.</para>
///
/// <para><b>Decode at display size, over black.</b> The target buffer is pre-filled black and the
/// image is drawn into it scaled, which is WPF's own composition: a flash window's background is
/// <c>Brushes.Black</c> and the <c>Image</c> on it is pinned to the window's size
/// (<c>FlashService.cs:1245</c>, <c>:1274-1281</c>). A PNG with transparency therefore shows black
/// where it is transparent, as it does upstream, rather than showing the desktop through it — this
/// surface has one uniform alpha and no per-pixel alpha to give it (a recorded divergence).</para>
///
/// <para><b>What it cannot decode.</b> WebP. It is in the pool's extension list
/// (<see cref="FlashImagePool"/>) because the DTRH media rules list it, and GDI+ has no WebP codec;
/// such a file returns null here and contributes no surface, which is the same outcome as a
/// corrupt file. Recorded as a divergence rather than hidden.</para>
/// </summary>
public sealed class GdiPlusFlashFrameSource : IFlashFrameSource
{
    /// <summary>GDI+ <c>Status.Ok</c>.</summary>
    private const int Ok = GdiPlusRuntime.Ok;

    /// <summary>32bpp, no alpha channel in the target: the frame is opaque over black.</summary>
    private const int PixelFormat32bppRgb = 0x00022009;

    /// <summary>GDI+ <c>InterpolationModeHighQualityBicubic</c>. WPF picks its resampling from a
    /// performance tier (<c>FlashService.cs:1288</c>); there is no tier here, so the port takes the
    /// quality end once rather than inventing a policy.</summary>
    private const int HighQualityBicubic = 7;

    /// <summary>True when GDI+ initialised in this process. The startup itself is
    /// <see cref="GdiPlusRuntime"/>'s (the text rasteriser needs the same library up and
    /// <c>GdiplusStartup</c> is per process, not per caller). False means every render returns
    /// null — no frames, no exception, and nothing pretending.</summary>
    public static bool Available => GdiPlusRuntime.Available;

    /// <inheritdoc/>
    public OverlayFrame? Render(string path, Func<int, int, (int Width, int Height)> targetSize)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(targetSize);

        if (!Available)
        {
            return null;
        }

        if (GdiPlus.GdipCreateBitmapFromFile(path, out var source) != Ok || source == 0)
        {
            return null;
        }

        try
        {
            if (GdiPlus.GdipGetImageWidth(source, out var sourceWidth) != Ok
                || GdiPlus.GdipGetImageHeight(source, out var sourceHeight) != Ok
                || sourceWidth == 0 || sourceHeight == 0)
            {
                return null;
            }

            var (width, height) = targetSize((int)sourceWidth, (int)sourceHeight);
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            return RenderInto(source, width, height);
        }
        finally
        {
            GdiPlus.GdipDisposeImage(source);
        }
    }

    private static OverlayFrame? RenderInto(nint source, int width, int height)
    {
        var pixels = new byte[width * height * OverlayFrame.BytesPerPixel];
        // Black, and opaque in the padding byte, BEFORE the draw: transparent source pixels blend
        // onto black exactly as they do on WPF's black-backed flash window.
        for (var i = OverlayFrame.BytesPerPixel - 1; i < pixels.Length; i += OverlayFrame.BytesPerPixel)
        {
            pixels[i] = 0xFF;
        }

        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            // Positive stride = top-down rows, which is the order OverlayFrame declares and the
            // order the surface's top-down DIB section expects. Nothing flips a row anywhere.
            var stride = width * OverlayFrame.BytesPerPixel;
            if (GdiPlus.GdipCreateBitmapFromScan0(
                    width, height, stride, PixelFormat32bppRgb, pinned.AddrOfPinnedObject(), out var target) != Ok
                || target == 0)
            {
                return null;
            }

            try
            {
                if (GdiPlus.GdipGetImageGraphicsContext(target, out var graphics) != Ok || graphics == 0)
                {
                    return null;
                }

                try
                {
                    GdiPlus.GdipSetInterpolationMode(graphics, HighQualityBicubic);
                    if (GdiPlus.GdipDrawImageRectI(graphics, source, 0, 0, width, height) != Ok)
                    {
                        return null;
                    }

                    GdiPlus.GdipFlush(graphics, intention: 1);
                }
                finally
                {
                    GdiPlus.GdipDeleteGraphics(graphics);
                }
            }
            finally
            {
                GdiPlus.GdipDisposeImage(target);
            }
        }
        finally
        {
            pinned.Free();
        }

        return new OverlayFrame(width, height, pixels);
    }

    private static class GdiPlus
    {
        [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
        internal static extern int GdipCreateBitmapFromFile(
            [MarshalAs(UnmanagedType.LPWStr)] string filename, out nint bitmap);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipCreateBitmapFromScan0(
            int width, int height, int stride, int format, nint scan0, out nint bitmap);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipGetImageWidth(nint image, out uint width);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipGetImageHeight(nint image, out uint height);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipSetInterpolationMode(nint graphics, int mode);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipDrawImageRectI(nint graphics, nint image, int x, int y, int width, int height);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipFlush(nint graphics, int intention);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipDeleteGraphics(nint graphics);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipDisposeImage(nint image);
    }
}
