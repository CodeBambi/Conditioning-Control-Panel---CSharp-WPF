using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1829 moved GIFs onto AnimatedWebp.DecodeFrames (SKCodec). It always told SKCodec "the canvas
/// holds frame i-1", which is not allowed when frame i-1 is GIF "restore to previous" (disposal 3):
/// SKCodec answered InvalidParameters and the common "background kept, then overlays undone"
/// pattern played its first two frames only. Every disposal pattern must keep all 8 frames and
/// compose them right. The GIFs are written here, 16x16, a few hundred bytes each.
/// </summary>
public class AnimatedWebpGifDisposalTests
{
    private const int Size = 16;
    private const int Frames = 8;

    public static IEnumerable<object[]> Patterns() => new[]
    {
        new object[] { new[] { 1, 1, 1, 1, 1, 1, 1, 1 } }, // keep
        new object[] { new[] { 3, 3, 3, 3, 3, 3, 3, 3 } }, // restore previous, every frame on its own
        new object[] { new[] { 1, 3, 3, 3, 3, 3, 3, 3 } }, // background kept, each overlay undone
        new object[] { new[] { 1, 2, 2, 2, 2, 2, 2, 2 } }, // background kept, each overlay cleared
    };

    [Theory]
    [MemberData(nameof(Patterns))]
    public void EveryFrameSurvives_AndShowsItsOwnSquare(int[] disposals)
    {
        var gif = Gif(disposals);
        using (var codec = SKCodec.Create(SKData.CreateCopy(gif)))
        {
            // The fixture really carries the disposals it says it does.
            Assert.Equal(Frames, codec.FrameCount);
            Assert.Equal(disposals, codec.FrameInfo.Select(f => (int)f.DisposalMethod).ToArray());
        }

        var decoded = AnimatedWebp.DecodeFrames(new MemoryStream(gif), maxDim: 64, maxFrames: 48, maxMemoryMb: 24.0);

        Assert.NotNull(decoded);
        Assert.Equal(Frames, decoded!.Value.Frames.Count);
        for (int f = 1; f < Frames; f++)
            Assert.True(IsGold(decoded.Value.Frames[f], SquareX(f)), $"frame {f} lost its square");
    }

    [Fact]
    public void RestorePrevious_PutsTheBackgroundBack_UnderEachOverlay()
    {
        var decoded = AnimatedWebp.DecodeFrames(new MemoryStream(Gif(new[] { 1, 3, 3, 3, 3, 3, 3, 3 })),
            maxDim: 64, maxFrames: 48, maxMemoryMb: 24.0);

        Assert.NotNull(decoded);
        for (int f = 2; f < Frames; f++)
        {
            var frame = decoded!.Value.Frames[f];
            Assert.True(IsGold(frame, SquareX(f)), $"frame {f} lost its square");
            Assert.True(IsBackground(frame, SquareX(f - 1)), $"frame {f} still shows frame {f - 1}'s square");
        }
    }

    [Fact]
    public void Keep_LeavesTheTrail()
    {
        var decoded = AnimatedWebp.DecodeFrames(new MemoryStream(Gif(new[] { 1, 1, 1, 1, 1, 1, 1, 1 })),
            maxDim: 64, maxFrames: 48, maxMemoryMb: 24.0);

        Assert.NotNull(decoded);
        Assert.True(IsGold(decoded!.Value.Frames[Frames - 1], SquareX(1)));
    }

    // ---- #1829 frame rate: the GIF callers' caps (FIXES-6) ----

    /// <summary>A full-screen Blink Trainer tile asked for 1280 px and kept 20 of these 60 frames
    /// (27 fit the 24 MB budget, then every third was taken).</summary>
    [Fact]
    public void AFullScreenBlinkTile_KeepsEveryFrameOfA60FrameGif()
    {
        var gif = Gif(Enumerable.Repeat(1, 60).ToArray(), width: 640, height: 360);
        var decoded = AnimatedWebp.DecodeFrames(new MemoryStream(gif),
            BlinkTrainerService.GifDecodeDim(1280), AnimatedWebp.GifMaxFrames, maxMemoryMb: 24.0);

        Assert.NotNull(decoded);
        Assert.Equal(60, decoded!.Value.Frames.Count);
        Assert.Equal(100, decoded.Value.FrameDelay.TotalMilliseconds);
    }

