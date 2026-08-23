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
/// Opens a flash image as a CLIP, when it is one.
///
/// <para>Separate from <see cref="IFlashFrameSource"/> because the two answer different questions
/// and a still is not a degenerate clip: <see cref="IFlashFrameSource.Render"/> produces the pixels
/// a placement needs and is what every flash goes through, while this is asked only afterwards, and
/// only for a file that could carry more than one frame. A source that returns null for everything
/// is a build with no frame stepper, and every flash in it is still placed.</para>
/// </summary>
public interface IFlashAnimationSource
{
    /// <summary>
    /// Open <paramref name="path"/> as a clip at exactly <paramref name="width"/> ×
    /// <paramref name="height"/> pixels, or return null — never throw — when it is a still image,
    /// cannot be decoded, or is in a format this build cannot step. The returned animation's frame
    /// buffer is reused; see <see cref="ISpiralAnimation"/> for its lifetime.
    /// </summary>
    ISpiralAnimation? Open(string path, int width, int height);
}

/// <summary>
/// How fast an ANIMATED FLASH's frames go by, as a pure function.
///
/// <para><b>It is not <see cref="SpiralFrameDelay"/>, and the difference is loud.</b> Upstream
/// decodes a flash GIF or animated WebP through SkiaSharp and takes the MEAN of the per-frame
/// durations its codec reported, with a 100 ms fallback and a 20–2000 ms clamp
/// (<c>Services/Media/AnimatedWebp.cs:205-212</c>, reached for GIFs from
/// <c>Services/Flash/FlashService.cs:930-950</c> and for webp from <c>:909-924</c>; the same 100 ms
/// is the still path's field default at <c>:801</c>, <c>:985</c>). The spiral's law is a different
/// one on a different file (<c>Services/Notifications/OverlayService.cs:1543-1556</c>: the FIRST
/// frame's delay, 50 ms fallback, out-of-range falls back rather than clamping). A GIF declaring
/// 60 hundredths per frame runs at 600 ms here and would run at 50 ms there — twelve times too
/// fast — so the flash could not simply borrow the spiral's number.</para>
///
/// <para><b>Upstream's frame SUBSAMPLING is not ported and does not need to be.</b> Its decoder
/// keeps at most 60 frames inside a 30 MB budget and, when it drops to every <c>step</c>-th frame,
/// multiplies the delay by <c>step</c> so the clip still takes the same wall-clock time
/// (<c>AnimatedWebp.cs:212</c>). The port caches no frames at all — each one is decoded on demand
/// into a single reused buffer (divergence D88) — so there is nothing to subsample, every frame is
/// shown, and <c>step</c> is always 1.</para>
/// </summary>
public static class FlashFrameDelay
{
    /// <summary>Upstream's fallback when a clip carries no usable delay
    /// (<c>AnimatedWebp.cs:210-211</c>, and the still path's own initial value at
    /// <c>FlashService.cs:801</c>): 100 ms.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromMilliseconds(100);

    /// <summary>Upstream's lower clamp (<c>AnimatedWebp.cs:212</c>).</summary>
    public static readonly TimeSpan Minimum = TimeSpan.FromMilliseconds(20);

    /// <summary>Upstream's upper clamp (<c>AnimatedWebp.cs:212</c>). Four times the spiral's
    /// ceiling, which is why a slow slideshow GIF is a slideshow here and a strobe there.</summary>
    public static readonly TimeSpan Maximum = TimeSpan.FromMilliseconds(2000);

