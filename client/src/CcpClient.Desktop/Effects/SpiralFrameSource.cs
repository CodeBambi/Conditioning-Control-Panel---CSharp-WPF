using System.Runtime.InteropServices;
using CcpClient.Desktop.Overlay;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// Turns a spiral file into the frames the layer shows, one at a time, for as long as it is open.
///
/// <para><b>Why this seam is shaped differently from the other two.</b> A flash and a subliminal
/// each render ONE frame and are done, so <see cref="IFlashFrameSource"/> and
/// <see cref="ISubliminalFrameSource"/> are single calls. A spiral is a clip that keeps going for a
/// whole session, so what a caller needs is an OPEN animation it can ask for frame <i>n</i> of —
/// which is also what keeps the port's memory flat where upstream's is not (see
/// <see cref="ISpiralAnimation"/>).</para>
/// </summary>
public interface ISpiralFrameSource
{
    /// <summary>
    /// Open <paramref name="path"/> and prepare to render it at exactly
    /// <paramref name="width"/> × <paramref name="height"/> pixels. Returns null — never throws —
    /// when the file is missing, unreadable, or in a format this build cannot decode, which is the
    /// same ordinary outcome the other two frame sources return null for.
    /// </summary>
    ISpiralAnimation? Open(string path, int width, int height);
}

/// <summary>
/// One open spiral: how many frames it has, how fast they go by, and the pixels of any one of them.
///
/// <para><b>The returned frame's buffer is REUSED and is valid only until the next
/// <see cref="Render"/> or <see cref="IDisposable.Dispose"/>.</b> That is a deliberate contract, not
/// an accident: a full-screen frame is about 8 MB, and allocating one twenty times a second would
/// put 160 MB/s of large-object garbage on the heap for the length of a session. The one consumer
/// hands the frame straight to <see cref="IOverlayPresence.Paint"/>, which copies it into a DIB
/// section before it returns (<c>Overlay/Win32OverlayPresence.cs:295</c>), so nothing ever holds a
/// frame across two renders.</para>
///
/// <para><b>Thread.</b> Opened, rendered and disposed on the surface thread only — a decoder handle
/// is no more shareable than the window it feeds.</para>
/// </summary>
public interface ISpiralAnimation : IDisposable
{
    /// <summary>How many frames the clip has. One means a still image, and a still image is a
    /// legitimate spiral: WPF starts no frame timer at all for one
    /// (<c>Services/Notifications/OverlayService.cs:1370</c>).</summary>
    int FrameCount { get; }

    /// <summary>How long each frame is held — WPF's clamped GIF delay
    /// (<see cref="SpiralFrameDelay"/>).</summary>
    TimeSpan FrameDelay { get; }

    /// <summary>
    /// The pixels of frame <paramref name="index"/>, at the size this animation was opened for, or
    /// null when the frame cannot be produced. See the type's own remarks for the buffer's
    /// lifetime.
    /// </summary>
    OverlayFrame? Render(int index);
}

/// <summary>
/// The product spiral decoder: GDI+ (<c>gdiplus.dll</c>), one frame at a time, straight into a
/// reused full-screen buffer.
///
/// <para><b>What it reproduces</b>, from <c>Services/Notifications/OverlayService.cs</c>: the frame
/// list read off the image's FIRST frame dimension (<c>:1539-1540</c>, WPF's
/// <c>gif.FrameDimensionsList[0]</c>); the per-frame delay from property <c>0x5100</c> in
/// hundredths of a second with WPF's own out-of-range fallback (<c>:1545-1552</c>, see
/// <see cref="SpiralFrameDelay"/>); and the layer's geometry — <c>Stretch.UniformToFill</c> inside a
/// <c>ClipToBounds</c> container covering the whole screen (<c>:1694-1706</c>), which is a
/// centre-crop and is implemented here as one (<see cref="SourceCrop"/>).</para>
///
/// <para><b>Over black, because this surface has one uniform alpha.</b> The buffer is cleared to
/// opaque black before each draw, so a GIF with transparent pixels shows black through them rather
/// than the desktop. Upstream's spiral window is transparency-backed and would show the desktop;
/// this is the same constraint — and the same recorded divergence family — that
/// <see cref="GdiPlusFlashFrameSource"/> already carries for flashes (D57).</para>
///
/// <para><b>What it deliberately does NOT do: cache the clip.</b> Upstream decodes every frame up
/// front into <c>BitmapSource</c>s and then has to defend itself against its own cache — a
/// 1280-pixel downscale, a 120-frame cap and a 300 MB budget, all added after a "3.5 GB / laggy"
/// report (<c>:1560-1600</c>). Here the frame is produced on demand into ONE buffer, so a 500-frame
/// spiral costs the same as a 2-frame one and no cap is needed. The cost is a decode per frame
/// instead of a lookup, which at WPF's own 20 Hz cadence is the cheaper side of the trade
/// (divergence D88).</para>
/// </summary>
public sealed class GdiPlusSpiralFrameSource : ISpiralFrameSource
{
    /// <inheritdoc/>
    public ISpiralAnimation? Open(string path, int width, int height)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (width <= 0 || height <= 0 || !GdiPlusRuntime.Available)
        {
            return null;
        }

