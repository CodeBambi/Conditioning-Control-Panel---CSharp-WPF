using CcpClient.Desktop.Video;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-111 — the surface's composition, which is pure arithmetic and therefore needs no desktop.
///
/// <para>These facts are deliberately NOT in <c>RealDesktopCollection</c>: nothing here creates a
/// window, and putting pure arithmetic behind a machine-wide lease would slow every floor run for
/// no evidence.</para>
/// </summary>
public class VideoLetterboxTests
{
    [Theory]
    // A wide picture in a square surface pillarboxes vertically, and vice versa. Upstream's own
    // arithmetic (Services/Video/VideoService.cs:3193-3211, FitToAspect), applied to the inner box.
    [InlineData(400, 240, 320, 240, 312, 234)]
    [InlineData(240, 400, 320, 240, 234, 176)]
    [InlineData(400, 400, 320, 240, 394, 296)]
    public void FitIsUpstreamsLetterboxArithmetic_NeverAStretch(
        int surfaceWidth, int surfaceHeight, int pictureWidth, int pictureHeight, int expectedWidth, int expectedHeight)
    {
        var box = VideoLetterbox.Fit(surfaceWidth, surfaceHeight, pictureWidth, pictureHeight);
        Assert.Equal(expectedWidth, box.Width);
        Assert.Equal(expectedHeight, box.Height);

        // Never a stretch: the picture's aspect survives to within a pixel of rounding.
        var sourceAspect = pictureWidth / (double)pictureHeight;
        var fittedAspect = box.Width / (double)box.Height;
        Assert.True(
            Math.Abs(fittedAspect - sourceAspect) < 0.02,
            $"{box} has aspect {fittedAspect:0.###} against the picture's {sourceAspect:0.###}");
    }

    [Theory]
    [InlineData(400, 240, 320, 240)]
    [InlineData(400, 300, 400, 300)] // the surface's OWN aspect: without the floor there is no bar at all
    [InlineData(64, 64, 1, 1)]
    [InlineData(9, 9, 100, 3)]
    public void EveryFitLeavesABarForTheReadBacksControlPoint_WhateverTheAspect(
        int surfaceWidth, int surfaceHeight, int pictureWidth, int pictureHeight)
    {
        var box = VideoLetterbox.Fit(surfaceWidth, surfaceHeight, pictureWidth, pictureHeight);
        Assert.True(box.HasArea);
        Assert.True(
            box.X >= VideoLetterbox.Margin && box.Y >= VideoLetterbox.Margin,
            $"{box} must start at least {VideoLetterbox.Margin}px in on both axes: the read-back's control point "
            + "is read at (1,1) and it must be a pixel the painter FILLS and never draws picture into. A clip "
            + "whose aspect matched the surface exactly would otherwise have no bar at all");
        Assert.True(box.X + box.Width <= surfaceWidth - VideoLetterbox.Margin);
        Assert.True(box.Y + box.Height <= surfaceHeight - VideoLetterbox.Margin);
    }

    [Fact]
    public void ASurfaceTooSmallToHoldAnyPicture_FitsNOTHINGRatherThanADegenerateBox()
    {
        Assert.False(VideoLetterbox.Fit(6, 6, 320, 240).HasArea);
        Assert.False(VideoLetterbox.Fit(0, 240, 320, 240).HasArea);
        Assert.False(VideoLetterbox.Fit(400, 240, 0, 240).HasArea);
        Assert.False(VideoLetterbox.Fit(400, 240, 320, 0).HasArea);
    }

    [Fact]
    public void ComposePutsTheBarEverywhereAndThePictureONLYInsideItsBox()
    {
        const int surfaceWidth = 40;
        const int surfaceHeight = 30;
        var picture = VideoFrame.Solid(20, 10, 0xC0, 0x40, 0x20);
        var box = VideoLetterbox.Fit(surfaceWidth, surfaceHeight, picture.Width, picture.Height);
        var composed = VideoLetterbox.Compose(picture, surfaceWidth, surfaceHeight, box, 0x102030);

        Assert.Equal(surfaceWidth * surfaceHeight * VideoFrame.BytesPerPixel, composed.Length);
        Assert.Equal(0x102030u, ColourAt(composed, surfaceWidth, 1, 1));
        Assert.Equal(0x102030u, ColourAt(composed, surfaceWidth, surfaceWidth - 2, surfaceHeight - 2));
        Assert.Equal(
            picture.ColourAt(0, 0),
            ColourAt(composed, surfaceWidth, box.X + (box.Width / 2), box.Y + (box.Height / 2)));

        // Just outside the box on every side is still bar.
        Assert.Equal(0x102030u, ColourAt(composed, surfaceWidth, box.X - 1, box.Y + (box.Height / 2)));
        Assert.Equal(0x102030u, ColourAt(composed, surfaceWidth, box.X + box.Width, box.Y + (box.Height / 2)));
    }

