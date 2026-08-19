namespace CcpClient.Desktop.Glyph;

/// <summary>
/// One rectangle of pixels with <b>per-pixel alpha</b>: top-down, 32 bits per pixel, in
/// <b>B,G,R,A</b> byte order, with the colour channels <b>PREMULTIPLIED</b> by the alpha.
///
/// <para><b>How this differs from <see cref="Overlay.OverlayFrame"/>, and why both exist.</b> An
/// overlay frame is B,G,R,<b>X</b>: the fourth byte is padding and the whole rectangle is
/// composited at the window's one uniform <c>LWA_ALPHA</c>. That is right for a flash, which is a
/// rectangle at one opacity with an image filling it. It is wrong for glyphs, where the shape IS
/// the alpha: at the shipped default opacity a transparency-backed text window rendered as an
/// opaque rectangle would put a black screen with text on it in front of the user
/// (<c>wpf-surface-reachability.md</c> D83). The two frame types are deliberately NOT merged —
/// they feed different Win32 mechanisms (<c>BitBlt</c> into a window DC versus
/// <c>UpdateLayeredWindow</c>) that are mutually exclusive on one window, and a single type would
/// invite a caller to hand a padded buffer to a call that reads the fourth byte as alpha.</para>
///
/// <para><b>Why premultiplied, and why this type ENFORCES it.</b> <c>UpdateLayeredWindow</c> with
/// <c>AC_SRC_ALPHA</c> requires premultiplied source pixels. Measured on this machine before this
/// file existed: the operating system <b>accepts a non-premultiplied buffer without complaint</b> —
/// ULW returns TRUE — and simply composites it wrongly, so white at alpha 64 reads back as full
/// white. There is no OS-level error to catch, which means the invariant has to be a property of
/// the type or it is not checked anywhere at all. A channel above its own alpha is therefore a
/// construction-time <see cref="ArgumentException"/>, not a state and not a clamp: it is a caller
/// bug, exactly as <see cref="Overlay.OverlaySurfaceRequest"/> treats an opacity of zero.</para>
///
/// <para><b>Provable ink.</b> The surface can only prove a composite reached the OS by reading it
/// back, and a read-back of the window flattens onto black — so a fully transparent pixel and an
/// opaque black one are the same value there (measured). <see cref="ProvableInk"/> is the set of
/// sample points where the read-back can say something a ghost cannot, and a frame with none of
/// them is refused by the surface rather than silently claimed.</para>
/// </summary>
public sealed class GlyphFrame
{
    /// <summary>B, G, R and a real alpha byte.</summary>
    public const int BytesPerPixel = 4;

    /// <summary>
    /// How many ink points the frame remembers for the surface's read-back. Bounded on purpose: the
    /// confirmation runs on the surface thread once per painted frame, and a full comparison of a
    /// glyph-sized buffer there is a rendering cost paid to learn what a spread sample already
    /// answers. The overlay's own content check is bounded for the same reason.
    /// </summary>
    public const int ProvableInkTarget = 64;

    private readonly byte[] _pixels;
    private readonly (int X, int Y)[] _provableInk;

