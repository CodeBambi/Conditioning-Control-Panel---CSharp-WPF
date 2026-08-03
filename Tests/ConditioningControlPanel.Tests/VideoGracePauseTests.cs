using System;
using ConditioningControlPanel.Services;
using Xunit;
using static ConditioningControlPanel.Services.VideoService;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #735 "grace pause" — the first panic press while a mandatory video is really on screen pauses it
/// behind a Paused/Resume card instead of stopping the engine. Three pieces of pure logic carry the
/// feature and all three have sharp edges:
///
///   1. <see cref="VideoService.EvaluateGraceRequest"/> — the ladder decision, including the 200ms
///      dedup that stops ONE physical keystroke from both pausing (window PreviewKeyDown) and then
///      stopping the engine (the global hook's queued handler).
///   2. <see cref="VideoService.RemainingTimerInterval"/> — a 60s pause must not make the duration
///      guillotine or the max-length cap fire 60s early.
///   3. The countdown arithmetic + auto-resume trigger.
/// </summary>
public class VideoGracePauseTests
{
    // ---- 1. the ladder decision ----

    [Fact]
    public void PlayingVideo_FirstPress_Pauses()
    {
        var d = EvaluateGraceRequest(videoPlaying: true, cleaningUp: false, alreadyPaused: false,
            consumed: false, attentionTargetLive: false, msSinceLastGraceAction: double.MaxValue);
        Assert.Equal(GraceDecision.Pause, d);
    }

    [Fact]
    public void NoVideoPlaying_FallsThroughToNormalPanic()
    {
        var d = EvaluateGraceRequest(videoPlaying: false, cleaningUp: false, alreadyPaused: false,
            consumed: false, attentionTargetLive: false, msSinceLastGraceAction: double.MaxValue);
        Assert.Equal(GraceDecision.FallThrough, d);
    }

    [Fact]
    public void MidTeardown_FallsThroughToNormalPanic()
    {
        // A video already being torn down has nothing to pause, and swallowing the press would
        // strand the user staring at a dying fullscreen.
        var d = EvaluateGraceRequest(videoPlaying: true, cleaningUp: true, alreadyPaused: false,
            consumed: false, attentionTargetLive: false, msSinceLastGraceAction: double.MaxValue);
        Assert.Equal(GraceDecision.FallThrough, d);
    }

    [Fact]
    public void AlreadyPaused_SecondPress_IsTodaysNormalPanicPress()
    {
        var d = EvaluateGraceRequest(videoPlaying: true, cleaningUp: false, alreadyPaused: true,
            consumed: false, attentionTargetLive: false, msSinceLastGraceAction: 5000);
        Assert.Equal(GraceDecision.FallThrough, d);
    }

    [Fact]
    public void LiveAttentionTarget_RefusesThePause()
    {
        // Pausing mid-"CLICK ME" would break the mini-game timing. The press must still DO something,
        // so it falls through rather than being swallowed.
        var d = EvaluateGraceRequest(videoPlaying: true, cleaningUp: false, alreadyPaused: false,
            consumed: false, attentionTargetLive: true, msSinceLastGraceAction: double.MaxValue);
        Assert.Equal(GraceDecision.FallThrough, d);
    }

    // ---- 1b. the 200ms double-fire dedup ----

    [Fact]
    public void SameKeystroke_WithinDedupWindow_IsSwallowed()
    {
        // The global hook's queued HandlePanicKeyPress arriving a few ms after the window handler
        // already paused. Note alreadyPaused is TRUE by then — without the dedup this exact call
        // would return FallThrough and stop the engine.
        var d = EvaluateGraceRequest(videoPlaying: true, cleaningUp: false, alreadyPaused: true,
            consumed: false, attentionTargetLive: false, msSinceLastGraceAction: 12);
        Assert.Equal(GraceDecision.ConsumedDedup, d);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(199)]
    public void InsideDedupBoundary_IsConsumed(double sinceMs)
    {
        var d = EvaluateGraceRequest(videoPlaying: true, cleaningUp: false, alreadyPaused: true,
            consumed: false, attentionTargetLive: false, msSinceLastGraceAction: sinceMs);
        Assert.Equal(GraceDecision.ConsumedDedup, d);
    }

    [Theory]
    [InlineData(200)]   // exactly at the boundary — no longer the same keystroke
    [InlineData(201)]
    [InlineData(350)]   // a genuinely fast human double-tap
    public void AtOrPastDedupBoundary_IsARealSecondPress(double sinceMs)
    {
        Assert.Equal(GraceDedupMs, 200);
        var d = EvaluateGraceRequest(videoPlaying: true, cleaningUp: false, alreadyPaused: true,
            consumed: false, attentionTargetLive: false, msSinceLastGraceAction: sinceMs);
        Assert.Equal(GraceDecision.FallThrough, d);
    }