    /// <summary>
    /// The delay a clip's <c>PropertyTagFrameDelay</c> (0x5100) values mean, in upstream's
    /// arithmetic and upstream's order (<c>AnimatedWebp.cs:205-212</c>): each entry is hundredths of
    /// a second, so × 10 for milliseconds; only entries above zero are SAMPLED; the mean of the
    /// samples is taken; a mean below <see cref="Minimum"/> — and a clip with no samples at all —
    /// becomes <see cref="Default"/> rather than the bound; and the result is clamped.
    ///
    /// <para><b>A zero delay is skipped, not counted as zero.</b> Most of the web's GIFs declare 0,
    /// meaning "as fast as you can", and averaging those in would drag every clip that has one
    /// toward a strobe. Upstream samples <c>if (d &gt; 0)</c> for exactly that reason and a clip of
    /// nothing but zeroes therefore lands on 100 ms, which is what a browser shows it at.</para>
    /// </summary>
    public static TimeSpan FromHundredths(IReadOnlyList<int> hundredths)
    {
        ArgumentNullException.ThrowIfNull(hundredths);

        var total = 0L;
        var samples = 0;
        for (var i = 0; i < hundredths.Count; i++)
        {
            var milliseconds = (long)hundredths[i] * 10;
            if (milliseconds > 0)
            {
                total += milliseconds;
                samples++;
            }
        }

        var mean = samples > 0 ? (double)total / samples : Default.TotalMilliseconds;
        if (mean < Minimum.TotalMilliseconds)
        {
            mean = Default.TotalMilliseconds;
        }

        return TimeSpan.FromMilliseconds(
            Math.Clamp(mean, Minimum.TotalMilliseconds, Maximum.TotalMilliseconds));
    }
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

/// <summary>
/// The product flash CLIP source — and it contains no decoder, on purpose.
///
/// <para>Stepping GDI+ frames was already solved once, for the spiral
/// (<see cref="GdiPlusSpiralFrameSource.OpenAnimation"/>): the first frame dimension, the frame
/// count, the <c>0x5100</c> delay property, <c>GdipImageSelectActiveFrame</c> and one reused
/// buffer. A second copy of that walk is exactly what this file must not grow, so this class is the
/// three decisions that are the FLASH's — which files are worth opening, what a one-frame file
/// means, and which profile the shared walk runs under — and nothing else.</para>
///
/// <para><b>What animates and what does not, stated rather than implied.</b> GIF does. WebP does
/// NOT: GDI+ has no WebP codec at all, so an animated <c>.webp</c> in the user's folder returns
/// null here and is placed as whatever the still path could make of it — which for WebP is nothing
/// (<see cref="GdiPlusFlashFrameSource"/>'s own recorded divergence). Upstream animates both,
/// through SkiaSharp (<c>FlashService.cs:903-928</c>); matching that would mean a decoding
/// dependency this build does not have, and it is recorded as a divergence rather than papered
/// over. <b>Windows only</b>, exactly like the still decoder and the surface it feeds — the overlay
/// refuses on every other platform anyway, so no capability is lost and none is claimed.</para>
/// </summary>
public sealed class GdiPlusFlashAnimationSource : IFlashAnimationSource
{
    /// <summary>The extensions in the pool's own list (<see cref="FlashImagePool"/>) that this
    /// build's decoder can carry more than one frame of. Everything else is opened by the still path
    /// and never by this one, so an ordinary JPEG flash costs not one extra decode.</summary>
    private static readonly string[] AnimatableExtensions = [".gif"];

    /// <summary>True when a file is even worth asking about — its extension is one this build can
    /// step. Public because it is the whole reason a flash's animation attempt is nearly free.</summary>
    public static bool MayAnimate(string path) =>
        !string.IsNullOrEmpty(path)
        && AnimatableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public ISpiralAnimation? Open(string path, int width, int height)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!MayAnimate(path))
        {
            return null;
        }

        var animation = GdiPlusSpiralFrameSource.OpenAnimation(
            path, width, height, AnimatedImageProfile.Flash);
        if (animation is null)
        {
            return null;
        }

        // A one-frame GIF is a PICTURE, and upstream says so in the same words: its animated decode
        // returns null below two frames (AnimatedWebp.cs:209) and the caller falls through to the
        // static path (FlashService.cs:955-956). Returning it here instead would put a timer on the
        // clock that repainted the same pixels for the whole of the surface's life.
        if (animation.FrameCount > 1)
        {
            return animation;
        }

        animation.Dispose();
        return null;
    }
}
