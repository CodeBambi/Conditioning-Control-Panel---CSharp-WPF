using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1239 part 2 - "Test video does nothing while the For You feed is open". The defer
/// itself is deliberate (#1073: the feed IS the video experience, so the scheduler must not barge
/// in on it), so the fix is not to remove it - it is that a button the user just pressed has to say
/// what happened instead of producing nothing at all.
/// </summary>
public class VideoFeedDeferNoticeTests
{
    [Fact]
    public void AButtonThePlayerPressed_IsToldWhoHasTheScreen()
        => Assert.True(VideoService.ShouldAnnounceFeedDefer(
            feedOwnsTheScreen: true, userInitiated: true, userEarned: false));

    [Fact]
    public void TheSchedulerDeferring_SaysNothing()
        => Assert.False(VideoService.ShouldAnnounceFeedDefer(
            feedOwnsTheScreen: true, userInitiated: false, userEarned: false));

    [Fact]
    public void NoFeedOnScreen_SaysNothing()
    {
        // The video is about to play, which is the only answer that press wanted.
        Assert.False(VideoService.ShouldAnnounceFeedDefer(
            feedOwnsTheScreen: false, userInitiated: true, userEarned: false));
    }

    [Fact]
    public void APoppedBubble_SaysNothing_BecauseItNeverDefers()
    {
        // #1135: a user-earned video plays OVER the feed, so there is nothing to announce.
        Assert.False(VideoService.ShouldAnnounceFeedDefer(
            feedOwnsTheScreen: true, userInitiated: true, userEarned: true));
        Assert.False(VideoService.ShouldAnnounceFeedDefer(
            feedOwnsTheScreen: true, userInitiated: false, userEarned: true));
    }
}
