using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using XamlAnimatedGif;

namespace ConditioningControlPanel;

/// <summary>
/// Full-screen, click-through overlay for the Chaos "GifCascade" payload: images/gifs spawn at the top
/// of the screen on a timer and fall/cascade downward, then despawn off the bottom. Sources images from
/// the SAME pool the flash/braindrain payloads draw on (<c>EffectiveAssetsPath/images</c>). Silent no-op
/// if the pool is empty. One window, KEPT ALIVE between cascades and only closed at run teardown
/// (<see cref="CloseActive"/>): creating/closing a layered window mid-run can wedge the shared WPF
/// render thread (Application Hang 1002 — see ChaosEffectBannerOverlay). A new Show() restarts the
/// cascade in the existing window.
/// </summary>
public sealed class ChaosGifCascadeOverlay : Window
{
    private static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp" };

    /// <summary>
    /// Hard ceiling on clips alive at once. Animated GIFs are decoded frame-by-frame at full
    /// native resolution by XamlAnimatedGif, so a pile-up of large gifs can exhaust memory and
    /// hard-crash the process (no managed exception). This cap makes that impossible regardless
    /// of spawn rate / fall speed.
    /// </summary>
    private const int MAX_CONCURRENT = 14;

    /// <summary>
    /// Budget on clips that actually ANIMATE. XamlAnimatedGif decodes every frame at native
    /// resolution on the UI thread — the 2026-06-10 cascade retune (10 clips / 6s) tripled
    /// concurrency and a pool of heavy gifs froze the UI for 15s+ (AppHangB1, watchdog log).
    /// Clips beyond this budget (or over the byte cap) fall as display-size STILLS instead —
    /// same look in motion, none of the decode cost.
    /// </summary>
    private const int MAX_ANIMATED = 3;
    private const long ANIMATED_MAX_BYTES = 3_000_000;

    private static ChaosGifCascadeOverlay? _active;
    private static readonly Random _rng = new();

    /// <summary>Spawn a falling cascade of flash-pool images. All knobs come from the payload's named consts.</summary>
    public static void Show(double spawnRatePerSec, double durationSec, double gifSize, double fallSpeed, double opacity, double startScale = 1.0)
    {
        try
        {
            var files = PickFiles();
            if (files.Count == 0) return;
            if (_active == null) { _active = new ChaosGifCascadeOverlay(); ((Window)_active).Show(); }
            else if (!_active.IsVisible) { try { ((Window)_active).Show(); } catch { } }   // idles hidden between cascades
            ChaosWindowZ.RaiseAboveVideo(_active);   // un-hiding doesn't re-stack — kick over a playing video
            // Dashboard "cascade" trigger-bubble use (no chaos run): force the singleton topmost so a
            // stale Topmost=false from a prior Free-Desktop run can't bury the rain behind the app.
            if (App.Chaos?.IsRunning != true) ChaosWindowZ.ForceTopmost(_active);
            _active.Restart(files, spawnRatePerSec, durationSec, gifSize, fallSpeed, opacity, startScale);
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosGifCascadeOverlay.Show: {E}", ex.Message); }
    }

    /// <summary>Re-stack the live window above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
    public static void RaiseActive() => ChaosWindowZ.RaiseTopmost(_active);

    /// <summary>Close any active cascade immediately (run teardown).</summary>
    public static void CloseActive() { try { _active?.CloseNow(); } catch { } }

    /// <summary>True while a cascade is actually in flight (spawning or clips still falling).
    /// The chaos heavy gate and VideoService both read this — a mandatory video opening over
    /// a falling cascade is the proven UI-thread killer, so REALITY gates it, not estimates.</summary>
    public static bool IsRaining
    {
        get { try { var a = _active; return a != null && (a._spawning || a._fallers.Count > 0); } catch { return false; } }
    }

    private readonly Canvas _canvas;
    private List<string> _files = new();
    private double _gifSize = 200;
    private double _fallSpeed = 4;
    private double _opacity = 1.0;
    private double _startScale = 1.0;   // <1: clips spawn small at the top and grow toward _gifSize as they slide down
    private readonly List<Faller> _fallers = new();
    private readonly DispatcherTimer _spawn;
    private readonly DispatcherTimer _life;
    private bool _spawning;
    // Motion runs off the composition clock (vsync-aligned) instead of a 16ms
    // DispatcherTimer, whose OS-quantized cadence beat against the refresh and made
    // the cascade judder. _lastRender feeds a delta-time frame scale.
    private TimeSpan _lastRender = TimeSpan.MinValue;

    private sealed class Faller
    {
        public Image Img = null!; public double Y; public double CenterX; public double Speed; public bool Animated;
        // Motion + growth ride RENDER transforms: layout-property animation (Width /
        // Canvas.Left/Top per frame) forces a full layout pass over the giant layered
        // window every frame — the 2026-06-10 mid-cascade UI freezes (Hang 1002).
        public TranslateTransform Move = null!; public ScaleTransform Grow = null!;
    }
    private int _animatedAlive;   // clips currently running the full XamlAnimatedGif decode

    private ChaosGifCascadeOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = ChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Single-monitor by default: confine to the primary screen unless multi-monitor is on,
        // so the falling cascade doesn't rain across every screen (see ChaosWindowZ.StageBounds).
        var (sl, st, sw, sh) = ChaosWindowZ.StageBounds();
        Left = sl; Top = st; Width = sw; Height = sh;

        _canvas = new Canvas { IsHitTestVisible = false };
        Content = _canvas;
        SourceInitialized += (_, _) => ApplyExStyles();

        _spawn = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _spawn.Tick += (_, _) => SpawnOne();

        _life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _life.Tick += (_, _) => { _life.Stop(); _spawning = false; _spawn.Stop(); };  // stop spawning; let in-flight fall out
    }

