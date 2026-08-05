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
            // Draw the clips from the SAME enabled pool the flashes use (disk + content packs,
            // honoring the asset manager). Pull a batch sized to the cascade (with a margin) — the
            // spawner samples it with replacement over the ~6s window. Fetched OFF the UI thread
            // because pack images decrypt to temp on demand, and a burst of that on the render
            // thread is exactly what froze earlier cascades.
            int batch = (int)Math.Clamp(Math.Ceiling(spawnRatePerSec * Math.Max(1.0, durationSec)) + 6, 8, 24);
            var flash = App.Flash;
            System.Threading.Tasks.Task.Run(() =>
            {
                List<string> files;
                try
                {
                    // #762/#798/#619: the raw-folder fallback is for a MISSING flash service only. Keying it
                    // off an empty result meant "user unchecked every folder" looked identical to "service not
                    // up", so deselecting everything rained the raw folder — precisely the content the user
                    // just turned off. An empty enabled pool is a legitimate "rain nothing".
                    files = flash != null ? flash.GetChaosImagePaths(batch) : PickFiles();
                }
                catch { files = new List<string>(); }
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    ShowWithFiles(files, spawnRatePerSec, durationSec, gifSize, fallSpeed, opacity, startScale)));
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosGifCascadeOverlay.Show: {E}", ex.Message); }
    }

    /// <summary>UI-thread continuation of <see cref="Show"/> once the image batch has been fetched.</summary>
    private static void ShowWithFiles(List<string> files, double spawnRatePerSec, double durationSec,
                                      double gifSize, double fallSpeed, double opacity, double startScale)
    {
        try
        {
            if (files.Count == 0) { App.Logger?.Debug("GifCascade: no images in pool — nothing to rain"); return; }
            if (_active == null) { _active = new ChaosGifCascadeOverlay(); ((Window)_active).Show(); }
            else if (!_active.IsVisible) { try { ((Window)_active).Show(); } catch { } }   // idles hidden between cascades

            // Confine the rain: dashboard triggers (no chaos run) always rain across ALL monitors so the
            // user sees them wherever they are (#493); a chaos run keeps its per-config coverage (all
            // screens when DualMonitor is on, primary-only when off).
            bool chaosRun = App.Chaos?.IsRunning == true;
            bool dual = App.Settings?.Current?.DualMonitorEnabled ?? true;
            _active.SetSpawnSpread(fullSpread: !chaosRun || dual);

            ChaosWindowZ.RaiseAboveVideo(_active);   // un-hiding doesn't re-stack — kick over a playing video
            // Dashboard "cascade" trigger-bubble use (no chaos run): force the singleton topmost so a
            // stale Topmost=false from a prior Free-Desktop run can't bury the rain behind the app.
            if (!chaosRun) ChaosWindowZ.ForceTopmost(_active);
            _active.Restart(files, spawnRatePerSec, durationSec, gifSize, fallSpeed, opacity, startScale);
            App.Logger?.Information("GifCascade: raining (pool={N}, spread=[{L:F0}..{R:F0}], chaos={Chaos})",
                files.Count, _active._spawnLeft, _active._spawnLeft + _active._spawnWidth, chaosRun);
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosGifCascadeOverlay.ShowWithFiles: {E}", ex.Message); }
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
    // Window-local DIP band in which clips may spawn/fall. The window always spans the full virtual
    // screen; this confines the rain (full-width for dashboard + multi-monitor; primary-only for a
    // single-screen chaos run). Set per-Show by SetSpawnSpread.
    private double _spawnLeft;
    private double _spawnWidth;
    private readonly List<Faller> _fallers = new();
    private readonly DispatcherTimer _spawn;
    private readonly DispatcherTimer _life;
    private readonly DispatcherTimer _hideGrace;
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
        // The window ALWAYS spans the full virtual screen (all monitors). A realized layered window
        // can't be resized without risking a render-thread deadlock, and this singleton is reused across
        // dashboard triggers AND chaos runs that want different coverage — so we size to the superset
        // once and confine only WHERE CLIPS SPAWN, per-Show (SetSpawnSpread). This also fixes the
        // dashboard "Gif Rain does nothing" report (#493): the old primary-only sizing rained off the
        // visible area for multi-monitor users whose active screen wasn't the primary.
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        _spawnLeft = 0; _spawnWidth = Width;

        _canvas = new Canvas { IsHitTestVisible = false };
        Content = _canvas;
        SourceInitialized += (_, _) => ApplyExStyles();

        _spawn = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _spawn.Tick += (_, _) => SpawnOne();

        _life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _life.Tick += (_, _) => { _life.Stop(); _spawning = false; _spawn.Stop(); };  // stop spawning; let in-flight fall out

        // Hiding right when the last clip landed meant the NEXT cascade re-Show()ed a
        // full-virtual-screen layered window (DWM re-composition hitch at trigger time). The
        // window idles visible-but-empty (no redraws) through this grace before hiding.
        _hideGrace = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _hideGrace.Tick += (_, _) =>
        {
            _hideGrace.Stop();
            if (!_spawning && _fallers.Count == 0) { try { Hide(); } catch { } }
        };
    }

    /// <summary>Set the window-local X band clips spawn/fall in. Full = the whole virtual screen (all
    /// monitors); otherwise the primary monitor only. Window-local X = global DIP − window.Left, and the
    /// window's Left is VirtualScreenLeft, so the primary's left edge is (0 − VirtualScreenLeft).</summary>
    private void SetSpawnSpread(bool fullSpread)
    {
        if (fullSpread) { _spawnLeft = 0; _spawnWidth = Width; return; }
        _spawnLeft = 0 - SystemParameters.VirtualScreenLeft;
        _spawnWidth = SystemParameters.PrimaryScreenWidth;
    }

    /// <summary>(Re)start a cascade in the existing window — any in-flight clips are replaced.</summary>
    private void Restart(List<string> files, double spawnRatePerSec, double durationSec,
                         double gifSize, double fallSpeed, double opacity, double startScale)
    {
        _hideGrace.Stop();
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
            // A gif/animated-webp only animates while the animated budget has room and it isn't
            // huge; otherwise it falls as a still (BitmapImage decodes the first frame).
            bool animate = false;
            bool isGif = path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
            bool isAnimatedWebp = !isGif
                && path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                && Services.AnimatedWebp.IsAnimated(path);
            if ((isGif || isAnimatedWebp) && _animatedAlive < MAX_ANIMATED)
            {
                long len = 0;
                try { len = new FileInfo(path).Length; } catch { }
                animate = len > 0 && len <= ANIMATED_MAX_BYTES;
            }
            if (animate)
            {
                _animatedAlive++;
                if (isGif)
                {
                    AnimationBehavior.SetRepeatBehavior(img, System.Windows.Media.Animation.RepeatBehavior.Forever);
                    AnimationBehavior.SetAutoStart(img, true);
                    AnimationBehavior.SetSourceUri(img, new Uri(path, UriKind.Absolute));
                }
                else
                {
                    // XamlAnimatedGif is GIF-only — webp loops via a pre-decoded (off-thread,
                    // display-size) frame animation instead. Detached with the faller.
                    Services.AnimatedWebp.AttachAnimation(img, path, (int)_gifSize);
                }
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
            double centerX = _spawnLeft + _gifSize / 2 + _rng.NextDouble() * Math.Max(1, _spawnWidth - _gifSize);
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
                    try { AnimationBehavior.SetSourceUri(f.Img, null); Services.AnimatedWebp.Detach(f.Img); f.Img.Source = null; } catch { }
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
                // Cascade over: keep the (now empty, non-redrawing) window up for the grace so a
                // follow-up cascade doesn't pay a full-virtual-screen Show(); hide after that.
                _hideGrace.Stop();
                _hideGrace.Start();
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
            try { AnimationBehavior.SetSourceUri(f.Img, null); Services.AnimatedWebp.Detach(f.Img); f.Img.Source = null; } catch { }
        }
        _fallers.Clear();
        _animatedAlive = 0;
        try { _canvas.Children.Clear(); } catch { }
    }

    private void CloseNow()
    {
        try { _hideGrace.Stop(); } catch { }
        StopAndClear();
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    // TTL-cached shared listing — re-walking the whole images folder per cascade was UI-thread I/O.
    private static List<string> PickFiles() => ChaosImagePool.GetFiles();

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
