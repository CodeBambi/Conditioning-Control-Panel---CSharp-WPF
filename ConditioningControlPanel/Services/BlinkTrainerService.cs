using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using XamlAnimatedGif;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Lab feature: a transparent, topmost, click-through overlay that displays a
/// random image / GIF / video from a user-selected folder pool, swapping to a
/// new random asset every time WebcamTrackingService.OnBlink fires. Auto-stops
/// after the configured duration. Coexists with the gaze features — both
/// subscribe to the shared App.Webcam stream.
/// </summary>
public class BlinkTrainerService : IDisposable
{
    private readonly object _lock = new();
    private readonly List<OverlayInstance> _overlays = new();
    private List<string> _pool = new();
    private int _lastIndex = -1;
    private DispatcherTimer? _durationTimer;
    private bool _subscribed;

    // Mix-mode state. Aspect lookups are served from _aspectCache after a
    // background warm-up; until then GetCompatibleImages falls back to
    // _imagesAll so the first blinks still render.
    private readonly object _cacheLock = new();
    // Replaced (not cleared in place) on each Start so a still-draining
    // background warm-up from the previous Start can't accidentally
    // populate the new run's cache with stale aspect entries.
    private Dictionary<string, double> _aspectCache = new();
    private List<string> _imagesAll = new();
    private Dictionary<AspectBucket, List<string>> _imagesByBucket = NewEmptyBuckets();
    private CancellationTokenSource? _cacheCts;

    public bool IsRunning { get; private set; }
    public string LastError { get; private set; } = "";
    public DateTime? StartedAt { get; private set; }
    public TimeSpan? Duration { get; private set; }

    /// <summary>Time remaining; TimeSpan.Zero when not running.</summary>
    public TimeSpan Remaining
    {
        get
        {
            if (!IsRunning || StartedAt == null || Duration == null) return TimeSpan.Zero;
            var rem = Duration.Value - (DateTime.UtcNow - StartedAt.Value);
            return rem < TimeSpan.Zero ? TimeSpan.Zero : rem;
        }
    }

    /// <summary>Fires when IsRunning flips, on the UI thread.</summary>
    public event Action? StateChanged;

    public BlinkTrainerService()
    {
        if (Application.Current != null)
            Application.Current.Exit += (_, _) => Stop();
    }

