using System;
using System.Collections.Generic;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Shapes;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Threading;
using Point = global::Avalonia.Point;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosFieldFxOverlay: full-virtual-screen click-through canvas for
/// Size Queen ripples, Aftermath residue, Tail-Plug sparkle trail, and The Bound tether.
/// </summary>
public partial class ChaosFieldFxOverlay : Window
{
    private const int TRAIL_DOT_POOL = 90;
    private const double TRAIL_DOT_SIZE = 16;

    private static readonly Color RingColor = Color.FromRgb(0x7A, 0xE0, 0xFF);
    private static readonly Color ResidueColor = Color.FromRgb(0x9C, 0x5C, 0xFF);
    private static readonly Color TrailColor = Color.FromRgb(0xFF, 0x4D, 0xC4);
    private static readonly Color WarmTrailColor = Color.FromRgb(0xFF, 0x8A, 0x14);

    private static ChaosFieldFxOverlay? _active;

    private readonly Canvas _canvas;
    private readonly Ellipse[] _trailDots = new Ellipse[TRAIL_DOT_POOL];
    private readonly ScaleTransform[] _trailScales = new ScaleTransform[TRAIL_DOT_POOL];
    private int _trailIndex;
    private int _transientCount;
    private readonly IBrush _trailBrushCool;
    private readonly IBrush _trailBrushWarm;
    private readonly Dictionary<int, Line> _tethers = new();
    private static readonly IBrush TetherBrush = Frozen(Color.FromArgb(150, 0xFF, 0x69, 0xB4));

    public ChaosFieldFxOverlay()
    {
        InitializeComponent();

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

        var (sl, st, sw, sh) = AvaloniaChaosWindowZ.StageBoundsDip();
        Position = new PixelPoint((int)sl, (int)st);
        Width = sw;   // DIP (StageBoundsDip already divided the physical span by scaling)
        Height = sh;

        _canvas = new Canvas { IsHitTestVisible = false };
        Content = _canvas;

        _trailBrushCool = BuildTrailBrush(TrailColor);
        _trailBrushWarm = BuildTrailBrush(WarmTrailColor);
        for (int i = 0; i < TRAIL_DOT_POOL; i++)
        {
            var sc = new ScaleTransform(1, 1);
            var dot = new Ellipse
            {
                Width = TRAIL_DOT_SIZE, Height = TRAIL_DOT_SIZE,
                Fill = _trailBrushCool,
                IsHitTestVisible = false,
                Opacity = 0,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RenderTransform = sc,
            };
            _trailDots[i] = dot;
            _trailScales[i] = sc;
            _canvas.Children.Add(dot);
        }

        Opened += (_, _) => ApplyExStyles();
    }

