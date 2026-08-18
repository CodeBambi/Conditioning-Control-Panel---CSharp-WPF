using CcpClient.Desktop.Effects;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-106 — the spiral decoder, against a REAL animated GIF built byte by byte in this file.
///
/// <para><b>Why the GIF is hand-built rather than checked in.</b> This port bundles no art (D86) and
/// the legacy tree's bytes stay owned by the legacy tree. A twelve-line GIF89a whose two frames are
/// one flat colour each is enough to prove every decision the decoder makes — the frame COUNT, the
/// frame DELAY, and that frame 1 and frame 2 really differ — and it is self-validating: a malformed
/// GIF cannot produce the right two colours by accident.</para>
///
/// <para><b>Windows-only mechanism, asserted on both.</b> GDI+ is a Windows library, exactly like
/// the surface it feeds, so on any other OS the honest answer is null and the facts below assert
/// THAT rather than skipping. Nothing here is added to <c>allowedSkips</c>.</para>
/// </summary>
public class SpiralFrameSourceTests
{
    private const int Size = 8;

    /// <summary>
    /// Which channel dominates a decoded pixel, out of the OS's own COLORREF (0x00BBGGRR).
    ///
    /// <para><b>Why the assertion is dominance and not equality.</b> The scale is bicubic — WPF
    /// picks its resampling from a performance tier and the port takes the quality end once
    /// (<see cref="GdiPlusFlashFrameSource"/>) — and a bicubic kernel reaching past the edge of a
    /// 2x2 source lands the centre of an 8x8 upscale a few steps short of the source value (236 of
    /// 255, measured). Pinning 255 would be pinning the resampler's edge policy, which is not a
    /// behaviour this port owns; pinning WHICH FRAME IS ON SCREEN is.</para>
    /// </summary>
    private static (int Red, int Green, int Blue) Channels(uint colourRef) =>
        ((int)(colourRef & 0xFF), (int)((colourRef >> 8) & 0xFF), (int)((colourRef >> 16) & 0xFF));

    private static void AssertMostlyRed(uint colourRef)
    {
        var (red, green, blue) = Channels(colourRef);
        Assert.True(red > 200, $"expected a red pixel, got {red},{green},{blue}");
        Assert.True(blue < 40, $"expected no blue in a red pixel, got {red},{green},{blue}");
    }

    private static void AssertMostlyBlue(uint colourRef)
    {
        var (red, green, blue) = Channels(colourRef);
        Assert.True(blue > 200, $"expected a blue pixel, got {red},{green},{blue}");
        Assert.True(red < 40, $"expected no red in a blue pixel, got {red},{green},{blue}");
    }

    // ---------------------------------------------------------------------------------
    //  the clip
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AnAnimatedGifOpensAsTwoFrames_AtTheDelayTheFileAsksFor()
    {
        using var file = new TempGif(TwoFrameGif());

        using var animation = new GdiPlusSpiralFrameSource().Open(file.Path, Size, Size);

        if (!GdiPlusRuntime.Available)
        {
            // Not a skip and not a silence: on a platform with no GDI+ the decoder returns null,
            // the module reports spiral-not-decoded, and nothing pretends to have drawn.
            Assert.Null(animation);
            return;
        }

        Assert.NotNull(animation);
        Assert.Equal(2, animation.FrameCount);

        // The file carries 5 hundredths per frame; WPF's arithmetic makes that 50 ms
        // (OverlayService.cs:1549-1552).
        Assert.Equal(TimeSpan.FromMilliseconds(50), animation.FrameDelay);
    }

    [Fact]
    public void TheTwoFramesReallyDiffer_WhichIsTheOnlyThingThatMakesItMotion()
    {
        using var file = new TempGif(TwoFrameGif());
        using var animation = new GdiPlusSpiralFrameSource().Open(file.Path, Size, Size);
        if (!GdiPlusRuntime.Available)
        {
            Assert.Null(animation);
            return;
        }

        var first = animation!.Render(0);
        var firstCentre = first!.ColourAt(Size / 2, Size / 2);
        var second = animation.Render(1);
        var secondCentre = second!.ColourAt(Size / 2, Size / 2);

        // GdipImageSelectActiveFrame is what makes this a clip rather than a picture. If it were
        // dropped, both renders would return the same pixels and the layer would sit still forever
        // while every counter in the module said it was moving.
        AssertMostlyRed(firstCentre);
        AssertMostlyBlue(secondCentre);
        Assert.NotEqual(firstCentre, secondCentre);

        // And it goes back: the loop revisits frame 0 every cycle for the whole session.
        AssertMostlyRed(animation.Render(0)!.ColourAt(Size / 2, Size / 2));
    }