    [Fact]
    public void ComposeIsNEARESTNEIGHBOUR_SoAReadBackCanAssertEqualityRatherThanATolerance()
    {
        // A picture of exactly two colours, scaled up. If any pixel came back as a blend of the two,
        // an equality read-back would need a tolerance — and a tolerance is how a read-back stops
        // being able to tell a picture from a smear.
        var picture = new VideoFrame(2, 2, TestAvi.VerticalSplit(2, 2, (0x10, 0x20, 0x30), (0xA0, 0xB0, 0xC0)));
        var box = VideoLetterbox.Fit(60, 60, 2, 2);
        var composed = VideoLetterbox.Compose(picture, 60, 60, box, 0x000000);

        var allowed = new[] { 0x102030u, 0xA0B0C0u, 0x000000u };
        for (var y = 0; y < 60; y++)
        {
            for (var x = 0; x < 60; x++)
            {
                Assert.Contains(ColourAt(composed, 60, x, y), allowed);
            }
        }
    }

    [Fact]
    public void ComposeSurvivesAPictureBoxThatWouldRunOffTheSurface()
    {
        var picture = VideoFrame.Solid(8, 8, 1, 2, 3);
        var composed = VideoLetterbox.Compose(picture, 10, 10, new PictureBox(8, 8, 8, 8), 0x111111);
        Assert.Equal(10 * 10 * VideoFrame.BytesPerPixel, composed.Length);
        Assert.Equal(0x010203u, ColourAt(composed, 10, 9, 9));
        Assert.Equal(0x111111u, ColourAt(composed, 10, 0, 0));
    }

    [Fact]
    public void TheSampleFingerprintTellsAPictureFromItsOwnVerticalMIRROR()
    {
        var upright = new VideoFrame(16, 16, TestAvi.VerticalSplit(16, 16, (0x10, 0x20, 0x30), (0xA0, 0xB0, 0xC0)));
        var mirrored = new VideoFrame(16, 16, TestAvi.VerticalSplit(16, 16, (0xA0, 0xB0, 0xC0), (0x10, 0x20, 0x30)));

        Assert.NotEqual(upright.Sample(), mirrored.Sample());
        Assert.Equal(upright.Sample(), new VideoFrame(16, 16, (byte[])upright.Pixels.Clone()).Sample());
    }

    [Fact]
    public void TheSurfacesSamplePointsAreASYMMETRIC_ForTheSameReason()
    {
        var box = new PictureBox(0, 0, 100, 100);
        var points = VideoLetterbox.SamplePoints(box);
        Assert.Equal(9, points.Count);

        // If the point set were symmetric about the horizontal midline, a mirrored picture would
        // sample to the same multiset of colours and the read-back could not tell them apart.
        var mirrored = points.Select(p => (p.X, Y: box.Height - 1 - p.Y)).ToHashSet();
        Assert.NotEqual(points.ToHashSet(), mirrored);
        Assert.Empty(VideoLetterbox.SamplePoints(default));
    }

    [Fact]
    public void AFrameRejectsAPixelBufferOfTheWrongSize()
    {
        Assert.Throws<ArgumentException>(() => new VideoFrame(4, 4, new byte[10]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoFrame(0, 4, new byte[0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoFrame.Solid(4, 0, 1, 2, 3));
    }

    private static uint ColourAt(byte[] pixels, int width, int x, int y)
    {
        var offset = ((y * width) + x) * VideoFrame.BytesPerPixel;
        return (uint)((pixels[offset] << 16) | (pixels[offset + 1] << 8) | pixels[offset + 2]);
    }
}
