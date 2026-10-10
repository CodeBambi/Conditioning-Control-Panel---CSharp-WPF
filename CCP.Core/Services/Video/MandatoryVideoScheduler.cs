using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>What the mandatory-video scheduler needs from a head: put a clip on the targeted
    /// screens, and take it down again. Called on the head's UI thread (ticks hop through
    /// <see cref="CoreDispatch"/>).</summary>
    public interface IMandatoryVideoHost
    {
        /// <summary>Open the full-screen video window(s) and play <paramref name="path"/>. The host calls
        /// <see cref="MandatoryVideoScheduler.End"/> at the clip's end or on Esc, and
        /// <see cref="MandatoryVideoScheduler.ForceCleanup"/> on the in-window panic key.</summary>
        void Show(string path, bool strict);
        /// <summary>Close every video window; returns the seconds of the clip that were watched.</summary>
        double CloseAll();
        /// <summary>WPF ShowMessage: the attention-check verdict over every screen for
        /// <paramref name="ms"/>, then <paramref name="then"/> on the UI thread.</summary>
        void ShowMessage(AttentionVerdict verdict, int ms, Action then);
    }

    /// <summary>What an ended clip's attention checks come to (WPF EndCurrentVideo :5955).</summary>
    public enum AttentionVerdict { None, Pass, Troll, Fail, Mercy }

    /// <summary>What a key press does in a mandatory-video window (WPF SetupStrictHandlers).</summary>
    public enum VideoKeyAction { None, Dismiss, ForceStop, Swallow }

    /// <summary>WPF VideoService.GraceDecision (#735): pause now, swallow the same keystroke's second
    /// handler, or fall through to the normal Esc/panic path.</summary>
    public enum GraceDecision { Pause, ConsumedDedup, FallThrough }

    /// <summary>
    /// The portable half of WPF <c>VideoService</c> (ConditioningControlPanel/Services/Video/VideoService.cs):
    /// the schedule (<c>ScheduleNext</c> :2952), the local pick (<c>GetNextVideo</c> :7997 /
    /// <c>RefillVideoQueues</c> :8337), the 1.3 s pre-roll (<c>PlayVideo</c> :3146), the two teardown
    /// doors (<c>Cleanup</c> :7934 re-arms, <c>ForceCleanup</c> :2670 does not), watch credit
    /// (<c>FinalizeWatchCredit</c> :7424) and the strict-key rules. Drawing is the head's
    /// <see cref="IMandatoryVideoHost"/>.
    /// ponytail: local library only - content-pack and remote clips, the duration filter
    /// (MetadataCache; the max-length cap in <see cref="Guard"/> still holds the max), cascade/feed/DND/browser-media defers and the
    /// interaction queue are WPF-head services; add each here when it reaches Core.
    /// <para><b>Deliberate deviation:</b> a scheduled tick that finds an empty library re-arms the
    /// schedule. WPF returns from ContinueTriggerVideo (:2424) without ScheduleNext, so its schedule
    /// silently dies until the engine restarts; here a video added mid-session still plays
    /// (docs/avalonia-decisions.md).</para>
    /// </summary>
    public sealed class MandatoryVideoScheduler
    {
        /// <summary>WPF <c>SkipRetrySeconds</c>: a skipped tick retries this soon.</summary>
        public const double SkipRetrySeconds = 30;
        public static readonly TimeSpan PreRoll = TimeSpan.FromSeconds(1.3);

        public static readonly string[] SupportedVideoExtensions =
            { ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".m4v", ".mpg", ".mpeg", ".flv", ".ts" };

        private readonly IMandatoryVideoHost _host;
        private readonly TimeProvider _time;
        private readonly Func<IReadOnlyList<string>> _library;
        private readonly Random _random = new();
        private Queue<string> _queue = new();
        private ITimer? _scheduler, _preroll;
        private volatile bool _running, _playing;
        private int _retryGeneration, _noVideosShown;

        /// <summary>The library came back empty for the first time this launch (WPF's "no videos"
        /// guidance dialog). Raised on the UI thread.</summary>
        public event Action? NoVideos;

        /// <summary>A clip is on screen (WPF VideoService.VideoStarted, VideoService.cs:3370).</summary>
        public event Action? VideoStarted;

        /// <summary>The clip <see cref="VideoStarted"/> announced (WPF VideoService.LastVideoPath).</summary>
        public string? LastVideoPath { get; private set; }

        /// <summary>Replays forced this run (WPF _penalties); reset by <see cref="End"/>.</summary>
        public int Penalties { get; private set; }
        /// <summary>This clip's attention spawns and catches (WPF _spawned / _hits).</summary>
        public int AttentionSpawned { get; private set; }
        public int AttentionHits { get; private set; }

        /// <summary>A target batch went up (WPF SpawnTarget's _spawned++).</summary>
        public void NoteSpawn() => AttentionSpawned++;

        /// <summary>One target of a batch was caught: +15 XP (WPF SpawnTarget onHit).</summary>
        public void NoteHit()
        {
            AttentionHits++;
            _ = CoreHaptics.Service?.VideoTargetHitAsync();   // WPF VideoService.cs:5729
            CoreProgression.AddXP(AttentionHitXp, "Video");
        }

        public MandatoryVideoScheduler(IMandatoryVideoHost host, TimeProvider? time = null, Func<IReadOnlyList<string>>? library = null)
        {
            _host = host;
            _time = time ?? TimeProvider.System;
            _library = library ?? LocalLibrary;
        }

        public bool IsRunning => _running;
        /// <summary>A video is in pre-roll or on screen (WPF <c>_videoPlaying</c>).</summary>
        public bool IsPlaying => _playing;
        public bool IsStrict { get; private set; }

        /// <summary>WPF ScheduleNext: 3600/perHour jittered 0.8-1.2x, never under 60 s.</summary>
        public static double NextIntervalSeconds(int perHour, double roll) =>
            Math.Max(60, 3600.0 / Math.Max(1, perHour) * (0.8 + roll * 0.4));

        /// <summary>WPF ShouldFillSecondaryMonitors (:2764) over the TARGETED screen count.</summary>
        public static bool ShouldFillSecondaryMonitors(int screenCount, bool fillAll) =>
            screenCount > 1 && (screenCount <= 2 || fillAll);

        /// <summary>WPF GetEffectiveVolume (:1630): master x video, 0-100.</summary>
        public static int EffectiveVolume(int master, int video) => (int)(master / 100.0 * (video / 100.0) * 100);

        /// <summary>WPF: +15 XP per caught target (SpawnTarget onHit).</summary>
        public const int AttentionHitXp = 15;

        /// <summary>WPF SetupAttention: AttentionDensity targets, or 1..density when randomized.</summary>
        public static int AttentionTargetCount(int density, bool randomize, Random r)
        {
            var max = Math.Max(1, density);
            return randomize ? r.Next(1, max + 1) : max;
        }

        /// <summary>WPF SetupAttention: spawn seconds in [3, 3 + max(1, dur-8)], sorted, at least 3 s apart
        /// (a 0-length clip counts as 60 s).</summary>
        public static List<double> AttentionSpawnTimes(int total, double durationSeconds, Random r)
        {
            var window = Math.Max(1, (durationSeconds > 0 ? durationSeconds : 60) - 8);
            var t = Enumerable.Range(0, total).Select(_ => 3 + r.NextDouble() * window).OrderBy(x => x).ToList();
            for (var i = 1; i < t.Count; i++) if (t[i] - t[i - 1] < 3) t[i] = t[i - 1] + 3;
            return t;
        }

        /// <summary>WPF EndCurrentVideo: every spawn caught passes (1 in 10 passes is trolled into a
        /// replay); any miss fails; with no spawn there is no verdict.</summary>
        public static AttentionVerdict Evaluate(bool enabled, int spawned, int hits, double roll) =>
            !enabled || spawned <= 0 ? AttentionVerdict.None
            : hits < spawned ? AttentionVerdict.Fail
            : roll < 0.1 ? AttentionVerdict.Troll : AttentionVerdict.Pass;

        /// <summary>WPF: a pass pays (replays + 1) x 50 + 200 XP.</summary>
        public static int AttentionPassXp(int penalties) => (penalties + 1) * 50 + 200;

        /// <summary>WPF NeedsBlurFill: bars (and so the blurred fill) only when the clip's aspect is
        /// more than 3% off the screen's.</summary>
        public static bool NeedsBlurFill(double videoAspect, double screenAspect) =>
            videoAspect > 0 && screenAspect > 0 && Math.Abs(videoAspect / screenAspect - 1.0) > 0.03;

        /// <summary>WPF AttentionTargetVisual.FormatTriggerText: 2 words stack, 4+ words split in two lines.</summary>
        public static string FormatTriggerText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var w = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (w.Length == 2) return $"{w[0]}\n{w[1]}";
            if (w.Length >= 4) return string.Join(" ", w.Take(w.Length / 2)) + "\n" + string.Join(" ", w.Skip(w.Length / 2));
            return text;
        }

        public static bool IsSupportedVideoExtension(string path)
        {
            var ext = string.IsNullOrEmpty(path) ? "" : Path.GetExtension(path);
            return ext.Length > 0 && SupportedVideoExtensions.Contains(ext.ToLowerInvariant());
        }

        /// <summary>WPF SetupStrictHandlers minus the grace pause. Strict: the panic key, Alt+F4 and the
        /// System key do nothing - but only while a global panic listener is live to stop the video
        /// (<paramref name="panicListenerLive"/>: panic enabled AND its listener bound). Without one, the panic key and Esc force-stop it:
        /// strict must never trap the user behind a topmost full-screen window (LockCardWindow #875).
        /// Otherwise Esc dismisses (the run goes on) and the panic key force-stops.</summary>
        public static VideoKeyAction KeyAction(bool strict, string key, bool alt, bool panicEnabled, string? panicKey, bool panicListenerLive = true)
        {
            if (strict && !panicListenerLive && (key == "Escape" || key == panicKey))
                return VideoKeyAction.ForceStop;
            if (strict)
                return key == panicKey || key == "System" || (key == "F4" && alt) ? VideoKeyAction.Swallow : VideoKeyAction.None;
            if (key == "Escape") return VideoKeyAction.Dismiss;
            return panicEnabled && key == panicKey ? VideoKeyAction.ForceStop : VideoKeyAction.None;
        }

        // ---- grace pause (WPF #735) and the clip guards ----

        /// <summary>WPF GraceWindowSeconds: the pause resumes on its own after this.</summary>
        public const int GraceWindowSeconds = 60;
        /// <summary>WPF GraceDedupMs: one physical keystroke reaching two handlers pauses once.</summary>
        public const int GraceDedupMs = 200;

        /// <summary>WPF EvaluateGraceRequest: one grace pause per clip, only while it is really on
        /// screen; the press after it (or any press once it is spent) is the normal Esc/panic.</summary>
        public static GraceDecision EvaluateGrace(bool videoPlaying, bool cleaningUp, bool alreadyPaused, bool consumed,
            double msSinceLastGraceAction, int dedupMs = GraceDedupMs)
        {
            if (msSinceLastGraceAction < dedupMs) return GraceDecision.ConsumedDedup;
            if (!videoPlaying || cleaningUp || alreadyPaused || consumed) return GraceDecision.FallThrough;
            return GraceDecision.Pause;
        }

        /// <summary>WPF GraceSecondsRemaining: the countdown rounds up, so "60" shows for a full second.</summary>
        public static int GraceSecondsRemaining(double elapsedSeconds, int windowSeconds = GraceWindowSeconds)
            => (int)Math.Max(0, Math.Ceiling(windowSeconds - elapsedSeconds));

        /// <summary>WPF SetupStrictHandlers: whether this press first tries the grace pause, and if so
        /// whether it counts as the panic key (null = not a grace key). Strict: only Esc, only with a
        /// panic key that is not Esc; never when the strict fall-open force-stop owns the key.
        /// Otherwise Esc (as the panic key when it is bound to Esc) and the panic key.</summary>
        public static bool? GraceKey(bool strict, string key, bool panicEnabled, string? panicKey, bool panicListenerLive = true)
        {
            var escIsPanic = Safety.PanicPolicy.EscapeIsThePanicKey(panicEnabled, panicKey);
            if (strict)
                return panicListenerLive && key == "Escape" && panicEnabled && !escIsPanic ? false : null;
            if (key == "Escape") return escIsPanic;
            return panicEnabled && key == panicKey ? true : null;
        }

        /// <summary>WPF VoutGraceMs: no frame this long after Play is a dead output.</summary>
        public const double NoFrameSeconds = 8;
        /// <summary>WPF VoutLostGraceMs: output that was there and stays gone this long is lost.</summary>
        public const double OutputLostSeconds = 5;
        /// <summary>WPF MaxVideoFallbackSeconds: the cap when the clip never reports a length.</summary>
        public const double FallbackSeconds = 600;

        /// <summary>WPF's clip guards, on unpaused clocks (a grace pause freezes them, as WPF re-phases
        /// its timers). <paramref name="sinceShow"/> counts from the window opening,
        /// <paramref name="played"/> from the first frame (from the opening for a clip with no video
        /// track, which WPF lets play out). "no frame": no output 8 s after Play (vout watchdog);
        /// "output lost": frames stopped for 5 s mid-clip (mid-play vout loss) - both heal, see
        /// <see cref="GuardHeals"/>. "max length" (VideoMaxDurationSeconds cap), "overran" (length + 5 s
        /// safety timer, or 600 s with no length) end the clip. Null = fine.</summary>
        public static string? Guard(double sinceShow, double played, bool framed, bool hasVideo, double lengthSeconds,
            double sinceFrame, int maxSeconds)
        {
            if (!framed && hasVideo) return sinceShow >= NoFrameSeconds ? "no frame" : null;
            if (maxSeconds > 0 && played >= maxSeconds) return "max length";
            if (lengthSeconds > 0 ? played >= lengthSeconds + 5 : played >= FallbackSeconds) return "overran";
            return framed && sinceFrame >= OutputLostSeconds ? "output lost" : null;
        }

        /// <summary>WPF VoutWatchdogFire / VoutMidPlayTick: a dead or lost output replays the same clip
        /// once; after that (or for any other guard) the clip ends like a dismiss.</summary>
        public static bool GuardHeals(string why, bool healUsed) => !healUsed && why is "no frame" or "output lost";

        /// <summary>WPF PlayVideo(isVoutRetry): the replayed clip starts its attention checks afresh.</summary>
        public void NoteReplay() => AttentionSpawned = AttentionHits = 0;

        public void Start()
        {
            if (_running) return;
            _running = true;
            ScheduleNext();
            Log.Information("VideoService started");
        }

        /// <summary>WPF Stop: every caller is a live stop (engine stop, panic, the card toggle).</summary>
        public void Stop()
        {
            _running = false;
            _retryGeneration++;   // WPF CancelPendingRetry: a verdict message must not replay after a stop
            Dispose(ref _scheduler);
            ForceCleanup();
            Log.Debug("VideoService stopped");
        }

        private void ScheduleNext(double? retrySeconds = null)
        {
            var s = CoreSettings.Current;
            if (!_running || !s.MandatoryVideosEnabled) return;
            var secs = retrySeconds ?? NextIntervalSeconds(s.VideosPerHour, _random.NextDouble());
            Dispose(ref _scheduler);
            ITimer? created = null;
            created = _time.CreateTimer(_ => { var mine = created; CoreDispatch.Post(() => Tick(mine)); },
                null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _scheduler = created;
            created.Change(TimeSpan.FromSeconds(secs), Timeout.InfiniteTimeSpan);
        }

        private void Tick(ITimer? firedBy)
        {
            if (!_running || !ReferenceEquals(firedBy, _scheduler)) return;
            Dispose(ref _scheduler);
            try
            {
                // WPF VideoService :3007: a do-not-disturb app in front reschedules, never drops.
                if (!_playing && Services.UI.DndGuard.ShouldSuppressVideos())
                {
                    Services.UI.DndGuard.LogSuppressionThrottled("scheduled video");
                    ScheduleNext(SkipRetrySeconds);
                }
                else if (!_playing && !Trigger()) ScheduleNext();
            }
            catch (Exception ex)
            {
                // WPF #388: a throwing trigger must not end the session's videos.
                Log.Error(ex, "VideoService: scheduler tick failed - re-arming scheduler");
                _playing = false;
                ScheduleNext();
            }
        }

        /// <summary>WPF TriggerVideo -> PlayVideo: pick a clip, stop the flash, and show it after the
        /// pre-roll. False when nothing was started (no clip, or one is already playing).
        /// <paramref name="path"/> plays that clip instead of a pick (WPF PlaySpecificVideo).</summary>
        public bool Trigger(bool? strictOverride = null, string? path = null)
        {
            if (_playing) return false;
            path ??= PickNext();
            if (path == null)
            {
                Log.Warning("VideoService: no videos found in the library");
                // WPF #1124: the guidance dialog once per launch, never per tick.
                if (Interlocked.Exchange(ref _noVideosShown, 1) == 0) NoVideos?.Invoke();
                return false;
            }
            IsStrict = strictOverride ?? CoreSettings.Current.StrictLockEnabled;
            _playing = true;
            _retryGeneration++;   // WPF PlayVideo CancelPendingRetry: a new clip retires any pending verdict replay
            AttentionSpawned = AttentionHits = 0;
            CoreFlash.Stop();   // WPF PlayVideo: App.Flash?.Stop()
            var strict = IsStrict;
            Dispose(ref _preroll);
            ITimer? created = null;
            created = _time.CreateTimer(_ => { var mine = created; CoreDispatch.Post(() =>
            {
                // WPF ShouldStartAfterPreroll: only the run that armed it, and only while still live.
                if (!ReferenceEquals(mine, _preroll) || !_playing) return;
                Dispose(ref _preroll);
                CoreBubbles.Pause();   // WPF StartVideoPlayback: App.Bubbles?.PauseAndClear(), until the run ends
                try { _host.Show(path, strict); }
                catch (Exception ex) { Log.Error(ex, "VideoService: show failed"); End(); return; }
                LastVideoPath = path;
                try { VideoStarted?.Invoke(); }
                catch (Exception ex) { Log.Debug("VideoStarted handler failed: {Error}", ex.Message); }
                // WPF VideoService.cs:3372: background vibe and the clip's funscript, once on screen.
                _ = CoreHaptics.Service?.StartVideoBackgroundVibeAsync();
                try { CoreHaptics.Service?.FunScript.OnVideoStarted(path); }
                catch (Exception ex) { Log.Debug("FunScript start hook failed: {Error}", ex.Message); }
            }); }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _preroll = created;
            created.Change(PreRoll, Timeout.InfiniteTimeSpan);
            Log.Information("VideoService: playing {File} (strict={Strict})", Path.GetFileName(path), strict);
            return true;
        }

        /// <summary>WPF Cleanup: the natural end or an Esc dismiss. The flash resumes and, while the
        /// engine runs, the next video is scheduled.</summary>
        public void End()
        {
            if (!Finish()) return;
            AfterEnd();
        }

        private void AfterEnd()
        {
            Penalties = 0;
            CoreBubbles.Resume();   // WPF Cleanup: App.Bubbles?.Resume() - not before a verdict replay
            if (_running && CoreSettings.Current.FlashEnabled) CoreFlash.Start();
            if (_running) ScheduleNext();
        }

        /// <summary>WPF EndCurrentVideo: the clip played to its end. Scores the attention checks
        /// (pass XP and achievements); a fail or a troll closes the clip, shows the verdict and plays
        /// a fresh clip with the same strictness, until the MercyAfterFails-th replay earns mercy
        /// (MercySystemEnabled), which ends the run like <see cref="End"/>.
        /// Pass/fail reach achievements (and the head's Chaster "attention" note) through
        /// <see cref="CoreProgression.TrackAttentionCheck"/> (the head's provider also takes the
        /// Trainer's -25 XP); the Chaster "video" note is the head's, it knows whether a frame showed.</summary>
        public void Ended()
        {
            if (!_playing) return;
            var s = CoreSettings.Current;
            var verdict = Evaluate(s.AttentionChecksEnabled, AttentionSpawned, AttentionHits, _random.NextDouble());
            if (verdict != AttentionVerdict.None)
                Log.Information("Attention result: {Hits}/{Spawned} = {Verdict} (replays {Penalties})", AttentionHits, AttentionSpawned, verdict, Penalties);
            if (verdict is AttentionVerdict.Pass or AttentionVerdict.Troll)
            {
                CoreProgression.AddXP(AttentionPassXp(Penalties), "Video");
                CoreProgression.TrackAttentionCheck(true);
            }
            else if (verdict == AttentionVerdict.Fail) CoreProgression.TrackAttentionCheck(false);
            if (verdict is not (AttentionVerdict.Troll or AttentionVerdict.Fail)) { End(); return; }

            Penalties++;
            var mercy = Penalties >= s.MercyAfterFails && s.MercySystemEnabled;   // #1145: 2..10, default 3
            var strict = IsStrict;
            var gen = ++_retryGeneration;
            Finish();
            _host.ShowMessage(mercy ? AttentionVerdict.Mercy : verdict, mercy ? 2500 : 2000, () =>
            {
                if (gen != _retryGeneration) return;   // stopped or replaced while the message was up
                if (mercy || !Trigger(strict)) AfterEnd();
            });
        }

        /// <summary>WPF ForceCleanup: close without scheduling a replacement.</summary>
        public void ForceCleanup()
        {
            _retryGeneration++;
            // Deviation: WPF leaves bubbles paused here (its panic stops the engine too); this head's
            // fall-open panic force-stops the clip alone, and must not leave the field held forever.
            if (Finish() && _running) CoreBubbles.Resume();   // Stop() clears _running first: no resume after an engine stop
        }

        private bool Finish()
        {
            Dispose(ref _preroll);
            if (!_playing) return false;
            _playing = false;
            IsStrict = false;
            double watched = 0;
            try { watched = _host.CloseAll(); }
            catch (Exception ex) { Log.Warning(ex, "VideoService: close failed"); }
            if (watched >= 1.0) CoreProgression.TrackVideoWatched(watched);
            // WPF CloseAll (VideoService.cs:7916): every teardown path stops the vibe and the funscript.
            _ = CoreHaptics.Service?.StopVideoBackgroundVibeAsync();
            try { CoreHaptics.Service?.FunScript.OnVideoStopped(); }
            catch (Exception ex) { Log.Debug("FunScript stop hook failed: {Error}", ex.Message); }
            return true;
        }

        /// <summary>WPF VideoService.ReloadAssets: drop the dealt queue so the next pick re-reads the
        /// enabled library (an untick or preset switch takes effect on the very next video, #130).</summary>
        public void ReloadAssets() => _queue = new Queue<string>();

        /// <summary>WPF GetNextVideo, local half: a shuffled queue refilled when it runs dry.</summary>
        internal string? PickNext()
        {
            if (_queue.Count == 0)
            {
                var enabled = _library();
                var kept = KeepByLength(enabled, CoreSettings.Current.VideoMinDurationSeconds,
                    CoreSettings.Current.VideoMaxDurationSeconds, DurationOf);
                LastFunnelEnabled = enabled.Count;   // WPF _lastFunnelEnabled / _lastFunnelDuration (#1352)
                LastFunnelDuration = kept.Count;
                _queue = new Queue<string>(kept.OrderBy(_ => _random.Next()));
            }
            return _queue.Count > 0 ? _queue.Dequeue() : null;
        }

        /// <summary>WPF VideoService.MetadataCache: a clip's cached length, null on a miss. Null keeps
        /// every clip (WPF without LibVLC).</summary>
        public Func<string, double?>? DurationOf { get; set; }
        /// <summary>The last refill's enabled clips and how many the length filter kept; -1 before the first.</summary>
        public int LastFunnelEnabled { get; private set; } = -1;
        public int LastFunnelDuration { get; private set; } = -1;

        /// <summary>WPF RefillVideoQueues' duration filter: drop clips shorter than <paramref name="min"/> or
        /// longer than <paramref name="max"/> seconds (0 = unset). A clip with no cached length is kept
        /// (fall open; <paramref name="durationOf"/> starts its parse for the next refill).</summary>
        public static List<string> KeepByLength(IReadOnlyList<string> files, int min, int max, Func<string, double?>? durationOf)
        {
            if ((min <= 0 && max <= 0) || durationOf == null) return files.ToList();
            return files.Where(f => durationOf(f) is not double d || ((min <= 0 || d >= min) && (max <= 0 || d <= max))).ToList();
        }

        /// <summary>WPF RefillVideoQueues' local walk: assets/videos, recursive, supported extensions,
        /// minus the user's disabled files and folders.</summary>
        public static IReadOnlyList<string> LocalLibrary()
        {
            var root = CorePaths.EffectiveAssets;
            var dir = Path.Combine(root, "videos");
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Where(IsSupportedVideoExtension).ToList();
            return AssetFolderExclusion.Enabled(files, root, CoreSettings.Current);
        }

        private static void Dispose(ref ITimer? timer)
        {
            var t = timer;
            timer = null;
            t?.Dispose();
        }
    }
}
