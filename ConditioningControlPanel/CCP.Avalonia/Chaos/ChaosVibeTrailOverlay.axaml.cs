using System;
using System.Runtime.InteropServices;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Shapes;
using global::Avalonia.Media;
using global::Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosVibeTrailOverlay: a warm glow and short fading trail following the cursor.
/// </summary>
public partial class ChaosVibeTrailOverlay : Window
{
    private readonly ILogger<ChaosVibeTrailOverlay> _logger;


    private const double GLOW_SIZE = 58;
    private const double DOT_SIZE = 20;
    private const int TRAIL_DOTS = 14;
    private const double EMIT_DIST = 9;
    private const int FADE_MS = 340;
    private const int TICK_MS = 16;

    private static ChaosVibeTrailOverlay? _active;

    private readonly Canvas _canvas;
    private readonly Ellipse _glow;
    private readonly Ellipse[] _dots = new Ellipse[TRAIL_DOTS];
    private readonly ScaleTransform[] _dotScales = new ScaleTransform[TRAIL_DOTS];
    private readonly DispatcherTimer _follow = new();
    private ScalePulse? _buzz;

#if WINDOWS
    private int _dotIndex;
    private Point _lastEmit = new(double.MinValue, double.MinValue);
#endif

    public ChaosVibeTrailOverlay()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<ChaosVibeTrailOverlay>>();
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

