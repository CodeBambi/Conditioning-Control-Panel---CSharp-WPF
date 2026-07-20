namespace CcpClient.Desktop.Features.AvatarTube;

/// <summary>
/// Minimal 32/24-bpp BMP codec — pure C#, no platform, no packages. BMP is the
/// cross-platform capture/asset format in this project's evidence flow (XGetImage on
/// WSLg emits BMP, CopyFromScreen saves BMP; SP-013 precedent). Keeping the pack sheets
/// as BMP means the generator, the pack loader, and the evidence strip-decode all share
/// one dependency-free codec, and unit tests never need a Skia platform.
/// </summary>
public static class BmpCodec
{
    public const int BitsPerPixel32 = 32;
    public const int BitsPerPixel24 = 24;

    /// <summary>Encodes a BGRA pixel buffer as a bottom-up 32bpp BI_RGB BMP.</summary>
    public static byte[] Encode32(int width, int height, byte[] bgra)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
        if (bgra.Length != width * height * 4)
        {
            throw new ArgumentException("BGRA buffer must be width*height*4 bytes.", nameof(bgra));
        }

        var pixelBytes = width * height * 4;
        var fileSize = 54 + pixelBytes;
        var bytes = new byte[fileSize];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        WriteInt32(bytes, 2, fileSize);
        WriteInt32(bytes, 10, 54); // pixel data offset
        WriteInt32(bytes, 14, 40); // BITMAPINFOHEADER size
        WriteInt32(bytes, 18, width);
        WriteInt32(bytes, 26, 1); // planes
        WriteInt32(bytes, 28, BitsPerPixel32);
        WriteInt32(bytes, 34, pixelBytes);
        // Height left at 0 here and written below as positive bottom-up.
        WriteInt32(bytes, 22, height);

        // BMP rows are bottom-up; the buffer is top-down.
        for (var y = 0; y < height; y++)
        {
            var src = y * width * 4;
            var dst = 54 + (height - 1 - y) * width * 4;
            Array.Copy(bgra, src, bytes, dst, width * 4);
        }

        return bytes;
    }

    /// <summary>
    /// Decodes a BI_RGB 24bpp or 32bpp BMP (bottom-up or top-down) into a top-down BGRA
    /// buffer. Anything else (compression, palettes, bitfields) is rejected — the
    /// demonstrator's assets and both capture paths stay inside this envelope, and a
    /// rejection is a loud finding, never a silent mis-decode.
    /// </summary>
    public static (int Width, int Height, byte[] Bgra) Decode(byte[] bmp)
    {
        if (bmp.Length < 54 || bmp[0] != 'B' || bmp[1] != 'M')
        {
            throw new InvalidDataException("bmp: missing BM signature");
        }

        var pixelOffset = ReadInt32(bmp, 10);
        var headerSize = ReadInt32(bmp, 14);
        if (headerSize < 40)
        {
            throw new InvalidDataException($"bmp: unsupported header size {headerSize} (need BITMAPINFOHEADER+)");
        }

        var width = ReadInt32(bmp, 18);
        var rawHeight = ReadInt32(bmp, 22);
        var topDown = rawHeight < 0;
        var height = Math.Abs(rawHeight);
        var bpp = ReadInt16(bmp, 28);
        var compression = ReadInt32(bmp, 30);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException($"bmp: bad dimensions {width}x{height}");
        }

        if (bpp != BitsPerPixel32 && bpp != BitsPerPixel24)
        {
            throw new InvalidDataException($"bmp: unsupported bpp {bpp} (24/32 only)");
        }

        if (compression != 0)
        {
            throw new InvalidDataException($"bmp: compressed BMP unsupported (compression={compression})");
        }

        var srcStride = ((width * bpp + 31) / 32) * 4;
        if ((long)pixelOffset + (long)srcStride * height > bmp.Length)
        {
            throw new InvalidDataException("bmp: truncated pixel data");
        }

        var bgra = new byte[width * height * 4];
        var bytesPerPixel = bpp / 8;
        for (var y = 0; y < height; y++)
        {
            var srcRow = topDown ? y : height - 1 - y;
            var src = pixelOffset + srcRow * srcStride;
            var dst = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                bgra[dst + x * 4 + 0] = bmp[src + x * bytesPerPixel + 0]; // B
                bgra[dst + x * 4 + 1] = bmp[src + x * bytesPerPixel + 1]; // G
                bgra[dst + x * 4 + 2] = bmp[src + x * bytesPerPixel + 2]; // R
                bgra[dst + x * 4 + 3] = 255;
            }
        }

        return (width, height, bgra);
    }

    private static void WriteInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24);

    private static int ReadInt16(byte[] bytes, int offset) =>
        bytes[offset] | (bytes[offset + 1] << 8);
}
