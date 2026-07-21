using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The gate every interrupting subsystem consults before firing over a video playing in the
/// embedded browser. Two reports drove this: web videos being interrupted mid-play by Takeover /
/// AI effects / mandatory videos, and a new video starting "during or immediately after" one that
/// was already running. The first needs the playing case, the second needs the cool-off.
/// </summary>
public class BrowserMediaGateTests
{
    [Fact]
    public void WhilePlaying_DefersInterruptions()
        => Assert.True(BrowserMediaService.ResolveDeferInterruptions(
            protectEnabled: true, isPlaying: true, secondsSinceEnded: double.PositiveInfinity, graceSeconds: 45));

    [Fact]
    public void OptedOut_NeverDefers_EvenMidPlayback()
        => Assert.False(BrowserMediaService.ResolveDeferInterruptions(
            protectEnabled: false, isPlaying: true, secondsSinceEnded: 0, graceSeconds: 45));

    [Fact]
    public void NoSessionEverPlayed_DoesNotDefer()
        => Assert.False(BrowserMediaService.ResolveDeferInterruptions(
            protectEnabled: true, isPlaying: false, secondsSinceEnded: double.PositiveInfinity, graceSeconds: 45));

    [Theory]
    [InlineData(0, true)]     // the instant it ends — the "restarts immediately after" report
    [InlineData(10, true)]
    [InlineData(44.9, true)]
    [InlineData(45, false)]   // grace elapsed exactly
    [InlineData(120, false)]
    public void CoolOff_HoldsForGraceWindowAfterPlaybackEnds(double sinceEnded, bool expected)
        => Assert.Equal(expected, BrowserMediaService.ResolveDeferInterruptions(
            protectEnabled: true, isPlaying: false, secondsSinceEnded: sinceEnded, graceSeconds: 45));

    [Fact]
    public void ZeroGrace_ReleasesImmediatelyOnStop()
        => Assert.False(BrowserMediaService.ResolveDeferInterruptions(
            protectEnabled: true, isPlaying: false, secondsSinceEnded: 0, graceSeconds: 0));

    [Fact]
    public void NegativeGrace_TreatedAsZero_NotAsAlwaysDefer()
        => Assert.False(BrowserMediaService.ResolveDeferInterruptions(
            protectEnabled: true, isPlaying: false, secondsSinceEnded: 0, graceSeconds: -30));

    [Fact]
    public void ProtectionDefaultsOn_SoTheFixAppliesWithoutOptIn()
    {
        var settings = new AppSettings();
        Assert.True(settings.ProtectBrowserVideoPlayback);
        Assert.True(settings.BrowserVideoGraceSeconds > 0);
    }

    [Theory]
    [InlineData(-5, 0)]      // clamps up to the floor
    [InlineData(9999, 600)]  // clamps down to the ceiling
    [InlineData(45, 45)]
    public void GraceSeconds_ClampedTo_0_600(int input, int expected)
    {
        var settings = new AppSettings { BrowserVideoGraceSeconds = input };
        Assert.Equal(expected, settings.BrowserVideoGraceSeconds);
    }
}
