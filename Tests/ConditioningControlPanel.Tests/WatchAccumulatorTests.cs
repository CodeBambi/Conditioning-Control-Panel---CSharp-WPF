using ConditioningControlPanel.Services.Homework;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The verified watch. Only playback-sized forward steps on a visible surface count, and the video
/// has to be one the proxy will accept, so neither a seek to the end nor a pre-roll ad hands in.
/// </summary>
public class WatchAccumulatorTests
{
    private static WatchAccumulator Play(double from, double to, double duration, double step = 0.5, bool visible = true)
    {
        var acc = new WatchAccumulator();
        for (var t = from; t <= to + 1e-9; t += step) acc.Sample(t, duration, visible);
        return acc;
    }

    [Fact]
    public void WatchingStraightThroughCompletes()
    {
        var acc = Play(0, 600, 600);
        Assert.True(acc.IsComplete);
        Assert.Equal(600, acc.WatchedSeconds, 3);
    }

    [Fact]
    public void NinetyPercentIsEnough_EightyNineIsNot()
    {
        Assert.True(Play(0, 540, 600).IsComplete);
        Assert.False(Play(0, 534, 600).IsComplete);
    }

    [Fact]
    public void ASeekToTheEndCountsNothing()
    {
        var acc = new WatchAccumulator();
        acc.Sample(0, 600, true);
        acc.Sample(590, 600, true);
        acc.Sample(590.5, 600, true);
        Assert.Equal(0.5, acc.WatchedSeconds, 3);
        Assert.False(acc.IsComplete);
    }

    [Fact]
    public void RewindingCountsNothing_ButPlayingOnAfterItDoes()
    {
        var acc = new WatchAccumulator();
        acc.Sample(100, 600, true);
        acc.Sample(50, 600, true);
        acc.Sample(51, 600, true);
        Assert.Equal(1, acc.WatchedSeconds, 3);
    }

    [Fact]
    public void AStepOfExactlyOneAndAHalfCounts_JustOverDoesNot()
    {
        var acc = new WatchAccumulator();
        acc.Sample(0, 600, true);
        acc.Sample(1.5, 600, true);
        acc.Sample(3.01, 600, true);
        Assert.Equal(1.5, acc.WatchedSeconds, 3);
    }

    [Fact]
    public void AHiddenOrMinimisedSurfaceCountsNothing()
    {
        Assert.Equal(0, Play(0, 600, 600, visible: false).WatchedSeconds);
    }

    [Fact]
    public void ComingBackIntoViewDoesNotCountTheTimeAway()
    {
        var acc = new WatchAccumulator();
        acc.Sample(10, 600, true);
        acc.Sample(10.5, 600, false);
        acc.Sample(11, 600, true);
        Assert.Equal(0.5, acc.WatchedSeconds, 3);
    }

    [Fact]
    public void APreRollShorterThanThirtySecondsNeverCompletes()
    {
        Assert.False(Play(0, 20, 20).IsComplete);
    }

    [Fact]
    public void ALongerThanThreeHourVideoNeverCompletes()
    {
        var acc = Play(0, 11000, 11000, step: 1);
        Assert.False(acc.IsComplete);
    }

    [Fact]
    public void ADifferentDurationIsADifferentVideo_AndStartsOver()
    {
        var acc = Play(0, 20, 25);
        acc.Sample(0, 600, true);
        acc.Sample(0.5, 600, true);
        Assert.Equal(0.5, acc.WatchedSeconds, 3);
        Assert.Equal(600, acc.DurationSeconds);
    }

    [Fact]
    public void AnUnknownDurationDoesNotReset_AndWatchedNeverExceedsTheDuration()
    {
        var acc = new WatchAccumulator();
        acc.Sample(0, double.NaN, true);
        acc.Sample(0.5, 0, true);
        Assert.Equal(0.5, acc.WatchedSeconds, 3);
        var looped = Play(0, 40, 40);
        looped.Sample(0, 40, true);
        for (var t = 0.5; t <= 10; t += 0.5) looped.Sample(t, 40, true);
        Assert.Equal(40, looped.WatchedSeconds, 3);
    }

    [Theory]
    [InlineData("https://hypnotube.com/video/sleepy-time-12345.html", true)]
    [InlineData("https://www.hypnotube.com/video/sleepy-time-12345.html?x=1#t", true)]
    [InlineData("https://HYPNOTUBE.com/Video/Sleepy-Time-12345.html/", true)]
    [InlineData("https://hypnotube.com/video/other-99.html", false)]
    [InlineData("https://evil.com/video/sleepy-time-12345.html", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void IsHomeworkPage_MatchesHostAndPathOnly(string? page, bool expected)
    {
        Assert.Equal(expected, WatchAccumulator.IsHomeworkPage(page, "https://www.hypnotube.com/video/sleepy-time-12345.html"));
    }
}
