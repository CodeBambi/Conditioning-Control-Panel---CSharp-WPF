using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Services.Chaos;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosGifCascadeOverlay: full-screen click-through falling image cascade.
/// Animated GIFs are decoded via the cross-platform SkiaSharp helper <see cref="AvaloniaAnimatedGif"/>.
/// </summary>
public partial class ChaosGifCascadeOverlay : Window
{
    private readonly ILogger<ChaosGifCascadeOverlay> _logger;


    private static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp" };

    private const int MAX_CONCURRENT = 14;

    /// <summary>
    /// Budget on clips that actually ANIMATE (WPF parity: ChaosGifCascadeOverlay.cs MAX_ANIMATED).
    /// Animated GIFs decode every frame on the UI thread; a pool of heavy gifs froze the UI 15s+
    /// (AppHangB1). Clips beyond this budget — or over <see cref="ANIMATED_MAX_BYTES"/> — fall as
    /// display-size STILLS instead: same look in motion, none of the per-frame decode cost.
    /// </summary>
    private const int MAX_ANIMATED = 3;

    /// <summary>Per-file byte ceiling for a clip allowed to animate. WPF parity.</summary>
    private const long ANIMATED_MAX_BYTES = 3_000_000;

    /// <summary>Bounded cache of decoded, display-size still bitmaps keyed by <c>path|width</c>.
    /// Stops re-decoding the same file on every sub-second spawn during one cascade. The window is a
    /// kept-alive singleton, so the cache owns its bitmaps and disposes them wholesale in
    /// <see cref="StopAndClear"/> (once every faller's <see cref="Image.Source"/> has been detached).
    /// Fallers only reference cache bitmaps — they never dispose them (that would use-after-dispose a
    /// shared <see cref="Image.Source"/>).</summary>
    private const int STILL_CACHE_MAX = 32;

    private static ChaosGifCascadeOverlay? _active;
    private static readonly Random _rng = new();

    private readonly Canvas _canvas;
    private List<string> _files = new();
    private double _gifSize = 200;
    private double _fallSpeed = 4;
    private double _opacity = 1.0;
    private double _startScale = 1.0;
    private readonly List<Faller> _fallers = new();
    private readonly DispatcherTimer _spawn = new();
    private readonly DispatcherTimer _life = new();
    private readonly DispatcherTimer _step = new();
    private bool _spawning;
    private DateTime _lastStep = DateTime.MinValue;

    /// <summary>Clips currently running the full <see cref="AvaloniaAnimatedGif"/> decode. UI thread only.</summary>
    private int _animatedAlive;

    /// <summary>Decoded still cache (see <see cref="STILL_CACHE_MAX"/>). Guards its own contents with a lock
    /// because decodes run on background threads.</summary>
    private readonly Dictionary<string, Bitmap> _stillCache = new();

    private sealed class Faller
    {
        public Image Img = null!;
        public AvaloniaAnimatedGif? Anim;
        /// <summary>True while this clip holds an <see cref="_animatedAlive"/> reservation.</summary>
        public bool Animated;
        public double Y;
        public double CenterX;
        public double Speed;
        public TranslateTransform Move = null!;
        public ScaleTransform Grow = null!;
    }

    public ChaosGifCascadeOverlay()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<ChaosGifCascadeOverlay>>();
WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = AvaloniaChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var (sl, st, sw, sh) = AvaloniaChaosWindowZ.StageBounds(forcePrimary: true);
        Position = new PixelPoint((int)sl, (int)st);
        Width = sw;
        Height = sh;

        _canvas = new Canvas { IsHitTestVisible = false };
        Content = _canvas;

        _spawn.Interval = TimeSpan.FromMilliseconds(500);
        _spawn.Tick += (_, _) => SpawnOne();

        _life.Interval = TimeSpan.FromSeconds(8);
        _life.Tick += (_, _) => { _life.Stop(); _spawning = false; _spawn.Stop(); };

        _step.Interval = TimeSpan.FromMilliseconds(16);
        _step.Tick += StepTick;