    public static void EnsureCreated()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null)
                    {
                        _active = new ChaosFieldFxOverlay();
                        ((global::Avalonia.Controls.Window)_active).Show();
                        _active.Hide();
                    }
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosFieldFxOverlay>>().LogInformation("ChaosFieldFx.EnsureCreated: {E}", ex.Message); }
            });
        }
        catch { }
    }

    public static void Ripple(Point centerPx, double radiusPx, double lifeMs) =>
        OnUi(w => w.DrawRipple(centerPx, radiusPx, lifeMs));
    public static void SnapRipple(Point centerPx, double radiusPx, double lifeMs) =>
        OnUi(w => w.DrawSnapRipple(centerPx, radiusPx, lifeMs));
    public static void Residue(Point centerPx, double radiusPx, double lifeMs) =>
        OnUi(w => w.DrawResidue(centerPx, radiusPx, lifeMs));
    public static void TrailDot(Point centerPx, double lifeSec, bool warm = false) =>
        OnUi(w => w.DrawTrailDot(centerPx, lifeSec, warm));
    public static void SetTether(int key, Point aPx, Point bPx) =>
        OnUi(w => w.UpdateTether(key, aPx, bPx));
    public static void ClearTether(int key) =>
        OnUi(w => w.RemoveTether(key));

    public static void RaiseActive() => AvaloniaChaosWindowZ.RaiseTopmost(_active);

    public static void CloseActive()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var w = _active;
                    _active = null;
                    w?.Close();
                }
                catch { }
            });
        }
        catch { }
    }

    private static void OnUi(Action<ChaosFieldFxOverlay> act)
    {
        var logger = App.Services.GetRequiredService<ILogger<ChaosFieldFxOverlay>>();
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null)
                    {
                        _active = new ChaosFieldFxOverlay();
                        ((global::Avalonia.Controls.Window)_active).Show();
                        AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
                    }
                    else if (!_active.IsVisible)
                    {
                        ((global::Avalonia.Controls.Window)_active).Show();
                        AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
                    }
                    act(_active);
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosFieldFxOverlay>>().LogInformation("ChaosFieldFx: {E}", ex.Message); }
            });
        }
        catch { }
    }

    private static IBrush BuildTrailBrush(Color edge)
    {
        var b = new RadialGradientBrush { GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(200, 0xFF, 0xE9, 0xA0), 0.0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(120, edge.R, edge.G, edge.B), 0.55));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, edge.R, edge.G, edge.B), 1.0));
        return b;
    }

    private double Scale => RenderScaling <= 0 ? 1.0 : RenderScaling;

    /// <summary>Physical screen px → this window's local DIPs. <see cref="Window.Position"/> is
    /// physical px while the canvas is laid out in the window's DIPs (<see cref="Scale"/>), so the
    /// physical offset must be divided by the scaling.</summary>
    private Point? Local(Point px)
    {
        double s = Scale;
        return new Point((px.X - Position.X) / s, (px.Y - Position.Y) / s);
    }

    private void DrawRipple(Point centerPx, double radiusPx, double lifeMs)
    {
        if (Local(centerPx) is not Point c) return;
        double r = radiusPx / Scale;
        var st = new ScaleTransform(0.05, 0.05);
        var ring = NewRing(c, r, RingColor, 6);
        ring.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        ring.RenderTransform = st;
        Canvas.SetLeft(ring, c.X - r);
        Canvas.SetTop(ring, c.Y - r);
        _canvas.Children.Add(ring);
        _transientCount++;
        // The ring EXPANDS from a point (WPF ScaleTransform 0.05→1.0, cubic ease-out) as it fades.
        AvaloniaChaosAnim.ScaleTo(st, 0.05, 1.0, lifeMs, AvaloniaChaosAnim.EasingMode.EaseOut);
        _ = new OpacityFade(ring, 0.95, 0, lifeMs, () => RemoveTransient(ring));
    }

    private void DrawSnapRipple(Point centerPx, double radiusPx, double lifeMs)
    {
        if (Local(centerPx) is not Point c) return;
        double r = radiusPx / Scale;
        // Cyan front grows LINEARLY (matches the linear kill-front); the pink echo rides eased behind.
        AddCastRing(c, r, lifeMs, Color.FromRgb(0x7A, 0xE0, 0xFF), 6, 0.95, eased: false);
        AddCastRing(c, r * 0.82, lifeMs, Color.FromRgb(0xFF, 0x69, 0xB4), 3.5, 0.65, eased: true);
        AddRadialShards(c, r, lifeMs);
    }

    private void AddCastRing(Point c, double r, double lifeMs, Color color, double thickness, double fromOpacity, bool eased)
    {
        var st = new ScaleTransform(0.05, 0.05);
        var ring = NewRing(c, r, color, thickness);
        ring.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        ring.RenderTransform = st;
        Canvas.SetLeft(ring, c.X - r);
        Canvas.SetTop(ring, c.Y - r);
        _canvas.Children.Add(ring);
        _transientCount++;
        AvaloniaChaosAnim.ScaleTo(st, 0.05, 1.0, lifeMs,
            eased ? AvaloniaChaosAnim.EasingMode.EaseOut : AvaloniaChaosAnim.EasingMode.Linear);
        _ = new OpacityFade(ring, fromOpacity, 0, lifeMs, () => RemoveTransient(ring));
    }

    private void AddRadialShards(Point center, double radius, double lifeMs)
    {
        const int count = 10;
        var rng = new Random();
        for (int i = 0; i < count; i++)
        {
            double angle = rng.NextDouble() * Math.PI * 2;
            double inner = radius * 0.15;
            double outer = radius * (0.75 + rng.NextDouble() * 0.45);
            var start = new Point(center.X + Math.Cos(angle) * inner, center.Y + Math.Sin(angle) * inner);
            var end = new Point(center.X + Math.Cos(angle) * outer, center.Y + Math.Sin(angle) * outer);
            var line = new Line
            {
                StartPoint = start,
                EndPoint = start,
                Stroke = new SolidColorBrush(Color.FromArgb(180, 0xFF, 0xE0, 0xFF)),
                StrokeThickness = 1.2 + rng.NextDouble() * 1.4,
                IsHitTestVisible = false,
                Opacity = 0.85,
            };
            _canvas.Children.Add(line);
            _transientCount++;
            _ = new ShardExpand(line, start, end, lifeMs, () => RemoveTransient(line));
        }
    }

    private sealed class ShardExpand : IDisposable
    {
        private readonly Line _line;
        private readonly Point _from;
        private readonly Point _to;
        private readonly double _durationMs;
        private readonly double _startMs;
        private readonly Action? _onComplete;
        private readonly DispatcherTimer _timer = new();
        private bool _done;

        public ShardExpand(Line line, Point from, Point to, double durationMs, Action? onComplete = null)
        {
            _line = line;
            _from = from;
            _to = to;
            _durationMs = Math.Max(1, durationMs);
            _startMs = Environment.TickCount64;
            _onComplete = onComplete;
            _timer.Interval = TimeSpan.FromMilliseconds(16);
            _timer.Tick += Tick;
            _timer.Start();
        }

        private void Tick(object? sender, EventArgs e)
        {
            if (_done) return;
            double elapsed = Environment.TickCount64 - _startMs;
            double t = Math.Min(1, elapsed / _durationMs);
            double eased = 1 - Math.Pow(1 - t, 3);
            double x = _from.X + (_to.X - _from.X) * eased;
            double y = _from.Y + (_to.Y - _from.Y) * eased;
            _line.EndPoint = new Point(x, y);
            _line.Opacity = 0.85 * (1 - t);
            if (t >= 1)
            {
                _done = true;
                _timer.Stop();
                _onComplete?.Invoke();
            }
        }

        public void Dispose()
        {
            _done = true;
            _timer.Stop();
            _timer.Tick -= Tick;
        }
    }

    private Ellipse NewRing(Point c, double r, Color color, double thickness)
    {
        return new Ellipse
        {
            Width = r * 2, Height = r * 2,
            Stroke = new SolidColorBrush(Color.FromArgb(190, color.R, color.G, color.B)),
            StrokeThickness = thickness,
            IsHitTestVisible = false,
        };
    }

    private void DrawResidue(Point centerPx, double radiusPx, double lifeMs)
    {
        if (Local(centerPx) is not Point c) return;
        double r = radiusPx;
        var brush = new RadialGradientBrush { GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(70, 0xBF, 0xEC, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(90, ResidueColor.R, ResidueColor.G, ResidueColor.B), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, ResidueColor.R, ResidueColor.G, ResidueColor.B), 1.0));
        var zone = new Ellipse
        {
            Width = r * 2, Height = r * 2,
            Fill = brush,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(zone, c.X - r);
        Canvas.SetTop(zone, c.Y - r);
        _canvas.Children.Add(zone);
        _transientCount++;
        // A crackling flicker for its whole life, then a quick fade-away at the end (WPF keyframes).
        StartCrackleFade(zone, lifeMs, () => RemoveTransient(zone));
    }

    /// <summary>Random-opacity crackle (0.55–1.0, repicked every ~90ms), then a linear fade to 0
    /// over the final crackle window — mirrors the WPF residue keyframe animation.</summary>
    private static void StartCrackleFade(Control target, double lifeMs, Action onComplete)
    {
        var rng = new Random();
        int steps = Math.Max(2, (int)(lifeMs / 90));
        double fadeStartMs = Math.Min(Math.Max(1, lifeMs - 1), (steps - 1) * 90.0);
        double startMs = Environment.TickCount64;
        double lastFlickMs = -1000;
        double flick = 0.55 + rng.NextDouble() * 0.45;
        target.Opacity = flick;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            double elapsed = Environment.TickCount64 - startMs;
            if (elapsed >= lifeMs) { timer.Stop(); target.Opacity = 0; onComplete(); return; }
            if (elapsed < fadeStartMs)
            {
                if (elapsed - lastFlickMs >= 90) { lastFlickMs = elapsed; flick = 0.55 + rng.NextDouble() * 0.45; }
                target.Opacity = flick;
            }
            else
            {
                double f = (elapsed - fadeStartMs) / Math.Max(1, lifeMs - fadeStartMs);
                target.Opacity = flick * (1 - f);
            }
        };
        timer.Start();
    }

    private void UpdateTether(int key, Point aPx, Point bPx)
    {
        if (Local(aPx) is not Point a || Local(bPx) is not Point b) return;
        if (!_tethers.TryGetValue(key, out var line))
        {
            line = new Line
            {
                Stroke = TetherBrush,
                StrokeThickness = 3.5,
                StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { 4, 3 },
                StrokeLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
                Opacity = 0.55,
            };
            _tethers[key] = line;
            _canvas.Children.Add(line);
        }
        line.StartPoint = a;
        line.EndPoint = b;
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        line.StrokeThickness = Math.Clamp(5.0 - dist / 250.0, 1.5, 5.0);
    }

    private void RemoveTether(int key)
    {
        if (_tethers.TryGetValue(key, out var line))
        {
            try { _canvas.Children.Remove(line); } catch { }
            _tethers.Remove(key);
        }
        MaybeHide();
    }

    private void DrawTrailDot(Point centerPx, double lifeSec, bool warm = false)
    {
        if (Local(centerPx) is not Point c) return;
        var dot = _trailDots[_trailIndex];
        var sc =
_trailScales[_trailIndex];
        _trailIndex = (_trailIndex + 1) % TRAIL_DOT_POOL;
        dot.Fill = warm ? _trailBrushWarm : _trailBrushCool;
        Canvas.SetLeft(dot, c.X - TRAIL_DOT_SIZE / 2);
        Canvas.SetTop(dot, c.Y - TRAIL_DOT_SIZE / 2);
        dot.Opacity = 0.65;
        sc.ScaleX = 1.25;
        sc.ScaleY = 1.25;
        _ = new OpacityFade(dot, 0.65, 0, lifeSec * 1000, () =>
        {
            if (_transientCount == 0 && _tethers.Count == 0 && AvaloniaChaosEnv.Bubbles?.ChaosRabbitTrailSecNow <= 0)
                try { Hide(); } catch { }
        });
        AvaloniaChaosAnim.AnimateDouble(sc, ScaleTransform.ScaleXProperty, 1.25, 0.0, lifeSec * 1000, AvaloniaChaosAnim.EasingMode.EaseOut);
        AvaloniaChaosAnim.AnimateDouble(sc, ScaleTransform.ScaleYProperty, 1.25, 0.0, lifeSec * 1000, AvaloniaChaosAnim.EasingMode.EaseOut);
    }

    private void RemoveTransient(Control el)
    {
        try { _canvas.Children.Remove(el); } catch { }
        _transientCount = Math.Max(0, _transientCount - 1);
        MaybeHide();
    }

    private void MaybeHide()
    {
        if (_transientCount == 0 && _tethers.Count == 0 && AvaloniaChaosEnv.Bubbles?.ChaosRabbitTrailSecNow <= 0)
            try { Hide(); } catch { }
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);

    private static IBrush Frozen(Color c) => new SolidColorBrush(c);
}
