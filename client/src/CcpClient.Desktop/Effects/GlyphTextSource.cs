using System.Runtime.InteropServices;
using CcpClient.Desktop.Glyph;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// Turns one word into the per-pixel-alpha pixels a glyph surface composites.
///
/// <para>A seam for the same reason <see cref="ISubliminalFrameSource"/> is one: the draw path can
/// be proven with a synthetic frame and no rasteriser at all, and a build that cannot raster text is
/// an ORDINARY outcome — no surface, no exception on a timer thread.</para>
/// </summary>
public interface IGlyphTextSource
{
    /// <summary>
    /// Measure the word at this presentation's size, in physical pixels, WITHOUT rasterising it.
    /// The motion needs the size every time the word changes, and a build with no rasteriser still
    /// has to answer.
    /// </summary>
    (int Width, int Height) Measure(string text, BouncingTextPresentation presentation);

    /// <summary>
    /// Render <paramref name="text"/> as a frame of exactly <paramref name="width"/> ×
    /// <paramref name="height"/> premultiplied BGRA pixels. Returns null — never throws — when this
    /// build has no rasteriser, the raster failed, or the result carries no provable ink.
    /// </summary>
    GlyphFrame? Render(
        string text, uint colour, int width, int height, BouncingTextPresentation presentation);
}

/// <summary>
/// The product rasteriser: GDI+ drawing into a <b>premultiplied</b> 32bpp ARGB bitmap whose bytes
/// are the frame's own buffer.
///
/// <para><b>Why premultiplied is asked for at the GDI+ level rather than converted afterwards.</b>
/// <c>PixelFormat32bppPARGB</c> makes GDI+ produce exactly the byte layout
/// <c>UpdateLayeredWindow</c> requires, so the antialiased edge of every glyph — which is where the
/// per-pixel alpha lives and the whole reason this module needed a new capability — arrives correct
/// without a second pass over the buffer. Converting from straight alpha afterwards would round
/// every edge pixel twice.</para>
///
/// <para><b>What it reproduces</b>, from <c>Services/Subliminal/BouncingTextService.cs</c>: bold
/// text at the size the dial computes (<c>:975</c>, <c>:983</c>), the logo's current colour as the
/// fill (<c>:908-923</c>), and either a black stroke round the glyph (<c>:867-880</c>, the outline
/// style) or a soft dark shadow behind it (<c>:995-1000</c>, the default drop-shadow style). The
/// background is <b>fully transparent everywhere the glyph is not</b> — which is the entire
/// difference from <see cref="GdiPlusSubliminalFrameSource"/>, whose card is opaque black by
/// design.</para>
///
/// <para><b>The font is not WPF's.</b> WPF measures with Segoe UI (<c>:283</c>) and renders with the
/// element's inherited family. This uses Arial, which is the family the port's other rasteriser
/// already uses and the one this build can rely on. A recorded divergence, not an accident.</para>
/// </summary>
public sealed class GdiPlusGlyphTextSource : IGlyphTextSource
{
    /// <summary>The family this build renders with. See the class remarks for why it is not
    /// upstream's.</summary>
    public const string FontFamily = "Arial";

    /// <summary>
    /// The transparent margin added on every side, in pixels, so an antialiased edge, a stroke or a
    /// shadow is never clipped by the surface rectangle.
    ///
    /// <para>It also guarantees the frame has fully-transparent pixels at known positions, which is
    /// what the surface's own read-back samples to catch an opaque plate composited where the glyph
    /// should have holes.</para>
    /// </summary>
    public const int Margin = 12;

    /// <summary>GDI+ <c>FontStyleBold</c>, which is WPF's <c>FontWeights.Bold</c>
    /// (<c>BouncingTextService.cs:974</c>).</summary>
    private const int FontStyleBold = 1;

    /// <summary>GDI+ <c>UnitPixel</c>.</summary>
    private const int UnitPixel = 2;

    /// <summary>GDI+ <c>StringFormatFlagsNoWrap</c>: a long word overflows rather than wrapping,
    /// which is what WPF's infinite-width measure does.</summary>
    private const int StringFormatFlagsNoWrap = 0x1000;

    /// <summary>GDI+ <c>TextRenderingHintAntiAlias</c>. <b>Not ClearType</b>: subpixel antialiasing
    /// writes colour fringes that are wrong over an unknown background, and a per-pixel-alpha
    /// surface has no known background by construction.</summary>
    private const int TextRenderingHintAntiAlias = 4;

    /// <summary>GDI+ <c>SmoothingModeAntiAlias</c>, for the outline path.</summary>
    private const int SmoothingModeAntiAlias = 4;

