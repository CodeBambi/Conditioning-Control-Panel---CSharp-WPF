// PORTED from WPF 7.1.5 ConditioningControlPanel/Controls/AnimatedLogoImage.cs (parity lane E3).
//
// The centre of Home: the CCP logo dial (Resources/branding/ccp-logo.jpg) drawn by
// DashboardLogoRenderer, alive at rest and three times faster under the pointer. WPF ran its own
// 30 fps DispatcherTimer into a WriteableBitmap; here the frame-locked FrameClock drives an
// FxSurface (one Skia surface, repainted in place). The numbers (idle floor, phase rate, the
// energy ease) are Core HomeDashboardRules. Motion Off or a refused ambient loop shows the still
// artwork; a missing or broken artwork falls back to the bundled wordmark (Resources/logo2.png),
// so the cell is never a hole.

using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Controls.Home;

public sealed class AnimatedLogoDial : Grid
{
    internal const string ArtworkUri = "avares://CCP.Avalonia/Resources/branding/ccp-logo.jpg";
    internal const string FallbackUri = "avares://CCP.Avalonia/Resources/logo2.png";

    private readonly FxSurface _surface = new();
    private readonly Image _still = new() { Stretch = Stretch.Uniform, IsVisible = false };
    private readonly FrameClock _clock;
    private readonly Stopwatch _watch = new();
    private DashboardLogoRenderer? _renderer;
    private IDisposable? _visWatch, _stateWatch;
    private double _last, _phase, _energy;
    private bool _failed, _hooked;

