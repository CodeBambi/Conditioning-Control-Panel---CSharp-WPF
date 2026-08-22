using System.Text;
using CcpClient.Desktop.Video;

namespace CcpClient.Tests;

/// <summary>
/// Synthesised media: a minimal uncompressed 32bpp RIFF/AVI, written in PURE MANAGED CODE
/// with no encoder, no codec and no interop anywhere in it — the video sibling of
/// <see cref="TestWav"/>.
///
/// <para><b>Why the suite synthesises its own clips.</b> The owner's real media directory
/// (<c>Z:\CCP Vids</c>) <b>does not exist on this machine and neither does the Z: volume</b>
/// (plan.md §0, Q0). That is a NAMED LIMIT rather than a blocker: every fact in this packet
/// runs against media this file produced, and nothing in the packet has ever been run against a real
/// library clip. A fixture that skipped instead would have hidden the whole capability.</para>
///
/// <para><b>Why AVI and why uncompressed.</b> Media Foundation opens it on this machine — measured,
/// <c>MFCreateSourceReaderFromURL</c> returned S_OK and the native subtype came back as
/// <c>MFVideoFormat_RGB32</c> — so the DECODER under test is genuinely the operating system's,
/// while the file itself contains no lossy step at all. A frame written here comes back byte for
/// byte, which is what lets a read-back assert EQUALITY instead of a tolerance.</para>
///
/// <para><b>The orientation trap, deliberately preserved.</b> AVI stores rows BOTTOM-UP for a
/// positive <c>biHeight</c>, and Media Foundation reports that back as a NEGATIVE
/// <c>MF_MT_DEFAULT_STRIDE</c> (measured: -1280). This writer takes TOP-DOWN frames and reverses
/// them on the way out, so a decoder that ignores the stride's sign hands back a vertically
/// mirrored picture and <see cref="VerticalSplit"/> catches it. A fixture of solid colours never
/// could.</para>
/// </summary>
internal static class TestAvi
{
    /// <summary>A frame whose top half and bottom half differ. The only fixture shape that can tell
    /// a correctly oriented picture from its own mirror.</summary>
    internal static byte[] VerticalSplit(
        int width, int height, (byte B, byte G, byte R) top, (byte B, byte G, byte R) bottom)
    {
        var pixels = new byte[width * height * VideoFrame.BytesPerPixel];
        for (var y = 0; y < height; y++)
        {
            var (b, g, r) = y < height / 2 ? top : bottom;
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * VideoFrame.BytesPerPixel;
                pixels[offset] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
                pixels[offset + 3] = 0xFF;
            }
        }

        return pixels;
    }

    /// <summary>A frame of one colour.</summary>
    internal static byte[] Solid(int width, int height, byte blue, byte green, byte red)
    {
        var pixels = new byte[width * height * VideoFrame.BytesPerPixel];
        for (var i = 0; i < pixels.Length; i += VideoFrame.BytesPerPixel)
        {
            pixels[i] = blue;
            pixels[i + 1] = green;
            pixels[i + 2] = red;
            pixels[i + 3] = 0xFF;
        }

        return pixels;
    }

    /// <summary>
    /// Write a clip. Each frame is TOP-DOWN BGRX of exactly <c>width * height * 4</c> bytes.
    /// </summary>
    internal static void Write(
        string path, int width, int height, IReadOnlyList<byte[]> frames, int microsecondsPerFrame = 100_000)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(frames.Count, 0);

        var frameBytes = width * height * VideoFrame.BytesPerPixel;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Fcc("RIFF"));
        var riffSize = stream.Position;
        writer.Write(0u);
        writer.Write(Fcc("AVI "));

        writer.Write(Fcc("LIST"));
        var hdrlSize = stream.Position;
        writer.Write(0u);
        writer.Write(Fcc("hdrl"));

        // MainAVIHeader, 56 bytes.
        writer.Write(Fcc("avih"));
        writer.Write(56u);
        writer.Write((uint)microsecondsPerFrame);
        writer.Write((uint)(frameBytes * (1_000_000.0 / microsecondsPerFrame)));
        writer.Write(0u); // dwPaddingGranularity
        writer.Write(0x10u); // AVIF_HASINDEX
        writer.Write((uint)frames.Count);
        writer.Write(0u); // dwInitialFrames
        writer.Write(1u); // dwStreams
        writer.Write((uint)frameBytes);
        writer.Write((uint)width);
        writer.Write((uint)height);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);

        writer.Write(Fcc("LIST"));
        var strlSize = stream.Position;
        writer.Write(0u);
        writer.Write(Fcc("strl"));

        // AVIStreamHeader, 56 bytes.
        writer.Write(Fcc("strh"));
        writer.Write(56u);
        writer.Write(Fcc("vids"));
        writer.Write(Fcc("DIB ")); // uncompressed
        writer.Write(0u); // dwFlags
        writer.Write((ushort)0); // wPriority
        writer.Write((ushort)0); // wLanguage
        writer.Write(0u); // dwInitialFrames
        writer.Write((uint)microsecondsPerFrame); // dwScale
        writer.Write(1_000_000u); // dwRate => rate/scale frames per second
        writer.Write(0u); // dwStart
        writer.Write((uint)frames.Count);
        writer.Write((uint)frameBytes);
        writer.Write(0u); // dwQuality
        writer.Write(0u); // dwSampleSize
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write((short)width);
        writer.Write((short)height);

        // BITMAPINFOHEADER, 40 bytes. A POSITIVE biHeight, which means the rows below are stored
        // bottom-up — see the type remarks.
        writer.Write(Fcc("strf"));
        writer.Write(40u);
        writer.Write(40u);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0u); // BI_RGB
        writer.Write((uint)frameBytes);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0u);
        writer.Write(0u);

        Patch(stream, writer, strlSize);
        Patch(stream, writer, hdrlSize);

        writer.Write(Fcc("LIST"));
        var moviSize = stream.Position;
        writer.Write(0u);
        var moviFcc = stream.Position;
        writer.Write(Fcc("movi"));

        var rowBytes = width * VideoFrame.BytesPerPixel;
        var offsets = new long[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (frame.Length != frameBytes)
            {
                throw new ArgumentException(
                    $"frame {i} is {frame.Length} bytes; a {width}x{height} top-down BGRX frame is {frameBytes}",
                    nameof(frames));
            }

            offsets[i] = stream.Position - moviFcc;
            writer.Write(Fcc("00db"));
            writer.Write((uint)frameBytes);
            for (var row = height - 1; row >= 0; row--)
            {
                writer.Write(frame, row * rowBytes, rowBytes);
            }

            if ((frameBytes & 1) == 1)
            {
                writer.Write((byte)0);
            }
        }

        Patch(stream, writer, moviSize);

        writer.Write(Fcc("idx1"));
        writer.Write((uint)(frames.Count * 16));
        for (var i = 0; i < frames.Count; i++)
        {
            writer.Write(Fcc("00db"));
            writer.Write(0x10u); // AVIIF_KEYFRAME
            writer.Write((uint)offsets[i]);
            writer.Write((uint)frameBytes);
        }

        Patch(stream, writer, riffSize);
        writer.Flush();
    }

    private static void Patch(FileStream stream, BinaryWriter writer, long sizePosition)
    {
        var end = stream.Position;
        stream.Position = sizePosition;
        writer.Write((uint)(end - sizePosition - 4));
        writer.Flush();
        stream.Position = end;
    }

    private static uint Fcc(string four) =>
        (uint)(four[0] | (four[1] << 8) | (four[2] << 16) | (four[3] << 24));
}