    /// <summary>GDI+ <c>PixelFormat32bppPARGB</c> — 32 bits, alpha, PREMULTIPLIED. The one constant
    /// that makes this file different from its opaque sibling.</summary>
    private const int PixelFormat32bppPargb = 0x000E200B;

    private const int Ok = GdiPlusRuntime.Ok;

    /// <inheritdoc/>
    public (int Width, int Height) Measure(string text, BouncingTextPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (string.IsNullOrEmpty(text) || !GdiPlusRuntime.Available)
        {
            return (0, 0);
        }

        var size = MeasureCore(text, presentation.FontSize);
        return size is null
            ? (0, 0)
            : (size.Value.Width + (2 * Margin), size.Value.Height + (2 * Margin));
    }

    /// <inheritdoc/>
    public GlyphFrame? Render(
        string text, uint colour, int width, int height, BouncingTextPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (string.IsNullOrEmpty(text) || width <= 0 || height <= 0 || !GdiPlusRuntime.Available)
        {
            return null;
        }

        var pixels = new byte[(long)width * height * GlyphFrame.BytesPerPixel];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var stride = width * GlyphFrame.BytesPerPixel;
            if (GdiPlus.GdipCreateBitmapFromScan0(
                    width, height, stride, PixelFormat32bppPargb, pinned.AddrOfPinnedObject(), out var bitmap) != Ok
                || bitmap == 0)
            {
                return null;
            }

            try
            {
                if (!DrawInto(bitmap, text, colour, width, height, presentation))
                {
                    return null;
                }
            }
            finally
            {
                GdiPlus.GdipDisposeImage(bitmap);
            }
        }
        finally
        {
            pinned.Free();
        }

        // GDI+ is asked for premultiplied output and gives it, but the frame type is the ONLY place
        // the invariant is checked at all (the operating system accepts a wrong buffer silently), so
        // a ROUNDING artefact is repaired here rather than thrown at a caller who cannot act on it.
        //
        // The repair is bounded at ONE, and the bound is the whole point. The sweep that produced
        // this file's facts found that an UNBOUNDED clamp silently converts a straight-alpha buffer
        // into a wrong-but-legal premultiplied one: changing the pixel-format constant to
        // PixelFormat32bppARGB was undetectable, because every over-bright pixel was simply clamped
        // down and the frame constructed fine. A one-unit repair fixes what a premultiplying
        // rasteriser's own rounding can produce and REFUSES anything larger, which is the only
        // difference between repairing a rounding error and hiding a format error.
        if (!TryRepairRounding(pixels))
        {
            return null;
        }

        var frame = new GlyphFrame(width, height, pixels);

