using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using ConditioningControlPanel.Services.Commands;
using Xunit;
using Pick = ConditioningControlPanel.Services.Commands.MediaCommand.MediaPick;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1325: the AI recommended one video and a random local file played instead.
/// A named video that does not resolve plays nothing; asking for "any video" stays random.
/// ccp-bugs #1330: a named video plays its closest local match, or opens its HypnoTube link.</summary>
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
    public void A_named_video_with_no_local_match_opens_its_hypnotube_link()
        => Assert.Equal(Pick.HypnoTube, MediaCommand.Decide(
            new Media("Bambi Bae", "https://hypnotube.com/video/bambi-bae-113979.html"), AICommandType.video, false, hypnoTubeLink: true));

    [Fact]
    public void A_local_match_wins_over_the_hypnotube_link()
        => Assert.Equal(Pick.Named, MediaCommand.Decide(
            new Media("Bambi Bae", "https://hypnotube.com/video/bambi-bae-113979.html"), AICommandType.video, true, hypnoTubeLink: true));

    [Fact]
    public void A_random_request_never_opens_a_link()
        => Assert.Equal(Pick.Random, MediaCommand.Decide(
            new Media("", "https://hypnotube.com/video/bambi-bae-113979.html", Random: true), AICommandType.video, false, hypnoTubeLink: true));

    [Fact]
    public void A_named_audio_never_opens_a_link()
        => Assert.Equal(Pick.Random, MediaCommand.Decide(
            new Media("t", "https://hypnotube.com/video/bambi-bae-113979.html"), AICommandType.audio, false, hypnoTubeLink: true));

    [Theory]
    [InlineData("https://hypnotube.com/video/bambi-bae-113979.html", "", "https://hypnotube.com/video/bambi-bae-113979.html")]
    [InlineData("videos/a.mp4", " https://www.hypnotube.com/video/123/ ", "https://www.hypnotube.com/video/123/")]
    [InlineData("https://example.com/video/bambi-bae-113979.html", "Bambi Bae", null)]
    [InlineData("https://hypnotube.com/videos/", "", null)]
    [InlineData("", "Bambi Bae", null)]
    public void Only_a_hypnotube_video_page_counts_as_a_link(string path, string title, string? expected)
        => Assert.Equal(expected, MediaCommand.HypnoTubeLinkOf(new Media(title, path)));

    [Fact]
    public void A_named_audio_that_does_not_resolve_keeps_its_random_fallback()
        => Assert.Equal(Pick.Random, MediaCommand.Decide(new Media("t", "nope.mp3"), AICommandType.audio, false));

    static readonly System.Collections.Generic.Dictionary<string, string> Pool = new()
    {
        ["Sissy Dreams 3"] = "https://hypnotube.com/video/sissy-dreams-3-113979.html",
        ["Bambi Bae"] = "https://hypnotube.com/video/bambi-bae-113980.html",
    };

    [Theory]
    [InlineData("Sissy Dreams 3", "", "sissy-dreams-3-113979")]
    [InlineData("sissy dreams 3", "", "sissy-dreams-3-113979")]   // case
    [InlineData("", "Bambi Bae", "bambi-bae-113980")]            // the name arrives as Path
    public void A_pool_title_resolves_to_its_hypnotube_link(string title, string path, string expect)
        => Assert.Contains(expect, MediaCommand.PoolLinkOf(new Media(title, path), Pool));

    [Fact]
    public void A_title_outside_the_pool_resolves_to_nothing()
        => Assert.Null(MediaCommand.PoolLinkOf(new Media("Totally unrelated thing", ""), Pool));

    [Fact]
    public void An_empty_pool_resolves_to_nothing()
        => Assert.Null(MediaCommand.PoolLinkOf(new Media("Bambi Bae", ""), null));
}
