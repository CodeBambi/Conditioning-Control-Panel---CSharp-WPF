using System;
using System.Text;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The multi-monitor half of the mandatory-video pipeline, extracted as pure decisions so it can
    /// be unit-tested without a second monitor.
    ///
    /// Background (#533 #540 #542 #559 #592 #617 #918 #1015 #1016 #1024 #1025 #1035 #1039 #1059):
    /// every one of those reports is the same shape - "the video is black on ONE monitor and fine on
    /// the others, and there is no sound". The pipeline runs one surface per screen, and it used to
    /// treat the whole clip as a single pass/fail:
    ///
    ///   * a browser surface whose WebView2 never came up raised nothing at all, so the audio-bearing
    ///     PRIMARY could sit black for the full pre-ready budget while every secondary played;
    ///   * one dead LibVLC memory-render surface aborted the clip on ALL screens, even when N-1
    ///     surfaces were decoding happily.
    ///
    /// The rules below are what replaced that: per-surface verdicts, with the clip only ending when
    /// every surface is dead.
    /// </summary>
    internal static class VideoSurfaceHealth
    {
        /// <summary>What one tick of a per-surface frame-liveness watchdog should do.</summary>
        internal enum FrameWatchdogAction
        {
            /// <summary>Nothing to do: the surface rendered, or the clip is already torn down.</summary>
            Ignore,
            /// <summary>Playback is deliberately held (grace pause) - slide the deadline, judge nothing.</summary>
            Defer,
            /// <summary>First strike: give THIS surface another go before condemning anything.</summary>
            Retry,
            /// <summary>Second strike: this surface is dead. The caller then asks
            /// <see cref="ShouldAbortClip"/> whether any sibling is still alive.</summary>
            GiveUp,
        }

        /// <summary>What the host should do when the browser engine reports a failed surface.</summary>
        internal enum BrowserFailureAction
        {
            /// <summary>Already handled - one fallback per session reaches the host.</summary>
            Ignore,
            /// <summary>A mirror died. Log it, drop it, and let the audio-bearing primary keep playing.</summary>
            DropSecondary,
            /// <summary>The primary died BEFORE the first frame: replay the whole clip through LibVLC.</summary>
            FallbackWholeClip,
            /// <summary>The primary died AFTER playback started: end the run through the normal funnel
            /// rather than making the user rewatch the clip from zero.</summary>
            EndClip,
        }

        /// <summary>
        /// One tick of a surface's frame-liveness watchdog. Ordering is load-bearing: teardown beats
        /// everything (a late timer must never act on a video that already ended), a grace pause beats
        /// the liveness verdict (#735 - a paused vmem surface produces no frames BY DESIGN), and only
        /// then does "no frame" become a strike.
        /// </summary>
        internal static FrameWatchdogAction DecideFrameWatchdog(bool tornDown, bool gracePaused, bool hasRendered, bool retryUsed)
        {
            if (tornDown) return FrameWatchdogAction.Ignore;
            if (gracePaused) return FrameWatchdogAction.Defer;
            if (hasRendered) return FrameWatchdogAction.Ignore;
            return retryUsed ? FrameWatchdogAction.GiveUp : FrameWatchdogAction.Retry;
        }

        /// <summary>
        /// Whether a dead surface should take the whole clip with it. This is the #1015/#1035 fix in
        /// one line: it used to be "any dead surface ends the clip", which is how a stalled decoder on
        /// monitor 2 blacked out a video that monitor 1 was playing perfectly.
        /// <paramref name="deadSurfaces"/> INCLUDES the surface that just gave up.
        /// </summary>
        internal static bool ShouldAbortClip(int totalSurfaces, int deadSurfaces)
            => totalSurfaces > 0 && deadSurfaces >= totalSurfaces;

        /// <summary>
        /// Policy for a browser-engine surface failure. A secondary is a mirror and is never allowed
        /// to end the run; the primary carries the audio and the session, so its pre-first-frame
        /// failure replays through LibVLC and its mid-clip failure ends the run.
        /// </summary>
        internal static BrowserFailureAction DecideBrowserFailure(bool isPrimarySurface, bool alreadyFellBack, bool playbackStartedFired)
        {
            if (!isPrimarySurface) return BrowserFailureAction.DropSecondary;
            if (alreadyFellBack) return BrowserFailureAction.Ignore;
            return playbackStartedFired ? BrowserFailureAction.EndClip : BrowserFailureAction.FallbackWholeClip;
        }

        /// <summary>
        /// Whether a retire request that did NOT come from the currently playing clip must wait.
        /// A leased player (bubble count, mini player, previews) whose Stop() wedged used to retire
        /// the shared LibVLC instance immediately - the same instance the mandatory video's per-monitor
        /// players were mid-decode on. That spends one of the four per-session retires, arms the 60s
        /// poison cooldown and drops the metadata cache while the user is still watching, which is the
        /// "and then there was no bubble-pop sound for the rest of the session" tail on these reports.
        /// </summary>
        internal static bool ShouldDeferRetire(bool fromCurrentPlayback, bool playbackLive)
            => !fromCurrentPlayback && playbackLive;

        /// <summary>
        /// The one-line-per-surface diagnostic the bug reports need. Reporters upload video-diag.log,
        /// and until now nothing in it said WHICH engine a given monitor ended up on or how long its
        /// first frame took - so "black on the primary, fine on the others" could not be told apart
        /// from "the whole clip failed". Pure so the exact shape is locked by a test.
        /// </summary>
        /// <param name="firstFrameMs">Milliseconds from surface creation to the first presented frame,
        /// or a negative value when no frame was ever seen.</param>
        /// <param name="failureReason">Why this surface fell back / died, or null when it is healthy.</param>
        internal static string FormatSurfaceLine(string? engine, string? monitor, bool primary, long firstFrameMs, string? failureReason)
        {
            var sb = new StringBuilder();
            sb.Append("engine=").Append(string.IsNullOrWhiteSpace(engine) ? "?" : engine);
            sb.Append(" monitor=").Append(string.IsNullOrWhiteSpace(monitor) ? "?" : monitor);
            sb.Append(" role=").Append(primary ? "primary" : "secondary");
            sb.Append(" firstFrame=").Append(firstFrameMs < 0 ? "none" : firstFrameMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");
            if (!string.IsNullOrWhiteSpace(failureReason))
                sb.Append(" reason=").Append(failureReason!.Replace('\r', ' ').Replace('\n', ' '));
            return sb.ToString();
        }

        /// <summary>
        /// Emit one <see cref="FormatSurfaceLine"/> to BOTH sinks: Serilog (Information, so it lands in
        /// the log the support flow collects) and the video trace (which is the file reporters attach).
        /// Never throws - a diagnostic must not be able to break playback.
        /// </summary>
        internal static void Report(string engine, string? monitor, bool primary, long firstFrameMs, string? failureReason)
        {
            try
            {
                var line = FormatSurfaceLine(engine, monitor, primary, firstFrameMs, failureReason);
                App.Logger?.Information("VideoSurface: {Surface}", line);
                VideoDiag.Log("SURFACE", line);
            }
            catch (Exception ex)
            {
                try { App.Logger?.Debug("VideoSurfaceHealth.Report failed: {E}", ex.Message); } catch { }
            }
        }
    }
}
