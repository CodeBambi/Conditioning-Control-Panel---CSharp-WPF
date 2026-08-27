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
    /// The rules below are what replaced that: per-surface verdicts. A dead MIRROR no longer touches
    /// the clip - it ends only when every surface is dead, or when the audio-bearing one is (a black
    /// and silent primary is not a playable clip, and it is the only surface wired to the end/error
    /// events that could otherwise stop the run).
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
            /// <summary>Last strike: this surface is dead. The caller then asks
            /// <see cref="ShouldAbortClip"/> whether the clip can go on without it. The audio-bearing
            /// surface reaches this on its FIRST missed window - see the retryAllowed parameter.</summary>
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
        /// <param name="retryAllowed">
        /// Whether this surface gets a SECOND grace window before it is condemned. False for the
        /// audio-bearing surface, deliberately: a dead primary ends the clip either way (see
        /// <see cref="ShouldAbortClip"/>), so granting it a retry would only double the time the user
        /// spends staring at a black, silent main screen - 16s where the released build takes 8s, and
        /// on the single-monitor majority rig the primary is the ONLY surface, so the retry rung would
        /// be pure added latency there. Mirrors do get the retry: the clip keeps playing while they
        /// take it, so it costs the user nothing.
        /// </param>
        internal static FrameWatchdogAction DecideFrameWatchdog(bool tornDown, bool gracePaused, bool hasRendered, bool retryUsed, bool retryAllowed)
        {
            if (tornDown) return FrameWatchdogAction.Ignore;
            if (gracePaused) return FrameWatchdogAction.Defer;
            if (hasRendered) return FrameWatchdogAction.Ignore;
            return (retryUsed || !retryAllowed) ? FrameWatchdogAction.GiveUp : FrameWatchdogAction.Retry;
        }

        /// <summary>
        /// Whether a dead surface should take the whole clip with it. This is the #1015/#1035 fix in
        /// one line: it used to be "any dead surface ends the clip", which is how a stalled decoder on
        /// monitor 2 blacked out a video that monitor 1 was playing perfectly.
        /// <paramref name="deadSurfaces"/> INCLUDES the surface that just gave up.
        ///
        /// <paramref name="primarySurfaceDead"/> is not a nicety, it is the whole safety net. Only the
        /// AUDIO-BEARING surface is wired to EndReached / EncounteredError / LengthChanged, and the
        /// blurred path (the default) never arms the vout watchdog either - so a primary that decoded
        /// nothing raises NOTHING. If a live mirror were allowed to carry that clip, the only remaining
        /// backstop would be the 10-minute fallback safety timer, i.e. the reported "primary black and
        /// silent" would last minutes instead of the 8 seconds the released build takes. A dead primary
        /// therefore always ends the clip; a dead MIRROR only does when every surface is gone, which is
        /// the actual #1015/#1035 win.
        /// </summary>
        internal static bool ShouldAbortClip(int totalSurfaces, int deadSurfaces, bool primarySurfaceDead)
            => primarySurfaceDead || (totalSurfaces > 0 && deadSurfaces >= totalSurfaces);

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
        /// A leased player (bubble count, mini player, previews) whose Stop() wedged used to retire the
        /// shared LibVLC instance immediately - the same instance the mandatory video's per-monitor
        /// players were mid-decode on. That drops the metadata cache under a live clip and makes the
        /// next EnsureLibVLCInitialized block the UI thread on the rebuild's lock, mid-video. Parking
        /// it until teardown keeps a foreign wedge from reaching under the screens that are playing.
        ///
        /// SCOPE, stated precisely so nobody reads more into it: this does NOT suppress the 60s native
        /// poison cooldown (QuarantineNative arms that before any retire decision is reached), it does
        /// not save a retire from the per-session budget (the parked retire still runs at teardown),
        /// and it does not touch the two retire sites inside the mandatory pipeline's own CloseAll,
        /// which are that clip's self-heal and must stay immediate. The "no bubble-pop audio for the
        /// rest of the session" tail on these reports is separate, still-open work.
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