    /// <param name="width">Frame width in physical pixels; must be positive.</param>
    /// <param name="height">Frame height in physical pixels; must be positive.</param>
    /// <param name="pixels">
    /// Exactly <c>width * height * 4</c> bytes, top row first, B,G,R,A per pixel, colour channels
    /// already multiplied by alpha. Taken by reference and never copied.
    /// </param>
    /// <exception cref="ArgumentException">The buffer is the wrong length, or some pixel has a
    /// colour channel greater than its own alpha (i.e. it is not premultiplied).</exception>
    public GlyphFrame(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
        ArgumentNullException.ThrowIfNull(pixels);
        var expected = (long)width * height * BytesPerPixel;
        if (pixels.LongLength != expected)
        {
            throw new ArgumentException(
                $"a glyph frame for {width}x{height} needs exactly {expected} bytes (top-down premultiplied "
                + $"BGRA), but {pixels.LongLength} were given",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        _pixels = pixels;
        _provableInk = Validate(width, height, pixels);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes per row. No padding: a 32bpp DIB row is always 4-byte aligned already.</summary>
    public int Stride => Width * BytesPerPixel;

    /// <summary>The buffer itself, for the one consumer that copies it into a DIB section.</summary>
    public byte[] Pixels => _pixels;

    /// <summary>
    /// The sample points a read-back can prove something with: fully opaque (alpha 255) and not
    /// black. Empty means the frame is unprovable — see
    /// <see cref="GlyphReasonCodes.GlyphFrameCarriesNoProvableInk"/>.
    /// </summary>
    public IReadOnlyList<(int X, int Y)> ProvableInk => _provableInk;

    /// <summary>True when the surface can confirm this frame reached the OS at all.</summary>
    public bool HasProvableInk => _provableInk.Length > 0;

    /// <summary>The alpha at a point.</summary>
    public byte AlphaAt(int x, int y)
    {
        BoundsCheck(x, y);
        return _pixels[(y * Stride) + (x * BytesPerPixel) + 3];
    }

    /// <summary>
    /// The PREMULTIPLIED colour at a point, as the OS's own <c>COLORREF</c> (<c>0x00BBGGRR</c>), so
    /// it can be compared directly with what a window read-back reports. For a fully opaque pixel
    /// this is simply the colour; for a transparent one it is zero, which is exactly why a
    /// transparent pixel and an opaque black one are indistinguishable in that read-back.
    /// </summary>
    public uint PremultipliedColourAt(int x, int y)
    {
        BoundsCheck(x, y);
        var offset = (y * Stride) + (x * BytesPerPixel);
        return (uint)((_pixels[offset] << 16) | (_pixels[offset + 1] << 8) | _pixels[offset + 2]);
    }

    /// <summary>
    /// What the COMPOSITED DESKTOP should read at a point when this frame is composited at
    /// <paramref name="constantAlpha"/> over a known opaque background: premultiplied source-over,
    /// <c>src + dst·(255−α)/255</c>, with each division ROUNDED TO NEAREST.
    ///
    /// <para><b>The rounding is not a preference; it is the measurement.</b> White at alpha 128 over
    /// B=200 G=40 R=10 reads back <c>0xE49485</c> off this machine's composited desktop. Truncating
    /// gives <c>0xE39484</c> — one low in every channel — and rounding gives exactly what the screen
    /// holds: <c>200·127/255 = 99.6 → 100</c>, so blue is <c>128+100 = 228 = 0xE4</c>. The first
    /// draft of this method truncated and the differential caught it, which is the reading being
    /// used as an oracle rather than as decoration.</para>
    ///
    /// <para>Only <paramref name="constantAlpha"/> = 255 has been measured against a screen. At
    /// lower values the same arithmetic is applied to the source term, and that arm is arithmetic
    /// rather than evidence.</para>
    /// </summary>
    /// <param name="x">Sample column.</param>
    /// <param name="y">Sample row.</param>
    /// <param name="background">The background's <c>COLORREF</c> (<c>0x00BBGGRR</c>).</param>
    /// <param name="constantAlpha">The surface's uniform multiplier, 0-255.</param>
    public uint CompositeOver(int x, int y, uint background, byte constantAlpha)
    {
        BoundsCheck(x, y);
        var offset = (y * Stride) + (x * BytesPerPixel);
        var alpha = Scale(_pixels[offset + 3], constantAlpha);
        var inverse = 255 - alpha;

        var blue = Math.Clamp(Scale(_pixels[offset], constantAlpha) + Scale((int)((background >> 16) & 0xFF), inverse), 0, 255);
        var green = Math.Clamp(Scale(_pixels[offset + 1], constantAlpha) + Scale((int)((background >> 8) & 0xFF), inverse), 0, 255);
        var red = Math.Clamp(Scale(_pixels[offset + 2], constantAlpha) + Scale((int)(background & 0xFF), inverse), 0, 255);
        return (uint)((blue << 16) | (green << 8) | red);
    }

    /// <summary><c>value · factor / 255</c>, rounded to nearest. See <see cref="CompositeOver"/>.</summary>
    private static int Scale(int value, int factor) => ((value * factor) + 127) / 255;

    /// <summary>
    /// A frame of one uniform colour at one uniform alpha, premultiplied here so a caller cannot
    /// get the multiplication wrong. The harness's way to say "this many pixels of exactly this".
    /// </summary>
    public static GlyphFrame Solid(int width, int height, byte blue, byte green, byte red, byte alpha)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);

        var pixels = new byte[width * height * BytesPerPixel];
        for (var i = 0; i < pixels.Length; i += BytesPerPixel)
        {
            pixels[i] = (byte)(blue * alpha / 255);
            pixels[i + 1] = (byte)(green * alpha / 255);
            pixels[i + 2] = (byte)(red * alpha / 255);
            pixels[i + 3] = alpha;
        }

        return new GlyphFrame(width, height, pixels);
    }

    /// <summary>
    /// Build a frame from STRAIGHT (non-premultiplied) BGRA, doing the multiplication here. The
    /// rasteriser that produces premultiplied pixels natively does not use this; a caller that has
    /// straight-alpha bytes does, and the alternative — letting straight bytes through — is the
    /// silently-wrong composite the operating system will not complain about.
    /// </summary>
    public static GlyphFrame FromStraightAlpha(int width, int height, byte[] straight)
    {
        ArgumentNullException.ThrowIfNull(straight);
        var premultiplied = new byte[straight.Length];
        for (var i = 0; i + 3 < straight.Length; i += BytesPerPixel)
        {
            var alpha = straight[i + 3];
            premultiplied[i] = (byte)(straight[i] * alpha / 255);
            premultiplied[i + 1] = (byte)(straight[i + 1] * alpha / 255);
            premultiplied[i + 2] = (byte)(straight[i + 2] * alpha / 255);
            premultiplied[i + 3] = alpha;
        }

        return new GlyphFrame(width, height, premultiplied);
    }

    private void BoundsCheck(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
    }

    /// <summary>
    /// Enforce premultiplication and collect the provable ink in ONE pass. Both are properties of
    /// the same bytes and a second walk of a glyph-sized buffer buys nothing.
    /// </summary>
    private static (int X, int Y)[] Validate(int width, int height, byte[] pixels)
    {
        var stride = width * BytesPerPixel;
        var ink = new List<(int X, int Y)>(ProvableInkTarget);

        // Spread the candidate points over the whole frame rather than taking the first N, which on
        // a text raster would all come from one glyph's first row.
        var step = Math.Max(1, (int)Math.Sqrt((double)width * height / (ProvableInkTarget * 4)));

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * stride) + (x * BytesPerPixel);
                var alpha = pixels[offset + 3];
                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];

                if (blue > alpha || green > alpha || red > alpha)
                {
                    throw new ArgumentException(
                        $"the pixel at {x},{y} is B={blue} G={green} R={red} over alpha {alpha}, which is not "
                        + "premultiplied. UpdateLayeredWindow with AC_SRC_ALPHA requires premultiplied source "
                        + "pixels and the operating system accepts a wrong buffer WITHOUT ERROR (measured), so "
                        + "this is checked here or nowhere",
                        nameof(pixels));
                }

                if (alpha == 255 && (blue | green | red) != 0
                    && ink.Count < ProvableInkTarget
                    && x % step == 0 && y % step == 0)
                {
                    ink.Add((x, y));
                }
            }
        }

        return [.. ink];
    }
}