    [Fact]
    public void EveryFrameIsExactlyTheSizeTheSurfaceWasPresentedAt_BecausePaintRefusesAnyOther()
    {
        using var file = new TempGif(TwoFrameGif());
        using var animation = new GdiPlusSpiralFrameSource().Open(file.Path, 64, 32);
        if (!GdiPlusRuntime.Available)
        {
            Assert.Null(animation);
            return;
        }

        var frame = animation!.Render(0);

        // IOverlayPresence.Paint refuses a mismatched frame rather than stretching it onto the
        // user's screen (OverlayFrameSizeMismatch), so the decoder scales, not the capability.
        Assert.Equal(64, frame!.Width);
        Assert.Equal(32, frame.Height);
    }

    [Fact]
    public void TheBufferIsREUSEDAcrossFrames_WhichIsTheContractAndNotAnAccident()
    {
        using var file = new TempGif(TwoFrameGif());
        using var animation = new GdiPlusSpiralFrameSource().Open(file.Path, Size, Size);
        if (!GdiPlusRuntime.Available)
        {
            Assert.Null(animation);
            return;
        }

        var first = animation!.Render(0);
        var second = animation.Render(1);

        // A full-screen frame is about 8 MB; allocating one twenty times a second would put
        // 160 MB/s of large-object garbage on the heap for the length of a session. The one
        // consumer hands each frame straight to Paint, which copies it into a DIB before it
        // returns, so nothing ever holds a frame across two renders — and this fact is what makes
        // that contract visible rather than a comment.
        Assert.Same(first!.Pixels, second!.Pixels);
    }

    [Fact]
    public void AnIndexOutsideTheClipProducesNothing_RatherThanWrappingSilently()
    {
        using var file = new TempGif(TwoFrameGif());
        using var animation = new GdiPlusSpiralFrameSource().Open(file.Path, Size, Size);
        if (!GdiPlusRuntime.Available)
        {
            Assert.Null(animation);
            return;
        }

        // The LOOP is the presenter's job (`(index + 1) % count`, WPF's own arithmetic at
        // OverlayService.cs:1641). A decoder that wrapped as well would hide an off-by-one forever.
        Assert.Null(animation!.Render(2));
        Assert.Null(animation.Render(-1));
    }

    [Fact]
    public void AStillImageIsOneFrameAtTheDefaultDelay_AndIsAPerfectlyGoodSpiral()
    {
        using var file = new TempGif(SingleFrameGif());
        using var animation = new GdiPlusSpiralFrameSource().Open(file.Path, Size, Size);
        if (!GdiPlusRuntime.Available)
        {
            Assert.Null(animation);
            return;
        }

        // WPF starts no frame timer for one (OverlayService.cs:1369), and a single-frame file has
        // no 0x5100 property to read, so the delay falls back to the default rather than to zero.
        Assert.Equal(1, animation!.FrameCount);
        Assert.Equal(SpiralFrameDelay.Default, animation.FrameDelay);
        AssertMostlyRed(animation.Render(0)!.ColourAt(Size / 2, Size / 2));
    }

    // ---------------------------------------------------------------------------------
    //  ordinary failures, which must be null and never an exception
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AMissingOrUndecodableFileIsNull_NeverAnExceptionOnASurfaceThread()
    {
        var source = new GdiPlusSpiralFrameSource();
        using var notAnImage = new TempGif([0x00, 0x01, 0x02, 0x03]);

        Assert.Null(source.Open(Path.Combine(Path.GetTempPath(), "ccp-sp106-nothing-here.gif"), Size, Size));
        Assert.Null(source.Open(notAnImage.Path, Size, Size));
    }

    [Fact]
    public void AZeroSizedTargetIsRefused_BecauseAZeroByteFrameWouldBeBlitted()
    {
        var source = new GdiPlusSpiralFrameSource();
        using var file = new TempGif(TwoFrameGif());

        Assert.Null(source.Open(file.Path, 0, Size));
        Assert.Null(source.Open(file.Path, Size, 0));
        Assert.Null(source.Open(file.Path, -4, Size));
    }