    public bool Start()
    {
        lock (_lock)
        {
            if (IsRunning) return true;

            var settings = App.Settings?.Current;
            if (settings == null) { LastError = "Settings not loaded."; return false; }

            // Build the asset pool from selected folders via the shared helper
            // (also used by the Exclusives tab's stage preview).
            var poolHelper = BlinkTrainerAssetPool.Build(
                settings.BlinkTrainerFolders,
                settings.BlinkTrainerIncludeVideos);

            if (poolHelper.IsEmpty)
            {
                LastError = settings.BlinkTrainerFolders == null || settings.BlinkTrainerFolders.Count == 0
                    ? Loc.Get("blink_trainer_error_no_folders")
                    : Loc.Get("blink_trainer_error_no_assets");
                return false;
            }

            var pool = poolHelper.Paths.ToList();

            // Webcam: must have consent + be running.
            if (App.Webcam == null) { LastError = Loc.Get("blink_trainer_error_webcam_uninit"); return false; }
            if (!WebcamTrackingService.IsConsentCurrent()) { LastError = Loc.Get("blink_trainer_error_no_consent"); return false; }
            if (!App.Webcam.IsRunning && !App.Webcam.Start())
            {
                LastError = Loc.GetF("blink_trainer_error_webcam_start_format", App.Webcam.State);
                return false;
            }

            // Gaze-reactive spawn: the user has to be looking at the overlay
            // for blink detection to work, so pin to the calibrated screen
            // when calibration is loaded. No-op without calibration.
            var screens = GazeContentScreenPolicy.ResolveGazeReactiveScreens(settings);

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

            // Pre-bucket the image pool by aspect so per-blink picks don't
            // re-open files. Synchronous list of all non-video images is
            // available immediately; per-bucket lists are populated by a
            // background task as aspects are measured.
            lock (_cacheLock)
            {
                _imagesAll = _pool.Where(p => !IsVideoExt(p)).ToList();
                _imagesByBucket = NewEmptyBuckets();
                _aspectCache = new Dictionary<string, double>();
            }
            _cacheCts = new CancellationTokenSource();
            var cacheToken = _cacheCts.Token;
            var imagesToWarm = _imagesAll;
            _ = Task.Run(() => BuildAspectCache(imagesToWarm, cacheToken), cacheToken);

            App.Webcam.OnBlink += HandleBlink;
            _subscribed = true;

            Duration = TimeSpan.FromMinutes(Math.Clamp(settings.BlinkTrainerDurationMinutes, 1, 180));
            StartedAt = DateTime.UtcNow;
            _durationTimer = new DispatcherTimer { Interval = Duration.Value };
            _durationTimer.Tick += DurationTimer_Tick;
            _durationTimer.Start();

            IsRunning = true;
            LastError = "";
            App.Logger?.Information(
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
            App.Logger?.Information("BlinkTrainer: stopped");
        }
        try { StateChanged?.Invoke(); } catch { }
    }

    private void Cleanup()
    {
        if (_subscribed && App.Webcam != null)
        {
            App.Webcam.OnBlink -= HandleBlink;
        }
        _subscribed = false;

        if (_durationTimer != null)
        {
            try { _durationTimer.Stop(); } catch { }
            _durationTimer.Tick -= DurationTimer_Tick;
            _durationTimer = null;
        }

        try { _cacheCts?.Cancel(); } catch { }
        try { _cacheCts?.Dispose(); } catch { }
        _cacheCts = null;

        foreach (var ov in _overlays)
        {
            if (ov.ZOrderTimer != null)
            {
                try { ov.ZOrderTimer.Stop(); } catch { }
                ov.ZOrderTimer = null;
            }
            try { TeardownHostChildren(ov.Host); } catch { }
            try { ov.Window.Close(); } catch { }
        }
        _overlays.Clear();
        _pool = new();
        _lastIndex = -1;
        lock (_cacheLock)
        {
            _aspectCache = new Dictionary<string, double>();
            _imagesAll = new();
            _imagesByBucket = NewEmptyBuckets();
        }
    }

    private void DurationTimer_Tick(object? sender, EventArgs e)
    {
        App.Logger?.Information("BlinkTrainer: duration elapsed — auto-stopping");
        Stop();
    }

    private void HandleBlink()
    {
        // OnBlink is already dispatcher-marshaled by WebcamTrackingService.
        if (!IsRunning) return;
        ShowRandom();
        TriggerBlinkHaptic();
        // Quest credit: a blink logged in the live blink trainer (Patreon-exclusive
        // category). This path only runs while the live trainer is active.
        App.Quests?.TrackBlinkTrainerBlink();
    }

    /// <summary>
    /// Pulse the connected haptic device on each blink. No-ops unless the user
    /// has haptics enabled, the Blink feature on, and a device connected (all
    /// checked inside HapticService.BlinkPulseAsync). Fire-and-forget off the
    /// UI thread so blink handling never blocks on the device's HTTP round-trip.
    /// </summary>
    private static void TriggerBlinkHaptic()
    {
        var haptics = App.Haptics;
        if (haptics == null) return;

        _ = Task.Run(async () =>
        {
            try { await haptics.BlinkPulseAsync(); }
            catch (Exception ex) { App.Logger?.Debug(ex, "BlinkTrainer: blink haptic failed"); }
        });
    }

    private void ShowRandom()
    {
        if (_pool.Count == 0 || _overlays.Count == 0) return;

        int idx = Random.Shared.Next(_pool.Count);
        if (idx == _lastIndex && _pool.Count > 1) idx = (idx + 1) % _pool.Count;
        _lastIndex = idx;
        var path = _pool[idx];
        var isVideo = IsVideoExt(path);

        // Videos: single decoder on the primary overlay, secondaries mirror
        // via VisualBrush. Two MediaElements for the same file race their
        // decoders and the secondary visibly stalls on its first frame for a
        // few seconds before catching up — same fix VideoService landed in
        // bd628e8 ("Fix multi-monitor video sync").
        if (isVideo)
        {
            try { ApplyVideoToAllOverlays(path); }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BlinkTrainer: failed to apply video {Path}", path);
            }
            return;
        }

        var mix = App.Settings?.Current?.BlinkTrainerMixImages == true;
        foreach (var ov in _overlays)
        {
            try
            {
                if (mix) ApplyAssetMixed(ov, path);
                else ApplyAsset(ov, path);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BlinkTrainer: failed to apply asset {Path}", path);
            }
        }
    }