    /// <summary>A small cascade tile kept 45 of these 90 frames under the old 48 cap.</summary>
    [Fact]
    public void ACascadeTile_KeepsEveryFrameOfA90FrameGif()
    {
        var decoded = AnimatedWebp.DecodeFrames(new MemoryStream(Gif(Enumerable.Repeat(1, 90).ToArray())),
            maxDim: 200, AnimatedWebp.GifMaxFrames, maxMemoryMb: 24.0);

        Assert.NotNull(decoded);
        Assert.Equal(90, decoded!.Value.Frames.Count);
    }

    // ---- a tiny GIF89a writer ----

    private static readonly byte[] Palette = { 40, 10, 60, 255, 105, 180, 255, 215, 0, 0, 0, 0 };
    private const int SquareY = 6;

    // Frame 0 is the whole background (index 0); frame f >= 1 is a 2x2 gold square (index 2).
    private static int SquareX(int frame) => 2 * (frame - 1) % (Size - 2);

    private static byte[] Gif(int[] disposals, int width = Size, int height = Size)
    {
        var ms = new MemoryStream();
        void U16(int v) { ms.WriteByte((byte)v); ms.WriteByte((byte)(v >> 8)); }

        ms.Write(Encoding.ASCII.GetBytes("GIF89a"));
        U16(width); U16(height);
        ms.WriteByte(0xF1); // global colour table of 4 entries
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.Write(Palette);
        ms.Write(new byte[] { 0x21, 0xFF, 0x0B });
        ms.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        ms.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });

        for (int f = 0; f < disposals.Length; f++)
        {
            int x = f == 0 ? 0 : SquareX(f), y = f == 0 ? 0 : SquareY;
            int w = f == 0 ? width : 2, h = f == 0 ? height : 2;
            ms.Write(new byte[] { 0x21, 0xF9, 0x04, (byte)(disposals[f] << 2), 10, 0, 0, 0 });
            ms.WriteByte(0x2C);
            U16(x); U16(y); U16(w); U16(h);
            ms.WriteByte(0);
            ms.WriteByte(2); // LZW minimum code size
            var data = Lzw(Enumerable.Repeat((byte)(f == 0 ? 0 : 2), w * h).ToArray());
            for (int i = 0; i < data.Length; i += 255)
            {
                int n = System.Math.Min(255, data.Length - i);
                ms.WriteByte((byte)n);
                ms.Write(data, i, n);
            }
            ms.WriteByte(0);
        }
        ms.WriteByte(0x3B);
        return ms.ToArray();
    }

    // Uncompressed LZW: a clear code before every two pixels keeps the table from growing, so
    // every code stays 3 bits wide (clear = 4, end = 5).
    private static byte[] Lzw(byte[] indices)
    {
        var output = new List<byte>();
        int acc = 0, bits = 0;
        void Code(int c)
        {
            acc |= c << bits;
            bits += 3;
            while (bits >= 8) { output.Add((byte)acc); acc >>= 8; bits -= 8; }
        }
        for (int i = 0; i < indices.Length; i += 2)
        {
            Code(4);
            Code(indices[i]);
            if (i + 1 < indices.Length) Code(indices[i + 1]);
        }
        Code(5);
        if (bits > 0) output.Add((byte)acc);
        return output.ToArray();
    }

    private static byte[] Pixel(BitmapSource frame, int x, int y)
    {
        var px = new byte[4];
        frame.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), px, 4, 0);
        return px; // B, G, R, A (premultiplied)
    }

    private static bool IsGold(BitmapSource frame, int x)
    {
        var p = Pixel(frame, x, SquareY);
        return p[3] == 255 && p[2] == 255 && p[1] == 215 && p[0] == 0;
    }

    private static bool IsBackground(BitmapSource frame, int x)
    {
        var p = Pixel(frame, x, SquareY);
        return p[3] == 255 && p[2] == 40 && p[1] == 10 && p[0] == 60;
    }
}