    public AnimatedLogoDial()
    {
        ClipToBounds = false;
        RenderOptions.SetBitmapInterpolationMode(_still, BitmapInterpolationMode.HighQuality);
        Children.Add(_still);
        Children.Add(_surface);
        _surface.PaintSurface += OnPaint;
        _clock = new FrameClock(this) { Interval = TimeSpan.FromSeconds(1.0 / HomeDashboardRules.LogoFps) };
        _clock.Tick += (_, _) => Tick();
        _surface.SizeChanged += (_, _) => _surface.Redraw();

        AttachedToVisualTree += (_, _) =>
        {
            _visWatch?.Dispose();
            _visWatch = EffectiveVisibility.Watch(this, Refresh);
            // A minimised window parks the dial (perf pass 2026-10-09); restoring restarts it.
            _stateWatch?.Dispose();
            _stateWatch = TopLevel.GetTopLevel(this) is Window w
                ? w.GetObservable(Window.WindowStateProperty).Subscribe(new StatePing(this))
                : null;
            if (!_hooked) { AmbientFxCanvas.Env.MotionGateChanged += Refresh; _hooked = true; }
            Refresh();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _visWatch?.Dispose();
            _visWatch = null;
            _stateWatch?.Dispose();
            _stateWatch = null;
            if (_hooked) { AmbientFxCanvas.Env.MotionGateChanged -= Refresh; _hooked = false; }
            Stop();
        };
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) Refresh(); };
        // Safety net for a missed visibility edge: the ancestor watch is captured at attach, and
        // a page made visible by a host that swaps its own parent never reached it, so the dial
        // sat on its still frame. Any layout pass that finds it on screen, parked and allowed to
        // move starts it. Event-driven, never a loop; a running clock returns at the first test.
        LayoutUpdated += (_, _) =>
        {
            if (_clock.IsEnabled || _failed || !OnScreen || !AmbientFxCanvas.Env.AllowAmbientLoops) return;
            Refresh();
        };
    }

    /// <summary>True while the clock runs (tests).</summary>
    internal bool IsAnimating => _clock.IsEnabled;

    /// <summary>True when the dial art loaded; false = the wordmark fallback is showing (tests).</summary>
    internal bool HasArtwork => _renderer != null && !_failed;

    /// <summary>The surface (tests read its paint count).</summary>
    internal FxSurface Surface => _surface;

    private bool OnScreen => this.IsAttachedToVisualTree() && IsEffectivelyVisible
        && TopLevel.GetTopLevel(this) is not Window { WindowState: WindowState.Minimized };

    private sealed class StatePing(AnimatedLogoDial owner) : IObserver<WindowState>
    {
        public void OnNext(WindowState value) => owner.Refresh();
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    private void Refresh()
    {
        try
        {
            EnsureRenderer();
            bool animate = OnScreen && !_failed && _renderer != null && AmbientFxCanvas.Env.AllowAmbientLoops;
            if (animate)
            {
                if (_clock.IsEnabled) return;
                _last = 0;
                _watch.Restart();
                _clock.Start();
            }
            else
            {
                Stop();
                if (OnScreen) _surface.Redraw();   // the still frame
            }
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void Stop()
    {
        _clock.Stop();
        _watch.Reset();
        _energy = 0;
    }

    private void EnsureRenderer()
    {
        if (_renderer != null || _failed) return;
        using var stream = OpenArtwork();
        if (stream == null) { Fail(null); return; }
        _renderer = new DashboardLogoRenderer(stream);
        _still.IsVisible = false;
        _surface.IsVisible = true;
    }

    /// <summary>The packed resource, else the same file beside the executable.</summary>
    private static Stream? OpenArtwork()
    {
        try { return AssetLoader.Open(new Uri(ArtworkUri)); }
        catch { /* not packed in this build: try the disk */ }
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "branding", "ccp-logo.jpg");
            return File.Exists(path) ? File.OpenRead(path) : null;
        }
        catch { return null; }
    }

    private void Fail(Exception? ex)
    {
        // Warning, not Debug: a still wordmark in the centre of Home is a visible regression and
        // the only trace of why must survive the default log level.
        Log.Warning("Logo dial fallback to the still wordmark: {E}", ex?.Message ?? "artwork not found");
        _failed = true;
        Stop();
        lock (_drawGate)
        {
            _renderer?.Dispose();
            _renderer = null;
        }
        _surface.IsVisible = false;
        try { _still.Source ??= new Bitmap(AssetLoader.Open(new Uri(FallbackUri))); }
        catch (Exception e2) { Log.Debug("Logo wordmark fallback failed: {E}", e2.Message); }
        _still.IsVisible = true;
    }

    private void Tick()
    {
        try
        {
            double now = _watch.Elapsed.TotalSeconds, dt = Math.Clamp(now - _last, 0, .1);
            _last = now;
            _energy = HomeDashboardRules.LogoEnergyStep(_energy, IsPointerOver ? 1 : 0, dt);
            _phase = (_phase + dt * HomeDashboardRules.LogoPhaseRate(_energy)) % Math.Tau;
            RenderAhead(_phase, HomeDashboardRules.LogoDrive(_energy));
            _surface.Redraw();
        }
        catch (Exception ex) { Fail(ex); }
    }

    // ---- frames off the UI thread (owner, 2026-10-09: "still a bit of lag" on the dashboard) ----
    // The dial is ~8 ms of CPU raster per frame; on the UI thread that stole a quarter of every
    // second from input and the other loops. A worker now draws each frame into a back bitmap and
    // the paint only blits the newest finished one (one frame behind, at 30 fps). The renderer is
    // only ever touched under _drawGate; a busy worker drops the tick rather than queueing.

    private readonly object _drawGate = new(), _frameGate = new();
    private SKBitmap? _front, _back;
    private int _side;
    private bool _working;

    private void RenderAhead(double phase, double drive)
    {
        int side = _side;
        if (side <= 0 || _working || _renderer == null) return;
        _working = true;
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                lock (_drawGate)
                {
                    var r = _renderer;
                    if (r == null) return;
                    if (_back == null || _back.Width != side)
                    {
                        _back?.Dispose();
                        _back = new SKBitmap(side, side, SKColorType.Bgra8888, SKAlphaType.Premul);
                    }
                    using (var c = new SKCanvas(_back))
                    {
                        c.Clear(SKColors.Transparent);
                        r.Draw(c, side, side, phase, drive);
                    }
                    lock (_frameGate) (_front, _back) = (_back, _front);
                }
            }
            catch (Exception ex) { Log.Debug("Logo dial worker frame failed: {E}", ex.Message); }
            finally { _working = false; }
        });
    }

    private void OnPaint(object? sender, FxPaintEventArgs e)
    {
        var r = _renderer;
        if (r == null) return;
        // A square dial centred in whatever cell it is given (WPF Stretch=Uniform).
        int side = Math.Min(e.Info.Width, e.Info.Height);
        if (side <= 0) return;
        _side = side;
        var canvas = e.Canvas;
        canvas.Save();
        canvas.Translate((e.Info.Width - side) / 2f, (e.Info.Height - side) / 2f);
        try
        {
            if (_clock.IsEnabled)
            {
                lock (_frameGate)
                {
                    if (_front != null && _front.Width == side) { canvas.DrawBitmap(_front, 0, 0); return; }
                }
                // No finished frame at this size yet (first tick, a resize): draw this one inline.
                lock (_drawGate) r.Draw(canvas, side, side, _phase, HomeDashboardRules.LogoDrive(_energy));
            }
            else lock (_drawGate) r.DrawStill(canvas, side, side);
        }
        finally { canvas.Restore(); }
    }
}
