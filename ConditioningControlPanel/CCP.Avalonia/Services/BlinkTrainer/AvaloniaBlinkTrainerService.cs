using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Chaos;
using Microsoft.Extensions.Logging;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.BlinkTrainer;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Webcam;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Services.BlinkTrainer;

/// <summary>
/// Avalonia implementation of the Blink Trainer: a transparent, topmost, click-through
/// overlay that swaps to a random image/GIF/video on every blink. Auto-stops after the
/// configured duration.
/// </summary>
public sealed class AvaloniaBlinkTrainerService : IBlinkTrainerService, IDisposable
{
    private const int MaxTiles = 6;

    // Cold-start readiness wait (W-3/P-7): StartTracking() is fire-and-forget and
    // IsRunning flips asynchronously, so an immediate check always failed on the
    // first activation. ~8s upper bound.
    private const int BlinkWebcamStartTimeoutMs = 8000;
    private const int BlinkWebcamPollIntervalMs = 50;

    private readonly object _lock = new();
    private readonly ISettingsService _settings;
    private readonly IWebcamService _webcam;
    private readonly IScreenProvider _screens;
    private readonly ILibVlcProvider _libVlcProvider;
    private readonly IHapticsService? _haptics;
    private readonly IQuestService? _quests;
    private readonly ILogger<AvaloniaBlinkTrainerService>? _logger;

    private readonly List<OverlayInstance> _overlays = new();
    private List<string> _pool = new();
    private int _lastIndex = -1;
    private DispatcherTimer? _durationTimer;
    private bool _subscribed;

    private readonly object _cacheLock = new();
    private Dictionary<string, double> _aspectCache = new();
    private List<string> _imagesAll = new();
    private Dictionary<AspectBucket, List<string>> _imagesByBucket = NewEmptyBuckets();
    private CancellationTokenSource? _cacheCts;

    public bool IsRunning { get; private set; }
    public string LastError { get; private set; } = "";
    public DateTime? StartedAt { get; private set; }
    public TimeSpan? Duration { get; private set; }

    public TimeSpan Remaining
    {
        get
        {
            if (!IsRunning || StartedAt == null || Duration == null) return TimeSpan.Zero;
            var rem = Duration.Value - (DateTime.UtcNow - StartedAt.Value);
            return rem < TimeSpan.Zero ? TimeSpan.Zero : rem;
        }
    }

    public event Action? StateChanged;

    public AvaloniaBlinkTrainerService(
        ISettingsService settings,
        IWebcamService webcam,
        IScreenProvider screens,
        ILibVlcProvider libVlcProvider,
        IHapticsService? haptics = null,
        IQuestService? quests = null,
        ILogger<AvaloniaBlinkTrainerService>? logger = null)
    {
        _settings = settings;
        _webcam = webcam;
        _screens = screens;
        _libVlcProvider = libVlcProvider;
        _haptics = haptics;
        _quests = quests;
        _logger = logger;
    }

