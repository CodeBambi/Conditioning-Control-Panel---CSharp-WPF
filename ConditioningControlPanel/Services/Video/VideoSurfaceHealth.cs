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
            /// <see cref="ShouldAbortClip"/> whether the clip can go on without it. A surface with no
            /// retry rung reaches this on its FIRST missed window - see the retryAllowed parameter.</summary>
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
        /// Whether this surface gets a SECOND grace window before it is condemned. The caller decides
        /// this from the RIG, not from the role:
        ///
        ///   * a MIRROR always gets it - the clip keeps playing while it takes its retry, so a second
        ///     window costs the user nothing;
        ///   * the AUDIO-BEARING surface gets it too as soon as MORE THAN ONE surface is armed. That
        ///     arm is the headline multi-monitor report (#533 #1015 #1024 #1035 #1039): a primary that
        ///     missed its window on a dual-monitor rig used to skip the clip outright with no recovery
        ///     rung of any kind, so one re-Play() is the only thing standing between "the decoder
        ///     hiccuped while three screens spun up" and "the video was skipped". A dead primary still
        ///     ends the clip (see <see cref="ShouldAbortClip"/>) - it just ends it one grace window
        ///     later, after the retry demonstrably failed;
        ///   * it is withheld on a SINGLE-surface rig, which is the majority. There the primary is the
        ///     only surface, nothing else can be spinning up alongside it, and the retry would be pure
        ///     added latency - 16s of black where the released build takes 8s.
        /// </param>
        internal static FrameWatchdogAction DecideFrameWatchdog(bool tornDown, bool gracePaused, bool hasRendered, bool retryUsed, bool retryAllowed)
        {
            if (tornDown) return FrameWatchdogAction.Ignore;
            if (gracePaused) return FrameWatchdogAction.Defer;
            if (hasRendered) return FrameWatchdogAction.Ignore;
            return (retryUsed || !retryAllowed) ? FrameWatchdogAction.GiveUp : FrameWatchdogAction.Retry;
        }

        /// <summary>
        /// Whether this surface may spend a retry rung, decided by the RIG rather than by the role -
        /// the <c>retryAllowed</c> argument of <see cref="DecideFrameWatchdog"/> in one line.
        ///
        /// A mirror always may: the clip keeps playing while it retries, so a second grace window
        /// costs the user nothing. The audio-bearing surface may too, as soon as it has siblings -
        /// that is the multi-monitor headline report, where a primary that missed its window used to
        /// skip the clip on the spot with no recovery rung at all. A LONE primary may not: it is the
        /// only surface on the rig, a dead primary ends the clip either way, and the rung would be
        /// nothing but 8 extra seconds of black for the single-monitor majority.
        /// </summary>
        internal static bool AllowsFrameRetry(bool primarySurface, int armedSurfaces)
            => !primarySurface || armedSurfaces > 1;

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
        ///
        /// <para>The one exception is the STRICT lock, and it is a content-lock integrity rule rather
        /// than a playback nicety. Ending a clip mid-run releases the lock, so under strict a page
        /// failure that does NOT blame the file is a way OUT of a video the user is not allowed to
        /// stop - which is exactly how two taps on a Bluetooth headset's play/pause ended a Strict
        /// Lock video in v6.9.0: the page's pause rule reported 102, nothing blamed the file, and
        /// EndClip let the user go. A non-file failure under the lock hands the SAME clip to LibVLC
        /// instead, so the cover stays on screen and the session runs on. A failure that DOES blame
        /// the file still ends the run: replaying a file that cannot decode would only trap the user
        /// against a clip that can never finish.</para>
        /// </summary>
        internal static BrowserFailureAction DecideBrowserFailure(bool isPrimarySurface, bool alreadyFellBack, bool playbackStartedFired,
            bool strictLock = false, bool blamesFile = false)
        {
            if (!isPrimarySurface) return BrowserFailureAction.DropSecondary;
            if (alreadyFellBack) return BrowserFailureAction.Ignore;
            if (!playbackStartedFired) return BrowserFailureAction.FallbackWholeClip;
            if (strictLock && !blamesFile) return BrowserFailureAction.FallbackWholeClip;
            return BrowserFailureAction.EndClip;
        }

        /// <summary>What one tick of the browser engine's per-surface first-frame sweep should do
        /// with ONE window.</summary>
        internal enum BrowserFrameSweepAction
        {
            /// <summary>This surface already presented a frame - there is nothing left to watch.</summary>
            Ignore,
            /// <summary>Its own deadline has not passed yet.</summary>
            Wait,
            /// <summary>The audio-bearing surface never rendered: fail the session so the host's
            /// LibVLC fallback replays the clip.</summary>
            FailSession,
            /// <summary>A mirror never rendered: report it and drop THAT window. The clip is
            /// untouched and keeps playing wherever it is rendering.</summary>
            DropMirror,
        }

        /// <summary>
        /// One window's verdict in the browser engine's first-frame sweep. The browser engine is the
        /// DEFAULT for mp4/webm and it drives EVERY screen, so this is the multi-monitor liveness net
        /// for most rigs, not a corner case.
        ///
        /// Before this rule existed the engine watched the PRIMARY alone, and it stopped watching the
        /// instant the primary posted `playing` - so a mirror whose WebView2 came up, completed the
        /// handshake and then never decoded a frame (a GPU stall, a swallowed fetch, a decode failure
        /// the page never turned into an `error`) was never noticed, never dropped and never even
        /// logged. It stayed an opaque black fullscreen window for the whole clip, and video-diag.log
        /// carried no line at all for that monitor - indistinguishable, in a bug report, from a window
        /// that was never created or a truncated log. Every screen now carries its own deadline and
        /// gets its own verdict.
        /// </summary>
        internal static BrowserFrameSweepAction DecideBrowserFrameSweep(bool firstFrameSeen, bool deadlinePassed, bool isPrimarySurface)
        {
            if (firstFrameSeen) return BrowserFrameSweepAction.Ignore;
            if (!deadlinePassed) return BrowserFrameSweepAction.Wait;
            return isPrimarySurface ? BrowserFrameSweepAction.FailSession : BrowserFrameSweepAction.DropMirror;
        }

        /// <summary>
        /// Whether a browser surface's first-frame deadline has actually expired.
        ///
        /// <see cref="DateTime.MaxValue"/> means UNARMED, and that is load-bearing rather than a
        /// convenience sentinel: the engine initialises its windows STRICTLY SERIALLY
        /// (BrowserVideoEngine.InitWindowsAsync), so on a cold start a mirror's WebView2 has not begun
        /// coming up at all while the primary's is still building. Arming every window's budget at
        /// SESSION start therefore made the trailing mirrors burn their whole pre-ready budget while
        /// QUEUED, and on a 2-4 monitor rig with a slow disk a window could reach its deadline before
        /// its own init even started - condemning and closing a perfectly healthy secondary. A mirror
        /// is now armed when ITS init starts; until then it is unarmed and can never be judged. (The
        /// primary still arms at session start - see <see cref="ArmsFrameDeadlineAtSessionStart"/>.)
        /// </summary>
        internal static bool FrameDeadlinePassed(DateTime deadlineUtc, DateTime nowUtc)
            => deadlineUtc != DateTime.MaxValue && nowUtc >= deadlineUtc;

        /// <summary>
        /// Which surfaces get their first-frame budget armed at SESSION start rather than when their
        /// own init begins. Only the audio-bearing one, and it is a safety net rather than a nicety:
        /// the primary is initialised FIRST, so it never queues behind anybody, and its session-start
        /// deadline is the ONLY thing that ends a session whose WebView2 environment task never
        /// completes at all - the serial init loop cannot arm anything in that case because it never
        /// runs. Every MIRROR is armed when its own init starts instead, so the time it spent queued
        /// behind the primary can never condemn it.
        /// </summary>
        internal static bool ArmsFrameDeadlineAtSessionStart(bool isPrimarySurface) => isPrimarySurface;

        /// <summary>
        /// Push a pending first-frame deadline out by one watch tick (the grace-pause slide). An
        /// UNARMED deadline stays unarmed: sliding <see cref="DateTime.MaxValue"/> would throw
        /// ArgumentOutOfRangeException and abandon the rest of that tick's sweep.
        /// </summary>
        internal static DateTime SlidePendingDeadline(DateTime deadlineUtc, int tickMs)
            => deadlineUtc == DateTime.MaxValue ? deadlineUtc : deadlineUtc.AddMilliseconds(tickMs);

        /// <summary>
        /// Whether a dead surface's screen may be UNCOVERED (its window hidden), or has to stay covered
        /// by that opaque black window for the rest of the clip.
        ///
        /// A mandatory video under a STRICT lock (Lock Card / Lockdown / Possession) covers every
        /// screen and refuses Alt+F4 on purpose - that IS the commitment device. Freeing a dead
        /// mirror's monitor there would hand the user a fully interactive desktop mid-clip, so under
        /// strict a stalled mirror stays black: a dead screen is a far better outcome than an escape
        /// hatch, and it is what the released build already did (a vetoed Close left the window up).
        /// Outside strict mode nothing is being committed to, and parking a dead fullscreen topmost
        /// window over someone's second monitor for the whole clip is pure harm - so it goes.
        ///
        /// Both engines ask this, so the WebView2 path and the LibVLC blur path leave the same visible
        /// state for the same failure instead of disagreeing about it.
        /// </summary>
        internal static bool ShouldUncoverDeadSurface(bool hostStrict) => !hostStrict;

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
