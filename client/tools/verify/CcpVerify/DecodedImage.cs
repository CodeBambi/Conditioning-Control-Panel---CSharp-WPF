namespace CcpVerify;

/// <summary>
/// A decoded capture as a raw BGRA pixel buffer. Pure data — the console tool produces
/// one from an Avalonia <c>Bitmap</c>; unit tests synthesize one directly (no Avalonia
/// runtime needed for assertion-logic tests).
/// </summary>
public sealed class DecodedImage
{
    public DecodedImage(int width, int height, byte[] bgra)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        }

        if (bgra.Length != width * height * 4)
        {
            throw new ArgumentException($"BGRA buffer must be width*height*4 bytes ({width * height * 4}), got {bgra.Length}.", nameof(bgra));
        }

        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>BGRA bytes, row-major, top-down, 4 bytes per pixel.</summary>
    public byte[] Bgra { get; }

    public (byte R, byte G, byte B) PixelAt(int x, int y)
    {
        var offset = (y * Width + x) * 4;
        return (Bgra[offset + 2], Bgra[offset + 1], Bgra[offset]);
    }
}
