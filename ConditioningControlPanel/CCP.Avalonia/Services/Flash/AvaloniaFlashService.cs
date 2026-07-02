using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Services.Flash;

/// <summary>
/// Avalonia implementation of the flash-image effect engine.
/// Spawns images via the unified compositor layer at a configurable frequency,
/// loads images from the user's assets folder, supports click-to-close, and
/// implements the hydra multiplication mode.
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

    private string _imagesPath = "";
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _scheduledTimer;
    private bool _isBusy;
    private bool _noImagesWarningShown;
    private readonly List<string> _lastDisplayedImagePaths = new();

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

        // Install mouse hook for flash click detection
        if (_mouseHook != null && settings.FlashClickable)
        {
            _mouseHook.LeftButtonDown += OnMouseLeftDown;
            try { _mouseHook.Install(); } catch { }
        }

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

        // Uninstall mouse hook
        if (_mouseHook != null)
        {
            _mouseHook.LeftButtonDown -= OnMouseLeftDown;
            try { _mouseHook.Uninstall(); } catch { }
        }

        // Clear all flash items from the compositor layer
        _flashLayer?.Clear();
        lock (_sync) { _clickData.Clear(); }

        _logger?.LogInformation("AvaloniaFlashService stopped");
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
            lock (_lastDisplayedImagePaths) { _lastDisplayedImagePaths.Add(data.FilePath); }

            var hydraLimit = Math.Min(settings.HydraLimit, 20);
            var count = Math.Min(settings.SimultaneousImages, hydraLimit);

            if (count <= 1)
            {
                SpawnFlash(data, settings, 0);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var copy = i == 0 ? data : PickImageData();
                    if (copy != null) SpawnFlash(copy, settings, i);
                }
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

    private void SpawnFlash(ImageData data, AppSettings settings, int hydraGeneration, int remainingMs = -1)
    {
        if (!IsRunning && !data.OneShot) return;

        var geom = data.Geometry;
        var monitor = data.Monitor;

        // Avoid overlap with existing flashes
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!IsOverlapping(geom.X, geom.Y, geom.Width, geom.Height)) break;
            geom = new ImageGeometry
            {
                X = (int)(monitor.X + _random.Next(0, Math.Max(1, (int)(monitor.Width - geom.Width)))),
                Y = (int)(monitor.Y + _random.Next(0, Math.Max(1, (int)(monitor.Height - geom.Height)))),
                Width = geom.Width,
                Height = geom.Height
            };
        }

        var bitmap = data.Bitmap;
        if (bitmap == null) return;

        var maxOpacity = settings.FlashOpacity / 100.0;
        var lifetimeMs = remainingMs > 0 ? remainingMs : Math.Max(500, (int)(settings.FlashDuration * 1000));
        var id = _flashLayer?.Spawn(data.FilePath, bitmap, geom.X, geom.Y, geom.Width, geom.Height, maxOpacity, lifetimeMs, settings.FlashClickable);

        if (id.HasValue && id.Value != Guid.Empty)
        {
            lock (_sync)
            {
                _clickData[id.Value] = new FlashClickData(
                    data.FilePath, lifetimeMs, hydraGeneration, monitor, settings.FlashClickable);
            }
        }
    }

    private void OnMouseLeftDown(object? sender, HookPoint e)
    {
        if (!IsRunning) return;
        var settings = _settings.Current;
        if (settings == null || !settings.FlashClickable) return;

        var item = _flashLayer?.HitTest(e.X, e.Y);
        if (item == null)
        {
            _logger?.LogDebug("Flash click: no item at {X},{Y}", e.X, e.Y);
            return;
        }

        FlashClickData? data;
        lock (_sync)
        {
            if (!_clickData.TryGetValue(item.Id, out data))
            {
                _logger?.LogDebug("Flash click: item {Id} not found in _clickData", item.Id);
                return;
            }
            _clickData.Remove(item.Id);
        }

        _flashLayer?.RemoveItem(item);
        FlashClicked?.Invoke(this, EventArgs.Empty);
        _logger?.LogDebug("Flash clicked: item {Id} removed, CorruptionMode={Corruption}, HydraLimit={Limit}",
            item.Id, settings.CorruptionMode, settings.HydraLimit);

        if (settings.CorruptionMode)
        {
            var maxHydra = Math.Min(settings.HydraLimit, 20);
            int currentCount;
            lock (_sync) { currentCount = _clickData.Count; }

            _logger?.LogDebug("Flash hydra: currentCount={Current}, maxHydra={Max}, canMultiply={Can}",
                currentCount, maxHydra, currentCount + 1 < maxHydra);

            if (currentCount + 1 < maxHydra)
            {
                var remainingMs = Math.Max(1000, (int)(data.ExpiresAt - DateTime.Now).TotalMilliseconds);
                TriggerMultiplication(maxHydra, currentCount, data.OriginalLifetimeMs, remainingMs, data.HydraGeneration, data.Monitor);
            }
        }
    }

    private void TriggerMultiplication(int maxHydra, int currentCount, int originalLifetimeMs, int remainingMs, int hydraGeneration, ImageGeometry monitor)
    {
        var settings = _settings.Current;
        if (settings == null)
        {
            _logger?.LogDebug("TriggerMultiplication: settings is null");
            return;
        }

        var multiplier = Math.Min(2, maxHydra - currentCount - 1);
        _logger?.LogDebug("TriggerMultiplication: spawning {Multiplier} items (maxHydra={Max}, currentCount={Count})",
            multiplier, maxHydra, currentCount);

        for (int i = 0; i < multiplier; i++)
        {
            var data = PickImageData();
            if (data == null)
            {
                _logger?.LogDebug("TriggerMultiplication: PickImageData returned null");
                continue;
            }
            data.OneShot = true;
            data.Geometry = new ImageGeometry
            {
                X = (int)(monitor.X + _random.Next(0, Math.Max(1, (int)(monitor.Width - 200)))),
                Y = (int)(monitor.Y + _random.Next(0, Math.Max(1, (int)(monitor.Height - 200)))),
                Width = data.Geometry.Width,
                Height = data.Geometry.Height
            };
            SpawnFlash(data, settings, hydraGeneration + 1, remainingMs);
        }
    }

    private bool IsOverlapping(double x, double y, double w, double h)
    {
        lock (_sync)
        {
            foreach (var kvp in _clickData)
            {
                var item = _flashLayer?.HitTest(x + w / 2, y + h / 2);
                if (item != null) return true;
            }
        }
        return false;
    }

    private ImageData? PickImageData()
    {
        var files = GetImageFiles();
        if (files.Count == 0) return null;

        var path = files[_random.Next(files.Count)];
        try
        {
            var bitmap = new Bitmap(path);
            var monitor = GetRandomMonitor();
            var scale = Math.Min(1.0, Math.Min(
                monitor.Width / (bitmap.PixelSize.Width / monitor.Scaling),
                monitor.Height / (bitmap.PixelSize.Height / monitor.Scaling)));
            var w = (int)(bitmap.PixelSize.Width / monitor.Scaling * scale);
            var h = (int)(bitmap.PixelSize.Height / monitor.Scaling * scale);
            var x = (int)(monitor.X + _random.Next(0, Math.Max(1, (int)(monitor.Width - w))));
            var y = (int)(monitor.Y + _random.Next(0, Math.Max(1, (int)(monitor.Height - h))));

            return new ImageData
            {
                FilePath = path,
                Bitmap = bitmap,
                Geometry = new ImageGeometry { X = x, Y = y, Width = w, Height = h },
                Monitor = monitor
            };
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("AvaloniaFlashService: failed to load image {Path}: {Error}", path, ex.Message);
            return null;
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

                var files = Directory.GetFiles(_imagesPath, "*.*", SearchOption.TopDirectoryOnly)
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
        try
        {
            var all = _screens.GetAllScreens();
            if (all.Count == 0)
                return new ImageGeometry { X = 0, Y = 0, Width = 1920, Height = 1080 };

            var screen = all[_random.Next(all.Count)];
            return new ImageGeometry
            {
                X = screen.Bounds.X / screen.Scaling,
                Y = screen.Bounds.Y / screen.Scaling,
                Width = screen.Bounds.Width / screen.Scaling,
                Height = screen.Bounds.Height / screen.Scaling,
                Scaling = screen.Scaling
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
        if (settings == null) return;

        Bitmap? bitmap = null;
        try
        {
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                bitmap = new Bitmap(imagePath);
            }
            else
            {
                var files = GetImageFiles();
                if (files.Count > 0)
                {
                    var path = files[_random.Next(files.Count)];
                    bitmap = new Bitmap(path);
                    imagePath = path;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("AvaloniaFlashService: failed to load one-shot image: {Error}", ex.Message);
            return;
        }

        if (bitmap == null) return;

        var monitor = GetRandomMonitor();
        var scale = Math.Min(1.0, Math.Min(
            monitor.Width / (bitmap.PixelSize.Width / monitor.Scaling),
            monitor.Height / (bitmap.PixelSize.Height / monitor.Scaling)));
        var w = (int)(bitmap.PixelSize.Width / monitor.Scaling * scale);
        var h = (int)(bitmap.PixelSize.Height / monitor.Scaling * scale);
        var x = (int)(monitor.X + _random.Next(0, Math.Max(1, (int)(monitor.Width - w))));
        var y = (int)(monitor.Y + _random.Next(0, Math.Max(1, (int)(monitor.Height - h))));

        var maxOpacity = settings.FlashOpacity / 100.0;
        var id = _flashLayer?.Spawn(imagePath, bitmap, x, y, w, h, maxOpacity, durationMs, settings.FlashClickable);

        if (id.HasValue && id.Value != Guid.Empty)
        {
            lock (_sync)
            {
                _clickData[id.Value] = new FlashClickData(
                    imagePath ?? "", durationMs, 0, monitor, settings.FlashClickable);
            }
        }
    }

    public bool GazePop(ConditioningControlPanel.Core.Platform.PixelRect rect)
    {
        if (!IsRunning) return false;

        var item = _flashLayer?.HitTest(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0);
        if (item == null) return false;

        lock (_sync)
        {
            _clickData.Remove(item.Id);
        }
        _flashLayer?.RemoveItem(item);
        FlashClicked?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Dispose()
    {
        Stop();
        lock (_sync) { _clickData.Clear(); }
    }

    private sealed class ImageData
    {
        public string FilePath { get; set; } = "";
        public Bitmap? Bitmap { get; set; }
        public ImageGeometry Geometry { get; set; } = new();
        public ImageGeometry Monitor { get; set; } = new();
        public bool OneShot { get; set; }
    }

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