    public bool Start()
    {
        lock (_lock)
        {
            if (IsRunning) return true;

            var settings = _settings.Current;
            if (settings == null) { LastError = "Settings not loaded."; return false; }

            // Consent gate: require BOTH the flag AND a current consent version, mirroring
            // the IWebcamService consumers' IsConsentCurrent() check
            // (WebcamConsent.ConsentVersion). A stale version (left over from before a
            // privacy-contract bump) must fail fast here with the no-consent message
            // instead of passing the gate, then silently timing out at the 8s
            // WaitForWebcamReady below because the webcam service itself refuses a
            // stale consent.
            if (!settings.WebcamConsentGiven
                || settings.WebcamConsentVersion != WebcamConsent.ConsentVersion)
            {
                LastError = Loc.Get("blink_trainer_error_no_consent");
                return false;
            }

            var poolHelper = BlinkTrainerAssetPool.Build(settings.BlinkTrainerFolders, settings.BlinkTrainerIncludeVideos);
            if (poolHelper.IsEmpty)
            {
                LastError = settings.BlinkTrainerFolders == null || settings.BlinkTrainerFolders.Count == 0
                    ? Loc.Get("blink_trainer_error_no_folders")
                    : Loc.Get("blink_trainer_error_no_assets");
                return false;
            }

            var pool = poolHelper.Paths.ToList();

            if (!_webcam.IsRunning)
            {
                _webcam.StartTracking();
                _logger?.LogInformation("BlinkTrainer: waiting for webcam readiness…");
                // Cold-start race (W-3/P-7): StartTracking() is fire-and-forget, so poll
                // readiness instead of failing on the immediate IsRunning check.
                if (!WaitForWebcamReady(BlinkWebcamStartTimeoutMs))
                {
                    LastError = Loc.GetF("blink_trainer_error_webcam_start_format", "starting");
                    return false;
                }
            }

            var screens = GazeContentScreenPolicy.ResolveGazeReactiveScreens(settings, _webcam, _screens);
            var opacity = Math.Clamp(settings.BlinkTrainerOpacity, 1, 100) / 100.0;

            foreach (var screen in screens)
            {
                if (screen == null) continue;
                var ov = CreateOverlay(screen, opacity);
                if (ov != null) _overlays.Add(ov);
            }

            if (_overlays.Count == 0)
            {
                LastError = Loc.Get("blink_trainer_error_overlay_create");
                Cleanup();
                return false;
            }

            _pool = pool;
            _lastIndex = -1;

            lock (_cacheLock)
            {
                _imagesAll = _pool.Where(p => !BlinkTrainerAssetPool.IsVideo(p)).ToList();
                _imagesByBucket = NewEmptyBuckets();
                _aspectCache = new Dictionary<string, double>();
            }
            _cacheCts = new CancellationTokenSource();
            _ = Task.Run(() => BuildAspectCache(_imagesAll, _cacheCts.Token), _cacheCts.Token);

            _webcam.OnBlink += HandleBlink;
            _subscribed = true;

            Duration = TimeSpan.FromMinutes(Math.Clamp(settings.BlinkTrainerDurationMinutes, 1, 180));
            StartedAt = DateTime.UtcNow;
            _durationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _durationTimer.Tick += DurationTimer_Tick;
            _durationTimer.Start();

            IsRunning = true;
            LastError = "";
            _logger?.LogInformation(
                "BlinkTrainer: started — pool={Count} assets, duration={Mins}m, opacity={Opacity}%, screens={Screens}",
                _pool.Count, settings.BlinkTrainerDurationMinutes, settings.BlinkTrainerOpacity, _overlays.Count);

            ShowRandom();
            try { StateChanged?.Invoke(); } catch { }
            return true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning && _overlays.Count == 0 && !_subscribed) return;
            Cleanup();
            IsRunning = false;
            StartedAt = null;
            Duration = null;
            _logger?.LogInformation("BlinkTrainer: stopped");
        }
        try { StateChanged?.Invoke(); } catch { }
    }

    /// <summary>
    /// Bounded readiness wait for the cold-start race (W-3/P-7). The session toggle
    /// invokes Start() on a background thread (await Task.Run in the tab VM), so a
    /// short blocking poll is safe there; if we are somehow on the UI thread we must
    /// NOT block (the webcam start runs on the UI thread and would deadlock), so we
    /// return optimistically and let the next blink flow once tracking is up.
    /// </summary>
    private bool WaitForWebcamReady(int timeoutMs)
    {
        if (_webcam.IsRunning) return true;
        if (Dispatcher.UIThread.CheckAccess())
        {
            // On the UI thread: don't block. Tracking will come up shortly.
            return true;
        }
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try { Thread.Sleep(BlinkWebcamPollIntervalMs); }
            catch (ThreadInterruptedException) { break; }
            if (_webcam.IsRunning) return true;
        }
        return _webcam.IsRunning;
    }

    private void Cleanup()
    {
        if (_subscribed)
        {
            _webcam.OnBlink -= HandleBlink;
        }
        _subscribed = false;

        _durationTimer?.Stop();
        if (_durationTimer != null) _durationTimer.Tick -= DurationTimer_Tick;
        _durationTimer = null;

        try { _cacheCts?.Cancel(); } catch { }
        try { _cacheCts?.Dispose(); } catch { }
        _cacheCts = null;

        foreach (var ov in _overlays)
        {
            try { TeardownHostChildren(ov.Host); } catch { }
            try { ov.Window.Close(); } catch { }
        }
        _overlays.Clear();
        _pool = new List<string>();
        _lastIndex = -1;

        lock (_cacheLock)
        {
            _aspectCache = new Dictionary<string, double>();
            _imagesAll = new List<string>();
            _imagesByBucket = NewEmptyBuckets();
        }
    }

    private void DurationTimer_Tick(object? sender, EventArgs e)
    {
        if (Remaining <= TimeSpan.Zero)
        {
            _logger?.LogInformation("BlinkTrainer: duration elapsed — auto-stopping");
            Stop();
        }
    }

    private void HandleBlink()
    {
        if (!IsRunning) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsRunning) return;
            ShowRandom();
            TriggerBlinkHaptic();
            try { _quests?.TrackBlinkTrainerBlink(); } catch { }
        });
    }

    private void TriggerBlinkHaptic()
    {
        if (_haptics == null) return;
        _ = _haptics.BlinkPulseAsync();
    }

    private void ShowRandom()
    {
        if (_pool.Count == 0 || _overlays.Count == 0) return;

        int idx = Random.Shared.Next(_pool.Count);
        if (idx == _lastIndex && _pool.Count > 1) idx = (idx + 1) % _pool.Count;
        _lastIndex = idx;
        var path = _pool[idx];
        var isVideo = BlinkTrainerAssetPool.IsVideo(path);

        if (isVideo)
        {
            try { ApplyVideoToAllOverlays(path); }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "BlinkTrainer: failed to apply video {Path}", path);
            }
            return;
        }

        var mix = _settings.Current?.BlinkTrainerMixImages == true;
        foreach (var ov in _overlays)
        {
            try
            {
                if (mix) ApplyAssetMixed(ov, path);
                else ApplyAsset(ov, path);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "BlinkTrainer: failed to apply asset {Path}", path);
            }
        }
    }

    private void ApplyVideoToAllOverlays(string path)
    {
        foreach (var ov in _overlays)
        {
            try
            {
                TeardownHostChildren(ov.Host);
                ov.Host.Children.Clear();

                var video = new AvaloniaInlineLoopVideo(_libVlcProvider.Value, path, (uint)Math.Max(480, ov.Window.Width), (uint)Math.Max(270, ov.Window.Height));
                var surface = video.Surface;
                surface.HorizontalAlignment = HorizontalAlignment.Stretch;
                surface.VerticalAlignment = VerticalAlignment.Stretch;
                ov.Host.Children.Add(surface);
                ov.Disposables.Add(video);
                video.Resume();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "BlinkTrainer: failed to create video tile for {Path}", path);
            }
        }
    }

    private void ApplyAsset(OverlayInstance ov, string path)
    {
        TeardownHostChildren(ov.Host);
        ov.Host.Children.Clear();

        var isGif = Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase);
        var imageAspect = MeasureImageAspect(path);
        var screenAspect = ov.Window.Width > 0 && ov.Window.Height > 0
            ? ov.Window.Width / ov.Window.Height
            : 16.0 / 9.0;

        int cols = 1, rows = 1;
        if (imageAspect > 0)
        {
            if (imageAspect < screenAspect * 0.95)
                cols = Math.Min(MaxTiles, Math.Max(1, (int)Math.Ceiling(screenAspect / imageAspect)));
            else if (imageAspect > screenAspect * 1.05)
                rows = Math.Min(MaxTiles, Math.Max(1, (int)Math.Ceiling(imageAspect / screenAspect)));
        }

        if (cols == 1 && rows == 1)
        {
            ov.Host.Children.Add(BuildTile(path, isGif, null, Stretch.Uniform, out var d));
            if (d != null) ov.Disposables.Add(d);
            return;
        }

        var tileGrid = new global::Avalonia.Controls.Primitives.UniformGrid { Columns = cols, Rows = rows };
        Bitmap? shared = null;
        if (!isGif)
        {
            try
            {
                using var fs = File.OpenRead(path);
                shared = new Bitmap(fs);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "BlinkTrainer: shared bitmap load failed for {Path}", path);
            }
        }

        var total = cols * rows;
        for (var i = 0; i < total; i++)
        {
            var tile = BuildTile(path, isGif, shared, Stretch.UniformToFill, out var disp);
            tileGrid.Children.Add(tile);
            if (disp != null) ov.Disposables.Add(disp);
        }
        ov.Host.Children.Add(tileGrid);
        if (shared != null) ov.Disposables.Add(shared);
    }

    private void ApplyAssetMixed(OverlayInstance ov, string leadPath)
    {
        TeardownHostChildren(ov.Host);
        ov.Host.Children.Clear();

        var leadAspect = MeasureImageAspectCached(leadPath);
        var screenAspect = ov.Window.Width > 0 && ov.Window.Height > 0
            ? ov.Window.Width / ov.Window.Height
            : 16.0 / 9.0;

        int cols = 1, rows = 1;
        if (leadAspect > 0)
        {
            if (leadAspect < screenAspect * 0.95)
                cols = Math.Min(MaxTiles, Math.Max(1, (int)Math.Ceiling(screenAspect / leadAspect)));
            else if (leadAspect > screenAspect * 1.05)
                rows = Math.Min(MaxTiles, Math.Max(1, (int)Math.Ceiling(leadAspect / screenAspect)));
        }

        if (cols == 1 && rows == 1)
        {
            var leadIsGif = Path.GetExtension(leadPath).Equals(".gif", StringComparison.OrdinalIgnoreCase);
            ov.Host.Children.Add(BuildTile(leadPath, leadIsGif, null, Stretch.Uniform, out var d));
            if (d != null) ov.Disposables.Add(d);
            return;
        }

        var total = cols * rows;
        var paths = new List<string>(total) { leadPath };
        var bucket = GetCompatibleImages(AspectBucketOf(leadAspect));
        var candidates = bucket.Where(p => !string.Equals(p, leadPath, StringComparison.OrdinalIgnoreCase)).ToList();
        Shuffle(candidates);

        for (int i = 0; i < total - 1; i++)
        {
            if (i < candidates.Count) paths.Add(candidates[i]);
            else if (candidates.Count > 0) paths.Add(candidates[Random.Shared.Next(candidates.Count)]);
            else paths.Add(leadPath);
        }

        var tileGrid = new global::Avalonia.Controls.Primitives.UniformGrid { Columns = cols, Rows = rows };
        foreach (var p in paths)
        {
            var pIsGif = Path.GetExtension(p).Equals(".gif", StringComparison.OrdinalIgnoreCase);
            var tile = BuildTile(p, pIsGif, null, Stretch.UniformToFill, out var disp);
            tileGrid.Children.Add(tile);
            if (disp != null) ov.Disposables.Add(disp);
        }
        ov.Host.Children.Add(tileGrid);
    }

    private static Control BuildTile(string path, bool isGif, Bitmap? sharedBmp, Stretch stretch, out IDisposable? disposable)
    {
        disposable = null;
        var img = new Image
        {
            Stretch = stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        if (isGif)
        {
            var gif = AvaloniaAnimatedGif.TryCreate(path);
            if (gif != null)
            {
                img.Source = gif.Source;
                gif.FrameRendered += (_, _) => img.InvalidateVisual();
                gif.Start();
                disposable = gif;
            }
        }
        else if (sharedBmp != null)
        {
            img.Source = sharedBmp;
        }
        else
        {
            try
            {
                using var fs = File.OpenRead(path);
                var bmp = new Bitmap(fs);
                img.Source = bmp;
                disposable = bmp;
            }
            catch { }
        }

        return img;
    }

    private static double MeasureImageAspect(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var bmp = new Bitmap(fs);
            if (bmp.PixelSize.Height > 0)
                return (double)bmp.PixelSize.Width / bmp.PixelSize.Height;
        }
        catch { }
        return 1.0;
    }

    private double MeasureImageAspectCached(string path)
    {
        lock (_cacheLock)
        {
            if (_aspectCache.TryGetValue(path, out var cached)) return cached;
        }
        var measured = MeasureImageAspect(path);
        lock (_cacheLock)
        {
            _aspectCache[path] = measured;
            var bucket = AspectBucketOf(measured);
            if (_imagesByBucket.TryGetValue(bucket, out var list) && !list.Contains(path))
                list.Add(path);
        }
        return measured;
    }

    private void BuildAspectCache(List<string> paths, CancellationToken token)
    {
        foreach (var path in paths)
        {
            if (token.IsCancellationRequested) return;
            double asp;
            try { asp = MeasureImageAspect(path); }
            catch { asp = 1.0; }
            lock (_cacheLock)
            {
                _aspectCache[path] = asp;
                var bucket = AspectBucketOf(asp);
                if (_imagesByBucket.TryGetValue(bucket, out var list) && !list.Contains(path))
                    list.Add(path);
            }
        }
    }

    private List<string> GetCompatibleImages(AspectBucket bucket)
    {
        lock (_cacheLock)
        {
            if (_imagesByBucket.TryGetValue(bucket, out var list) && list.Count >= 2)
                return new List<string>(list);
            return new List<string>(_imagesAll);
        }
    }

    private static AspectBucket AspectBucketOf(double aspect)
    {
        if (aspect < 0.85) return AspectBucket.Portrait;
        if (aspect > 1.15) return AspectBucket.Landscape;
        return AspectBucket.Square;
    }

    private static Dictionary<AspectBucket, List<string>> NewEmptyBuckets() => new()
    {
        { AspectBucket.Portrait, new List<string>() },
        { AspectBucket.Square, new List<string>() },
        { AspectBucket.Landscape, new List<string>() },
    };

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static void TeardownHostChildren(Grid host)
    {
        foreach (var child in host.Children)
        {
            if (child is Image img)
            {
                try { img.Source = null; } catch { }
            }
            else if (child is global::Avalonia.Controls.Primitives.UniformGrid ug)
            {
                foreach (var inner in ug.Children.OfType<Image>())
                {
                    try { inner.Source = null; } catch { }
                }
                ug.Children.Clear();
            }
        }
        host.Children.Clear();
    }

    private OverlayInstance? CreateOverlay(ScreenInfo screen, double opacity)
    {
        try
        {
            var host = new Grid { ClipToBounds = true };
            var window = new Window
            {
                WindowDecorations = WindowDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                CanResize = false,
                Content = host,
                Title = "BlinkTrainerOverlay",
                Opacity = Math.Clamp(opacity, 0.01, 1.0),
            };

            window.Opened += (_, _) =>
            {
                try
                {
                    window.ConstrainToScreen(screen);
                    ChaosWin32Helper.ApplyOverlayExStyles(window, transparent: true);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "BlinkTrainer: overlay window styling failed");
                }
            };

            window.Show();
            return new OverlayInstance(window, host);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BlinkTrainer: failed to create overlay window");
            return null;
        }
    }

    public void Dispose() => Stop();

    private sealed class OverlayInstance
    {
        public Window Window { get; }
        public Grid Host { get; }
        public List<IDisposable> Disposables { get; } = new();

        public OverlayInstance(Window window, Grid host)
        {
            Window = window;
            Host = host;
        }
    }

    private enum AspectBucket { Portrait, Square, Landscape }
}
