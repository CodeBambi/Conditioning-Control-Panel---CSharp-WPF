using System;
using System.Collections.Generic;
using Xunit;
using static ConditioningControlPanel.Services.VideoSurfaceHealth;
using OverlayService = ConditioningControlPanel.Services.OverlayService;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The #1 open bug: mandatory video on a multi-monitor rig plays on the secondary screens while the
/// primary sits black and silent (#533 #540 #542 #559 #592 #617 #918 #1015 #1016 #1024 #1025 #1035
/// #1039 #1059, 6.3.2 through 6.8.4). Every rule the fix rests on is pure and lives here; the
/// windowing itself needs a real second monitor and is listed as a play-test.
/// </summary>
public class MultiMonitorVideoSurfaceTests
{
    // ---- per-surface frame watchdog (deliverable 2) ----

    [Fact]
    public void FrameWatchdog_SurfaceThatRendered_IsLeftAlone()
    {
        Assert.Equal(FrameWatchdogAction.Ignore,
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: true, retryUsed: false, retryAllowed: true));
        Assert.Equal(FrameWatchdogAction.Ignore,
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: true, retryUsed: true, retryAllowed: false));
    }

    [Fact]
    public void FrameWatchdog_TeardownBeatsEverything()
    {
        // A late timer tick must never act on a clip that already ended - not even to retry.
        Assert.Equal(FrameWatchdogAction.Ignore,
            DecideFrameWatchdog(tornDown: true, gracePaused: false, hasRendered: false, retryUsed: true, retryAllowed: true));
        Assert.Equal(FrameWatchdogAction.Ignore,
            DecideFrameWatchdog(tornDown: true, gracePaused: true, hasRendered: false, retryUsed: false, retryAllowed: false));
    }

    [Fact]
    public void FrameWatchdog_GracePauseDefers_ItDoesNotCondemn()
    {
        // #735: a deliberately paused vmem surface produces no frames BY DESIGN.
        Assert.Equal(FrameWatchdogAction.Defer,
            DecideFrameWatchdog(tornDown: false, gracePaused: true, hasRendered: false, retryUsed: false, retryAllowed: true));
        Assert.Equal(FrameWatchdogAction.Defer,
            DecideFrameWatchdog(tornDown: false, gracePaused: true, hasRendered: false, retryUsed: true, retryAllowed: false));
    }

    [Fact]
    public void FrameWatchdog_AMirrorGetsOneRetry_ThenGivesUp()
    {
        Assert.Equal(FrameWatchdogAction.Retry,
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: false, retryUsed: false, retryAllowed: true));
        Assert.Equal(FrameWatchdogAction.GiveUp,
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: false, retryUsed: true, retryAllowed: true));
    }

    [Fact]
    public void FrameWatchdog_ASurfaceWithNoRetryRung_IsCondemnedOnItsFirstMissedWindow()
    {
        Assert.Equal(FrameWatchdogAction.GiveUp,
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: false, retryUsed: false, retryAllowed: false));
    }

    [Fact]
    public void FrameWatchdog_AMultiMonitorPrimary_GetsTheSameOneRetry()
    {
        // The exact hole round 2 caught: the audio-bearing surface used to be condemned on its FIRST
        // missed window on EVERY rig, so a dual-monitor user whose primary stalled got the released
        // build's behaviour verbatim (skip, no re-Play, no recovery rung of any kind) - i.e. the
        // headline trace "blurred-background video produced no frame within 8000ms on primary
        // \\.\DISPLAY1 - skipping to next" was unchanged by the whole branch.
        Assert.Equal(FrameWatchdogAction.Retry,
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: false, retryUsed: false, retryAllowed: true));
        Assert.Equal(FrameWatchdogAction.GiveUp,
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: false, retryUsed: true, retryAllowed: true));
    }

    // ---- who is allowed a retry rung: the RIG decides, not the role ----

    [Fact]
    public void RetryRung_AMirrorAlwaysGetsOne()
    {
        // The clip keeps playing while a mirror retries, so the second window costs the user nothing.
        Assert.True(AllowsFrameRetry(primarySurface: false, armedSurfaces: 1));
        Assert.True(AllowsFrameRetry(primarySurface: false, armedSurfaces: 2));
        Assert.True(AllowsFrameRetry(primarySurface: false, armedSurfaces: 4));
    }

    [Fact]
    public void RetryRung_APrimaryWithSiblingsGetsOne()
    {
        // Multi-monitor: one re-Play() is the only thing between "the decoder hiccuped while three
        // screens spun up at once" and "the video was skipped".
        Assert.True(AllowsFrameRetry(primarySurface: true, armedSurfaces: 2));
        Assert.True(AllowsFrameRetry(primarySurface: true, armedSurfaces: 3));
    }

    [Fact]
    public void RetryRung_ALonePrimaryAlsoGetsOne()
    {
        // #1121: the single-monitor rig used to be the one rig with no recovery at all - the only
        // surface it has was the only surface denied a re-Play(), so a decoder that came up late
        // meant a black screen and a skipped clip. One rung, worst case one more grace window.
        Assert.True(AllowsFrameRetry(primarySurface: true, armedSurfaces: 1));
        // Defensive: a watch judged before it was registered has nothing to re-Play.
        Assert.False(AllowsFrameRetry(primarySurface: true, armedSurfaces: 0));
    }

    [Fact]
    public void DualMonitorPrimary_StillEndsTheClip_JustOneGraceWindowLater()
    {
        // Round 1's blocker holds: a dead primary must end the clip. The retry rung only moves that
        // from ~8s to ~16s, and only on a rig where something else could have starved it.
        bool allowed = AllowsFrameRetry(primarySurface: true, armedSurfaces: 2);
        Assert.Equal(FrameWatchdogAction.Retry,
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: false, retryUsed: false, retryAllowed: allowed));
        Assert.Equal(FrameWatchdogAction.GiveUp,
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: false, retryUsed: true, retryAllowed: allowed));
        Assert.True(ShouldAbortClip(totalSurfaces: 2, deadSurfaces: 1, primarySurfaceDead: true));
    }

    // ---- liveness aggregation (deliverable 2) ----

    [Fact]
    public void OneDeadMIRROR_DoesNotAbortTheClip()
    {
        // THE regression this whole branch exists for: the old code ended the clip on every screen
        // the moment one surface missed its frame budget. A dead mirror is now survivable...
        Assert.False(ShouldAbortClip(totalSurfaces: 2, deadSurfaces: 1, primarySurfaceDead: false));
        Assert.False(ShouldAbortClip(totalSurfaces: 3, deadSurfaces: 2, primarySurfaceDead: false));
    }

    [Fact]
    public void ADeadPRIMARY_AbortsTheClip_EvenWhileMirrorsStillRender()
    {
        // ...but a dead AUDIO-BEARING surface is not. It is the only player wired to EndReached /
        // EncounteredError / LengthChanged, and the blurred path arms no vout watchdog, so a clip
        // carried by mirrors alone would have no end condition short of the 10-minute fallback
        // timer: black and silent on the main screen for minutes, un-closable in strict mode. That
        // is the headline report (#533 #1015 #1024 #1035 #1039), so it must skip immediately.
        Assert.True(ShouldAbortClip(totalSurfaces: 2, deadSurfaces: 1, primarySurfaceDead: true));
        Assert.True(ShouldAbortClip(totalSurfaces: 4, deadSurfaces: 1, primarySurfaceDead: true));
    }

    [Fact]
    public void EverySurfaceDead_AbortsTheClip()
    {
        Assert.True(ShouldAbortClip(totalSurfaces: 1, deadSurfaces: 1, primarySurfaceDead: true));
        Assert.True(ShouldAbortClip(totalSurfaces: 2, deadSurfaces: 2, primarySurfaceDead: false));
        // Defensive: a double-report must not read as "still alive".
        Assert.True(ShouldAbortClip(totalSurfaces: 2, deadSurfaces: 3, primarySurfaceDead: false));
    }

    [Fact]
    public void SingleMonitorRig_GetsOneRetryAndThenStillAborts()
    {
        // One screen means one surface and it IS the primary. #1121 gives it the same single rung
        // every other surface has: first missed window -> Retry, second -> GiveUp -> abort. Driven
        // through AllowsFrameRetry so the rig rule and the ladder cannot drift apart.
        bool allowed = AllowsFrameRetry(primarySurface: true, armedSurfaces: 1);
        Assert.True(allowed);
        Assert.Equal(FrameWatchdogAction.Retry,
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: false, retryUsed: false, retryAllowed: allowed));
        Assert.Equal(FrameWatchdogAction.GiveUp,
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: false, retryUsed: true, retryAllowed: allowed));
        Assert.True(ShouldAbortClip(totalSurfaces: 1, deadSurfaces: 1, primarySurfaceDead: true));
    }

    [Fact]
    public void NoSurfacesAtAll_IsNotAnAbort()
    {
        // Nothing was ever built, so there is nothing for this watchdog to end; the pre-roll
        // watchdogs own that case.
        Assert.False(ShouldAbortClip(totalSurfaces: 0, deadSurfaces: 0, primarySurfaceDead: false));
        Assert.False(ShouldAbortClip(totalSurfaces: 0, deadSurfaces: 5, primarySurfaceDead: false));
    }

    // ---- browser failure policy (deliverable 1) ----

    [Fact]
    public void SecondaryBrowserFailure_NeverEndsTheRun()
    {
        Assert.Equal(BrowserFailureAction.DropSecondary,
            DecideBrowserFailure(isPrimarySurface: false, alreadyFellBack: false, playbackStartedFired: false));
        Assert.Equal(BrowserFailureAction.DropSecondary,
            DecideBrowserFailure(isPrimarySurface: false, alreadyFellBack: true, playbackStartedFired: true));
    }

    [Fact]
    public void PrimaryFailsBeforeFirstFrame_FallsTheClipBackToLibVlc()
    {
        Assert.Equal(BrowserFailureAction.FallbackWholeClip,
            DecideBrowserFailure(isPrimarySurface: true, alreadyFellBack: false, playbackStartedFired: false));
    }

    [Fact]
    public void PrimaryFailsMidClip_EndsTheRunInsteadOfReplayingIt()
    {
        // The user has already watched most of it; a fallback here would restart from zero.
        Assert.Equal(BrowserFailureAction.EndClip,
            DecideBrowserFailure(isPrimarySurface: true, alreadyFellBack: false, playbackStartedFired: true));
    }

    [Fact]
    public void FallbackHappensAtMostOnce()
    {
        Assert.Equal(BrowserFailureAction.Ignore,
            DecideBrowserFailure(isPrimarySurface: true, alreadyFellBack: true, playbackStartedFired: false));
        Assert.Equal(BrowserFailureAction.Ignore,
            DecideBrowserFailure(isPrimarySurface: true, alreadyFellBack: true, playbackStartedFired: true));
    }

    // ---- browser per-surface first-frame sweep (deliverables 2 + 6, DEFAULT engine) ----
    //
    // BrowserVideoEngineEnabled defaults true and the browser engine drives EVERY screen, so for most
    // users an mp4 mandatory video is a WebView2 session on all monitors. The engine used to watch the
    // PRIMARY only, and to disarm the moment the primary posted `playing` - so a mirror that came up,
    // handshook and then never decoded was never noticed, never dropped and never logged.

    [Fact]
    public void BrowserSweep_ASurfaceThatRendered_IsLeftAlone()
    {
        Assert.Equal(BrowserFrameSweepAction.Ignore,
            DecideBrowserFrameSweep(firstFrameSeen: true, deadlinePassed: true, isPrimarySurface: true));
        Assert.Equal(BrowserFrameSweepAction.Ignore,
            DecideBrowserFrameSweep(firstFrameSeen: true, deadlinePassed: true, isPrimarySurface: false));
    }

    [Fact]
    public void BrowserSweep_BeforeItsOwnDeadline_ASurfaceIsJustWaiting()
    {
        // Each window carries its own deadline (pre-handshake budget, restarted shorter at `ready`),
        // so a slow mirror is never judged on the primary's clock.
        Assert.Equal(BrowserFrameSweepAction.Wait,
            DecideBrowserFrameSweep(firstFrameSeen: false, deadlinePassed: false, isPrimarySurface: false));
        Assert.Equal(BrowserFrameSweepAction.Wait,
            DecideBrowserFrameSweep(firstFrameSeen: false, deadlinePassed: false, isPrimarySurface: true));
    }

    [Fact]
    public void BrowserSweep_ABlackMirrorIsReportedAndDropped_NotLeftOnScreen()
    {
        // "Monitor 2 is permanently black": an opaque fullscreen window with a page that never
        // decoded. It gets its own surface line and then goes away; the clip is untouched.
        Assert.Equal(BrowserFrameSweepAction.DropMirror,
            DecideBrowserFrameSweep(firstFrameSeen: false, deadlinePassed: true, isPrimarySurface: false));
    }

    [Fact]
    public void BrowserSweep_ABlackPrimaryFailsTheSession_SoLibVlcCanReplayTheClip()
    {
        Assert.Equal(BrowserFrameSweepAction.FailSession,
            DecideBrowserFrameSweep(firstFrameSeen: false, deadlinePassed: true, isPrimarySurface: true));
    }

    [Fact]
    public void BrowserSweep_AHealthyPrimaryDoesNotExcuseABlackMirror()
    {
        // The regression in one pair of asserts: the primary reporting `playing` used to disarm the
        // whole watch, which is precisely why the mirror below was never judged.
        Assert.Equal(BrowserFrameSweepAction.Ignore,
            DecideBrowserFrameSweep(firstFrameSeen: true, deadlinePassed: true, isPrimarySurface: true));
        Assert.Equal(BrowserFrameSweepAction.DropMirror,
            DecideBrowserFrameSweep(firstFrameSeen: false, deadlinePassed: true, isPrimarySurface: false));
    }

    // ---- retire deferral (deliverable 4) ----

    [Fact]
    public void ForeignWedgeDuringPlayback_DefersTheRetire()
    {
        // A bubble-count / mini-player lease wedging must not pull the shared LibVLC instance out
        // from under the per-monitor players that are decoding the video on screen right now.
        Assert.True(ShouldDeferRetire(fromCurrentPlayback: false, playbackLive: true));
    }

    [Fact]
    public void RetireRunsImmediately_WhenNothingIsPlaying()
    {
        Assert.False(ShouldDeferRetire(fromCurrentPlayback: false, playbackLive: false));
    }

    [Fact]
    public void TheClipsOwnWedge_IsNeverDeferred()
    {
        // The vout heal, the wedge ladder and CloseAll's own quarantine ARE the current playback;
        // deferring them would disarm the self-heal entirely. Those are also the retire sites the
        // bug traces actually show, so this rule deliberately does NOT cover them - the deferral is
        // only about a FOREIGN lease reaching under a live clip.
        Assert.False(ShouldDeferRetire(fromCurrentPlayback: true, playbackLive: true));
        Assert.False(ShouldDeferRetire(fromCurrentPlayback: true, playbackLive: false));
    }

    // ---- the diagnostics line reporters upload (video-diag.log) ----

    [Fact]
    public void SurfaceLine_CarriesEngineMonitorRoleAndLatency()
    {
        Assert.Equal(
            @"engine=browser monitor=\\.\DISPLAY1 role=primary firstFrame=412ms",
            FormatSurfaceLine("browser", @"\\.\DISPLAY1", primary: true, firstFrameMs: 412, failureReason: null));
    }

    [Fact]
    public void SurfaceLine_NoFrameReadsAsNoneAndCarriesTheReason()
    {
        Assert.Equal(
            @"engine=libvlc monitor=\\.\DISPLAY2 role=secondary firstFrame=none reason=no frame within 8000ms",
            FormatSurfaceLine("libvlc", @"\\.\DISPLAY2", primary: false, firstFrameMs: -1,
                failureReason: "no frame within 8000ms"));
    }

    [Fact]
    public void SurfaceLine_StaysOnOneLine_EvenWithAMultiLineExceptionMessage()
    {
        var line = FormatSurfaceLine("browser", "MON", primary: true, firstFrameMs: -1,
            failureReason: "InitAsync failed:\r\nWebView2 runtime missing");
        Assert.DoesNotContain("\n", line);
        Assert.DoesNotContain("\r", line);
        Assert.Contains("reason=InitAsync failed:  WebView2 runtime missing", line);
    }

    [Fact]
    public void SurfaceLine_MissingFieldsDegradeInsteadOfThrowing()
    {
        Assert.Equal("engine=? monitor=? role=secondary firstFrame=none",
            FormatSurfaceLine(null, "   ", primary: false, firstFrameMs: -1, failureReason: null));
    }

    // ---- multi-monitor z-order anchor (deliverable 3, #1016) ----

    private static readonly IntPtr Mon1 = new IntPtr(0x1001);
    private static readonly IntPtr Mon2 = new IntPtr(0x1002);
    private static readonly IntPtr Video1 = new IntPtr(0xA001);
    private static readonly IntPtr Video2 = new IntPtr(0xA002);
    private static readonly IntPtr Overlay2 = new IntPtr(0xB002);

    private static List<(IntPtr Hwnd, IntPtr Monitor)> TwoScreenVideo() =>
        new() { (Video1, Mon1), (Video2, Mon2) };

    [Fact]
    public void OverlayOnSecondMonitor_AnchorsToThatMonitorsVideoWindow()
    {
        // #1016 itself: anchoring to the primary left the overlay ABOVE monitor 2's video window,
        // which is the "pink filter covers the video on my second screen" report.
        Assert.Equal(Video2, OverlayService.ResolveVideoAnchor(
            TwoScreenVideo(), targetHwnd: Overlay2, targetMonitor: Mon2, fallbackHwnd: Video1));
    }

    [Fact]
    public void OverlayOnPrimaryMonitor_StillAnchorsToThePrimaryVideoWindow()
    {
        Assert.Equal(Video1, OverlayService.ResolveVideoAnchor(
            TwoScreenVideo(), targetHwnd: new IntPtr(0xB001), targetMonitor: Mon1, fallbackHwnd: Video1));
    }

    [Fact]
    public void OverlayOnAMonitorWithNoVideoWindow_FallsBackToThePrimary()
    {
        // Nothing overlaps there, so the choice is cosmetic - but it must be a real window, not zero.
        Assert.Equal(Video1, OverlayService.ResolveVideoAnchor(
            TwoScreenVideo(), targetHwnd: new IntPtr(0xB003), targetMonitor: new IntPtr(0x1003),
            fallbackHwnd: Video1));
    }

    [Fact]
    public void NoVideoPlaying_YieldsNoAnchor()
    {
        Assert.Equal(IntPtr.Zero, OverlayService.ResolveVideoAnchor(
            new List<(IntPtr, IntPtr)>(), targetHwnd: Overlay2, targetMonitor: Mon2,
            fallbackHwnd: IntPtr.Zero));
    }

    [Fact]
    public void AVideoWindowIsNeverAnchoredToAnotherVideoWindow()
    {
        // Ordering a video window below its sibling would bury one of the screens outright.
        Assert.Equal(IntPtr.Zero, OverlayService.ResolveVideoAnchor(
            TwoScreenVideo(), targetHwnd: Video2, targetMonitor: Mon2, fallbackHwnd: Video1));
        Assert.Equal(IntPtr.Zero, OverlayService.ResolveVideoAnchor(
            TwoScreenVideo(), targetHwnd: Video1, targetMonitor: Mon1, fallbackHwnd: Video1));
    }

    [Fact]
    public void AWindowIsNeverAnchoredToItself()
    {
        // SetWindowPos(h, h, ...) would drop the pin entirely.
        Assert.Equal(IntPtr.Zero, OverlayService.ResolveVideoAnchor(
            new List<(IntPtr, IntPtr)>(), targetHwnd: Video1, targetMonitor: Mon1, fallbackHwnd: Video1));
    }

    [Fact]
    public void UnknownMonitorHandle_FallsBackRatherThanReturningZero()
    {
        // MonitorFromWindow can hand back NULL during a display-topology change; the sweep must
        // still pin the overlay below the video instead of skipping the #497 rule.
        Assert.Equal(Video1, OverlayService.ResolveVideoAnchor(
            TwoScreenVideo(), targetHwnd: Overlay2, targetMonitor: IntPtr.Zero, fallbackHwnd: Video1));
    }

    // ---- browser first-frame deadline arming (round 4 blocker) ----

    [Fact]
    public void AMirrorWhoseInitHasNotStartedIsNeverCondemned()
    {
        // BrowserVideoEngine.InitWindowsAsync brings the WebView2 windows up STRICTLY SERIALLY. A
        // mirror sitting in that queue has not been asked to render anything yet, so no amount of
        // wall clock may condemn it. DateTime.MaxValue is the "unarmed" state.
        Assert.False(FrameDeadlinePassed(DateTime.MaxValue, new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc)));
        Assert.False(FrameDeadlinePassed(DateTime.MaxValue, DateTime.MaxValue));
    }

    [Fact]
    public void AQueuedMirrorSurvivesTheSweepNoMatterHowLongThePrimaryTakes()
    {
        // The regression this pins: a 4-monitor rig on a cold disk used to reach the 20s pre-ready
        // budget on mirror 4 while mirror 4's WebView2 had not started, and the sweep closed a
        // perfectly healthy window. Unarmed must resolve to Wait, never DropMirror.
        var anHourLater = new DateTime(2026, 8, 27, 13, 0, 0, DateTimeKind.Utc);
        Assert.Equal(BrowserFrameSweepAction.Wait, DecideBrowserFrameSweep(
            firstFrameSeen: false,
            deadlinePassed: FrameDeadlinePassed(DateTime.MaxValue, anHourLater),
            isPrimarySurface: false));
    }

    [Fact]
    public void AnArmedSurfacePastItsBudgetIsCondemned()
    {
        var now = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(FrameDeadlinePassed(now.AddSeconds(-1), now));
        Assert.True(FrameDeadlinePassed(now, now)); // the deadline itself counts as passed
    }

    [Fact]
    public void AnArmedSurfaceInsideItsBudgetIsLeftAlone()
    {
        var now = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(FrameDeadlinePassed(now.AddSeconds(1), now));
    }

    [Fact]
    public void OnlyThePrimaryArmsItsFrameClockAtSessionStart()
    {
        // The primary is initialised first so it never queues, and its session-start clock is the
        // only deadline left that can still fire if the shared WebView2 environment task hangs
        // forever - without it a hung environment would sit black until the 10 minute safety timer.
        Assert.True(ArmsFrameDeadlineAtSessionStart(isPrimarySurface: true));
        Assert.False(ArmsFrameDeadlineAtSessionStart(isPrimarySurface: false));
    }

    [Fact]
    public void AGracePauseSlidesAnArmedDeadlineByOneTick()
    {
        var deadline = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(deadline.AddMilliseconds(500), SlidePendingDeadline(deadline, 500));
    }

    [Fact]
    public void AGracePauseLeavesAQueuedMirrorUnarmed()
    {
        // Sliding MaxValue would overflow; it must stay the unarmed sentinel instead, so a session
        // paused while the mirrors are still queuing does not silently arm them.
        Assert.Equal(DateTime.MaxValue, SlidePendingDeadline(DateTime.MaxValue, 500));
    }

    // ---- uncovering a dead surface (round 4 strict-mode guard) ----

    [Fact]
    public void ADeadMirrorUnderAStrictLockStaysCovered()
    {
        // Lock Card / Lockdown / Possession are commitment devices: the user asked to be unable to
        // look away. Hiding a dead mirror would hand back a monitor mid-clip, so it stays on screen
        // as an opaque black cover instead. Close() is vetoed by the strict Closing handler, but
        // Hide() is not, so this rule is the only thing standing between a wedge and an escape hatch.
        Assert.False(ShouldUncoverDeadSurface(hostStrict: true));
    }

    [Fact]
    public void ADeadMirrorOutsideStrictIsUncovered()
    {
        // No commitment in play: leaving a black rectangle over a second screen for the rest of the
        // clip is just broken, so the window goes away and that monitor comes back.
        Assert.True(ShouldUncoverDeadSurface(hostStrict: false));
    }
}
