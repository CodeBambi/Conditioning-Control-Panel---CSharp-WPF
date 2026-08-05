using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Forms; // For Screen class
using NAudio.Wave;
using Serilog;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using Image = System.Windows.Controls.Image;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles flash image display with full GIF animation support.
    /// Ported from Python engine.py with all features intact.
    /// </summary>
    public class FlashService : IDisposable
    {
        #region Fields

        private readonly Random _random = new();
        private readonly List<FlashWindow> _activeWindows = new();

        // Window pool: retired flash windows Hide() and get recycled instead of Close().
        // Destroying a layered window while other layered surfaces animate (chaos bubbles,
        // GIF flashes) can wedge the shared WPF render thread (Application Hang 1002) — the
        // per-flash create/close churn was implicated in repeated mid-chaos-run freezes.
        // UI-thread only (heartbeat/spawn/close all run on the dispatcher).
        private readonly Stack<FlashWindow> _windowPool = new();
        private const int WINDOW_POOL_MAX = 12;
        // Hard cap on concurrent live flash windows. Each is a WS_EX_LAYERED topmost window with
        // its own native compositor surface; 30 was enough to back up the render thread under chaos
        // (which both starved CompleteRender into the resize deadlock and drove the native-memory
        // ramp — managed heap stayed ~82MB while private memory hit 3GB). 10 relieves both.
        private const int MAX_CONCURRENT_FLASH = 10;
        // Compositor/solid flashes are cheap items on ONE shared Skia host — no per-flash layered
        // window, so the native-memory/render-thread churn that forced MAX_CONCURRENT_FLASH down to
        // 10 does not apply. Honor the user's SimultaneousImages slider (max 20) plus headroom for
        // hydra children instead of silently capping at 10 (bug: slider set to 11-20 only showed 10).
        private const int MAX_CONCURRENT_FLASH_HOST = 30;

        /// <summary>
        /// Mode-aware concurrent-flash cap (#601). The classic per-flash layered-window path carries
        /// the native-memory/render-churn risk that pins it to <see cref="MAX_CONCURRENT_FLASH"/> (10);
        /// compositor-layer and solid-host flashes are cheap shared-host items and get the higher
        /// <see cref="MAX_CONCURRENT_FLASH_HOST"/> (30) so the SimultaneousImages slider (max 20) is honored.
        /// Pure so it can be unit-tested without spinning up WPF.
        /// </summary>
        internal static int ResolveFlashCap(bool useLayer, bool useHost)
            => (useLayer || useHost) ? MAX_CONCURRENT_FLASH_HOST : MAX_CONCURRENT_FLASH;
        // Per-window shells are sized to a coarse bucket grid so the pool recycles by size instead of
        // realizing a fresh window on nearly every (image-sized, so almost-always-unique) flash. A fresh
        // Window.Show() runs a synchronous MediaContext.CompleteRender on first realization; under a
        // backed-up render thread that call never returns and wedges the whole UI (dump-confirmed
        // 2026-07-05). Bucketing => the pool hits => almost no fresh realizations on the hot path.
        // SLACK guarantees the bucket is large enough to center the glow border inside without clipping.
        private const int FLASH_SHELL_BUCKET = 128;
        private const int FLASH_SHELL_SLACK = 64;
        private static int BucketUp(int v) => ((Math.Max(0, v) + FLASH_SHELL_SLACK + FLASH_SHELL_BUCKET - 1) / FLASH_SHELL_BUCKET) * FLASH_SHELL_BUCKET;
        private List<string> _imageList = new();  // Cached image list for random selection
        private List<(string PackId, PackFileEntry File)> _packImageList = new();  // Cached pack images for random selection
        // Size of DisabledAssetPaths when the live pools were last reconciled against it. Every
        // asset-manager toggle moves that count, so one int compare per draw is enough to notice a
        // pool that predates the user's latest selection — see PruneDeselectedFromPools.
        private int _poolDisabledStamp = -1;
        private Queue<string> _soundQueue = new();  // Performance: Changed to Queue for O(1) dequeue
        private readonly List<string> _tempPackFiles = new();  // Track temp files for cleanup
        private readonly object _lockObj = new();
        private FlashWindow[] _windowsSnapshot = Array.Empty<FlashWindow>(); // Reusable snapshot for heartbeat

        // Performance: Cache for directory file listings to avoid repeated disk scans
        private static readonly Dictionary<string, (List<string> files, DateTime lastScan)> _fileListCache = new();
        private static readonly object _cacheLock = new();
        private const int CACHE_EXPIRY_SECONDS = 60;  // Re-scan directories every 60 seconds

        private DispatcherTimer? _schedulerTimer;
        private bool _heartbeatOn;                       // CompositionTarget.Rendering subscribed
        private TimeSpan _lastHeartbeat = TimeSpan.MinValue;
        private CancellationTokenSource? _cancellationSource;
        
        private bool _isRunning;
        private bool _isBusy;
        private bool _oneShotActive; // For TriggerFlashOnce when service not running

        // Solid mode: one ref-counted hold on the shared ChaosBubbleHostOverlay for the whole
        // flash session (Start→Stop, or a one-shot burst) — NOT per flash. The host has the same
        // keep-alive contract as every chaos overlay: creating/closing a fullscreen layered window
        // per flash would reintroduce exactly the churn solid mode exists to remove.
        private bool _hostRefHeld;
        private bool _noImagesWarningShown;

        // COMPOSITOR (unified overlay host): flashes render as items on the shared per-monitor
        // Skia host - no per-flash window, no pooled shells, no host-canvas churn. The heartbeat
        // keeps driving fade/frames/dwell through the FlashWindow state bag (same split as solid
        // mode), so lifetime/hydra/gaze behavior is identical in all three modes.
        private Compositor.FlashLayer? _flashLayer;
        private static bool UseCompositor => App.CompositorEnabled;

        // Clickable layer flashes: the compositor host is click-through, so clicks arrive via a
        // global mouse hook hit-testing an immutable snapshot (same pattern as shared-host
        // bubbles). The hook thread reads ONLY the copied rect values in _layerHits - never
        // live WPF state. Rebuilt each heartbeat tick.
        private GlobalMouseHook? _layerHook;
        private sealed class LayerHit
        {
            public FlashWindow Win = null!;
            public float X, Y, W, H;   // world px
        }
        private volatile LayerHit[] _layerHits = Array.Empty<LayerHit>();
        // While a mandatory video is playing, the compositor host is pinned BELOW the video
        // (#497 reconciler), so a layer flash under the video rect is invisible — swallowing
        // clicks there would eat the user's attention-check clicks on a flash they can't see.
        // Published as an immutable [x,y,w,h] physical-px array (atomic reference swap; the
        // hook thread only ever reads the captured reference). Null = no exclusion.
        private volatile float[]? _layerVideoExcludePx;
        
        // Audio - only ONE sound per flash event
        private WaveOutEvent? _currentSound;
        private AudioFileReader? _currentAudioFile;
        private bool _soundPlayingForCurrentFlash;

        // Paths
        private string _imagesPath = "";

        /// <summary>
        /// Flash voice-line folder, re-resolved on every use — NEVER cached in a field. On a modular
        /// install the folder does not exist at construction time (audio-base isn't downloaded yet) and
        /// a cached path would keep pointing at the empty install-dir location, leaving flash audio
        /// silent until a restart. Also picks up a mid-session mod switch for free
        /// (CompanionPhraseService resolves the ACTIVE mod's flashes_audio first).
        /// Cheap: a couple of Directory.Exists calls, and only hit when the shuffle queue runs dry.
        /// </summary>
        private static string SoundsPath => CompanionPhraseService.VoiceLineFolder;

        // Image decode cache: avoids reloading/re-decoding the same images every flash
        // Key = file path, Value = (data, lastAccess)
        // Keyed by file path ONLY (decodeMax stored alongside). Keying by path+dim let the
        // performance tier's auto up/down-shifts cache the same file at 2-3 sizes at once,
        // halving effective capacity and forcing extra decodes exactly when the system was
        // already under load (#486). A hit at an equal-or-larger cached dim is served as-is;
        // a larger request replaces the entry.
        private readonly Dictionary<string, (LoadedImageData data, DateTime lastAccess, int decodeMax)> _imageDecodeCache = new();
        private const int MAX_IMAGE_CACHE_ENTRIES = 50;
        private const long MAX_IMAGE_CACHE_BYTES = 200L * 1024 * 1024; // 200 MB cap
        private long _imageCacheBytes;

        // Decode attribution (cumulative, app-lifetime) for the chaos OOM hunt. A cache MISS that
        // runs an actual decode increments these; cache hits don't. The GIF path was the last
        // GDI+ consumer and was migrated to SKCodec (#486) — if native~ in [CHAOSMEM] still climbs
        // in lockstep with GifDecodes after the migration, look beyond the decoder.
        public long GifDecodes;     // SKCodec (SkiaSharp) animated decodes — was GDI+ until #486
        public long StaticDecodes;  // WIC (BitmapImage) decodes — off GDI+ since the chaos OOM fix

        // Snapshot of file paths from the most recent FlashDisplayed batch.
        // Read by SessionLogService after FlashDisplayed fires.
        private IReadOnlyList<string> _lastDisplayedPaths = Array.Empty<string>();

        #endregion

        #region Events

        public event EventHandler? FlashAboutToDisplay;
        public event EventHandler? FlashDisplayed;
        public event EventHandler? FlashClicked;
        public event EventHandler<FlashAudioEventArgs>? FlashAudioPlaying;

        #endregion

        #region Properties

        /// <summary>
        /// Whether the flash service is currently running
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Number of flash windows currently on screen. Used as a live-load signal for
        /// automatic performance-tier escalation (see Services/PerformanceProfile.cs).
        /// </summary>
        public int ActiveWindowCount => _activeWindows.Count;

        /// <summary>
        /// Re-assert HWND_TOPMOST on every live flash window. Flashes (and gif cascades) are the
        /// top attention layer by design, sitting ABOVE the chaos bubbles. The chaos run re-raises
        /// its bubbles over the HUD/boons/active-skill chrome ~once a second; this lets the chaos
        /// layer kick the flashes back on top afterwards so an already-showing flash is never
        /// briefly buried under a re-raised bubble. Focus-free, cheap, no-op when nothing is live.
        /// </summary>
        public void RaiseAllToFront()
        {
            DispatcherHelper.RunOnUI(() =>
            {
                bool anyHosted = false, anyLayer = false;
                lock (_lockObj)
                {
                    foreach (var w in _activeWindows)
                    {
                        if (w.IsFadingOut) continue;
                        if (w.UsesHost) anyHosted = true;   // no hwnd of its own — raise the shared host instead
                        else if (w.UsesLayer) anyLayer = true;   // ditto — raise the compositor host
                        else ForceTopmost(w);
                    }
                }
                if (anyHosted) ChaosBubbleHostOverlay.RaiseActive();

                // Layer flashes live on the compositor host, which only asserts topmost on its
                // show edge — kick it back above the chaos layer's ~1/s bubble re-raise the same
                // way legacy flash windows are force-topmosted. Skip while a mandatory video is
                // playing: OverlayService.ReassertZOrder deliberately pins the host BELOW the
                // video (#497), and raising it here would just fight that reconciler.
                if (anyLayer && App.Video?.IsPlaying != true && App.Compositor is { } engine)
                {
                    try
                    {
                        foreach (var hostHwnd in engine.GetVisibleHostHandles())
                            NativeMethods.SetWindowPos(hostHwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                    }
                    catch { }
                }
            });
        }

        /// <summary>
        /// File paths of images shown by the most recent FlashDisplayed event.
        /// Snapshot is captured immediately before the event fires so subscribers
        /// can read it synchronously. Empty when no flash has displayed yet.
        /// </summary>
        public IReadOnlyList<string> LastDisplayedImagePaths => _lastDisplayedPaths;

        /// <summary>
        /// Snapshot of currently-active flash windows that should respond to
        /// Focus Gaze dwells. Returns empty when neither gaze-pop nor
        /// stare-linger is enabled — dwell tracking has no consumer in that
        /// state. FlashClickable controls mouse clicks only; gaze-pop and
        /// linger have their own toggles. Caller iterates in reverse for
        /// topmost-first selection.
        /// </summary>
        internal IReadOnlyList<FlashWindow> GetGazeTargets()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return Array.Empty<FlashWindow>();

            // Decoupled from FlashClickable: a user can have mouse-clickable
            // OFF while gaze-pop or linger is ON, and vice versa. Bail only
            // when BOTH gaze behaviors are off — there's nothing to consume
            // the dwell.
            if (!settings.FlashGazePopEnabled && !settings.FlashGazeLingerEnabled)
                return Array.Empty<FlashWindow>();

            lock (_lockObj)
            {
                var list = new List<FlashWindow>(_activeWindows.Count);
                foreach (var w in _activeWindows)
                {
                    if (!w.IsFadingOut) list.Add(w);
                }
                return list;
            }
        }

        /// <summary>
        /// Programmatic equivalent of a mouse click on a flash window. Runs
        /// the same close + hydra-multiplication + haptic + FlashClicked
        /// pipeline as MouseLeftButtonDown. Flagged as gaze-driven so hydra
        /// can stop the self-sustaining chain (#784) — see OnFlashClicked.
        /// </summary>
        internal void GazePop(FlashWindow window)
        {
            if (window == null || window.IsFadingOut) return;
            OnFlashClicked(window, App.Settings.Current, fromGaze: true);
        }

        #endregion

        #region Constructor

        public FlashService()
        {
            RefreshImagesPath();
            // Same hazard as SubliminalService's ctor: the voice-line folder is install-dir anchored
            // and Program Files is read-only, so once flashes_audio ships as a downloadable content
            // pack this line would throw on startup for anyone who hasn't fetched it yet. Best effort —
            // and only a convenience for users dropping their own files in; playback re-resolves the
            // folder per use (see SoundsPath), so failing here costs nothing.
            var soundsPath = SoundsPath;
            try { Directory.CreateDirectory(soundsPath); }
            catch (Exception ex)
            {
                App.Logger?.Warning("FlashService: could not create {Path} - {Error}", soundsPath, ex.Message);
            }
            // Animation/fade heartbeat runs off CompositionTarget.Rendering (vsync-aligned)
            // — see StartHeartbeat. A 33ms DispatcherTimer's OS-quantized cadence beats
            // against the display refresh and makes GIF flashes judder (same fix as the
            // chaos DVD logo / gif cascade).
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Refresh the images path based on current settings.
        /// Call this after changing the custom assets path.
        /// </summary>
        public void RefreshImagesPath()
        {
            _imagesPath = Path.Combine(App.EffectiveAssetsPath, "images");
            Directory.CreateDirectory(_imagesPath);
            ClearFileCache(); // Clear cached file list so it reloads from new path

            lock (_lockObj)
            {
                _imageList.Clear();
                _packImageList.Clear();
                CleanupTempPackFiles();
            }

            App.Logger?.Information("FlashService: Images path refreshed to {Path}", _imagesPath);
        }

        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _cancellationSource?.Dispose();
            _cancellationSource = new CancellationTokenSource();
            StartHeartbeat();

            ScheduleNextFlash();

            // Update Discord presence
            App.DiscordRpc?.SetFlashActivity();

            App.Logger.Information("FlashService started, images path: {Path}", _imagesPath);
        }

        public void Stop()
        {
            _isRunning = false;
            try { _cancellationSource?.Cancel(); }
            catch (ObjectDisposedException) { }
            StopHeartbeat();
            _schedulerTimer?.Stop();

            StopCurrentSound();
            CloseAllWindows();
            ReleaseHostRef();
            ReleaseLayerHook();

            // Release cached BitmapSource objects from the LOH on stop
            ClearImageCache();

            // Update Discord presence back to idle
            App.DiscordRpc?.SetIdleActivity();

            App.Logger.Information("FlashService stopped");
        }

        public void TriggerFlash()
        {
            if (!_isRunning || _isBusy) return;
            // Skip spawning a fresh layered flash window while a monitor/DPI change is settling — one
            // dropped flash is invisible; a new surface during the composition rebuild is not (freeze cluster).
            if (Services.UI.DisplayChangeCoordinator.SpawnsSuppressed) return;

            _isBusy = true;
            _soundPlayingForCurrentFlash = false; // Reset for new flash event
            Task.Run(() => LoadAndShowImages());
        }

        /// <summary>
        /// Trigger a one-shot flash that works even when service is not running.
        /// Used by Autonomy Mode to trigger flashes independently of engine state.
        /// </summary>
        public void TriggerFlashOnce(int? amount = null, int? duration = null, int? size = null, bool suppressHaptic = false)
        {
            if (_isBusy)
            {
                App.Logger?.Debug("FlashService: TriggerFlashOnce skipped - busy");
                return;
            }
            if (Services.UI.DisplayChangeCoordinator.SpawnsSuppressed)
            {
                App.Logger?.Debug("FlashService: TriggerFlashOnce skipped - display change in progress");
                return;
            }

            // Ensure path is set (in case constructor didn't run or path changed)
            if (string.IsNullOrEmpty(_imagesPath))
            {
                RefreshImagesPath();
            }

            App.Logger?.Information("FlashService: TriggerFlashOnce called (path: {Path})", _imagesPath);

            _isBusy = true;
            _oneShotActive = true; // Enable one-shot mode to bypass _isRunning checks
            _soundPlayingForCurrentFlash = false;

            // Start heartbeat timer for animation and fade management
            StartHeartbeat();

            Task.Run(() => LoadAndShowImages(amount, duration, size, suppressHaptic));
        }

        /// <summary>
        /// One-shot flash that displays a specific image instead of picking randomly
        /// from the cached image list. Used by Deeper enhancement Effect timeline
        /// items that pin a particular image. <paramref name="imagePath"/> is
        /// absolute or rooted under <c>App.EffectiveAssetsPath/images</c>; passing
        /// null or empty falls back to <see cref="TriggerFlashOnce"/> behavior.
        /// </summary>
        public void TriggerFlashOnceWithImage(string? imagePath, int durationMs, bool playSound, bool suppressHaptic = false)
        {
            if (_isBusy)
            {
                App.Logger?.Debug("FlashService: TriggerFlashOnceWithImage skipped - busy");
                return;
            }

            if (string.IsNullOrWhiteSpace(imagePath))
            {
                TriggerFlashOnce(amount: 1, duration: durationMs, suppressHaptic: suppressHaptic);
                return;
            }

            string resolved = imagePath!;
            if (!System.IO.Path.IsPathRooted(resolved))
                resolved = System.IO.Path.Combine(App.EffectiveAssetsPath ?? "", "images", resolved);

            if (!System.IO.File.Exists(resolved))
            {
                App.Logger?.Debug("FlashService: TriggerFlashOnceWithImage path not found ({Path}); falling back to random", resolved);
                TriggerFlashOnce(amount: 1, duration: durationMs);
                return;
            }

            if (string.IsNullOrEmpty(_imagesPath)) RefreshImagesPath();

            _isBusy = true;
            _oneShotActive = true;
            _soundPlayingForCurrentFlash = false;
            StartHeartbeat();

            Task.Run(() => LoadAndShowSpecificImage(resolved, durationMs, playSound, suppressHaptic));
        }

        private async void LoadAndShowSpecificImage(string imagePath, int durationMs, bool playSound, bool suppressHaptic = false)
        {
            try
            {
                var settings = App.Settings.Current;
                var soundPath = playSound ? GetNextSound() : null;
                var scale = settings.ImageScale / 100.0;

                var data = await LoadImageAsync(imagePath);
                if (data == null)
                {
                    _isBusy = false;
                    return;
                }

                var monitor = PickMonitor(settings);
                var geometry = CalculateGeometry(data.Width, data.Height, monitor, scale);
                data.Geometry = geometry;
                data.Monitor = monitor;

                await DispatcherHelper.RunOnUIAsync(() =>
                {
                    ShowImages(new List<LoadedImageData> { data }, soundPath, false, customDuration: durationMs, suppressHaptic: suppressHaptic);
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("FlashService: TriggerFlashOnceWithImage error: {Error}", ex.Message);
                _isBusy = false;
            }
        }

        public void LoadAssets()
        {
            lock (_lockObj)
            {
                _imageList.Clear();  // Clear cached image list
                _packImageList.Clear();  // Clear cached pack image list
                _soundQueue = new Queue<string>();
                CleanupTempPackFiles();
            }
            ClearFileCache();  // Performance: Clear cached file listings to pick up new files
            lock (_imageDecodeCache) { _imageDecodeCache.Clear(); _imageCacheBytes = 0; }
            App.Logger.Information("Assets reloaded");
        }

        /// <summary>
        /// Refresh the flash schedule when frequency changes
        /// </summary>
        public void RefreshSchedule()
        {
            if (!_isRunning) return;
            ScheduleNextFlash();
        }

        #endregion

        #region Scheduling

        private void ScheduleNextFlash()
        {
            if (!_isRunning) return;

            var settings = App.Settings.Current;
            if (!settings.FlashEnabled)
            {
                App.Logger.Debug("FlashService: Flashes disabled in settings");
                return;
            }
            
            // flash_freq = flashes per HOUR (1-180)
            var baseFreq = Math.Max(1, settings.FlashFrequency);
            var baseInterval = 3600.0 / baseFreq; // seconds between flashes
            
            // Add ±30% variance
            var variance = baseInterval * 0.3;
            var interval = baseInterval + (_random.NextDouble() * variance * 2 - variance);
            interval = Math.Max(3, interval); // Minimum 3 seconds
            
            if (_schedulerTimer == null)
            {
                _schedulerTimer = new DispatcherTimer();
                _schedulerTimer.Tick += SchedulerTimer_Tick;
            }
            _schedulerTimer.Stop();
            _schedulerTimer.Interval = TimeSpan.FromSeconds(interval);
            _schedulerTimer.Start();
        }

        private void SchedulerTimer_Tick(object? sender, EventArgs e)
        {
            _schedulerTimer?.Stop();
            if (_isRunning && !_isBusy)
            {
                TriggerFlash();
            }
            ScheduleNextFlash();
        }

        #endregion

        #region Image Loading

        private async void LoadAndShowImages(int? amount = null, int? duration = null, int? size = null, bool suppressHaptic = false)
        {
            try
            {
                var settings = App.Settings.Current;
                var images = GetNextImages(amount ?? settings.SimultaneousImages);

                if (images.Count == 0)
                {
                    if (!_noImagesWarningShown)
                    {
                        App.Logger.Warning("FlashService: No images found in {Path}. Add images to this folder to enable flash display.", _imagesPath);
                        _noImagesWarningShown = true;
                    }
                    _isBusy = false;
                    return;
                }

                App.Logger.Information("FlashService: Displaying {Count} flash image(s)", images.Count);

                // Fire pre-event so avatar can announce the flash
                FlashAboutToDisplay?.Invoke(this, EventArgs.Empty);

                // Wait 1 second so speech bubble appears before flash
                await Task.Delay(1000);

                // Get sound ONCE for this flash event
                var soundPath = GetNextSound();

                // Scale is percentage: 50-250%, stored as 50-250, so divide by 100
                var scale = (size ?? settings.ImageScale) / 100.0;

                // Load images, retrying with fresh picks if some are corrupted/unsupported,
                // until we reach the requested count or run out of candidates.
                var targetCount = amount ?? settings.SimultaneousImages;
                var loadedImages = await LoadImagesUntilAsync(targetCount);

                if (loadedImages.Count == 0)
                {
                    _isBusy = false;
                    return;
                }

                // Show on UI thread - pass sound path only ONCE
                await DispatcherHelper.RunOnUIAsync(() =>
                {
                    ShowImages(loadedImages, soundPath, false, customDuration: duration, suppressHaptic: suppressHaptic);
                });
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex, "Error loading flash images");
                _isBusy = false;
            }
        }

        /// <summary>
        /// Loads up to <paramref name="targetCount"/> images, retrying with new candidates
        /// when a file is missing, corrupted, or uses an unsupported codec. Images are used
        /// as soon as they decode successfully; slow or broken files do not block the others.
        /// </summary>
        private async Task<List<LoadedImageData>> LoadImagesUntilAsync(int targetCount)
        {
            var loaded = new List<LoadedImageData>(targetCount);
            var attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var settings = App.Settings.Current;
            var scale = settings.ImageScale / 100.0;
            int attempts = 0;
            int maxAttempts = Math.Max(targetCount * 5, 20);
            var pending = new List<Task<LoadedImageData?>>();

            while (loaded.Count < targetCount && attempts < maxAttempts)
            {
                int need = targetCount - loaded.Count;

                // Keep a generous pipeline of decode tasks running.
                int fetch = Math.Min(Math.Max(need * 3, 3), maxAttempts - attempts - pending.Count);
                if (fetch > 0)
                {
                    var candidates = GetNextImages(fetch);
                    if (candidates.Count == 0 && pending.Count == 0) break;

                    var newCandidates = candidates.Where(c => attempted.Add(c)).ToList();
                    if (newCandidates.Count == 0 && pending.Count == 0) break;

                    pending.AddRange(newCandidates.Select(LoadImageAsync));
                    attempts += newCandidates.Count;
                }

                if (pending.Count == 0) break;

                // Use the first image that finishes decoding, whether it succeeds or fails.
                var completed = await Task.WhenAny(pending);
                pending.Remove(completed);
                var data = await completed;
                if (data != null && loaded.Count < targetCount)
                {
                    var monitor = PickMonitor(settings);
                    var geometry = CalculateGeometry(data.Width, data.Height, monitor, scale);
                    data.Geometry = geometry;
                    data.Monitor = monitor;
                    loaded.Add(data);
                }
            }

            // Drain any stragglers so unobserved exceptions don't linger.
            if (pending.Count > 0)
            {
                try { await Task.WhenAll(pending); } catch { /* individual tasks are already guarded */ }
            }

            return loaded;
        }

        private async Task<LoadedImageData?> LoadImageAsync(string path)
        {
            try
            {
                // Decode images at (roughly) display resolution instead of full source
                // resolution — a 4K image shown at ~300-1000px wastes memory + GPU fill-rate.
                // Cap scales with the active performance tier and the user's ImageScale.
                int decodeMax = ComputeDecodeMaxDim();

                // Check decode cache first (frozen BitmapSources are thread-safe). A cached
                // decode serves any request at the same or smaller cap (WPF scales down for
                // free at render). It also serves LARGER requests when the source image never
                // hit the cap (decoded size strictly below the cached cap = native res kept).
                // Only a genuinely capped decode being asked for more pixels re-decodes, and
                // the store below then replaces the entry — one entry per file, always.
                lock (_imageDecodeCache)
                {
                    if (_imageDecodeCache.TryGetValue(path, out var cached))
                    {
                        bool uncapped = Math.Max(cached.data.Width, cached.data.Height) < cached.decodeMax;
                        if (cached.decodeMax >= decodeMax || uncapped)
                        {
                            _imageDecodeCache[path] = (cached.data, DateTime.UtcNow, cached.decodeMax);
                            return CloneImageData(cached.data);
                        }
                    }
                }

                return await Task.Run(() =>
                {
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    var data = new LoadedImageData { FilePath = path };

                    if (!File.Exists(path))
                    {
                        App.Logger?.Debug("FlashService: image file not found: {Path}", path);
                        return null;
                    }

                    if (extension == ".gif")
                    {
                        System.Threading.Interlocked.Increment(ref GifDecodes);
                        LoadGifFrames(path, data, decodeMax);
                    }
                    else if (extension == ".webp" && TryLoadAnimatedWebpFrames(path, data, decodeMax))
                    {
                        // Animated webp → frame list, played by the same heartbeat frame-stepper
                        // as GIFs. Still/undecodable webps return false and fall through to the
                        // static WIC branch below.
                    }
                    else
                    {
                        System.Threading.Interlocked.Increment(ref StaticDecodes);
                        // Decode the static image through WIC (WPF BitmapImage), NOT System.Drawing/GDI+.
                        // GDI+ allocates decoded pixels on the native Win32 heap and bloats/leaks it under
                        // the high-frequency flash decode churn — VMMap pinned ~1.3GB in the native heap as
                        // the chaos OOM, while the managed GC heap, GDI handles and MILCore all stayed small.
                        // WIC decodes into a WPF-owned buffer and DecodePixelWidth/Height scales DURING the
                        // decode (no full-size intermediate, nothing on the GDI+ heap).
                        int srcW = 0, srcH = 0;
                        try
                        {
                            var probe = BitmapFrame.Create(new Uri(path, UriKind.Absolute),
                                BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                            srcW = probe.PixelWidth; srcH = probe.PixelHeight;
                        }
                        catch { }

                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;                  // decode now, release the file handle
                        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        // Only DOWNSCALE (never upscale a small source): cap the larger edge to decodeMax.
                        if (srcW > decodeMax || srcH > decodeMax)
                        {
                            if (srcW >= srcH) bmp.DecodePixelWidth = decodeMax;
                            else bmp.DecodePixelHeight = decodeMax;
                        }
                        bmp.EndInit();
                        bmp.Freeze();

                        data.Frames.Add(bmp);
                        data.Width = bmp.PixelWidth;
                        data.Height = bmp.PixelHeight;
                        data.FrameDelay = TimeSpan.FromMilliseconds(100);
                    }

                    if (data.Frames.Count == 0) return null;

                    // Estimate memory: width × height × 4 bytes × frame count
                    var entryBytes = (long)data.Width * data.Height * 4 * data.Frames.Count;

                    lock (_imageDecodeCache)
                    {
                        // Replacing an existing entry for this file (re-decode at a larger
                        // cap, or a concurrent miss that raced us)? Release its bytes first
                        // so the accounting doesn't drift upward.
                        if (_imageDecodeCache.TryGetValue(path, out var existing))
                        {
                            _imageDecodeCache.Remove(path);
                            _imageCacheBytes -= (long)existing.data.Width * existing.data.Height * 4 * existing.data.Frames.Count;
                        }

                        // Evict if over limits
                        while (_imageDecodeCache.Count >= MAX_IMAGE_CACHE_ENTRIES ||
                               _imageCacheBytes + entryBytes > MAX_IMAGE_CACHE_BYTES)
                        {
                            if (_imageDecodeCache.Count == 0) break;
                            // Evict least recently accessed
                            string? oldest = null;
                            var oldestTime = DateTime.MaxValue;
                            long oldestBytes = 0;
                            foreach (var kvp in _imageDecodeCache)
                            {
                                if (kvp.Value.lastAccess < oldestTime)
                                {
                                    oldestTime = kvp.Value.lastAccess;
                                    oldest = kvp.Key;
                                    oldestBytes = (long)kvp.Value.data.Width * kvp.Value.data.Height * 4 * kvp.Value.data.Frames.Count;
                                }
                            }
                            if (oldest != null)
                            {
                                _imageDecodeCache.Remove(oldest);
                                _imageCacheBytes -= oldestBytes;
                            }
                            else break;
                        }

                        _imageDecodeCache[path] = (data, DateTime.UtcNow, decodeMax);
                        _imageCacheBytes += entryBytes;
                    }

                    return CloneImageData(data);
                });
            }
            catch (Exception ex)
            {
                App.Logger.Debug("Could not load image {Path}: {Error}", path, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Creates a shallow clone of LoadedImageData with its own Frames list.
        /// BitmapSource references are shared (they're frozen/immutable), but the list
        /// is independent so SafeCloseFlashWindow can clear it without affecting the cache.
        /// </summary>
        private static LoadedImageData CloneImageData(LoadedImageData source)
        {
            var clone = new LoadedImageData
            {
                FilePath = source.FilePath,
                Width = source.Width,
                Height = source.Height,
                FrameDelay = source.FrameDelay,
            };
            clone.Frames.AddRange(source.Frames);
            return clone;
        }

        /// <summary>
        /// Animated .webp → frame list. WIC only ever decodes webp's first frame and GDI+ can't
        /// open the format at all, so animated webps flashed as stills. SkiaSharp decodes and
        /// composes the full animation under the same memory budget as LoadGifFrames. Returns
        /// false (data untouched) for still webps or on decode failure so the caller falls back
        /// to the static WIC path.
        /// </summary>
        private static bool TryLoadAnimatedWebpFrames(string path, LoadedImageData data, int decodeMax)
        {
            try
            {
                if (!AnimatedWebp.IsAnimated(path)) return false;
                if (AnimatedWebp.DecodeFrames(path, decodeMax, maxFrames: 60, maxMemoryMb: 30.0) is not { } d)
                    return false;

                data.Frames.AddRange(d.Frames);
                data.Width = d.Frames[0].PixelWidth;
                data.Height = d.Frames[0].PixelHeight;
                data.FrameDelay = d.FrameDelay;
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Could not load webp frames: {Error}", ex.Message);
                return false;
            }
        }

        private void LoadGifFrames(string path, LoadedImageData data, int decodeMax)
        {
            // SkiaSharp (SKCodec) decode — NOT System.Drawing/GDI+. This was the last
            // GDI+ decoder in the flash pipeline: every cache-miss GIF ran each frame
            // through Graphics.DrawImage on the native Win32 heap, which bloats and
            // never returns pages under high-frequency decode churn — the same VMMap
            // signature (managed heap flat, private bytes climbing to multi-GB, native
            // OOM with an empty crash log) that got the static path migrated to WIC
            // (#486). SKCodec composes delta frames (RequiredFrame handling) and shares
            // the animated-webp budget: decodeMax edge, ≤60 frames, ≤30MB kept.
            try
            {
                if (AnimatedWebp.DecodeFrames(path, decodeMax, maxFrames: 60, maxMemoryMb: 30.0) is { } d)
                {
                    data.Frames.AddRange(d.Frames);
                    data.Width = d.Frames[0].PixelWidth;
                    data.Height = d.Frames[0].PixelHeight;
                    data.FrameDelay = d.FrameDelay;
                    return;
                }
            }
            catch (Exception ex)
            {
                App.Logger.Debug("Could not load GIF frames: {Error}", ex.Message);
            }

            // Single-frame or undecodable GIF → static WIC decode, mirroring the static
            // image branch (decode-time downscale, no full-size intermediate, no GDI+).
            try
            {
                int srcW = 0, srcH = 0;
                try
                {
                    var probe = BitmapFrame.Create(new Uri(path, UriKind.Absolute),
                        BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    srcW = probe.PixelWidth; srcH = probe.PixelHeight;
                }
                catch { }

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                if (srcW > decodeMax || srcH > decodeMax)
                {
                    if (srcW >= srcH) bmp.DecodePixelWidth = decodeMax;
                    else bmp.DecodePixelHeight = decodeMax;
                }
                bmp.EndInit();
                bmp.Freeze();

                data.Frames.Add(bmp);
                data.Width = bmp.PixelWidth;
                data.Height = bmp.PixelHeight;
                data.FrameDelay = TimeSpan.FromMilliseconds(100);
            }
            catch (Exception ex)
            {
                App.Logger.Debug("Could not load GIF as static image: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Largest pixel dimension to decode a flash image/GIF frame at. Scales with the active
        /// performance tier and the user's ImageScale, clamped to a sane range. Keeping decoded
        /// frames near display size (rather than full source res) is the single biggest memory
        /// and GPU-fill-rate win when many flashes are on screen.
        /// </summary>
        private static int ComputeDecodeMaxDim()
        {
            int baseCap = PerformanceProfile.MaxDecodeDimension(PerformanceProfile.CurrentTier);
            int scale = App.Settings?.Current?.ImageScale ?? 100;
            int dim = (int)(baseCap * (scale / 100.0));
            return Math.Clamp(dim, 256, 2048);
        }

        // ScaledSize / DownscaleBitmap / ConvertToBitmapSource (the GDI+ decode helpers)
        // were removed with the LoadGifFrames SKCodec migration (#486) — no flash decode
        // path touches System.Drawing anymore.

        #endregion

        #region Display

        /// <summary>
        /// Shows flash images on screen with per-window lifetimes~ 🌸
        /// </summary>
        /// <param name="overrideLifetimeMs">If provided, overrides the calculated lifetime (used for hydra linked timing)~ 🔗</param>
        /// <param name="hydraGeneration">How many hydra hops deep these spawns are (0 = original flash)~ 🐙</param>
        private void ShowImages(List<LoadedImageData> images, string? soundPath, bool isMultiplication, int? overrideLifetimeMs = null, int hydraGeneration = 0, int? customDuration = null, bool suppressHaptic = false)
        {
            if (!_isRunning && !_oneShotActive)
            {
                if (!isMultiplication) _isBusy = false;
                return;
            }

            var settings = App.Settings.Current;
            // customDuration is in MILLISECONDS (matches the surrounding lifetimeMs units);
            // settings.FlashDuration is in SECONDS. Normalise to seconds so PlaySound /
            // unduck / lifetime math downstream stays in one unit.
            double duration = customDuration.HasValue
                ? customDuration.Value / 1000.0
                : settings.FlashDuration;

            // Play sound ONLY ONCE per flash event (not for hydra spawns) - only if audio enabled
            if (settings.FlashAudioEnabled && !_soundPlayingForCurrentFlash && !isMultiplication && !string.IsNullOrEmpty(soundPath) && File.Exists(soundPath))
            {
                try
                {
                    _soundPlayingForCurrentFlash = true;
                    duration = PlaySound(soundPath, settings.MasterVolume);
                    // Tell the bark system a flash "whisper" is audible so the companion won't talk over it.
                    App.Audio?.MarkWhisperAudio(duration);

                    // Fire event so avatar can show the audio text as speech bubble
                    FlashAudioPlaying?.Invoke(this, new FlashAudioEventArgs(soundPath));

                    // Audio ducking
                    if (settings.AudioDuckingEnabled)
                    {
                        App.Audio.Duck(settings.DuckingLevel);
                        var duckGen = App.Audio?.DuckGeneration ?? -1;

                        // Schedule unduck
                        var unduckDelay = (int)(duration * 1000) + 1500;
                        var token = _cancellationSource?.Token ?? CancellationToken.None;
                        Task.Delay(unduckDelay, token).ContinueWith(_ =>
                        {
                            try { App.Audio?.Unduck(duckGen); }
                            catch (Exception ex) { App.Logger?.Debug("FlashService unduck failed: {Error}", ex.Message); }
                        }, TaskContinuationOptions.NotOnCanceled);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.Debug("Could not play sound: {Error}", ex.Message);
                }
            }

            // Set per-window lifetime: each window gets its own CancellationTokenSource~ 🌸
            // This lets newer images live longer and users can keep clicking for hydra spawns!
            var lifetimeMs = (int)(duration * 1000) + 1000;
            
            // Allow hydra spawns to override the lifetime (linked vs independent timing)~ 🔗
            if (overrideLifetimeMs.HasValue)
            {
                lifetimeMs = overrideLifetimeMs.Value;
            }
            
            // For one-shot mode, schedule cleanup of one-shot state after all windows should be done fading
            if (_oneShotActive && !isMultiplication)
            {
                var oneShotCleanupDelay = lifetimeMs + 2000; // extra 2s for fade-out
                var cleanupToken = _cancellationSource?.Token ?? CancellationToken.None;
                Task.Delay(oneShotCleanupDelay, cleanupToken).ContinueWith(_ =>
                {
                    try
                    {
                        if (!_oneShotActive || _isRunning) return;
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                        {
                            // Only stop if no active windows remain
                            bool hasWindows;
                            lock (_lockObj) { hasWindows = _activeWindows.Count > 0; }
                            if (!hasWindows && _oneShotActive && !_isRunning)
                            {
                                _oneShotActive = false;
                                StopHeartbeat();
                                ReleaseHostRef();
                                App.Logger?.Debug("FlashService: One-shot flash completed (all windows faded) uwu~ 🌙");
                            }
                        });
                    }
                    catch { }
                }, TaskContinuationOptions.NotOnCanceled);
            }

            // Spawn windows — each gets its own lifetime CTS~ ✨
            for (int i = 0; i < images.Count; i++)
            {
                var imageData = images[i];
                var delayMs = isMultiplication ? i * 100 : i * 300;
                
                if (delayMs == 0)
                {
                    SpawnFlashWindow(imageData, settings, lifetimeMs, hydraGeneration, suppressHaptic);
                }
                else
                {
                    var capturedData = imageData;
                    var capturedLifetime = lifetimeMs;
                    var capturedGeneration = hydraGeneration;
                    var capturedSuppressHaptic = suppressHaptic;
                    var spawnToken = _cancellationSource?.Token ?? CancellationToken.None;
                    Task.Delay(delayMs, spawnToken).ContinueWith(_ =>
                    {
                        try
                        {
                            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                            {
                                if (_isRunning || _oneShotActive)
                                    SpawnFlashWindow(capturedData, settings, capturedLifetime, capturedGeneration, capturedSuppressHaptic);
                            });
                        }
                        catch { }
                    }, TaskContinuationOptions.NotOnCanceled);
                }
            }

            // Snapshot file paths before notifying subscribers so SessionLogService
            // can attribute the FlashDisplayed event to specific files.
            var pathSnapshot = new List<string>(images.Count);
            for (int p = 0; p < images.Count; p++)
            {
                var fp = images[p]?.FilePath;
                if (!string.IsNullOrEmpty(fp)) pathSnapshot.Add(fp);
            }
            _lastDisplayedPaths = pathSnapshot;

            FlashDisplayed?.Invoke(this, EventArgs.Empty);

            if (!isMultiplication)
            {
                _isBusy = false;
            }
        }

        /// <summary>
        /// Spawns a single flash window with its own independent lifetime~ 🌟
        /// CopilotNotes: Each window gets a CTS that fires after lifetimeMs, triggering independent fade-out.
        /// When hydraGeneration > 0 and independent timing is active, XP is reduced by 25% per generation (floor 10%).
        /// </summary>
        private void SpawnFlashWindow(LoadedImageData imageData, AppSettings settings, int lifetimeMs, int hydraGeneration = 0, bool suppressHaptic = false)
        {
            if (!_isRunning && !_oneShotActive) return;

            // Decide the render path up front so the concurrency cap can be mode-aware. Compositor
            // (layer item on the shared Skia host) takes precedence over solid mode (child of the
            // shared host); both fall back to a classic per-flash layered window.
            bool useLayer = UseCompositor;
            bool useHost = !useLayer && settings.FlashSolidMode;

            // Prevent memory explosion / compositor backup from too many concurrent flash windows.
            // Only the classic layered-window path carries that risk; compositor/solid flashes are
            // cheap shared-host items, so they get the higher cap (see MAX_CONCURRENT_FLASH_HOST).
            int cap = ResolveFlashCap(useLayer, useHost);
            lock (_lockObj)
            {
                if (_activeWindows.Count >= cap) return;
            }

            // Create per-window CTS with automatic cancellation after the lifetime expires~ ✨
            var windowCts = new CancellationTokenSource();
            windowCts.CancelAfter(lifetimeMs);

            FlashWindow? window = null;
            int xpAmount = 0;
            int multiplier = 1;
            try
            {
                var geom = imageData.Geometry;
                
                // Avoid overlap with existing windows
                var finalX = geom.X;
                var finalY = geom.Y;
                var monitor = imageData.Monitor;
                
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    if (!IsOverlapping(finalX, finalY, geom.Width, geom.Height))
                        break;

                    // MUST go through PickSpawnPoint, not a raw re-randomize: this loop used to
                    // bypass the geometry rules entirely, so with #770's avoid-center on, any
                    // overlapping flash would land right back on the crosshair.
                    (finalX, finalY) = PickSpawnPoint(monitor, geom.Width, geom.Height);
                }

                // Render path decided at the top of this method (mode-aware cap):
                //   useLayer — COMPOSITOR: visual is a layer item on the shared Skia host; the
                //     per-flash state bag rides along and clickability survives (global-hook
                //     hit-test), unlike solid mode. Takes precedence over solid + classic.
                //   useHost — SOLID MODE: the FlashWindow is created but never shown — it carries
                //     the per-flash state (lifetime CTS, gaze rect, hydra data) while the visual is
                //     a child of the ONE shared click-through host, killing the per-flash
                //     topmost-layered-window churn that some fullscreen games react to.
                //   otherwise — classic per-flash layered window from the pool.

                // Recycled from the pool when possible — all per-spawn state must be (re)set here.
                // The window comes back already at geom's size (matched from the pool or freshly
                // created at that size). NEVER assign Width/Height here: changing the size of an
                // already-realized layered window forces a synchronous MediaContext.CompleteRender
                // on the compositor and deadlocks the UI thread under chaos load (see AcquireFlashWindow).
                // Left/Top is a move (no surface resize) and is safe on a live window.
                // (A host-mode state bag has no hwnd, so sizing it is plain property storage.)
                // True display size (from the image geometry) vs. the bucketed per-window shell that
                // the pool recycles. Host mode has no window shell, so it stays at true size.
                int trueW = geom.Width, trueH = geom.Height;
                int shellW = (useHost || useLayer) ? trueW : BucketUp(trueW);
                int shellH = (useHost || useLayer) ? trueH : BucketUp(trueH);

                window = useLayer
                    ? new FlashWindow { UsesLayer = true, Width = trueW, Height = trueH }
                    : useHost
                        ? new FlashWindow { UsesHost = true, Width = trueW, Height = trueH }
                        : AcquireFlashWindow(shellW, shellH);
                window.Left = finalX;
                window.Top = finalY;
                window.Frames = imageData.Frames;
                window.FrameDelay = imageData.FrameDelay;
                window.StartTime = DateTime.Now;
                window.CurrentFrameIndex = 0;
                // The shared host is fully click-through (pops on it would need the global mouse
                // hook, like bubbles) — solid-mode flashes are gaze-pop/linger only by design.
                window.IsClickable = settings.FlashClickable && !useHost;
                window.Background = System.Windows.Media.Brushes.Black;
                window.IsFadingOut = false;
                window.LifetimeCts = windowCts;
                window.ExpiresAt = DateTime.Now.AddMilliseconds(lifetimeMs);
                window.OriginalLifetimeMs = lifetimeMs;
                window.HydraGeneration = hydraGeneration;
                // Capture the monitor on the window so hydra children can inherit
                // their parent's screen (TriggerMultiplication reads window.Monitor).
                window.Monitor = monitor;

                // Register cancellation callback — when the token fires, mark this window for fade-out~ 🌙
                // Store the registration so we can dispose it in SafeCloseFlashWindow
                window.LifetimeRegistration = windowCts.Token.Register(() =>
                {
                    try
                    {
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                        {
                            window.IsFadingOut = true;
                        });
                    }
                    catch { }
                });

                // Create image control (layer mode has no WPF visual tree at all - the layer
                // draws the SKImage frames directly, and the heartbeat drives FrameIndex).
                var perfTier = PerformanceProfile.CurrentTier;
                Image? image = null;
                if (!useLayer)
                {
                    image = new Image
                    {
                        Stretch = Stretch.Uniform,
                        Source = imageData.Frames[0],
                        // Pin the display size so centering the flash inside the (larger) bucketed shell
                        // never rescales it — the visual stays exactly the size/aspect it was before.
                        Width = trueW,
                        Height = trueH,
                    };
                    // Cheaper resampling — after decode-at-display-size there is little residual
                    // scaling, so the quality difference is imperceptible while saving GPU fill cost.
                    RenderOptions.SetBitmapScalingMode(image, PerformanceProfile.ScalingMode(perfTier));
                    RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

                    window.ImageControl = image;
                }

                // The visual root: assigned as window.Content in per-window mode, or added to the
                // shared host canvas in solid mode (an element can't be both — one logical parent).
                // Stays null in layer mode (no WPF visual), which never reaches the attach branches.
                FrameworkElement content = null!;

                // Layer-mode glow parameters, filled by the glow branch below and consumed at
                // the layer spawn (the WPF DropShadow content build is skipped entirely).
                double layerGlowRadius = 0, layerGlowOpacity = 0;
                System.Windows.Media.Color layerGlowColor = default;

                // Roll for lucky flash BEFORE show so we can apply visual effects
                xpAmount = _soundPlayingForCurrentFlash ? 8 : 4;

                if (!settings.HydraLinkedTiming && hydraGeneration > 0)
                {
                    if (hydraGeneration >= 2)
                    {
                        xpAmount = 1;
                    }
                    else
                    {
                        // Gen 1: 75% of base XP
                        xpAmount = (int)Math.Max(1, Math.Round(xpAmount * 0.75));
                    }
                    App.Logger?.Debug("Hydra XP: gen {Gen}, xp {XP}", hydraGeneration, xpAmount);
                }

                multiplier = (hydraGeneration > 0) ? 1 : (App.SkillTree?.RollLuckyFlash() ?? 1);
                var isLucky = multiplier > 1;
                window.IsLucky = isLucky;

                if (isLucky)
                {
                    PlayLuckyFlashSound();
                }

                // Apply glow effect based on sparkle boost tier or lucky proc.
                // Glow is a DropShadow blur (expensive at scale) — gate it behind the global
                // glow toggle AND the performance tier (disabled entirely under Performance),
                // and cap the blur radius so 25+ simultaneous flashes don't each run a 60px blur.
                var sparkleBoostTier = App.SkillTree?.GetSparkleBoostTier() ?? 0;
                bool glowEnabled = (App.Settings?.Current?.FlashGlowEnabled ?? true)
                                   && PerformanceProfile.AllowGlow(perfTier);
                if (glowEnabled && (isLucky || sparkleBoostTier > 0))
                {
                    var glowColor = isLucky
                        ? System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00) // Gold
                        : System.Windows.Media.Color.FromRgb(0xFF, 0x69, 0xB4); // Hot pink

                    double blurRadius, glowOpacity;
                    if (isLucky)
                    {
                        blurRadius = 60;
                        glowOpacity = 0.9;
                    }
                    else
                    {
                        blurRadius = sparkleBoostTier switch { 1 => 25, 2 => 35, _ => 45 };
                        glowOpacity = sparkleBoostTier switch { 1 => 0.5, 2 => 0.6, _ => 0.7 };
                    }

                    // Cap the blur radius per tier (Quality ~24, Balanced ~18).
                    blurRadius = Math.Min(blurRadius, PerformanceProfile.MaxGlowBlurRadius(perfTier));

                    // Layer mode: no WPF effect tree - stash the parameters for the layer item
                    // and expand the bookkeeping rect exactly like host mode (the glow halo draws
                    // outside the image, and gaze/overlap read this rect). The lucky pulse runs
                    // inside the layer's render, so no Forever animations to leak either.
                    if (useLayer)
                    {
                        layerGlowRadius = blurRadius;
                        layerGlowOpacity = glowOpacity;
                        layerGlowColor = glowColor;
                        var layerPad = blurRadius / 2;
                        window.Width += layerPad * 2;
                        window.Height += layerPad * 2;
                        window.Left -= layerPad;
                        window.Top -= layerPad;
                    }
                    else
                    {
                    var glowEffect = new DropShadowEffect
                    {
                        Color = glowColor,
                        BlurRadius = blurRadius,
                        ShadowDepth = 0,
                        Opacity = glowOpacity
                    };

                    // Clip the image with rounded corners so the glow wraps softly
                    var clipBorder = new Border
                    {
                        CornerRadius = new CornerRadius(12),
                        ClipToBounds = true,
                        Child = image
                    };

                    var border = new Border
                    {
                        Background = System.Windows.Media.Brushes.Transparent,
                        Effect = glowEffect,
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(blurRadius / 2),
                        Child = clipBorder
                    };

                    window.Background = System.Windows.Media.Brushes.Transparent;
                    content = border;
                    window.GlowEffect = glowEffect;   // tracked so SafeCloseFlashWindow can stop its animations + free the native blur target

                    // Host mode expands the bookkeeping rect (the gaze rect) to match the glow-expanded
                    // visual. Per-window mode must NOT resize the window here: the shell is already
                    // bucketed with slack to fit the glow, and the glow border is centered inside it
                    // (see the shell wrap below). Resizing a pooled/realized layered window is itself a
                    // synchronous-CompleteRender deadlock trigger — this used to run on every glow flash.
                    if (useHost)
                    {
                        var padding = blurRadius / 2;
                        window.Width += padding * 2;
                        window.Height += padding * 2;
                        window.Left -= padding;
                        window.Top -= padding;
                    }

                    // Pulsing golden animation for lucky procs
                    if (isLucky)
                    {
                        // Pulse relative to the (capped) base radius so the cap is respected.
                        var blurAnim = new DoubleAnimation(blurRadius, blurRadius * 1.6, TimeSpan.FromMilliseconds(400))
                        {
                            AutoReverse = true,
                            RepeatBehavior = RepeatBehavior.Forever
                        };
                        var opacityAnim = new DoubleAnimation(0.7, 1.0, TimeSpan.FromMilliseconds(400))
                        {
                            AutoReverse = true,
                            RepeatBehavior = RepeatBehavior.Forever
                        };
                        glowEffect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blurAnim);
                        glowEffect.BeginAnimation(DropShadowEffect.OpacityProperty, opacityAnim);
                    }
                    }   // !useLayer (WPF glow content build)
                }
                else if (!useLayer)
                {
                    // The image gets the black backing the window shell used to provide directly: the
                    // per-window shell background is Transparent now (so the bucket padding stays
                    // invisible), and host mode needs it because the image is a bare Canvas child.
                    content = new Border { Background = System.Windows.Media.Brushes.Black, Child = image };
                }

                if (useLayer)
                {
                    // Convert frames + spawn the layer item; the heartbeat drives it from here
                    // via window.LayerItem (fade, GIF frames, gaze dwell).
                    SpawnLayerVisual(window, imageData, monitor,
                        layerGlowColor, layerGlowRadius, layerGlowOpacity, isLucky);

                    if (!suppressHaptic)
                        _ = App.Haptics?.FlashDecayVibeAsync();
                }
                else if (useHost)
                {
                    // Size the root to the (possibly glow-expanded) bookkeeping rect; the glow
                    // border's own Padding re-insets the image, matching per-window layout.
                    content.Width = window.Width;
                    content.Height = window.Height;
                    content.Opacity = 0;
                    content.IsHitTestVisible = false;
                    window.HostedRoot = content;

                    EnsureHostRef();
                    // Flashes are the top attention layer — sit above any bubbles sharing the host.
                    System.Windows.Controls.Panel.SetZIndex(content, 1000);
                    // Mixed-DPI: the host renders at ONE scale; a flash on a differently-scaled
                    // screen compensates with a LayoutTransform or it draws displaced/mis-sized
                    // (same fix as the bubble shared host's second-screen bug).
                    var flashDpi = monitor.DpiScale > 0 ? monitor.DpiScale : 1.0;
                    var hostScale = ChaosBubbleHostOverlay.RenderScale;
                    if (hostScale > 0 && Math.Abs(hostScale - flashDpi) > 0.001)
                        content.LayoutTransform = new ScaleTransform(flashDpi / hostScale, flashDpi / hostScale);
                    ChaosBubbleHostOverlay.Add(content);
                    // Place takes PHYSICAL px; the bookkeeping rect is in this monitor's DIPs.
                    ChaosBubbleHostOverlay.Place(content, window.Left * flashDpi, window.Top * flashDpi);

                    if (!suppressHaptic)
                        _ = App.Haptics?.FlashDecayVibeAsync();
                }
                else
                {
                    // Bucket shell: the window is sized to the pool-recyclable bucket, so render the
                    // true-size flash CENTERED inside it with invisible transparent padding. Centering
                    // keeps the visual exactly where it was positioned regardless of shell/glow padding
                    // (the content's centre == the shell's centre == the intended image centre).
                    if (shellW > trueW || shellH > trueH)
                    {
                        content.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                        content.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                        content = new Grid
                        {
                            Width = shellW,
                            Height = shellH,
                            Background = System.Windows.Media.Brushes.Transparent,
                            Children = { content },
                        };
                        window.Left = finalX - (shellW - trueW) / 2.0;
                        window.Top = finalY - (shellH - trueH) / 2.0;
                    }
                    // Shell padding is invisible: the window itself must not paint an opaque background.
                    window.Background = System.Windows.Media.Brushes.Transparent;
                    window.Content = content;
                    window.Opacity = 0;

                    // Click handler + Alt+Tab hiding are wired ONCE in AcquireFlashWindow (the
                    // handler reads IsClickable per spawn) so recycled windows never stack handlers.
                    window.Cursor = settings.FlashClickable
                        ? System.Windows.Input.Cursors.Hand
                        : System.Windows.Input.Cursors.No;

                    window.Show();
                    ApplyClickability(window, settings.FlashClickable);
                    if (!suppressHaptic)
                        _ = App.Haptics?.FlashDecayVibeAsync();

                    // Force topmost even over fullscreen apps
                    ForceTopmost(window);
                }

                // PHASE F — luminance sync. One bool test when the feature is off. Runs after the
                // visual is up so it can never delay the flash, and rides the flash's own lifetime
                // through the mixer's auto-zero (no hide hook, so no way to leave the layer stuck).
                if (!suppressHaptic) ApplyLuminanceSync(imageData, lifetimeMs);

                lock (_lockObj)
                {
                    _activeWindows.Add(window);
                }
            }
            catch (Exception ex)
            {
                // If anything fails before the window is tracked, dispose the CTS so it doesn't leak~ 🧹
                App.Logger?.Debug("SpawnFlashWindow failed: {Error}", ex.Message);
                try { windowCts.Cancel(); } catch { }
                try { windowCts.Dispose(); } catch { }
                if (window != null)
                {
                    try
                    {
                        if (window.LayerItem != null)
                        {
                            _flashLayer?.Remove(window.LayerItem);
                            window.LayerItem = null;
                        }
                        if (window.HostedRoot != null)
                        {
                            ChaosBubbleHostOverlay.Remove(window.HostedRoot);
                            window.HostedRoot = null;
                        }
                        window.LifetimeCts = null;
                        // Layer state bags still registered in Application.Windows — Close them
                        // too (never shown, so Close is safe) or the failed spawn leaks a Window.
                        if (window.UsesLayer) CloseStateBagWindow(window);
                        else if (!window.UsesHost) window.Close();
                    }
                    catch { }
                }
                return;
            }

            App.Progression?.AddXP(xpAmount * multiplier, XPSource.Flash);

            // Track for achievement
            if (settings.HydraLinkedTiming || hydraGeneration == 0)
            {
                App.Achievements?.TrackFlashImage();
            }
        }

        #region Luminance Sync (Phase F)

        /// <summary>Average luminance per image file, 0..1. The sample is a fixed property of the
        /// file, so it is computed once and reused for every later flash of the same image — a
        /// 25-flash burst of repeats costs 25 dictionary hits, not 25 downscales.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> _luminanceCache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Cap on <see cref="_luminanceCache"/>. A big library would otherwise leave one
        /// entry per distinct path alive for the whole process; past this we drop the lot, which
        /// costs one 8x8 re-sample per image afterwards and needs no LRU bookkeeping.</summary>
        private const int LuminanceCacheMax = 500;

        /// <summary>Downscale target for the luminance sample. 8x8 is enough for an average and
        /// small enough that the WIC scaler's work is dominated by the (already decoded, already
        /// display-sized) source read.</summary>
        private const int LuminanceSampleSize = 8;

        /// <summary>
        /// PHASE F: push the flash's average brightness onto the continuous Luminance layer for as
        /// long as the flash is up. The layer self-clears (mixer auto-zero) after
        /// <paramref name="lifetimeMs"/>, so a flash that dies in an unusual way (panic key, engine
        /// stop, crash-path teardown) still cannot leave the toy humming.
        ///
        /// Never re-decodes: it samples the frozen BitmapSource the flash is already showing.
        /// </summary>
        private void ApplyLuminanceSync(LoadedImageData imageData, int lifetimeMs)
        {
            try
            {
                var haptics = App.Haptics;
                if (haptics == null || !haptics.Settings.LuminanceSyncEnabled) return;      // the whole cost when off
                if (imageData == null || imageData.Frames.Count == 0) return;

                var scale = haptics.Settings.LuminanceSyncIntensity;
                if (scale <= 0) return;

                var key = imageData.FilePath;
                double luminance;
                if (string.IsNullOrEmpty(key))
                {
                    luminance = SampleLuminance(imageData.Frames[0]);
                }
                else if (!_luminanceCache.TryGetValue(key!, out luminance))
                {
                    luminance = SampleLuminance(imageData.Frames[0]);
                    if (luminance >= 0)
                    {
                        if (_luminanceCache.Count >= LuminanceCacheMax) _luminanceCache.Clear();
                        _luminanceCache[key!] = luminance;
                    }
                }

                if (luminance < 0) return;      // sampling failed — stay silent rather than guess

                haptics.SetLayer(Services.Haptics.Core.HapticLayer.Luminance,
                                 luminance * Math.Clamp(scale, 0, 1),
                                 autoZeroMs: Math.Clamp(lifetimeMs, 100, 30_000));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("FlashService: luminance sync failed (non-fatal): {Error}", ex.Message);
            }
        }

        /// <summary>Average perceptual luminance (Rec. 601) of an already-decoded frame, 0..1.
        /// Returns -1 when the bitmap cannot be sampled. Downscales to 8x8 through WIC — no file
        /// access, no GDI+, no second decode.</summary>
        private static double SampleLuminance(BitmapSource? source)
        {
            try
            {
                if (source == null || source.PixelWidth <= 0 || source.PixelHeight <= 0) return -1;

                var sx = LuminanceSampleSize / (double)source.PixelWidth;
                var sy = LuminanceSampleSize / (double)source.PixelHeight;
                BitmapSource sampled = source;
                if (sx < 1.0 || sy < 1.0)
                {
                    sampled = new TransformedBitmap(source, new ScaleTransform(Math.Min(sx, 1.0), Math.Min(sy, 1.0)));
                }
                if (sampled.Format != System.Windows.Media.PixelFormats.Bgra32)
                {
                    sampled = new FormatConvertedBitmap(sampled, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                }

                var w = sampled.PixelWidth;
                var h = sampled.PixelHeight;
                if (w <= 0 || h <= 0) return -1;

                var stride = w * 4;
                var pixels = new byte[stride * h];
                sampled.CopyPixels(pixels, stride, 0);

                double sum = 0;
                double weight = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    // Bgra32 is premultiplied-free straight alpha here; a transparent pixel
                    // contributes nothing (a mostly-transparent PNG is not "bright").
                    double alpha = pixels[i + 3] / 255.0;
                    if (alpha <= 0) continue;
                    double b = pixels[i] / 255.0;
                    double g = pixels[i + 1] / 255.0;
                    double r = pixels[i + 2] / 255.0;
                    sum += (0.299 * r + 0.587 * g + 0.114 * b) * alpha;
                    weight += alpha;
                }

                if (weight <= 0) return 0;
                return Math.Clamp(sum / weight, 0, 1);
            }
            catch
            {
                return -1;
            }
        }

        #endregion

        /// <summary>
        /// COMPOSITOR: convert the decoded frames and spawn this flash's layer item. The
        /// bookkeeping rect on <paramref name="window"/> (DIPs, already glow-expanded) converts
        /// to world px with the spawn monitor's own scale — the same math as host mode's Place.
        /// The per-frame pixel copies (up to 60 frames × multi-MB images × concurrent spawns)
        /// used to run synchronously on the dispatcher and hitched the UI at spawn — they now
        /// run in Task.Run (SpiralLayer.ShowFrames pattern) and the layer item spawns on the
        /// dispatcher when the conversion lands. window.LayerSpawnPending keeps the heartbeat
        /// from sweeping the still-itemless window during the conversion window.
        /// </summary>
        private void SpawnLayerVisual(FlashWindow window, LoadedImageData imageData, MonitorInfo monitor,
            System.Windows.Media.Color glowColor, double glowRadius, double glowOpacity, bool luckyPulse)
        {
            if (_flashLayer == null)
            {
                _flashLayer = new Compositor.FlashLayer(App.Compositor!);
                App.Compositor!.RegisterLayer(_flashLayer);
            }
            var layer = _flashLayer;

            // Snapshot everything the worker/continuation reads. The frames are frozen
            // BitmapSources (LoadImageAsync and AnimatedWebp freeze every frame), so reading
            // them from a worker thread is safe.
            var sourceFrames = imageData.Frames.ToArray();
            var dpi = monitor.DpiScale > 0 ? monitor.DpiScale : 1.0;
            var hasGlow = glowRadius > 0;
            var x = (float)(window.Left * dpi);
            var y = (float)(window.Top * dpi);
            var w = (float)(window.Width * dpi);
            var h = (float)(window.Height * dpi);
            var paddingPx = (float)(hasGlow ? glowRadius / 2 * dpi : 0);
            var cornerRadiusPx = hasGlow ? (float)(12 * dpi) : 0f;
            var skGlowColor = new SkiaSharp.SKColor(glowColor.R, glowColor.G, glowColor.B);
            var glowSigmaPx = (float)(glowRadius * dpi / 3.0);   // WPF blur radius -> sigma (R/3)

            window.LayerSpawnPending = true;

            _ = Task.Run(() =>
            {
                // Straight pixel copies of the already-decoded (display-sized, cached) frames — no
                // SKCodec decode here, so the global decode gate doesn't apply.
                SkiaSharp.SKImage[]? frames = null;
                try
                {
                    frames = new SkiaSharp.SKImage[sourceFrames.Length];
                    for (int i = 0; i < frames.Length; i++)
                        frames[i] = Compositor.SkiaWpfInterop.ToSKImage(sourceFrames[i]);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("SpawnLayerVisual: frame conversion failed: {E}", ex.Message);
                    DisposeLayerFrames(frames);
                    frames = null;
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted)
                {
                    DisposeLayerFrames(frames);
                    window.LayerSpawnPending = false;   // plain CLR property — safe off-thread
                    return;
                }

                dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        window.LayerSpawnPending = false;
                        if (frames == null)
                            return;   // conversion failed — the heartbeat sweeps the itemless window

                        // The flash may have been clicked away, expired or torn down (Stop /
                        // CloseAllWindows) while converting — dispose instead of spawning.
                        bool tracked;
                        lock (_lockObj) { tracked = _activeWindows.Contains(window); }
                        if (!tracked || window.IsFadingOut || (!_isRunning && !_oneShotActive)
                            || window.LifetimeCts == null || window.LifetimeCts.IsCancellationRequested)
                        {
                            DisposeLayerFrames(frames);
                            return;
                        }

                        window.LayerItem = layer.Spawn(frames, x, y, w, h,
                            paddingPx, cornerRadiusPx, skGlowColor, glowSigmaPx,
                            glowOpacity, luckyPulse);
                        frames = null;   // ownership transferred — FlashLayer.Remove disposes them

                        if (window.IsClickable)
                            EnsureLayerHook();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("SpawnLayerVisual: layer spawn failed: {E}", ex.Message);
                        DisposeLayerFrames(frames);
                    }
                });
            });
        }

        /// <summary>Dispose a (possibly partially filled) converted-frame array.</summary>
        private static void DisposeLayerFrames(SkiaSharp.SKImage[]? frames)
        {
            if (frames == null) return;
            foreach (var f in frames)
            {
                try { f?.Dispose(); } catch { }
            }
        }

        /// <summary>Install the click hook for layer flashes (UI thread — the hook needs this
        /// thread's message loop). Idempotent; released when no layer flashes remain.</summary>
        private void EnsureLayerHook()
        {
            if (_layerHook != null) return;
            _layerHook = new GlobalMouseHook { LeftDown = OnLayerFlashLeftDown };
            _layerHook.Start();
        }

        private void ReleaseLayerHook()
        {
            if (_layerHook == null) return;
            try { _layerHook.Dispose(); } catch { }
            _layerHook = null;
            _layerHits = Array.Empty<LayerHit>();
        }

        /// <summary>
        /// HOOK THREAD: hit-test a physical-px click against the layer-flash snapshot, topmost
        /// (most recent spawn) first. A hit swallows the click — exactly what a clickable flash
        /// window did by consuming it — and pops on the dispatcher.
        /// </summary>
        private bool OnLayerFlashLeftDown(System.Windows.Point px)
        {
            // Clicks inside a playing mandatory video's rect belong to the video (attention
            // checks) — the flash there is pinned below it and invisible, so never swallow.
            var exclude = _layerVideoExcludePx;
            if (exclude != null
                && px.X >= exclude[0] && px.X <= exclude[0] + exclude[2]
                && px.Y >= exclude[1] && px.Y <= exclude[1] + exclude[3])
                return false;

            var hits = _layerHits;
            for (int i = hits.Length - 1; i >= 0; i--)
            {
                var hit = hits[i];
                if (px.X < hit.X || px.X > hit.X + hit.W || px.Y < hit.Y || px.Y > hit.Y + hit.H)
                    continue;
                var win = hit.Win;
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    try
                    {
                        // Re-check on the UI thread — the flash may have expired since the snapshot.
                        if (!win.IsFadingOut && win.LayerItem != null)
                            OnFlashClicked(win, App.Settings.Current);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Layer flash pop failed: {E}", ex.Message);
                    }
                });
                return true;
            }
            return false;
        }

        /// <summary>
        /// Refresh the hook-thread hit snapshot from the live layer items (heartbeat cadence),
        /// and drop the hook once the last layer flash is gone.
        /// </summary>
        private void RebuildLayerHitSnapshot()
        {
            if (_layerHook == null) return;
            List<LayerHit>? hits = null;
            bool anyLayer = false;
            lock (_lockObj)
            {
                foreach (var w in _activeWindows)
                {
                    if (!w.UsesLayer) continue;
                    anyLayer = true;
                    var item = w.LayerItem;
                    if (item == null || !w.IsClickable || w.IsFadingOut) continue;
                    (hits ??= new List<LayerHit>()).Add(new LayerHit
                    {
                        Win = w,
                        X = item.X, Y = item.Y, W = item.W, H = item.H
                    });
                }
            }
            _layerHits = hits?.ToArray() ?? Array.Empty<LayerHit>();

            // Publish the playing video's physical rect so the hook thread won't swallow
            // clicks on a flash the video is covering (the host sits pinned below the video).
            float[]? exclude = null;
            try
            {
                if (App.Video?.IsPlaying == true && App.Video.PrimaryVideoWindow is Window vw)
                {
                    var vh = new System.Windows.Interop.WindowInteropHelper(vw).Handle;
                    if (vh != IntPtr.Zero && NativeMethods.GetWindowRect(vh, out var r))
                        exclude = new float[] { r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top };
                }
            }
            catch { }
            _layerVideoExcludePx = exclude;

            if (!anyLayer) ReleaseLayerHook();
        }

        /// <param name="fromGaze">True when a gaze dwell/blink popped this flash rather than the mouse.</param>
        private void OnFlashClicked(FlashWindow window, AppSettings settings, bool fromGaze = false)
        {
            // Cancel only THIS window's lifetime — other windows keep living~ ✨
            try { window.LifetimeCts?.Cancel(); } catch { }

            lock (_lockObj)
            {
                _activeWindows.Remove(window);
            }

            SafeCloseFlashWindow(window);
            FlashClicked?.Invoke(this, EventArgs.Empty);
            _ = App.Haptics?.FlashClickVibeAsync();

            // Hydra mode: spawn 2 more when clicking (NO NEW AUDIO)
            // No global _cleanupInProgress check needed — each window has its own lifetime~ 🐍
            // #784: a gaze pop is automatic — the dwell attractor snaps straight onto the fresh
            // children and pops them ~1s later, so an unrestricted gaze→hydra chain feeds itself
            // and flashes never stop spawning (the population cap only bounds how many are on
            // screen at once, not the endless churn). Let gaze take the FIRST hop off an original
            // flash (the documented "stare to pop = click, including hydra") and stop there;
            // children of a gaze pop just dismiss. Mouse clicks are unchanged — a human hand is
            // the throttle there.
            if (settings.CorruptionMode && (!fromGaze || window.HydraGeneration == 0))
            {
                var maxHydra = Math.Min(settings.HydraLimit, 20);
                int currentCount;
                lock (_lockObj)
                {
                    currentCount = _activeWindows.Count;
                }

                if (currentCount + 1 < maxHydra)
                {
                    // Calculate remaining lifetime from the clicked window for linked timing~ 🔗
                    var remainingMs = Math.Max(1000, (int)(window.ExpiresAt - DateTime.Now).TotalMilliseconds);
                    TriggerMultiplication(maxHydra, currentCount, window.OriginalLifetimeMs, remainingMs, window.HydraGeneration, window.Monitor);
                }
            }
        }

        /// <summary>
        /// Spawns hydra children when a flash window is clicked~ 🐙
        /// CopilotNotes: parentLifetimeMs is the full original duration; parentRemainingMs is what's left on the clicked window's timer.
        /// When HydraLinkedTiming is true, children get parentRemainingMs; when false, they get a fresh full lifetime.
        /// parentGeneration is the clicked window's generation — children will be parentGeneration + 1.
        /// </summary>
        private async void TriggerMultiplication(int maxHydra, int currentCount, int parentLifetimeMs, int parentRemainingMs, int parentGeneration, MonitorInfo? parentMonitor = null)
        {
            try
            {
                if (!_isRunning && !_oneShotActive) return;

                var spaceAvailable = maxHydra - currentCount;
                var numToSpawn = Math.Min(2, spaceAvailable);

                if (numToSpawn <= 0) return;

                var settings = App.Settings.Current;
                var images = GetNextImages(numToSpawn);
                if (images.Count == 0) return;

                var scale = settings.ImageScale / 100.0;

                // Decide hydra spawn lifetime based on the Linked timing setting~ 🔗✨
                var hydraLifetimeMs = settings.HydraLinkedTiming
                    ? parentRemainingMs   // Linked: inherits whatever time the parent had left
                    : parentLifetimeMs;   // Independent: gets a fresh full-duration lifetime

                var childGeneration = parentGeneration + 1;

                var loadTasks = images.Select(imagePath => LoadImageAsync(imagePath)).ToArray();
                var results = await Task.WhenAll(loadTasks);

                // Safety: check app is still alive after await
                if (System.Windows.Application.Current?.Dispatcher == null) return;

                var loadedImages = new List<LoadedImageData>();
                foreach (var data in results)
                {
                    if (data != null)
                    {
                        // Hydra children stay on the parent's screen (preferred
                        // monitor). When no parent monitor is known, PickMonitor
                        // falls through to the calibration clamp / random pick.
                        var monitor = PickMonitor(settings, parentMonitor);
                        var geometry = CalculateGeometry(data.Width, data.Height, monitor, scale);
                        data.Geometry = geometry;
                        data.Monitor = monitor;
                        loadedImages.Add(data);
                    }
                }

                if (loadedImages.Count > 0)
                {
                    var capturedLifetime = hydraLifetimeMs;
                    var capturedGeneration = childGeneration;
                    await DispatcherHelper.RunOnUIAsync(() =>
                    {
                        // Pass null for sound - NO AUDIO FOR HYDRA
                        ShowImages(loadedImages, null, true, capturedLifetime, capturedGeneration);
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "FlashService: TriggerMultiplication failed");
            }
        }

        #endregion

        #region Heartbeat & Animation

        // Old 33ms-tick fade step was 0.08/tick — same speed, expressed per second.
        private const double FADE_PER_SEC = 2.4;

        /// <summary>Subscribe the heartbeat to the composition clock (idempotent, any thread).</summary>
        private void StartHeartbeat()
        {
            var disp = System.Windows.Application.Current?.Dispatcher;
            if (disp == null) return;
            void Sub()
            {
                if (_heartbeatOn) return;
                _heartbeatOn = true;
                _lastHeartbeat = TimeSpan.MinValue;
                CompositionTarget.Rendering += Heartbeat_Render;
            }
            if (disp.CheckAccess()) Sub(); else disp.BeginInvoke((Action)Sub);
        }

        /// <summary>Unsubscribe the heartbeat (idempotent, any thread). Important: a live
        /// Rendering subscription forces WPF to render continuously, so it only runs while
        /// flashes are actually active.</summary>
        private void StopHeartbeat()
        {
            var disp = System.Windows.Application.Current?.Dispatcher;
            if (disp == null) return;
            void Unsub()
            {
                if (!_heartbeatOn) return;
                _heartbeatOn = false;
                CompositionTarget.Rendering -= Heartbeat_Render;
            }
            if (disp.CheckAccess()) Unsub(); else disp.BeginInvoke((Action)Unsub);
        }

        private void Heartbeat_Render(object? sender, EventArgs e)
        {
            // True delta time from the composition clock: baseline on the first frame,
            // skip duplicate callbacks, clamp after a stall so fades can't jump.
            double dt = 0.033;
            if (e is RenderingEventArgs r)
            {
                if (_lastHeartbeat == TimeSpan.MinValue) { _lastHeartbeat = r.RenderingTime; return; }
                dt = (r.RenderingTime - _lastHeartbeat).TotalSeconds;
                if (dt <= 0) return;
                _lastHeartbeat = r.RenderingTime;
                if (dt > 0.1) dt = 0.1;
            }
            Heartbeat_Tick(dt);
        }

        private void Heartbeat_Tick(double dt)
        {
            if (!_isRunning && !_oneShotActive) return;

            var settings = App.Settings.Current;
            var maxAlpha = Math.Min(1.0, Math.Max(0.0, settings.FlashOpacity / 100.0));
            var fadeStep = FADE_PER_SEC * dt;

            FlashWindow[] windowsCopy;
            lock (_lockObj)
            {
                // Reuse snapshot array when size matches to avoid per-frame allocation
                if (_windowsSnapshot.Length != _activeWindows.Count)
                    _windowsSnapshot = new FlashWindow[_activeWindows.Count];
                _activeWindows.CopyTo(_windowsSnapshot);
                windowsCopy = _windowsSnapshot;
            }

            var toRemove = new List<FlashWindow>();

            foreach (var window in windowsCopy)
            {
                try
                {
                    // Host-mode flashes have no hwnd — alive means their visual is still on the
                    // shared canvas (layer mode: still on the compositor, OR its off-thread
                    // frame conversion is still pending — don't sweep a flash that hasn't had
                    // the chance to spawn its layer item yet). Per-window flashes keep the
                    // loaded/visible liveness check.
                    if (window.UsesLayer ? (window.LayerItem == null && !window.LayerSpawnPending)
                        : window.UsesHost ? window.HostedRoot == null
                        : (!window.IsLoaded || !window.IsVisible))
                    {
                        toRemove.Add(window);
                        continue;
                    }

                    // Per-window fade control — each window manages its own lifetime~ 🌸
                    var showThisWindow = DateTime.Now < window.ExpiresAt && !window.IsFadingOut;
                    var targetAlpha = showThisWindow ? maxAlpha : 0.0;

                    // Fade in/out per-window~ uwu (VisualOpacity routes to the hosted root in solid mode)
                    var currentAlpha = window.VisualOpacity;
                    if (targetAlpha > currentAlpha)
                    {
                        window.VisualOpacity = Math.Min(targetAlpha, currentAlpha + fadeStep);
                    }
                    else if (targetAlpha < currentAlpha)
                    {
                        var newAlpha = Math.Max(0.0, currentAlpha - fadeStep);
                        window.VisualOpacity = newAlpha;

                        if (newAlpha <= 0)
                        {
                            toRemove.Add(window);
                            continue;
                        }
                    }

                    // Animate GIF frames
                    if (window.Frames.Count > 1 && (window.ImageControl != null || window.LayerItem != null))
                    {
                        var elapsed = DateTime.Now - window.StartTime;
                        var frameIndex = (int)(elapsed.TotalMilliseconds / window.FrameDelay.TotalMilliseconds) % window.Frames.Count;

                        if (frameIndex != window.CurrentFrameIndex)
                        {
                            window.CurrentFrameIndex = frameIndex;
                            if (window.LayerItem != null)
                                window.LayerItem.FrameIndex = frameIndex;
                            else
                                window.ImageControl!.Source = window.Frames[frameIndex];
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.Debug("Heartbeat error: {Error}", ex.Message);
                    toRemove.Add(window);
                }
            }

            // Clean up windows
            foreach (var window in toRemove)
            {
                SafeCloseFlashWindow(window);

                lock (_lockObj)
                {
                    _activeWindows.Remove(window);
                }
            }

            if (toRemove.Count > 0)
                App.Overlay?.NotifyTopWindowClosed();

            // Layer flashes: refresh the click-hook hit snapshot (positions are static but
            // items expire), and release the hook once the last one is gone.
            RebuildLayerHitSnapshot();

            // Clear stale references in snapshot so removed windows can be GC'd
            Array.Clear(_windowsSnapshot, 0, _windowsSnapshot.Length);
        }

        #endregion

        #region Monitor Support

        /// <summary>
        /// Selects a monitor for the next flash spawn. Resolution order:
        ///   1. Preferred (hydra inheritance): when <paramref name="preferred"/>
        ///      is supplied (passed by TriggerMultiplication so children stay
        ///      on the parent's screen) and exists in the candidate list,
        ///      return it.
        ///   2. Random pick from GetMonitors(DualMonitorEnabled).
        /// Flashes are baseline content — they do not consult the gaze
        /// calibration clamp. Off-cal-screen flashes are filtered out of
        /// gaze-pop / gaze-linger interaction by GazeFocusService.FindBestTarget;
        /// mouse-click works everywhere.
        /// </summary>
        private MonitorInfo PickMonitor(AppSettings settings, MonitorInfo? preferred = null)
        {
            var candidates = GetMonitors(settings.DualMonitorEnabled);

            // Hydra inheritance: keep children on the parent's screen.
            if (preferred != null)
            {
                foreach (var m in candidates)
                {
                    if (m.X == preferred.X && m.Y == preferred.Y
                        && m.Width == preferred.Width && m.Height == preferred.Height)
                        return m;
                }
            }

            return candidates[_random.Next(candidates.Count)];
        }

        private List<MonitorInfo> GetMonitors(bool dualMonitor)
        {
            var monitors = new List<MonitorInfo>();

            try
            {
                foreach (var screen in App.GetAllScreensCached())
                {
                    // Get DPI scale for THIS specific screen (not just primary)
                    var dpiScale = GetDpiForScreen(screen);

                    // Convert from physical pixels to WPF device-independent pixels
                    monitors.Add(new MonitorInfo
                    {
                        X = (int)(screen.Bounds.X / dpiScale),
                        Y = (int)(screen.Bounds.Y / dpiScale),
                        Width = (int)(screen.Bounds.Width / dpiScale),
                        Height = (int)(screen.Bounds.Height / dpiScale),
                        IsPrimary = screen.Primary,
                        DpiScale = dpiScale
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger.Debug("Could not enumerate monitors: {Error}", ex.Message);
            }

            if (monitors.Count == 0)
            {
                // SystemParameters already returns DIPs, so no conversion needed
                monitors.Add(new MonitorInfo
                {
                    X = 0,
                    Y = 0,
                    Width = (int)SystemParameters.PrimaryScreenWidth,
                    Height = (int)SystemParameters.PrimaryScreenHeight,
                    IsPrimary = true
                });
            }

            // If dual monitor is disabled, only use primary
            if (!dualMonitor)
            {
                var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
                return new List<MonitorInfo> { primary };
            }

            return monitors;
        }
        
        private double GetDpiForScreen(Screen screen)
        {
            try
            {
                uint dpiX = 96, dpiY = 96;
                var hMonitor = MonitorFromPoint(new POINT { X = screen.Bounds.X + 1, Y = screen.Bounds.Y + 1 }, 2);

                if (hMonitor != IntPtr.Zero)
                {
                    var result = GetDpiForMonitor(hMonitor, 0, out dpiX, out dpiY);
                    if (result == 0)
                    {
                        return dpiX / 96.0;
                    }
                }

                using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
                return g.DpiX / 96.0;
            }
            catch
            {
                return 1.0;
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        private ImageGeometry CalculateGeometry(int origWidth, int origHeight, MonitorInfo monitor, double scale)
        {
            // Base size is 40% of monitor dimensions (matching Python)
            var baseWidth = monitor.Width * 0.4;
            var baseHeight = monitor.Height * 0.4;
            
            // Calculate scale ratio to fit within base size while maintaining aspect ratio
            // Then multiply by user's scale setting (0.5 to 2.5)
            var ratio = Math.Min(baseWidth / origWidth, baseHeight / origHeight) * scale;
            
            var targetWidth = Math.Max(50, (int)(origWidth * ratio));
            var targetHeight = Math.Max(50, (int)(origHeight * ratio));

            // Random position within monitor bounds with edge padding, honoring the #770
            // avoid-the-center exclusion box. PickSpawnPoint is the ONE spawn-point computation —
            // the anti-overlap retry loop in ShowSingleImage re-rolls through it too, or overlapping
            // flashes would punch straight back into the crosshair.
            var (x, y) = PickSpawnPoint(monitor, targetWidth, targetHeight);

            return new ImageGeometry
            {
                X = x,
                Y = y,
                Width = targetWidth,
                Height = targetHeight
            };
        }

        /// <summary>
        /// Keep targets away from screen edges so they're fully visible and clickable.
        /// </summary>
        internal const int SpawnEdgePadding = 50;

        /// <summary>
        /// Picks a top-left spawn point (in virtual-desktop DIPs) for a <paramref name="w"/>x<paramref name="h"/>
        /// image on <paramref name="monitor"/>. Applies the #770 centered exclusion box when the user
        /// has it on. Shared by <see cref="CalculateGeometry"/> and the anti-overlap retry loop.
        /// </summary>
        private (int X, int Y) PickSpawnPoint(MonitorInfo monitor, int w, int h)
        {
            var s = App.Settings?.Current;
            bool avoid = s?.FlashAvoidCenter == true;
            int pct = s?.FlashCenterExclusionPercent ?? 25;

            if (avoid &&
                TryPickAvoidCenterPointAdaptive(monitor.Width, monitor.Height, w, h, pct, _random,
                    out int lx, out int ly, out _))
            {
                return (monitor.X + lx, monitor.Y + ly);
            }

            if (avoid)
            {
                // Even a floor-sized box leaves nowhere legal — the image is bigger than the whole
                // padded spawn area. Fall back to an unconstrained pick rather than never spawning.
                // Warning, not Debug: with the feature ON this silently puts flashes back on the
                // crosshair, which is exactly the complaint #770 exists to fix. Keyed on the full
                // geometry so a different image size / monitor / percentage still gets reported.
                var key = (w, h, monitor.Width, monitor.Height, pct);
                bool firstForThisGeometry;
                lock (_avoidCenterFallbackLogged)
                    firstForThisGeometry = _avoidCenterFallbackLogged.Add(key);

                if (firstForThisGeometry)
                {
                    App.Logger.Warning(
                        "Flash avoid-center: no legal band for {W}x{H} on {MW}x{MH} even at the {Floor}% floor — falling back to unconstrained placement (once per geometry; requested {Pct}%)",
                        w, h, monitor.Width, monitor.Height, MinExclusionPercent, pct);
                }
            }

            var (minX, minY, maxX, maxY) = SpawnBounds(monitor.Width, monitor.Height, w, h);
            return (monitor.X + _random.Next(minX, maxX), monitor.Y + _random.Next(minY, maxY));
        }

        /// <summary>
        /// Geometries (image w/h, monitor w/h, requested pct) already reported as unplaceable, so the
        /// warning above stays once-per-geometry instead of once-per-process (a single bool hid every
        /// later, different failure) or once-per-flash.
        /// </summary>
        private readonly HashSet<(int W, int H, int MonW, int MonH, int Pct)> _avoidCenterFallbackLogged = new();

        /// <summary>
        /// The unconstrained legal range for a top-left spawn point, in monitor-local DIPs.
        /// Ranges are half-open on the max end, matching <see cref="Random.Next(int,int)"/>.
        /// </summary>
        internal static (int MinX, int MinY, int MaxX, int MaxY) SpawnBounds(int monW, int monH, int w, int h)
        {
            var minX = SpawnEdgePadding;
            var minY = SpawnEdgePadding;
            var maxX = Math.Max(minX + 1, monW - w - SpawnEdgePadding);
            var maxY = Math.Max(minY + 1, monH - h - SpawnEdgePadding);
            return (minX, minY, maxX, maxY);
        }

        /// <summary>
        /// #770 — band remap (NOT rejection sampling). Builds the centered exclusion square
        /// (<paramref name="pct"/>% of the SHORTER monitor edge, per-monitor) and splits the legal
        /// area into 4 DISJOINT bands where a <paramref name="w"/>x<paramref name="h"/> image fits
        /// without touching it: left / right / above / below. A band is picked weighted by its area
        /// and the point is then uniform inside it, so the result is uniform over the whole legal
        /// region in a single roll — no retry loop, no worst-case starvation.
        /// </summary>
        /// <returns>false when the total legal area is 0 (image too large); caller falls back.</returns>
        internal static bool TryPickAvoidCenterPoint(
            int monW, int monH, int w, int h, int pct, Random random, out int x, out int y)
        {
            x = y = 0;

            var b = new AvoidCenterBands(monW, monH, w, h, pct);
            long total = b.TotalArea;
            if (total <= 0) return false;

            // Weighted band pick, then uniform inside the chosen band.
            long roll = (long)(random.NextDouble() * total);
            if (roll >= total) roll = total - 1; // guard the 1.0 edge
            Span<long> areas = stackalloc long[4] { b.LeftArea, b.RightArea, b.AboveArea, b.BelowArea };
            int band = 0;
            for (; band < 3; band++)
            {
                if (roll < areas[band]) break;
                roll -= areas[band];
            }

            switch (band)
            {
                case 0: x = random.Next(b.MinX, b.LeftMaxX); y = random.Next(b.MinY, b.MaxY); break;
                case 1: x = random.Next(b.RightMinX, b.MaxX); y = random.Next(b.MinY, b.MaxY); break;
                case 2: x = random.Next(b.StripMinX, b.StripMaxX); y = random.Next(b.MinY, b.AboveMaxY); break;
                default: x = random.Next(b.StripMinX, b.StripMaxX); y = random.Next(b.BelowMinY, b.MaxY); break;
            }
            return true;
        }

        /// <summary>Smallest exclusion box the adaptive shrink will fall back to, in percent.</summary>
        internal const int MinExclusionPercent = 5;

        /// <summary>Largest exclusion box the setting allows, in percent (matches the AppSettings clamp).</summary>
        internal const int MaxExclusionPercent = 60;

        /// <summary>
        /// How much of the padded spawn area must remain legal before the requested exclusion box is
        /// accepted. Below this the placement is technically legal but visually degenerate — at the
        /// default 25% a monitor-aspect image at ImageScale=100 leaves 1.4%, i.e. two ~8px-wide
        /// slivers, so every flash lands in the same two columns.
        /// </summary>
        internal const double MinLegalAreaFraction = 0.10;

        /// <summary>
        /// #770 follow-up — the exclusion box has to DEGRADE, not vanish. A flash at the default
        /// ImageScale is 40% of the monitor's width, so on 1920x1080 an ordinary 768x432 flash has no
        /// legal band at all at 30% (the feature silently became a no-op and flashes went back to the
        /// crosshair) and only 1.4% of the spawn area at the default 25%. This shrinks the effective
        /// percentage 5 points at a time, down to <see cref="MinExclusionPercent"/>, until at least
        /// <see cref="MinLegalAreaFraction"/> of the spawn area is legal, and picks with THAT box.
        /// Legal area is monotonic in the percentage (a smaller square is a subset of a bigger one),
        /// so the first percentage that clears the bar is also the largest one that does — the user's
        /// setting is honoured as far as the image size allows.
        /// </summary>
        /// <param name="effectivePct">The percentage actually used (&lt;= the requested one).</param>
        /// <returns>false only when even the floor leaves nothing (image bigger than the spawn area).</returns>
        internal static bool TryPickAvoidCenterPointAdaptive(
            int monW, int monH, int w, int h, int pct, Random random,
            out int x, out int y, out int effectivePct)
        {
            x = y = 0;
            effectivePct = Math.Clamp(pct, MinExclusionPercent, MaxExclusionPercent);

            while (true)
            {
                var bands = new AvoidCenterBands(monW, monH, w, h, effectivePct);
                if (bands.LegalFraction >= MinLegalAreaFraction || effectivePct <= MinExclusionPercent)
                {
                    if (bands.TotalArea <= 0) return false;   // at the floor and still nowhere to go
                    return TryPickAvoidCenterPoint(monW, monH, w, h, effectivePct, random, out x, out y);
                }
                effectivePct = Math.Max(MinExclusionPercent, effectivePct - 5);
            }
        }

        /// <summary>
        /// Legal band area as a fraction (0..1) of the unconstrained padded spawn area. Exposed for
        /// the tests that pin the adaptive shrink's "at least 10% of the screen stays usable" bar.
        /// </summary>
        internal static double LegalAreaFraction(int monW, int monH, int w, int h, int pct)
            => new AvoidCenterBands(monW, monH, w, h, pct).LegalFraction;

        /// <summary>
        /// The 4 disjoint legal bands around the #770 exclusion square plus their areas. One place so
        /// the pick and the adaptive shrink can never measure different regions.
        /// </summary>
        private readonly struct AvoidCenterBands
        {
            public readonly int MinX, MinY, MaxX, MaxY;
            public readonly int LeftMaxX, RightMinX, StripMinX, StripMaxX, AboveMaxY, BelowMinY;
            public readonly long LeftArea, RightArea, AboveArea, BelowArea;

            public AvoidCenterBands(int monW, int monH, int w, int h, int pct)
            {
                (MinX, MinY, MaxX, MaxY) = SpawnBounds(monW, monH, w, h);

                // Centered exclusion square, sized off the shorter edge so it stays square on ultrawides.
                double side = Math.Clamp(pct, MinExclusionPercent, MaxExclusionPercent) / 100.0 * Math.Min(monW, monH);
                int exLeft = (int)Math.Round((monW - side) / 2.0);
                int exTop = (int)Math.Round((monH - side) / 2.0);
                int exRight = exLeft + (int)Math.Round(side);
                int exBottom = exTop + (int)Math.Round(side);

                // An image at local (x,y) misses the box iff it is fully left (x + w <= exLeft),
                // fully right (x >= exRight), fully above (y + h <= exTop) or fully below (y >= exBottom).
                // Left/right take the FULL y range; above/below take only the x-strip left over between
                // them, which keeps the 4 bands disjoint so area weighting stays a true uniform.
                LeftMaxX = Math.Min(MaxX, exLeft - w + 1);   // exclusive
                RightMinX = Math.Max(MinX, exRight);
                StripMinX = Math.Max(MinX, LeftMaxX);
                StripMaxX = Math.Min(MaxX, RightMinX);       // exclusive
                AboveMaxY = Math.Min(MaxY, exTop - h + 1);   // exclusive
                BelowMinY = Math.Max(MinY, exBottom);

                LeftArea = Area(MinX, LeftMaxX, MinY, MaxY);
                RightArea = Area(RightMinX, MaxX, MinY, MaxY);
                AboveArea = Area(StripMinX, StripMaxX, MinY, AboveMaxY);
                BelowArea = Area(StripMinX, StripMaxX, BelowMinY, MaxY);
            }

            public long TotalArea => LeftArea + RightArea + AboveArea + BelowArea;

            /// <summary>Unconstrained padded spawn area, the denominator for <see cref="LegalFraction"/>.</summary>
            public long SpawnArea => (long)Math.Max(0, MaxX - MinX) * Math.Max(0, MaxY - MinY);

            public double LegalFraction => SpawnArea <= 0 ? 0.0 : (double)TotalArea / SpawnArea;

            private static long Area(int x0, int x1, int y0, int y1)
                => (long)Math.Max(0, x1 - x0) * Math.Max(0, y1 - y0);
        }

        private bool IsOverlapping(int x, int y, int w, int h)
        {
            lock (_lockObj)
            {
                foreach (var window in _activeWindows)
                {
                    try
                    {
                        var wx = (int)window.Left;
                        var wy = (int)window.Top;
                        var ww = (int)window.Width;
                        var wh = (int)window.Height;

                        var dx = Math.Min(x + w, wx + ww) - Math.Max(x, wx);
                        var dy = Math.Min(y + h, wy + wh) - Math.Max(y, wy);

                        if (dx >= 0 && dy >= 0)
                        {
                            var overlapArea = dx * dy;
                            var windowArea = w * h;
                            if (overlapArea > windowArea * 0.3)
                                return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Error checking window overlap: {Error}", ex.Message);
                    }
                }
            }
            return false;
        }

        #endregion

        #region Media Queue

        private List<string> GetNextImages(int count)
        {
            lock (_lockObj)
            {
                // Periodically clean temp pack files instead of letting the list grow unbounded
                if (_tempPackFiles.Count > 50)
                {
                    CleanupTempPackFiles();
                }

                // Refresh image lists if empty (first call, or after ClearFileCache/LoadAssets
                // emptied the pools so a selection change takes effect on this draw)
                if (_imageList.Count == 0 && _packImageList.Count == 0)
                {
                    RefreshImageLists();
                }

                // Safety net: never draw something the user has since deselected, even if whoever
                // wrote DisabledAssetPaths forgot to invalidate the pools.
                PruneDeselectedFromPools();

                // If both lists are empty after refresh, no images available
                if (_imageList.Count == 0 && _packImageList.Count == 0)
                {
                    return new List<string>();
                }

                var result = new List<string>(count);
                for (int i = 0; i < count; i++)
                {
                    // Randomly choose between regular and pack images based on what's available
                    bool usePackImage = false;
                    if (_imageList.Count > 0 && _packImageList.Count > 0)
                    {
                        // Both available - pick randomly weighted by count
                        var totalCount = _imageList.Count + _packImageList.Count;
                        usePackImage = _random.Next(totalCount) >= _imageList.Count;
                    }
                    else if (_packImageList.Count > 0)
                    {
                        usePackImage = true;
                    }

                    if (usePackImage && _packImageList.Count > 0)
                    {
                        // Randomly select a pack image (true random, not sequential)
                        var index = _random.Next(_packImageList.Count);
                        var packImage = _packImageList[index];
                        // Decrypt pack image to temp file
                        var tempPath = App.ContentPacks?.GetPackFileTempPath(packImage.PackId, packImage.File);
                        if (!string.IsNullOrEmpty(tempPath))
                        {
                            _tempPackFiles.Add(tempPath);  // Track for cleanup
                            result.Add(tempPath);
                            App.Logger?.Debug("Using pack image: {Name} from pack {PackId}", packImage.File.OriginalName, packImage.PackId);
                            continue;
                        }
                        // If decryption failed, try regular list
                    }

                    if (_imageList.Count > 0)
                    {
                        // Randomly select an image (true random, not sequential)
                        var index = _random.Next(_imageList.Count);
                        result.Add(_imageList[index]);
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// Random, ready-to-load image paths drawn from the EXACT same enabled pool the flashes
        /// use: disk images (recursed, honoring the asset manager's disabled set) AND active
        /// content-pack images. Pack images are decrypted to a temp file on demand and tracked for
        /// cleanup here, just like a normal flash. Returns fewer than requested only when the
        /// enabled pool is smaller; empty when nothing is enabled.
        ///
        /// This is what lets the Chaos "glitch" wash and "cascade" gif-rain match the user's live
        /// preset — previously they re-listed the raw images folder (ChaosImagePool), which ignored
        /// both disabled assets and content packs, so they silently drew nothing for pack/curated
        /// users while flashes worked. Picks are DISTINCT (unlike the flash pipeline's independent,
        /// with-replacement picks): a single wash/rain that repeats the same image 2-3x looks broken,
        /// so dedup on the source identity here. May do disk I/O (pack decrypt, one per chosen pack
        /// image) — call OFF the UI thread when requesting more than a couple.
        /// </summary>
        public List<string> GetChaosImagePaths(int count)
        {
            count = Math.Max(0, count);
            if (count == 0) return new List<string>();
            lock (_lockObj)
            {
                if (_tempPackFiles.Count > 50) CleanupTempPackFiles();
                if (_imageList.Count == 0 && _packImageList.Count == 0) RefreshImageLists();
                PruneDeselectedFromPools();
                if (_imageList.Count == 0 && _packImageList.Count == 0) return new List<string>();

                // Dedup on the SOURCE index (disk image / pack entry), not the resulting path: a pack
                // image decrypts to a fresh temp path each call, so path-level dedup would neither
                // catch pack dupes nor stop us re-decrypting the same file. Distinct sources also mean
                // each chosen pack image decrypts exactly once. Cap at the pool size.
                int poolSize = _imageList.Count + _packImageList.Count;
                int want = Math.Min(count, poolSize);
                var chosenDisk = new HashSet<int>();
                var chosenPack = new HashSet<int>();
                var result = new List<string>(want);
                int guard = 0, maxGuard = poolSize * 8 + 16;   // backstop vs. random-collision / decrypt-fail retries

                while (result.Count < want && guard++ < maxGuard)
                {
                    bool usePackImage = false;
                    if (_imageList.Count > 0 && _packImageList.Count > 0)
                        usePackImage = _random.Next(poolSize) >= _imageList.Count;   // weighted by count
                    else if (_packImageList.Count > 0)
                        usePackImage = true;

                    if (usePackImage && _packImageList.Count > 0)
                    {
                        var index = _random.Next(_packImageList.Count);
                        if (!chosenPack.Add(index)) continue;   // already drew this pack entry
                        var packImage = _packImageList[index];
                        var tempPath = App.ContentPacks?.GetPackFileTempPath(packImage.PackId, packImage.File);
                        if (!string.IsNullOrEmpty(tempPath))
                        {
                            _tempPackFiles.Add(tempPath);   // track for cleanup
                            result.Add(tempPath);
                        }
                        // decrypt failed → index stays marked chosen so we don't retry a broken entry
                    }
                    else if (_imageList.Count > 0)
                    {
                        var index = _random.Next(_imageList.Count);
                        if (!chosenDisk.Add(index)) continue;   // already drew this disk image
                        result.Add(_imageList[index]);
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// Refreshes both image lists (regular and pack images) from disk cache.
        /// Called when lists are empty or cache has expired.
        /// </summary>
        private void RefreshImageLists()
        {
            // Clean up old temp pack files
            CleanupTempPackFiles();

            // Load regular images (include common extensions and variants)
            // GetMediaFiles has its own 60-second cache, so this is efficient
            _imageList = GetMediaFiles(_imagesPath, new[] { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif", ".ico" });

            // Load pack images from active packs
            _packImageList = App.ContentPacks?.GetAllActivePackImages() ?? new List<(string, PackFileEntry)>();

            // Both sources filtered against the live disabled set, so the pools are in sync with it.
            _poolDisabledStamp = App.Settings?.Current?.DisabledAssetPaths.Count ?? 0;

            App.Logger?.Information("Image lists refreshed: {RegularCount} regular images, {PackCount} pack images from {Path}",
                _imageList.Count, _packImageList.Count, _imagesPath);
        }

        /// <summary>
        /// Drops anything the user has deselected in the Assets tab out of the LIVE pools.
        ///
        /// The pools are filtered when they are built (GetMediaFiles / GetAllActivePackImages) and
        /// the asset manager empties them on every toggle
        /// (MainWindow.InvalidateAssetPoolsAfterSelectionChange -> ClearFileCache). This is the
        /// belt to those braces: a pool built before a toggle — or a toggle that reached settings
        /// through a path that forgot to invalidate — otherwise keeps flashing content the user
        /// turned off for the rest of the process, which reads as "unchecking does nothing until I
        /// restart". Gated on the size of the disabled set so the normal draw costs one int
        /// compare; the walk only runs on the first draw after a selection actually changed.
        /// Caller must hold <see cref="_lockObj"/>.
        /// </summary>
        private void PruneDeselectedFromPools()
        {
            var disabled = App.Settings?.Current?.DisabledAssetPaths;
            int count = disabled?.Count ?? 0;
            if (count == _poolDisabledStamp) return;
            _poolDisabledStamp = count;
            if (disabled == null || count == 0) return;

            // DisabledAssetPaths is OrdinalIgnoreCase and stored forward-slashed (AppSettings), so
            // only the separator of the runtime relative path needs normalizing here.
            var basePath = App.EffectiveAssetsPath;
            int removed = _imageList.RemoveAll(f =>
                disabled.Contains(Path.GetRelativePath(basePath, f).Replace('\\', '/')));
            removed += _packImageList.RemoveAll(p =>
                disabled.Contains($"pack:{p.PackId}/{p.File.OriginalName}"));

            if (removed > 0)
                App.Logger?.Information("FlashService: dropped {Count} deselected image(s) from the live pool", removed);
        }

        /// <summary>
        /// Cleans up temporary pack image files.
        /// </summary>
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

        private string? GetNextSound()
        {
            lock (_lockObj)
            {
                if (_soundQueue.Count == 0)
                {
                    var files = GetMediaFiles(SoundsPath, new[] { ".mp3", ".wav", ".ogg" });
                    if (files.Count == 0) return null;

                    // Performance: Shuffle and enqueue all at once
                    _soundQueue = new Queue<string>(files.OrderBy(_ => _random.Next()));
                }

                return _soundQueue.Count > 0 ? _soundQueue.Dequeue() : null; // Performance: O(1) instead of O(n)
            }
        }

        /// <summary>
        /// Plays a random sound from the flashes audio folder.
        /// Used for quest completion and other celebratory events.
        /// </summary>
        public void PlayRandomSound()
        {
            try
            {
                var soundPath = GetNextSound();
                if (!string.IsNullOrEmpty(soundPath) && File.Exists(soundPath))
                {
                    var volume = App.Settings?.Current?.MasterVolume ?? 50;
                    PlaySound(soundPath, volume);
                    App.Logger?.Debug("Playing random flash sound for event: {Path}", soundPath);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play random sound: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Plays a random chime sound for lucky flash (10x XP)
        /// </summary>
        private void PlayLuckyFlashSound()
        {
            try
            {
                var soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds");
                var chimeFiles = new[] { "chime1.mp3", "chime2.mp3", "chime3.mp3" };
                var chimePath = Path.Combine(soundsPath, chimeFiles[_random.Next(chimeFiles.Length)]);

                if (File.Exists(chimePath))
                {
                    var masterVolume = App.Settings.Current.MasterVolume / 100f;
                    var volume = (float)Math.Pow(masterVolume, 1.5) * 0.35f;
                    App.Audio?.PlayOneShot(chimePath, volume, "lucky-flash");
                    App.Logger?.Information("🎉 Lucky Flash! 10x XP!");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play lucky flash sound: {Error}", ex.Message);
            }
        }

        private List<string> GetMediaFiles(string folder, string[] extensions)
        {
            if (!Directory.Exists(folder)) return new List<string>();

            // Performance: Create cache key from folder + extensions
            var cacheKey = $"{folder}|{string.Join(",", extensions)}";

            lock (_cacheLock)
            {
                // Check if we have a valid cached result
                if (_fileListCache.TryGetValue(cacheKey, out var cached))
                {
                    var age = (DateTime.UtcNow - cached.lastScan).TotalSeconds;
                    if (age < CACHE_EXPIRY_SECONDS)
                    {
                        return new List<string>(cached.files);  // Return copy to prevent modification
                    }
                }
            }

            // Scan directory (cache miss or expired)
            var files = new List<string>();
            var blockedCount = 0;
            var sanitizeFailedCount = 0;

            foreach (var ext in extensions)
            {
                // Scan subfolders to support user-organized categories
                // Note: Directory.GetFiles is case-insensitive on Windows NTFS
                foreach (var file in Directory.GetFiles(folder, $"*{ext}", SearchOption.AllDirectories))
                {
                    // Security: Validate path is within allowed directories (app dir, user assets, or custom path)
                    var isInAppDir = SecurityHelper.IsPathSafe(file, AppDomain.CurrentDomain.BaseDirectory);
                    var isInUserAssets = SecurityHelper.IsPathSafe(file, App.UserDataPath);
                    var isInCustomPath = SecurityHelper.IsPathSafe(file, App.EffectiveAssetsPath);

                    if (isInAppDir || isInUserAssets || isInCustomPath)
                    {
                        // Security: Sanitize filename to prevent path traversal
                        var fileName = SecurityHelper.SanitizeFilename(Path.GetFileName(file));
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            files.Add(file);
                        }
                        else
                        {
                            sanitizeFailedCount++;
                            App.Logger?.Debug("File sanitization failed for: {Path}", file);
                        }
                    }
                    else
                    {
                        blockedCount++;
                        App.Logger?.Warning("Blocked file outside allowed directory: {Path}", file);
                    }
                }
            }

            if (blockedCount > 0 || sanitizeFailedCount > 0)
            {
                App.Logger?.Information("GetMediaFiles: Found {FileCount} files, blocked {BlockedCount}, sanitize failed {SanitizeCount} in {Folder}",
                    files.Count, blockedCount, sanitizeFailedCount, folder);
            }

            // Filter out disabled assets (blacklist approach).
            // Normalize both sides for the lookup: case-insensitive and separator-agnostic.
            // Paths get saved when the user unchecks an item in the asset tree, but the
            // saved string can differ from the runtime relative path by separator or case
            // (Windows is case-insensitive at the filesystem level), causing the unchecked
            // image to slip through the filter.
            if (App.Settings?.Current?.DisabledAssetPaths.Count > 0)
            {
                var basePath = App.EffectiveAssetsPath;
                static string Norm(string p) => p.Replace('\\', '/');
                var disabled = new HashSet<string>(
                    App.Settings.Current.DisabledAssetPaths.Select(Norm),
                    StringComparer.OrdinalIgnoreCase);
                files = files.Where(f =>
                {
                    var relativePath = Norm(Path.GetRelativePath(basePath, f));
                    return !disabled.Contains(relativePath);
                }).ToList();
            }

            // Update cache
            lock (_cacheLock)
            {
                _fileListCache[cacheKey] = (new List<string>(files), DateTime.UtcNow);
            }

            return files;
        }

        /// <summary>
        /// Clear the file list cache (called when assets are reloaded or selection changes).
        /// Also empties the live selection pools: they are drawn from by random index and never
        /// drained, so clearing only the 60s listing cache left every flash still picking from
        /// the pre-toggle paths until the next LoadAssets().
        /// </summary>
        public void ClearFileCache()
        {
            // Lock order: _lockObj BEFORE _cacheLock, matching
            // GetNextImages -> RefreshImageLists -> GetMediaFiles. The reverse order deadlocks.
            lock (_lockObj)
            {
                _imageList.Clear();  // forces RefreshImageLists() on the next draw
                _packImageList.Clear();
                _soundQueue = new Queue<string>();

                lock (_cacheLock)
                {
                    _fileListCache.Clear();
                }
            }
        }

        /// <summary>
        /// Clear the decoded image cache to free memory (e.g. between sessions).
        /// Cached BitmapSources on the LOH are released so GC can reclaim them.
        /// </summary>
        public void ClearImageCache()
        {
            lock (_imageDecodeCache)
            {
                _imageDecodeCache.Clear();
                _imageCacheBytes = 0;
            }
            App.Logger?.Debug("FlashService: Image decode cache cleared");
        }

        #endregion

        #region Audio

        private double PlaySound(string path, int volumePercent)
        {
            StopCurrentSound();

            AudioFileReader? audioFile = null;
            WaveOutEvent? sound = null;
            try
            {
                audioFile = new AudioFileReader(path);
                sound = new WaveOutEvent();
                App.Audio?.ApplyPreferredDevice(sound);

                // Apply volume curve (gentler, minimum 5%)
                var volume = volumePercent / 100.0f;
                var curvedVolume = Math.Max(0.05f, (float)Math.Pow(volume, 1.5));
                audioFile.Volume = curvedVolume;

                sound.Init(audioFile);
                sound.Play();
                App.Audio?.NoteOutputSuccess();

                // Only assign to fields after everything succeeded
                _currentAudioFile = audioFile;
                _currentSound = sound;

                return audioFile.TotalTime.TotalSeconds;
            }
            catch (Exception ex)
            {
                // Dispose locally — these never made it to the fields
                sound?.Dispose();
                audioFile?.Dispose();
                App.Logger.Warning("Could not play sound {Path}: {Error}", path, ex.Message);
                App.Audio?.NoteOutputFailure("flash-sound", ex.Message);
                return 5.0;
            }
        }

        private void StopCurrentSound()
        {
            try
            {
                _currentSound?.Stop();
                _currentSound?.Dispose();
                _currentAudioFile?.Dispose();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Error stopping flash sound: {Error}", ex.Message);
            }

            _currentSound = null;
            _currentAudioFile = null;
        }

        #endregion

        #region Window Management

        /// <summary>
        /// Pop a recycled flash window or create a fresh one. The window's chrome and
        /// one-time hooks (click handler, CTS safety net, Alt+Tab hiding) are wired here
        /// exactly once; everything per-spawn is assigned by SpawnFlashWindow.
        /// </summary>
        private FlashWindow AcquireFlashWindow(int width, int height)
        {
            // Reuse ONLY a pooled window whose size already matches the request. Resizing a
            // realized layered window is the render-thread-deadlock trigger (dump-confirmed
            // 2026-06-13: SetValue(Width) -> OnResize -> MediaContext.CompleteRender wedges the
            // UI thread on a backed-up compositor), so a size mismatch gets a fresh window
            // sized BEFORE its first Show() instead — never a live resize.
            if (_windowPool.Count > 0)
            {
                FlashWindow? match = null;
                var keep = new List<FlashWindow>(_windowPool.Count);
                while (_windowPool.Count > 0)
                {
                    var pooled = _windowPool.Pop();
                    if (!pooled.IsLoaded)
                    {
                        // Unloaded shell (display-topology change, external teardown). Dropping the
                        // reference is NOT enough: a constructed Window stays registered in
                        // Application.Windows until Close(), so its hwnd would survive for the whole
                        // process lifetime. Close it explicitly - a leaked USER object per pool
                        // eviction is exactly the kind of drip that ends a 4h session with
                        // CreateWindowEx failing (#627).
                        try { pooled.Close(); } catch { }
                        continue;
                    }
                    if (match == null && (int)pooled.Width == width && (int)pooled.Height == height)
                        match = pooled;
                    else
                        keep.Add(pooled);
                }
                foreach (var w2 in keep) _windowPool.Push(w2);   // restore the non-matching windows
                if (match != null) return match;
            }

            var w = new FlashWindow
            {
                AllowsTransparency = true,
                WindowStyle = WindowStyle.None,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Background = System.Windows.Media.Brushes.Black,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Opacity = 0,
                // Size the shell before it is ever shown (no HWND yet => no live resize).
                Width = width,
                Height = height,
            };

            // One-time click handler — gated on the per-spawn IsClickable flag so a
            // recycled window behaves per its current spawn, with no handler stacking.
            w.MouseLeftButtonDown += (s, e) =>
            {
                if (s is FlashWindow fw && fw.IsClickable && !fw.IsFadingOut)
                    OnFlashClicked(fw, App.Settings.Current);
            };

            // Safety net: if the window is closed externally (e.g., OS shutdown, Alt+F4)
            // without going through SafeCloseFlashWindow, dispose the CTS to prevent leaks~ 🧹
            w.Closed += (s, e) =>
            {
                if (s is FlashWindow fw)
                {
                    try { fw.LifetimeRegistration?.Dispose(); } catch { }
                    fw.LifetimeRegistration = null;
                    try { fw.LifetimeCts?.Cancel(); } catch { }
                    try { fw.LifetimeCts?.Dispose(); } catch { }
                    fw.LifetimeCts = null;
                }
            };

            // Hide from Alt+Tab for ALL flash windows (SourceInitialized fires once, at first Show)
            HideFromAltTab(w);
            return w;
        }

        /// <summary>
        /// Toggle mouse click-through on a (shown) flash window. Recycled windows can flip
        /// between clickable and click-through across spawns, so the style is re-applied
        /// directly on the live hwnd each time rather than via SourceInitialized.
        /// </summary>
        private static void ApplyClickability(FlashWindow window, bool clickable)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE)
                            | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_NOACTIVATE;
                if (clickable) style &= ~NativeMethods.WS_EX_TRANSPARENT;
                else style |= NativeMethods.WS_EX_TRANSPARENT;
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, style);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ApplyClickability failed: {Error}", ex.Message);
            }
        }

        private void SafeCloseFlashWindow(FlashWindow window)
        {
            try
            {
                // Dispose CTS registration first to release the closure capturing this window
                try { window.LifetimeRegistration?.Dispose(); } catch { }
                window.LifetimeRegistration = null;

                // Cancel and dispose per-window lifetime token~ 🧹
                try { window.LifetimeCts?.Cancel(); } catch { }
                try { window.LifetimeCts?.Dispose(); } catch { }
                window.LifetimeCts = null;

                // Release bitmap references before retiring to prevent memory accumulation
                // Without this, retired windows hold BitmapSource frames until GC collects them,
                // causing multi-GB memory growth over long sessions
                if (window.ImageControl != null)
                {
                    window.ImageControl.Source = null;
                    window.ImageControl = null;
                }
                window.Frames.Clear();

                // Stop the glow's animations BEFORE dropping the content. A lucky proc starts
                // RepeatBehavior.Forever blur+opacity animations on this DropShadowEffect; a Forever
                // animation keeps its target pinned by the app-global timing manager (it survives
                // run teardown) until cleared with BeginAnimation(prop, null). The effect pins a
                // native GPU blur render-target, so leaving it animated leaked native memory every
                // glowed flash — the chaos-mode OOM climb (managed heap stayed flat the whole time).
                if (window.GlowEffect is { } glow)
                {
                    try
                    {
                        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, null);
                        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
                    }
                    catch { }
                    window.GlowEffect = null;
                }

                // Compositor: detach the layer item — this disposes its SKImage frames
                // deterministically. No hwnd, nothing to pool.
                if (window.UsesLayer)
                {
                    if (window.LayerItem != null)
                    {
                        var item = window.LayerItem;
                        window.LayerItem = null;
                        _flashLayer?.Remove(item);
                    }
                    window.IsFadingOut = false;
                    // The state bag is still a real Window: constructing it registered it in
                    // Application.Windows, and only Close() removes it — returning without a
                    // Close leaked one Window per layer flash for the app lifetime. Close on a
                    // never-shown Window is legal (no hwnd, so no layered-window deadlock),
                    // and returning here keeps it out of the recycle pool below.
                    CloseStateBagWindow(window);
                    return;
                }

                // Solid mode: just detach the visual from the shared canvas — there is no hwnd to
                // hide/close/pool, and the host itself stays alive (see EnsureHostRef/ReleaseHostRef).
                if (window.UsesHost)
                {
                    if (window.HostedRoot != null)
                    {
                        ChaosBubbleHostOverlay.Remove(window.HostedRoot);
                        window.HostedRoot = null;
                    }
                    window.IsFadingOut = false;
                    // Same state-bag leak as the layer branch above: the never-shown Window
                    // stays registered in Application.Windows until Close().
                    CloseStateBagWindow(window);
                    return;
                }

                window.Content = null;
                window.Effect = null;   // belt-and-suspenders: ensure no effect render-target lingers on the pooled shell
                window.IsFadingOut = false;
                window.Opacity = 0;

                // Recycle instead of Close: hide the window and return it to the pool.
                // Closing a layered window mid-run is the render-thread-deadlock trigger.
                if (window.IsLoaded && _windowPool.Count < WINDOW_POOL_MAX)
                {
                    using var _uiMark = VideoDiag.UiScope("FlashService.HideFlashWindow(layered)");
                    window.Hide();
                    _windowPool.Push(window);
                }
                else
                {
                    // The pool-overflow branch: the comment above says it, so breadcrumb it — if a
                    // hang report's "last UI mark" is this, the deadlock trigger fired for real.
                    using var _uiMark = VideoDiag.UiScope("FlashService.CloseFlashWindow(layered, pool full)");
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to close flash window: {Error}", ex.Message);
                try { window.Close(); } catch { }
            }
        }

        /// <summary>
        /// Close a never-shown state-bag Window (layer mode) on its dispatcher so it leaves
        /// Application.Windows. Safe to call from any thread; Close is idempotent-ish and any
        /// failure is swallowed (the window has no hwnd, handlers or content by this point).
        /// </summary>
        private static void CloseStateBagWindow(FlashWindow window)
        {
            try
            {
                if (window.Dispatcher.CheckAccess())
                {
                    window.Close();
                }
                else
                {
                    window.Dispatcher.BeginInvoke(() =>
                    {
                        try { window.Close(); } catch { }
                    });
                }
            }
            catch { }
        }

        // WndProc hook for flash windows: drop WM_DPICHANGED so WPF never runs its auto DPI-rescale
        // (OnDpiChanged -> OnResize -> CompleteRender), which deadlocks the UI thread. See the hook
        // registration in HideFromAltTab.
        private static IntPtr SwallowDpiChanged(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_DPICHANGED = 0x02E0;
            if (msg == WM_DPICHANGED)
                handled = true;   // consume it; WPF's HwndTarget never sees the resize
            return IntPtr.Zero;
        }

        private void HideFromAltTab(Window window)
        {
            try
            {
                window.SourceInitialized += (s, e) =>
                {
                    if (s is not Window w) return;
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                    var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                        extendedStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);

                    // Swallow WM_DPICHANGED on flash windows. These are transient overlays whose
                    // per-monitor geometry is already computed manually (CalculateGeometry uses the
                    // target monitor's DpiScale), so WPF's automatic DPI rescale is unwanted — and its
                    // OnDpiChanged -> OnResize -> synchronous MediaContext.CompleteRender deadlocks the
                    // UI thread on a backed-up render thread, especially re-entrantly while a flash window
                    // is closing (dump-confirmed 2026-07-05). Fired only on cross-DPI-monitor moves, so
                    // dropping it never affects the initial render.
                    System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook(SwallowDpiChanged);
                };
            }
            catch (Exception ex)
            {
                App.Logger.Debug("Could not hide window from Alt+Tab: {Error}", ex.Message);
            }
        }

        /// <summary>Take the flash session's single reference on the shared host (UI thread; the
        /// create is synchronous there, so the host is placeable in the same spawn tick).</summary>
        private void EnsureHostRef()
        {
            if (_hostRefHeld) return;
            _hostRefHeld = true;
            ChaosBubbleHostOverlay.EnsureCreated();
        }

        /// <summary>Release the session's host reference (Stop, or one-shot fully faded). The host
        /// only actually closes when no other owner (chaos run, ambient bubbles) still holds it.</summary>
        private void ReleaseHostRef()
        {
            if (!_hostRefHeld) return;
            _hostRefHeld = false;
            ChaosBubbleHostOverlay.CloseActive();
        }

        private void ForceTopmost(Window window)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to force window topmost: {Error}", ex.Message);
            }
        }

        private void CloseAllWindows()
        {
            List<FlashWindow> windowsCopy;
            lock (_lockObj)
            {
                windowsCopy = _activeWindows.ToList();
                _activeWindows.Clear();
            }

            foreach (var window in windowsCopy)
            {
                SafeCloseFlashWindow(window);
            }

            _soundPlayingForCurrentFlash = false;

            if (windowsCopy.Count > 0)
                App.Overlay?.NotifyTopWindowClosed();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Stop();
            try { _flashLayer?.Clear(); } catch { }
            // Drain the recycled-window pool — the only place pooled hwnds actually close
            // (app shutdown; nothing else is animating, so the close is safe here).
            while (_windowPool.Count > 0)
            {
                try { _windowPool.Pop().Close(); } catch { }
            }
            _cancellationSource?.Dispose();
            StopCurrentSound();
            CleanupTempPackFiles();
            lock (_imageDecodeCache) { _imageDecodeCache.Clear(); _imageCacheBytes = 0; }
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// A flash image window with its own independent lifetime managed by a CancellationToken~ ✨
    /// CopilotNotes: Each window owns its CTS so it can fade out independently without nuking siblings.
    /// </summary>
    internal class FlashWindow : Window
    {
        public List<BitmapSource> Frames { get; set; } = new();
        public TimeSpan FrameDelay { get; set; }
        public DateTime StartTime { get; set; }
        public int CurrentFrameIndex { get; set; }
        public Image? ImageControl { get; set; }
        public bool IsClickable { get; set; }

        /// <summary>
        /// Solid mode: this instance is never Show()n — it stays a pure state bag (lifetime CTS,
        /// gaze bookkeeping, hydra data; Left/Top/Width/Height hold the DIP rect as plain values)
        /// while the visual lives as <see cref="HostedRoot"/> on the shared ChaosBubbleHostOverlay
        /// canvas. Keeping the FlashWindow shape means GazeFocusService, hydra and the heartbeat
        /// all work unchanged in both modes.
        /// </summary>
        public bool UsesHost { get; set; }

        /// <summary>Solid mode only: the visual on the shared host canvas (null once torn down).</summary>
        public FrameworkElement? HostedRoot { get; set; }

        /// <summary>
        /// Compositor mode: like solid mode, this instance is a pure state bag — the visual is a
        /// FlashLayer item on the shared compositor host. Same DIP bookkeeping in Left/Top/
        /// Width/Height, so gaze, hydra and the heartbeat work unchanged.
        /// </summary>
        public bool UsesLayer { get; set; }

        /// <summary>Compositor mode only: this flash's layer item (null once torn down).</summary>
        public Services.Compositor.FlashLayer.FlashItem? LayerItem { get; set; }

        /// <summary>
        /// Compositor mode only: true while the off-thread frame conversion for this spawn is
        /// still running — LayerItem is null but the flash is NOT dead, so the heartbeat must
        /// not sweep it. Cleared by the spawn continuation whether it spawns or bails.
        /// </summary>
        public bool LayerSpawnPending { get; set; }

        /// <summary>
        /// The fade alpha the heartbeat animates: window Opacity in per-window mode, the hosted
        /// root's Opacity in solid mode, the layer item's in compositor mode (an unshown
        /// Window's Opacity renders nothing).
        /// </summary>
        public double VisualOpacity
        {
            get => UsesLayer ? (LayerItem?.Opacity ?? 0.0)
                 : UsesHost ? (HostedRoot?.Opacity ?? 0.0)
                 : Opacity;
            set
            {
                if (UsesLayer) { if (LayerItem != null) LayerItem.Opacity = value; }
                else if (UsesHost) { if (HostedRoot != null) HostedRoot.Opacity = value; }
                else Opacity = value;
            }
        }

        /// <summary>
        /// The lucky/sparkle glow effect applied this spawn, if any. Held so the pool-return path
        /// can stop its animations: a lucky proc starts RepeatBehavior.Forever blur+opacity
        /// animations on this DropShadowEffect, and a Forever animation keeps its target (plus the
        /// effect's native GPU blur render-target) pinned by the global timing manager until it is
        /// explicitly cleared with BeginAnimation(prop, null). Without that, every glowed flash
        /// leaked a native render surface that survived run teardown — the chaos OOM climb.
        /// </summary>
        public System.Windows.Media.Effects.DropShadowEffect? GlowEffect { get; set; }

        /// <summary>
        /// Per-window cancellation source — cancel this to begin fade-out for THIS window only~ 🌙
        /// </summary>
        public CancellationTokenSource? LifetimeCts { get; set; }

        /// <summary>
        /// Registration handle for the CTS callback — must be disposed to release the closure
        /// that captures this window, preventing memory leaks per flash.
        /// </summary>
        public CancellationTokenRegistration? LifetimeRegistration { get; set; }

        /// <summary>
        /// When this window should start fading out. Set by the cancellation callback or on creation.
        /// </summary>
        public DateTime ExpiresAt { get; set; } = DateTime.MaxValue;

        /// <summary>
        /// Whether this window is actively fading out (set when token is cancelled)~ uwu
        /// </summary>
        public bool IsFadingOut { get; set; }

        /// <summary>
        /// The full original lifetime this window was spawned with (ms), for hydra spawn calculations~ 🐙
        /// CopilotNotes: Used when HydraLinkedTiming is false (independent mode) to give children a fresh full lifetime.
        /// </summary>
        public int OriginalLifetimeMs { get; set; }

        /// <summary>
        /// How many hydra hops deep this window is~ 🐙✨
        /// 0 = original flash, 1 = first hydra child, 2 = grandchild, etc.
        /// CopilotNotes: Used for XP diminishing returns in independent timing mode.
        /// Gen 0 = 100% XP, Gen 1 = 75%, Gen 2 = 50%, Gen 3 = 25%, Gen 4+ = 10% floor.
        /// </summary>
        public int HydraGeneration { get; set; }

        /// <summary>
        /// Monitor this window was spawned on. Set by SpawnFlashWindow so
        /// hydra children (TriggerMultiplication) can inherit the parent's
        /// screen via PickMonitor's preferred-monitor path.
        /// </summary>
        public MonitorInfo Monitor { get; set; } = new();

        /// <summary>
        /// Pushes the window's death deadline out by <paramref name="extraMs"/>
        /// from now. Called by GazeFocusService each dwell tick while gaze is
        /// on this window and stare-linger is enabled. CancelAfter is replaced
        /// per call (last call wins), so the deadline tracks "alive for
        /// extraMs more from the most recent boost." When gaze leaves, the
        /// deadline stops being pushed and elapses naturally. Updates
        /// ExpiresAt too so any code that reads it sees the new deadline.
        /// </summary>
        public void BoostLifetime(int extraMs)
        {
            if (extraMs <= 0) return;
            // If the lifetime token has already fired (timer elapsed, window is
            // fading out) CancelAfter is a silent no-op — but pushing ExpiresAt
            // into the future would make the heartbeat re-show a window whose
            // CTS can never re-fire, leaving it immortal on screen. Don't revive
            // a window that is already on its way out. (#384)
            if (IsFadingOut || LifetimeCts == null || LifetimeCts.IsCancellationRequested) return;
            try
            {
                LifetimeCts.CancelAfter(extraMs);
                ExpiresAt = DateTime.Now.AddMilliseconds(extraMs);
            }
            catch
            {
                // CTS may have been disposed (window fading out) — silent
                // is fine, the window is already on its way out.
            }
        }

        /// <summary>
        /// Whether this flash triggered a lucky proc (golden glow effect)
        /// </summary>
        public bool IsLucky { get; set; }

        /// <summary>
        /// Drives a subtle inflate effect on the flash content during Focus
        /// Gaze dwell. t01 is the dwell progress in [0, 1]; content is scaled
        /// 1.0 → 1.10 around its center. Independent of the existing fade
        /// animation so it composes cleanly.
        /// </summary>
        public void SetGazeDwellProgress(double t01)
        {
            if (IsFadingOut) return;
            if (UsesLayer)
            {
                // The layer draws the item scaled about its center — same 1.0 → 1.10 inflate.
                if (LayerItem != null)
                    LayerItem.DwellScale = 1.0 + Math.Max(0.0, Math.Min(1.0, t01)) * 0.10;
                return;
            }
            var fe = UsesHost ? HostedRoot : Content as FrameworkElement;
            if (fe == null) return;
            var clamped = Math.Max(0.0, Math.Min(1.0, t01));
            var scale = 1.0 + clamped * 0.10;
            // Reuse an existing ScaleTransform if SetGazeDwellProgress put one
            // there last frame; otherwise install a fresh one.
            if (fe.RenderTransform is ScaleTransform st)
            {
                st.ScaleX = scale;
                st.ScaleY = scale;
            }
            else
            {
                fe.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                fe.RenderTransform = new ScaleTransform(scale, scale);
            }
        }
    }

    internal class LoadedImageData
    {
        public string FilePath { get; set; } = "";
        public List<BitmapSource> Frames { get; } = new();
        public int Width { get; set; }
        public int Height { get; set; }
        public TimeSpan FrameDelay { get; set; }
        public ImageGeometry Geometry { get; set; } = new();
        public MonitorInfo Monitor { get; set; } = new();
    }

    internal class ImageGeometry
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    internal class MonitorInfo
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }

        /// <summary>
        /// DPI scale of the physical screen these DIP bounds were derived from. Solid mode needs it
        /// to convert flash DIPs back to physical px for ChaosBubbleHostOverlay.Place, and to build
        /// the per-child LayoutTransform when this screen's scale differs from the host's render scale.
        /// </summary>
        public double DpiScale { get; set; } = 1.0;
    }

    /// <summary>
    /// Event args for when flash audio starts playing, containing the audio filename text
    /// </summary>
    public class FlashAudioEventArgs : EventArgs
    {
        /// <summary>
        /// The text extracted from the audio filename (without extension)
        /// </summary>
        public string Text { get; }

        public FlashAudioEventArgs(string audioPath)
        {
            // Extract filename without extension and clean it up
            var fileName = Path.GetFileNameWithoutExtension(audioPath);
            Text = fileName ?? string.Empty;
        }
    }

    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOACTIVATE = 0x0010;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    }

    #endregion
}
