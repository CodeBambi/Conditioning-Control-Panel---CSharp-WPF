using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Forms;
using NAudio.Wave;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using Application = System.Windows.Application;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel.Services
{
    /// <summary>Payload for <see cref="VideoService.VideoWatchCredited"/>: the real seconds watched
    /// of a video and its total length, so consumers can tally watch-time and detect skips.</summary>
    public sealed class VideoWatchInfoEventArgs : EventArgs
    {
        public double WatchedSec { get; set; }
        public double DurationSec { get; set; }
        public bool EndedNaturally { get; set; }
    }

    public class VideoService : IDisposable
    {
        private readonly Random _random = new();
        private Queue<string> _videoQueue = new();  // Performance: Changed to Queue for O(1) dequeue
        private Queue<(string PackId, PackFileEntry File)> _packVideoQueue = new();  // Queue for pack videos
        private readonly List<Window> _windows = new();
        private readonly List<FloatingText> _targets = new();
        private readonly List<string> _tempPackFiles = new();  // Track temp files for cleanup

        private DispatcherTimer? _scheduler;
        private DispatcherTimer? _attentionTimer;
        private DispatcherTimer? _safetyTimer;
        private DispatcherTimer? _fallbackSafetyTimer;
        // Hard wall-clock cap that force-ends a video once it has played for VideoMaxDurationSeconds.
        // The selection-time duration filter (RefillQueues) is best-effort — it lets a video with no
        // cached duration through and warms the cache in the background — so a long clip can slip past
        // the user's max on a cold cache (#584). This cap guarantees the max is never exceeded on screen.
        private DispatcherTimer? _maxLenCapTimer;

        // ---- UI-thread wedge watchdog (freeze/lockout rescue, #529/#532 storm) ----
        // The _safetyTimer/_fallbackSafetyTimer above are DispatcherTimers: dead weight the moment
        // the dispatcher itself blocks (native LibVLC detach/attach or a layered-window render
        // deadlock while creating the next fullscreen video window). Those are the "final frame froze
        // and locked out my whole computer for minutes" reports. This watchdog lives OFF the UI thread:
        // a UI-thread heartbeat bumps _uiHeartbeatTicks; a threadpool timer notices when it goes stale
        // while a video is playing and force-breaks the wedge (off-thread player.Stop, the same call
        // CloseAll already makes from background tasks) + posts a teardown so the machine recovers on
        // its own instead of needing a hard shutdown. Armed at the very top of PlayVideo — BEFORE any
        // window is created — because the wedge frequently happens mid-creation, before the safety
        // timers are even armed.
        // ---- Vout self-heal (white-screen storm #557/#558/#559/#560/#574) ----
        // Software decode (#533/#537/#540) did not end the white screens: on the affected machines
        // the failure is OUTPUT-side — the player decodes fine but never creates a video output, so
        // the window stays white. Worse, once one teardown wedges, disposing the wedged player
        // poisons the shared LibVLC instance and EVERY later video white-screens until app restart
        // (#559: "it runs fine a while, and then gets bugged"). Self-heal in three parts:
        //   1. Vout watchdog: no video output within VoutGraceMs of Play ⇒ retire the shared LibVLC
        //      and retry the same video ONCE on a fresh instance.
        //   2. Rooted quarantine: a player whose Stop() wedged is NEVER Dispose()d — that dispose is
        //      the poisoning step. It is rooted in _nativeQuarantine instead (a bounded native leak
        //      beats a poisoned pipeline) and the owning shared instance is retired.
        //   3. Retire-on-wedge: any UI-wedge rescue during playback also retires the instance, so
        //      the next video starts clean instead of inheriting the corrupted native state.
        //   4. Mid-play re-arm: the start-of-play watchdog is one-shot, so a vout that appears and
        //      then VANISHES mid-clip (the screen goes white partway through — #600) went unhealed
        //      until the user toggled the engine. A periodic poll watches for a vout that was present
        //      and stays gone past VoutLostGraceMs, then runs the SAME retire+replay heal once.
        private System.Threading.Timer? _voutWatchTimer;
        private volatile bool _voutSeen;
        private bool _voutRetryUsed;
        private const int VoutGraceMs = 8000;
        // Mid-play vout-loss detection (part 4). Separate from the one-shot start watchdog so the
        // proven start-of-play path is untouched. Polls the primary player's live VoutCount.
        private System.Threading.Timer? _voutMidWatchTimer;
        private volatile bool _voutEverSeen;        // vout has been present at least once this playback
        private long _voutLostSinceTicks;           // UtcNow ticks when vout first went absent after being seen (0 = present/never)
        private bool _voutMidHealUsed;              // one mid-play heal per playback (own budget, independent of _voutRetryUsed)
        private const int VoutMidPollMs = 2000;     // poll cadence
        // Loss must persist this long before healing — generous so a brief drop during a seek/segment
        // boundary or the tail end of a clip does not truncate a healthy video.
        private const int VoutLostGraceMs = 5000;
        private static readonly List<object> _nativeQuarantine = new();
        private static readonly object _quarantineLock = new();
        private static bool _coreLoaded;   // Core.Initialize is per-process; retire/re-init must not repeat it
        // Circuit breaker: each retire roots a whole LibVLC instance in quarantine forever, so on a
        // machine where every video output-fails the heal would otherwise leak two instances per
        // scheduled video, unboundedly. After the cap the watchdog stops retiring (videos are still
        // skipped at the grace deadline, but no more native instances are condemned).
        private static int _libVLCRetireCount;
        private const int MaxLibVLCRetiresPerSession = 4;
        // Bumped by every CloseAll. Watchdog actions (vout heal, wedge rescue) snapshot it when they
        // fire and abort if it moved by the time their dispatched continuation runs - otherwise a
        // stale continuation can resurrect a video the user panicked away, run reentrantly inside
        // another teardown's message pump, or kill a newer video it never belonged to.
        private int _teardownGeneration;

        private System.Threading.Timer? _wedgeWatchdog;
        private DispatcherTimer? _heartbeatTimer;
        private long _uiHeartbeatTicks;
        private volatile bool _wedgeRescueFired;
        private volatile bool _preRollStallLogged; // one pre-roll stall line per armed watchdog
        // Fire only after a long, unambiguous stall — this targets the multi-minute lockouts, never a
        // UI thread that's merely busy for a beat. The legitimate ~4s teardown pump-wait keeps the
        // heartbeat ticking (it pumps Background-priority work), so it won't trip this.
        // #687: 22s was too patient to be a rescue. UiHangWatchdog already declares a hang at 15s and
        // both freeze reporters had task-killed the process by ~26s, so the off-thread rescue was
        // racing a user with Task Manager open and losing. 8s of ZERO Normal-priority dispatcher
        // callbacks is already pathological (the pump-wait keeps beating), so the rescue now engages
        // while the user is still watching the frozen frame rather than after they gave up.
        private const int WedgeStallMs = 8000;

        // Watch-time crediting for stats/quests (#447). _lastWatchPositionMs tracks the current video's
        // latest playback position (LibVLC TimeChanged); _creditedWatchSeconds is a watermark of what's
        // already been credited so repeated teardown calls can't double-count.
        private double _lastWatchPositionMs;
        private double _creditedWatchSeconds;

        private bool _isRunning;
        private bool _videoPlaying;
        // True only once a real player/window exists on screen. _videoPlaying goes true ~2.6s
        // EARLIER — at the top of PlayVideo, before the Discord/flash/duck prologue and the 1.3s
        // announce delay — so it cannot be used to answer "is playback actually live?". The wedge
        // watchdog used to ask exactly that of _videoPlaying and treated a pre-roll stall as a
        // wedged playback, whose rescue rebuilds LibVLC on a background thread while the UI thread
        // is on its way into EnsureLibVLCInitialized: a _libVLCLock race that killed the process
        // natively with an empty crash.log (#750-#753). Set in StartVideoPlayback, cleared by every
        // teardown path.
        private volatile bool _playbackStarted;
        private bool _triggerInProgress; // Guards the 800ms freeze delay window in TriggerVideo
        private bool _strictActive;
        // True across the strict retry GAP: from the moment a strict run's attention check fails
        // until the replacement video is actually on screen. ShowMessage deliberately clears
        // _videoPlaying for the ~2s "TRY AGAIN" window (the strict Closing veto reads that field and
        // would otherwise refuse to let the message windows replace the video), which made
        // IsStrictActive report false for the whole gap — precisely the moment a frustrated user
        // shouts "stop the video", and the voice guards would have waved it through.
        //
        // Scoped to the retry gap ON PURPOSE rather than mirroring _strictActive everywhere: that
        // keeps the `_videoPlaying &&` conjunct in IsStrictActive protecting every other path, so a
        // stuck value here can't wedge the user out of their own stop controls for longer than one
        // message window. Cleared by every real end-of-run path (Stop / ForceCleanup / Cleanup), by
        // the start of any new video, and by the retry callback's own finally.
        private bool _strictRetryPending;
        // Bumped whenever a queued attention-fail retry becomes stale. The retry fires from a bare
        // Task.Delay continuation that nothing can cancel, so instead of cancelling it we let it run
        // and have it discover it is obsolete: it captures this value when scheduled and bails if it
        // no longer matches. Without this, panic during the message window announced "we're stopping,
        // you're safe" and then started another mandatory video ~2s later.
        private int _retryGeneration;
        // True while THIS video holds an audio-duck ref (App.Audio.Duck is ref-counted). Balanced
        // 1:1 with the Duck in PlayVideo by an Unduck in CloseAll — every teardown path (natural end,
        // attention retry / troll "watch again" loop, engine Stop, ForceCleanup) runs through CloseAll,
        // so the duck can never leak the ref count and pin other apps at the ducked level (#526).
        private bool _didDuck;
        private string? _retryPath;
        private DateTime _startTime;
        private double _duration;

        // #536: a Deeper enhancement (VideoEnhancementBridge) can loop or hold the clip past its
        // declared duration (loop_region, SpeakHoldMode.LoopRegion, speak-pauses). While one is
        // bound the safety timer switches from a duration guillotine to a progress-based stall
        // watch (see StartSafetyTimer) so real playback isn't cut "after the original time is over".
        private volatile bool _enhancementDriving;
        private long _lastSafetyTimeMs = -1;
        private DateTime _lastSafetyProgressUtc = DateTime.MinValue;
        // Recheck cadence + no-progress grace used only while an enhancement is driving.
        private static readonly TimeSpan EnhancementRecheckInterval = TimeSpan.FromSeconds(15);
        private const double EnhancementStallGraceSeconds = 90;

        /// <summary>
        /// Set by <see cref="Services.Deeper.VideoEnhancementBridge"/> while a Deeper enhancement is
        /// bound to the primary player. Such an enhancement can loop/hold the clip well past its
        /// declared duration, so the duration-keyed safety timer must not force-close a video that is
        /// still genuinely advancing. Cleared on unbind and defensively in CloseAll. (#536)
        /// </summary>
        public void SetEnhancementDriving(bool active) => _enhancementDriving = active;

        // Cleanup synchronization to prevent race conditions
        private readonly object _cleanupLock = new();
        private volatile bool _isCleaningUp;

        // Maximum video duration fallback (10 minutes) - if LengthChanged never fires
        private const int MaxVideoFallbackSeconds = 600;
        
        private List<double> _spawnTimes = new();
        private int _hits, _total, _spawned, _penalties;
        private List<Window> _messageWindows = new();  // Track message windows for cleanup
        private bool _codecWarningShown;  // Only show codec warning once per session

        private string _videosPath = "";

        // LibVLC for codec-independent video playback
        private static LibVLC? _libVLC;
        private static readonly object _libVLCLock = new();
        private static bool _libVLCInitialized;
        private static bool _libVLCInitializing;
        // Recreated on every retire (see RetireSharedLibVLC) so WaitForLibVLC waits for the REBUILD
        // instead of instantly returning false off the first init's completed TCS. Reads/writes are
        // under _libVLCLock or tolerate a stale snapshot (worst case: one early false).
        private static TaskCompletionSource<bool> _libVLCReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<LibVLCSharp.Shared.MediaPlayer> _mediaPlayers = new();
        private readonly object _mediaPlayersLock = new();  // Thread-safe access to _mediaPlayers

        // ---- Blurred-background (TikTok-style) render path ----
        // Live memory-render surfaces (one per screen while a blurred-background video plays). Torn
        // down in CloseAll — invalidates the frame buffer + unhooks the render tick, then frees the
        // native buffer after a delay so an in-flight LibVLC frame can't touch freed memory.
        private readonly List<BlurVmemSurface> _blurSurfaces = new();

        // Primary-monitor refs for the Deeper enhancement engine. Set when the
        // audio-bearing video window is created, cleared in CloseAll. Reading
        // these from outside the playback flow is safe — null means "no video
        // is currently the primary", which the engine treats as "do nothing".
        private LibVLCSharp.Shared.MediaPlayer? _primaryMediaPlayer;
        private Window? _primaryVideoWindow;

        public event EventHandler? VideoAboutToStart; // Fires 1.3s before video
        public event EventHandler? VideoStarted;
        public event EventHandler? VideoEnded;
        /// <summary>Fires once per video at watch-credit finalize (teardown), carrying the real
        /// seconds watched + the clip length. Consumers (e.g. DtRH session telemetry) use it to
        /// tally watch-time and classify a skip (watched well short of the end). Reuses the
        /// existing interruption-safe credit path (#447) - no new timing.</summary>
        public event EventHandler<VideoWatchInfoEventArgs>? VideoWatchCredited;

        // ---- chaos random-segment mode (one-shot) ----
        // The NEXT triggered video jumps to a random position leaving at least _segmentSec of
        // runway (the chaos 15s cap then ends it — so the player sees a random 15s slice, not
        // always the opening). One shared fraction keeps dual-monitor mirrors in sync. Armed
        // immediately before TriggerVideo by the chaos VideoPayload; disarmed in CloseAll.
        private double _segmentSec;
        private double _segmentFraction;
        private DateTime _segmentArmedAtUtc = DateTime.MinValue;
        private bool SegmentArmed => (DateTime.UtcNow - _segmentArmedAtUtc).TotalSeconds < 30;

        /// <summary>Chaos: make the next video start at a random position with at least
        /// <paramref name="segmentSec"/> seconds left to play.</summary>
        public void ArmRandomSegment(double segmentSec)
        {
            _segmentSec = Math.Max(1, segmentSec);
            _segmentFraction = Random.Shared.NextDouble();
            _segmentArmedAtUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Primary (audio-bearing) media player, or null if no video is playing.
        /// Exposed for the Deeper EnhancementEngine to read playback time and
        /// drive Seek/Pause. Treat as read-only — the engine should not mutate
        /// state outside of Seek/Pause/Play helpers below.
        /// </summary>
        public LibVLCSharp.Shared.MediaPlayer? PrimaryMediaPlayer => _primaryMediaPlayer;

        /// <summary>
        /// Primary video window (audio monitor), or null if no video is playing.
        /// Used to compute screen-space video rect for gaze-target rules.
        /// </summary>
        public Window? PrimaryVideoWindow => _primaryVideoWindow;

        /// <summary>
        /// Current primary-player playback time in milliseconds, or -1 if none.
        /// </summary>
        public long GetCurrentPlaybackTimeMs()
        {
            try { return _primaryMediaPlayer?.Time ?? -1; }
            catch { return -1; }
        }

        /// <summary>
        /// Seek the primary player to the given absolute time. No-op if no
        /// video is active or the player rejects the seek (LibVLC will silently
        /// ignore for non-seekable streams).
        /// </summary>
        public void SeekPrimary(long ms)
        {
            // Mirror the seek to EVERY screen's player, not just the primary — otherwise a Deeper
            // blink/rewind ("pull backward") rewinds the primary monitor while the secondary clone
            // keeps playing, so the two screens desync (#527). Only the primary raises events, but
            // all players must track the same position.
            var target = Math.Max(0, ms);
            foreach (var p in SnapshotPlayers())
            {
                try
                {
                    if (p.IsSeekable) p.Time = target;
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("VideoService.SeekPrimary failed: {Error}", ex.Message);
                }
            }
        }

        /// <summary>Pause every screen's player (kept in lockstep for multi-monitor). No-op if none.</summary>
        public void PausePrimary()
        {
            foreach (var p in SnapshotPlayers())
            {
                try { p.SetPause(true); }
                catch (Exception ex) { App.Logger?.Debug("VideoService.PausePrimary failed: {Error}", ex.Message); }
            }
        }

        /// <summary>Resume every screen's player (kept in lockstep for multi-monitor). No-op if none.</summary>
        public void PlayPrimary()
        {
            foreach (var p in SnapshotPlayers())
            {
                try { p.SetPause(false); }
                catch (Exception ex) { App.Logger?.Debug("VideoService.PlayPrimary failed: {Error}", ex.Message); }
            }
        }

        /// <summary>Thread-safe snapshot of all active players (all screens) for lockstep control.</summary>
        private List<LibVLCSharp.Shared.MediaPlayer> SnapshotPlayers()
        {
            lock (_mediaPlayersLock) { return _mediaPlayers.ToList(); }
        }

        /// <summary>
        /// Fired when the primary player's playback position advances.
        /// Argument is current time in milliseconds. Fires from LibVLC's
        /// internal thread; subscribers must marshal to the UI thread.
        /// </summary>
        public event Action<long>? PrimaryPlaybackTimeMsChanged;

        /// <summary>
        /// Whether a video is currently playing
        /// </summary>
        public bool IsPlaying => _videoPlaying;

        /// <summary>
        /// True while a strict-locked video is actively playing. Escape hatches that
        /// bypass the main Stop button's lockdown guard (e.g. the avatar's quick menu)
        /// must respect this — stopping the engine mid-strict-video both defeats the
        /// lock and races the LibVLC teardown (#479).
        ///
        /// "Actively playing" includes the attention-fail retry gap: no video is on screen for
        /// those ~2 seconds, but the strict run has not ended and must not be escapable through
        /// it. See <see cref="_strictRetryPending"/>.
        /// </summary>
        public bool IsStrictActive => (_videoPlaying && _strictActive) || _strictRetryPending;

        /// <summary>
        /// Whether any video windows still exist. Stays true through teardown after
        /// <see cref="IsPlaying"/> has already flipped false — use this when something
        /// must not be shown until the fullscreen surfaces are really gone (#462).
        /// </summary>
        public bool HasOpenWindows => _windows.Count > 0;

        /// <summary>
        /// True while CloseAll is tearing windows down. It pumps the dispatcher at Background
        /// priority while it runs, so anything that must not execute re-entrantly inside that
        /// pump (e.g. a modal dialog) should wait this out — the flag always clears in a
        /// finally, so waiting terminates (#462).
        /// </summary>
        public bool IsCleaningUp => _isCleaningUp;

        /// <summary>
        /// Filename (without extension) of the most recently started video. Stays set after
        /// the video ends so VideoEnded handlers can pass it to companion AI reactions.
        /// </summary>
        public string? LastVideoTitle =>
            string.IsNullOrEmpty(_retryPath) ? null : Path.GetFileNameWithoutExtension(_retryPath);

        /// <summary>
        /// Full file path of the most recently started video. Read by SessionLogService
        /// inside the VideoStarted handler. Null/empty when no video has played yet.
        /// </summary>
        public string? LastVideoPath => _retryPath;
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Number of failed attention attempts in the CURRENT video playthrough (the per-video
        /// penalty counter; reset when a new video starts). Exposed for the bark system's
        /// "failed this video N times" reaction.
        /// </summary>
        public int PlaythroughFailCount => _penalties;

        /// <summary>
        /// Get the shared LibVLC instance (used by BubbleCountWindow).
        /// Returns null if not yet initialized - caller should handle this.
        /// LibVLC is initialized via PreloadLibVLC() during app startup.
        /// </summary>
        public static LibVLC? SharedLibVLC => _libVLC;

        // Lazy-init cache of per-video duration metadata. Used by the
        // min/max duration filter in RefillVideoQueues; missing entries fall
        // open (video is included, duration parsed lazily on next refill).
        private VideoMetadataCache? _metadataCache;
        public VideoMetadataCache? MetadataCache
        {
            get
            {
                if (_metadataCache != null) return _metadataCache;
                if (_libVLC == null) return null;
                _metadataCache = new VideoMetadataCache(_libVLC);
                return _metadataCache;
            }
        }

        /// <summary>
        /// Snapshot of currently-active attention targets that should respond
        /// to Focus Gaze dwells. Returns empty when VideoGazeClickEnabled is
        /// off. Caller iterates in reverse for topmost-first selection.
        /// </summary>
        internal IReadOnlyList<FloatingText> GetGazeTargets()
        {
            if (App.Settings?.Current?.VideoGazeClickEnabled != true)
                return Array.Empty<FloatingText>();
            lock (_targets)
            {
                return _targets.ToArray();
            }
        }

        /// <summary>
        /// Programmatic equivalent of a mouse click on an attention target.
        /// Runs the same idempotent Hit() pipeline (sound, onHit callback,
        /// fade). Safe to call against a target that's already been hit or
        /// destroyed.
        /// </summary>
        internal void GazeClick(FloatingText target)
        {
            if (target == null) return;
            target.Hit();
        }

        /// <summary>
        /// Wait for LibVLC initialization to complete (with timeout).
        /// Returns true if LibVLC is available, false if timeout or init failed.
        /// </summary>
        public static bool WaitForLibVLC(int timeoutMs = 5000)
        {
            if (_libVLCInitialized) return _libVLC != null;

            try
            {
                if (_libVLCReady.Task.Wait(timeoutMs))
                    return _libVLC != null;
            }
            catch { }

            App.Logger?.Warning("VideoService: Timed out waiting for LibVLC initialization");
            return _libVLC != null;
        }

        public VideoService()
        {
            RefreshVideosPath();
            // LibVLC initialization is deferred to first video playback for faster startup
        }

        /// <summary>
        /// Pre-initialize LibVLC during app startup to avoid slow first video.
        /// Call this from App.OnStartup() in a background task.
        /// </summary>
        public void PreloadLibVLC()
        {
            Task.Run(() =>
            {
                App.Logger?.Information("VideoService: Pre-loading LibVLC in background...");
                EnsureLibVLCInitialized();
                App.Logger?.Information("VideoService: LibVLC pre-load complete, initialized={Initialized}", _libVLCInitialized);
            });
        }

        /// <summary>
        /// Initialize LibVLC for codec-independent video playback.
        /// Uses VLC's bundled codecs instead of Windows Media Foundation.
        /// Called lazily on first video playback to improve startup time.
        /// </summary>
        private void EnsureLibVLCInitialized()
        {
            lock (_libVLCLock)
            {
                if (_libVLCInitialized || _libVLCInitializing) return;
                _libVLCInitializing = true;
            }

            InitializeLibVLCCore();
        }

        private void InitializeLibVLCCore()
        {
            lock (_libVLCLock)
            {
                if (_libVLC != null)
                {
                    _libVLCInitialized = true;
                    _libVLCInitializing = false;
                    _libVLCReady.TrySetResult(true);
                    return;
                }

                try
                {
                    // Load the native libvlc libraries once per process. A retire/re-init cycle
                    // (see RetireSharedLibVLC) must NOT run Core.Initialize again - only the managed
                    // LibVLC instance is recreated.
                    if (!_coreLoaded)
                    {
                        // Find the libvlc folder - check for libvlc.dll existence, not just folder
                        var appDir = AppDomain.CurrentDomain.BaseDirectory;
                        string? libvlcPath = null;

                        // Try paths in order of preference
                        var pathsToTry = new[]
                        {
                            Path.Combine(appDir, "libvlc", "win-x64"),  // NuGet package structure
                            Path.Combine(appDir, "libvlc"),             // Direct folder
                            appDir                                       // Same folder as exe
                        };

                        foreach (var path in pathsToTry)
                        {
                            var dllPath = Path.Combine(path, "libvlc.dll");
                            App.Logger?.Information("Checking for LibVLC at: {Path}", dllPath);
                            if (File.Exists(dllPath))
                            {
                                libvlcPath = path;
                                App.Logger?.Information("Found libvlc.dll at: {Path}", path);
                                break;
                            }
                        }

                        if (libvlcPath != null)
                        {
                            // Initialize LibVLCSharp core with explicit path
                            Core.Initialize(libvlcPath);
                            App.Logger?.Information("LibVLC core initialized from: {Path}", libvlcPath);
                        }
                        else
                        {
                            // Try default initialization (may find system-installed VLC)
                            App.Logger?.Information("libvlc.dll not found in expected locations, trying default initialization");
                            Core.Initialize();
                            App.Logger?.Information("LibVLC core initialized from default location");
                        }
                        _coreLoaded = true;
                    }

                    // Create LibVLC instance with audio and video options.
                    // Don't force --aout: on Windows 11 the DirectSound module silently fails to
                    // bind on some setups (Rose, build 26200) and produces no audio. Letting LibVLC
                    // auto-pick selects mmdevice (WASAPI) on Win7+, which is the modern default.
                    _libVLC = new LibVLC(
                        "--no-video-title-show",  // Don't show filename
                        "--no-osd",               // No on-screen display
                        "--gain=1.0",             // Audio gain
                        "--no-disable-screensaver", // Don't interfere with screensaver
                        "--no-mouse-events",      // Prevent click-to-pause on video surface
                        "--no-keyboard-events",   // Prevent keyboard input to video surface
                        "--verbose=-1"            // Reduce logging
                    );

                    App.Logger?.Information("LibVLC initialized successfully (version {Version})", _libVLC.Version);

                    // TODO(audio-diag): remove once Rose's no-audio bug (#197/#200/#201) is confirmed fixed.
                    // Logs available aout modules so we can see which one LibVLC auto-picks on systems
                    // with broken DirectSound binding.
                    try
                    {
                        var outputs = _libVLC.AudioOutputs;
                        if (outputs != null)
                        {
                            var names = string.Join(", ", outputs.Select(o => $"{o.Name} ({o.Description})"));
                            App.Logger?.Information("LibVLC available aout modules: {Outputs}", names);
                        }
                    }
                    catch (Exception aoutEx)
                    {
                        App.Logger?.Warning("LibVLC AudioOutputs enumeration failed: {Error}", aoutEx.Message);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Failed to initialize LibVLC - falling back to MediaElement");
                    _libVLC = null;
                }
                finally
                {
                    // Only mark as initialized if it succeeded — allows retry on transient failures
                    // (e.g., DLL temporarily locked). If it failed, _libVLC is null and next call retries.
                    _libVLCInitialized = _libVLC != null;
                    _libVLCInitializing = false;
                    _libVLCReady.TrySetResult(_libVLC != null);
                }
            }
        }

        /// <summary>
        /// Root a wedged native object forever so it is never disposed or finalized. Disposing a
        /// wedged player is exactly what corrupts the shared LibVLC instance (the #559 "every video
        /// after is broken" poisoning) — and its finalizer would call the same native teardown, so
        /// the object must stay strongly reachable for the life of the process. A handful of leaked
        /// players (~a few MB each, and wedges are rare) is a far better failure mode.
        /// </summary>
        private static void QuarantineNative(object nativeObj, string reason)
        {
            int count;
            lock (_quarantineLock)
            {
                _nativeQuarantine.Add(nativeObj);
                count = _nativeQuarantine.Count;
            }
            App.Logger?.Warning("VideoService: quarantined a wedged {Type} ({Reason}) - {Count} native object(s) rooted",
                nativeObj.GetType().Name, reason, count);
            // A quarantine entry means a native Stop() never came back — the exact precondition for
            // the "one bad teardown, then every video is a white screen" spiral (#559, and the
            // suspected shape of #616/#617/#621/#622/#623). Count it in the trace.
            VideoDiag.Log("QUARANTINE", $"{nativeObj.GetType().Name} rooted ({reason}) - {count} object(s) now quarantined");
        }

        /// <summary>
        /// Retire the shared LibVLC instance: quarantine it (never dispose - players created from it
        /// may still be wedged inside native calls) and clear the init flags so the next video's
        /// EnsureLibVLCInitialized builds a fresh instance. <paramref name="suspect"/> guards against
        /// retiring an instance that was already replaced (e.g. the async disposal task condemning
        /// the old instance after a vout retry already built the new one): pass the instance that
        /// misbehaved, and the retire is skipped if it is no longer the shared one.
        /// </summary>
        private bool RetireSharedLibVLC(LibVLC? suspect, string reason)
        {
            // No suspect means the caller doesn't know which instance misbehaved (e.g. it captured
            // _libVLC after a retire already nulled it). Never condemn blind: on the vout-retry path
            // a null capture would otherwise bypass the ReferenceEquals guard below and quarantine
            // the FRESH instance the retry just built.
            if (suspect == null)
            {
                VideoDiag.Log("RETIRE", $"declined ({reason}) - no suspect instance supplied");
                return false;
            }

            lock (_libVLCLock)
            {
                if (_libVLC == null) { VideoDiag.Log("RETIRE", $"declined ({reason}) - already retired"); return false; }
                if (!ReferenceEquals(_libVLC, suspect)) { VideoDiag.Log("RETIRE", $"declined ({reason}) - suspect is no longer the shared instance"); return false; }
                if (_libVLCRetireCount >= MaxLibVLCRetiresPerSession)
                {
                    App.Logger?.Warning(
                        "VideoService: retire requested ({Reason}) but the per-session cap ({Cap}) is reached - keeping the current instance",
                        reason, MaxLibVLCRetiresPerSession);
                    // Circuit breaker tripped: from here on every video output failure is permanent
                    // for the rest of the session. A report whose trace shows this line explains a
                    // "it worked for a while and then every video was a black screen" description.
                    VideoDiag.Log("RETIRE", $"CIRCUIT BREAKER - cap {MaxLibVLCRetiresPerSession} reached, no further self-heal this session ({reason})");
                    return false;
                }
                _libVLCRetireCount++;

                lock (_quarantineLock) { _nativeQuarantine.Add(_libVLC); }
                App.Logger?.Warning(
                    "VideoService: retiring shared LibVLC instance ({Reason}, retire {N}/{Cap}) - a fresh instance will be created for the next video",
                    reason, _libVLCRetireCount, MaxLibVLCRetiresPerSession);
                VideoDiag.Log("RETIRE", $"shared LibVLC retired ({reason}) - {_libVLCRetireCount}/{MaxLibVLCRetiresPerSession}");
                _libVLC = null;
                _libVLCInitialized = false;
                _libVLCInitializing = false;
                // The old TCS is already completed; a fresh one lets WaitForLibVLC actually WAIT for
                // the rebuild (BubbleCount's recovery calls PreloadLibVLC + WaitForLibVLC - with the
                // stale completed TCS it returned false in microseconds while the rebuild was mid-flight).
                _libVLCReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            // The duration cache holds Media objects created from the retired instance; drop it so
            // it lazily rebuilds against the fresh one (the retired instance stays rooted, so any
            // in-flight parse can finish harmlessly).
            _metadataCache = null;
            // Proactively rebuild off-thread: SharedLibVLC consumers outside the mandatory-video
            // pipeline (bubble count, mini player, help/Deeper previews) read the static directly and
            // would otherwise be stranded on null until the next scheduled video runs EnsureLibVLC.
            // Init is serialized under _libVLCLock, so this can't race the retry's own init - the
            // retry either finds the fresh instance ready or briefly blocks on the lock until it is.
            Task.Run(() =>
            {
                // NOTE (#616-#623): this holds _libVLCLock for the whole of a native `new LibVLC(...)`.
                // A UI-thread EnsureLibVLCInitialized (StartVideoPlayback) that lands in the same
                // window blocks on this lock — bracket it so a frozen session's trace shows whether
                // the rebuild was still in flight when the dispatcher went silent.
                VideoDiag.Log("RETIRE", "background LibVLC rebuild begin (holds _libVLCLock)");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { EnsureLibVLCInitialized(); }
                catch (Exception ex) { App.Logger?.Debug("VideoService: background LibVLC rebuild failed: {E}", ex.Message); }
                VideoDiag.Log("RETIRE", $"background LibVLC rebuild end after {sw.ElapsedMilliseconds}ms");
            });
            return true;
        }

        /// <summary>
        /// Refresh the videos path based on current settings.
        /// Call this after changing the custom assets path.
        /// </summary>
        public void RefreshVideosPath()
        {
            _videosPath = Path.Combine(App.EffectiveAssetsPath, "videos");
            Directory.CreateDirectory(_videosPath);
            _videoQueue.Clear();
            _packVideoQueue.Clear();
            App.Logger?.Information("VideoService: Videos path refreshed to {Path}", _videosPath);
        }

        /// <summary>
        /// Reloads all video assets (regular and pack videos).
        /// Call this when pack activation state changes.
        /// </summary>
        public void ReloadAssets()
        {
            var beforeRegular = _videoQueue.Count;
            var beforePack = _packVideoQueue.Count;
            _videoQueue.Clear();
            _packVideoQueue.Clear();
            CleanupTempPackFiles();
            App.Logger?.Information("VideoService: Assets reloaded - cleared queues (was {RegularCount} regular, {PackCount} pack)",
                beforeRegular, beforePack);
        }

        /// <summary>
        /// Update volume on all currently playing videos (for live master volume changes).
        /// </summary>
        public void UpdateMasterVolume(int volume)
        {
            UpdatePlayingVideosVolume();
        }

        /// <summary>
        /// Update video-specific volume (separate from master volume).
        /// </summary>
        public void UpdateVideoVolume(int volume)
        {
            UpdatePlayingVideosVolume();
        }

        /// <summary>
        /// An external owner (the DtRH dive) is holding every video silent. Its in-page
        /// mute is a WEB switch and can never reach LibVLC, so a mandatory video that
        /// covered the page kept fading its audio in over a muted descent. Volume-based
        /// rather than LibVLC's Mute: Mute reads unreliably before an audio output exists
        /// and the play path force-unmutes, so folding this into GetEffectiveVolume is the
        /// one place that covers every site at once - play-time AND live slider drags.
        /// </summary>
        private static volatile bool _externalMute;

        /// <summary>Silence (or release) every video for an external owner. Applies live.
        /// MUST be released on teardown - see DtrhHostService.DisposeAll.</summary>
        public void SetExternalMute(bool on)
        {
            if (_externalMute == on) return;
            _externalMute = on;
            App.Logger?.Information("VideoService external mute: {On}", on);
            UpdatePlayingVideosVolume();
        }

        /// <summary>
        /// Calculate effective volume combining master and video volume.
        /// </summary>
        private int GetEffectiveVolume()
        {
            if (_externalMute) return 0;
            var master = App.Settings.Current.MasterVolume;
            var video = App.Settings.Current.VideoVolume;
            return (int)((master / 100.0) * (video / 100.0) * 100);
        }

        /// <summary>
        /// Route the primary player to the user's chosen audio endpoint and log ONE line saying
        /// where its audio actually ended up. Runs once per video, off the LibVLC event thread,
        /// after Playing has fired.
        /// </summary>
        /// <remarks>
        /// LibVLC has no audio output (and therefore no device list, and no routing to read back)
        /// until playback is live, so the old construction-time ApplyPreferredDevice call always
        /// validated against an empty list and applied nothing - every mandatory video played to
        /// the Windows default endpoint while the NAudio paths honoured the setting. Users whose
        /// default endpoint is an HDMI monitor / dead / virtual device got silent video and
        /// audible everything-else, every time (#707/#708).
        ///
        /// The probe line is the diagnostic half: pre-fix a silent video left no trace at all
        /// ("LibVLC audio: Volume=99, Mute=false" is logged before any aout exists and only
        /// reports what was requested), so wrong-endpoint and an adummy fallback - LibVLC quietly
        /// picking the null output when mmdevice/directsound/waveout all fail to bind - looked
        /// identical in a bug report. Tagged [VIDEO] so BugReportService rescues it.
        /// </remarks>
        private void RouteAndProbeAudio(LibVLCSharp.Shared.MediaPlayer player, string label)
        {
            try
            {
                // Same staleness guards the vout watchdog uses: never touch a player that
                // teardown has already taken ownership of, or one from a previous video.
                if (_isCleaningUp || !_videoPlaying) return;
                if (!ReferenceEquals(player, _primaryMediaPlayer)) return;

                bool applied = App.Audio?.ApplyPreferredDevice(player) ?? false;
                var routing = App.Audio?.DescribeLibVlcAudioRouting(player) ?? "audio service unavailable";
                App.Logger?.Information("[VIDEO] aout live for {File}: deviceApplied={Applied} {Routing}",
                    label, applied, routing);
                VideoDiag.Log("AOUT", $"{label} deviceApplied={applied} {routing}");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoService: audio route/probe failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Apply current volume settings to all playing videos.
        /// </summary>
        private void UpdatePlayingVideosVolume()
        {
            var effectiveVolume = GetEffectiveVolume();

            // Update LibVLC media players (thread-safe snapshot)
            List<LibVLCSharp.Shared.MediaPlayer> playersCopy;
            lock (_mediaPlayersLock)
            {
                playersCopy = _mediaPlayers.ToList();
            }

            foreach (var player in playersCopy)
            {
                try
                {
                    // Set Volume on every player. The old `if (!player.Mute)` skip broke live
                    // drags: player.Mute reads true while no audio output exists yet (transient),
                    // so the primary player got skipped. Volume and Mute are independent in
                    // LibVLC - setting Volume never unmutes, and no-audio secondaries ignore it.
                    player.Volume = effectiveVolume;
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to update LibVLC player volume: {Error}", ex.Message);
                }
            }

            // Update MediaElement players in active windows
            foreach (var win in _windows.ToList())
            {
                try
                {
                    if (win.Content is Grid g && g.Children.Count > 0 && g.Children[0] is MediaElement me)
                    {
                        me.Volume = effectiveVolume / 100.0;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to update MediaElement volume: {Error}", ex.Message);
                }
            }
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            ScheduleNext();

            // Listen for Windows lock/unlock. Without this, a video that's playing when the
            // user hits Win-L survives the lock screen and lands back on the desktop in a
            // half-broken state — attention check buttons missing, overlays floating on top
            // of the video, and the EndReached cleanup sometimes never fires (leaves a black
            // window the user has to kill via tray). Force-cleanup on lock; the session can
            // resume normal scheduling on unlock.
            try
            {
                Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
                Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoService: failed to subscribe to SessionSwitch — {Error}", ex.Message);
            }

            // Sleep/wake doesn't always go through SessionSwitch (only fires if "lock on sleep"
            // is enabled). Without this, suspending the PC mid-video leaves the final frame
            // frozen on the desktop after wake, with the main window unreachable — users have
            // to kill the app from the tray. Force-cleanup on Suspend.
            try
            {
                Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoService: failed to subscribe to PowerModeChanged — {Error}", ex.Message);
            }

            App.Logger.Information("VideoService started");
        }

        public void Stop()
        {
            _isRunning = false;
            _scheduler?.Stop();
            _attentionTimer?.Stop();
            _safetyTimer?.Stop();
            _fallbackSafetyTimer?.Stop();
            _fallbackSafetyTimer = null;
            StopWedgeWatchdog();

            try { Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch; }
            catch { }
            try { Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged; }
            catch { }

            // Force cleanup of any playing video. Every caller of Stop() is a LIVE stop
            // (engine stop, panic, remote control, feature toggle) — app shutdown never
            // routes through here (OnExit ends in Environment.Exit). Use the async
            // disposal path: synchronous disposal freed the native MediaPlayer while
            // in-flight LibVLC callbacks could still touch it, a use-after-free that
            // killed the whole process when the engine was stopped mid-video (#479).
            // Windows still close immediately either way; only the player Dispose is
            // deferred ~1s to a background task.
            _videoPlaying = false;
            _strictActive = false;
            CancelPendingRetry();
            try
            {
                CloseAll(synchronous: false);
            }
            catch (FileNotFoundException)
            {
                // LibVLCSharp.WPF assembly not loaded (no video was played this session)
                // CloseAll references VideoView type which triggers JIT assembly load
                App.Logger?.Debug("VideoService.Stop: LibVLCSharp.WPF not loaded, skipping CloseAll");
            }

            App.Logger?.Information("VideoService stopped");
        }

        private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (e.Reason != Microsoft.Win32.SessionSwitchReason.SessionLock) return;
            if (!_videoPlaying && _windows.Count == 0) return;

            App.Logger?.Information("VideoService: Windows session locked while a video was active — force-cleaning to prevent broken-overlay state on unlock");

            try
            {
                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.HasShutdownStarted)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { ForceCleanup(); }
                        catch (Exception ex)
                        {
                            App.Logger?.Warning(ex, "VideoService: ForceCleanup on session lock threw");
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoService: SessionSwitch dispatch failed — {Error}", ex.Message);
            }
        }

        private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            // Suspend = the system is about to sleep. Resume = it just woke. We only act on
            // Suspend: tear down any active video so the wake doesn't land on a frozen frame
            // with no main window to interact with. (Resume normally fires SessionUnlock too,
            // but only when the user has "lock on sleep" enabled — we can't rely on it.)
            if (e.Mode != Microsoft.Win32.PowerModes.Suspend) return;
            if (!_videoPlaying && _windows.Count == 0) return;

            App.Logger?.Information("VideoService: System suspending while a video was active — force-cleaning to prevent frozen frame after wake");

            try
            {
                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.HasShutdownStarted)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { ForceCleanup(); }
                        catch (Exception ex)
                        {
                            App.Logger?.Warning(ex, "VideoService: ForceCleanup on suspend threw");
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoService: PowerModeChanged dispatch failed — {Error}", ex.Message);
            }
        }

        /// <param name="silentIfEmpty">
        /// When true, an empty videos folder is logged and ignored instead of popping the
        /// "no videos found" dialog. Used for the auto-played startup video (#333) so a user
        /// who simply hasn't added videos isn't greeted by a blocking prompt every launch.
        /// User-initiated triggers leave this false so they still get the helpful guidance.
        /// </param>
        /// <param name="strictOverride">
        /// Per-call strictness: null reads the global StrictLockEnabled (normal scheduled /
        /// manual videos); true/false forces it for this video only. Takeover passes the
        /// TakeoverVideosStrict setting here instead of the old "flip the global setting off
        /// for 3 seconds" hack, which made concurrently-scheduled videos come up non-strict.
        /// </param>
        public void TriggerVideo(bool silentIfEmpty = false, bool? strictOverride = null)
        {
            App.Logger?.Information("VideoService: TriggerVideo called");

            // Prevent overlapping triggers (e.g. during 800ms freeze delay)
            if (_triggerInProgress)
            {
                App.Logger?.Information("VideoService: TriggerVideo skipped - trigger already in progress");
                return;
            }

            // Teardown of a previous video is pumping messages (CloseAll/WaitWithMessagePump
            // runs a nested dispatcher loop for up to ~4s) — a trigger re-entering through
            // that pump would start creating windows while players are mid-detach, the
            // proven multi-monitor freeze path. Drop WITHOUT touching the queue slot:
            // during teardown, CurrentInteraction==Video is the DYING video's claim
            // (ForceCleanup sets _videoPlaying=false at its top), and releasing it here
            // would dequeue the next interaction into this very pump. ForceCleanup's own
            // trailing CompleteIfCurrent(Video) releases once teardown is done.
            if (_isCleaningUp)
            {
                App.Logger?.Information("VideoService: TriggerVideo dropped - cleanup in progress");
                return;
            }

            // Chaos gif rain in flight: a mandatory video opening over a falling cascade is the
            // proven UI-thread killer (AppHangB1 2026-06-10). Drop the trigger outright — never
            // queue it. Chaos's own video bubbles are already gated upstream while the rain
            // falls, so this only ever drops ambient/scheduler triggers.
            if (ChaosGifCascadeOverlay.IsRaining)
            {
                App.Logger?.Information("VideoService: TriggerVideo dropped - chaos gif cascade in flight");
                // When this trigger was DEQUEUED, the queue already claimed the Video slot for
                // us — dropping without releasing would block every interaction for the
                // 5-minute stuck window. Safe on non-dequeue paths: current==Video with no
                // video playing only happens right after a dequeue.
                if (!_videoPlaying &&
                    App.InteractionQueue?.CurrentInteraction == InteractionQueueService.InteractionType.Video)
                {
                    App.InteractionQueue.Complete(InteractionQueueService.InteractionType.Video);
                }
                return;
            }

            // Check if another fullscreen interaction is active (bubble count, lock card)
            // If so, queue this video for later
            // Note: If CurrentInteraction is already Video, the queue dequeued us — proceed normally
            var alreadyActive = App.InteractionQueue?.CurrentInteraction == InteractionQueueService.InteractionType.Video;
            if (!alreadyActive && App.InteractionQueue != null && !App.InteractionQueue.CanStart)
            {
                App.Logger?.Information("VideoService: Queueing video - another interaction is active: {Type}",
                    App.InteractionQueue.CurrentInteraction);
                App.InteractionQueue.TryStart(
                    InteractionQueueService.InteractionType.Video,
                    () => TriggerVideo(silentIfEmpty, strictOverride),
                    queue: true);
                return;
            }

            // Notify queue we're starting (skip if queue already set us as active)
            if (!alreadyActive)
            {
                App.InteractionQueue?.TryStart(
                    InteractionQueueService.InteractionType.Video,
                    () => { }, // Already executing
                    queue: false);
            }

            // Force close any stuck/existing video windows first
            if (_videoPlaying || _windows.Count > 0)
            {
                App.Logger?.Warning("VideoService: Forcing cleanup of existing video before triggering new one");
                ForceCleanup();
            }

            _triggerInProgress = true;

            // Resolve strictness NOW (trigger time), not after the 800ms freeze delay —
            // the global setting can change inside that window.
            var strict = strictOverride ?? App.Settings.Current.StrictLockEnabled;

            // #732: selection is not cheap. For a content-pack clip GetNextVideo decrypts the whole
            // encrypted file to a temp path - read the file into a byte[], AES-decrypt into a second
            // copy, write it back to disk - and RefillVideoQueues can walk the entire video library
            // when the queues drain. Running that on the dispatcher froze the app for as long as the
            // copy took, with no exception and so no crash log. Users popping a "video" trigger
            // bubble hit it straight from the mouse handler and reported a hang they had to kill.
            //
            // Select off-thread, then resume on the UI thread: everything downstream (message boxes,
            // PlayVideo, LibVLC window creation) is UI-affine and must stay there.
            Task.Run(() =>
            {
                // Bracketed because this step is invisible from the outside and can take seconds
                // (content-pack decrypt, full-library refill). A trace that shows SELECT: begin and
                // never an end means the trigger died here, not in playback (#750-#753).
                var selectSw = System.Diagnostics.Stopwatch.StartNew();
                VideoDiag.Log("SELECT", "begin (off-thread)");
                string? selected = null;
                try
                {
                    selected = GetNextVideo();
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "VideoService: GetNextVideo failed");
                }
                VideoDiag.Log("SELECT", $"end after {selectSw.ElapsedMilliseconds}ms clip={(selected == null ? "(none)" : Path.GetFileName(selected))}");

                DispatcherHelper.RunOnUI(() =>
                {
                    try
                    {
                        ContinueTriggerVideo(selected, strict, silentIfEmpty);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "VideoService: TriggerVideo continuation failed");
                        _triggerInProgress = false;
                        App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.Video);
                    }
                });
            });
        }

        /// <summary>
        /// The rest of <see cref="TriggerVideo"/>, resumed on the UI thread once the clip has been
        /// chosen off it (#732). Split out rather than inlined so the expensive selection cannot
        /// drift back onto the dispatcher.
        /// </summary>
        private void ContinueTriggerVideo(string? path, bool strict, bool silentIfEmpty)
        {
            App.Logger?.Information("VideoService: GetNextVideo returned: {Path}", path ?? "(null)");

            if (string.IsNullOrEmpty(path))
            {
                _triggerInProgress = false;
                // No video to play - release the queue lock
                App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.Video);

                // Startup auto-play: a user who hasn't added videos shouldn't get a blocking
                // dialog on every launch (#333). Log and bail quietly — manual triggers still
                // fall through to the guidance prompt below.
                if (silentIfEmpty)
                {
                    App.Logger?.Information("VideoService: startup video skipped — no videos in {Path}", _videosPath);
                    return;
                }

                // Build helpful error message
                var activePackCount = App.ContentPacks?.GetActivePackIds()?.Count ?? 0;
                var installedPackCount = App.ContentPacks?.InstalledPacks?.Count ?? 0;
                var message = Loc.GetF("video_no_videos_found", _videosPath) + "\n\n";

                if (installedPackCount > 0 && activePackCount == 0)
                {
                    message += Loc.GetF("video_packs_installed_none_active", installedPackCount) + "\n";
                    message += Loc.Get("video_enable_packs_hint") + "\n\n";
                }
                else if (activePackCount > 0)
                {
                    message += Loc.GetF("video_active_packs_no_videos", activePackCount) + "\n\n";
                }

                message += Loc.Get("video_add_files_hint");

                System.Windows.MessageBox.Show(message, Loc.Get("video_no_videos_title"));
                return;
            }

            // Trigger Bambi Freeze subliminal+audio BEFORE video, but only if:
            // - No minigame is active
            // - Attention checks are NOT enabled (user needs to be alert to click targets)
            var skipFreeze = App.Settings.Current.AttentionChecksEnabled ||
                            (App.BubbleCount != null && App.BubbleCount.IsBusy);

            if (!skipFreeze)
            {
                // Defer the reset until video ends (pass deferReset: true)
                App.Subliminal?.TriggerBambiFreeze(deferReset: true);

                // Small delay to let the freeze effect register before video starts
                App.Logger?.Debug("VideoService: Starting 800ms freeze delay before PlayVideo");
                Task.Delay(800).ContinueWith(_ =>
                {
                    try
                    {
                        if (Application.Current?.Dispatcher == null)
                        {
                            App.Logger?.Warning("VideoService: Dispatcher is null after freeze delay, cannot play video");
                            _triggerInProgress = false;
                            App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.Video);
                            return;
                        }

                        App.Logger?.Debug("VideoService: Freeze delay complete, calling PlayVideo on UI thread");
                        DispatcherHelper.RunOnUISync(() =>
                        {
                            PlayVideo(path, strict);
                        });
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "VideoService: Delayed video play failed");
                        _triggerInProgress = false;
                        App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.Video);
                    }
                });
            }
            else
            {
                // Attention checks or minigame active - play video without freeze
                App.Logger?.Debug("VideoService: Playing video immediately (skipFreeze=true)");
                PlayVideo(path, strict);
            }
        }

        /// <summary>
        /// Play a specific video file (used for startup video)
        /// </summary>
        public void PlaySpecificVideo(string videoPath, bool strictMode)
        {
            if (string.IsNullOrEmpty(videoPath) || !System.IO.File.Exists(videoPath))
            {
                App.Logger?.Warning("VideoService: Specific video not found: {Path}", videoPath);
                return;
            }

            // Same re-entrancy protection as TriggerVideo: never start creating windows
            // while a previous video's teardown is pumping messages (freeze path). Drop
            // without releasing the queue slot — it's the dying video's claim, and
            // ForceCleanup's trailing CompleteIfCurrent(Video) frees it after teardown.
            if (_isCleaningUp)
            {
                App.Logger?.Information("VideoService: PlaySpecificVideo dropped - cleanup in progress");
                return;
            }

            // Check if another fullscreen interaction is active
            // If so, queue this video for later
            // Note: If CurrentInteraction is already Video, the queue dequeued us — proceed normally
            var alreadyActive = App.InteractionQueue?.CurrentInteraction == InteractionQueueService.InteractionType.Video;
            if (!alreadyActive && App.InteractionQueue != null && !App.InteractionQueue.CanStart)
            {
                App.InteractionQueue.TryStart(
                    InteractionQueueService.InteractionType.Video,
                    () => PlaySpecificVideo(videoPath, strictMode),
                    queue: true);
                return;
            }

            // Notify queue we're starting (skip if queue already set us as active)
            if (!alreadyActive)
            {
                App.InteractionQueue?.TryStart(
                    InteractionQueueService.InteractionType.Video,
                    () => { }, // Already executing
                    queue: false);
            }

            // Force close any stuck/existing video windows first
            if (_videoPlaying || _windows.Count > 0)
            {
                App.Logger?.Warning("VideoService: Forcing cleanup of existing video before playing specific video");
                ForceCleanup();
            }

            // Skip freeze if attention checks are enabled (user needs to click targets)
            if (!App.Settings.Current.AttentionChecksEnabled)
            {
                // Trigger Bambi Freeze subliminal+audio BEFORE video
                App.Subliminal?.TriggerBambiFreeze(deferReset: true);

                // Small delay to let the freeze effect register before video starts
                App.Logger?.Debug("VideoService: Starting 800ms freeze delay before specific video");
                Task.Delay(800).ContinueWith(_ =>
                {
                    try
                    {
                        if (Application.Current?.Dispatcher == null)
                        {
                            App.Logger?.Warning("VideoService: Dispatcher is null, cannot play specific video");
                            App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.Video);
                            return;
                        }

                        App.Logger?.Debug("VideoService: Freeze delay complete, calling PlayVideo for specific video");
                        DispatcherHelper.RunOnUISync(() =>
                        {
                            PlayVideo(videoPath, strictMode);
                        });
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "VideoService: Delayed specific video play failed");
                        App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.Video);
                    }
                });
            }
            else
            {
                // Attention checks enabled - play immediately without freeze
                App.Logger?.Debug("VideoService: Playing specific video immediately (attention checks enabled)");
                PlayVideo(videoPath, strictMode);
            }
        }

        /// <summary>
        /// Force cleanup without scheduling next - used for panic key and preventing stacking
        /// </summary>
        /// <param name="synchronous">If true, disposes LibVLC players synchronously (use during app exit)</param>
        public void ForceCleanup(bool synchronous = false)
        {
            // Panic / session-lock / suspend / wedge-rescue all land here. Bracketed because the
            // #616-#623 reports say the panic key "did nothing": if the trace shows PANIC received
            // but no "ForceCleanup begin", the keystroke never reached this service; if it shows
            // begin and no end, the teardown itself is where the app died.
            VideoDiag.Log("CLEANUP", $"ForceCleanup begin (synchronous={synchronous}, playing={_videoPlaying}, windows={_windows.Count})");
            _safetyTimer?.Stop();
            _fallbackSafetyTimer?.Stop();
            _fallbackSafetyTimer = null;
            _maxLenCapTimer?.Stop();
            _maxLenCapTimer = null;
            _videoPlaying = false;
            _playbackStarted = false;
            _triggerInProgress = false;
            _strictActive = false;
            CancelPendingRetry();
            CloseAll(synchronous);
            App.Audio?.ForceUnduck();
            _penalties = 0;

            // Release the InteractionQueue slot if we still hold it. Panic key / stuck-timer /
            // session-switch teardown routes through ForceCleanup (not Cleanup), so without this
            // the "Video" slot stayed claimed for the full 5-minute stuck window (#14) — blocking
            // the next interaction and leaving a dead fullscreen. CompleteIfCurrent is guarded, so
            // it never clears a BubbleCount/LockCard that has since taken over.
            App.InteractionQueue?.CompleteIfCurrent(InteractionQueueService.InteractionType.Video);

            App.Logger?.Information("VideoService: Force cleanup completed (synchronous={Sync})", synchronous);
            VideoDiag.Log("CLEANUP", "ForceCleanup end - every video window is closed");
        }

        /// <summary>
        /// Play a video URL on all screens (used for browser fullscreen with dual monitor)
        /// No attention checks, no strict mode - just playback
        /// </summary>
        public void PlayUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (_videoPlaying) return;

            EnsureLibVLCInitialized();

            if (_libVLC == null)
            {
                App.Logger?.Warning("Cannot play URL - LibVLC not available");
                return;
            }

            DispatcherHelper.RunOnUISync(() =>
            {
                _videoPlaying = true;
                _strictActive = false;
                CancelPendingRetry();

                var allScreens = App.GetAllScreensCached().ToList();
                if (allScreens.Count == 0) return;

                var primary = allScreens.FirstOrDefault(s => s.Primary) ?? allScreens[0];
                var secondaries = allScreens.Where(s => !s.Primary).ToList();

                // Create primary window with audio
                var primaryWin = CreateLibVLCUrlWindow(url, primary, withAudio: true);
                _windows.Add(primaryWin);

                // Create secondary windows (muted), unless capped on a high monitor count (#389)
                if (ShouldFillSecondaryMonitors(allScreens.Count))
                {
                    foreach (var screen in secondaries)
                    {
                        var win = CreateLibVLCUrlWindow(url, screen, withAudio: false);
                        _windows.Add(win);
                    }
                }
                else if (App.Settings.Current.DualMonitorEnabled && secondaries.Count > 0)
                {
                    App.Logger?.Information("Skipping {N} secondary video decoder(s) on {Total} monitors to avoid lag (#389); enable 'Fill all monitors with video' to override.",
                        secondaries.Count, allScreens.Count);
                }

                App.Logger?.Information("Playing URL via LibVLC on {Count} screen(s): {Url}", _windows.Count, url);
            });
        }

        /// <summary>
        /// Whether each secondary monitor should get its own LibVLC decoder. To avoid the lag
        /// of N independent decoders on high monitor counts (#389), 3+ monitors only fill the
        /// secondaries when the user opts in via FillAllMonitorsWithVideo. 1–2 monitor setups
        /// are unaffected (they still fill every screen when DualMonitor is on).
        /// <paramref name="screenCount"/> is the total screen count, including the primary.
        /// </summary>
        private static bool ShouldFillSecondaryMonitors(int screenCount)
        {
            if (!App.Settings.Current.DualMonitorEnabled) return false;
            if (screenCount <= 2) return true; // 1–2 monitors: unchanged
            return App.Settings.Current.FillAllMonitorsWithVideo;
        }

        private Window CreateLibVLCUrlWindow(string url, Screen screen, bool withAudio)
        {
            var dpiScale = BubbleCountWindow.GetDpiForScreen(screen);
            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = withAudio,
                Topmost = true,
                Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = screen.Bounds.X / dpiScale,
                Top = screen.Bounds.Y / dpiScale,
                Width = screen.Bounds.Width / dpiScale,
                Height = screen.Bounds.Height / dpiScale
            };

            var videoView = new VideoView
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Black
            };

            var mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC!);
            lock (_mediaPlayersLock)
            {
                _mediaPlayers.Add(mediaPlayer);
            }
            // The user's chosen output device is applied from the Playing handler further down,
            // never here: LibVLC has no audio output until playback starts, so a construction-time
            // call enumerates nothing and no-ops (#707/#708). See RouteAndProbeAudio.

            if (withAudio)
            {
                _primaryMediaPlayer = mediaPlayer;
                _primaryVideoWindow = win;

                mediaPlayer.TimeChanged += (s, e) =>
                {
                    _lastWatchPositionMs = e.Time; // track watched position for quest crediting (#447)
                    try { PrimaryPlaybackTimeMsChanged?.Invoke(e.Time); }
                    catch (Exception ex) { App.Logger?.Debug("PrimaryPlaybackTimeMsChanged handler error: {Error}", ex.Message); }
                };

                mediaPlayer.EndReached += (s, e) =>
                {
                    // CRITICAL: Detach from LibVLC thread immediately to prevent deadlocks
                    App.Logger?.Information("VideoService: LibVLC URL EndReached fired, _isCleaningUp={Cleaning}", _isCleaningUp);
                    Task.Run(() =>
                    {
                        try
                        {
                            // Skip if cleanup already in progress (e.g., from panic key)
                            if (_isCleaningUp)
                            {
                                App.Logger?.Debug("VideoService: URL EndReached skipped - cleanup already in progress");
                                return;
                            }

                            var dispatcher = Application.Current?.Dispatcher;
                            if (dispatcher == null || dispatcher.HasShutdownStarted)
                            {
                                App.Logger?.Warning("VideoService: Dispatcher unavailable in URL EndReached");
                                return;
                            }
                            dispatcher.BeginInvoke(() =>
                            {
                                if (_isCleaningUp) return; // Double-check on UI thread
                                _videoPlaying = false;
                                CloseAll();
                            });
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Error(ex, "VideoService: Failed to dispatch CloseAll from URL EndReached");
                            try
                            {
                                if (!_isCleaningUp)
                                    Application.Current?.Dispatcher?.Invoke(() => ForceCleanup());
                            }
                            catch { /* Last resort failed */ }
                        }
                    });
                };

                mediaPlayer.EncounteredError += (s, e) =>
                {
                    App.Logger?.Error("VideoService: LibVLC URL playback error");
                    Task.Run(() =>
                    {
                        try
                        {
                            var dispatcher = Application.Current?.Dispatcher;
                            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                            dispatcher.BeginInvoke(() =>
                            {
                                _videoPlaying = false;
                                CloseAll();
                            });
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Error(ex, "VideoService: Failed to dispatch CloseAll from URL EncounteredError");
                        }
                    });
                };
            }

            win.Content = videoView;

            // URL playback (PlayUrl) is never strict — just routes through the
            // shared non-strict handler so ESC dismiss + PanicKey both behave
            // identically to the file-based mandatory video windows.
            SetupStrictHandlers(win, strict: false);

            // Pin to the monitor's true physical bounds (PerMonitorV2 DPI fix — see
            // ForceFullScreenBounds; matches the file-based mandatory-video path).
            ForceFullScreenBounds(win, screen);
            win.Show();
            if (withAudio) win.Activate();
            DisableChildWindowInput(win);

            videoView.MediaPlayer = mediaPlayer;

            // Create media from URL — disposed after Play() (LibVLC ref-counts internally)
            using var media = new Media(_libVLC!, url, FromType.FromLocation);
            // Secondaries skip audio decoding entirely — prevents a parallel WASAPI session
            // from opening on the same MMDevice and racing the primary's mixer state. Also
            // skip it when audio is deactivated (effective volume 0), else the async Play()
            // lets the video blip at 100% before the volume set below lands (see file path).
            if (!withAudio || GetEffectiveVolume() <= 0) media.AddOption(":no-audio");

            // Subscribed BEFORE Play(): a fast start raises Playing inside Play() itself, and this
            // handler owns everything that needs a live aout - the volume re-apply (the set after
            // Play() no-ops while no audio output exists) and the output-device routing.
            if (withAudio)
            {
                int audioRouted = 0; // Playing can fire again after a seek/restart - route once
                mediaPlayer.Playing += (s, e) =>
                {
                    try { mediaPlayer.Mute = false; mediaPlayer.Volume = GetEffectiveVolume(); }
                    catch (Exception ex) { App.Logger?.Debug(ex, "VideoService: URL volume apply on Playing failed"); }

                    if (System.Threading.Interlocked.Exchange(ref audioRouted, 1) == 0)
                        Task.Run(() => RouteAndProbeAudio(mediaPlayer, url.Length > 60 ? url.Substring(0, 60) + "..." : url));
                };
            }

            mediaPlayer.Play(media);

            if (withAudio)
            {
                mediaPlayer.Mute = false;
                mediaPlayer.Volume = GetEffectiveVolume();
            }

            return win;
        }

        /// <summary>Short re-check used when a tick was skipped because something was in the way,
        /// rather than because the interval elapsed normally.</summary>
        private const double SkipRetrySeconds = 30;

        /// <param name="retrySeconds">When set, wait this long instead of a full fresh interval.
        /// A skipped tick MUST use this: recomputing the full 3600/VideosPerHour gap threw away
        /// all the time already elapsed, so with browser videos in play the mandatory scheduler
        /// was pushed back indefinitely and never fired at all.</param>
        private void ScheduleNext(double? retrySeconds = null)
        {
            if (!_isRunning || !App.Settings.Current.MandatoryVideosEnabled) return;

            double secs;
            if (retrySeconds.HasValue)
            {
                secs = retrySeconds.Value;
            }
            else
            {
                var perHour = Math.Max(1, App.Settings.Current.VideosPerHour);
                secs = 3600.0 / perHour * (0.8 + _random.NextDouble() * 0.4);
                secs = Math.Max(60, secs);
            }

            _scheduler?.Stop();
            _scheduler = new DispatcherTimer { Interval = TimeSpan.FromSeconds(secs) };
            _scheduler.Tick += (s, e) =>
            {
                _scheduler?.Stop();
                try
                {
                    if (_isRunning && !_videoPlaying && !_triggerInProgress)
                    {
                        if (App.BrowserMedia?.ShouldDeferInterruptions == true ||
                            App.InteractionQueue?.CurrentInteraction == InteractionQueueService.InteractionType.WebVideo)
                        {
                            // A browser video is playing (user- or app-started) — don't stack a
                            // mandatory video (and its audio) on top (BUG-XRFQH4AHDN). Nothing
                            // else re-arms us in this branch (there's no mandatory-video Cleanup
                            // to follow), so retry SHORTLY — a full fresh interval here meant a
                            // steady diet of web videos starved mandatory ones completely.
                            // Reschedule rather than queue: an ambient video shouldn't pile up
                            // behind a possibly-long web video.
                            App.Logger?.Information("VideoService: scheduler tick skipped — browser media active; retrying in {Retry}s", SkipRetrySeconds);
                            ScheduleNext(SkipRetrySeconds);
                        }
                        else if (_isCleaningUp)
                        {
                            // Previous video's teardown still pumping — same short-retry logic.
                            App.Logger?.Information("VideoService: scheduler tick skipped — cleanup in progress; retrying in {Retry}s", SkipRetrySeconds);
                            ScheduleNext(SkipRetrySeconds);
                        }
                        else
                        {
                            TriggerVideo();
                        }
                    }
                    // Cleanup() will call ScheduleNext() when video ends
                }
                catch (Exception ex)
                {
                    // A throw here would otherwise bubble to DispatcherUnhandledException,
                    // which logs and marks it handled — silently killing all further videos
                    // while the session timer keeps counting down (#388). Re-arm the
                    // scheduler so a single bad TriggerVideo (e.g. LibVLC/codec failure)
                    // doesn't take the rest of the session's mandatory videos with it.
                    App.Logger?.Error(ex, "VideoService: scheduler tick failed — re-arming scheduler");
                    _triggerInProgress = false;
                    ScheduleNext();
                }
            };
            _scheduler.Start();
        }

        private void PlayVideo(string path, bool strict, bool isVoutRetry = false)
        {
            App.Logger?.Information("VideoService: PlayVideo called for {File}", Path.GetFileName(path));

            // #616/#617/#621/#622/#623 instrumentation. The five v6.5.0 "fullscreen black/white +
            // frozen app + dead panic key" reports carry an EMPTY crash.log (a hang, not a throw)
            // and an app-log tail that only shows the post-reboot startup. Everything this path
            // does now also lands in the flush-on-write video-diag trace, which the next report
            // will still contain. Record the configuration up front: the render path (blurred
            // background vs VideoView) and the decoder (SW vs HW) are the two switches that decide
            // WHICH of the known white/black-screen failure modes is even reachable.
            VideoDiag.Log("VIDEO", string.Format(
                "BEGIN {0} strict={1} voutRetry={2} blurBg={3} hwDecode={4} dualMon={5}",
                Path.GetFileName(path), strict, isVoutRetry,
                App.Settings?.Current?.VideoBlurredBackgroundEnabled == true,
                App.Settings?.Current?.VideoForceHardwareDecoding == true,
                App.Settings?.Current?.DualMonitorEnabled == true));

            _triggerInProgress = false;

            if (_videoPlaying)
            {
                App.Logger?.Warning("VideoService: PlayVideo skipped - video already playing");
                VideoDiag.Log("VIDEO", "SKIP - a video is already playing");
                return;
            }

            // Fresh video ⇒ fresh vout-retry budget. The retry pass keeps the budget spent so a
            // machine where even the fresh LibVLC instance can't present skips on to ScheduleNext
            // instead of looping retire/retry forever.
            if (!isVoutRetry) _voutRetryUsed = false;

            _videoPlaying = true;
            _playbackStarted = false; // nothing is on screen yet — we are entering pre-roll
            _strictActive = strict;
            // A video is on screen again, so whatever gap we were covering is over. Cleared HERE,
            // after the two flags above are already set, so a reader on another thread never sees a
            // moment where neither the gap flag nor (_videoPlaying && _strictActive) is true.
            CancelPendingRetry();
            _retryPath = path;
            _startTime = DateTime.Now;
            _hits = _total = 0;
            _spawnTimes.Clear();

            // Everything from here to the delay timer's tick used to be a diag blind spot: the trace
            // jumped straight from "BEGIN" to "libvlc-init begin" ~2.6s later, so a report whose
            // freeze began inside this prologue told us nothing about WHICH step ate the dispatcher.
            // One breadcrumb per step, each carrying the elapsed prologue time (#750-#753).
            var prologueSw = System.Diagnostics.Stopwatch.StartNew();

            // Arm the off-thread wedge watchdog NOW, before any window is created — the freeze often
            // strikes mid window-creation of this video, before the safety timers get a chance to arm.
            // During pre-roll it can only OBSERVE (a stall diag line); the destructive rescue is
            // gated on _playbackStarted and re-armed from StartVideoPlayback. See WedgeWatchdogTick.
            StartWedgeWatchdog();
            VideoDiag.Log("VIDEO", $"prologue: wedge watchdog armed +{prologueSw.ElapsedMilliseconds}ms");

            // Update Discord presence
            App.DiscordRpc?.SetVideoActivity();
            VideoDiag.Log("VIDEO", $"prologue: SetVideoActivity +{prologueSw.ElapsedMilliseconds}ms");

            // Fire pre-announcement event 1.3s before video starts
            VideoAboutToStart?.Invoke(this, EventArgs.Empty);
            VideoDiag.Log("VIDEO", $"prologue: VideoAboutToStart handlers +{prologueSw.ElapsedMilliseconds}ms");

            // Stop flashes during video
            App.Flash?.Stop();
            VideoDiag.Log("VIDEO", $"prologue: Flash.Stop +{prologueSw.ElapsedMilliseconds}ms");

            // Duck other apps. Record that we took a duck ref so CloseAll releases exactly one
            // matching Unduck on teardown — otherwise a retry/troll "watch again" loop ducks again
            // each pass and only Cleanup unducks once, leaking the ref count (#526).
            if (App.Settings.Current.AudioDuckingEnabled)
            {
                App.Audio?.Duck(App.Settings.Current.DuckingLevel);
                _didDuck = true;
                // Duck() only queues now — the sweep's own "DUCK: duck applied after Nms" line
                // (AudioService) is what says how long the WASAPI walk actually took.
                VideoDiag.Log("VIDEO", $"prologue: Duck requested +{prologueSw.ElapsedMilliseconds}ms");
            }

            // Delay video start by 1.3 seconds to allow avatar to announce
            App.Logger?.Debug("VideoService: Starting 1.3s delay before playback");
            var delayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.3) };
            delayTimer.Tick += (s, e) =>
            {
                delayTimer.Stop();
                VideoDiag.Log("VIDEO", $"prologue: delay timer ticked +{prologueSw.ElapsedMilliseconds}ms - starting playback");
                App.Logger?.Debug("VideoService: Delay complete, calling StartVideoPlayback");
                StartVideoPlayback(path, strict);
            };
            delayTimer.Start();
            VideoDiag.Log("VIDEO", $"prologue: 1.3s delay timer scheduled +{prologueSw.ElapsedMilliseconds}ms");
        }

        private void StartVideoPlayback(string path, bool strict)
        {
            App.Logger?.Information("VideoService: StartVideoPlayback called for {File}", Path.GetFileName(path));

            // Safety check: ensure app is still running
            if (Application.Current == null)
            {
                App.Logger?.Warning("VideoService: Application.Current is null, aborting playback");
                return;
            }

            try
            {
                // Start a fallback safety timer immediately - this ensures we ALWAYS have a timeout
                // even if LibVLC's LengthChanged never fires. Will be replaced by accurate timer
                // once video duration is known.
                StartFallbackSafetyTimer();

                // Enforce the user's max video-length cap (#584). This is independent of the safety
                // timers (which key off the file's own duration, not the user's cap) and of the
                // selection filter (best-effort, cold-cache bypass), so it holds even for a long clip
                // that slipped past selection. Wall-clock from playback start; a few seconds of LibVLC
                // startup latency before frames roll is negligible against a minutes-long cap.
                StartMaxLengthCapTimer();

                // Ensure LibVLC is initialized (deferred from startup for faster launch).
                // #616-#623 NOTE: this call runs ON THE UI THREAD and takes _libVLCLock, which a
                // background rebuild (RetireSharedLibVLC's Task.Run) can hold for the whole duration
                // of a native `new LibVLC(...)`. Bracket it in the trace: if the last diag line of a
                // frozen session is "libvlc-init begin", the dispatcher died waiting on that lock /
                // on native LibVLC construction, and no amount of watchdog timers can help because
                // every one of them is a DispatcherTimer.
                VideoDiag.Log("VIDEO", "libvlc-init begin (UI thread, takes _libVLCLock)");
                var libvlcInitSw = System.Diagnostics.Stopwatch.StartNew();
                EnsureLibVLCInitialized();
                VideoDiag.Log("VIDEO", $"libvlc-init end after {libvlcInitSw.ElapsedMilliseconds}ms, instance={( _libVLC != null )}");
                App.Logger?.Information("VideoService: LibVLC initialized = {Initialized}, LibVLC instance = {HasInstance}",
                    _libVLCInitialized, _libVLC != null);

                DispatcherHelper.RunOnUISync(() =>
                {
                    try
                    {
                        var allScreens = App.GetAllScreensCached().ToList();
                        if (allScreens.Count == 0)
                        {
                            App.Logger?.Error("VideoService: No screens available - cannot play video");
                            OnEnded();
                            return;
                        }
                        var primary = allScreens.FirstOrDefault(s => s.Primary) ?? allScreens[0];
                        var secondaries = allScreens.Where(s => !s.Primary).ToList();

                        App.Logger?.Information("VideoService: Detected {Total} screens - Primary: {Primary}, Secondary: {SecCount} ({SecNames})",
                            allScreens.Count, primary.DeviceName, secondaries.Count,
                            string.Join(", ", secondaries.Select(s => s.DeviceName)));

                        // Use LibVLC if available (codec-independent), otherwise fall back to MediaElement
                        if (_libVLC != null)
                        {
                            // Create primary screen with LibVLC VideoView (with audio).
                            // Window creation is the single longest UI-thread block in the whole show
                            // path (layered fullscreen window + HwndHost airspace surface + native
                            // Play()), and the historic freezes strike mid-creation — before any of
                            // the DispatcherTimer-based guards exist. Bracket each window (#616-#623).
                            VideoDiag.Log("VIDEO", $"window-create begin (primary, {primary.DeviceName})");
                            var primaryWin = CreateLibVLCVideoWindow(path, primary, strict, withAudio: true);
                            VideoDiag.Log("VIDEO", "window-create end (primary)");
                            _windows.Add(primaryWin);

                            // Create secondary screens with their own LibVLC players (muted),
                            // unless capped on a high monitor count to avoid decoder lag (#389)
                            if (ShouldFillSecondaryMonitors(allScreens.Count))
                            {
                                foreach (var scr in secondaries)
                                {
                                    VideoDiag.Log("VIDEO", $"window-create begin (secondary, {scr.DeviceName})");
                                    var win = CreateLibVLCVideoWindow(path, scr, strict, withAudio: false);
                                    VideoDiag.Log("VIDEO", "window-create end (secondary)");
                                    _windows.Add(win);
                                }
                            }
                            else if (App.Settings.Current.DualMonitorEnabled && secondaries.Count > 0)
                            {
                                App.Logger?.Information("Skipping {N} secondary video decoder(s) on {Total} monitors to avoid lag (#389); enable 'Fill all monitors with video' to override.",
                                    secondaries.Count, allScreens.Count);
                            }
                        }
                        else
                        {
                            // Fallback to MediaElement (requires Windows codecs)
                            var (primaryWin, primaryMedia) = CreateMediaElementVideoWindow(path, primary, strict);
                            _windows.Add(primaryWin);

                            if (App.Settings.Current.DualMonitorEnabled)
                            {
                                foreach (var scr in secondaries)
                                {
                                    var win = CreateMirrorVideoWindow(primaryMedia, scr, strict);
                                    _windows.Add(win);
                                }
                            }

                            primaryMedia.Play();
                        }

                        App.Logger?.Information("VideoService: Created {Count} video windows (DualMonitor={Enabled})",
                            _windows.Count, App.Settings.Current.DualMonitorEnabled);

                        // A video that fires mid-chaos-run must never grab z-order back from the
                        // run's bubbles/HUD. Mark every video window WS_EX_NOACTIVATE so Windows
                        // can't activate it on a click or hand it focus when a subliminal/target/
                        // bubble above it disappears — that focus-driven raise was the flicker.
                        // The chaos VideoStarted handler raises the game layer above it once; with
                        // no-activate it stays put for the whole video. See MakeNonActivating.
                        if (App.Chaos?.IsRunning == true)
                            foreach (var w in _windows) MakeNonActivating(w);

                        // Regardless of chaos, clicking the mandatory video (e.g. missing a bubble
                        // and hitting the video behind it) must NOT re-raise it above whatever is
                        // layered on top. Answer WM_MOUSEACTIVATE with MA_NOACTIVATE so the click
                        // never triggers a z-order raise. Focus-preserving, so ESC/panic still work.
                        foreach (var w in _windows) PreventClickRaise(w);

                        // Ambient bubble game: pause + clear so it doesn't fight the video for
                        // clicks / z-order (no-op during a chaos run, which isn't "running").
                        // A chaos run keeps its bubbles + HUD alive and lifts them back above the
                        // video itself — see ChaosModeService's VideoStarted handler.
                        App.Bubbles?.PauseAndClear();

                        if (App.Settings.Current.AttentionChecksEnabled)
                            SetupAttention();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "VideoService: Error during video window creation");
                        VideoDiag.Log("VIDEO", "window-create THREW: " + ex.Message);
                        Cleanup();
                    }
                });

                // Playback is REAL from here: a window (and, on the LibVLC path, a registered media
                // player) exists. Only now may the wedge watchdog do its destructive retire/rescue.
                // Re-arm it so the pre-roll's stall — which is exactly what the reporters hit while
                // Duck() enumerated their audio endpoints — doesn't count toward the wedge threshold
                // and immediately trip a rescue against a perfectly healthy playback (#750-#753).
                if (_windows.Count > 0)
                {
                    _playbackStarted = true;
                    StartWedgeWatchdog();
                }

                VideoDiag.Log("VIDEO", $"show complete - {_windows.Count} window(s) on screen");
                VideoStarted?.Invoke(this, EventArgs.Empty);
                _ = App.Haptics?.StartVideoBackgroundVibeAsync();
                App.Logger?.Information("Playing: {File}", Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "VideoService: Critical error in StartVideoPlayback");
                _videoPlaying = false;
                Cleanup();
            }
        }

        /// <summary>
        /// Creates a video window using LibVLC (codec-independent).
        /// Works on Windows N/KN editions without additional codecs.
        /// </summary>
        /// <param name="path">Video file path</param>
        /// <param name="screen">Target screen</param>
        /// <param name="strict">Whether strict mode is enabled</param>
        /// <param name="withAudio">Whether to play audio (primary monitor) or mute (secondary monitors)</param>
        private Window CreateLibVLCVideoWindow(string path, Screen screen, bool strict, bool withAudio)
        {
            Window? win = null;
            LibVLCSharp.Shared.MediaPlayer? mediaPlayer = null;

            try
            {
                var dpiScale = BubbleCountWindow.GetDpiForScreen(screen);
                win = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = withAudio, // Only activate primary
                    Topmost = true,
                    Background = Brushes.Black,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    // Size to the full screen bounds up-front. Starting at 400x300 and
                    // maximizing afterward briefly exposed an unpainted (white) region for
                    // a frame before the black background caught up — and on a secondary
                    // monitor that white frame could linger (#368). This mirrors the
                    // flash-free CreateLibVLCUrlWindow path.
                    Left = screen.Bounds.X / dpiScale,
                    Top = screen.Bounds.Y / dpiScale,
                    Width = screen.Bounds.Width / dpiScale,
                    Height = screen.Bounds.Height / dpiScale
                };

                // Render path for THIS screen: the blurred-background composite (WPF Image pair fed
                // by LibVLC memory callbacks — no HwndHost, so it composites in WPF) vs the classic
                // VideoView. When the setting is on we ALWAYS use the composite: the decoder reports
                // the true frame size through the video-format callback (reliable on the very first
                // play, unlike a pre-parse of the container), and the blurred fill auto-hides for a
                // clip that already matches the screen — so a landscape video pays no blur cost.
                bool useBlur = App.Settings?.Current?.VideoBlurredBackgroundEnabled == true;

                // Create the video surface: either the blurred-background composite or a VideoView.
                VideoView? videoView = null;
                BlurVmemSurface? blurSurface = null;
                if (useBlur)
                {
                    double screenAspect = (double)screen.Bounds.Width / Math.Max(1, screen.Bounds.Height);
                    blurSurface = new BlurVmemSurface(screenAspect);
                    _blurSurfaces.Add(blurSurface);
                    App.Logger?.Information("VideoService: blurred-background path armed for {File} on {Screen} (screenAspect={AR:0.000})",
                        Path.GetFileName(path), screen.DeviceName, screenAspect);
                }
                else
                {
                    videoView = new VideoView
                    {
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        Background = Brushes.Black
                    };
                }

                // Create media player for this video.
                mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC!);
                // Mandatory-video windows default to SOFTWARE decoding. On Windows 11 (build 26200)
                // and some Win10 machines the LibVLC hardware (DXVA/D3D11) path intermittently fails
                // to present a frame — the window stays white and MediaEnded never fires, wedging
                // cleanup. It hit the *primary* (audio + HW) player on both single-monitor (#537) and
                // dual-monitor (#533, #540) setups, so it is the hardware decoder itself failing, not
                // GPU contention between the two mirror decoders. These are short attention-check
                // clips, so software decode is a negligible cost and eliminates the whole white-screen
                // class. Users who want GPU decode back can flip VideoForceHardwareDecoding on (opt-in).
                mediaPlayer.EnableHardwareDecoding = App.Settings?.Current?.VideoForceHardwareDecoding ?? false;
                lock (_mediaPlayersLock)
                {
                    _mediaPlayers.Add(mediaPlayer);
                }
                // NOTE: the user's chosen output device is applied from the Playing handler below,
                // NOT here. LibVLC has no audio output until playback starts, so a call at
                // construction time enumerated an empty device list, matched nothing and silently
                // no-op'd — every mandatory video went to the Windows default endpoint while the
                // NAudio paths honoured the setting (#707/#708). See RouteAndProbeAudio.

            // Only the primary player handles events (to avoid duplicate triggers)
            if (withAudio)
            {
                _primaryMediaPlayer = mediaPlayer;
                _primaryVideoWindow = win;

                mediaPlayer.TimeChanged += (s, e) =>
                {
                    _lastWatchPositionMs = e.Time; // track watched position for quest crediting (#447)
                    try { PrimaryPlaybackTimeMsChanged?.Invoke(e.Time); }
                    catch (Exception ex) { App.Logger?.Debug("PrimaryPlaybackTimeMsChanged handler error: {Error}", ex.Message); }
                };

                mediaPlayer.LengthChanged += (s, e) =>
                {
                    _duration = e.Length / 1000.0; // Convert ms to seconds
                    App.Logger?.Information("VideoService: LibVLC LengthChanged fired, duration={Duration}s", _duration);

                    // Chaos random segment: jump to the shared random start (every player uses the
                    // same fraction so mirrors stay in sync). Only when the file outruns the segment.
                    // The seek is DEFERRED until the player is actually rolling: LengthChanged fires
                    // before the video output finishes initializing, and seeking mid-creation blanks
                    // the primary view (observed 2026-06-10).
                    try
                    {
                        long segMs = (long)(_segmentSec * 1000);
                        if (SegmentArmed && e.Length > segMs)
                        {
                            long startMs = (long)((e.Length - segMs) * _segmentFraction);
                            if (startMs > 500)
                            {
                                var dispatcher = Application.Current?.Dispatcher;
                                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                                {
                                    dispatcher.BeginInvoke(() =>
                                    {
                                        var seek = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                                        seek.Tick += (_, _) =>
                                        {
                                            seek.Stop();
                                            try
                                            {
                                                if (_videoPlaying && mediaPlayer.IsPlaying && mediaPlayer.Time < startMs - 1000)
                                                {
                                                    mediaPlayer.Time = startMs;
                                                    App.Logger?.Information("VideoService: random segment — seeking to {Start}s of {Len}s",
                                                        startMs / 1000, e.Length / 1000);
                                                }
                                            }
                                            catch (Exception ex2) { App.Logger?.Debug("VideoService random-segment seek: {E}", ex2.Message); }
                                        };
                                        seek.Start();
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex) { App.Logger?.Debug("VideoService random-segment seek: {E}", ex.Message); }
                    try
                    {
                        var dispatcher = Application.Current?.Dispatcher;
                        if (dispatcher != null && !dispatcher.HasShutdownStarted)
                        {
                            dispatcher.BeginInvoke(() => StartSafetyTimer(_duration));
                        }
                        else
                        {
                            App.Logger?.Warning("VideoService: Dispatcher unavailable in LengthChanged");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "VideoService: Failed to start safety timer from LengthChanged");
                    }
                };

                mediaPlayer.EndReached += (s, e) =>
                {
                    // CRITICAL: Must detach from LibVLC thread IMMEDIATELY to prevent deadlocks
                    // LibVLC waits for event handlers to complete before returning, and if we
                    // try to Stop/Dispose the player while waiting, it deadlocks.
                    // Using Task.Run ensures we return from the event handler immediately.
                    App.Logger?.Information("VideoService: LibVLC EndReached fired, _isCleaningUp={Cleaning}", _isCleaningUp);
                    Task.Run(() =>
                    {
                        try
                        {
                            // Skip if cleanup already in progress (e.g., from panic key)
                            if (_isCleaningUp)
                            {
                                App.Logger?.Debug("VideoService: EndReached skipped - cleanup already in progress");
                                return;
                            }

                            var dispatcher = Application.Current?.Dispatcher;
                            if (dispatcher == null)
                            {
                                App.Logger?.Warning("VideoService: Dispatcher is null in EndReached, cannot cleanup properly");
                                return;
                            }
                            if (dispatcher.HasShutdownStarted)
                            {
                                App.Logger?.Warning("VideoService: Dispatcher shutting down in EndReached");
                                return;
                            }
                            dispatcher.BeginInvoke(OnEnded);
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Error(ex, "VideoService: Failed to dispatch OnEnded from EndReached");
                            // Try direct cleanup as last resort - windows may stay open otherwise
                            try
                            {
                                if (!_isCleaningUp)
                                    Application.Current?.Dispatcher?.Invoke(() => ForceCleanup());
                            }
                            catch (Exception ex2)
                            {
                                App.Logger?.Error(ex2, "VideoService: Even ForceCleanup failed in EndReached");
                            }
                        }
                    });
                };

                mediaPlayer.EncounteredError += (s, e) =>
                {
                    App.Logger?.Error("VideoService: LibVLC playback error");
                    Task.Run(() =>
                    {
                        try
                        {
                            var dispatcher = Application.Current?.Dispatcher;
                            if (dispatcher == null || dispatcher.HasShutdownStarted)
                            {
                                App.Logger?.Warning("VideoService: Dispatcher unavailable in EncounteredError");
                                return;
                            }
                            dispatcher.BeginInvoke(OnEnded);
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Error(ex, "VideoService: Failed to dispatch OnEnded from EncounteredError");
                            try
                            {
                                Application.Current?.Dispatcher?.Invoke(() => ForceCleanup());
                            }
                            catch { /* Last resort failed */ }
                        }
                    });
                };
            }

            var grid = new Grid { Background = Brushes.Black };
            // Blurred path: the composite (blurred fill + sharp centred video) is pure WPF content;
            // VideoView path: the airspace HwndHost surface. Either way the click overlay sits above.
            grid.Children.Add(useBlur ? blurSurface!.Root : videoView!);

            // Add invisible click overlay - LibVLC uses Win32 child window that bypasses WPF events
            // This overlay catches all clicks before they reach the video surface
            var clickOverlay = new System.Windows.Shapes.Rectangle
            {
                Fill = Brushes.Transparent,
                IsHitTestVisible = true
            };
            clickOverlay.MouseDown += (s, e) =>
            {
                e.Handled = true;
                BringTargetsToFront();
            };
            grid.Children.Add(clickOverlay);

            win.Content = grid;

            SetupStrictHandlers(win, strict);

            // Also handle at window level for any clicks that get through
            win.PreviewMouseDown += (s, e) =>
            {
                // Don't let the video window activate - keeps targets on top
                e.Handled = true;
                BringTargetsToFront();
            };

            // Window is already sized to the full screen bounds, so we don't start small
            // and Maximize — that sequence caused the white-frame flash (#368). A borderless
            // Topmost window at full bounds covers the taskbar just like the maximized one did.
            // Pin to the monitor's true physical bounds so a secondary monitor with different DPI
            // scaling gets the full screen instead of a part-width window (see ForceFullScreenBounds).
            ForceFullScreenBounds(win, screen);
            win.Show();
            if (withAudio) win.Activate();
            DisableChildWindowInput(win);

            // Attach media player to the surface and start playback. The blurred path wires LibVLC
            // memory callbacks (SetVideoFormat + SetVideoCallbacks) instead of a VideoView; it must
            // happen BEFORE Play() below, same as attaching a VideoView.
            if (useBlur)
                blurSurface!.Attach(mediaPlayer);
            else
                videoView!.MediaPlayer = mediaPlayer;

            // Create media - use file path directly for better compatibility
            // Media is disposed after Play() — LibVLC internally ref-counts, so this is safe
            // (DualMonitorVideoService already uses this pattern with 'using var media')
            using var media = new Media(_libVLC!, path, FromType.FromPath);
            // Secondaries skip audio decoding entirely. Setting Mute=true after Play() opened
            // a second WASAPI session on the same MMDevice; Windows collapsed both into one
            // per-app mixer slider and the result was doubled/desynced or zero-volume audio.
            // Also skip it when audio is deactivated (effective volume 0): Play() is async, so
            // the Volume=0 set below no-ops until the aout exists and the video would start at
            // 100% for a beat before the Playing handler cuts it — the audible blip a
            // "deactivated audio" user hears. :no-audio never decodes audio, so nothing blips.
            // Same reasoning covers a muted player (dive master-mute or a zeroed slider): it has to
            // start SILENT, and killing audio at the media level is the only way to beat the aout.
            // Trade-off: un-muting mid-video won't restore sound - fine for the mandatory dive
            // video, and any later playback opens a fresh Media that re-evaluates this.
            if (!withAudio || GetEffectiveVolume() <= 0)
            {
                media.AddOption(":no-audio");
            }

            // Force software video decode at the media level (belt-and-suspenders with
            // EnableHardwareDecoding=false above) to dodge the LibVLC white-screen/wedge on the
            // hardware path (#533/#537/#540). Skipped only when the user has explicitly opted into
            // GPU decode, in which case we honour their choice and leave the HW path enabled.
            if (!(App.Settings?.Current?.VideoForceHardwareDecoding ?? false))
            {
                media.AddOption(":avcodec-hw=none");
            }

            // Everything that needs a live aout hangs off Playing, and the handler is subscribed
            // BEFORE Play(): a fast start can raise Playing inside the Play() call itself, and a
            // handler attached afterwards misses the event entirely - stranding both the volume
            // re-apply and the output-device routing below.
            if (withAudio)
            {
                int audioRouted = 0; // Playing can fire again after a seek/restart - route once
                mediaPlayer.Playing += (s, e) =>
                {
                    // Play() is async: LibVLC hasn't created the audio output yet when it returns,
                    // so the Volume set after Play() silently no-ops (libvlc_audio_set_volume fails
                    // with no aout) and the video starts at 100% regardless of the slider. Only
                    // this re-apply lands the Video Volume slider (matches
                    // DualMonitorVideoService/MiniPlayerWindow).
                    try { mediaPlayer!.Mute = false; mediaPlayer.Volume = GetEffectiveVolume(); }
                    catch (Exception ex) { App.Logger?.Debug(ex, "VideoService: volume apply on Playing failed"); }

                    // Anything heavier than a property set goes off the LibVLC event thread.
                    if (System.Threading.Interlocked.Exchange(ref audioRouted, 1) == 0)
                        Task.Run(() => RouteAndProbeAudio(mediaPlayer!, Path.GetFileName(path)));
                };
            }

            // Play the media
            mediaPlayer.Play(media);

            // Configure audio AFTER Play() - LibVLC sometimes ignores settings before playback.
            // Don't call SetAudioTrack here: Play() is async and tracks aren't enumerated yet,
            // so requesting a hardcoded track ID leaves the player stuck at -1 (audio disabled).
            // LibVLC auto-selects the first audio track once the media is parsed.
            if (withAudio)
            {
                mediaPlayer.Mute = false;
                mediaPlayer.Volume = GetEffectiveVolume();
                // REQUESTED values only. No aout exists this early, so this line says nothing about
                // audibility - it read "Volume=99, Mute=false" on both #707 and #708, which were
                // dead silent. RouteAndProbeAudio logs the RESOLVED routing once the aout is live;
                // that is the line to read when a report says "video plays but has no sound".
                App.Logger?.Information("LibVLC audio requested: Volume={Vol}, Mute={Mute} (pre-aout - see the [VIDEO] aout line for what actually happened)",
                    mediaPlayer.Volume, mediaPlayer.Mute);

                // Arm the vout watchdog on the primary player: if no video output exists within the
                // grace window the screen is white regardless of decode state (#557-#560/#574) —
                // self-heal by retiring the shared instance and retrying once (see VoutWatchdogFire).
                // The blurred path renders through memory callbacks (no vout HWND, so none of the
                // DXVA present failures the vout watchdog targets), so it uses a simpler frame-arrival
                // watchdog instead: no frame within the grace window ⇒ skip to the next video.
                if (useBlur)
                    StartBlurFrameWatchdog(blurSurface!);
                else
                    StartVoutWatchdog(mediaPlayer, path, strict);
            }

                App.Logger?.Debug("LibVLC video window on: {Screen} (audio: {Audio}, vol: {Vol}, mute: {Mute})",
                    screen.DeviceName, withAudio, mediaPlayer.Volume, mediaPlayer.Mute);
                return win;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "VideoService: Failed to create LibVLC video window on {Screen}", screen.DeviceName);

                // Clean up on failure
                try
                {
                    if (mediaPlayer != null)
                    {
                        lock (_mediaPlayersLock)
                        {
                            _mediaPlayers.Remove(mediaPlayer);
                        }
                        mediaPlayer.Dispose();
                    }
                    win?.Close();
                }
                catch { /* Ignore cleanup errors */ }

                // Create a black placeholder window so we don't crash
                var fallbackDpi = BubbleCountWindow.GetDpiForScreen(screen);
                var fallbackWin = new Window
                {
                    WindowStyle = WindowStyle.None,
                    Background = Brushes.Black,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = screen.Bounds.X / fallbackDpi,
                    Top = screen.Bounds.Y / fallbackDpi,
                    Width = screen.Bounds.Width / fallbackDpi,
                    Height = screen.Bounds.Height / fallbackDpi
                };
                fallbackWin.Show(); // already full-bounds — no Maximize (see #368 above)

                // Auto-close after 3 seconds. Use ForceCleanup, NOT OnEnded: OnEnded
                // early-returns when _videoPlaying is already false (or cleanup is in
                // progress), which would leave this black/white placeholder orphaned on a
                // secondary monitor (#368). ForceCleanup unconditionally closes every video
                // window; ScheduleNext re-arms so a single failed video doesn't end the
                // session's mandatory videos.
                var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                closeTimer.Tick += (s, e) => { closeTimer.Stop(); ForceCleanup(); ScheduleNext(); };
                closeTimer.Start();

                return fallbackWin;
            }
        }

        /// <summary>
        /// Memory-render buffer dimensions for a video of the given display size: the video aspect
        /// (so LibVLC bakes in NO bars), with the longer side capped so decode + per-frame copy +
        /// blur stay cheap, and both sides even.
        /// </summary>
        private static (uint W, uint H) ComputeBufferDims((int W, int H) dims)
        {
            int w = Math.Max(2, dims.W);
            int h = Math.Max(2, dims.H);
            const int cap = 1080; // long side; a portrait clip fills a 1080-tall screen at full crispness
            int longSide = Math.Max(w, h);
            double scale = longSide > cap ? (double)cap / longSide : 1.0;
            uint bw = (uint)Math.Max(16, ((int)Math.Round(w * scale) + 1) & ~1);
            uint bh = (uint)Math.Max(16, ((int)Math.Round(h * scale) + 1) & ~1);
            return (bw, bh);
        }

        /// <summary>
        /// Byte size of one BGRA frame of the given geometry, or 0 if the geometry is absurd
        /// (overflow / zero side). A bogus format callback must make the blit SKIP the frame, never
        /// allocate or copy on a garbage length. Pure — unit-tested.
        /// </summary>
        internal static int StagingBytesFor(uint w, uint h)
        {
            if (w == 0 || h == 0) return 0;
            if (w > 65535 || h > 65535) return 0; // no real decoded frame is this big; keeps the product in range
            long bytes = (long)w * h * 4;
            if (bytes > int.MaxValue) return 0;
            return (int)bytes;
        }

        /// <summary>
        /// Geometry of the small snapshot that feeds the blurred fill: the frame divided down, floored
        /// at 8px a side so a tiny clip can't degenerate to a 0-wide bitmap. Pure — unit-tested.
        /// </summary>
        internal static (int W, int H) ComputeSnapshotDims((uint W, uint H) buffer, int divisor)
        {
            int d = Math.Max(1, divisor);
            int sw = Math.Max(8, (int)buffer.W / d);
            int sh = Math.Max(8, (int)buffer.H / d);
            return (sw, sh);
        }

        /// <summary>
        /// Whether this frame index should refresh the blurred fill's snapshot. The fill sits behind a
        /// radius-48 Gaussian, so it does not need per-frame freshness. Pure — unit-tested.
        /// </summary>
        internal static bool IsSnapshotFrame(int frameIndex, int everyN)
            => everyN <= 1 || (frameIndex % everyN) == 0;

        /// <summary>
        /// Whether a bitmap rebuild posted with <paramref name="postedGeneration"/> is still the newest
        /// one (#687: three format callbacks landed within 15ms and an older rebuild ran last, pinning
        /// the bitmap at a size that no longer matched the buffer). Pure — unit-tested.
        /// </summary>
        internal static bool IsRebuildCurrent(int postedGeneration, int currentGeneration)
            => postedGeneration == currentGeneration;

        /// <summary>
        /// Liveness watchdog for the blurred-background (memory-render) path: if the surface has
        /// produced no frame within the grace window, the decode never started — skip to the next
        /// video rather than sit on a black screen. Cheaper counterpart to StartVoutWatchdog, which
        /// targets the VideoView/DXVA white-screen the memory path can't hit.
        /// </summary>
        private void StartBlurFrameWatchdog(BlurVmemSurface surface)
        {
            var gen = _teardownGeneration;
            // DIAGNOSTIC NOTE (#616/#617/#621/#622/#623): unlike StartVoutWatchdog (a threadpool
            // timer) this guard is a DispatcherTimer, so it is dead weight in exactly the scenario
            // the reports describe — a wedged UI thread. The blurred-background path is the DEFAULT
            // in 6.5.0, which means the default render path's only frame-liveness guard cannot fire
            // during a freeze. Logged, not changed: replacing it is a behaviour change, not
            // instrumentation. See the write-up.
            VideoDiag.Log("BLUR", $"frame watchdog armed (DispatcherTimer, {VoutGraceMs}ms) - cannot fire if the UI thread wedges");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(VoutGraceMs) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (!_videoPlaying || gen != _teardownGeneration) return; // torn down / superseded
                if (surface.HasRendered) { VideoDiag.Log("BLUR", "frame watchdog: frames are arriving, all good"); return; }
                App.Logger?.Warning("VideoService: blurred-background video produced no frame within {Ms}ms — skipping to next", VoutGraceMs);
                VideoDiag.Log("BLUR", $"NO FRAME within {VoutGraceMs}ms - black-screen state confirmed, skipping to next video");
                try { OnEnded(); } catch (Exception ex) { App.Logger?.Debug("StartBlurFrameWatchdog OnEnded threw: {E}", ex.Message); }
            };
            timer.Start();
        }

        /// <summary>
        /// A fullscreen "blurred background" video surface built from LibVLC memory (vmem) callbacks:
        /// a single decoded frame is displayed twice in pure WPF — stretched-to-fill + Gaussian blur
        /// behind, and aspect-fit + sharp in front — so a vertical clip on a widescreen monitor fills
        /// the pillarbox bars with an upscaled blur of itself (the TikTok / Shorts look) instead of
        /// flat black. Uses memory rendering (no VideoView/HwndHost) precisely so the two layers
        /// composite in WPF; an airspace HWND could not be blurred or stacked this way.
        ///
        /// Pattern (buffer + lock/display callbacks + CompositionTarget.Rendering blit + delayed
        /// native teardown) mirrors <see cref="InlineLoopVideo"/> / <see cref="DualMonitorVideoService"/>.
        /// </summary>
        private sealed class BlurVmemSurface : IDisposable
        {
            // Snapshot cadence + scale for the blurred fill (see RefreshBackgroundSnapshot). Every 6th
            // frame is ~5 refreshes/sec at 30fps — invisible behind a radius-48 Gaussian, and 1/8 scale
            // means the render thread uploads 1/64th of the pixels it used to.
            private const int BgSnapshotEveryNFrames = 6;
            private const int BgSnapshotDivisor = 8;

            private readonly double _screenAspect;
            private readonly object _bufferLock = new();
            private readonly System.Windows.Shapes.Rectangle _background;
            private readonly ImageBrush _bgBrush;
            private readonly Image _foreground;
            private readonly System.Windows.Shapes.Rectangle _scrim;

            // Set from the video-format callback once the decoder reports the real frame size.
            private uint _w;
            private uint _h;
            private IntPtr _frameBuffer = IntPtr.Zero;
            private WriteableBitmap? _bitmap;
            private volatile bool _bufferValid;
            private volatile bool _frameReady;
            private volatile bool _hasRendered;
            private bool _hooked;
            private bool _disposed;

            // #687: managed staging copy of the newest decoded frame. The UI thread copies the native
            // buffer into this UNDER _bufferLock and then RELEASES the lock before it goes anywhere
            // near WriteableBitmap.Lock() — see OnRendering for why holding both deadlocked three
            // threads. Grown on demand (never shrunk) so the steady state allocates nothing.
            private byte[] _staging = Array.Empty<byte>();

            // #687: the blurred fill gets its own tiny, frozen snapshot instead of pointing at the live
            // per-frame bitmap. _snapBuf is the reusable downsample scratch; the frozen BitmapSource
            // handed to the brush is swapped whole, so the render thread never blocks the UI thread on
            // the background layer at all. UI thread only.
            private byte[] _snapBuf = Array.Empty<byte>();
            private int _snapW;
            private int _snapH;
            private int _frameCounter;
            private bool _needsBlur;

            // #687: stamped by every FormatCallback (under _bufferLock, so it is paired with _w/_h).
            // A rebuild posted to the dispatcher applies only if its stamp is still the newest.
            private int _formatGeneration;

            // Delegates handed to native LibVLC — kept in fields so the GC can't collect them
            // while libvlc still holds the pointers (the callbacks fire for the whole playback).
            private LibVLCSharp.Shared.MediaPlayer.LibVLCVideoFormatCb? _formatCb;
            private LibVLCSharp.Shared.MediaPlayer.LibVLCVideoCleanupCb? _cleanupCb;
            private LibVLCSharp.Shared.MediaPlayer.LibVLCVideoLockCb? _lockCb;
            private LibVLCSharp.Shared.MediaPlayer.LibVLCVideoDisplayCb? _displayCb;

            /// <summary>The WPF element to drop into the window's grid.</summary>
            public FrameworkElement Root { get; }

            /// <summary>True once at least one frame has been blitted to the surface.</summary>
            public bool HasRendered => _hasRendered;

            public BlurVmemSurface(double screenAspect)
            {
                _screenAspect = screenAspect > 0 ? screenAspect : (16.0 / 9.0);

                // Blurred fill: same frame scaled to cover the whole screen (cropping the excess),
                // heavily blurred. Hidden until the format callback decides bars are actually needed.
                // Painted via an ImageBrush (not an Image) so the crop is anchored at the CENTRE —
                // a plain Image + UniformToFill anchors the vertical crop at the top, so the side
                // panels only ever showed the top of the frame (the character's scalp/hair).
                _bgBrush = new ImageBrush
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                _background = new System.Windows.Shapes.Rectangle
                {
                    Fill = _bgBrush,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Visibility = Visibility.Collapsed,
                    Effect = new BlurEffect
                    {
                        Radius = 48,
                        KernelType = KernelType.Gaussian,
                        RenderingBias = RenderingBias.Performance
                    }
                };

                // Subtle dark scrim so the bright blurred fill doesn't wash out the video's edges.
                _scrim = new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Visibility = Visibility.Collapsed
                };

                // Sharp centred video: same frame, aspect-fit. Margins are transparent so the
                // blurred fill shows through where the bars would otherwise be black.
                _foreground = new Image
                {
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var grid = new Grid { ClipToBounds = true, Background = Brushes.Black };
                grid.Children.Add(_background);
                grid.Children.Add(_scrim);
                grid.Children.Add(_foreground);
                Root = grid;
            }

            /// <summary>Wire LibVLC memory callbacks to this surface and start blitting. Must be
            /// called before <c>MediaPlayer.Play</c>, on the UI thread. The buffer + bitmap are
            /// created lazily inside the format callback once the decoder reports the real size.</summary>
            public void Attach(LibVLCSharp.Shared.MediaPlayer player)
            {
                _formatCb = FormatCallback;
                _cleanupCb = CleanupCallback;
                _lockCb = LockCallback;
                _displayCb = DisplayCallback;
                player.SetVideoFormatCallbacks(_formatCb, _cleanupCb);
                player.SetVideoCallbacks(_lockCb, null, _displayCb);
                Hook();
            }

            /// <summary>
            /// Called by LibVLC (native thread) with the decoder's real frame size. We pick a
            /// buffer geometry matching the video aspect (so no bars are baked in), allocate it,
            /// and marshal to the UI thread to (re)build the WriteableBitmap and decide whether the
            /// blurred fill is needed for this screen.
            /// </summary>
            private uint FormatCallback(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
                                        ref uint pitches, ref uint lines)
            {
                uint vw = width, vh = height;
                var (bw, bh) = ComputeBufferDims(((int)vw, (int)vh));

                // Ask LibVLC for straight BGRA ("RV32"): 4 bytes/pixel, single plane.
                WriteChroma(chroma, "RV32");
                width = bw;
                height = bh;
                pitches = bw * 4;
                lines = bh;

                int gen;
                lock (_bufferLock)
                {
                    if (_frameBuffer != IntPtr.Zero)
                    {
                        try { Marshal.FreeHGlobal(_frameBuffer); } catch { /* ignore */ }
                    }
                    _frameBuffer = Marshal.AllocHGlobal((int)(bw * bh * 4));
                    _w = bw;
                    _h = bh;
                    _bufferValid = true;
                    gen = ++_formatGeneration; // stamped with the geometry it belongs to
                }

                // Bars appear when the video aspect differs from the screen aspect. Only then do we
                // pay for the blurred fill; a clip that already fills the screen shows just the sharp
                // layer (which covers everything at Uniform == the whole screen).
                double videoAspect = vh > 0 ? (double)vw / vh : _screenAspect;
                bool needsBlur = Math.Abs(videoAspect / _screenAspect - 1.0) > 0.03;

                App.Logger?.Information("VideoService: blurred-background format cb — video {VW}x{VH}, buffer {BW}x{BH}, blurFill={Blur}",
                    vw, vh, bw, bh, needsBlur);
                // Runs on a LibVLC decoder thread — enqueue only, never touch the disk or the UI here.
                // blurFill=true arms the fullscreen Gaussian; in 6.5.0 it ran every frame over the LIVE
                // per-frame bitmap and that was the freeze (#616-#623/#632/#636/#687). It now runs over
                // a small frozen snapshot instead, but the trace still records when it is armed.
                VideoDiag.Log("BLUR", $"format cb - video {vw}x{vh}, buffer {bw}x{bh}, blurFill={needsBlur}");

                var disp = Application.Current?.Dispatcher;
                if (disp != null && !disp.HasShutdownStarted)
                    disp.BeginInvoke(new Action(() => RebuildBitmap(gen, bw, bh, needsBlur)));

                return 1; // one plane (RV32)
            }

            private void CleanupCallback(ref IntPtr opaque) { /* buffer is freed in Dispose */ }

            /// <summary>
            /// (Re)build the foreground bitmap for a format generation. Runs on the UI thread, posted
            /// from a decoder thread.
            ///
            /// #687: this used to carry no generation. A mid-stream resolution change fired THREE format
            /// callbacks within 15ms (368x640 -> 368x642) and the posted rebuilds are not guaranteed to
            /// run in the order they were queued, so an older one could land last — leaving _bitmap at a
            /// size that no longer matched _w/_h. The dimension guard in OnRendering then rejected every
            /// single frame for the rest of the clip: a permanently black background with a healthy
            /// decoder behind it. The stamp is re-checked INSIDE _bufferLock so the swap is atomic with
            /// respect to a format callback that lands while this rebuild is mid-flight.
            /// </summary>
            private void RebuildBitmap(int gen, uint bw, uint bh, bool needsBlur)
            {
                if (_disposed) return;
                try
                {
                    if (!IsRebuildCurrent(gen, Volatile.Read(ref _formatGeneration)))
                    {
                        VideoDiag.Log("BLUR", $"stale bitmap rebuild dropped ({bw}x{bh}, gen {gen} superseded)");
                        return;
                    }

                    var bmp = new WriteableBitmap((int)bw, (int)bh, 96, 96, PixelFormats.Bgr32, null);
                    bool applied;
                    lock (_bufferLock)
                    {
                        applied = IsRebuildCurrent(gen, _formatGeneration);
                        if (applied) _bitmap = bmp;
                    }
                    if (!applied)
                    {
                        VideoDiag.Log("BLUR", $"stale bitmap rebuild dropped at swap ({bw}x{bh}, gen {gen} superseded)");
                        return;
                    }

                    _foreground.Source = bmp;
                    _needsBlur = needsBlur;
                    _background.Visibility = needsBlur ? Visibility.Visible : Visibility.Collapsed;
                    _scrim.Visibility = needsBlur ? Visibility.Visible : Visibility.Collapsed;

                    // The fill is fed by RefreshBackgroundSnapshot, NOT by this bitmap (#687). Drop the
                    // old snapshot so the next frame rebuilds it at the new geometry immediately.
                    _snapW = 0;
                    _snapH = 0;
                    _frameCounter = 0;
                    if (!needsBlur) _bgBrush.ImageSource = null;
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("BlurVmemSurface: bitmap rebuild failed: {Error}", ex.Message);
                }
            }

            /// <summary>Write a 4-char FourCC into LibVLC's chroma buffer.</summary>
            private static void WriteChroma(IntPtr chroma, string fourcc)
            {
                for (int i = 0; i < 4; i++)
                    Marshal.WriteByte(chroma, i, (byte)(i < fourcc.Length ? fourcc[i] : 0));
            }

            private IntPtr LockCallback(IntPtr opaque, IntPtr planes)
            {
                lock (_bufferLock)
                {
                    if (!_bufferValid || _frameBuffer == IntPtr.Zero)
                    {
                        Marshal.WriteIntPtr(planes, IntPtr.Zero);
                        return IntPtr.Zero;
                    }
                    Marshal.WriteIntPtr(planes, _frameBuffer);
                    return IntPtr.Zero;
                }
            }

            private void DisplayCallback(IntPtr opaque, IntPtr picture) => _frameReady = true;

            private void Hook()
            {
                if (_hooked) return;
                CompositionTarget.Rendering += OnRendering;
                _hooked = true;
            }

            private void Unhook()
            {
                if (!_hooked) return;
                try { CompositionTarget.Rendering -= OnRendering; } catch { /* ignore */ }
                _hooked = false;
            }

            /// <summary>
            /// Per-frame blit, UI thread. Two strictly separated phases — the separation IS the fix for
            /// the #632/#636/#687 freeze cluster:
            ///
            ///   1. Under _bufferLock: copy the native frame into a managed staging array, capturing
            ///      _w/_h in the same critical section so the blit below can't mix pixels from one
            ///      geometry with dimensions from another. Nothing here can block on another thread.
            ///   2. Lock RELEASED: WriteableBitmap.Lock / copy / AddDirtyRect / Unlock.
            ///
            /// The old code did step 2 while still holding _bufferLock, and that was a three-thread
            /// deadlock: bmp.Lock() waits on the WPF render thread; the render thread was slow because
            /// it was rasterising a fullscreen Gaussian over this very bitmap; and the LibVLC decoder
            /// thread was parked on _bufferLock inside LockCallback/FormatCallback. With the decoder
            /// parked in a native callback, player.Stop() could never return, which is what wedged
            /// CloseAll and leaked a quarantined instance. A previous hang dump caught the render thread
            /// in CWGXBitmapLockState::LockRead — the exact signature. One extra memcpy per frame buys
            /// the guarantee that a stalled render thread can only ever cost frames, never the decoder.
            /// </summary>
            private void OnRendering(object? sender, EventArgs e)
            {
                if (!_bufferValid || !_frameReady) return;
                _frameReady = false;

                WriteableBitmap? bmp = null;
                byte[]? staging = null;
                uint w = 0, h = 0;
                int bytes = 0;

                // ---- Phase 1: native -> managed, under the lock, no blocking calls. ----
                bool got = false;
                try
                {
                    got = Monitor.TryEnter(_bufferLock, 8);
                    if (!got) return;
                    if (!_bufferValid || _frameBuffer == IntPtr.Zero) return;

                    var current = _bitmap;
                    // Dimensions are read HERE, paired with the pixels, so a format callback landing
                    // between the two phases can't make the blit below write a stale size.
                    w = _w;
                    h = _h;
                    // Bitmap not built yet, or its dims don't match the current buffer (a mid-stream
                    // resolution change between FormatCallback and RebuildBitmap): skip this frame.
                    if (current == null || current.PixelWidth != (int)w || current.PixelHeight != (int)h) return;

                    bytes = StagingBytesFor(w, h);
                    if (bytes == 0) return; // absurd geometry from a bogus format callback

                    if (_staging.Length < bytes)
                    {
                        _staging = new byte[bytes];
                        VideoDiag.Log("BLUR", $"staging buffer grown to {bytes / 1024}KB for {w}x{h}");
                    }
                    Marshal.Copy(_frameBuffer, _staging, 0, bytes);
                    staging = _staging;
                    bmp = current;
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("BlurVmemSurface: frame snapshot error: {Error}", ex.Message);
                    return;
                }
                finally
                {
                    if (got) Monitor.Exit(_bufferLock);
                }

                if (bmp == null || staging == null) return;

                // ---- Phase 2: managed -> bitmap, lock released. bmp.Lock() can still wait on the
                // render thread, but it now blocks only this UI frame; the decoder runs on. Time it
                // with a tick delta (no allocation on the per-frame path) and log ONLY when a single
                // blit crosses a human-visible stall, plus the very first frame. ----
                try
                {
                    long lockStart = Environment.TickCount64;
                    bmp.Lock();
                    try
                    {
                        Marshal.Copy(staging, 0, bmp.BackBuffer, bytes);
                        bmp.AddDirtyRect(new Int32Rect(0, 0, (int)w, (int)h));
                    }
                    finally
                    {
                        bmp.Unlock();
                    }
                    long blitMs = Environment.TickCount64 - lockStart;
                    if (blitMs >= 250)
                        VideoDiag.Log("BLUR", $"UI-thread frame blit took {blitMs}ms (WriteableBitmap.Lock contended with the render thread)");
                    if (!_hasRendered)
                        VideoDiag.Log("BLUR", $"first frame blitted ({w}x{h})");
                    _hasRendered = true;

                    RefreshBackgroundSnapshot(staging, w, h);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("BlurVmemSurface: frame copy error: {Error}", ex.Message);
                }
            }

            /// <summary>
            /// Feed the blurred fill from a small, frozen snapshot of the frame instead of the live
            /// bitmap (#687). Before this, _bgBrush.ImageSource WAS the per-frame WriteableBitmap, so
            /// every frame the render thread had to upscale it to the full screen and run a radius-48
            /// Gaussian over it while holding that bitmap's read lock — on a wide monitor with a
            /// vertical clip it fell behind, and the UI thread's next bmp.Lock() inherited the stall.
            /// A 1/8-scale source refreshed every Nth frame is 1/64th of the pixels at 1/6th of the
            /// rate, and because the snapshot is a frozen BitmapSource that is swapped whole, the
            /// render thread never blocks the UI thread on the background layer at all. The output is
            /// behind a heavy blur, so neither the scale nor the cadence is visible.
            /// UI thread only.
            /// </summary>
            private void RefreshBackgroundSnapshot(byte[] src, uint w, uint h)
            {
                if (!_needsBlur) return;

                int frame = _frameCounter;
                _frameCounter = (frame + 1) & 0x3FFFFFFF; // stays positive forever
                if (!IsSnapshotFrame(frame, BgSnapshotEveryNFrames)) return;

                try
                {
                    var (sw, sh) = ComputeSnapshotDims((w, h), BgSnapshotDivisor);
                    int need = sw * sh * 4;
                    if (_snapBuf.Length < need)
                    {
                        _snapBuf = new byte[need];
                        VideoDiag.Log("BLUR", $"blur-fill snapshot source is {sw}x{sh} (1/{BgSnapshotDivisor} of {w}x{h}, every {BgSnapshotEveryNFrames} frames)");
                    }
                    else if (sw != _snapW || sh != _snapH)
                    {
                        VideoDiag.Log("BLUR", $"blur-fill snapshot resized to {sw}x{sh} (from {w}x{h})");
                    }
                    _snapW = sw;
                    _snapH = sh;

                    BoxDownsample(src, (int)w, (int)h, _snapBuf, sw, sh);

                    var snap = BitmapSource.Create(sw, sh, 96, 96, PixelFormats.Bgr32, null, _snapBuf, sw * 4);
                    snap.Freeze(); // frozen => the render thread reads it without ever locking us out
                    _bgBrush.ImageSource = snap;
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("BlurVmemSurface: blur-fill snapshot failed: {Error}", ex.Message);
                }
            }

            /// <summary>
            /// Average each source block down to one destination BGRA pixel. Box filter, allocation
            /// free, capped at 4x4 taps per output pixel so the cost stays flat no matter how big the
            /// block is — which is all the quality a radius-48 blur can possibly show.
            /// </summary>
            private static void BoxDownsample(byte[] src, int sw, int sh, byte[] dst, int dw, int dh)
            {
                if (dw <= 0 || dh <= 0 || sw <= 0 || sh <= 0) return;
                int srcStride = sw * 4;

                for (int dy = 0; dy < dh; dy++)
                {
                    int y0 = dy * sh / dh;
                    int y1 = Math.Max(y0 + 1, (dy + 1) * sh / dh);
                    int stepY = Math.Max(1, (y1 - y0) / 4);
                    int dRow = dy * dw * 4;

                    for (int dx = 0; dx < dw; dx++)
                    {
                        int x0 = dx * sw / dw;
                        int x1 = Math.Max(x0 + 1, (dx + 1) * sw / dw);
                        int stepX = Math.Max(1, (x1 - x0) / 4);

                        int b = 0, g = 0, r = 0, n = 0;
                        for (int y = y0; y < y1; y += stepY)
                        {
                            int row = y * srcStride;
                            for (int x = x0; x < x1; x += stepX)
                            {
                                int i = row + x * 4;
                                b += src[i];
                                g += src[i + 1];
                                r += src[i + 2];
                                n++;
                            }
                        }
                        if (n == 0) n = 1;

                        int o = dRow + dx * 4;
                        dst[o] = (byte)(b / n);
                        dst[o + 1] = (byte)(g / n);
                        dst[o + 2] = (byte)(r / n);
                        dst[o + 3] = 255;
                    }
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                // Invalidate the buffer first so LockCallback hands LibVLC nothing, and stop blitting.
                _bufferValid = false;
                _frameReady = false;
                Unhook();

                // #616-#623: this `lock` is taken on the UI THREAD during CloseAll and contends with
                // LibVLC's native FormatCallback/LockCallback, which hold the same lock. Unlike the
                // per-frame blit (which uses TryEnter with an 8ms budget) this one waits forever, so
                // a decoder thread stuck inside a callback wedges the dispatcher here. Bracketed so
                // the trace shows whether teardown died on this exact lock.
                IntPtr buf;
                long lockStart = Environment.TickCount64;
                lock (_bufferLock)
                {
                    buf = _frameBuffer;
                    _frameBuffer = IntPtr.Zero;
                    _bitmap = null;
                }
                long waited = Environment.TickCount64 - lockStart;
                VideoDiag.Log("BLUR", waited >= 100
                    ? $"surface disposed - waited {waited}ms for _bufferLock (native callback contention)"
                    : "surface disposed");

                // Free the native buffer only after a delay, so any frame still in flight on a
                // LibVLC thread can't write into freed memory. The player is already stopped by
                // CloseAll before this runs, so this is belt-and-suspenders.
                if (buf != IntPtr.Zero)
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(500);
                        try { Marshal.FreeHGlobal(buf); } catch { /* ignore */ }
                    });
                }
            }
        }

        /// <summary>
        /// Creates the primary video window with MediaElement (fallback for when LibVLC fails).
        /// Requires Windows Media Foundation codecs.
        /// </summary>
        private (Window win, MediaElement media) CreateMediaElementVideoWindow(string path, Screen screen, bool strict)
        {
            var mediaDpi = BubbleCountWindow.GetDpiForScreen(screen);
            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = true,
                Topmost = true,
                Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = (screen.Bounds.X + 100) / mediaDpi,
                Top = (screen.Bounds.Y + 100) / mediaDpi,
                Width = 400,
                Height = 300
            };

            var mediaElement = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Volume = GetEffectiveVolume() / 100.0
            };

            mediaElement.MediaOpened += (s, e) =>
            {
                if (mediaElement.NaturalDuration.HasTimeSpan)
                {
                    _duration = mediaElement.NaturalDuration.TimeSpan.TotalSeconds;
                    StartSafetyTimer(_duration);
                }
            };

            mediaElement.MediaEnded += (s, e) =>
                DispatcherHelper.RunOnUI(OnEnded);

            mediaElement.MediaFailed += (s, e) =>
            {
                var errorMsg = e.ErrorException?.Message ?? "Unknown error";
                App.Logger.Error("Media failed: {Error}", errorMsg);

                // Check for Windows Media Player / codec issues
                if (errorMsg.Contains("Windows Media Player") ||
                    errorMsg.Contains("MF_E_") ||
                    errorMsg.Contains("0xC00D") ||
                    errorMsg.Contains("codec", StringComparison.OrdinalIgnoreCase))
                {
                    // Show one-time warning about missing codecs
                    if (!_codecWarningShown)
                    {
                        _codecWarningShown = true;
                        DispatcherHelper.RunOnUI(() =>
                        {
                            System.Windows.MessageBox.Show(
                                Loc.Get("video_codec_required_body"),
                                Loc.Get("video_codec_required_title"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        });
                    }
                }

                DispatcherHelper.RunOnUI(OnEnded);
            };

            var grid = new Grid { Background = Brushes.Black };
            grid.Children.Add(mediaElement);
            win.Content = grid;

            SetupStrictHandlers(win, strict);

            // Prevent video window from stealing focus when clicked (keeps attention targets visible)
            win.PreviewMouseDown += (s, e) =>
            {
                e.Handled = true;
                BringTargetsToFront();
            };

            win.Show();
            // Pump the WPF message loop once to let the compositor settle before maximizing
            win.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            win.WindowState = WindowState.Maximized;
            win.Activate();

            // Load source
            mediaElement.Source = new Uri(path);

            App.Logger.Debug("MediaElement video window on: {Screen}", screen.DeviceName);
            return (win, mediaElement);
        }

        /// <summary>
        /// Creates a mirror window that displays the same video using VisualBrush.
        /// This avoids the decoder creating a separate decode stream.
        /// </summary>
        private Window CreateMirrorVideoWindow(MediaElement sourceMedia, Screen screen, bool strict)
        {
            var mirrorDpi = BubbleCountWindow.GetDpiForScreen(screen);
            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = (screen.Bounds.X + 100) / mirrorDpi,
                Top = (screen.Bounds.Y + 100) / mirrorDpi,
                Width = 400,
                Height = 300
            };

            // Use VisualBrush to mirror the primary MediaElement
            var visualBrush = new VisualBrush
            {
                Visual = sourceMedia,
                Stretch = Stretch.Uniform,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };

            var rectangle = new System.Windows.Shapes.Rectangle
            {
                Fill = visualBrush,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var grid = new Grid { Background = Brushes.Black };
            grid.Children.Add(rectangle);
            win.Content = grid;

            SetupStrictHandlers(win, strict);

            // Prevent video window from stealing focus when clicked (keeps attention targets visible)
            win.PreviewMouseDown += (s, e) =>
            {
                e.Handled = true;
                BringTargetsToFront();
            };

            win.Show();
            win.WindowState = WindowState.Maximized;

            App.Logger.Debug("Mirror video window on: {Screen}", screen.DeviceName);
            return win;
        }

        /// <summary>
        /// Creates a fullscreen video window on the specified screen.
        /// Kept for backward compatibility.
        /// </summary>
        private Window CreateFullscreenVideoWindow(string path, Screen screen, bool strict, bool withAudio)
        {
            var (win, media) = CreateMediaElementVideoWindow(path, screen, strict);
            if (withAudio)
            {
                media.Volume = GetEffectiveVolume() / 100.0;
                media.IsMuted = false;
            }
            else
            {
                media.Volume = 0;
                media.IsMuted = true;
            }
            media.Play();
            return win;
        }

        private void SetupStrictHandlers(Window win, bool strict)
        {
            if (strict)
            {
                // Veto user-initiated closes (Alt+F4 etc.) ONLY while the video is
                // genuinely playing. Once CloseAll() begins teardown it sets
                // _isCleaningUp (and clears _videoPlaying) up front, so a real teardown
                // is never vetoed — otherwise a strict window whose _videoPlaying was
                // left true becomes permanently un-closable and renders solid black
                // (the "video ended, black window won't close" report).
                win.Closing += (s, e) => { if (_videoPlaying && !_isCleaningUp) e.Cancel = true; };
                win.PreviewKeyDown += (s, e) =>
                {
                    // In strict mode, block panic key, Alt+F4, and system keys
                    if (e.Key.ToString() == App.Settings.Current.PanicKey || e.Key == Key.System ||
                        (e.Key == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
                        e.Handled = true;
                };
                // In strict mode the window is already Topmost — reactivation causes a
                // focus-stealing loop that interferes with LibVLC's child HWND rendering
                win.Deactivated += (s, e) =>
                {
                    if (_videoPlaying && _strictActive && !App.Settings.Current.AttentionChecksEnabled)
                    {
                        App.Logger?.Debug("VideoService: Strict video window deactivated (Topmost keeps it visible)");
                    }
                };
            }
            else
            {
                // PreviewKeyDown (not KeyDown) so the LibVLC VideoView / MediaElement
                // child surface can't swallow ESC before the window-level handler
                // sees it. Two independent hotkeys:
                //   - ESC: hardcoded, non-rebindable "dismiss current video" — calls
                //     Cleanup() so the session keeps running and ScheduleNext() fires.
                //   - PanicKey: user-rebindable, calls ForceCleanup() which ends the
                //     run without scheduling a replacement.
                // The two roles never overlap because they call different cleanup paths.
                win.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        // WPF key routing only happens if the dispatcher is draining input, so this
                        // line doubles as proof the UI thread was alive when the user pressed ESC
                        // (#616-#623: "I hit escape/panic and nothing happened").
                        VideoDiag.Log("PANIC", "ESC received by the video window - dismissing via Cleanup");
                        e.Handled = true;
                        Cleanup();
                        return;
                    }
                    if (App.Settings.Current.PanicKeyEnabled &&
                        e.Key.ToString() == App.Settings.Current.PanicKey)
                    {
                        VideoDiag.Log("PANIC", $"panic key '{e.Key}' received by the video window - ForceCleanup");
                        e.Handled = true;
                        ForceCleanup();
                    }
                };
            }
        }

        #region Attention Checks

        private void SetupAttention()
        {
            Task.Delay(2000).ContinueWith(_ =>
            {
                try
                {
                    DispatcherHelper.RunOnUI(() =>
                    {
                        if (!_videoPlaying) return;

                        _spawned = 0; // Reset spawned counter
                        var dur = _duration > 0 ? _duration : 60;
                        // Use setting directly as total count (not density)
                        var maxTargets = Math.Max(1, App.Settings.Current.AttentionDensity);
                        _total = App.Settings.Current.RandomizeAttentionTargets
                            ? _random.Next(1, maxTargets + 1)  // Random from 1 to max (inclusive)
                            : maxTargets;

                        // Generate spawn times with minimum gap to prevent simultaneous targets
                        var minGap = 3.0; // Minimum 3 seconds between targets
                        var availableWindow = Math.Max(1, dur - 8); // Stop spawning ~5s before end
                        for (int i = 0; i < _total; i++)
                        {
                            var spawnTime = 3 + _random.NextDouble() * availableWindow;
                            _spawnTimes.Add(spawnTime);
                        }
                        _spawnTimes.Sort();

                        // Ensure minimum gap between targets (adjust times if too close)
                        for (int i = 1; i < _spawnTimes.Count; i++)
                        {
                            if (_spawnTimes[i] - _spawnTimes[i - 1] < minGap)
                            {
                                _spawnTimes[i] = _spawnTimes[i - 1] + minGap;
                            }
                        }

                        _attentionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                        _attentionTimer.Tick += CheckSpawnTargets;
                        _attentionTimer.Start();

                        App.Logger.Information("Attention: {Count} targets over {Duration}s", _total, (int)dur);
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("SetupAttention failed: {Error}", ex.Message);
                }
            });
        }

        private void CheckSpawnTargets(object? s, EventArgs e)
        {
            if (!_videoPlaying) return;
            var elapsed = (DateTime.Now - _startTime).TotalSeconds;
            while (_spawnTimes.Count > 0 && elapsed >= _spawnTimes[0])
            {
                _spawnTimes.RemoveAt(0);
                SpawnTarget();
            }
        }

        private void SpawnTarget()
        {
            try
            {
                var settings = App.Settings.Current;
                var pool = settings.AttentionPool.Where(p => p.Value).Select(p => p.Key).ToList();
                var text = pool.Count > 0 ? pool[_random.Next(pool.Count)] : "CLICK ME";

                var screens = settings.DualMonitorEnabled ? App.GetAllScreensCached() : new[] { Screen.PrimaryScreen };
                // Safety check: ensure we have at least one screen
                if (screens == null || screens.Length == 0 || screens[0] == null)
                {
                    App.Logger?.Warning("SpawnTarget: No screens available");
                    return;
                }

                _spawned++; // Track spawn events (not individual targets)

                // When dual monitor is enabled, spawn targets on ALL screens simultaneously
                // User only needs to click ONE target to get the hit - all targets from this spawn clear together
                var spawnedTargets = new List<FloatingText>();
                bool hitRegistered = false; // Prevent double-counting hits from the same spawn

                App.Logger?.Debug("Spawning attention target: '{Text}' on {ScreenCount} screen(s) ({Spawned}/{Total})",
                    text, screens.Length, _spawned, _total);

                foreach (var screen in screens)
                {
                    if (screen == null) continue;

                    FloatingText? target = null;
                    target = new FloatingText(text, screen, settings.AttentionSize, () =>
                    {
                        // Only count as a hit once per spawn (user clicked any target from this batch)
                        if (hitRegistered) return;
                        hitRegistered = true;

                        _ = App.Haptics?.VideoTargetHitAsync();
                        _hits++;
                        App.Progression?.AddXP(15, XPSource.Video);

                        // Destroy ALL targets from this spawn (user caught one, clear all on all monitors)
                        lock (_targets)
                        {
                            foreach (var t in spawnedTargets)
                            {
                                if (_targets.Contains(t))
                                {
                                    _targets.Remove(t);
                                    if (t != target) // The clicked one will fade out naturally
                                    {
                                        t.Destroy();
                                    }
                                }
                            }
                        }

                        // Get remaining targets for bringing to front
                        List<FloatingText> remainingTargets;
                        lock (_targets)
                        {
                            remainingTargets = _targets.ToList();
                        }

                        App.Logger?.Information("ATTENTION: Hit {Hits}/{Spawned}, {Remaining} targets remaining", _hits, _spawned, remainingTargets.Count);

                        // Bring remaining targets to front AFTER the clicked target fully closes
                        if (remainingTargets.Count > 0)
                        {
                            Task.Delay(300).ContinueWith(_ =>
                            {
                                try
                                {
                                    Application.Current?.Dispatcher?.BeginInvoke(() =>
                                    {
                                        foreach (var t in remainingTargets)
                                        {
                                            t.BringToFront();
                                        }
                                    });
                                }
                                catch (Exception ex)
                                {
                                    App.Logger?.Debug("Failed to bring targets to front after hit: {Error}", ex.Message);
                                }
                            });
                        }
                    });

                    spawnedTargets.Add(target);
                    lock (_targets)
                    {
                        _targets.Add(target);
                    }
                }

                App.Logger?.Information("ATTENTION: Spawned {Count} targets on all screens, total now: {Total}",
                    spawnedTargets.Count, _targets.Count);

                // Auto-expire all targets from this spawn together
                var lifespan = settings.AttentionLifespan * 1000;
                Task.Delay(lifespan).ContinueWith(_ =>
                {
                    try
                    {
                        DispatcherHelper.RunOnUI(() =>
                        {
                            try
                            {
                                lock (_targets)
                                {
                                    foreach (var target in spawnedTargets)
                                    {
                                        if (_targets.Contains(target))
                                        {
                                            _targets.Remove(target);
                                            target.Destroy();
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Warning("Error expiring targets: {Error}", ex.Message);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Target auto-expire task failed (app may be shutting down): {Error}", ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to spawn attention target: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Brings all attention targets back to front when video is clicked
        /// </summary>
        private void BringTargetsToFront()
        {
            // Delay slightly to ensure targets come to front AFTER video window activation
            Task.Delay(50).ContinueWith(_ =>
            {
                try
                {
                    Application.Current?.Dispatcher?.BeginInvoke(() =>
                    {
                        // The click that got us here ACTIVATED the video window (e.Handled only
                        // stops the WPF routed event, not Win32 WM_MOUSEACTIVATE), which raised
                        // it above the chaos run's bubbles/HUD/overlays. Lift the game layer
                        // back first, then the attention targets on top of everything.
                        try { App.Chaos?.RaiseGameLayerAboveVideo(); } catch { }
                        lock (_targets)
                        {
                            foreach (var t in _targets)
                            {
                                t.BringToFront();
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("BringTargetsToFront task failed (app may be shutting down): {Error}", ex.Message);
                }
            });
        }

        #endregion

        #region Video End / Penalty / Mercy

        private void OnEnded()
        {
            App.Logger?.Information("VideoService: OnEnded() called, _videoPlaying={Playing}, _windows={WinCount}, _isCleaningUp={Cleaning}",
                _videoPlaying, _windows.Count, _isCleaningUp);

            // Skip if cleanup is already in progress (e.g., from panic key)
            if (_isCleaningUp)
            {
                App.Logger?.Information("VideoService: OnEnded() early return - cleanup already in progress");
                return;
            }

            if (!_videoPlaying)
            {
                App.Logger?.Information("VideoService: OnEnded() early return - video already marked as not playing");
                return;
            }

            var settings = App.Settings.Current;
            bool loop = false, troll = false;

            if (settings.AttentionChecksEnabled && _spawned > 0)
            {
                bool passed = _hits >= _spawned;
                App.Logger.Information("Attention result: {Hits}/{Spawned} (of {Total} scheduled) = {Result}", _hits, _spawned, _total, passed ? "PASS" : "FAIL");

                if (passed)
                {
                    var xpForPlays = (_penalties + 1) * 50;
                    var bonus = 200;
                    App.Progression?.AddXP(xpForPlays + bonus, XPSource.Video);

                    // Track successful attention check
                    App.Achievements?.TrackAttentionCheckPassed(isVideo: true);

                    if (_random.NextDouble() < 0.1)
                    {
                        loop = troll = true;
                    }
                }
                else
                {
                    loop = true;
                    // Track attention check failure for "Mercy Beggar" achievement
                    App.Achievements?.TrackAttentionCheckFailed();
                    // Track video-specific failure stat
                    App.Achievements?.TrackVideoAttentionCheckFailed();
                    // Apply Trainer companion penalty (-25 XP)
                    App.Companion?.OnAttentionCheckFailed();
                }
            }

            if (loop && !string.IsNullOrEmpty(_retryPath))
            {
                _penalties++;
                if (_penalties >= 3 && settings.MercySystemEnabled)
                    ShowMessage(App.Mods?.GetAttentionCheckMercyMessage() ?? "BAMBI GETS MERCY", 2500, Cleanup);
                else
                {
                    // Snapshot the strict flag NOW, while the run is still live, and keep the run
                    // marked strict across the message window.
                    //
                    // The retry fires from a bare Task.Delay continuation inside ShowMessage that
                    // Stop() has no way to cancel, and Stop() clears _strictActive. Reading the
                    // FIELD from inside that callback therefore let a stop landing during the ~2s
                    // message window silently downgrade the replacement video to non-strict: Esc
                    // dismissed it, panic worked, Alt+F4 worked. The vout self-heal retry further
                    // down this file (~:3856) has the identical "_videoPlaying = false, close, then
                    // replay" shape and is immune for exactly this reason — it re-passes its
                    // captured `strict` PARAMETER instead of re-reading the field. Same trick here.
                    var strictForRetry = _strictActive;
                    _strictRetryPending = strictForRetry;
                    var retryGen = _retryGeneration;

                    Action replay = () =>
                    {
                        try
                        {
                            // Did anything end or replace this run while the message was up? Panic is
                            // the case that matters: it reaches Stop(), which tears the run down and
                            // speaks "we're stopping, you're safe" - and then this callback, which no
                            // one can cancel, used to start another mandatory video ~2s later and make
                            // a liar of it. The delay still elapses; the retry just no longer acts.
                            if (_retryGeneration != retryGen)
                                return;

                            // ShowMessage already set _videoPlaying = false and called CloseAll()
                            // Reset attention tracking for retry
                            _hits = 0;
                            _spawnTimes.Clear();
                            // Extend the stuck detection timeout to prevent InteractionQueue from
                            // auto-completing Video during the retry gap, which would let queued
                            // interactions (e.g. BubbleCount) start while the retry video plays.
                            App.InteractionQueue?.ExtendTimeout(300, InteractionQueueService.InteractionType.Video);
                            // Pick a fresh video for the retry — replaying the same video makes
                            // attention checks easier to game (memorize the timing) and was a
                            // user-reported inconsistency vs. BubbleCount which always picks new.
                            var retryVideo = GetNextVideo();
                            PlayVideo(string.IsNullOrEmpty(retryVideo) ? _retryPath! : retryVideo, strictForRetry);
                        }
                        finally
                        {
                            // The gap is over either way. PlayVideo already clears this on its
                            // success path; this covers the paths that never reach that line (it
                            // early-returns when another video grabbed the slot, or something in
                            // here threw). A gap flag that leaked true would lock the user out of
                            // their own stop controls indefinitely, which is far worse than the
                            // hole it closes.
                            _strictRetryPending = false;
                        }
                    };

                    try
                    {
                        ShowMessage(troll ? "GOOD GIRL!\nWATCH AGAIN 😜" : (App.Mods?.GetAttentionCheckFailMessage() ?? "DUMB BAMBI!\nTRY AGAIN"), 2000, replay);
                    }
                    catch
                    {
                        // ShowMessage itself blew up (window creation on a machine mid screen
                        // reconfiguration, say), so the callback that would have cleared the gap
                        // flag never gets scheduled. Clear it before letting the exception carry on
                        // as it always has - leaving it set would strand the user locked out with no
                        // video on screen and nothing left running to end the run.
                        _strictRetryPending = false;
                        throw;
                    }
                }
                return;
            }

            // Watch-time crediting now happens in CloseAll (position-based, so interruptions/attention
            // fails credit what was watched too — #447). The WPF MediaElement fallback path has no
            // TimeChanged position, so on a natural end seed the watched position to the full duration to
            // preserve the old "credit full length" behavior; the LibVLC path is already at ~duration here.
            if (_lastWatchPositionMs <= 0 && _duration > 0)
                _lastWatchPositionMs = _duration * 1000.0;

            Cleanup();
        }

        /// <summary>
        /// Marks any attention-fail retry that is still sitting in a Task.Delay as obsolete, and ends
        /// the strict gap. Called from every path that ends or replaces a run - the teardowns (Stop,
        /// ForceCleanup, Cleanup) and the start of any new video (PlayVideo, PlayUrl).
        /// </summary>
        private void CancelPendingRetry()
        {
            _strictRetryPending = false;
            _retryGeneration++;
        }

        private void ShowMessage(string text, int ms, Action then)
        {
            // CRITICAL: Set _videoPlaying to false BEFORE CloseAll() so strict mode
            // handlers don't cancel window closing (they check _videoPlaying in Closing event)
            _videoPlaying = false;
            CloseAll();

            var screens = App.Settings.Current.DualMonitorEnabled ? App.GetAllScreensCached() : new[] { Screen.PrimaryScreen };
            // Safety check: ensure we have at least one screen
            if (screens == null || screens.Length == 0 || screens[0] == null)
            {
                App.Logger?.Warning("ShowMessage: No screens available, executing callback immediately");
                then();
                return;
            }

            foreach (var screen in screens)
            {
                var msgDpi = BubbleCountWindow.GetDpiForScreen(screen);
                var win = new Window
                {
                    WindowStyle = WindowStyle.None,
                    Background = Brushes.Black,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = (screen.Bounds.X + 100) / msgDpi,
                    Top = (screen.Bounds.Y + 100) / msgDpi,
                    Width = 400,
                    Height = 300,
                    Content = new TextBlock
                    {
                        Text = text,
                        Foreground = Brushes.Magenta,
                        FontSize = 64,
                        FontWeight = FontWeights.Bold,
                        FontFamily = new FontFamily("Impact"),
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                win.Show();
                win.WindowState = WindowState.Maximized;
                _messageWindows.Add(win);  // Track for cleanup
            }

            Task.Delay(ms).ContinueWith(_ =>
            {
                try
                {
                    DispatcherHelper.RunOnUI(() =>
                    {
                        CloseMessageWindows();
                        then();
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("ShowMessage callback failed: {Error}", ex.Message);
                }
            });
        }

        private void CloseMessageWindows()
        {
            foreach (var w in _messageWindows.ToList())
            {
                try { w.Close(); }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to close message window: {Error}", ex.Message);
                }
            }
            _messageWindows.Clear();
        }

        #endregion

        #region Safety Timeout

        /// <summary>
        /// Starts a safety timer to force cleanup if MediaEnded never fires.
        /// This prevents the video window from getting stuck on fullscreen.
        /// </summary>
        private void StartSafetyTimer(double videoDurationSeconds)
        {
            _safetyTimer?.Stop();

            // Stop the fallback timer since we now have accurate duration
            _fallbackSafetyTimer?.Stop();
            _fallbackSafetyTimer = null;

            // Extend stuck detection timeout so long videos aren't killed prematurely
            App.InteractionQueue?.ExtendTimeout(videoDurationSeconds, InteractionQueueService.InteractionType.Video);

            // Add 5 second buffer beyond video duration
            var timeoutSeconds = videoDurationSeconds + 5;

            _lastSafetyTimeMs = -1;
            _lastSafetyProgressUtc = DateTime.UtcNow;

            _safetyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(timeoutSeconds) };
            _safetyTimer.Tick += (s, e) =>
            {
                if (!_videoPlaying) { _safetyTimer?.Stop(); return; }

                // #536: while a Deeper enhancement drives the clip it can loop or hold well past the
                // declared duration (loop_region, SpeakHoldMode.LoopRegion, speak-pauses), so a plain
                // duration guillotine cuts the video "after the original time is over". Switch to a
                // progress-based stall watch: keep the video alive while playback is still advancing,
                // slide the InteractionQueue stuck window forward too, and only force-close once it has
                // shown zero progress for the grace window (a genuine wedge, not an intended pause).
                if (_enhancementDriving)
                {
                    long curMs = -1;
                    try { curMs = _primaryMediaPlayer?.Time ?? -1; } catch { curMs = -1; }

                    var now = DateTime.UtcNow;
                    bool advancing = curMs >= 0 && curMs != _lastSafetyTimeMs;
                    if (advancing || _lastSafetyTimeMs < 0)
                    {
                        _lastSafetyTimeMs = curMs;
                        _lastSafetyProgressUtc = now;
                        // Keep the secondary InteractionQueue stuck-recovery backstop from cutting the
                        // loop either (its window is min 5 min / duration+30s — a long loop can reach it).
                        try { App.InteractionQueue?.ExtendTimeout(_duration > 0 ? _duration : MaxVideoFallbackSeconds, InteractionQueueService.InteractionType.Video); }
                        catch (Exception ex) { App.Logger?.Debug("VideoService: safety-timer ExtendTimeout failed: {E}", ex.Message); }
                        if (_safetyTimer != null) _safetyTimer.Interval = EnhancementRecheckInterval;
                        return;
                    }

                    if ((now - _lastSafetyProgressUtc).TotalSeconds < EnhancementStallGraceSeconds)
                    {
                        if (_safetyTimer != null) _safetyTimer.Interval = EnhancementRecheckInterval;
                        return; // paused (e.g. speak-hold) but within grace — not a wedge
                    }

                    _safetyTimer?.Stop();
                    App.Logger?.Warning("VideoService: Enhancement-driven video stalled ~{Grace}s with no playback progress. Forcing cleanup.", EnhancementStallGraceSeconds);
                    Cleanup();
                    return;
                }

                // Non-enhanced video: unchanged one-shot guillotine at duration+5s.
                _safetyTimer?.Stop();
                if (_videoPlaying)
                {
                    App.Logger?.Warning("VideoService: Safety timeout triggered - MediaEnded did not fire. Forcing cleanup.");
                    Cleanup();
                }
            };
            _safetyTimer.Start();

            App.Logger?.Debug("VideoService: Safety timer started for {Duration}s (fallback timer stopped)", timeoutSeconds);
        }

        /// <summary>
        /// Starts a fallback safety timer with a fixed maximum duration.
        /// Used when video duration is unknown (LengthChanged may never fire).
        /// Will be replaced by accurate timer once duration is known.
        /// </summary>
        private void StartFallbackSafetyTimer()
        {
            _fallbackSafetyTimer?.Stop();

            _fallbackSafetyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(MaxVideoFallbackSeconds) };
            _fallbackSafetyTimer.Tick += (s, e) =>
            {
                _fallbackSafetyTimer?.Stop();
                if (_videoPlaying)
                {
                    App.Logger?.Warning("VideoService: FALLBACK safety timeout triggered after {Duration}s - video duration was never determined. Forcing cleanup.",
                        MaxVideoFallbackSeconds);
                    Cleanup();
                }
            };
            _fallbackSafetyTimer.Start();

            App.Logger?.Debug("VideoService: Fallback safety timer started for {Duration}s", MaxVideoFallbackSeconds);
        }

        /// <summary>
        /// Arms the user's max video-length cap (#584). When VideoMaxDurationSeconds &gt; 0, force-ends the
        /// current video once it has played that many wall-clock seconds, regardless of the file's real
        /// duration — the selection-time filter only best-effort excludes long files (cold-cache bypass),
        /// so this is what actually guarantees the on-screen video never runs past the user's limit.
        /// One-shot; disarmed on Cleanup. No-op when no cap is set.
        /// </summary>
        private void StartMaxLengthCapTimer()
        {
            _maxLenCapTimer?.Stop();
            _maxLenCapTimer = null;

            var maxSec = App.Settings?.Current?.VideoMaxDurationSeconds ?? 0;
            if (maxSec <= 0) return;

            _maxLenCapTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(maxSec) };
            _maxLenCapTimer.Tick += (s, e) =>
            {
                _maxLenCapTimer?.Stop();
                _maxLenCapTimer = null;
                if (_videoPlaying)
                {
                    App.Logger?.Information("VideoService: Max video-length cap ({Max}s) reached - ending video early.", maxSec);
                    Cleanup();
                }
            };
            _maxLenCapTimer.Start();

            App.Logger?.Debug("VideoService: Max video-length cap timer started for {Max}s", maxSec);
        }

        /// <summary>
        /// Arms the off-thread wedge watchdog for the current video. Idempotent (safe to call again on
        /// a retry/troll replay). Must be called on the UI thread, before window creation.
        /// </summary>
        /// <summary>
        /// Arm the vout (video output) watchdog for the primary player of the current video. Called
        /// right after Play(). One-shot: fires once at <see cref="VoutGraceMs"/> and checks whether
        /// LibVLC ever created a video output. Decode-side health signals (TimeChanged, EndReached)
        /// are useless here — on the white-screen machines the clip "plays" fine, there is just
        /// nothing on screen, which is why the software-decode default (#533/#537/#540) didn't cure it.
        /// </summary>
        private void StartVoutWatchdog(LibVLCSharp.Shared.MediaPlayer player, string path, bool strict)
        {
            // Capture the instance the player was built from NOW: by the time the timer fires,
            // another heal path may already have retired and rebuilt the shared instance, and the
            // fire handler must never condemn the fresh one.
            var owner = _libVLC;
            _voutSeen = false;
            // Vout attach/detach is the single most informative signal for the black/white-screen
            // reports (#616-#623): e.Count > 0 means LibVLC actually created a video output, 0 means
            // it tore one down. Fires on a LibVLC thread, so it must not do anything but enqueue.
            player.Vout += (s, e) =>
            {
                VideoDiag.Log("VOUT", e.Count > 0 ? $"attach (count={e.Count})" : "DETACH (count=0)");
                if (e.Count > 0) { _voutSeen = true; _voutEverSeen = true; }
            };
            VideoDiag.Log("VOUT", $"watchdog armed - grace {VoutGraceMs}ms, mid-play poll every {VoutMidPollMs}ms");
            _voutWatchTimer?.Dispose();
            _voutWatchTimer = new System.Threading.Timer(
                _ => VoutWatchdogFire(player, owner, path, strict), null, VoutGraceMs, System.Threading.Timeout.Infinite);

            // Part 4: mid-play vout-loss poll. Reset per-playback state and start ticking after the
            // start grace has elapsed (before that, a missing vout is the start watchdog's job).
            _voutEverSeen = false;
            _voutLostSinceTicks = 0;
            _voutMidHealUsed = false;
            _voutMidWatchTimer?.Dispose();
            _voutMidWatchTimer = new System.Threading.Timer(
                _ => VoutMidPlayTick(player, owner, path, strict), null, VoutGraceMs + VoutMidPollMs, VoutMidPollMs);
        }

        private void StopVoutWatchdog()
        {
            try { _voutWatchTimer?.Dispose(); } catch { }
            _voutWatchTimer = null;
            try { _voutMidWatchTimer?.Dispose(); } catch { }
            _voutMidWatchTimer = null;
        }

        /// <summary>
        /// Threadpool callback at the vout deadline. If the primary player never created a video
        /// output the user is staring at a white fullscreen: retire the (suspect) shared LibVLC
        /// instance and replay the same video once on a fresh one; if the retry white-screens too,
        /// give up on this video and let the scheduler move on.
        /// </summary>
        private void VoutWatchdogFire(LibVLCSharp.Shared.MediaPlayer player, LibVLC? owner, string path, bool strict)
        {
            try
            {
                if (!_videoPlaying || _isCleaningUp) return;
                if (!ReferenceEquals(player, _primaryMediaPlayer)) return;   // stale timer from a previous video
                if (_voutSeen) return;
                // Authoritative live check — covers a vout that appeared before the event was wired.
                try { if (player.VoutCount > 0) return; } catch { }

                // Reaching here IS the white-screen state the reports describe (#557-#560/#574, and
                // now #616/#617/#621/#622/#623): the clip is decoding but nothing is on screen.
                VideoDiag.Log("VOUT", $"NO VIDEO OUTPUT {VoutGraceMs}ms after Play ({Path.GetFileName(path)}) - white-screen state confirmed");

                // A file with no video track legitimately never creates a vout — an audio-only .mp4 in
                // the videos folder is not an output failure. Let it play out (black window + audio,
                // the pre-heal behavior) instead of truncating it at the deadline every rotation and
                // condemning a healthy instance each time.
                try
                {
                    using var media = player.Media;
                    var tracks = media?.Tracks;
                    if (tracks != null && tracks.Length > 0 && !tracks.Any(t => t.TrackType == TrackType.Video))
                    {
                        App.Logger?.Information(
                            "VideoService: no vout after {Grace}ms but {File} has no video track - letting it play out",
                            VoutGraceMs, Path.GetFileName(path));
                        VideoDiag.Log("VOUT", "no video track in this file - not an output failure, letting it play out");
                        return;
                    }
                }
                catch { /* track probe is best-effort; fall through to the heal */ }

                // Retire first, retry only if the retire actually happened — if the circuit breaker
                // tripped (or another heal already swapped the instance) a replay would just fail the
                // same way, so skip straight to the give-up path.
                var retired = RetireSharedLibVLC(owner, "no video output within grace window");
                var retry = retired && !_voutRetryUsed;
                _voutRetryUsed = true;
                App.Logger?.Error(
                    "VideoService: no video output {Grace}ms after Play ({File}) - white-screen output failure (retired={Retired}){Next}",
                    VoutGraceMs, Path.GetFileName(path), retired,
                    retry ? ", retrying on a fresh instance" : "; skipping this video");
                VideoDiag.Log("HEAL", $"start-of-play decision: retired={retired}, retry={retry} (retryBudgetUsed={_voutRetryUsed})");

                DispatchVoutHeal(player, path, strict, retry);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VoutWatchdogFire error: {Error}", ex.Message);
                VideoDiag.Log("VOUT", "watchdog fire THREW: " + ex.Message);
            }
        }

        /// <summary>
        /// Threadpool poll (every <see cref="VoutMidPollMs"/>, starting after the start grace) for a
        /// vout that appeared and then VANISHED mid-clip — the screen goes white partway through
        /// (#600) but decode-side signals stay healthy, so nothing else catches it. Only acts once the
        /// loss has persisted past <see cref="VoutLostGraceMs"/> (a brief drop at a seek/segment
        /// boundary or the clip tail is not a failure) and heals at most once per playback.
        /// </summary>
        /// <summary>Outcome of a single mid-play vout poll (#600). Pure decision extracted so the
        /// state machine can be unit-tested without LibVLC.</summary>
        internal enum VoutMidDecision
        {
            Healthy,               // vout present — reset the loss clock
            DeferToStartWatchdog,  // never seen a vout yet — the start watchdog owns this case
            StartLossClock,        // vout just went absent — begin timing the loss
            WithinGrace,           // absent but not long enough yet to act
            Heal                   // absent past the grace window — retire + replay
        }

        /// <summary>
        /// Pure state transition for the mid-play vout poll. Given the live <paramref name="voutCount"/>
        /// and the persisted loss state, returns the action and the updated loss-clock value. Mirrors
        /// exactly the branch logic in <see cref="VoutMidPlayTick"/> so tests exercise the real decision.
        /// </summary>
        internal static VoutMidDecision EvaluateVoutMidPlay(
            uint voutCount, bool everSeen, long lostSinceTicks, long nowTicks, int graceMs, out long newLostSinceTicks)
        {
            if (voutCount > 0)
            {
                newLostSinceTicks = 0;   // healthy — reset the loss window
                return VoutMidDecision.Healthy;
            }

            newLostSinceTicks = lostSinceTicks;

            // vout absent. Before it has ever appeared, a missing vout is the start watchdog's job.
            if (!everSeen) return VoutMidDecision.DeferToStartWatchdog;

            if (lostSinceTicks == 0)
            {
                newLostSinceTicks = nowTicks;   // start the loss clock
                return VoutMidDecision.StartLossClock;
            }

            var lostMs = (nowTicks - lostSinceTicks) / TimeSpan.TicksPerMillisecond;
            if (lostMs < graceMs) return VoutMidDecision.WithinGrace;

            return VoutMidDecision.Heal;
        }

        private void VoutMidPlayTick(LibVLCSharp.Shared.MediaPlayer player, LibVLC? owner, string path, bool strict)
        {
            try
            {
                if (!_videoPlaying || _isCleaningUp || _voutMidHealUsed) return;
                if (!ReferenceEquals(player, _primaryMediaPlayer)) return;   // stale timer from a previous video

                uint voutCount;
                try { voutCount = player.VoutCount; } catch { return; }

                var now = DateTime.UtcNow.Ticks;
                var decision = EvaluateVoutMidPlay(voutCount, _voutEverSeen, _voutLostSinceTicks, now, VoutLostGraceMs, out var newLost);
                _voutLostSinceTicks = newLost;

                switch (decision)
                {
                    case VoutMidDecision.Healthy:
                        _voutEverSeen = true;
                        return;
                    case VoutMidDecision.DeferToStartWatchdog:
                    case VoutMidDecision.StartLossClock:
                    case VoutMidDecision.WithinGrace:
                        return;
                    // VoutMidDecision.Heal falls through to the heal path below.
                }

                var lostMs = (now - _voutLostSinceTicks) / TimeSpan.TicksPerMillisecond;
                _voutMidHealUsed = true;
                var retired = RetireSharedLibVLC(owner, "video output lost mid-playback");
                App.Logger?.Error(
                    "VideoService: video output vanished mid-playback ({File}, gone {LostMs}ms) - white-screen mid-clip (retired={Retired}){Next}",
                    Path.GetFileName(path), lostMs, retired,
                    retired ? ", replaying on a fresh instance" : "; giving up on this video");
                VideoDiag.Log("HEAL", $"mid-play vout loss ({lostMs}ms gone) - retired={retired}, replay={retired}");

                // Retire succeeded ⇒ replay the same clip once on the fresh instance; if the circuit
                // breaker tripped, a replay would fail identically, so give up via the natural-end path.
                DispatchVoutHeal(player, path, strict, retry: retired);
            }
            catch (Exception ex) { App.Logger?.Debug("VoutMidPlayTick error: {Error}", ex.Message); }
        }

        /// <summary>
        /// Shared UI-thread tail of both vout heal paths (start-of-play and mid-play). Either replays
        /// the same video once on the freshly-rebuilt instance (<paramref name="retry"/>) or gives up
        /// via the full natural-end <see cref="Cleanup"/>. Snapshots the teardown generation so a
        /// racing teardown (panic, natural end, wedge rescue) or a newer video aborts this action — it
        /// must never resurrect a video the user stopped, run reentrantly inside another teardown's
        /// message pump, or kill a video it never belonged to.
        /// </summary>
        private void DispatchVoutHeal(LibVLCSharp.Shared.MediaPlayer player, string path, bool strict, bool retry)
        {
            var gen = _teardownGeneration;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            // The heal decision is made off-thread but EXECUTED on the dispatcher. If the UI thread
            // is already wedged this continuation never runs — and the user keeps staring at the
            // black/white fullscreen with nothing in the log (#616-#623). Recording both the post
            // and the run is what distinguishes "the heal never decided" from "the heal decided but
            // the dispatcher was dead".
            VideoDiag.Log("HEAL", $"posting to dispatcher (retry={retry}, gen={gen}, uiStall={VideoDiag.UiStallMs}ms)");
            dispatcher.BeginInvoke(new Action(() =>
            {
                VideoDiag.Log("HEAL", "dispatcher continuation running");
                try
                {
                    if (_isCleaningUp || _teardownGeneration != gen || !_videoPlaying)
                    {
                        VideoDiag.Log("HEAL", $"aborted - cleaningUp={_isCleaningUp}, genMoved={_teardownGeneration != gen}, playing={_videoPlaying}");
                        return;
                    }
                    // A different video may have started while this was queued — never kill it.
                    if (!ReferenceEquals(player, _primaryMediaPlayer))
                    {
                        VideoDiag.Log("HEAL", "aborted - a different video owns the primary player now");
                        return;
                    }

                    if (retry)
                    {
                        // Slot-preserving teardown, mirroring the attention-fail retry: CloseAll
                        // does NOT release the InteractionQueue Video slot, so queued lock cards /
                        // bubble counts can't start on top of the retry video; extend the stuck
                        // timeout so the queue doesn't auto-complete the slot during the gap.
                        _safetyTimer?.Stop();
                        _fallbackSafetyTimer?.Stop();
                        _fallbackSafetyTimer = null;
                        _videoPlaying = false;
                        CloseAll();
                        App.InteractionQueue?.ExtendTimeout(300, InteractionQueueService.InteractionType.Video);
                        App.Logger?.Information("VideoService: vout self-heal - replaying {File} on a fresh LibVLC instance",
                            Path.GetFileName(path));
                        VideoDiag.Log("HEAL", "replaying on a fresh LibVLC instance");
                        PlayVideo(path, strict, isVoutRetry: true);
                    }
                    else
                    {
                        VideoDiag.Log("HEAL", "giving up on this video - full natural-end cleanup");
                        // Give up on this video via the FULL natural-end path: Cleanup raises
                        // VideoEnded, resumes flashes/bubbles, releases the queue slot, and re-arms
                        // the scheduler. ForceCleanup did none of that, which left ambient features
                        // suppressed for the whole inter-video gap on chronically failing machines.
                        Cleanup();
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "VideoService: vout self-heal dispatch failed");
                    VideoDiag.Log("HEAL", "dispatch THREW: " + ex.Message);
                }
            }));
        }

        private void StartWedgeWatchdog()
        {
            _wedgeRescueFired = false;
            _preRollStallLogged = false;
            System.Threading.Interlocked.Exchange(ref _uiHeartbeatTicks, DateTime.UtcNow.Ticks);

            // UI-thread heartbeat: proves the dispatcher is still draining. Background priority so a
            // genuine native block (which does NOT pump) lets it go stale, while the legitimate
            // teardown pump-wait (which pumps Background work) keeps it fresh — no false positives.
            _heartbeatTimer?.Stop();
            _heartbeatTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _heartbeatTimer.Tick += (s, e) =>
                System.Threading.Interlocked.Exchange(ref _uiHeartbeatTicks, DateTime.UtcNow.Ticks);
            _heartbeatTimer.Start();

            _wedgeWatchdog?.Dispose();
            _wedgeWatchdog = new System.Threading.Timer(_ => WedgeWatchdogTick(), null, 3000, 3000);
        }

        private void StopWedgeWatchdog()
        {
            try { _wedgeWatchdog?.Dispose(); } catch { }
            _wedgeWatchdog = null;
            try { _heartbeatTimer?.Stop(); } catch { }
            _heartbeatTimer = null;
        }

        /// <summary>
        /// Runs on a threadpool thread every ~3s. If a video is playing but the UI thread stopped
        /// heart-beating for <see cref="WedgeStallMs"/>, the dispatcher is wedged (frozen final frame,
        /// topmost window holding the screen). Break it off-thread and queue a teardown so the app
        /// recovers without a hard shutdown. Fires at most once per playback.
        ///
        /// PRE-ROLL is observe-only: see <see cref="_playbackStarted"/>. Retiring LibVLC while the
        /// UI thread is still walking PlayVideo's prologue towards EnsureLibVLCInitialized races the
        /// background rebuild on _libVLCLock and takes the process down natively (#750-#753).
        /// </summary>
        private void WedgeWatchdogTick()
        {
            try
            {
                if (!_videoPlaying || _wedgeRescueFired) return;

                var last = System.Threading.Interlocked.Read(ref _uiHeartbeatTicks);
                var stallMs = (DateTime.UtcNow.Ticks - last) / TimeSpan.TicksPerMillisecond;
                if (stallMs < WedgeStallMs) return;

                bool live = _playbackStarted;
                if (!live) lock (_mediaPlayersLock) { live = _mediaPlayers.Count > 0; }
                if (!live)
                {
                    // The UI thread IS stalled, but there is nothing on screen to rescue yet.
                    // Record it (once) and let the next tick reconsider — whatever the prologue is
                    // blocked on will either finish, or the app will die where the trace points.
                    if (!_preRollStallLogged)
                    {
                        _preRollStallLogged = true;
                        App.Logger?.Warning("VideoService: UI thread stalled {StallMs}ms during video PRE-ROLL — no rescue (nothing on screen yet)", stallMs);
                        VideoDiag.Log("WEDGE", $"UI thread stalled {stallMs}ms during PRE-ROLL - observing only, no LibVLC retire");
                    }
                    return;
                }

                _wedgeRescueFired = true;
                App.Logger?.Error(
                    "VideoService: UI thread wedged {StallMs}ms during video playback — off-thread rescue (freeze/lockout guard)",
                    stallMs);
                // This is THE line the five v6.5.0 reports need (#616/#617/#621/#622/#623): it dates
                // the freeze to the second and proves the rescue ran. Everything below is logged
                // step by step because the rescue itself can be what hangs (a native Stop() that
                // never returns), and we currently cannot tell those two cases apart.
                VideoDiag.Log("WEDGE", $"UI THREAD WEDGED {stallMs}ms during playback - starting off-thread rescue");

                // Stop native players off-thread. LibVLC Stop() is thread-safe (CloseAll already calls
                // it from Task.Run) and can unblock a stuck decode/render that's holding the UI thread.
                // Harmless when the list is empty (e.g. the wedge is mid window-recreate before the new
                // player is registered — the posted teardown below still recovers it once unblocked).
                List<LibVLCSharp.Shared.MediaPlayer> players;
                lock (_mediaPlayersLock) { players = _mediaPlayers.ToList(); }
                VideoDiag.Log("WEDGE", $"stopping {players.Count} native player(s) off-thread");
                for (int i = 0; i < players.Count; i++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        players[i].Stop();
                        VideoDiag.Log("WEDGE", $"player[{i}].Stop returned after {sw.ElapsedMilliseconds}ms");
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Wedge rescue: player.Stop failed - {Error}", ex.Message);
                        VideoDiag.Log("WEDGE", $"player[{i}].Stop threw after {sw.ElapsedMilliseconds}ms: {ex.Message}");
                    }
                }

                // A wedge mid-playback means the shared instance's native state is suspect - the #559
                // pattern is precisely "one wedge, then every later video white-screens". Retire it so
                // the next video starts on a clean instance (the retired one is rooted, never disposed).
                RetireSharedLibVLC(_libVLC, "UI-thread wedge rescue during playback");

                // Queue a real teardown + reschedule for the instant the dispatcher drains again, so a
                // multi-minute lockout collapses to a brief stutter instead of stranding the frozen
                // frame until the user kills the app or the next scheduled video jolts it loose.
                // Generation-guarded: if some other teardown already ran by then (e.g. the vout heal
                // tore down and started its retry video), this must not kill the newcomer.
                var gen = _teardownGeneration;
                try
                {
                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher != null && !dispatcher.HasShutdownStarted)
                    {
                        VideoDiag.Log("WEDGE", "posting rescue teardown to the dispatcher");
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            // If this line never appears in a report's trace, the dispatcher NEVER
                            // drained again — i.e. the rescue's off-thread Stop() did not break the
                            // wedge and only a process kill / hard reset could end it.
                            VideoDiag.Log("WEDGE", $"rescue teardown running (dispatcher drained again after {VideoDiag.UiStallMs}ms)");
                            if (_teardownGeneration != gen) return;
                            try { ForceCleanup(); }
                            catch (Exception ex) { App.Logger?.Warning(ex, "Wedge rescue: ForceCleanup threw"); }
                            if (_isRunning) ScheduleNext();
                            VideoDiag.Log("WEDGE", "rescue teardown complete");
                        }));
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("Wedge rescue: dispatch failed - {Error}", ex.Message); }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("WedgeWatchdogTick error: {Error}", ex.Message);
            }
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Credit the minutes actually watched of the current video toward stats/quests, then reset for
        /// the next video. Position-based (from LibVLC TimeChanged) and watermarked via
        /// <see cref="_creditedWatchSeconds"/> so repeated teardown calls can't double-count, and an
        /// interrupted video still credits what was watched (#447). Never throws.
        /// </summary>
        private void FinalizeWatchCredit()
        {
            try
            {
                double watchedSec = _lastWatchPositionMs / 1000.0;
                double uncredited = watchedSec - _creditedWatchSeconds;
                if (uncredited >= 1.0)
                {
                    _creditedWatchSeconds = watchedSec;
                    App.Achievements?.TrackVideoWatched(uncredited);
                    // Session telemetry: report the full watched seconds + clip length once per
                    // video so downstream consumers can tally watch-time and detect skips.
                    try
                    {
                        VideoWatchCredited?.Invoke(this, new VideoWatchInfoEventArgs
                        {
                            WatchedSec = watchedSec,
                            DurationSec = _duration,
                            EndedNaturally = _duration > 0 && watchedSec >= _duration * 0.90,
                        });
                    }
                    catch (Exception ex) { App.Logger?.Debug("VideoWatchCredited handler error: {Error}", ex.Message); }
                }
            }
            catch (Exception ex) { App.Logger?.Debug("FinalizeWatchCredit error: {Error}", ex.Message); }
            finally
            {
                _lastWatchPositionMs = 0;
                _creditedWatchSeconds = 0;
            }
        }

        private void CloseAll(bool synchronous = false)
        {
            // Use lock to prevent race conditions between multiple cleanup triggers
            // (panic key, EndReached, safety timer, etc.)
            // #616/#617/#621/#622/#623: everything below runs ON THE UI THREAD and blocks it for
            // hundreds of ms in the good case and up to ~4.9s when a native Stop() is slow (a
            // non-pumping Task.WaitAll(500ms), then a pumping wait of up to 4s, then 300ms, then
            // 100ms). While the dispatcher is inside those waits the app is unresponsive by
            // DESIGN — and a WH_KEYBOARD_LL hook that the OS can't call back within
            // LowLevelHooksTimeout is silently dropped by Windows, which is a mechanism for
            // "the panic key did nothing". Every phase is timestamped so the next report tells us
            // which phase the teardown died in instead of just going quiet.
            var closeSw = System.Diagnostics.Stopwatch.StartNew();
            VideoDiag.Log("CLOSE", $"CloseAll begin (synchronous={synchronous}, windows={_windows.Count})");
            lock (_cleanupLock)
            {
                if (_isCleaningUp)
                {
                    App.Logger?.Debug("CloseAll: Already cleaning up, skipping duplicate call");
                    VideoDiag.Log("CLOSE", "CloseAll skipped - already cleaning up");
                    return;
                }
                _isCleaningUp = true;
                // Make CloseAll self-sufficient: clear the "playing" flag here rather than
                // relying on every caller to do it first. This guarantees the strict
                // Closing veto (SetupStrictHandlers) can never block the window teardown
                // below, even if a caller forgot to clear it.
                _videoPlaying = false;
                _playbackStarted = false;
                // Invalidate any in-flight watchdog continuation (see _teardownGeneration).
                System.Threading.Interlocked.Increment(ref _teardownGeneration);
            }

            // Credit the minutes actually watched, on EVERY teardown (natural end, manual stop, panic,
            // safety timeout, attention-fail retry) — not just OnEnded. Position-based + watermarked so it
            // can't double-count, so an interrupted video still counts toward the video quest (#447).
            FinalizeWatchCredit();

            try
            {
                _attentionTimer?.Stop();
                _segmentArmedAtUtc = DateTime.MinValue;   // random-segment mode is one-shot per video

                lock (_targets)
                {
                    App.Logger?.Information("ATTENTION: CloseAll() called - destroying {Count} targets", _targets.Count);
                    foreach (var t in _targets.ToList()) t.Destroy();
                    _targets.Clear();
                }

                App.Logger?.Debug("CloseAll: Closing {Count} video windows, {MsgCount} message windows",
                    _windows.Count, _messageWindows.Count);

                // CRITICAL: Stop all LibVLC media players FIRST before detaching from VideoViews
                // If we detach while player is still rendering, it can crash (especially multi-monitor)
                List<LibVLCSharp.Shared.MediaPlayer> playersCopy;
                lock (_mediaPlayersLock)
                {
                    playersCopy = _mediaPlayers.ToList();
                    _mediaPlayers.Clear();
                }

                // Drop primary refs before tearing players down so any concurrent
                // engine read sees null rather than a half-disposed handle.
                _primaryMediaPlayer = null;
                _primaryVideoWindow = null;
                // The enhancement is torn down with the video; clear the driving flag so a later
                // non-enhanced video gets the normal duration guillotine even if the bridge's
                // Unbind races the teardown (panic/CloseAll doesn't raise VideoEnded). (#536)
                _enhancementDriving = false;

                // Stop all players in parallel with timeout to prevent one hanging player from blocking
                // others. Keep the player↔task pairing: a player whose Stop() never completes is WEDGED
                // and must be quarantined at disposal time, not disposed (see _nativeQuarantine).
                var stopPairs = playersCopy.Select(player => (player, task: Task.Run(() =>
                {
                    try
                    {
                        player.Stop();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("CloseAll: Failed to stop LibVLC player - {Error}", ex.Message);
                    }
                }))).ToList();
                // Snapshot the instance these players belong to, so the (possibly delayed) retire below
                // can never condemn a FRESH instance that a vout-retry has since built.
                var owningLibVLC = _libVLC;

                if (stopPairs.Count > 0)
                {
                    var stopTasks = stopPairs.Select(p => p.task).ToArray();

                    // Wait for all players to stop with timeout (500ms should be plenty).
                    // This wait does NOT pump: the dispatcher is hard-blocked for up to 500ms here.
                    VideoDiag.Log("CLOSE", $"waiting for {stopTasks.Length} player Stop() task(s) (500ms, non-pumping)");
                    var allStopped = Task.WaitAll(stopTasks, TimeSpan.FromMilliseconds(500));
                    VideoDiag.Log("CLOSE", $"player Stop() wait returned allStopped={allStopped} at +{closeSw.ElapsedMilliseconds}ms");
                    if (!allStopped)
                    {
                        App.Logger?.Warning("CloseAll: Some LibVLC players did not stop within timeout");
                        // Detaching a VideoView from a STILL-RENDERING player is the multi-monitor
                        // crash/freeze path (2026-06-10: the chaos 15s cap stops players mid-decode,
                        // which regularly outruns 500ms with two of them). Pump-wait up to 4s more
                        // for the stragglers before touching the views — a slow close beats a
                        // wedged render thread.
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        while (sw.ElapsedMilliseconds < 4000 && stopTasks.Any(t => !t.IsCompleted))
                            WaitWithMessagePump(150);
                        if (stopTasks.Any(t => !t.IsCompleted))
                        {
                            App.Logger?.Error("CloseAll: LibVLC player STILL stopping after extended wait — detaching anyway");
                            VideoDiag.Log("CLOSE", $"player Stop() WEDGED past the {sw.ElapsedMilliseconds}ms extended wait - detaching anyway (poisoning signature)");
                            // A Stop() that outlives a 4.5s wait is the poisoning signature: retire the
                            // owning instance NOW so the very next video (which may start within seconds)
                            // gets a clean one instead of inheriting the corrupted native state (#559).
                            RetireSharedLibVLC(owningLibVLC, "player Stop() wedged past extended wait");
                        }
                        else
                        {
                            App.Logger?.Information("CloseAll: stragglers stopped after extended wait ({Ms}ms)", sw.ElapsedMilliseconds);
                            VideoDiag.Log("CLOSE", $"stragglers stopped after {sw.ElapsedMilliseconds}ms of pumped waiting");
                        }
                    }
                }

                // CRITICAL: Wait a bit after stopping players to let LibVLC finish any pending operations
                // This prevents crashes when detaching VideoView while LibVLC is still processing
                // Use message-pump-aware wait to prevent deadlock (LibVLC threads may need UI thread)
                if (playersCopy.Count > 0)
                {
                    WaitWithMessagePump(300);
                }

                // Now detach MediaPlayers from VideoViews (safe since players are stopped and we waited).
                // Detaching an HwndHost surface from a player that is still presenting is the
                // historical multi-monitor freeze; if the trace stops here, that is what happened.
                VideoDiag.Log("CLOSE", $"detaching VideoViews at +{closeSw.ElapsedMilliseconds}ms");
                var windowsCopy = _windows.ToList();
                foreach (var w in windowsCopy)
                {
                    try
                    {
                        if (w.Content is Grid g)
                        {
                            // Find VideoView in the grid (might not be at index 0 due to overlays)
                            foreach (var child in g.Children)
                            {
                                if (child is VideoView vv)
                                {
                                    vv.MediaPlayer = null; // Detach after stopping
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("CloseAll: Error detaching VideoView - {Error}", ex.Message);
                    }
                }

                // Another small delay after detaching before closing windows
                // Use message-pump-aware wait to prevent deadlock with LibVLC
                if (windowsCopy.Count > 0)
                {
                    WaitWithMessagePump(100);
                }

                // Close video windows AFTER media players are stopped and detached
                VideoDiag.Log("CLOSE", $"closing {_windows.Count} video window(s) at +{closeSw.ElapsedMilliseconds}ms");
                foreach (var w in _windows.ToList())
                {
                    try
                    {
                        // Stop any MediaElement
                        if (w.Content is Grid g)
                        {
                            foreach (var child in g.Children)
                            {
                                if (child is MediaElement me)
                                {
                                    me.Stop();
                                    me.Source = null; // Release media resources
                                    break;
                                }
                            }
                        }
                        w.Close();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("CloseAll: Failed to close video window - {Error}", ex.Message);
                    }
                }
                _windows.Clear();

                // Tear down any blurred-background memory-render surfaces: invalidate the frame
                // buffer + unhook the render tick now (the players above are already stopped), then
                // free the native buffer after a delay so an in-flight frame can't touch freed memory.
                if (_blurSurfaces.Count > 0)
                {
                    foreach (var s in _blurSurfaces.ToList())
                    {
                        try { s.Dispose(); }
                        catch (Exception ex) { App.Logger?.Debug("CloseAll: blur surface dispose failed - {Error}", ex.Message); }
                    }
                    _blurSurfaces.Clear();
                }

                if (!synchronous)
                    App.Overlay?.NotifyTopWindowClosed();

                // Dispose media players - synchronously during app exit, async during normal operation.
                // NEVER dispose a player whose Stop() hasn't completed: disposing a wedged player is
                // the exact step that poisons the shared LibVLC instance and turns one bad teardown
                // into "every video after this is a white screen" (#559). Wedged players are rooted in
                // quarantine instead, and their owning instance is retired.
                if (stopPairs.Count > 0)
                {
                    if (synchronous)
                    {
                        // Synchronous disposal - used during app exit to prevent orphaned windows
                        App.Logger?.Debug("CloseAll: Synchronous disposal of {Count} LibVLC players", stopPairs.Count);
                        foreach (var (player, task) in stopPairs)
                        {
                            if (!task.IsCompleted)
                            {
                                // App is exiting anyway; a blocking Dispose here would wedge shutdown.
                                QuarantineNative(player, "Stop() still wedged at app exit");
                                continue;
                            }
                            try
                            {
                                player.Dispose();
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Debug("CloseAll: Failed to dispose LibVLC player - {Error}", ex.Message);
                            }
                        }
                    }
                    else
                    {
                        // Async disposal - normal operation to prevent blocking UI
                        Task.Run(async () =>
                        {
                            // Wait for any pending EndReached events to complete their Task.Run dispatch
                            await Task.Delay(1000);

                            foreach (var (player, task) in stopPairs)
                            {
                                if (!task.IsCompleted)
                                {
                                    QuarantineNative(player, "Stop() never completed");
                                    RetireSharedLibVLC(owningLibVLC, "a wedged player was quarantined");
                                    continue;
                                }
                                try
                                {
                                    player.Dispose();
                                }
                                catch (Exception ex)
                                {
                                    App.Logger?.Debug("CloseAll: Failed to dispose LibVLC player - {Error}", ex.Message);
                                }
                            }
                        });
                    }
                }

                // Also close any lingering message windows
                CloseMessageWindows();
            }
            finally
            {
                // Video is torn down — the wedge and vout watchdogs have nothing left to guard.
                StopWedgeWatchdog();
                StopVoutWatchdog();

                // Release the audio-duck ref taken in PlayVideo, exactly once per teardown. Balancing
                // it here (not only in Cleanup) covers the paths that tear down WITHOUT Cleanup — the
                // attention retry / troll loop (ShowMessage → CloseAll → next PlayVideo) and engine
                // Stop() — so the ref count returns to 0 and other apps' volume is restored (#526).
                if (_didDuck)
                {
                    _didDuck = false;
                    try { App.Audio?.Unduck(); }
                    catch (Exception ex) { App.Logger?.Debug("CloseAll: Unduck failed - {Error}", ex.Message); }
                }

                lock (_cleanupLock)
                {
                    _isCleaningUp = false;
                }

                // Total UI-thread block for this teardown. Anything past ~1s here is the app being
                // unresponsive to the user (and to the low-level keyboard hook) — #616-#623.
                VideoDiag.Log("CLOSE", $"CloseAll end after {closeSw.ElapsedMilliseconds}ms of UI-thread time");
            }
        }

        /// <summary>
        /// Waits for a specified number of milliseconds while continuing to pump WPF messages.
        /// This prevents deadlocks when LibVLC threads need to dispatch to the UI thread during cleanup.
        /// MUST be called from UI thread.
        /// </summary>
        private static void WaitWithMessagePump(int milliseconds)
        {
            var endTime = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < endTime)
            {
                try
                {
                    // Process pending WPF messages at Background priority
                    // This allows LibVLC callbacks to complete without deadlock
                    Application.Current?.Dispatcher?.Invoke(
                        DispatcherPriority.Background,
                        new Action(() => { }));
                }
                catch
                {
                    // Dispatcher may be gone during shutdown
                    return;
                }
                Thread.Sleep(10); // Small sleep between message pumps to avoid busy-wait
            }
        }

        private void Cleanup()
        {
            App.Logger?.Information("VideoService: Cleanup() called, _videoPlaying={Playing}, _windows={WinCount}",
                _videoPlaying, _windows.Count);
            VideoDiag.Log("VIDEO", $"END (natural-end path) - playing={_videoPlaying}, windows={_windows.Count}");

            _safetyTimer?.Stop();
            _fallbackSafetyTimer?.Stop();
            _fallbackSafetyTimer = null;
            _maxLenCapTimer?.Stop();
            _maxLenCapTimer = null;
            _videoPlaying = false;
            _triggerInProgress = false;
            CloseAll();

            App.Logger?.Information("VideoService: Cleanup() - CloseAll completed, _windows now={WinCount}", _windows.Count);
            // Audio unduck now happens inside CloseAll (above) so every teardown path releases the
            // duck ref exactly once (#526). No Unduck here — a second call would decrement a duck ref
            // that another consumer (subliminal/session) may legitimately hold.
            _strictActive = false;
            CancelPendingRetry();
            _penalties = 0;

            // Resume bubbles now that video is done
            App.Bubbles?.Resume();

            // Trigger deferred Bambi Reset now that video has ended
            App.Subliminal?.TriggerDeferredBambiReset();

            // Stop haptic background vibe
            _ = App.Haptics?.StopVideoBackgroundVibeAsync();

            // Notify InteractionQueue that video is complete (triggers queued items)
            App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.Video);

            VideoEnded?.Invoke(this, EventArgs.Empty);

            if (_isRunning && App.Settings.Current.FlashEnabled)
            {
                App.Flash?.Start();
                // Discord presence will be updated by FlashService.Start()
            }
            else
            {
                // Update Discord presence back to idle
                App.DiscordRpc?.SetIdleActivity();
            }

            if (_isRunning)
                ScheduleNext();
        }

        #endregion

        private string? GetNextVideo()
        {
            // Refill queues if both are empty
            if (_videoQueue.Count == 0 && _packVideoQueue.Count == 0)
            {
                RefillVideoQueues();
            }

            // If both queues are empty after refill, no videos available
            if (_videoQueue.Count == 0 && _packVideoQueue.Count == 0)
            {
                return null;
            }

            // Randomly choose between regular and pack videos based on what's available
            bool usePackVideo = false;
            if (_videoQueue.Count > 0 && _packVideoQueue.Count > 0)
            {
                // Both available - pick randomly weighted by count
                var totalCount = _videoQueue.Count + _packVideoQueue.Count;
                usePackVideo = _random.Next(totalCount) >= _videoQueue.Count;
            }
            else if (_packVideoQueue.Count > 0)
            {
                usePackVideo = true;
            }

            if (usePackVideo && _packVideoQueue.Count > 0)
            {
                var packVideo = _packVideoQueue.Dequeue();
                // Decrypt pack video to temp file
                var tempPath = App.ContentPacks?.GetPackFileTempPath(packVideo.PackId, packVideo.File);
                if (!string.IsNullOrEmpty(tempPath))
                {
                    _tempPackFiles.Add(tempPath);  // Track for cleanup
                    App.Logger?.Debug("Using pack video: {Name} from pack {PackId}", packVideo.File.OriginalName, packVideo.PackId);
                    return tempPath;
                }
                // If decryption failed, try regular queue
            }

            return _videoQueue.Count > 0 ? _videoQueue.Dequeue() : null;
        }

        /// <summary>
        /// Refills both video queues (regular and pack videos).
        /// </summary>
        private void RefillVideoQueues()
        {
            var validExtensions = new[] { ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm" };

            // Clean up old temp pack files
            CleanupTempPackFiles();

            App.Logger?.Debug("VideoService: Scanning for videos in {Path}", _videosPath);

            // Load regular videos
            var files = new List<string>();
            if (Directory.Exists(_videosPath))
            {
                // Scan subfolders to support user-organized categories
                var allFiles = Directory.GetFiles(_videosPath, "*.*", SearchOption.AllDirectories);
                App.Logger?.Debug("VideoService: Found {Count} total files in videos folder", allFiles.Length);

                foreach (var file in allFiles)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (!validExtensions.Contains(ext))
                    {
                        App.Logger?.Debug("VideoService: Skipping non-video file: {Path} (ext: {Ext})", file, ext);
                        continue;
                    }

                    // Security: Validate path is within allowed directories (app dir, user assets, or custom path)
                    var isInAppDir = SecurityHelper.IsPathSafe(file, AppDomain.CurrentDomain.BaseDirectory);
                    var isInUserAssets = SecurityHelper.IsPathSafe(file, App.UserDataPath);
                    var isInCustomPath = SecurityHelper.IsPathSafe(file, App.EffectiveAssetsPath);

                    if (!isInAppDir && !isInUserAssets && !isInCustomPath)
                    {
                        App.Logger?.Warning("Blocked video outside allowed directory: {Path} (AppDir={AppDir}, UserData={UserData}, Custom={Custom})",
                            file, AppDomain.CurrentDomain.BaseDirectory, App.UserDataPath, App.EffectiveAssetsPath);
                        continue;
                    }

                    // Security: Sanitize filename
                    var fileName = SecurityHelper.SanitizeFilename(Path.GetFileName(file));
                    if (string.IsNullOrEmpty(fileName))
                    {
                        App.Logger?.Warning("VideoService: Sanitized filename empty for: {Path}", file);
                        continue;
                    }

                    files.Add(file);
                }
            }
            else
            {
                App.Logger?.Warning("VideoService: Videos directory does not exist: {Path}", _videosPath);
            }

            App.Logger?.Debug("VideoService: {Count} videos passed security checks", files.Count);

            // Filter out disabled assets (blacklist approach).
            // Normalize for case-insensitive, separator-agnostic comparison so saved
            // entries don't slip past on Windows (case) or path-style mismatch.
            if (App.Settings?.Current?.DisabledAssetPaths.Count > 0)
            {
                var beforeCount = files.Count;
                var basePath = App.EffectiveAssetsPath;
                static string Norm(string p) => p.Replace('\\', '/');
                var disabled = new HashSet<string>(
                    App.Settings.Current.DisabledAssetPaths.Select(Norm),
                    StringComparer.OrdinalIgnoreCase);
                files = files.Where(f =>
                {
                    var relativePath = Norm(Path.GetRelativePath(basePath, f));
                    var isDisabled = disabled.Contains(relativePath);
                    if (isDisabled)
                    {
                        App.Logger?.Debug("VideoService: Video disabled by user: {Path}", relativePath);
                    }
                    return !isDisabled;
                }).ToList();
                App.Logger?.Debug("VideoService: {Before} -> {After} after disabled filter", beforeCount, files.Count);
            }

            // Duration filter (Phase 5). Best-effort: videos with no cached
            // duration are included and parsed lazily — they'll get filtered
            // correctly on the next refill once the cache is warm. We never
            // block playback on a cache miss, so cold runs aren't penalized.
            var minSec = App.Settings?.Current?.VideoMinDurationSeconds ?? 0;
            var maxSec = App.Settings?.Current?.VideoMaxDurationSeconds ?? 0;
            if ((minSec > 0 || maxSec > 0) && MetadataCache != null)
            {
                var beforeDur = files.Count;
                files = files.Where(f =>
                {
                    var dur = MetadataCache.TryGetDuration(f);
                    if (dur == null)
                    {
                        // Kick off a background parse so the next refill has the data.
                        // Don't await — we let this video through this cycle.
                        _ = MetadataCache.GetOrComputeDurationAsync(f);
                        return true;
                    }
                    if (minSec > 0 && dur.Value < minSec) return false;
                    if (maxSec > 0 && dur.Value > maxSec) return false;
                    return true;
                }).ToList();
                App.Logger?.Debug("VideoService: {Before} -> {After} after duration filter [{Min}s, {Max}s]",
                    beforeDur, files.Count, minSec, maxSec);
            }

            // Shuffle using Fisher-Yates algorithm for reliable randomization
            ShuffleList(files);
            _videoQueue = new Queue<string>(files);

            // Load pack videos from active packs
            var packVideos = App.ContentPacks?.GetAllActivePackVideos() ?? new List<(string, PackFileEntry)>();

            // Log which packs the videos are coming from
            var packVideosByPack = packVideos.GroupBy(v => v.PackId).ToList();
            foreach (var group in packVideosByPack)
            {
                App.Logger?.Information("VideoService: Pack '{PackId}' contributing {Count} videos", group.Key, group.Count());
            }

            // Shuffle pack videos using Fisher-Yates for reliable randomization
            var packVideosList = packVideos.ToList();
            ShuffleList(packVideosList);
            _packVideoQueue = new Queue<(string, PackFileEntry)>(packVideosList);

            App.Logger?.Information("VideoService: Queues refilled - {RegularCount} regular videos, {PackCount} pack videos (path: {Path})",
                _videoQueue.Count, _packVideoQueue.Count, _videosPath);
        }

        /// <summary>
        /// Cleans up temporary pack video files.
        /// </summary>
        /// <summary>
        /// Fisher-Yates shuffle for reliable randomization.
        /// </summary>
        private void ShuffleList<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private void CleanupTempPackFiles()
        {
            foreach (var tempFile in _tempPackFiles)
            {
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to delete temp pack file: {Error}", ex.Message);
                }
            }
            _tempPackFiles.Clear();
        }

        #region LibVLC Child Window Input Blocking

        [DllImport("user32.dll")]
        private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;

        /// <summary>
        /// Pins a fullscreen video window to the true physical bounds of its target monitor.
        ///
        /// The app is PerMonitorV2-DPI-aware (app.manifest). A WPF Window's Left/Top/Width/Height
        /// set before it's shown are realized in the DPI context of the monitor where the HWND is
        /// first created (the primary), so when the window lands on a *secondary* monitor whose
        /// scaling differs, the DIP→physical math is wrong and the window covers only part of that
        /// screen (mandatory video showing left-aligned at ~half width on the second monitor).
        ///
        /// SetWindowPos operates in real screen pixels and is immune to that per-monitor rescaling,
        /// so we force the exact monitor rect. We apply twice: once on SourceInitialized (HWND now
        /// exists, before the window is visible) and once on Loaded — moving onto a different-DPI
        /// monitor fires WM_DPICHANGED, which WPF may answer by resizing the window back toward the
        /// creation-DPI size, so the second pass re-pins it after layout settles. On uniform-DPI
        /// setups both passes set the rect the window already has (harmless no-op).
        /// </summary>
        private void ForceFullScreenBounds(Window win, Screen screen)
        {
            void Apply()
            {
                try
                {
                    var hwnd = new WindowInteropHelper(win).Handle;
                    if (hwnd == IntPtr.Zero) return;
                    var b = screen.Bounds; // physical pixels
                    SetWindowPos(hwnd, IntPtr.Zero, b.X, b.Y, b.Width, b.Height,
                        SWP_NOZORDER | SWP_NOACTIVATE);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("VideoService.ForceFullScreenBounds failed: {Error}", ex.Message);
                }
            }

            win.SourceInitialized += (_, _) => Apply();
            win.Loaded += (_, _) => Apply();
        }

        /// <summary>
        /// Marks a fullscreen video window as WS_EX_NOACTIVATE so Windows will never make it the
        /// foreground/active window. Used ONLY for videos that fire mid-chaos-run: such a window can
        /// never raise itself to the top of the topmost band by being clicked, or when the current
        /// foreground window (a flashing subliminal, an expiring attention target, the bubble the
        /// player just popped) disappears and Windows hands focus to the next topmost window. Without
        /// this the video kept stealing z-order above the chaos bubbles and the reactive ~1s re-raise
        /// only pulled them back a second later — the visible "video flickers over the elements, then
        /// they come back, repeat". The window still renders and still receives WPF mouse events via
        /// its click overlay (NOACTIVATE blocks activation, not messages); the chaos HUD owns the
        /// keyboard during a run, so the video forgoing focus costs nothing. Idempotent.
        /// </summary>
        private void MakeNonActivating(Window win)
        {
            try
            {
                var hwnd = new WindowInteropHelper(win).Handle;
                if (hwnd == IntPtr.Zero) return;
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                if ((ex & WS_EX_NOACTIVATE) == 0)
                    SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MakeNonActivating failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Stops a fullscreen video window from being re-raised in the topmost z-band when the user
        /// clicks it (e.g. missing a bubble and hitting the video behind it). WS_EX_NOACTIVATE alone
        /// (MakeNonActivating) only blocks focus-driven raises; Windows still brings a topmost window
        /// to the top of its band on mouse-down. We answer WM_MOUSEACTIVATE with MA_NOACTIVATE, which
        /// denies the click-activation (and the z-order raise that rides on it) while KEEPING the
        /// mouse message — so the WPF click overlay + attention targets still register — and WITHOUT
        /// dropping existing focus, so ESC / panic-key on non-chaos videos keep working. This is the
        /// pre-emptive counterpart to the reactive BringTargetsToFront lift. Idempotent per window.
        /// </summary>
        private void PreventClickRaise(Window win)
        {
            try
            {
                void Wire()
                {
                    try
                    {
                        var hwnd = new WindowInteropHelper(win).Handle;
                        if (hwnd == IntPtr.Zero) return;
                        HwndSource.FromHwnd(hwnd)?.AddHook(NoClickRaiseHook);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("PreventClickRaise wire failed: {Error}", ex.Message);
                    }
                }

                if (new WindowInteropHelper(win).Handle != IntPtr.Zero)
                    Wire();
                else
                    win.SourceInitialized += (_, _) => Wire();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("PreventClickRaise failed: {Error}", ex.Message);
            }
        }

        private static IntPtr NoClickRaiseHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(MA_NOACTIVATE);
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Disables input on LibVLC's native child HWNDs to prevent mouse/keyboard events
        /// from reaching the native renderer (WPF airspace limitation).
        /// Scheduled with a short delay to allow VideoView to fully create its native window.
        /// </summary>
        private void DisableChildWindowInput(Window win)
        {
            Task.Delay(300).ContinueWith(_ =>
            {
                try
                {
                    DispatcherHelper.RunOnUI(() =>
                    {
                        try
                        {
                            if (!win.IsLoaded) return;
                            var hwndSource = PresentationSource.FromVisual(win) as HwndSource;
                            if (hwndSource == null) return;

                            var parentHwnd = hwndSource.Handle;
                            EnumChildWindows(parentHwnd, (childHwnd, _) =>
                            {
                                EnableWindow(childHwnd, false);
                                return true; // continue enumeration
                            }, IntPtr.Zero);
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Debug("DisableChildWindowInput: Failed - {Error}", ex.Message);
                        }
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("DisableChildWindowInput: Dispatch failed - {Error}", ex.Message);
                }
            });
        }

        #endregion

        public void Dispose()
        {
            Stop();
            CleanupTempPackFiles();
        }
    }

    /// <summary>
    /// Bouncing text target - customizable via settings
    /// </summary>
    internal class FloatingText
    {
        // Win32 for reliable z-order management and tool window style
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;

        private readonly Window _win;
        private readonly DispatcherTimer _timer;
        private double _x, _y, _vx, _vy;
        private readonly double _minX, _maxX, _minY, _maxY;
        private bool _dead;
        private IntPtr _hwnd;
        private int _tickCount;  // For periodic z-order refresh

        // Stored for programmatic invocation (gaze-click). The mouse click
        // handler also routes through Hit() so the click and gaze paths
        // share identical bookkeeping (idempotency, sound, callback, fade).
        private readonly Action _onHit;
        private bool _clicked;

        public FloatingText(string text, Screen screen, int size, Action onHit)
        {
            _onHit = onHit;
            try
            {
                size = Math.Max(40, size);

                // Format multi-word triggers: 2 words = 2 lines, 4+ words = 2 lines with 2 on each
                text = FormatTriggerText(text);

                // Get DPI scale factor (Screen uses physical pixels, WPF uses DIPs)
                double dpiScale = 1.0;
                try
                {
                    var source = PresentationSource.FromVisual(Application.Current.MainWindow);
                    if (source?.CompositionTarget != null)
                    {
                        dpiScale = source.CompositionTarget.TransformToDevice.M11;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Could not get DPI scale for attention target: {Error}", ex.Message);
                }

                // Use WorkingArea (excludes taskbar) with generous margins
                // Convert physical pixels to WPF DIPs
                var area = screen.WorkingArea;
                double areaX = area.X / dpiScale;
                double areaY = area.Y / dpiScale;
                double areaWidth = area.Width / dpiScale;
                double areaHeight = area.Height / dpiScale;

                // Margins scale with screen size to handle different resolutions
                var marginX = Math.Min(150, areaWidth * 0.08);
                var marginY = Math.Min(100, areaHeight * 0.08);
                _minX = areaX + marginX;
                _minY = areaY + marginY;
                _maxX = areaX + areaWidth - marginX;
                _maxY = areaY + areaHeight - marginY;

                // Load style settings
                var settings = App.Settings.Current;
                Color color1, color2, textColor, borderColor;
                try
                {
                    color1 = (Color)ColorConverter.ConvertFromString(settings.AttentionColor1);
                    color2 = (Color)ColorConverter.ConvertFromString(settings.AttentionColor2);
                    textColor = (Color)ColorConverter.ConvertFromString(settings.AttentionTextColor);
                    borderColor = (Color)ColorConverter.ConvertFromString(settings.AttentionBorderColor);
                }
                catch
                {
                    // Fallback to bright fluo pink if colors invalid
                    color1 = Color.FromRgb(255, 20, 147); // DeepPink
                    color2 = Color.FromRgb(255, 105, 180); // HotPink
                    textColor = Color.FromRgb(255, 20, 147); // DeepPink
                    borderColor = Color.FromRgb(255, 20, 147);
                }

                // Check if floating text mode (no background)
                var isFloating = settings.AttentionFloatingText;

                // Create container with customizable styling
                var border = new Border
                {
                    Background = isFloating
                        ? Brushes.Transparent
                        : new LinearGradientBrush(color1, color2, 90),
                    CornerRadius = isFloating ? new CornerRadius(0) : new CornerRadius(20),
                    BorderBrush = (settings.AttentionShowBorder && !isFloating)
                        ? new SolidColorBrush(borderColor)
                        : Brushes.Transparent,
                    BorderThickness = (settings.AttentionShowBorder && !isFloating)
                        ? new Thickness(3)
                        : new Thickness(0),
                    Padding = isFloating ? new Thickness(0) : new Thickness(20, 10, 20, 10),
                    Effect = isFloating ? null : new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 15,
                        ShadowDepth = 5,
                        Opacity = 0.6
                    },
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                // Create outlined text using geometry for crisp 2mm black outline
                var fontFamily = new FontFamily($"{settings.AttentionFont}, Segoe UI, Arial");
                var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

                // Create FormattedText to generate geometry
                var formattedText = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    typeface,
                    size,
                    Brushes.White, // Placeholder, we'll use geometry
                    VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);
                formattedText.TextAlignment = TextAlignment.Center;
                formattedText.LineHeight = size * 0.95;

                // Get text geometry for outline
                var textGeometry = formattedText.BuildGeometry(new System.Windows.Point(0, 0));

                // 2mm ≈ 7.5 pixels at 96 DPI
                const double outlineThickness = 7.5;

                // Get the actual bounds of the geometry and offset to ensure nothing is clipped
                var bounds = textGeometry.Bounds;
                double offsetX = -bounds.X + outlineThickness;
                double offsetY = -bounds.Y + outlineThickness;

                // Apply transform to offset the geometry so it starts within the container
                var transformedGeometry = textGeometry.Clone();
                transformedGeometry.Transform = new TranslateTransform(offsetX, offsetY);

                // Create path for outline (black stroke)
                var outlinePath = new System.Windows.Shapes.Path
                {
                    Data = transformedGeometry,
                    Stroke = Brushes.Black,
                    StrokeThickness = outlineThickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    Fill = Brushes.Transparent
                };

                // Create path for fill (text color)
                var fillPath = new System.Windows.Shapes.Path
                {
                    Data = transformedGeometry,
                    Fill = new SolidColorBrush(textColor),
                    Stroke = Brushes.Transparent
                };

                // Stack outline behind fill in a Grid
                var textContainer = new Grid
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                textContainer.Children.Add(outlinePath);
                textContainer.Children.Add(fillPath);

                border.Child = textContainer;

                // Measure the text to get proper sizing (use actual geometry bounds + outline thickness)
                double w = bounds.Width + outlineThickness * 2 + 60;  // Add padding + outline
                double h = bounds.Height + outlineThickness * 2 + 40;

                // Ensure minimum size
                w = Math.Max(w, 150);
                h = Math.Max(h, 60);

                // Create a container grid with an invisible hit zone
                // This ensures clicks register even on transparent pixels (inside "O", etc.)
                var container = new Grid();

                // Invisible hit zone rectangle - nearly transparent but still hit-testable
                var hitZone = new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), // Almost invisible but hit-testable
                    IsHitTestVisible = true
                };
                container.Children.Add(hitZone);
                container.Children.Add(border);

                _win = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Width = w,
                    Height = h,
                    Content = container,
                    ShowActivated = false  // Don't steal focus
                };

                // Random position - ensure window stays fully within bounds
                // Calculate spawn range: from minX to (maxX - windowWidth)
                var spawnRangeX = Math.Max(0, (_maxX - w) - _minX);
                var spawnRangeY = Math.Max(0, (_maxY - h) - _minY);
                _x = _minX + Random.Shared.NextDouble() * spawnRangeX;
                _y = _minY + Random.Shared.NextDouble() * spawnRangeY;
                // Clamp to ensure we're definitely within bounds
                _x = Math.Clamp(_x, _minX, Math.Max(_minX, _maxX - w));
                _y = Math.Clamp(_y, _minY, Math.Max(_minY, _maxY - h));
                _win.Left = _x;
                _win.Top = _y;

                // Random velocity (slightly faster for better visibility)
                var angle = Random.Shared.NextDouble() * Math.PI * 2;
                _vx = Math.Cos(angle) * 3.0;
                _vy = Math.Sin(angle) * 3.0;

                // Click = hit — routes through Hit() so the gaze-click path
                // shares the same idempotency + sound + callback + fade.
                _win.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;  // Prevent click from propagating to windows behind
                    Hit();
                };

                // Movement
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _timer.Tick += (s, e) =>
                {
                    if (_dead) return;
                    _x += _vx; _y += _vy;
                    if (_x < _minX) { _x = _minX; _vx = Math.Abs(_vx); }
                    if (_x + w > _maxX) { _x = _maxX - w; _vx = -Math.Abs(_vx); }
                    if (_y < _minY) { _y = _minY; _vy = Math.Abs(_vy); }
                    if (_y + h > _maxY) { _y = _maxY - h; _vy = -Math.Abs(_vy); }
                    _win.Left = _x;
                    _win.Top = _y;

                    // Periodically re-assert topmost z-order (every ~32ms = 2 ticks at 16ms)
                    // This ensures targets stay on top of subliminals and fullscreen video windows
                    _tickCount++;
                    if (_tickCount >= 2 && _hwnd != IntPtr.Zero)
                    {
                        _tickCount = 0;
                        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    }
                };

                // Set tool window style BEFORE window is shown (SourceInitialized fires after HWND created but before visible)
                _win.SourceInitialized += (s, e) =>
                {
                    _hwnd = new WindowInteropHelper(_win).Handle;
                    if (_hwnd != IntPtr.Zero)
                    {
                        // Set as tool window to hide from Alt+Tab - must be done before window is visible
                        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
                        exStyle |= WS_EX_TOOLWINDOW;  // Add tool window style
                        exStyle &= ~WS_EX_APPWINDOW;  // Remove app window style
                        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
                        App.Logger?.Debug("Attention target: Set WS_EX_TOOLWINDOW style on hwnd={Hwnd}", _hwnd);
                    }
                };

                _win.Loaded += (s, e) =>
                {
                    _timer.Start();
                    // Ensure hwnd is captured if not already
                    if (_hwnd == IntPtr.Zero)
                        _hwnd = new WindowInteropHelper(_win).Handle;

                    if (_hwnd != IntPtr.Zero)
                    {
                        // Ensure topmost via Win32
                        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    }
                    App.Logger?.Debug("Attention target window loaded at ({X}, {Y}), hwnd={Hwnd}", _x, _y, _hwnd);
                };

                // Track when window is closing (to debug unexpected closes)
                _win.Closing += (s, e) =>
                {
                    App.Logger?.Information("ATTENTION: Target window Closing event, _dead={Dead}", _dead);
                };

                // Prevent activation from stealing focus from other targets
                _win.Activated += (s, e) =>
                {
                    // Immediately bring all other topmost windows back to front
                    // This is handled by the VideoService's BringTargetsToFront
                };

                _win.Show();
                App.Logger?.Debug("Attention target window created: '{Text}' size {W}x{H}", text, w, h);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to create FloatingText window: {Error}", ex.Message);
                _timer = new DispatcherTimer(); // Prevent null reference
                _win = new Window { Visibility = Visibility.Collapsed }; // Dummy window
            }
        }

        private void PlayPopSound()
        {
            try
            {
                var soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "bubbles");
                var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
                var chosenPop = popFiles[Random.Shared.Next(popFiles.Length)];
                var popPath = Path.Combine(soundsPath, chosenPop);

                if (File.Exists(popPath))
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            using var audioFile = new AudioFileReader(popPath);
                            // Apply master volume to attention target pop sound
                            var masterVolume = App.Settings?.Current?.MasterVolume ?? 100;
                            audioFile.Volume = 0.6f * (masterVolume / 100f);
                            using var outputDevice = new WaveOutEvent();
                            App.Audio?.ApplyPreferredDevice(outputDevice);
                            outputDevice.Init(audioFile);
                            outputDevice.Play();
                            while (outputDevice.PlaybackState == PlaybackState.Playing)
                            {
                                Thread.Sleep(50);
                            }
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Debug("Pop sound playback failed: {Error}", ex.Message);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to start pop sound: {Error}", ex.Message);
            }
        }

        private void FadeOut()
        {
            App.Logger?.Debug("FloatingText.FadeOut() starting");
            _timer.Stop();
            var fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            fade.Tick += (s, e) =>
            {
                _win.Opacity -= 0.15;
                if (_win.Opacity <= 0.1) { fade.Stop(); Destroy(); }
            };
            fade.Start();
        }

        public void Destroy()
        {
            if (_dead) return;  // Already destroyed
            _dead = true;
            _timer.Stop();
            App.Logger?.Information("ATTENTION: Target destroyed (window closing)");
            try { _win.Close(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to close target window: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Programmatic equivalent of a mouse click on this target. Plays the
        /// pop sound, invokes the stored onHit callback, and fades the
        /// window. Idempotent — calling Hit() twice (mouse + gaze racing)
        /// only registers once. Used by VideoService.GazeClick for the
        /// stare-to-click path.
        /// </summary>
        public void Hit()
        {
            if (_clicked || _dead) return;
            _clicked = true;
            App.Logger?.Information("ATTENTION: Target clicked");
            PlayPopSound();
            try { _onHit?.Invoke(); }
            catch (Exception ex) { App.Logger?.Debug("FloatingText.Hit: onHit callback threw: {Error}", ex.Message); }
            FadeOut();
        }

        /// <summary>
        /// Current bounds in WPF DIPs for gaze hit-testing. Matches the
        /// coordinate space that WebcamTrackingService.OnGazeMove emits.
        /// Returns Rect.Empty when the window is dead or coordinates can't
        /// be read.
        /// </summary>
        public System.Windows.Rect GetGazeBounds()
        {
            if (_dead) return System.Windows.Rect.Empty;
            try { return new System.Windows.Rect(_x, _y, _win.Width, _win.Height); }
            catch { return System.Windows.Rect.Empty; }
        }

        public void BringToFront()
        {
            if (_dead)
            {
                App.Logger?.Information("ATTENTION: BringToFront skipped - target is dead");
                return;
            }
            if (_hwnd == IntPtr.Zero)
            {
                App.Logger?.Information("ATTENTION: BringToFront skipped - hwnd is zero");
                return;
            }
            try
            {
                // Use Win32 SetWindowPos for reliable z-order without focus stealing
                bool result = SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                App.Logger?.Information("ATTENTION: BringToFront hwnd={Hwnd}, success={Result}, visible={Visible}",
                    _hwnd, result, _win.IsVisible);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("ATTENTION: BringToFront failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Formats trigger text for display:
        /// - 2 words: stack vertically (one per line)
        /// - 4+ words: 2 lines with words split evenly
        /// - 1 word or 3 words: keep as-is
        /// </summary>
        private static string FormatTriggerText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 2)
            {
                // 2 words: stack vertically
                return $"{words[0]}\n{words[1]}";
            }
            else if (words.Length >= 4)
            {
                // 4+ words: split into 2 lines
                int mid = words.Length / 2;
                var line1 = string.Join(" ", words.Take(mid));
                var line2 = string.Join(" ", words.Skip(mid));
                return $"{line1}\n{line2}";
            }

            // 1 or 3 words: keep as-is
            return text;
        }
    }
}
