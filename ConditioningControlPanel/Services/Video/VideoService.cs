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
        private System.Threading.Timer? _voutWatchTimer;
        private volatile bool _voutSeen;
        private bool _voutRetryUsed;
        private const int VoutGraceMs = 8000;
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
        // Fire only after a long, unambiguous stall — this targets the multi-minute lockouts, never a
        // UI thread that's merely busy for a beat. The legitimate ~4s teardown pump-wait keeps the
        // heartbeat ticking (it pumps Background-priority work), so it won't trip this.
        private const int WedgeStallMs = 22000;

        // Watch-time crediting for stats/quests (#447). _lastWatchPositionMs tracks the current video's
        // latest playback position (LibVLC TimeChanged); _creditedWatchSeconds is a watermark of what's
        // already been credited so repeated teardown calls can't double-count.
        private double _lastWatchPositionMs;
        private double _creditedWatchSeconds;

        private bool _isRunning;
        private bool _videoPlaying;
        private bool _triggerInProgress; // Guards the 800ms freeze delay window in TriggerVideo
        private bool _strictActive;
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
        /// </summary>
        public bool IsStrictActive => _videoPlaying && _strictActive;

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
            if (suspect == null) return false;

            lock (_libVLCLock)
            {
                if (_libVLC == null) return false;
                if (!ReferenceEquals(_libVLC, suspect)) return false;
                if (_libVLCRetireCount >= MaxLibVLCRetiresPerSession)
                {
                    App.Logger?.Warning(
                        "VideoService: retire requested ({Reason}) but the per-session cap ({Cap}) is reached - keeping the current instance",
                        reason, MaxLibVLCRetiresPerSession);
                    return false;
                }
                _libVLCRetireCount++;

                lock (_quarantineLock) { _nativeQuarantine.Add(_libVLC); }
                App.Logger?.Warning(
                    "VideoService: retiring shared LibVLC instance ({Reason}, retire {N}/{Cap}) - a fresh instance will be created for the next video",
                    reason, _libVLCRetireCount, MaxLibVLCRetiresPerSession);
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
                try { EnsureLibVLCInitialized(); }
                catch (Exception ex) { App.Logger?.Debug("VideoService: background LibVLC rebuild failed: {E}", ex.Message); }
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
        /// Calculate effective volume combining master and video volume.
        /// </summary>
        private int GetEffectiveVolume()
        {
            var master = App.Settings.Current.MasterVolume;
            var video = App.Settings.Current.VideoVolume;
            return (int)((master / 100.0) * (video / 100.0) * 100);
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

            var path = GetNextVideo();
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
            _safetyTimer?.Stop();
            _fallbackSafetyTimer?.Stop();
            _fallbackSafetyTimer = null;
            _videoPlaying = false;
            _triggerInProgress = false;
            _strictActive = false;
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
            if (withAudio) App.Audio?.ApplyPreferredDevice(mediaPlayer);

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
            // from opening on the same MMDevice and racing the primary's mixer state.
            if (!withAudio) media.AddOption(":no-audio");
            mediaPlayer.Play(media);

            if (withAudio)
            {
                mediaPlayer.Mute = false;
                mediaPlayer.Volume = GetEffectiveVolume();
            }

            return win;
        }

        private void ScheduleNext()
        {
            if (!_isRunning || !App.Settings.Current.MandatoryVideosEnabled) return;

            var perHour = Math.Max(1, App.Settings.Current.VideosPerHour);
            var secs = 3600.0 / perHour * (0.8 + _random.NextDouble() * 0.4);

            _scheduler?.Stop();
            _scheduler = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(60, secs)) };
            _scheduler.Tick += (s, e) =>
            {
                _scheduler?.Stop();
                try
                {
                    if (_isRunning && !_videoPlaying && !_triggerInProgress)
                    {
                        if (App.Autonomy?.IsWebVideoActive == true ||
                            App.InteractionQueue?.CurrentInteraction == InteractionQueueService.InteractionType.WebVideo)
                        {
                            // A fullscreen browser/HypnoTube video is on screen — don't stack a
                            // mandatory video (and its audio) on top (BUG-XRFQH4AHDN). Nothing
                            // else re-arms us in this branch (there's no mandatory-video Cleanup
                            // to follow), so reschedule to retry once the web video has ended.
                            // Reschedule rather than queue: an ambient video shouldn't pile up
                            // behind a possibly-long web video.
                            App.Logger?.Information("VideoService: scheduler tick skipped — web video active; rescheduling");
                            ScheduleNext();
                        }
                        else if (_isCleaningUp)
                        {
                            // Previous video's teardown still pumping — same reschedule logic.
                            App.Logger?.Information("VideoService: scheduler tick skipped — cleanup in progress; rescheduling");
                            ScheduleNext();
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

            _triggerInProgress = false;

            if (_videoPlaying)
            {
                App.Logger?.Warning("VideoService: PlayVideo skipped - video already playing");
                return;
            }

            // Fresh video ⇒ fresh vout-retry budget. The retry pass keeps the budget spent so a
            // machine where even the fresh LibVLC instance can't present skips on to ScheduleNext
            // instead of looping retire/retry forever.
            if (!isVoutRetry) _voutRetryUsed = false;

            _videoPlaying = true;
            _strictActive = strict;
            _retryPath = path;
            _startTime = DateTime.Now;
            _hits = _total = 0;
            _spawnTimes.Clear();

            // Arm the off-thread wedge watchdog NOW, before any window is created — the freeze often
            // strikes mid window-creation of this video, before the safety timers get a chance to arm.
            StartWedgeWatchdog();

            // Update Discord presence
            App.DiscordRpc?.SetVideoActivity();

            // Fire pre-announcement event 1.3s before video starts
            VideoAboutToStart?.Invoke(this, EventArgs.Empty);

            // Stop flashes during video
            App.Flash?.Stop();

            // Duck other apps. Record that we took a duck ref so CloseAll releases exactly one
            // matching Unduck on teardown — otherwise a retry/troll "watch again" loop ducks again
            // each pass and only Cleanup unducks once, leaking the ref count (#526).
            if (App.Settings.Current.AudioDuckingEnabled)
            {
                App.Audio?.Duck(App.Settings.Current.DuckingLevel);
                _didDuck = true;
            }

            // Delay video start by 1.3 seconds to allow avatar to announce
            App.Logger?.Debug("VideoService: Starting 1.3s delay before playback");
            var delayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.3) };
            delayTimer.Tick += (s, e) =>
            {
                delayTimer.Stop();
                App.Logger?.Debug("VideoService: Delay complete, calling StartVideoPlayback");
                StartVideoPlayback(path, strict);
            };
            delayTimer.Start();
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

                // Ensure LibVLC is initialized (deferred from startup for faster launch)
                EnsureLibVLCInitialized();
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
                            // Create primary screen with LibVLC VideoView (with audio)
                            var primaryWin = CreateLibVLCVideoWindow(path, primary, strict, withAudio: true);
                            _windows.Add(primaryWin);

                            // Create secondary screens with their own LibVLC players (muted),
                            // unless capped on a high monitor count to avoid decoder lag (#389)
                            if (ShouldFillSecondaryMonitors(allScreens.Count))
                            {
                                foreach (var scr in secondaries)
                                {
                                    var win = CreateLibVLCVideoWindow(path, scr, strict, withAudio: false);
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
                        Cleanup();
                    }
                });

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

                // Create LibVLC VideoView
                var videoView = new VideoView
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = Brushes.Black
                };

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
                if (withAudio) App.Audio?.ApplyPreferredDevice(mediaPlayer);

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
            grid.Children.Add(videoView);

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

            // Attach media player to view and start playback
            videoView.MediaPlayer = mediaPlayer;

            // Create media - use file path directly for better compatibility
            // Media is disposed after Play() — LibVLC internally ref-counts, so this is safe
            // (DualMonitorVideoService already uses this pattern with 'using var media')
            using var media = new Media(_libVLC!, path, FromType.FromPath);
            // Secondaries skip audio decoding entirely. Setting Mute=true after Play() opened
            // a second WASAPI session on the same MMDevice; Windows collapsed both into one
            // per-app mixer slider and the result was doubled/desynced or zero-volume audio.
            if (!withAudio)
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
                // Play() is async: LibVLC hasn't created the audio output yet, so the
                // Volume set above silently no-ops (libvlc_audio_set_volume fails with no
                // aout) and the video starts at 100% regardless of the slider. Re-apply
                // once playback actually begins and the aout exists - only then does the
                // Video Volume slider land (matches DualMonitorVideoService/MiniPlayerWindow).
                mediaPlayer.Playing += (s, e) =>
                {
                    try { mediaPlayer.Mute = false; mediaPlayer.Volume = GetEffectiveVolume(); }
                    catch (Exception ex) { App.Logger?.Debug(ex, "VideoService: volume apply on Playing failed"); }
                };
                App.Logger?.Information("LibVLC audio: Volume={Vol}, Mute={Mute}",
                    mediaPlayer.Volume, mediaPlayer.Mute);

                // Arm the vout watchdog on the primary player: if no video output exists within the
                // grace window the screen is white regardless of decode state (#557-#560/#574) —
                // self-heal by retiring the shared instance and retrying once (see VoutWatchdogFire).
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
                        e.Handled = true;
                        Cleanup();
                        return;
                    }
                    if (App.Settings.Current.PanicKeyEnabled &&
                        e.Key.ToString() == App.Settings.Current.PanicKey)
                    {
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
                    ShowMessage(troll ? "GOOD GIRL!\nWATCH AGAIN 😜" : (App.Mods?.GetAttentionCheckFailMessage() ?? "DUMB BAMBI!\nTRY AGAIN"), 2000, () =>
                    {
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
                        PlayVideo(string.IsNullOrEmpty(retryVideo) ? _retryPath! : retryVideo, _strictActive);
                    });
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
            player.Vout += (s, e) => { if (e.Count > 0) _voutSeen = true; };
            _voutWatchTimer?.Dispose();
            _voutWatchTimer = new System.Threading.Timer(
                _ => VoutWatchdogFire(player, owner, path, strict), null, VoutGraceMs, System.Threading.Timeout.Infinite);
        }

        private void StopVoutWatchdog()
        {
            try { _voutWatchTimer?.Dispose(); } catch { }
            _voutWatchTimer = null;
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

                // Snapshot the teardown generation: if ANY teardown (panic, natural end, wedge rescue)
                // runs before the dispatched action does, the generation moves and the action aborts —
                // it must never resurrect a video the user stopped, nor run reentrantly inside another
                // teardown's message pump.
                var gen = _teardownGeneration;

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (_isCleaningUp || _teardownGeneration != gen || !_videoPlaying) return;
                        // A different video may have started while this was queued — never kill it.
                        if (!ReferenceEquals(player, _primaryMediaPlayer)) return;

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
                            PlayVideo(path, strict, isVoutRetry: true);
                        }
                        else
                        {
                            // Give up on this video via the FULL natural-end path: Cleanup raises
                            // VideoEnded, resumes flashes/bubbles, releases the queue slot, and re-arms
                            // the scheduler. ForceCleanup did none of that, which left ambient features
                            // suppressed for the whole inter-video gap on chronically failing machines.
                            Cleanup();
                        }
                    }
                    catch (Exception ex) { App.Logger?.Warning(ex, "VideoService: vout self-heal dispatch failed"); }
                }));
            }
            catch (Exception ex) { App.Logger?.Debug("VoutWatchdogFire error: {Error}", ex.Message); }
        }

        private void StartWedgeWatchdog()
        {
            _wedgeRescueFired = false;
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
        /// </summary>
        private void WedgeWatchdogTick()
        {
            try
            {
                if (!_videoPlaying || _wedgeRescueFired) return;

                var last = System.Threading.Interlocked.Read(ref _uiHeartbeatTicks);
                var stallMs = (DateTime.UtcNow.Ticks - last) / TimeSpan.TicksPerMillisecond;
                if (stallMs < WedgeStallMs) return;

                _wedgeRescueFired = true;
                App.Logger?.Error(
                    "VideoService: UI thread wedged {StallMs}ms during video playback — off-thread rescue (freeze/lockout guard)",
                    stallMs);

                // Stop native players off-thread. LibVLC Stop() is thread-safe (CloseAll already calls
                // it from Task.Run) and can unblock a stuck decode/render that's holding the UI thread.
                // Harmless when the list is empty (e.g. the wedge is mid window-recreate before the new
                // player is registered — the posted teardown below still recovers it once unblocked).
                List<LibVLCSharp.Shared.MediaPlayer> players;
                lock (_mediaPlayersLock) { players = _mediaPlayers.ToList(); }
                foreach (var p in players)
                {
                    try { p.Stop(); }
                    catch (Exception ex) { App.Logger?.Debug("Wedge rescue: player.Stop failed - {Error}", ex.Message); }
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
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (_teardownGeneration != gen) return;
                            try { ForceCleanup(); }
                            catch (Exception ex) { App.Logger?.Warning(ex, "Wedge rescue: ForceCleanup threw"); }
                            if (_isRunning) ScheduleNext();
                        }));
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
            lock (_cleanupLock)
            {
                if (_isCleaningUp)
                {
                    App.Logger?.Debug("CloseAll: Already cleaning up, skipping duplicate call");
                    return;
                }
                _isCleaningUp = true;
                // Make CloseAll self-sufficient: clear the "playing" flag here rather than
                // relying on every caller to do it first. This guarantees the strict
                // Closing veto (SetupStrictHandlers) can never block the window teardown
                // below, even if a caller forgot to clear it.
                _videoPlaying = false;
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

                    // Wait for all players to stop with timeout (500ms should be plenty)
                    var allStopped = Task.WaitAll(stopTasks, TimeSpan.FromMilliseconds(500));
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
                            // A Stop() that outlives a 4.5s wait is the poisoning signature: retire the
                            // owning instance NOW so the very next video (which may start within seconds)
                            // gets a clean one instead of inheriting the corrupted native state (#559).
                            RetireSharedLibVLC(owningLibVLC, "player Stop() wedged past extended wait");
                        }
                        else
                            App.Logger?.Information("CloseAll: stragglers stopped after extended wait ({Ms}ms)", sw.ElapsedMilliseconds);
                    }
                }

                // CRITICAL: Wait a bit after stopping players to let LibVLC finish any pending operations
                // This prevents crashes when detaching VideoView while LibVLC is still processing
                // Use message-pump-aware wait to prevent deadlock (LibVLC threads may need UI thread)
                if (playersCopy.Count > 0)
                {
                    WaitWithMessagePump(300);
                }

                // Now detach MediaPlayers from VideoViews (safe since players are stopped and we waited)
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

            _safetyTimer?.Stop();
            _fallbackSafetyTimer?.Stop();
            _fallbackSafetyTimer = null;
            _videoPlaying = false;
            _triggerInProgress = false;
            CloseAll();

            App.Logger?.Information("VideoService: Cleanup() - CloseAll completed, _windows now={WinCount}", _windows.Count);
            // Audio unduck now happens inside CloseAll (above) so every teardown path releases the
            // duck ref exactly once (#526). No Unduck here — a second call would decrement a duck ref
            // that another consumer (subliminal/session) may legitimately hold.
            _strictActive = false;
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