    /// <summary>(Re)start a cascade in the existing window — any in-flight clips are replaced.</summary>
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
        _spawn.Start(); _life.Start();
        _lastRender = TimeSpan.MinValue;
        CompositionTarget.Rendering -= OnRender; // guard against a double subscribe
        CompositionTarget.Rendering += OnRender;
    }

    private void SpawnOne()
    {
        if (!_spawning) return;
        if (_fallers.Count >= MAX_CONCURRENT) return;   // never let clips pile up into an OOM
        try
        {
            string path = _files[_rng.Next(_files.Count)];
            var img = new Image { Stretch = Stretch.Uniform, Opacity = _opacity };
            // A gif only animates while the animated budget has room and it isn't huge;
            // otherwise it falls as a still (BitmapImage on a .gif decodes the first frame).
            bool animate = false;
            if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) && _animatedAlive < MAX_ANIMATED)
            {
                long len = 0;
                try { len = new FileInfo(path).Length; } catch { }
                animate = len > 0 && len <= ANIMATED_MAX_BYTES;
            }
            if (animate)
            {
                _animatedAlive++;
                AnimationBehavior.SetRepeatBehavior(img, System.Windows.Media.Animation.RepeatBehavior.Forever);
                AnimationBehavior.SetAutoStart(img, true);
                AnimationBehavior.SetSourceUri(img, new Uri(path, UriKind.Absolute));
            }
            else
            {
                // Decode OFF the UI thread (a big still parsed synchronously at spawn was part of
                // the mid-cascade freezes); frozen bitmaps cross threads safely. The clip falls
                // empty for the few frames the decode takes — invisible in the rain.
                int decodeWidth = (int)_gifSize;
                string file = path;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = decodeWidth;   // decode at display size — cheap
                        bmp.UriSource = new Uri(file, UriKind.Absolute);
                        bmp.EndInit();
                        if (bmp.CanFreeze) bmp.Freeze();
                        Application.Current?.Dispatcher.BeginInvoke(() => { try { img.Source = bmp; } catch { } });
                    }
                    catch (Exception ex) { App.Logger?.Debug("GifCascade decode: {E}", ex.Message); }
                });
            }

            // Fixed layout (Width + Canvas slot set ONCE); per-frame motion/growth are pure
            // render transforms, so no layout pass ever runs during the fall.
            double centerX = _gifSize / 2 + _rng.NextDouble() * Math.Max(1, Width - _gifSize);
            double y = -_gifSize;
            var move = new TranslateTransform(0, y);
            var grow = new ScaleTransform(ScaleAt(y), ScaleAt(y));
            var tg = new TransformGroup();
            tg.Children.Add(grow);
            tg.Children.Add(move);
            img.Width = _gifSize;
            img.RenderTransformOrigin = new Point(0.5, 0.5);
            img.RenderTransform = tg;
            Canvas.SetLeft(img, centerX - _gifSize / 2);
            Canvas.SetTop(img, 0);
            _canvas.Children.Add(img);
            _fallers.Add(new Faller
            {
                Img = img, CenterX = centerX, Y = y,
                Speed = _fallSpeed * (0.7 + _rng.NextDouble() * 0.6),
                Animated = animate, Move = move, Grow = grow,
            });
        }
        catch (Exception ex) { App.Logger?.Debug("GifCascade spawn: {E}", ex.Message); }
    }

    private void OnRender(object? sender, EventArgs e)
    {
        try
        {
            // Vsync-aligned delta time, expressed as frames-worth of motion at the
            // old 16ms cadence so fall speeds keep their tuned feel.
            double frameScale = 1.0;
            if (e is RenderingEventArgs r)
            {
                if (_lastRender == TimeSpan.MinValue) { _lastRender = r.RenderingTime; return; }
                double dt = (r.RenderingTime - _lastRender).TotalSeconds;
                _lastRender = r.RenderingTime;
                if (dt <= 0) return;
                if (dt > 0.1) dt = 0.1;
                frameScale = dt / 0.016;
            }

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
                    try { AnimationBehavior.SetSourceUri(f.Img, null); f.Img.Source = null; } catch { }
                    if (f.Animated) _animatedAlive = Math.Max(0, _animatedAlive - 1);
                    _canvas.Children.Remove(f.Img);
                    _fallers.RemoveAt(i);
                }
            }
            // Cascade fully drained after the spawn window closed → idle the timers. The (now
            // empty, fully transparent) window stays alive until run teardown — mid-run Close()
            // of a layered window is the render-thread deadlock trigger.
            if (!_spawning && _fallers.Count == 0)
            {
                GoIdle();
                // Cascade over: hide the window (kept alive for the next payload) — an idle
                // visible full-virtual-screen layered surface taxes DWM composition every frame.
                try { Hide(); } catch { }
            }
        }
        catch (Exception ex) { App.Logger?.Debug("GifCascade step: {E}", ex.Message); }
    }

    /// <summary>Grow factor for a clip at vertical position <paramref name="y"/>: starts at
    /// <see cref="_startScale"/> up top and eases to full by ~75% of the way down.</summary>
    private double ScaleAt(double y)
    {
        if (_startScale >= 1.0) return 1.0;
        double p = Math.Clamp(y / Math.Max(1.0, Height * 0.75), 0, 1);
        return _startScale + (1.0 - _startScale) * p;
    }

    private void GoIdle()
    {
        try { _spawn.Stop(); } catch { }
        try { CompositionTarget.Rendering -= OnRender; } catch { }
        try { _life.Stop(); } catch { }
    }

    private void StopAndClear()
    {
        GoIdle();
        foreach (var f in _fallers)
        {
            try { AnimationBehavior.SetSourceUri(f.Img, null); f.Img.Source = null; } catch { }
        }
        _fallers.Clear();
        _animatedAlive = 0;
        try { _canvas.Children.Clear(); } catch { }
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
            var dir = Path.Combine(App.EffectiveAssetsPath ?? "", "images");
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
        }
        catch { return new List<string>(); }
    }

    private void ApplyExStyles()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