    /// <summary>
    /// Plays a video on overlay[0]'s MediaElement and mirrors it onto every
    /// other overlay via a VisualBrush. Single decoder, automatic frame-perfect
    /// sync — no per-screen first-frame stall.
    /// </summary>
    private void ApplyVideoToAllOverlays(string path)
    {
        if (_overlays.Count == 0) return;

        // Tear down all overlays first so the visual tree is clean.
        foreach (var ov in _overlays)
        {
            try
            {
                TeardownHostChildren(ov.Host);
                ov.Host.Children.Clear();
            }
            catch { }
        }

        var primary = _overlays[0];
        var media = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = Stretch.UniformToFill,
            IsMuted = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Source = new Uri(path),
        };
        media.MediaEnded += (_, _) =>
        {
            try { media.Position = TimeSpan.Zero; media.Play(); } catch { }
        };
        primary.Host.Children.Add(media);

        for (int i = 1; i < _overlays.Count; i++)
        {
            var sec = _overlays[i];
            var brush = new VisualBrush
            {
                Visual = media,
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
            };
            var rect = new System.Windows.Shapes.Rectangle
            {
                Fill = brush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            sec.Host.Children.Add(rect);
        }

        try { media.Play(); } catch { }
    }

    private static bool IsVideoExt(string path)
    {
        var ext = Path.GetExtension(path)?.ToLowerInvariant();
        return ext is ".mp4" or ".webm" or ".mov" or ".avi" or ".mkv";
    }

    /// <summary>
    /// Replace the overlay's host content with the new image asset, tiling N×
    /// when the asset's aspect ratio doesn't match the screen so the whole
    /// screen stays covered. A 9:16 portrait image on a 16:9 screen ends up
    /// as ~3 horizontal tiles; a square image on a 16:9 screen tiles 2×;
    /// aspects that already match render as a single tile. Videos take a
    /// separate path (ApplyVideoToAllOverlays).
    /// </summary>
    private static void ApplyAsset(OverlayInstance ov, string path)
    {
        TeardownHostChildren(ov.Host);
        ov.Host.Children.Clear();

        var (isGif, isAnimatedWebp) = ClassifyAnimation(path);
        var imageAspect = MeasureImageAspect(path);
        var screenAspect = ov.Window.ActualWidth > 0 && ov.Window.ActualHeight > 0
            ? ov.Window.ActualWidth / ov.Window.ActualHeight
            : 16.0 / 9.0;

        // Compute tile count. 5% deadband = aspects already match closely
        // enough that a single Uniform fit looks fine.
        int cols = 1, rows = 1;
        if (imageAspect > 0)
        {
            if (imageAspect < screenAspect * 0.95)
                cols = Math.Min(MaxTiles, Math.Max(1, (int)Math.Ceiling(screenAspect / imageAspect)));
            else if (imageAspect > screenAspect * 1.05)
                rows = Math.Min(MaxTiles, Math.Max(1, (int)Math.Ceiling(imageAspect / screenAspect)));
        }

        // Single-tile path: Uniform fit, no cropping.
        if (cols == 1 && rows == 1)
        {
            var img = BuildTile(path, isGif, isAnimatedWebp, null, Stretch.Uniform, TileDecodeDim(ov, 1, 1));
            ov.Host.Children.Add(img);
            return;
        }

        // Multi-tile path: UniformGrid + UniformToFill per cell. Each cell
        // gets a slightly different aspect from the source image so each tile
        // crops a little — that's the price of fully covering the screen.
        var tileGrid = new UniformGrid { Columns = cols, Rows = rows };
        BitmapImage? shared = null;
        if (!isGif && !isAnimatedWebp)
        {
            try
            {
                shared = new BitmapImage();
                shared.BeginInit();
                shared.CacheOption = BitmapCacheOption.OnLoad;
                shared.UriSource = new Uri(path);
                shared.EndInit();
                shared.Freeze();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BlinkTrainer: shared bitmap load failed for {Path}", path);
                shared = null;
            }
        }

        var total = cols * rows;
        var tileDim = TileDecodeDim(ov, cols, rows);
        for (var i = 0; i < total; i++)
        {
            tileGrid.Children.Add(BuildTile(path, isGif, isAnimatedWebp, shared, Stretch.UniformToFill, tileDim));
        }
        ov.Host.Children.Add(tileGrid);
    }

