using System;
using ConditioningControlPanel.Services;
using Xunit;
using static ConditioningControlPanel.Services.VideoService;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1135 "popping a video bubble does nothing about 75% of the time".
///
/// VideoService guards the trigger path with a single bool, _triggerInProgress. It is claimed at the
/// top of TriggerVideo and released at the top of PlayVideo, and for a long time the only carrier
/// between those two points was a bounded dispatcher Invoke that ABANDONS its queued work after five
/// seconds and returns normally. On a busy UI thread the video therefore never happened, nothing
/// released the flag, and every later trigger - every video bubble the user popped - was dropped as
/// "already in progress" until the InteractionQueue's 5-minute stuck detector force-cleaned. No error
/// was logged anywhere, which is exactly the shape of the report.
///
/// The dispatcher call is now fire-and-forget with its own catch, and the guard no longer trusts the
/// flag indefinitely. <see cref="VideoService.EvaluateTriggerGuard"/> is that escape hatch, pulled out
/// as pure logic so it can be tested without LibVLC (the same seam EvaluateGraceRequest uses).
/// </summary>
public class VideoTriggerGuardTests
{
    [Fact]
    public void NothingInFlight_Proceeds()
    {
        Assert.Equal(TriggerGuardDecision.Proceed,
            EvaluateTriggerGuard(triggerInProgress: false, sinceTriggerStarted: TimeSpan.Zero));
    }

    [Fact]
    public void NothingInFlight_ProceedsEvenWithAnAncientTimestamp()
    {
        // The timestamp is only meaningful while the flag is set; a stale one from a long-finished
        // trigger must not be mistaken for a stall.
        Assert.Equal(TriggerGuardDecision.Proceed,
            EvaluateTriggerGuard(triggerInProgress: false, sinceTriggerStarted: TimeSpan.FromHours(3)));
    }

    [Fact]
    public void GenuineOverlap_IsStillSkipped()
    {
        // Two triggers within the 800ms freeze delay is a real overlap, not a stall. Dropping the
        // second one is the behaviour the guard exists for and must survive the fix.
        Assert.Equal(TriggerGuardDecision.Skip,
            EvaluateTriggerGuard(triggerInProgress: true, sinceTriggerStarted: TimeSpan.FromMilliseconds(400)));
    }

    [Fact]
    public void LongButPlausibleTrigger_IsStillSkipped()
    {
        // A slow disk or a pack decrypt can legitimately put seconds between the claim and PlayVideo.
        Assert.Equal(TriggerGuardDecision.Skip,
            EvaluateTriggerGuard(triggerInProgress: true, sinceTriggerStarted: TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void RightBelowTheCeiling_IsStillSkipped()
    {
        Assert.Equal(TriggerGuardDecision.Skip,
            EvaluateTriggerGuard(triggerInProgress: true,
                sinceTriggerStarted: TriggerStallCeiling - TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void AtTheCeiling_ClearsTheStaleTrigger()
    {
        Assert.Equal(TriggerGuardDecision.ClearStaleAndProceed,
            EvaluateTriggerGuard(triggerInProgress: true, sinceTriggerStarted: TriggerStallCeiling));
    }

    [Fact]
    public void LatchedForever_ClearsTheStaleTrigger()
    {
        // The reported case: the flag never came back down, so without this the user's next bubble,
        // and the one after that, and every one until the 5-minute stuck detector, did nothing.
        Assert.Equal(TriggerGuardDecision.ClearStaleAndProceed,
            EvaluateTriggerGuard(triggerInProgress: true, sinceTriggerStarted: TimeSpan.FromMinutes(4)));
    }

    [Fact]
    public void CeilingIsWellClearOfARealTrigger_ButWellInsideTheStuckDetector()
    {
        // If the ceiling drops near a real trigger's length the escape starts double-launching videos;
        // if it climbs past the InteractionQueue's 5-minute force-clean it is pointless.
        Assert.True(TriggerStallCeiling > TimeSpan.FromSeconds(15),
            "ceiling must stay clear of a slow but honest trigger");
        Assert.True(TriggerStallCeiling < TimeSpan.FromMinutes(5),
            "ceiling must fire before the InteractionQueue stuck detector does");
    }
}
