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
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: true, retryUsed: false));
        Assert.Equal(FrameWatchdogAction.Ignore,
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: true, retryUsed: true));
    }

    [Fact]
    public void FrameWatchdog_TeardownBeatsEverything()
    {
        // A late timer tick must never act on a clip that already ended - not even to retry.
        Assert.Equal(FrameWatchdogAction.Ignore,
            DecideFrameWatchdog(tornDown: true, gracePaused: false, hasRendered: false, retryUsed: true));
        Assert.Equal(FrameWatchdogAction.Ignore,
            DecideFrameWatchdog(tornDown: true, gracePaused: true, hasRendered: false, retryUsed: false));
    }

    [Fact]
    public void FrameWatchdog_GracePauseDefers_ItDoesNotCondemn()
    {
        // #735: a deliberately paused vmem surface produces no frames BY DESIGN.
        Assert.Equal(FrameWatchdogAction.Defer,
            DecideFrameWatchdog(tornDown: false, gracePaused: true, hasRendered: false, retryUsed: false));
        Assert.Equal(FrameWatchdogAction.Defer,
            DecideFrameWatchdog(tornDown: false, gracePaused: true, hasRendered: false, retryUsed: true));
    }

    [Fact]
    public void FrameWatchdog_FirstStrikeRetries_SecondStrikeGivesUp()
    {
        Assert.Equal(FrameWatchdogAction.Retry,
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: false, retryUsed: false));
        Assert.Equal(FrameWatchdogAction.GiveUp,
            DecideFrameWatchdog(tornDown: false, gracePaused: false, hasRendered: false, retryUsed: true));
    }

    // ---- liveness aggregation (deliverable 2) ----

    [Fact]
    public void OneDeadSurfaceOfTwo_DoesNotAbortTheClip()
    {
        // THE regression this whole branch exists for: the old code ended the clip on every screen
        // the moment one surface missed its frame budget.
        Assert.False(ShouldAbortClip(totalSurfaces: 2, deadSurfaces: 1));
        Assert.False(ShouldAbortClip(totalSurfaces: 3, deadSurfaces: 2));
    }

    [Fact]
    public void EverySurfaceDead_AbortsTheClip()
    {
        Assert.True(ShouldAbortClip(totalSurfaces: 1, deadSurfaces: 1));
        Assert.True(ShouldAbortClip(totalSurfaces: 2, deadSurfaces: 2));
        // Defensive: a double-report must not read as "still alive".
        Assert.True(ShouldAbortClip(totalSurfaces: 2, deadSurfaces: 3));
    }

    [Fact]
    public void NoSurfacesAtAll_IsNotAnAbort()
    {
        // Nothing was ever built, so there is nothing for this watchdog to end; the pre-roll
        // watchdogs own that case.
        Assert.False(ShouldAbortClip(totalSurfaces: 0, deadSurfaces: 0));
        Assert.False(ShouldAbortClip(totalSurfaces: 0, deadSurfaces: 5));
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
        // The vout heal and the wedge ladder ARE the current playback; deferring them would disarm
        // the self-heal entirely.
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
}
