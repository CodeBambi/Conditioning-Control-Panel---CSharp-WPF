using System.Runtime.InteropServices;
using CcpClient.Desktop.Overlay;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// The three colours one subliminal card is drawn in, as ARGB.
///
/// <para><b>Why this is a value on the card rather than three constants on the rasteriser.</b>
/// Upstream reads all three out of settings at the moment it shows a card —
/// <c>ParseColor(App.Settings.Current.SubBackgroundColor, …)</c>,
/// <c>SubTextColor</c>, <c>SubBorderColor</c>, all inside <c>ShowSubliminalVisuals</c>
/// (<c>Services/Subliminal/SubliminalService.cs:622-624</c>) — so a colour changed between two
/// subliminals takes effect on the next one. Compiling them in reproduced upstream's DEFAULTS and
/// none of its configurability; carrying them on the card keeps the per-show read.</para>
///
/// <para><b>Alpha is forced opaque.</b> Upstream persists <c>#RRGGBB</c> with no alpha channel
/// (<c>Dialogs/ColorEditorDialog.xaml.cs:165</c> writes <c>$"#{R:X2}{G:X2}{B:X2}"</c>), and this
/// surface has no per-pixel alpha to spend it on anyway (<see cref="Overlay.OverlayFrame"/>).</para>
/// </summary>
/// <param name="BackgroundArgb">Fills the whole card before anything is drawn.</param>
/// <param name="TextArgb">The phrase itself, drawn last and therefore on top.</param>
/// <param name="OutlineArgb">The eight offset copies drawn UNDER the phrase.</param>
public readonly record struct SubliminalPalette(uint BackgroundArgb, uint TextArgb, uint OutlineArgb)
{
    /// <summary>WPF's <c>SubBackgroundColor</c> default, <c>#000000</c>
    /// (<c>CCP.Core/Models/AppSettings.cs:1352</c>).</summary>
    public const uint DefaultBackgroundArgb = 0xFF000000;

    /// <summary>WPF's <c>SubTextColor</c> default, magenta <c>#FF00FF</c>
    /// (<c>CCP.Core/Models/AppSettings.cs:1366</c>).</summary>
    public const uint DefaultTextArgb = 0xFFFF00FF;

    /// <summary>WPF's <c>SubBorderColor</c> default, white <c>#FFFFFF</c>
    /// (<c>CCP.Core/Models/AppSettings.cs:1380</c>).</summary>
    public const uint DefaultOutlineArgb = 0xFFFFFFFF;

    /// <summary>The card upstream ships: black behind magenta behind a white outline. Every one of
    /// the three is upstream's own field initializer, not a choice made here.</summary>
    public static readonly SubliminalPalette Default =
        new(DefaultBackgroundArgb, DefaultTextArgb, DefaultOutlineArgb);

    /// <summary>
    /// The palette a user's three persisted hex strings ask for, each falling back to its own
    /// shipped default — which is exactly the shape of upstream's three calls, whose second
    /// argument is the per-setting fallback colour (<c>SubliminalService.cs:622-624</c>).
    ///
    /// <para><b>The parser is <see cref="PinkFilterColour.TryParseHex"/></b>, already ported from
    /// WPF's <c>TryParseHexColor</c>, rather than a second one. It is STRICTER than the
    /// <c>ColorConverter.ConvertFromString</c> upstream reaches for here: a three-digit
    /// <c>#RGB</c>, an eight-digit <c>#AARRGGBB</c> and a named colour all fall back instead of
    /// parsing. That costs nothing for any value the product can produce, because the only writer
    /// of these settings is the colour editor and it emits six digits and a hash and nothing else
    /// (<c>ColorEditorDialog.xaml.cs:165</c>); a hand-edited exotic value lands on upstream's own
    /// documented fallback rather than on a wrong colour.</para>
    /// </summary>
    public static SubliminalPalette From(string? background, string? text, string? outline) =>
        new(Argb(background, DefaultBackgroundArgb),
            Argb(text, DefaultTextArgb),
            Argb(outline, DefaultOutlineArgb));

    private static uint Argb(string? hex, uint fallback) =>
        PinkFilterColour.TryParseHex(hex, out var rgb)
            ? 0xFF000000u | ((uint)rgb.Red << 16) | ((uint)rgb.Green << 8) | rgb.Blue
            : fallback;
}