        var dotBrush = new RadialGradientBrush
        {
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        };
        dotBrush.GradientStops.Add(new GradientStop(Color.FromArgb(190, 0xFF, 0xD7, 0x6A), 0.0));
        dotBrush.GradientStops.Add(new GradientStop(Color.FromArgb(110, 0xFF, 0xB0, 0x3A), 0.55));
        dotBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x8A, 0x3A), 1.0));

        for (int i = 0; i < TRAIL_DOTS; i++)
        {
            var sc = new ScaleTransform(1, 1);
            var dot = new Ellipse
            {
                Width = DOT_SIZE,
                Height = DOT_SIZE,
                Fill = dotBrush,
                IsHitTestVisible = false,
                Opacity = 0,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RenderTransform = sc,
            };
            _dots[i] = dot;
            _dotScales[i] = sc;
            _canvas.Children.Add(dot);
        }

        var glowBrush = new RadialGradientBrush
        {
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        };
        glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xE9, 0xA0), 0.12));
        glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(160, 0xFF, 0xB0, 0x3A), 0.5));
        glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x69, 0xB4), 1.0));

        var glowScale = new ScaleTransform(1, 1);
        _glow = new Ellipse
        {
            Width = GLOW_SIZE,
            Height = GLOW_SIZE,
            Fill = glowBrush,
            IsHitTestVisible = false,
            Opacity = 0,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = glowScale,
        };
        _canvas.Children.Add(_glow);

        // A quick buzz-pulse (WPF DoubleAnimation 0.9↔1.12 / 180ms AutoReverse Forever, SineEase) —
        // the sibling CursorGlow breathes slowly; this one is charged. ScalePulse's period is the
        // full up+down cycle, so 180ms half-period => 360ms.
        _buzz = new ScalePulse(glowScale, 0.9, 1.12, 360);

        _follow.Interval = TimeSpan.FromMilliseconds(TICK_MS);
        _follow.Tick += FollowTick;

        Opened += (_, _) => ApplyExStyles();
    }

    public static void EnsureCreated()
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess()) TryCreate();
            else Dispatcher.UIThread.Post(TryCreate);
        }
        catch { }
    }

    private static void TryCreate()
    {
        try
        {
            if (_active == null)
            {
                _active = new ChaosVibeTrailOverlay();
                ((global::Avalonia.Controls.Window)_active).Show();
                _active.Hide();
            }
        }
        catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosVibeTrailOverlay>>().LogInformation("ChaosVibeTrail.EnsureCreated: {E}", ex.Message); }
    }

    public static void Start()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null)
                    {
                        _active = new ChaosVibeTrailOverlay();
                        ((global::Avalonia.Controls.Window)_active).Show();
                    }
                    else if (!_active.IsVisible) _active.Show();
                    AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
                    _active.BeginFollow();
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosVibeTrailOverlay>>().LogInformation("ChaosVibeTrail.Start: {E}", ex.Message); }
            });
        }
        catch { }
    }

    public static void Stop()
    {
        try
        {
            Dispatcher.UIThread.Post(() => { try { _active?.EndFollow(); } catch { } });
        }
        catch { }
    }

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
                    if (w != null) { w._follow.Stop(); w._buzz?.Dispose(); w.Close(); }
                }
                catch { }
            });
        }
        catch { }
    }

    private void BeginFollow()
    {
#if WINDOWS
        _lastEmit = new Point(double.MinValue, double.MinValue);
#endif
        _glow.Opacity = 1;
        FollowTick(null, EventArgs.Empty);
        _follow.Start();
    }

    private void EndFollow()
    {
        _follow.Stop();
        _glow.Opacity = 0;
        foreach (var dot in _dots)
        {
            // TODO: cancel fade timers if implemented; for now just hide.
            dot.Opacity = 0;
        }
        try { Hide(); } catch { }
    }

    private void FollowTick(object? sender, EventArgs e)
    {
        try
        {
#if WINDOWS
            if (!GetCursorPos(out var px)) return;
            double scale = ScalingAt(px.X, px.Y);
            double cx = px.X / scale - Position.X / scale;
            double cy = px.Y / scale - Position.Y / scale;

            Canvas.SetLeft(_glow, cx - GLOW_SIZE / 2);
            Canvas.SetTop(_glow, cy - GLOW_SIZE / 2);

            double dx = cx - _lastEmit.X, dy = cy - _lastEmit.Y;
            if (dx * dx + dy * dy >= EMIT_DIST * EMIT_DIST)
            {
                _lastEmit = new Point(cx, cy);
                var dot = _dots[_dotIndex];
                var sc = _dotScales[_dotIndex];
                _dotIndex = (_dotIndex + 1) % TRAIL_DOTS;

                Canvas.SetLeft(dot, cx - DOT_SIZE / 2);
                Canvas.SetTop(dot, cy - DOT_SIZE / 2);
                dot.Opacity = 0.5;
                sc.ScaleX = 1.0;
                sc.ScaleY = 1.0;

                var fade = new OpacityFade(dot, 0.5, 0, FADE_MS);
                var shrink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                double startMs = Environment.TickCount64;
                shrink.Tick += (_, _) =>
                {
                    double t = Math.Min(1, (Environment.TickCount64 - startMs) / (double)FADE_MS);
                    double s = 1.0 - (1.0 - 0.35) * t;
                    sc.ScaleX = s;
                    sc.ScaleY = s;
                    if (t >= 1) shrink.Stop();
                };
                shrink.Start();
            }
#else
            // Global cursor position is not available cross-platform without platform-specific interop.
            // The trail overlay is a Windows-only visual flourish for now.
            return;
#endif
        }
        catch (Exception ex) { _logger?.LogInformation("ChaosVibeTrail tick: {E}", ex.Message); }
    }

    private double ScalingAt(int x, int y)
    {
        try
        {
            var screens = AvaloniaChaosWindowZ.GetScreens();
            if (screens == null) return 1.0;
            foreach (var s in screens.All)
            {
                var b = s.Bounds;
                if (x >= b.X && x < b.Right && y >= b.Y && y < b.Bottom)
                    return s.Scaling > 0 ? s.Scaling : 1.0;
            }
            var primary =
screens.Primary;
            if (primary != null) return primary.Scaling > 0 ? primary.Scaling : 1.0;
        }
        catch { }
        return 1.0;
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);

#if WINDOWS
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
#endif
}
