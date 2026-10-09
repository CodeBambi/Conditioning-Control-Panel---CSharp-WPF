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
    private IDisposable? _visWatch;
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
            if (!_hooked) { AmbientFxCanvas.Env.MotionGateChanged += Refresh; _hooked = true; }
            Refresh();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _visWatch?.Dispose();
            _visWatch = null;
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

    private bool OnScreen => this.IsAttachedToVisualTree() && IsEffectivelyVisible;

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
        _renderer?.Dispose();
        _renderer = null;
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
            _surface.Redraw();
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void OnPaint(object? sender, FxPaintEventArgs e)
    {
        var r = _renderer;
        if (r == null) return;
        // A square dial centred in whatever cell it is given (WPF Stretch=Uniform).
        int side = Math.Min(e.Info.Width, e.Info.Height);
        if (side <= 0) return;
        var canvas = e.Canvas;
        canvas.Save();
        canvas.Translate((e.Info.Width - side) / 2f, (e.Info.Height - side) / 2f);
        try
        {
            if (_clock.IsEnabled) r.Draw(canvas, side, side, _phase, HomeDashboardRules.LogoDrive(_energy));
            else r.DrawStill(canvas, side, side);
        }
        finally { canvas.Restore(); }
    }
}
