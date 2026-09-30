using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using ConditioningControlPanel.Services.Commands;
using Xunit;
using Pick = ConditioningControlPanel.Services.Commands.MediaCommand.MediaPick;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1325: the AI recommended one video and a random local file played instead.
/// A named video that does not resolve plays nothing; asking for "any video" stays random.</summary>
public class AiMediaPickTests
{
    [Fact]
    public void An_explicit_random_request_is_random()
        => Assert.Equal(Pick.Random, MediaCommand.Decide(new Media("Some title", "", Random: true), AICommandType.video, false));

    [Fact]
    public void A_request_that_names_nothing_is_random()
        => Assert.Equal(Pick.Random, MediaCommand.Decide(new Media("", "", Random: false), AICommandType.video, false));

    [Theory]
    [InlineData("A Hypnotube title", "")]                       // title only, no local path
    [InlineData("A Hypnotube title", "https://hypnotube.com/video/x")]
    [InlineData("", "made-up-file.mp4")]                         // a hallucinated file name
    public void A_named_video_that_does_not_resolve_plays_nothing(string title, string path)
        => Assert.Equal(Pick.Nothing, MediaCommand.Decide(new Media(title, path), AICommandType.video, false));

    [Fact]
    public void A_named_video_that_resolves_plays_it()
        => Assert.Equal(Pick.Named, MediaCommand.Decide(new Media("t", "videos/a.mp4"), AICommandType.video, true));

    [Fact]
    public void A_named_audio_that_does_not_resolve_keeps_its_random_fallback()
        => Assert.Equal(Pick.Random, MediaCommand.Decide(new Media("t", "nope.mp3"), AICommandType.audio, false));
}