        Opened += (_, _) => ApplyExStyles();
    }

    public static void Show(double spawnRatePerSec, double durationSec, double gifSize, double fallSpeed, double opacity, double startScale = 1.0)
    {
        var logger = App.Services.GetRequiredService<ILogger<Faller>>();
        try
        {
            var files = PickFiles();
            if (files.Count == 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosGifCascadeOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); }
                    else if (!_active.IsVisible) { try { ((global::Avalonia.Controls.Window)_active).Show(); } catch { } }
                    AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
                    _active.Restart(files, spawnRatePerSec, durationSec, gifSize, fallSpeed, opacity, startScale);
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<Faller>>().LogInformation("ChaosGifCascadeOverlay.Show: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { App.Services?.GetRequiredService<ILogger<Faller>>().LogInformation("ChaosGifCascadeOverlay.Show: {E}", ex.Message); }
    }

    public static void RaiseActive() => AvaloniaChaosWindowZ.RaiseTopmost(_active);

    public static void CloseActive() { try { _active?.CloseNow(); } catch { } }

    public static bool IsRaining
    {
        get { try { var a = _active; return a != null && (a._spawning || a._fallers.Count > 0); } catch { return false; } }
    }

    private void Restart(List<string> files, double spawnRatePerSec, double durationSec,
                         double gifSize, double fallSpeed, double opacity, double startScale)
    {
        StopAndClear();
        _files = files;
        _gifSize = Math.Clamp(gifSize, 40, 600);
        _fallSpeed = Math.Clamp(fallSpeed, 0.5, 30);
        _opacity = Math.Clamp(opacity, 0.05, 1.0);
        _startScale = Math.Clamp(startScale, 0.1, 1.0);
        _spawn.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(0.05, spawnRatePerSec));
        _life.Interval = TimeSpan.FromSeconds(Math.Max(1.0, durationSec));
        _spawning = true;
        SpawnOne();
        _spawn.Start();
        _life.Start();
        _lastStep = DateTime.UtcNow;
        _step.Start();
    }

    private void SpawnOne()
    {
        if (!_spawning) return;
        if (_fallers.Count >= MAX_CONCURRENT) return;
        try
        {
            string path = _files[_rng.Next(_files.Count)];
            var img = new Image { Stretch = Stretch.Uniform, Opacity = _opacity };

            double centerX = _gifSize / 2 + _rng.NextDouble() * Math.Max(1, Width - _gifSize);
            double y = -_gifSize;
            var move = new TranslateTransform(0, y);
            var grow = new ScaleTransform(ScaleAt(y), ScaleAt(y));
            var tg = new TransformGroup();
            tg.Children.Add(grow);
            tg.Children.Add(move);
            img.Width = _gifSize;
            img.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            img.RenderTransform = tg;
            Canvas.SetLeft(img, centerX - _gifSize / 2);
            Canvas.SetTop(img, 0);

            string file = path;
            bool isGif = file.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

            // A gif only animates while the animated budget has room AND it isn't huge; otherwise it
            // falls as a display-size still. Mirrors WPF ChaosGifCascadeOverlay.SpawnOne. The size stat
            // is a single cheap call on the UI thread — keeping the counter race-free on one thread.
            bool animate = false;
            if (isGif && _animatedAlive < MAX_ANIMATED)
            {
                long len = 0;
                try { len = new FileInfo(file).Length; } catch { }
                animate = len > 0 && len <= ANIMATED_MAX_BYTES;
            }

            var faller = new Faller
            {
                Img = img,
                Animated = animate,
                CenterX = centerX,
                Y = y,
                Speed = _fallSpeed * (0.7 + _rng.NextDouble() * 0.6),
                Move = move,
                Grow = grow,
            };
            if (animate) _animatedAlive++;

            _canvas.Children.Add(img);
            _fallers.Add(faller);

            int decodeWidth = Math.Max(1, (int)_gifSize);
            Task.Run(() =>
            {
                try
                {
                    if (animate)
                    {
                        var anim = AvaloniaAnimatedGif.TryCreate(file);
                        if (anim != null)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                // The clip may have fallen out / been cleared before the decode landed.
                                if (!_fallers.Contains(faller)) { anim.Dispose(); return; }
                                try
                                {
                                    faller.Anim = anim;
                                    img.Source = anim.Source;
                                    anim.FrameRendered += (_, _) => img.InvalidateVisual();
                                    anim.Start();
                                }
                                catch { anim.Dispose(); ReleaseAnimated(faller); }
                            });
                            return;
                        }

                        // GIF decode failed — hand the animated budget back and fall through to a still.
                        Dispatcher.UIThread.Post(() => ReleaseAnimated(faller));
                    }

                    var bmp = DecodeStillCached(file, decodeWidth);
                    if (bmp != null)
                        Dispatcher.UIThread.Post(() => { try { if (_fallers.Contains(faller)) img.Source = bmp; } catch { } });
                }
                catch (Exception ex) { _logger?.LogInformation("GifCascade decode: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { _logger?.LogInformation("GifCascade spawn: {E}", ex.Message); }
    }

    private void StepTick(object? sender, EventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            double dt = _lastStep == DateTime.MinValue ? 0.016 : (now - _lastStep).TotalSeconds;
            _lastStep = now;
            if (dt <= 0) return;
            if (dt > 0.1) dt = 0.1;
            double frameScale = dt / 0.016;

            for (int i = _fallers.Count - 1; i >= 0; i--)
            {
                var f = _fallers[i];
                f.Y += f.Speed * frameScale;
                double s = ScaleAt(f.Y);
                f.Grow.ScaleX = s;
                f.Grow.ScaleY = s;
                f.Move.Y = f.Y;
                if (f.Y > Height + _gifSize)
                {
                    // Detach the source only — still bitmaps belong to _stillCache (disposed at teardown);
                    // the animated decoder is per-clip and disposed here.
                    try { f.Img.Source = null; }
                    catch { }
                    f.Anim?.Dispose();
                    f.Anim = null;
                    ReleaseAnimated(f);
                    _canvas.Children.Remove(f.Img);
                    _fallers.RemoveAt(i);
                }
            }
            if (!_spawning && _fallers.Count == 0)
            {
                GoIdle();
                try { Hide(); } catch { }
            }
        }
        catch (Exception ex) { _logger?.LogInformation("GifCascade step: {E}", ex.Message); }
    }

    private double ScaleAt(double y)
    {
        if (_startScale >= 1.0) return 1.0;
        double p = Math.Clamp(y / Math.Max(1.0, Height * 0.75), 0, 1);
        return _startScale + (1.0 - _startScale) * p;
    }

    private void GoIdle()
    {
        try { _spawn.Stop(); } catch { }
        try { _step.Stop(); } catch { }
        try { _life.Stop(); } catch { }
    }

    private void StopAndClear()
    {
        GoIdle();
        foreach (var f in _fallers)
        {
            try { f.Img.Source = null; } catch { }
            f.Anim?.Dispose();
            f.Anim = null;
            f.Animated = false;
        }
        _fallers.Clear();
        _animatedAlive = 0;
        try { _canvas.Children.Clear(); } catch { }
        // Every faller's Source is now detached, so the cached stills are unreferenced and safe to free.
        lock (_stillCache)
        {
            foreach (var b in _stillCache.Values)
            {
                try { b.Dispose(); } catch { }
            }
            _stillCache.Clear();
        }
    }

    /// <summary>Hand back a clip's animated-budget reservation exactly once. UI thread only.</summary>
    private void ReleaseAnimated(Faller f)
    {
        if (!f.Animated) return;
        f.Animated = false;
        _animatedAlive = Math.Max(0, _animatedAlive - 1);
    }

    /// <summary>Decode a still at <paramref name="width"/> (downscaled — a phone photo is ~4000px, ~100MB
    /// BGRA nobody sees at cascade size) and memoise it. Runs on a background thread. The returned bitmap
    /// is owned by <see cref="_stillCache"/>; callers must not dispose it.</summary>
    private Bitmap? DecodeStillCached(string path, int width)
    {
        string key = path + "|" + width;
        lock (_stillCache)
        {
            if (_stillCache.TryGetValue(key, out var hit)) return hit;
        }

        Bitmap bmp;
        try
        {
            using var stream = File.OpenRead(path);
            bmp = Bitmap.DecodeToWidth(stream, width);
        }
        catch (Exception ex) { _logger?.LogInformation("GifCascade still decode: {E}", ex.Message); return null; }

        lock (_stillCache)
        {
            if (_stillCache.TryGetValue(key, out var raced))
            {
                // Another thread decoded the same key first — drop ours, share theirs.
                try { bmp.Dispose(); } catch { }
                return raced;
            }
            if (_stillCache.Count >= STILL_CACHE_MAX)
            {
                // Evict one entry: drop the strong ref only (a live faller may still show it; GC reclaims
                // it once nothing references it). Bulk disposal happens in StopAndClear.
                foreach (var oldKey in _stillCache.Keys)
                {
                    _stillCache.Remove(oldKey);
                    break;
                }
            }
            _stillCache[key] = bmp;
        }
        return bmp;
    }

    private void CloseNow()
    {
        StopAndClear();
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    private static List<string> PickFiles()
    {
        try
        {
            return ChaosImagePool.GetFiles(AvaloniaChaosEnv.EffectiveAssetsPath ?? "");
        }
        catch { return new List<string>(); }
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);
}
