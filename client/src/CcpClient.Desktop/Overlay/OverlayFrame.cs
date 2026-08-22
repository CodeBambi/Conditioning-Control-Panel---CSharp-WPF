namespace CcpClient.Desktop.Overlay;

/// <summary>
/// One rectangle of pixels to put on an overlay surface: top-down, 32 bits per pixel, in
/// <b>B,G,R,X</b> byte order.
///
/// <para><b>Why a raw buffer and not an image.</b> The surface is a Win32 top-level window and the
/// mechanism that fills it is a GDI blit; an image type here would drag a decoder, a format and a
/// colour-management opinion into a capability whose whole job is window state. Keeping the frame
/// content-free means the overlay never learns what a flash is — it is handed pixels — and it means
/// the draw path can be proven with a synthetic buffer, with no decoder and no Avalonia runtime
/// anywhere near it.</para>
///
/// <para><b>Byte order, and why the fourth byte is ignored.</b> B,G,R,X is what
/// <c>CreateDIBSection</c> hands back for a 32bpp <c>BI_RGB</c> section, so the frame can be copied
/// straight into one. The fourth byte is padding, NOT per-pixel alpha: this surface is composited
/// at the window's uniform <c>LWA_ALPHA</c> (<see cref="OverlaySurfaceRequest.Opacity"/>), which is
/// the shape the shipping product ships — a flash window is a rectangle at one opacity with an
/// image filling it (<c>Services/Flash/FlashService.cs:1245</c>, <c>:1274-1281</c>). Per-pixel alpha
/// would mean <c>UpdateLayeredWindow</c>, which is mutually exclusive with
/// <c>SetLayeredWindowAttributes</c> and would therefore delete the alpha read-back that
/// <see cref="OverlayReasonCodes.OverlayNotComposited"/> depends on (the packet record §1).</para>
/// </summary>
public sealed class OverlayFrame
{
    /// <summary>B, G, R and one padding byte.</summary>
    public const int BytesPerPixel = 4;

    private readonly byte[] _pixels;

    /// <param name="width">Frame width in physical pixels; must be positive.</param>
    /// <param name="height">Frame height in physical pixels; must be positive.</param>
    /// <param name="pixels">
    /// Exactly <c>width * height * 4</c> bytes, top row first, B,G,R,X per pixel. Taken by
    /// reference and never copied: a frame is built once per flash and handed straight to a blit.
    /// </param>
    public OverlayFrame(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
        ArgumentNullException.ThrowIfNull(pixels);
        var expected = (long)width * height * BytesPerPixel;
        if (pixels.LongLength != expected)
        {
            throw new ArgumentException(
                $"an overlay frame for {width}x{height} needs exactly {expected} bytes (top-down BGRX), "
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

    /// <summary>The buffer itself, for the one consumer that copies it into a DIB section.</summary>
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
    /// A frame of one colour. The overlay's own way to say "this many pixels of exactly this",
    /// used by the capture harness and by every draw-path fact that must know, byte for byte, what
    /// the OS should be holding afterwards.
    /// </summary>
    public static OverlayFrame Solid(int width, int height, byte blue, byte green, byte red)
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

        return new OverlayFrame(width, height, pixels);
    }
}
