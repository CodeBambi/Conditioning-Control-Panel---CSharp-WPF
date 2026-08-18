using System.IO.Compression;
using System.Text;

namespace CcpClient.Tests;

/// <summary>
/// A minimal PNG writer, so a fact can hand the product a REAL image file in a format the flash
/// pool actually accepts (<c>.png</c>) rather than a synthetic buffer.
///
/// <para>Deliberately hand-rolled rather than pulled from a package: the whole point of the frame
/// source under test is that it decodes what a user's folder contains, and a test that produced its
/// input with the same imaging library the product decodes with would be measuring a round trip
/// through one implementation. Eight-byte signature, IHDR, one zlib IDAT with filter 0 per row,
/// IEND — the format's own minimum.</para>
/// </summary>
internal static class TestPng
{
    /// <summary>Writes an opaque RGB PNG of one colour.</summary>
    internal static void WriteSolid(string path, int width, int height, byte red, byte green, byte blue)
    {
        var raw = new byte[((width * 3) + 1) * height];
        var offset = 0;
        for (var y = 0; y < height; y++)
        {
            raw[offset++] = 0;
            for (var x = 0; x < width; x++)
            {
                raw[offset++] = red;
                raw[offset++] = green;
                raw[offset++] = blue;
            }
        }

        Write(path, width, height, colourType: 2, raw);
    }

    /// <summary>
    /// Writes an RGBA PNG whose top half is fully transparent and whose bottom half is opaque.
    /// The transparent half is what proves the frame source composes over black, as WPF's flash
    /// window does (<c>Services/Flash/FlashService.cs:1245</c>).
    /// </summary>
    internal static void WriteHalfTransparent(string path, int width, int height, byte red, byte green, byte blue)
    {
        var raw = new byte[((width * 4) + 1) * height];
        var offset = 0;
        for (var y = 0; y < height; y++)
        {
            raw[offset++] = 0;
            var alpha = (byte)(y < height / 2 ? 0x00 : 0xFF);
            for (var x = 0; x < width; x++)
            {
                raw[offset++] = red;
                raw[offset++] = green;
                raw[offset++] = blue;
                raw[offset++] = alpha;
            }
        }

        Write(path, width, height, colourType: 6, raw);
    }

    private static void Write(string path, int width, int height, byte colourType, byte[] raw)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        using var file = File.Create(path);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8;              // bit depth
        header[9] = colourType;     // 2 = RGB, 6 = RGBA
        Chunk(file, "IHDR", header);
        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        stream.Write(length);

        var name = Encoding.ASCII.GetBytes(type);
        stream.Write(name);
        stream.Write(data);

        var crc = new byte[4];
        WriteBigEndian(crc, 0, unchecked((int)Crc32(name, data)));
        stream.Write(crc);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in first)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (var b in second)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
