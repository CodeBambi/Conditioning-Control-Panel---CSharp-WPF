using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Services.Flash;

/// <summary>
/// Avalonia implementation of the flash-image effect engine.
/// Spawns images via the unified compositor layer at a configurable frequency,
/// loads images from the user's assets folder, supports click-to-close, and
/// implements the hydra multiplication mode.
///
/// WPF spawn contract (FlashService.cs): each flash event shows SimultaneousImages
/// images STAGGERED 300ms apart (hydra children 100ms, :1046), each at an independent
/// RANDOM position sized to a 40%-of-monitor box * ImageScale with a 50 DIP edge pad
/// (:1770-1801), fading in at the spawn position. Click OR gaze pops run the same
/// close + hydra pipeline (:196, :1415): CorruptionMode spawns HydraMultiplyCount (2-5)
/// children per pop on the parent's monitor, capped by HydraLimit and MAX_CONCURRENT_FLASH.
/// </summary>
public sealed class AvaloniaFlashService : IFlashService, IDisposable
{
    private const double FADE_PER_SEC = 2.4;
    private const int MAX_CONCURRENT_FLASH = 10;
    private const int CACHE_EXPIRY_SECONDS = 60;
    private static readonly string[] IMAGE_EXTENSIONS = { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif", ".ico" };

    private readonly ISettingsService _settings;
    private readonly IAppEnvironment _environment;
    private readonly IScreenProvider _screens;
    private readonly IAchievementService _achievements;
    private readonly IProgressionService _progression;
    private readonly ILogger<AvaloniaFlashService>? _logger;
    private readonly Random _random = new();
    private readonly object _sync = new();
    private readonly CompositorEngine? _compositor;
    private readonly FlashLayer? _flashLayer;
    private readonly IMouseHook? _mouseHook;
    private readonly Dictionary<string, (List<string> files, DateTime lastScan)> _fileCache = new();
    private readonly Dictionary<Guid, FlashClickData> _clickData = new();
    // LRU decode cache (WPF FlashService._imageDecodeCache parity): one decode per file,
    // shared across spawns via ref-counted SkiaFrameSets. Persists across Stop/Start.
    private readonly FlashImageCache _frameCache = new();

    private string _imagesPath = "";
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _scheduledTimer;
    private bool _isBusy;
    private bool _noImagesWarningShown;
    private readonly List<string> _lastDisplayedImagePaths = new();
    // Hook subscription state: strictly paired Install/Uninstall (the shared IMouseHook is
    // ref-counted; an unpaired Uninstall used to tear down the bubble service's hook too).
    private bool _hookSubscribed;
    private AppSettings? _observedSettings;

    public AvaloniaFlashService(
        ISettingsService settings,
        IAppEnvironment environment,
        IScreenProvider screens,
        IAchievementService achievements,
        IProgressionService progression,
        ILogger<AvaloniaFlashService>? logger = null,
        CompositorEngine? compositor = null,
        IMouseHook? mouseHook = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _achievements = achievements ?? throw new ArgumentNullException(nameof(achievements));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _logger = logger;
        _compositor = compositor;
        _mouseHook = mouseHook;
        // Avalonia always renders flashes on the single shared compositor canvas (FlashLayer) — there
        // is no per-window flash path. This is exactly what WPF's opt-in FlashSolidMode achieves (one
        // shared overlay host instead of many layered windows), so the feature is inherently always-on
        // here; AppSettings.FlashSolidMode has no per-window fallback to gate and is intentionally ignored.
        _flashLayer = compositor != null ? new FlashLayer() : null;
        if (_flashLayer != null)
            _compositor?.RegisterLayer(_flashLayer);

        RefreshImagesPath();
    }

    public bool IsRunning { get; private set; }

    public IReadOnlyList<string> LastDisplayedImagePaths
    {
        get
        {
            lock (_lastDisplayedImagePaths) { return _lastDisplayedImagePaths.ToList(); }
        }
    }

    public event EventHandler? FlashAboutToDisplay;
    public event EventHandler? FlashDisplayed;
    public event EventHandler? FlashClicked;

    public void Start()
    {
        if (IsRunning) return;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            _logger?.LogDebug("AvaloniaFlashService: overlays are not supported on mobile; Start is a no-op");
            return;
        }

        var settings = _settings.Current;
        if (settings == null) return;

        IsRunning = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _noImagesWarningShown = false;

        // Install mouse hook for flash click detection. Applied live: WPF re-reads
        // FlashClickable per spawn (FlashService.ApplyClickability), so the Avalonia head
        // watches the setting and installs/uninstalls the hook subscription mid-session.
        _observedSettings = settings;
        settings.PropertyChanged += OnSettingsPropertyChanged;
        UpdateHookSubscription(settings.FlashClickable);

        ScheduleNext();
        _logger?.LogInformation("AvaloniaFlashService started, images path: {Path}", _imagesPath);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        _scheduledTimer?.Stop();
        _scheduledTimer = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_observedSettings != null)
        {
            _observedSettings.PropertyChanged -= OnSettingsPropertyChanged;
            _observedSettings = null;
        }
        // Paired uninstall: only releases the shared hook if this service installed it.
        UpdateHookSubscription(false);