    // ---------------------------------------------------------------------------------
    //  the crop law — WPF's UniformToFill, as arithmetic
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AMatchingAspectRatioIsNotCroppedAtAll()
    {
        // 16:9 into 16:9. Compared as a cross-multiplication rather than two divisions, because
        // 1920/1080 == 16/9 is not reliably true in double arithmetic and "no crop" must be exact.
        Assert.Equal((0, 0, 1600, 900), GdiPlusSpiralFrameSource.SourceCrop(1600, 900, 1920, 1080));
    }

    [Fact]
    public void ASourceWiderThanTheScreenIsCroppedLeftAndRightEqually()
    {
        // WPF's Stretch.UniformToFill inside a ClipToBounds container (OverlayService.cs:1697,
        // :1701-1706): the image covers the layer with nothing letterboxed and nothing distorted,
        // and the overflow is lost evenly on both sides.
        var (x, y, width, height) = GdiPlusSpiralFrameSource.SourceCrop(4000, 1000, 1000, 1000);

        Assert.Equal(1000, width);
        Assert.Equal(1000, height);
        Assert.Equal(1500, x);
        Assert.Equal(0, y);
        Assert.Equal(4000 - width - x, x);
    }

    [Fact]
    public void ASourceTallerThanTheScreenIsCroppedTopAndBottomEqually()
    {
        var (x, y, width, height) = GdiPlusSpiralFrameSource.SourceCrop(1000, 4000, 1000, 1000);

        Assert.Equal(1000, width);
        Assert.Equal(1000, height);
        Assert.Equal(0, x);
        Assert.Equal(1500, y);
        Assert.Equal(4000 - height - y, y);
    }

    [Fact]
    public void AOnePixelSourceStillProducesALegalCrop_RatherThanAZeroSizedOne()
    {
        // A 1x1 spiral is silly and is also a file a user can put in the folder. A zero-width source
        // rectangle would make GDI+ fail and the layer would go up holding nothing.
        var (_, _, width, height) = GdiPlusSpiralFrameSource.SourceCrop(1, 1, 1920, 1080);

        Assert.True(width >= 1);
        Assert.True(height >= 1);
    }

    // =====================================================================================

    private sealed class TempGif : IDisposable
    {
        public TempGif(byte[] bytes)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "ccp-sp106-" + Guid.NewGuid().ToString("N") + ".gif");
            File.WriteAllBytes(Path, bytes);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// A 2x2 GIF89a with two frames — frame 1 all red, frame 2 all blue — each declaring 5
    /// hundredths of a second.
    ///
    /// <para>The LZW payload emits a CLEAR code before every pixel, which keeps the code width at
    /// three bits for the whole stream and makes the four bytes checkable by hand. It is a
    /// conforming encoding, not a trick: a decoder that resets its table on CLEAR (all of them do)
    /// reads exactly four pixels of the index that follows each one.</para>
    /// </summary>
    private static byte[] TwoFrameGif() =>
    [
        // "GIF89a"
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
        // logical screen: 2x2, global colour table of 2 entries, background 0, aspect 0
        0x02, 0x00, 0x02, 0x00, 0xF0, 0x00, 0x00,
        // the table: red, blue
        0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF,
        // graphic control extension: no transparency, delay 0x0005 hundredths
        0x21, 0xF9, 0x04, 0x00, 0x05, 0x00, 0x00, 0x00,
        // image descriptor at 0,0 sized 2x2, no local colour table
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x02, 0x00, 0x00,
        // LZW min code size 2, one sub-block of 4 bytes: CLEAR,0,CLEAR,0,CLEAR,0,CLEAR,0,EOI
        0x02, 0x04, 0x04, 0x41, 0x10, 0x05, 0x00,
        // second frame, same delay
        0x21, 0xF9, 0x04, 0x00, 0x05, 0x00, 0x00, 0x00,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x02, 0x00, 0x00,
        // CLEAR,1,CLEAR,1,CLEAR,1,CLEAR,1,EOI
        0x02, 0x04, 0x0C, 0xC3, 0x30, 0x05, 0x00,
        // trailer
        0x3B,
    ];

    /// <summary>The same file with one frame and no graphic control extension: a still spiral.</summary>
    private static byte[] SingleFrameGif() =>
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
        0x02, 0x00, 0x02, 0x00, 0xF0, 0x00, 0x00,
        0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x02, 0x00, 0x00,
        0x02, 0x04, 0x04, 0x41, 0x10, 0x05, 0x00,
        0x3B,
    ];
}
