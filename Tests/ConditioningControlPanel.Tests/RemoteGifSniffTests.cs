using System.Text;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1007 - remote (Reddit/online) GIF flashes rendered as a single still, because the remote path
/// only ever pulled one WIC frame while the animated frame decode was disk-only. Remote URLs
/// usually carry no usable extension and CDNs serve generic content types, so the routing decision
/// has to come from the bytes: <see cref="FlashService.LooksLikeGif"/>.
/// </summary>
public class RemoteGifSniffTests
{
    private static byte[] Header(string s) => Encoding.ASCII.GetBytes(s);

    [Theory]
    [InlineData("GIF89a")]
    [InlineData("GIF87a")]
    public void AcceptsBothRatifiedGifSignatures(string sig)
    {
        Assert.True(FlashService.LooksLikeGif(Header(sig)));
    }

    [Fact]
    public void AcceptsARealisticGifHeaderWithTrailingBytes()
    {
        // Signature + logical screen descriptor - what actually arrives from the cache.
        var bytes = new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
                                 0x40, 0x01, 0xF0, 0x00, 0xF7, 0x00, 0x00 };
        Assert.True(FlashService.LooksLikeGif(bytes));
    }

    [Theory]
    [InlineData("GIF88a")]   // no such version
    [InlineData("GIF89b")]   // the signature must end in 'a'
    [InlineData("gif89a")]   // the signature is case-SENSITIVE
    [InlineData("RIFF00")]   // webp container
    public void RejectsNonGifHeaders(string sig)
    {
        Assert.False(FlashService.LooksLikeGif(Header(sig)));
    }

    [Fact]
    public void RejectsPngJpegEmptyAndShortBuffers()
    {
        Assert.False(FlashService.LooksLikeGif(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A }));
        Assert.False(FlashService.LooksLikeGif(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }));
        Assert.False(FlashService.LooksLikeGif(System.Array.Empty<byte>()));
        Assert.False(FlashService.LooksLikeGif(Header("GIF89")));   // one byte short of a decision
        Assert.False(FlashService.LooksLikeGif(Header("GIF")));
    }
}
