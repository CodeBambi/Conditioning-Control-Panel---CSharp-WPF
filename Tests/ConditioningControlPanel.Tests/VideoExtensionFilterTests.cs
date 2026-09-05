using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The extension gate of the local video walk (#1124: a user whose videos folder produced zero
/// clips, and a Release log that said nothing about why). Pure, so the ladder that decides whether
/// a file is even a candidate can be tested without a disk scan.
/// </summary>
public class VideoExtensionFilterTests
{
    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("clip.mov")]
    [InlineData("clip.avi")]
    [InlineData("clip.wmv")]
    [InlineData("clip.mkv")]
    [InlineData("clip.webm")]
    public void TheOriginalSixAreStillAccepted(string name)
    {
        Assert.True(VideoService.IsSupportedVideoExtension(name));
    }

    [Theory]
    [InlineData("clip.m4v")]
    [InlineData("clip.mpg")]
    [InlineData("clip.mpeg")]
    [InlineData("clip.flv")]
    [InlineData("clip.ts")]
    public void TheWidenedContainersAreAccepted(string name)
    {
        // These are all demuxable, and a folder of them used to scan to zero videos and raise the
        // "no videos found" dialog on a folder the user could see was full.
        Assert.True(VideoService.IsSupportedVideoExtension(name));
    }

    [Theory]
    [InlineData("CLIP.MP4")]
    [InlineData("clip.Mkv")]
    [InlineData("clip.TS")]
    public void MatchingIsCaseInsensitive(string name)
    {
        Assert.True(VideoService.IsSupportedVideoExtension(name));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("cover.jpg")]
    [InlineData("audio.mp3")]
    [InlineData("archive.zip")]
    public void NonVideoFilesAreRejected(string name)
    {
        Assert.False(VideoService.IsSupportedVideoExtension(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no_extension_at_all")]
    [InlineData("trailing.")]
    public void MissingOrEmptyExtensionsAreRejected(string name)
    {
        Assert.False(VideoService.IsSupportedVideoExtension(name));
    }

    [Fact]
    public void AFullPathIsJudgedByItsFileExtension()
    {
        Assert.True(VideoService.IsSupportedVideoExtension(@"C:\assets\videos\subfolder\a.name.with.dots.mkv"));
        Assert.False(VideoService.IsSupportedVideoExtension(@"C:\assets\videos.mp4\readme.md"));
    }
}
