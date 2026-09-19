using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class RemoteFaceAnimationTests
{
    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public void RemoteGif_PreservesFramesOnlyForAnimatedConsumers(bool animate, int count)
    {
        var encoder = new GifBitmapEncoder();
        foreach (byte red in new byte[] { 0, 255 })
        {
            var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 0, 0, red, 255 }, 4);
            encoder.Frames.Add(BitmapFrame.Create(source));
        }
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        var decoded = FlashService.DecodeRemoteStill("https://example.test/media", stream, 64, animate);
        Assert.NotNull(decoded);
        Assert.Equal(count, decoded.Frames.Count);
        if (animate)
        {
            var first = new byte[4];
            var last = new byte[4];
            new FormatConvertedBitmap(decoded.Frames[0], PixelFormats.Bgra32, null, 0).CopyPixels(first, 4, 0);
            new FormatConvertedBitmap(decoded.Frames[1], PixelFormats.Bgra32, null, 0).CopyPixels(last, 4, 0);
            Assert.NotEqual(first[2], last[2]);
        }
    }
}