        // A raster that produced no fully-opaque non-black pixel cannot be proven to have reached
        // the compositor at all, so it is refused HERE, where the caller's response is simply "no
        // frame this tick" rather than a capability refusal it would have to explain to a user.
        return frame.HasProvableInk ? frame : null;
    }

    private static bool DrawInto(
        nint bitmap, string text, uint colour, int width, int height, BouncingTextPresentation presentation)
    {
        if (GdiPlus.GdipGetImageGraphicsContext(bitmap, out var graphics) != Ok || graphics == 0)
        {
            return false;
        }

        var family = (nint)0;
        var font = (nint)0;
        var format = (nint)0;
        var fill = (nint)0;
        var shadow = (nint)0;
        var stroke = (nint)0;
        try
        {
            // Fully transparent everywhere the glyph is not. This single call is what makes the
            // module possible: an opaque clear here is the black screen D83 refused to ship.
            if (GdiPlus.GdipGraphicsClear(graphics, 0x00000000) != Ok
                || GdiPlus.GdipCreateFontFamilyFromName(FontFamily, 0, out family) != Ok || family == 0
                || GdiPlus.GdipCreateFont(family, presentation.FontSize, FontStyleBold, UnitPixel, out font) != Ok
                || font == 0
                || GdiPlus.GdipCreateStringFormat(StringFormatFlagsNoWrap, 0, out format) != Ok || format == 0
                || GdiPlus.GdipCreateSolidFill(ToArgb(colour, 0xFF), out fill) != Ok || fill == 0)
            {
                return false;
            }

            GdiPlus.GdipSetTextRenderingHint(graphics, TextRenderingHintAntiAlias);
            GdiPlus.GdipSetSmoothingMode(graphics, SmoothingModeAntiAlias);

            var box = new RectF(Margin, Margin, width - (2 * Margin), height - (2 * Margin));

            if (presentation.Outline)
            {
                // WPF's outline style is a black stroke whose thickness scales with the font
                // (:874). Approximated with eight offset copies at that radius, drawn UNDER the
                // fill, because GDI+'s flat API has no text-path stroke that does not require
                // building a GraphicsPath per frame.
                var radius = Math.Max(2, presentation.FontSize / 22);
                if (GdiPlus.GdipCreateSolidFill(0xFF000000, out stroke) != Ok || stroke == 0)
                {
                    return false;
                }

                for (var i = 0; i < OutlineOffsets.Length; i++)
                {
                    var (offsetX, offsetY) = OutlineOffsets[i];
                    var ring = new RectF(
                        box.X + (offsetX * radius), box.Y + (offsetY * radius), box.Width, box.Height);
                    if (GdiPlus.GdipDrawString(graphics, text, text.Length, font, ref ring, format, stroke) != Ok)
                    {
                        return false;
                    }
                }
            }
            else
            {
                // WPF's default is a black drop shadow, blur 10, depth 3 (:995-1000). A blur is not
                // available in the flat API, so this is three decreasing-alpha copies offset down
                // and right — the same read as the shadow, produced by the same additive
                // compositing GDI+ is already doing.
                for (var i = 0; i < ShadowSteps.Length; i++)
                {
                    var (offset, alpha) = ShadowSteps[i];
                    if (GdiPlus.GdipCreateSolidFill(ToArgb(0x000000, alpha), out shadow) != Ok || shadow == 0)
                    {
                        return false;
                    }

                    var cast = new RectF(box.X + offset, box.Y + offset, box.Width, box.Height);
                    var drawn = GdiPlus.GdipDrawString(graphics, text, text.Length, font, ref cast, format, shadow);
                    GdiPlus.GdipDeleteBrush(shadow);
                    shadow = 0;
                    if (drawn != Ok)
                    {
                        return false;
                    }
                }
            }

            if (GdiPlus.GdipDrawString(graphics, text, text.Length, font, ref box, format, fill) != Ok)
            {
                return false;
            }

            GdiPlus.GdipFlush(graphics, intention: 1);
            return true;
        }
        finally
        {
            if (stroke != 0)
            {
                GdiPlus.GdipDeleteBrush(stroke);
            }

            if (shadow != 0)
            {
                GdiPlus.GdipDeleteBrush(shadow);
            }

            if (fill != 0)
            {
                GdiPlus.GdipDeleteBrush(fill);
            }

            if (format != 0)
            {
                GdiPlus.GdipDeleteStringFormat(format);
            }

            if (font != 0)
            {
                GdiPlus.GdipDeleteFont(font);
            }

            if (family != 0)
            {
                GdiPlus.GdipDeleteFontFamily(family);
            }

            GdiPlus.GdipDeleteGraphics(graphics);
        }
    }

    /// <summary>The eight offsets the outline copies are drawn at, in units of the stroke
    /// radius.</summary>
    private static readonly (float X, float Y)[] OutlineOffsets =
    [
        (-1, -1), (1, -1), (-1, 1), (1, 1),
        (0, -1.3f), (0, 1.3f), (-1.3f, 0), (1.3f, 0),
    ];

    /// <summary>The drop shadow's three passes: offset in pixels, and the alpha of that pass.</summary>
    private static readonly (float Offset, byte Alpha)[] ShadowSteps =
    [
        (5f, 0x30), (4f, 0x40), (3f, 0x60),
    ];

    /// <summary>A <c>COLORREF</c> (<c>0x00BBGGRR</c>) plus an alpha, as the <c>0xAARRGGBB</c> GDI+
    /// wants.</summary>
    private static uint ToArgb(uint colourRef, byte alpha) =>
        ((uint)alpha << 24) | ((colourRef & 0xFF) << 16) | (colourRef & 0xFF00) | ((colourRef >> 16) & 0xFF);

    /// <summary>
    /// The largest overage this repairs. One unit is what a premultiplying rasteriser's own rounding
    /// can produce on an antialiased edge pixel; anything larger is a different bug and is refused.
    /// </summary>
    public const int MaxRoundingOverage = 1;

    /// <summary>
    /// Repairs a colour channel that rounded up to <see cref="MaxRoundingOverage"/> above its own
    /// alpha, and returns FALSE when any channel is further over than that.
    ///
    /// <para>GDI+ produces premultiplied output for a PARGB bitmap, and its own rounding can leave a
    /// channel one over on an antialiased edge pixel. <see cref="GlyphFrame"/> treats that as a
    /// caller bug and throws, correctly — the invariant exists because the operating system will not
    /// check it — so the one place that knows the bytes came from a premultiplying rasteriser is the
    /// one place allowed to repair them.</para>
    ///
    /// <para><b>And it is the one place that must NOT hide a format error.</b> An unbounded clamp
    /// turns a straight-alpha buffer into a wrong-but-legal premultiplied one, silently: the sweep
    /// proved it by changing the pixel-format constant and watching nothing fail. The bound is what
    /// separates the two cases.</para>
    /// </summary>
    private static bool TryRepairRounding(byte[] pixels)
    {
        for (var i = 0; i + 3 < pixels.Length; i += GlyphFrame.BytesPerPixel)
        {
            var alpha = pixels[i + 3];
            for (var channel = 0; channel < 3; channel++)
            {
                var value = pixels[i + channel];
                if (value <= alpha)
                {
                    continue;
                }

                if (value - alpha > MaxRoundingOverage)
                {
                    return false;
                }

                pixels[i + channel] = alpha;
            }
        }

        return true;
    }

    private static (int Width, int Height)? MeasureCore(string text, int fontSize)
    {
        if (GdiPlus.GdipCreateBitmapFromScan0(1, 1, 0, PixelFormat32bppPargb, 0, out var bitmap) != Ok
            || bitmap == 0)
        {
            return null;
        }

        try
        {
            if (GdiPlus.GdipGetImageGraphicsContext(bitmap, out var graphics) != Ok || graphics == 0)
            {
                return null;
            }

            var family = (nint)0;
            var font = (nint)0;
            var format = (nint)0;
            try
            {
                if (GdiPlus.GdipCreateFontFamilyFromName(FontFamily, 0, out family) != Ok || family == 0
                    || GdiPlus.GdipCreateFont(family, fontSize, FontStyleBold, UnitPixel, out font) != Ok || font == 0
                    || GdiPlus.GdipCreateStringFormat(StringFormatFlagsNoWrap, 0, out format) != Ok || format == 0)
                {
                    return null;
                }

                // WPF measures with infinite width (:292-300); a large finite box is the flat API's
                // equivalent and keeps a long phrase on one line.
                var layout = new RectF(0, 0, 16384, 16384);
                if (GdiPlus.GdipMeasureString(
                        graphics, text, text.Length, font, ref layout, format,
                        out var bounds, out _, out _) != Ok)
                {
                    return null;
                }

                return ((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height));
            }
            finally
            {
                if (format != 0)
                {
                    GdiPlus.GdipDeleteStringFormat(format);
                }

                if (font != 0)
                {
                    GdiPlus.GdipDeleteFont(font);
                }

                if (family != 0)
                {
                    GdiPlus.GdipDeleteFontFamily(family);
                }

                GdiPlus.GdipDeleteGraphics(graphics);
            }
        }
        finally
        {
            GdiPlus.GdipDisposeImage(bitmap);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectF(float x, float y, float width, float height)
    {
        public float X = x;
        public float Y = y;
        public float Width = width;
        public float Height = height;
    }

    private static class GdiPlus
    {
        [DllImport("gdiplus.dll")]
        internal static extern int GdipCreateBitmapFromScan0(
            int width, int height, int stride, int format, nint scan0, out nint bitmap);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipGraphicsClear(nint graphics, uint argb);

        [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
        internal static extern int GdipCreateFontFamilyFromName(
            [MarshalAs(UnmanagedType.LPWStr)] string name, nint fontCollection, out nint fontFamily);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipCreateFont(
            nint fontFamily, float emSize, int style, int unit, out nint font);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipCreateSolidFill(uint argb, out nint brush);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipCreateStringFormat(int formatAttributes, int language, out nint format);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipSetTextRenderingHint(nint graphics, int mode);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipSetSmoothingMode(nint graphics, int mode);

        [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
        internal static extern int GdipDrawString(
            nint graphics,
            [MarshalAs(UnmanagedType.LPWStr)] string text,
            int length,
            nint font,
            ref RectF layoutRect,
            nint stringFormat,
            nint brush);

        [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
        internal static extern int GdipMeasureString(
            nint graphics,
            [MarshalAs(UnmanagedType.LPWStr)] string text,
            int length,
            nint font,
            ref RectF layoutRect,
            nint stringFormat,
            out RectF boundingBox,
            out int codepointsFitted,
            out int linesFilled);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipFlush(nint graphics, int intention);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipDeleteBrush(nint brush);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipDeleteStringFormat(nint format);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipDeleteFont(nint font);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipDeleteFontFamily(nint fontFamily);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipDeleteGraphics(nint graphics);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipDisposeImage(nint image);
    }
}