    /// <summary>
    /// Mix-mode variant of ApplyAsset: tile dimensions still come from the
    /// lead image's aspect (so screen coverage math is unchanged), but each
    /// cell shows a different image — preferring images from the lead's
    /// aspect bucket so the per-cell UniformToFill crops stay coherent.
    /// </summary>
    private void ApplyAssetMixed(OverlayInstance ov, string leadPath)
    {
        TeardownHostChildren(ov.Host);
        ov.Host.Children.Clear();

        var leadAspect = MeasureImageAspectCached(leadPath);
        var screenAspect = ov.Window.ActualWidth > 0 && ov.Window.ActualHeight > 0
            ? ov.Window.ActualWidth / ov.Window.ActualHeight
            : 16.0 / 9.0;

        int cols = 1, rows = 1;
        if (leadAspect > 0)
        {
            if (leadAspect < screenAspect * 0.95)
                cols = Math.Min(MaxTiles, Math.Max(1, (int)Math.Ceiling(screenAspect / leadAspect)));
            else if (leadAspect > screenAspect * 1.05)
                rows = Math.Min(MaxTiles, Math.Max(1, (int)Math.Ceiling(leadAspect / screenAspect)));
        }

        // Single-tile path (aspects match): just show the lead with Uniform fit.
        if (cols == 1 && rows == 1)
        {
            var (leadIsGifSingle, leadIsWebpSingle) = ClassifyAnimation(leadPath);
            ov.Host.Children.Add(BuildTile(leadPath, leadIsGifSingle, leadIsWebpSingle, null, Stretch.Uniform,
                TileDecodeDim(ov, 1, 1)));
            return;
        }

        // Pick (cols*rows - 1) extra cells from the lead's aspect bucket.
        // Sample without replacement when the bucket is large enough; allow
        // repeats only as a last resort so a tiny pool still fills the grid.
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

        var tileGrid = new UniformGrid { Columns = cols, Rows = rows };
        var mixTileDim = TileDecodeDim(ov, cols, rows);
        foreach (var p in paths)
        {
            var (pIsGif, pIsAnimatedWebp) = ClassifyAnimation(p);
            tileGrid.Children.Add(BuildTile(p, pIsGif, pIsAnimatedWebp, null, Stretch.UniformToFill, mixTileDim));
        }
        ov.Host.Children.Add(tileGrid);
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// Which animated pipeline (if either) a pool asset needs. XamlAnimatedGif is
    /// GIF-only, so animated .webp used to fall through to the still path and play
    /// frozen on the first frame (reported 2026-07-24) — those go to AnimatedWebp's
    /// Skia decoder instead. Header probe only; still webps stay on the still path.
    /// </summary>
    private static (bool IsGif, bool IsAnimatedWebp) ClassifyAnimation(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)) return (true, false);
        if (ext.Equals(".webp", StringComparison.OrdinalIgnoreCase) && AnimatedWebp.IsAnimated(path))
            return (false, true);
        return (false, false);
    }

    /// <summary>
    /// Decode size for an animated-webp tile: the overlay's own cell size, so a
    /// full-screen single tile decodes near display res and a 3-up grid decodes a
    /// third of that. AnimatedWebp clamps to [64, 1280] and keeps every kept frame
    /// resident, so this is what bounds the frame-buffer cost.
    /// </summary>
    private static int TileDecodeDim(OverlayInstance ov, int cols, int rows)
    {
        var w = ov.Window.ActualWidth > 0 ? ov.Window.ActualWidth : 1920;
        var h = ov.Window.ActualHeight > 0 ? ov.Window.ActualHeight : 1080;
        var cell = Math.Max(w / Math.Max(1, cols), h / Math.Max(1, rows));
        return (int)Math.Clamp(cell, 64, 1280);
    }

    private static Image BuildTile(string path, bool isGif, bool isAnimatedWebp, BitmapImage? sharedBmp,
        Stretch stretch, int decodeDim)
    {
        var img = new Image
        {
            Stretch = stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        if (isGif)
        {
            AnimationBehavior.SetRepeatBehavior(img, RepeatBehavior.Forever);
            AnimationBehavior.SetSourceUri(img, new Uri(path));
        }
        else if (isAnimatedWebp)
        {
            // Decodes off-thread and loops via a keyframe animation on Image.Source.
            // TeardownHostChildren's Detach is load-bearing: the Forever clock pins
            // the Image until the animation is cleared.
            AnimatedWebp.AttachAnimation(img, path, decodeDim);
        }
        else if (sharedBmp != null)
        {
            img.Source = sharedBmp;
        }
        else
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                img.Source = bmp;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BlinkTrainer: tile bitmap load failed for {Path}", path);
            }
        }
        return img;
    }

    private static double MeasureImageAspect(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            if (decoder.Frames.Count > 0)
            {
                var f = decoder.Frames[0];
                if (f.PixelHeight > 0)
                    return (double)f.PixelWidth / f.PixelHeight;
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug(ex, "BlinkTrainer: could not measure aspect for {Path}", path);
        }

        // WIC fallback: webp decoding depends on the OS "WebP Image Extensions"
        // codec being present, and without it the decoder above throws — which
        // would bucket every webp as 1:1 square and tile it wrong. SKCodec reads
        // the dimensions with no OS codec dependency.
        try
        {
            using var codec = SkiaSharp.SKCodec.Create(path);
            if (codec != null && codec.Info.Height > 0)
                return (double)codec.Info.Width / codec.Info.Height;
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
            // Drop into the right bucket so subsequent picks find it.
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

    /// <summary>
    /// Returns a list of image paths whose aspect ratio is compatible with the
    /// requested bucket. Falls back to the full image pool if the bucket isn't
    /// populated yet (cache still warming) or contains too few images to
    /// usefully fill a tile grid.
    /// </summary>
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

    private enum AspectBucket { Portrait, Square, Landscape }

    private static void TeardownHostChildren(Grid host)
    {
        foreach (var child in host.Children)
        {
            switch (child)
            {
                case MediaElement m:
                    try { m.Stop(); m.Source = null; } catch { }
                    break;
                case Image img:
                    try { AnimationBehavior.SetSourceUri(img, null!); AnimatedWebp.Detach(img); img.Source = null; } catch { }
                    break;
                case UniformGrid ug:
                    foreach (var inner in ug.Children.OfType<Image>())
                    {
                        try { AnimationBehavior.SetSourceUri(inner, null!); AnimatedWebp.Detach(inner); inner.Source = null; } catch { }
                    }
                    ug.Children.Clear();
                    break;
                case System.Windows.Shapes.Rectangle rect:
                    // Secondary-overlay video mirror — drop the VisualBrush's
                    // reference to the primary MediaElement so it can be
                    // collected when the primary tears down too.
                    try
                    {
                        if (rect.Fill is VisualBrush vb) vb.Visual = null;
                        rect.Fill = null;
                    }
                    catch { }
                    break;
            }
        }
    }

    private static OverlayInstance? CreateOverlay(System.Windows.Forms.Screen screen, double opacity)
    {
        try
        {
            // Empty host — populated per-blink in ApplyAsset.
            var host = new Grid { ClipToBounds = true };

            // Initial WPF coords are approximate — SetWindowPos in
            // SourceInitialized re-positions in physical pixels. Same approach
            // as OverlayService spiral windows.
            var bounds = screen.Bounds;
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Opacity = Math.Clamp(opacity, 0.01, 1.0),
                Content = host,
                Title = "BlinkTrainerOverlay",
            };

            var targetScreen = screen;
            // Owns the periodic z-order re-assert; assigned to the returned
            // OverlayInstance below and torn down in Cleanup. Only started once
            // the hwnd is valid (in SourceInitialized).
            DispatcherTimer? zOrderTimer = null;
            window.SourceInitialized += (_, _) =>
            {
                try
                {
                    var hwnd = new WindowInteropHelper(window).Handle;
                    var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE,
                        ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                    SetWindowPos(hwnd, HWND_TOPMOST,
                        targetScreen.Bounds.Left, targetScreen.Bounds.Top,
                        targetScreen.Bounds.Width, targetScreen.Bounds.Height,
                        SWP_NOACTIVATE | SWP_SHOWWINDOW);

                    // Re-assert HWND_TOPMOST on a low-frequency timer so
                    // mandatory-video windows (which re-assert on their own
                    // timer) can't permanently occlude this overlay (bug #630).
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    timer.Tick += (_, _) =>
                    {
                        try
                        {
                            if (!IsWindow(hwnd)) return;
                            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                        }
                        catch { }
                    };
                    zOrderTimer = timer;
                    timer.Start();
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "BlinkTrainer: SourceInitialized positioning failed");
                }
            };

            window.Show();
            // SourceInitialized fires synchronously during Show() on this
            // thread, so zOrderTimer is assigned by the time we get here.
            return new OverlayInstance(window, host) { ZOrderTimer = zOrderTimer };
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "BlinkTrainer: failed to create overlay window");
            return null;
        }
    }

    public void Dispose() => Stop();

    private sealed record OverlayInstance(Window Window, Grid Host)
    {
        // Low-frequency z-order re-assert timer. Mandatory video windows
        // (VideoService) re-assert HWND_TOPMOST on their own timer and would
        // otherwise permanently sit above this overlay after our one-shot
        // SourceInitialized assert (bug #630). Started in SourceInitialized
        // once the hwnd is valid; stopped/released in Cleanup.
        public DispatcherTimer? ZOrderTimer { get; set; }
    }

    /// <summary>Cap on how many tiles we'll lay out (keeps GIF/CPU cost sane on extreme aspects).</summary>
    private const int MaxTiles = 6;

    // ─── Win32 interop ───────────────────────────────────────────────────────
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);
}
