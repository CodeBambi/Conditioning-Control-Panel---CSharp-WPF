namespace CcpClient.Desktop.Video;

/// <summary>
/// One decoded picture: top-down, 32 bits per pixel, <b>B,G,R,X</b> byte order — deliberately the
/// same layout as <c>Overlay.OverlayFrame</c>, so a reader who knows one knows the other and a
/// future unification is a refactor with no behaviour in it.
///
/// <para><b>Top-down is a NORMALISATION, not an assumption, and the measurement is why.</b> Media
/// Foundation hands RGB32 back with <c>MF_MT_DEFAULT_STRIDE = -1280</c> for the port's own fixture
/// media (SP-111 plan.md §0, Q3): the OS's buffer is BOTTOM-UP. A presenter that ignored the sign
/// would blit every video upside down, and no solid-colour fixture could ever catch it — which is
/// exactly why the decoder flips into this type instead of passing the OS's orientation through.
/// <see cref="Sample"/> below is what a read-back compares against, and it would compare a mirrored
/// picture against itself quite happily.</para>
/// </summary>
public sealed class VideoFrame
{
    /// <summary>B, G, R and one padding byte.</summary>
    public const int BytesPerPixel = 4;

    private readonly byte[] _pixels;

    /// <param name="width">Picture width in pixels; must be positive.</param>
    /// <param name="height">Picture height in pixels; must be positive.</param>
    /// <param name="pixels">Exactly <c>width * height * 4</c> bytes, TOP row first, B,G,R,X per
    /// pixel. Taken by reference and never copied: one of these exists per displayed frame.</param>
    public VideoFrame(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
        ArgumentNullException.ThrowIfNull(pixels);
        var expected = (long)width * height * BytesPerPixel;
        if (pixels.LongLength != expected)
        {
            throw new ArgumentException(
                $"a video frame for {width}x{height} needs exactly {expected} bytes (top-down BGRX), "
                + $"but {pixels.LongLength} were given",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes per row. No padding: a 32bpp DIB row is always 4-byte aligned already.</summary>
    public int Stride => Width * BytesPerPixel;

    /// <summary>The buffer itself, for the one consumer that blits it.</summary>
    public byte[] Pixels => _pixels;

    /// <summary>The colour at a point, as the OS's own <c>COLORREF</c> (<c>0x00BBGGRR</c>), so it
    /// can be compared directly with what a read-back reports.</summary>
    public uint ColourAt(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        var offset = (y * Stride) + (x * BytesPerPixel);
        return (uint)((_pixels[offset] << 16) | (_pixels[offset + 1] << 8) | _pixels[offset + 2]);
    }

    /// <summary>
    /// A cheap, orientation-sensitive fingerprint of the picture: the colours at nine fixed
    /// fractions of the frame, folded together.
    ///
    /// <para><b>Why a sample and not a hash of the buffer.</b> This runs once per presented frame,
    /// on the surface thread, beside a blit — a full hash of a 4K frame there would make the
    /// capability's own read-back the most expensive thing in the session. Nine points at
    /// asymmetric fractions distinguish two different pictures, a picture from its vertical mirror,
    /// and a picture from a solid fill, which is everything this port asks a fingerprint to
    /// do.</para>
    ///
    /// <para><b>What it does NOT do</b>, said out loud: it is not a checksum. Two different frames
    /// CAN share a sample, and a fact that needs "these are different pictures" asserts on the
    /// colours themselves rather than on this.</para>
    /// </summary>
    public uint Sample()
    {
        unchecked
        {
            var value = 2166136261u;
            foreach (var (fx, fy) in SamplePoints)
            {
                var x = Math.Min(Width - 1, (int)(Width * fx));
                var y = Math.Min(Height - 1, (int)(Height * fy));
                value = (value ^ ColourAt(x, y)) * 16777619u;
            }

            return value;
        }
    }

    /// <summary>A frame of one colour — what a fixture and a letterbox both need.</summary>
    public static VideoFrame Solid(int width, int height, byte blue, byte green, byte red)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);

        var pixels = new byte[width * height * BytesPerPixel];
        for (var i = 0; i < pixels.Length; i += BytesPerPixel)
        {
            pixels[i] = blue;
            pixels[i + 1] = green;
            pixels[i + 2] = red;
            pixels[i + 3] = 0xFF;
        }

        return new VideoFrame(width, height, pixels);
    }

    /// <summary>
    /// Nine fractions, spread over the picture and deliberately not on a lattice.
    ///
    /// <para><b>Their ASYMMETRY is not what makes the fingerprint tell a picture from its own
    /// mirror, and the mutation sweep is what established that</b> (SP-111 record §4, M-av). The
    /// fold above is ORDER-dependent, so a mirrored picture yields a different SEQUENCE of colours
    /// and therefore a different value even from a mirror-invariant point set. An earlier version of
    /// this comment claimed the asymmetry was load-bearing; a mutation that made the set exactly
    /// mirror-invariant survived, which disproves it. The fractions are kept because spreading the
    /// samples is still worth having, and the claim is corrected rather than the code.</para>
    /// </summary>
    private static readonly (double X, double Y)[] SamplePoints =
    [
        (0.11, 0.13), (0.50, 0.09), (0.87, 0.17),
        (0.19, 0.47), (0.50, 0.50), (0.79, 0.53),
        (0.13, 0.83), (0.50, 0.91), (0.91, 0.71),
    ];
}