        return GdiPlusSpiralAnimation.Open(path, width, height);
    }

    /// <summary>
    /// WPF's <c>Stretch.UniformToFill</c> inside a clipping container (<c>OverlayService.cs:1695</c>,
    /// <c>:1701-1706</c>), as arithmetic: the largest centred rectangle of the source that has the
    /// destination's aspect ratio. The image therefore covers the whole layer with nothing letterboxed
    /// and nothing distorted, and the overflow is cropped evenly on both sides.
    /// </summary>
    /// <returns>The source rectangle to draw from, in source pixels.</returns>
    public static (int X, int Y, int Width, int Height) SourceCrop(
        int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sourceWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sourceHeight, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(targetWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(targetHeight, 0);

        // Compared as a cross-multiplication rather than two divisions: equal aspect ratios must
        // land on the "no crop" answer exactly, and 16/9 == 1920/1080 is not reliably true in
        // double arithmetic.
        var sourceIsWider = (long)sourceWidth * targetHeight > (long)targetWidth * sourceHeight;
        if (sourceIsWider)
        {
            var cropWidth = Math.Max(1, (int)Math.Round((double)sourceHeight * targetWidth / targetHeight));
            cropWidth = Math.Min(cropWidth, sourceWidth);
            return ((sourceWidth - cropWidth) / 2, 0, cropWidth, sourceHeight);
        }

        var cropHeight = Math.Max(1, (int)Math.Round((double)sourceWidth * targetHeight / targetWidth));
        cropHeight = Math.Min(cropHeight, sourceHeight);
        return (0, (sourceHeight - cropHeight) / 2, sourceWidth, cropHeight);
    }

    /// <summary>One open GDI+ image plus the one buffer every frame is drawn into.</summary>
    private sealed class GdiPlusSpiralAnimation : ISpiralAnimation
    {
        /// <summary>GDI+ <c>Status.Ok</c>.</summary>
        private const int Ok = GdiPlusRuntime.Ok;

        /// <summary>32bpp, no alpha channel in the target: the frame is opaque over black.</summary>
        private const int PixelFormat32bppRgb = 0x00022009;

        /// <summary>GDI+ <c>InterpolationModeHighQualityBicubic</c>, the same quality end
        /// <see cref="GdiPlusFlashFrameSource"/> takes and for the same reason.</summary>
        private const int HighQualityBicubic = 7;

        /// <summary>GDI+ <c>UnitPixel</c>: the source rectangle is in source pixels.</summary>
        private const int UnitPixel = 2;

        /// <summary><c>PropertyTagFrameDelay</c> — WPF reads the same tag by the same number
        /// (<c>OverlayService.cs:1545</c>, <c>gif.GetPropertyItem(0x5100)</c>).</summary>
        private const int PropertyTagFrameDelay = 0x5100;

        private readonly nint _image;
        private readonly nint _target;
        private readonly nint _graphics;
        private readonly byte[] _pixels;
        private readonly GCHandle _pinned;
        private readonly int _width;
        private readonly int _height;
        private readonly int _sourceWidth;
        private readonly int _sourceHeight;
        private Guid _dimension;
        private bool _disposed;

        private GdiPlusSpiralAnimation(
            nint image, nint target, nint graphics, byte[] pixels, GCHandle pinned,
            int width, int height, int sourceWidth, int sourceHeight,
            Guid dimension, int frameCount, TimeSpan frameDelay)
        {
            _image = image;
            _target = target;
            _graphics = graphics;
            _pixels = pixels;
            _pinned = pinned;
            _width = width;
            _height = height;
            _sourceWidth = sourceWidth;
            _sourceHeight = sourceHeight;
            _dimension = dimension;
            FrameCount = frameCount;
            FrameDelay = frameDelay;
        }

        public int FrameCount { get; }

        public TimeSpan FrameDelay { get; }

        public static ISpiralAnimation? Open(string path, int width, int height)
        {
            if (GdiPlus.GdipLoadImageFromFile(path, out var image) != Ok || image == 0)
            {
                return null;
            }

            var opened = false;
            var pinned = default(GCHandle);
            var target = (nint)0;
            var graphics = (nint)0;
            try
            {
                if (GdiPlus.GdipGetImageWidth(image, out var sourceWidth) != Ok
                    || GdiPlus.GdipGetImageHeight(image, out var sourceHeight) != Ok
                    || sourceWidth == 0 || sourceHeight == 0)
                {
                    return null;
                }

                var dimension = FirstFrameDimension(image);
                var frameCount = FrameCountOf(image, ref dimension);
                var delay = ReadFrameDelay(image);

                var pixels = new byte[(long)width * height * OverlayFrame.BytesPerPixel];
                pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);

                // Positive stride = top-down rows, which is the order OverlayFrame declares and the
                // order the surface's top-down DIB section expects. Nothing flips a row anywhere.
                var stride = width * OverlayFrame.BytesPerPixel;
                if (GdiPlus.GdipCreateBitmapFromScan0(
                        width, height, stride, PixelFormat32bppRgb, pinned.AddrOfPinnedObject(), out target) != Ok
                    || target == 0)
                {
                    return null;
                }

                if (GdiPlus.GdipGetImageGraphicsContext(target, out graphics) != Ok || graphics == 0)
                {
                    return null;
                }

                GdiPlus.GdipSetInterpolationMode(graphics, HighQualityBicubic);
                opened = true;
                return new GdiPlusSpiralAnimation(
                    image, target, graphics, pixels, pinned, width, height,
                    (int)sourceWidth, (int)sourceHeight, dimension, frameCount, delay);
            }
            finally
            {
                if (!opened)
                {
                    if (graphics != 0)
                    {
                        GdiPlus.GdipDeleteGraphics(graphics);
                    }

                    if (target != 0)
                    {
                        GdiPlus.GdipDisposeImage(target);
                    }

                    if (pinned.IsAllocated)
                    {
                        pinned.Free();
                    }

                    GdiPlus.GdipDisposeImage(image);
                }
            }
        }

        public OverlayFrame? Render(int index)
        {
            if (_disposed || index < 0 || index >= FrameCount)
            {
                return null;
            }

            // Selecting the active frame is what makes this a clip rather than a picture; for a
            // one-frame image it is a no-op the decoder answers immediately.
            if (FrameCount > 1 && GdiPlus.GdipImageSelectActiveFrame(_image, ref _dimension, (uint)index) != Ok)
            {
                return null;
            }

            // Opaque black BEFORE the draw, every frame: the previous frame must not show through a
            // transparent pixel of this one, and this surface has no per-pixel alpha to give the
            // desktop through it either (see the type's remarks).
            if (GdiPlus.GdipGraphicsClear(_graphics, 0xFF000000) != Ok)
            {
                return null;
            }

            var (cropX, cropY, cropWidth, cropHeight) =
                SourceCrop(_sourceWidth, _sourceHeight, _width, _height);
            if (GdiPlus.GdipDrawImageRectRectI(
                    _graphics, _image, 0, 0, _width, _height,
                    cropX, cropY, cropWidth, cropHeight, UnitPixel, 0, 0, 0) != Ok)
            {
                return null;
            }

            GdiPlus.GdipFlush(_graphics, intention: 1);
            return new OverlayFrame(_width, _height, _pixels);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            GdiPlus.GdipDeleteGraphics(_graphics);
            GdiPlus.GdipDisposeImage(_target);
            GdiPlus.GdipDisposeImage(_image);
            if (_pinned.IsAllocated)
            {
                _pinned.Free();
            }
        }

        /// <summary>WPF's <c>gif.FrameDimensionsList[0]</c> (<c>OverlayService.cs:1539-1540</c>). An image
        /// with no dimension list at all is treated as a single still frame.</summary>
        private static Guid FirstFrameDimension(nint image)
        {
            if (GdiPlus.GdipImageGetFrameDimensionsCount(image, out var count) != Ok || count == 0)
            {
                return Guid.Empty;
            }

            var dimensions = new Guid[count];
            return GdiPlus.GdipImageGetFrameDimensionsList(image, dimensions, count) == Ok
                ? dimensions[0]
                : Guid.Empty;
        }

        private static int FrameCountOf(nint image, ref Guid dimension)
        {
            if (dimension == Guid.Empty
                || GdiPlus.GdipImageGetFrameCount(image, ref dimension, out var frames) != Ok
                || frames == 0)
            {
                return 1;
            }

            return (int)frames;
        }

        /// <summary>
        /// WPF's frame-delay read (<c>OverlayService.cs:1542-1549</c>): property <c>0x5100</c>, the
        /// first four bytes as an <c>int</c> of hundredths, through
        /// <see cref="SpiralFrameDelay.FromHundredths"/>. Anything missing or short is the default —
        /// upstream's <c>catch</c> around the same read.
        /// </summary>
        private static TimeSpan ReadFrameDelay(nint image)
        {
            if (GdiPlus.GdipGetPropertyItemSize(image, PropertyTagFrameDelay, out var size) != Ok
                || size < Marshal.SizeOf<PropertyItem>() + sizeof(int))
            {
                return SpiralFrameDelay.Default;
            }

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GdiPlus.GdipGetPropertyItem(image, PropertyTagFrameDelay, size, buffer) != Ok)
                {
                    return SpiralFrameDelay.Default;
                }

                var item = Marshal.PtrToStructure<PropertyItem>(buffer);
                if (item.Value == 0 || item.Length < sizeof(int))
                {
                    return SpiralFrameDelay.Default;
                }

                return SpiralFrameDelay.FromHundredths(Marshal.ReadInt32(item.Value));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>GDI+ <c>PropertyItem</c>: id, byte length, type, and a pointer into the same
        /// buffer this structure was read from.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyItem
        {
            public int Id;
            public int Length;
            public short Type;
            public nint Value;
        }

        private static class GdiPlus
        {
            [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
            internal static extern int GdipLoadImageFromFile(
                [MarshalAs(UnmanagedType.LPWStr)] string filename, out nint image);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipCreateBitmapFromScan0(
                int width, int height, int stride, int format, nint scan0, out nint bitmap);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipGetImageWidth(nint image, out uint width);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipGetImageHeight(nint image, out uint height);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipImageGetFrameDimensionsCount(nint image, out uint count);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipImageGetFrameDimensionsList(
                nint image, [Out] Guid[] dimensionIds, uint count);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipImageGetFrameCount(nint image, ref Guid dimensionId, out uint count);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipImageSelectActiveFrame(
                nint image, ref Guid dimensionId, uint frameIndex);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipGetPropertyItemSize(nint image, int propertyId, out uint size);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipGetPropertyItem(nint image, int propertyId, uint size, nint buffer);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipSetInterpolationMode(nint graphics, int mode);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipGraphicsClear(nint graphics, uint argb);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipDrawImageRectRectI(
                nint graphics, nint image,
                int dstX, int dstY, int dstWidth, int dstHeight,
                int srcX, int srcY, int srcWidth, int srcHeight,
                int srcUnit, nint imageAttributes, nint callback, nint callbackData);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipFlush(nint graphics, int intention);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipDeleteGraphics(nint graphics);

            [DllImport("gdiplus.dll")]
            internal static extern int GdipDisposeImage(nint image);
        }
    }
}