    [Fact]
    public void DedupDoesNotDependOnTheVideoStillPlaying()
    {
        // The window handler paused; by the time the queued global handler runs the video could
        // already be gone (a racing natural end). The duplicate must STILL be swallowed — resurrecting
        // it as a normal panic press would stop the engine off one keystroke.
        var d = EvaluateGraceRequest(videoPlaying: false, cleaningUp: true, alreadyPaused: false,
            consumed: true, attentionTargetLive: false, msSinceLastGraceAction: 30);
        Assert.Equal(GraceDecision.ConsumedDedup, d);
    }

    // ---- 1c. one grace pause per video run ----

    [Fact]
    public void ConsumedGrace_FallsThroughForTheRestOfTheRun()
    {
        var d = EvaluateGraceRequest(videoPlaying: true, cleaningUp: false, alreadyPaused: false,
            consumed: true, attentionTargetLive: false, msSinceLastGraceAction: double.MaxValue);
        Assert.Equal(GraceDecision.FallThrough, d);
    }

    [Fact]
    public void FreshVideo_GetsAFreshGracePause()
    {
        // PlayVideo clears the consumed flag on every new run (including each attention-check retry,
        // which is a fresh GetNextVideo through the same method), so the very next press pauses again.
        var consumed = true;
        consumed = false;   // what PlayVideo does
        var d = EvaluateGraceRequest(videoPlaying: true, cleaningUp: false, alreadyPaused: false,
            consumed: consumed, attentionTargetLive: false, msSinceLastGraceAction: double.MaxValue);
        Assert.Equal(GraceDecision.Pause, d);
    }

    // ---- 2. guard-timer remaining-time arithmetic ----

    [Fact]
    public void Remaining_IsIntervalMinusElapsed()
    {
        var armed = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        var remaining = RemainingTimerInterval(armed, TimeSpan.FromSeconds(305), armed.AddSeconds(40));
        Assert.Equal(265, remaining.TotalSeconds, 3);
    }

    [Fact]
    public void Remaining_OfANeverArmedTimer_IsZero_SoResumeDoesNotArmIt()
    {
        Assert.Equal(TimeSpan.Zero, RemainingTimerInterval(default, TimeSpan.FromSeconds(60), DateTime.UtcNow));
        Assert.Equal(TimeSpan.Zero, RemainingTimerInterval(DateTime.UtcNow, TimeSpan.Zero, DateTime.UtcNow));
    }

    [Fact]
    public void Remaining_NeverGoesNegativeOrZero()
    {
        var armed = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        var remaining = RemainingTimerInterval(armed, TimeSpan.FromSeconds(10), armed.AddSeconds(999));
        Assert.True(remaining > TimeSpan.Zero, "a re-armed timer must never be armed at 0ms");
    }

    [Fact]
    public void SixtySecondPause_DoesNotMoveTheGuillotineRelativeToTheClip()
    {
        // A 100s clip: safety timer armed for 105s at t=0. Pause at t=10, resume at t=70 wall.
        // The re-armed timer must still fire at clip position 105s, i.e. 95s after the resume —
        // not 60s early (the whole point of capturing the remainder).
        var armed = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        var interval = TimeSpan.FromSeconds(105);
        var pausedAt = armed.AddSeconds(10);
        var remaining = RemainingTimerInterval(armed, interval, pausedAt);
        Assert.Equal(95, remaining.TotalSeconds, 3);

        var resumedAt = pausedAt.AddSeconds(60);
        var firesAt = resumedAt + remaining;
        // 105s of actual playback happened by then (10 before the pause + 95 after).
        Assert.Equal(TimeSpan.FromSeconds(165), firesAt - armed);
    }

    // ---- 3. countdown + auto-resume ----

    [Fact]
    public void Countdown_StartsAtTheFullWindowAndCountsDown()
    {
        Assert.Equal(60, GraceWindowSeconds);
        Assert.Equal(60, GraceSecondsRemaining(0, GraceWindowSeconds));
        Assert.Equal(59, GraceSecondsRemaining(1.0, GraceWindowSeconds));
        Assert.Equal(59, GraceSecondsRemaining(1.2, GraceWindowSeconds));  // rounds UP, so 59 shows for a full second
        Assert.Equal(1, GraceSecondsRemaining(59.4, GraceWindowSeconds));
    }

    [Fact]
    public void Countdown_ClampsAtZero_NeverShowsANegative()
    {
        Assert.Equal(0, GraceSecondsRemaining(60, GraceWindowSeconds));
        Assert.Equal(0, GraceSecondsRemaining(9999, GraceWindowSeconds));
    }

    [Fact]
    public void CountdownReachingZero_TriggersTheAutoResume()
    {
        Assert.False(ShouldAutoResume(0, GraceWindowSeconds));
        Assert.False(ShouldAutoResume(59.9, GraceWindowSeconds));
        Assert.True(ShouldAutoResume(60, GraceWindowSeconds));
        Assert.True(ShouldAutoResume(75, GraceWindowSeconds));  // a starved dispatcher tick still resumes
    }
}