        // Clear all flash items from the compositor layer
        _flashLayer?.Clear();
        lock (_sync) { _clickData.Clear(); }

        _logger?.LogInformation("AvaloniaFlashService stopped");
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppSettings.FlashClickable)) return;
        var wanted = IsRunning && _settings.Current?.FlashClickable == true;
        if (Dispatcher.UIThread.CheckAccess())
            UpdateHookSubscription(wanted);
        else
            Dispatcher.UIThread.Post(() => UpdateHookSubscription(wanted && IsRunning));
    }

    /// <summary>
    /// Installs or releases this service's share of the global WH_MOUSE_LL hook. Strictly
    /// paired: the hook itself is ref-counted across consumers (flash, bubbles, chaos ripple),
    /// so an Uninstall without a prior Install would otherwise steal the hook from another
    /// consumer. Runs on the UI thread (SetWindowsHookEx needs a pumping thread).
    /// </summary>
    private void UpdateHookSubscription(bool wanted)
    {
        if (_mouseHook == null || wanted == _hookSubscribed) return;
        if (wanted)
        {
            _mouseHook.LeftButtonDown += OnMouseLeftDown;
            try { _mouseHook.Install(); } catch { }
            _hookSubscribed = true;
        }
        else
        {
            _mouseHook.LeftButtonDown -= OnMouseLeftDown;
            try { _mouseHook.Uninstall(); } catch { }
            _hookSubscribed = false;
        }
    }

    public void TriggerFlash()
    {
        if (!IsRunning) return;

        // Don't spawn a fresh overlay surface while the display topology / DPI is mid-change (the
        // compositor is rebuilding surfaces; adding one now risks a churn/tear burst). Mirrors WPF.
        if (ConditioningControlPanel.Core.Services.DisplayChangeCoordinator.SpawnsSuppressed) return;

        var settings = _settings.Current;
        if (settings == null) return;

        if (_isBusy) return;
        _isBusy = true;

        try
        {
            FlashAboutToDisplay?.Invoke(this, EventArgs.Empty);

            var data = PickImageData();
            if (data == null)
            {
                if (!_noImagesWarningShown)
                {
                    _logger?.LogWarning("AvaloniaFlashService: no images found at {Path}", _imagesPath);
                    _noImagesWarningShown = true;
                }
                return;
            }

            _noImagesWarningShown = false;

            // Bounded bookkeeping: expired flashes are removed layer-side, so their click
            // entries would otherwise linger for the whole run.
            PruneExpiredClickData();

            // WPF LoadAndShowImages (FlashService.cs:476): the initial spawn count is
            // SimultaneousImages. HydraLimit caps hydra GROWTH only (click/gaze pops);
            // the hard concurrency cap (MAX_CONCURRENT_FLASH) is enforced at spawn time.
            var count = Math.Max(1, settings.SimultaneousImages);

            // WPF ShowImages (FlashService.cs:1046): images of one flash event are
            // STAGGERED 300ms apart (hydra children 100ms) — never all in one tick — and
            // every image is generation 0 (the old code passed the loop index as the
            // hydra generation).
            var eventPaths = new List<string>(count) { data.FilePath };
            SpawnFlash(data, settings, hydraGeneration: 0, overrideLifetimeMs: -1, spawnDelayMs: 0);
            for (int i = 1; i < count; i++)
            {
                var copy = PickImageData();
                if (copy == null) continue;
                eventPaths.Add(copy.FilePath);
                SpawnFlash(copy, settings, hydraGeneration: 0, overrideLifetimeMs: -1, spawnDelayMs: i * 300);
            }

            // WPF snapshot semantics (FlashService.cs:1077-1085): the property is REPLACED
            // with this event's paths so SessionLog attributes the event correctly.
            lock (_lastDisplayedImagePaths)
            {
                _lastDisplayedImagePaths.Clear();
                _lastDisplayedImagePaths.AddRange(eventPaths);
            }

            FlashDisplayed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _isBusy = false;
        }
    }

    public void RefreshImagesPath()
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var assets = _environment.EffectiveAssetsPath ?? "";
        var newPath = Path.Combine(assets, "images");
        if (newPath != _imagesPath)
        {
            _imagesPath = newPath;
            lock (_fileCache) { _fileCache.Clear(); }
            _logger?.LogInformation("AvaloniaFlashService: images path refreshed to {Path}", _imagesPath);
        }
    }

    private void SpawnFlash(ImageData data, AppSettings settings, int hydraGeneration, int overrideLifetimeMs = -1, int spawnDelayMs = 0)
    {
        if (!IsRunning && !data.OneShot) return;
        if (_flashLayer == null) return;

        var maxOpacity = settings.FlashOpacity / 100.0;
        // WPF ShowImages (FlashService.cs:1006): lifetime = duration + 1s grace so the
        // fade-out never truncates the configured display time. Hydra children override
        // this with linked/independent timing (FlashService.cs:1459).
        var lifetimeMs = overrideLifetimeMs > 0 ? overrideLifetimeMs : (int)(settings.FlashDuration * 1000) + 1000;
        SpawnDecoded(data.FilePath, data.Geometry, data.Monitor, maxOpacity, lifetimeMs, settings.FlashClickable,
            hydraGeneration, data.OneShot, spawnDelayMs);
    }

    /// <summary>
    /// Decode <paramref name="filePath"/> off the UI thread (single decode per file via the
    /// LRU cache — WPF LoadImageAsync parity) and post the flash into the compositor layer
    /// once frames are ready. Replaces the old UI-thread Bitmap -> PNG -> SKImage triple
    /// decode that hitched every overlay on each spawn.
    /// </summary>
    private void SpawnDecoded(string filePath, ImageGeometry geom, ImageGeometry monitor,
        double maxOpacity, int lifetimeMs, bool clickable, int hydraGeneration, bool oneShot,
        int spawnDelayMs = 0)
    {
        var decodeMax = ComputeDecodeMaxDim();
        var token = _cts?.Token ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            SkiaFrameSet? frames = null;
            try
            {
                // Per-image stagger (WPF ShowImages, FlashService.cs:1046: i*300ms normal /
                // i*100ms hydra). Delayed BEFORE decode so the LRU cache isn't hit for a
                // spawn that Stop() already cancelled.
                if (spawnDelayMs > 0)
                {
                    try { await Task.Delay(spawnDelayMs, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }

                frames = _frameCache.GetOrDecode(filePath, decodeMax);
                if (frames == null)
                {
                    _logger?.LogDebug("AvaloniaFlashService: failed to decode {Path}", filePath);
                    return;
                }

                var set = frames;
                frames = null; // ownership moves to the UI-thread continuation
                Dispatcher.UIThread.Post(() =>
                {
                    if ((!IsRunning && !oneShot) || _flashLayer == null)
                    {
                        set.Release(); // never entered the layer — no renderer can reference it
                        return;
                    }

                    // Hard concurrency cap (WPF SpawnFlashWindow, FlashService.cs:1104).
                    if (_flashLayer.ActiveCount >= MAX_CONCURRENT_FLASH)
                    {
                        set.Release();
                        return;
                    }

                    // Overlap avoidance at SPAWN time (WPF SpawnFlashWindow,
                    // FlashService.cs:1123-1130): staggered siblings re-roll against the
                    // flashes actually on screen when they appear, not against the
                    // pre-stagger snapshot. Re-rolls span the full monitor (no edge pad),
                    // matching WPF's re-roll range.
                    var g = geom;
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        if (!IsOverlapping(g.X, g.Y, g.Width, g.Height)) break;
                        g = g with
                        {
                            X = (int)(monitor.X + _random.Next(0, Math.Max(1, (int)(monitor.Width - g.Width)))),
                            Y = (int)(monitor.Y + _random.Next(0, Math.Max(1, (int)(monitor.Height - g.Height))))
                        };
                    }

                    // Guarantee the engine is awake for this item: Start() is idempotent and
                    // cheap when running; if the engine was never started (e.g. remote
                    // 'start_flash' with no session) it creates the compositor windows now.
                    _compositor?.Start();

                    var id = _flashLayer.Spawn(set, g.X, g.Y, g.Width, g.Height,
                        maxOpacity, lifetimeMs, clickable);
                    lock (_sync)
                    {
                        _clickData[id] = new FlashClickData(filePath, lifetimeMs, hydraGeneration, monitor, clickable);
                    }
                });
            }
            catch (Exception ex)
            {
                frames?.Release();
                _logger?.LogDebug("AvaloniaFlashService: spawn decode failed for {Path}: {Error}", filePath, ex.Message);
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Largest pixel dimension to decode a flash image/GIF frame at. WPF parity:
    /// FlashService.ComputeDecodeMaxDim uses the performance tier's cap (1024 at the
    /// default Quality tier) scaled by ImageScale, clamped 256..2048. The port has no
    /// performance-tier system yet, so the Quality cap applies unconditionally.
    /// </summary>
    private int ComputeDecodeMaxDim()
    {
        var scale = _settings.Current?.ImageScale ?? 100;
        var dim = (int)(1024 * (scale / 100.0));
        return Math.Clamp(dim, 256, 2048);
    }

    /// <summary>
    /// Header-only pixel size probe (SKCodec parses the header without decoding pixels) —
    /// cheap enough for the UI thread, replacing the old full new Bitmap(path) decode.
    /// </summary>
    private static (int w, int h) ProbeImagePixelSize(string path)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            if (codec != null && codec.Info.Width > 0)
                return (codec.Info.Width, codec.Info.Height);

            // Rare fallback for formats SKCodec cannot stream.
            using var bmp = SKBitmap.Decode(path);
            return bmp != null ? (bmp.Width, bmp.Height) : (0, 0);
        }
        catch
        {
            return (0, 0);
        }
    }

    private void OnMouseLeftDown(object? sender, HookPoint e)
    {
        // WH_MOUSE_LL callback thread: keep this path near-empty (WPF GlobalMouseHook
        // contract — heavy work in the callback serializes ALL mouse input system-wide and
        // risks Windows silently removing the hook via LowLevelHooksTimeout). Everything —
        // hit-test, item removal, event raise, hydra respawn (directory scan + image probe) —
        // is marshalled to the UI thread, mirroring AvaloniaBubbleService.
        //
        // WS2 (known gap, documented): the popping click is NOT swallowed —
        // AvaloniaMouseHook always calls CallNextHookEx, so a click that pops a flash also
        // lands in the application underneath (button presses, focus, drag starts). WPF's
        // clickable flashes absorb the click as real windows. The swallow path (return 1 for
        // handled hits, with the hold-to-defuse pass-through exception) is the WS2
        // mouse-hook work item; deferring the handling here costs nothing behaviorally
        // because the click already propagates either way.
        if (!IsRunning) return;
        Dispatcher.UIThread.Post(() => HandleFlashClick(e));
    }

    private void HandleFlashClick(HookPoint e)
    {
        if (!IsRunning) return;
        var settings = _settings.Current;
        if (settings == null || !settings.FlashClickable) return;

        // Physical-px hit-test: the hook reports physical screen coordinates and FlashLayer
        // geometry is physical virtual-desktop px (IAvaloniaLayer contract), so no DPI
        // conversion — correct on mixed-DPI multi-monitor setups.
        var item = _flashLayer?.HitTest(e.X, e.Y);
        if (item == null)
        {
            _logger?.LogDebug("Flash click: no item at {X},{Y}", e.X, e.Y);
            return;
        }

        PopItem(item, settings);
    }

    /// <summary>
    /// Shared pop pipeline for mouse clicks AND gaze pops — WPF parity: GazePop is "the
    /// programmatic equivalent of a mouse click" and runs the same close + hydra
    /// multiplication + FlashClicked path (FlashService.cs:196, OnFlashClicked).
    /// UI thread only.
    /// </summary>
    private void PopItem(FlashLayer.FlashItem item, AppSettings settings)
    {
        FlashClickData? data;
        lock (_sync)
        {
            _clickData.TryGetValue(item.Id, out data);
            _clickData.Remove(item.Id);
        }

        _flashLayer?.RemoveItem(item);
        FlashClicked?.Invoke(this, EventArgs.Empty);
        _logger?.LogDebug("Flash popped: item {Id} removed, CorruptionMode={Corruption}, HydraLimit={Limit}",
            item.Id, settings.CorruptionMode, settings.HydraLimit);

        // Hydra mode (WPF OnFlashClicked, FlashService.cs:1415-1431): CorruptionMode read
        // LIVE from settings at pop time; count is the LIVE on-screen count after removing
        // the popped item (WPF counts _activeWindows — the old _clickData.Count here
        // included long-expired entries and silently killed multiplication mid-run).
        if (!settings.CorruptionMode || data == null) return;

        var maxHydra = Math.Min(settings.HydraLimit, 20);
        var currentCount = _flashLayer?.ActiveCount ?? 0;

        _logger?.LogDebug("Flash hydra: currentCount={Current}, maxHydra={Max}, canMultiply={Can}",
            currentCount, maxHydra, currentCount + 1 < maxHydra);

        if (currentCount + 1 < maxHydra) // WPF gate, FlashService.cs:1426
        {
            var remainingMs = Math.Max(1000, (int)(data.ExpiresAt - DateTime.Now).TotalMilliseconds);
            TriggerMultiplication(maxHydra, currentCount, data.OriginalLifetimeMs, remainingMs, data.HydraGeneration, data.Monitor);
        }
    }

    private void TriggerMultiplication(int maxHydra, int currentCount, int parentLifetimeMs, int parentRemainingMs, int parentGeneration, ImageGeometry parentMonitor)
    {
        var settings = _settings.Current;
        if (settings == null)
        {
            _logger?.LogDebug("TriggerMultiplication: settings is null");
            return;
        }

        // Each pop spawns HydraMultiplyCount children (user setting, 2-5; legacy WPF
        // hardcoded 2 — FlashService.cs:1446-1448), capped by the remaining hydra space.
        var spaceAvailable = maxHydra - currentCount;
        var numToSpawn = Math.Min(Math.Clamp(settings.HydraMultiplyCount, 2, 5), spaceAvailable);
        if (numToSpawn <= 0) return;

        // WPF FlashService.cs:1459-1461: Linked timing inherits the parent's remaining
        // time; Independent gives children a fresh full-duration lifetime.
        var hydraLifetimeMs = settings.HydraLinkedTiming ? parentRemainingMs : parentLifetimeMs;
        var childGeneration = parentGeneration + 1;

        _logger?.LogDebug("TriggerMultiplication: spawning {Count} children (maxHydra={Max}, currentCount={Current}, linked={Linked}, lifetimeMs={Lifetime})",
            numToSpawn, maxHydra, currentCount, settings.HydraLinkedTiming, hydraLifetimeMs);

        for (int i = 0; i < numToSpawn; i++)
        {
            // Children stay on the parent's screen (WPF PickMonitor preferred-monitor
            // inheritance, FlashService.cs:1668) with full random-position geometry.
            var data = PickImageData(parentMonitor);
            if (data == null)
            {
                _logger?.LogDebug("TriggerMultiplication: PickImageData returned null");
                continue;
            }
            data.OneShot = true;
            // WPF ShowImages stagger for multiplication spawns: i * 100ms (FlashService.cs:1046).
            SpawnFlash(data, settings, childGeneration, hydraLifetimeMs, spawnDelayMs: i * 100);
        }
    }

    private bool IsOverlapping(double x, double y, double w, double h)
    {
        // Single rect-vs-items test (physical px, all live flashes regardless of
        // clickability). Replaces the old loop that repeated an identical center-point
        // hit-test once per _clickData entry while holding both locks.
        return _flashLayer?.IntersectsAny(x, y, w, h) == true;
    }

    private ImageData? PickImageData(ImageGeometry? preferredMonitor = null)
    {
        var files = GetImageFiles();
        if (files.Count == 0) return null;

        var path = files[_random.Next(files.Count)];
        try
        {
            var (pxW, pxH) = ProbeImagePixelSize(path);
            if (pxW <= 0 || pxH <= 0)
            {
                _logger?.LogDebug("AvaloniaFlashService: could not read image size for {Path}", path);
                return null;
            }

            // Monitor inheritance for hydra children (WPF PickMonitor preferred-monitor
            // path, FlashService.cs:1668): children stay on the parent's screen.
            var monitor = preferredMonitor ?? GetRandomMonitor();
            var geometry = CalculateGeometry(pxW, pxH, monitor);

            return new ImageData
            {
                FilePath = path,
                Geometry = geometry,
                Monitor = monitor
            };
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("AvaloniaFlashService: failed to load image {Path}: {Error}", path, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// WPF CalculateGeometry parity (FlashService.cs:1770-1801), transposed from DIPs to
    /// the layer's physical-px space (DIP constants scaled by the monitor's DPI scaling —
    /// the seam class fixed in the chaos migrations, commits a8bf6f10/4c6c5992):
    ///   - base box is 40% of the monitor, scaled by ImageScale% (50-250),
    ///   - aspect-fit into that box, floor 50 DIP per side,
    ///   - RANDOM position inside the monitor with a 50 DIP edge padding so images are
    ///     fully visible and clickable — this is what makes flashes "pop up randomly"
    ///     instead of pinning to the monitor's top-left when a large image collapsed the
    ///     old native-size random range to zero.
    /// </summary>
    private ImageGeometry CalculateGeometry(int pxW, int pxH, ImageGeometry monitor)
    {
        var scalePct = (_settings.Current?.ImageScale ?? 100) / 100.0;
        var dpi = monitor.Scaling > 0 ? monitor.Scaling : 1.0;

        var baseWidth = monitor.Width * 0.4;   // WPF FlashService.cs:1772
        var baseHeight = monitor.Height * 0.4;
        var ratio = Math.Min(baseWidth / pxW, baseHeight / pxH) * scalePct;

        var minSide = (int)Math.Round(50 * dpi); // WPF: 50 DIP floor (FlashService.cs:1779)
        var w = Math.Max(minSide, (int)(pxW * ratio));
        var h = Math.Max(minSide, (int)(pxH * ratio));

        var edgePadding = (int)Math.Round(50 * dpi); // WPF: const 50 DIP (FlashService.cs:1784)
        var minX = edgePadding;
        var minY = edgePadding;
        var maxX = Math.Max(minX + 1, (int)(monitor.Width - w - edgePadding));
        var maxY = Math.Max(minY + 1, (int)(monitor.Height - h - edgePadding));

        var x = (int)monitor.X + _random.Next(minX, maxX);
        var y = (int)monitor.Y + _random.Next(minY, maxY);

        return new ImageGeometry { X = x, Y = y, Width = w, Height = h, Scaling = dpi };
    }

    /// <summary>
    /// Removes click entries whose flash expired long ago (layer-side removal does not
    /// notify the service). 30s grace comfortably covers the post-expiry fade-out.
    /// </summary>
    private void PruneExpiredClickData()
    {
        lock (_sync)
        {
            if (_clickData.Count == 0) return;
            List<Guid>? stale = null;
            var cutoff = DateTime.Now.AddSeconds(-30);
            foreach (var kvp in _clickData)
            {
                if (kvp.Value.ExpiresAt < cutoff)
                    (stale ??= new List<Guid>()).Add(kvp.Key);
            }
            if (stale != null)
            {
                foreach (var id in stale) _clickData.Remove(id);
            }
        }
    }

    private List<string> GetImageFiles()
    {
        lock (_fileCache)
        {
            if (_fileCache.TryGetValue(_imagesPath, out var cached) &&
                (DateTime.UtcNow - cached.lastScan).TotalSeconds < CACHE_EXPIRY_SECONDS)
            {
                return cached.files;
            }

            try
            {
                if (!Directory.Exists(_imagesPath))
                {
                    _fileCache[_imagesPath] = (new List<string>(), DateTime.UtcNow);
                    return new List<string>();
                }

                // Scan subfolders so user-organized category folders are included (WPF parity:
                // FlashService.GetMediaFiles uses SearchOption.AllDirectories, "Scan subfolders to
                // support user-organized categories", FlashService.cs:2039). TopDirectoryOnly only
                // saw the handful of loose top-level files, so a library organized into subfolders
                // replayed the same few images every launch instead of drawing from all of them.
                var files = Directory.GetFiles(_imagesPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => IMAGE_EXTENSIONS.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                _fileCache[_imagesPath] = (files, DateTime.UtcNow);
                return files;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("AvaloniaFlashService: failed to scan images: {Error}", ex.Message);
                _fileCache[_imagesPath] = (new List<string>(), DateTime.UtcNow);
                return new List<string>();
            }
        }
    }

    private ImageGeometry GetRandomMonitor()
    {
        // Returns the chosen monitor's PHYSICAL bounds (IAvaloniaLayer coordinate contract).
        // Honors DualMonitorEnabled: primary-only when off (WPF FlashService.PickMonitor
        // parity, FlashService.cs:1632).
        try
        {
            var candidates = _screens.GetEffectScreens(_settings.Current?.DualMonitorEnabled != false);
            if (candidates.Count == 0)
                return new ImageGeometry { X = 0, Y = 0, Width = 1920, Height = 1080 };

            var screen = candidates[_random.Next(candidates.Count)];
            return new ImageGeometry
            {
                X = screen.Bounds.X,
                Y = screen.Bounds.Y,
                Width = screen.Bounds.Width,
                Height = screen.Bounds.Height,
                // Carried so DIP-defined WPF constants (50px floor/padding) can be scaled
                // into this monitor's physical space in CalculateGeometry.
                Scaling = screen.Scaling > 0 ? screen.Scaling : 1.0
            };
        }
        catch
        {
            return new ImageGeometry { X = 0, Y = 0, Width = 1920, Height = 1080 };
        }
    }

    private void ScheduleNext()
    {
        if (!IsRunning) return;

        var settings = _settings.Current;
        if (settings == null || !settings.FlashEnabled) return;

        _scheduledTimer?.Stop();

        var freq = Math.Max(1, settings.FlashFrequency);
        var baseInterval = 3600.0 / freq;
        var variance = baseInterval * 0.3;
        var interval = baseInterval + (_random.NextDouble() * variance * 2 - variance);
        interval = Math.Max(1, interval);

        _scheduledTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (!IsRunning) return;
            var s = _settings.Current;
            if (s == null || !s.FlashEnabled) return;

            try { TriggerFlash(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "AvaloniaFlashService: TriggerFlash failed"); }
            ScheduleNext();
        };
        _scheduledTimer.Tick += handler;
        _scheduledTimer.Start();
    }

    public void RefreshSchedule()
    {
        if (!IsRunning) return;
        ScheduleNext();
    }

    public void ClearFileCache()
    {
        lock (_fileCache) { _fileCache.Clear(); }
    }

    public void LoadAssets()
    {
        RefreshImagesPath();
        ClearFileCache();
    }

    public void TriggerFlashOnce(string? imagePath, int durationMs, bool playSound, bool suppressHaptic)
    {
        if (!IsRunning) return;

        var settings = _settings.Current;
        if (settings == null || _flashLayer == null) return;

        var path = imagePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            var files = GetImageFiles();
            if (files.Count == 0) return;
            path = files[_random.Next(files.Count)];
        }

        var (pxW, pxH) = ProbeImagePixelSize(path);
        if (pxW <= 0 || pxH <= 0)
        {
            _logger?.LogDebug("AvaloniaFlashService: failed to load one-shot image {Path}", path);
            return;
        }

        // Same WPF sizing/positioning contract as scheduled flashes (CalculateGeometry).
        var monitor = GetRandomMonitor();
        var geom = CalculateGeometry(pxW, pxH, monitor);

        var maxOpacity = settings.FlashOpacity / 100.0;
        SpawnDecoded(path, geom, monitor, maxOpacity, durationMs, settings.FlashClickable,
            hydraGeneration: 0, oneShot: true);
    }

    public bool GazePop(ConditioningControlPanel.Core.Platform.PixelRect rect)
    {
        if (!IsRunning) return false;

        // Gate the gaze-pop path on FlashGazePopEnabled (WPF parity: GazeFocusService gates
        // both the blink and dwell call sites on this setting; closes the lot-1 deferred row).
        var settings = _settings.Current;
        if (settings == null || !settings.FlashGazePopEnabled) return false;

        // requireClickable:false — gaze targeting is deliberately DECOUPLED from
        // FlashClickable (WPF FlashService.GetGazeTargets enumerates all live flashes;
        // (FlashClickable=OFF, FlashGazePopEnabled=ON) is an explicitly supported config).
        // rect is physical virtual-desktop px, same space as the flash items.
        var item = _flashLayer?.HitTest(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0,
            requireClickable: false);
        if (item == null) return false;

        // Shared pop pipeline: WPF GazePop runs the SAME close + hydra-multiplication +
        // FlashClicked path as a mouse click (FlashService.cs:196-201), so gaze pops
        // multiply in hydra (CorruptionMode) exactly like clicks.
        PopItem(item, settings);
        return true;
    }

    public void Dispose()
    {
        Stop();
        lock (_sync) { _clickData.Clear(); }
        // Stop() cleared the layer's items (releasing their frame refs under the layer's
        // render lock); dropping the cache refs now deterministically disposes the frames.
        _frameCache.Clear();
    }

    private sealed class ImageData
    {
        public string FilePath { get; set; } = "";
        public ImageGeometry Geometry { get; set; } = new();
        public ImageGeometry Monitor { get; set; } = new();
        public bool OneShot { get; set; }
    }

    // All geometry in PHYSICAL virtual-desktop pixels (IAvaloniaLayer coordinate contract).
    // Scaling carries the source monitor's DPI scale so WPF's DIP-defined constants can be
    // transposed into physical px (0/unset means "assume 1.0").
    private sealed record ImageGeometry
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Scaling { get; set; } = 1.0;
    }

    private sealed record FlashClickData(
        string FilePath,
        int OriginalLifetimeMs,
        int HydraGeneration,
        ImageGeometry Monitor,
        bool Clickable)
    {
        public DateTime ExpiresAt { get; } = DateTime.Now.AddMilliseconds(OriginalLifetimeMs);
    }
}
