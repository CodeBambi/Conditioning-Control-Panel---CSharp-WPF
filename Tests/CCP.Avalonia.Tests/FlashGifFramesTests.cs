using System;
using System.IO;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF 7.1.5 animated flashes on this head: a GIF flash plays (it used to show frame 0
/// only), at the user's GIF speed, under WPF's frame budget.</summary>
public sealed class FlashGifFramesTests
{
    // A 1x1, two-frame GIF89a (black then white, 100 ms each, looping).
    private static readonly byte[] TwoFrameGif =
    {
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
        0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF,
        0x21, 0xFF, 0x0B, 0x4E, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2E, 0x30, 0x03, 0x01, 0x00, 0x00, 0x00,
        0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00,
        0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x4C, 0x01, 0x00,
        0x3B,
    };

    [Fact]
    public void An_animated_gif_decodes_every_frame_at_display_size()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccp-flashgif-{Guid.NewGuid():N}.gif");
        File.WriteAllBytes(path, TwoFrameGif);
        try
        {
            var anim = FlashGifFrames.Decode(path, 8, 6);
            Assert.NotNull(anim);
            Assert.Equal(2, anim!.Value.Frames.Count);
            Assert.Equal(8, anim.Value.Frames[0].PixelSize.Width);
            Assert.Equal(6, anim.Value.Frames[0].PixelSize.Height);
            Assert.Equal(100, anim.Value.FrameDelay.TotalMilliseconds);
            foreach (var f in anim.Value.Frames) f.Dispose();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_still_or_missing_file_keeps_the_still_path()
    {
        Assert.Null(FlashGifFrames.Decode(Path.Combine(Path.GetTempPath(), "ccp-no-such.gif"), 10, 10));
    }

    /// <summary>WPF FlashService.ScaleFrameDelay: speed 0.25..4, floor 10 ms, junk falls back.</summary>
    [Theory]
    [InlineData(100, 2.0, 50)]
    [InlineData(100, 10.0, 25)]
    [InlineData(20, 4.0, 10)]
    [InlineData(0, 1.0, 100)]
    [InlineData(100, double.NaN, 100)]
    public void The_gif_speed_setting_scales_the_frame_delay(double sourceMs, double mult, double expectMs)
    {
        Assert.Equal(expectMs, FlashGifFrames.ScaleFrameDelay(TimeSpan.FromMilliseconds(sourceMs), mult).TotalMilliseconds);
    }

    /// <summary>WPF #683: a ceiling stride keeps the whole clip, never a head slice.</summary>
    [Fact]
    public void A_long_clip_is_subsampled_across_its_whole_length()
    {
        var (step, decode, keep) = FlashGifFrames.Plan(100, 60);
        Assert.Equal(2, step);
        Assert.Equal(100, decode);
        Assert.Equal(60, keep);
        Assert.Equal(600, FlashGifFrames.Plan(900, 60).DecodeCount);
    }
}