/// <summary>
/// Turns one subliminal phrase into the pixels the card puts on screen.
///
/// <para>A seam for the same reason <see cref="IFlashFrameSource"/> is one: the draw path can then
/// be proven with a synthetic frame and no rasteriser at all, and a build that cannot raster text is
/// an ORDINARY outcome — no card, no exception on a timer thread.</para>
/// </summary>
public interface ISubliminalFrameSource
{
    /// <summary>
    /// Render <paramref name="text"/> as one card of exactly <paramref name="width"/> ×
    /// <paramref name="height"/> pixels, in <paramref name="palette"/>. Returns null — never
    /// throws — when this build has no rasteriser or the raster failed.
    /// </summary>
    OverlayFrame? Render(string text, int width, int height, SubliminalPalette palette);
}

/// <summary>
/// The product card rasteriser: GDI+ (<c>gdiplus.dll</c>) drawing straight into the frame buffer.
///
/// <para><b>What it reproduces</b>, from <c>Services/Subliminal/SubliminalService.cs</c>: an opaque
/// background over the whole card (<c>:969-980</c>); Arial Bold at 120 px (<c>:1237-1248</c>); the
/// phrase centred (<c>:777-787</c>); and an eight-copy outline drawn UNDER the main text at WPF's
/// own offsets (<c>:990-1008</c>). The three colours are the caller's
/// <see cref="SubliminalPalette"/>, which upstream re-reads from settings on every show
/// (<c>:622-624</c>).</para>
///
/// <para><b>The outline is drawn first and the phrase second</b>, which is the order that makes it
/// an outline rather than a smear — WPF adds the eight offset copies to the canvas before the main
/// text (<c>:996-1008</c>), so the later child paints over them. Reversing these two loops is
/// visible and is what the card's pixel facts pin.</para>
///
/// <para><b>No per-pixel alpha.</b> The frame is B,G,R,X over an opaque background and the card is
/// composited at the surface's one uniform <c>LWA_ALPHA</c>, which is the opacity dial
/// (<c>CCP.Core/Models/AppSettings.cs:1282</c>). That is the same constraint recorded for flashes
/// (D57), it costs the same thing — the text is not individually translucent, the whole card is —
/// and it is why <c>SubBackgroundTransparent</c> is refused rather than honoured
/// (<see cref="Session.EffectReasonCodes.SubliminalOpaqueBackgroundOnly"/>).</para>
/// </summary>
/// <param name="fontFamily">The family the card is drawn in. Defaults to WPF's; a caller passes a
/// different one only to drive the FAILED-FONT path, which upstream cannot reach at all because its
/// <c>FontFamily</c> is a literal at the call site.</param>
public sealed class GdiPlusSubliminalFrameSource(string fontFamily = GdiPlusSubliminalFrameSource.FontFamily)
    : ISubliminalFrameSource
{
    /// <summary>WPF's font family for a subliminal (<c>SubliminalService.cs:1244</c>).</summary>
    public const string FontFamily = "Arial";

    /// <summary>WPF's font size (<c>SubliminalService.cs:988</c>, <c>:1242</c>).</summary>
    public const float FontSizePixels = 120f;

    /// <summary>WPF's eight outline offsets, in its own order (<c>SubliminalService.cs:992-996</c>).</summary>
    public static readonly (float X, float Y)[] OutlineOffsets =
    [
        (-3, -3), (3, -3), (-3, 3), (3, 3),
        (0, -4), (0, 4), (-4, 0), (4, 0),
    ];

    /// <summary>GDI+ <c>FontStyleBold</c>, which is WPF's <c>FontWeights.Bold</c> (<c>:1243</c>).</summary>
    private const int FontStyleBold = 1;

    /// <summary>GDI+ <c>UnitPixel</c>: the font size is in device pixels, as the frame is.</summary>
    private const int UnitPixel = 2;

    /// <summary>GDI+ <c>StringAlignmentCenter</c>, on both axes.</summary>
    private const int StringAlignmentCenter = 1;

    /// <summary>GDI+ <c>StringFormatFlagsNoWrap</c>: WPF measures the phrase with infinite width
    /// (<c>SubliminalService.cs:778</c>), so a long phrase overflows rather than wrapping.</summary>
    private const int StringFormatFlagsNoWrap = 0x1000;

    /// <summary>GDI+ <c>TextRenderingHintAntiAlias</c>.</summary>
    private const int TextRenderingHintAntiAlias = 4;

    /// <summary>32bpp, no alpha channel in the target: the frame is opaque over the background.</summary>
    private const int PixelFormat32bppRgb = 0x00022009;

    private const int Ok = GdiPlusRuntime.Ok;

    /// <inheritdoc/>
    public OverlayFrame? Render(string text, int width, int height, SubliminalPalette palette)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        if (width <= 0 || height <= 0 || !GdiPlusRuntime.Available)
        {
            return null;
        }

        var pixels = new byte[(long)width * height * OverlayFrame.BytesPerPixel];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            // Positive stride = top-down rows, which is the order OverlayFrame declares and the
            // order the surface's top-down DIB section expects. Nothing flips a row anywhere.
            var stride = width * OverlayFrame.BytesPerPixel;
            if (GdiPlus.GdipCreateBitmapFromScan0(
                    width, height, stride, PixelFormat32bppRgb, pinned.AddrOfPinnedObject(), out var bitmap) != Ok
                || bitmap == 0)
            {
                return null;
            }

            try
            {
                return DrawInto(bitmap, text, width, height, palette)
                    ? new OverlayFrame(width, height, pixels)
                    : null;
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
    }

    private bool DrawInto(nint bitmap, string text, int width, int height, SubliminalPalette palette)
    {
        if (GdiPlus.GdipGetImageGraphicsContext(bitmap, out var graphics) != Ok || graphics == 0)
        {
            return false;
        }

        var family = (nint)0;
        var font = (nint)0;
        var format = (nint)0;
        var outlineBrush = (nint)0;
        var textBrush = (nint)0;
        try
        {
            if (GdiPlus.GdipGraphicsClear(graphics, palette.BackgroundArgb) != Ok
                || GdiPlus.GdipCreateFontFamilyFromName(fontFamily, 0, out family) != Ok || family == 0
                || GdiPlus.GdipCreateFont(family, FontSizePixels, FontStyleBold, UnitPixel, out font) != Ok || font == 0
                || GdiPlus.GdipCreateStringFormat(StringFormatFlagsNoWrap, 0, out format) != Ok || format == 0
                || GdiPlus.GdipSetStringFormatAlign(format, StringAlignmentCenter) != Ok
                || GdiPlus.GdipSetStringFormatLineAlign(format, StringAlignmentCenter) != Ok
                || GdiPlus.GdipCreateSolidFill(palette.OutlineArgb, out outlineBrush) != Ok || outlineBrush == 0
                || GdiPlus.GdipCreateSolidFill(palette.TextArgb, out textBrush) != Ok || textBrush == 0)
            {
                return false;
            }

            GdiPlus.GdipSetTextRenderingHint(graphics, TextRenderingHintAntiAlias);

            // The outline first, the phrase over it (SubliminalService.cs:996-1008). Centring is the
            // string format's, so an offset layout rectangle moves the glyphs by exactly that offset
            // — WPF's `centerX - width/2 + ox` (:779-780) with the arithmetic done by GDI+.
            foreach (var (offsetX, offsetY) in OutlineOffsets)
            {
                var box = new RectF(offsetX, offsetY, width, height);
                if (GdiPlus.GdipDrawString(graphics, text, text.Length, font, ref box, format, outlineBrush) != Ok)
                {
                    return false;
                }
            }

            var main = new RectF(0, 0, width, height);
            if (GdiPlus.GdipDrawString(graphics, text, text.Length, font, ref main, format, textBrush) != Ok)
            {
                return false;
            }

            GdiPlus.GdipFlush(graphics, intention: 1);
            return true;
        }
        finally
        {
            if (textBrush != 0)
            {
                GdiPlus.GdipDeleteBrush(textBrush);
            }

            if (outlineBrush != 0)
            {
                GdiPlus.GdipDeleteBrush(outlineBrush);
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
        internal static extern int GdipSetStringFormatAlign(nint format, int align);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipSetStringFormatLineAlign(nint format, int align);

        [DllImport("gdiplus.dll")]
        internal static extern int GdipSetTextRenderingHint(nint graphics, int mode);

        [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
        internal static extern int GdipDrawString(
            nint graphics,
            [MarshalAs(UnmanagedType.LPWStr)] string text,
            int length,
            nint font,
            ref RectF layoutRect,
            nint stringFormat,
            nint brush);

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
