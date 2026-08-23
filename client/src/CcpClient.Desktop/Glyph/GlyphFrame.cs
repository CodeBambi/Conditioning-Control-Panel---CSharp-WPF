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
/// (<c>wpf-surface-reachability.md</c> D83 @ 7527243e7). The two frame types are deliberately NOT merged —
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
    /// <c>src·k/255 + dst·(255−α)/255</c> where <c>α = a·k/255</c>, evaluated EXACTLY and rounded
    /// to nearest exactly ONCE, at the end, per channel.
    ///
    /// <para><b>The single rounding is the whole model, and it was arrived at the hard way.</b> An
    /// earlier draft rounded each of the three terms separately. That version reproduced the screen
    /// at seven of eight measured points and was <b>one unit low in the red channel of the eighth</b>
    /// (predicting <c>0xD65E47</c> where the screen holds <c>0xD65E48</c>), and the record around it
    /// claimed no reading of the formula reproduced the measurement. <b>That claim was false.</b>
    /// The same formula with one final rounding reproduces all eight: α = 128·128/255 = 64.25 lost
    /// its .25 and 10·191/255 = 7.48 lost its .48, and the two discarded fractions sum to 0.73 —
    /// enough to carry 71.73 up to 72. Rounding once keeps that.</para>
    ///
    /// <para><b>Why this is not fitting the oracle to the data.</b> It is the SAME formula; only the
    /// number of roundings changed, from three to the one that belongs there. It is exact at
    /// <paramref name="constantAlpha"/> = 255 (where both models agree and both are exact) AND at
    /// 128, over every sampled point, with no tolerance anywhere — which is what lets the harness
    /// assert equality rather than nearness, so a genuine one-unit-per-channel regression at a
    /// non-255 dial setting still reds.</para>
    ///
    /// <para><b>What remains unmeasured:</b> only the constant-alpha values 255 and 128 have been
    /// read off a screen at all. Every other setting of the dial is this arithmetic, unchecked.</para>
    /// </summary>
    /// <param name="x">Sample column.</param>
    /// <param name="y">Sample row.</param>
    /// <param name="background">The background's <c>COLORREF</c> (<c>0x00BBGGRR</c>).</param>
    /// <param name="constantAlpha">The surface's uniform multiplier, 0-255.</param>
    public uint CompositeOver(int x, int y, uint background, byte constantAlpha)
    {
        BoundsCheck(x, y);
        var offset = (y * Stride) + (x * BytesPerPixel);

        // Everything is kept over the common denominator 255*255 so no intermediate is rounded.
        var inverse = Denominator - (_pixels[offset + 3] * constantAlpha);

        var blue = Blend(_pixels[offset], constantAlpha, (int)((background >> 16) & 0xFF), inverse);
        var green = Blend(_pixels[offset + 1], constantAlpha, (int)((background >> 8) & 0xFF), inverse);
        var red = Blend(_pixels[offset + 2], constantAlpha, (int)(background & 0xFF), inverse);
        return (uint)((blue << 16) | (green << 8) | red);
    }

    /// <summary>255 * 255: the common denominator both terms are carried over.</summary>
    private const int Denominator = 255 * 255;

    /// <summary>
    /// One channel of the composite, exact until the single final rounding.
    ///
    /// <para><c>(src·k·255 + dst·inverse) / 65025</c>, rounded half-up by doubling rather than by a
    /// floating-point round, so the result is deterministic and has no representation error.</para>
    /// </summary>
    private static int Blend(int source, int constantAlpha, int background, int inverse)
    {
        var numerator = ((long)source * constantAlpha * 255) + ((long)background * inverse);
        var rounded = ((2 * numerator) + Denominator) / (2 * Denominator);
        return (int)Math.Clamp(rounded, 0, 255);
    }

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
