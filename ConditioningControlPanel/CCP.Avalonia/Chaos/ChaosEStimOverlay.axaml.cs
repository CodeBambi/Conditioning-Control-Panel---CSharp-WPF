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
/// Avalonia port of ChaosEStimOverlay: full-screen lightning bolts between bubbles.
/// Kept alive; shown only during a strike.
/// </summary>
public partial class ChaosEStimOverlay : Window
{
    private readonly ILogger<ChaosEStimOverlay> _logger;


    private const int FLICKER_MS = 40;
    private const int HOT_MS = 120;
    private const int FADE_MS = 80;
    private const double JITTER = 14;
    private const double SEG_LEN = 55;
    private const double FLASH_SIZE = 28;
    private const double STRIKE_OPACITY = 0.85;

    private static readonly Color GlowColor = Color.FromRgb(0x9C, 0x5C, 0xFF);
    private static readonly Color CoreColor = Color.FromRgb(0xBF, 0xEC, 0xFF);

    private static ChaosEStimOverlay? _active;

    private readonly Canvas _canvas;
    private readonly DispatcherTimer _tick;
    private readonly Random _rng = new();
    private readonly List<(Point A, Point B)> _segments = new();
    private readonly List<Polyline> _glows = new();
    private readonly List<Polyline> _cores = new();
    private double _elapsedMs;
    private int _strikeSeq;
    private OpacityFade? _lifeFade;
    private DispatcherTimer? _hold;

    public static void Arm() => ChaosEStimGlowOverlay.Arm();
    public static void Disarm() => ChaosEStimGlowOverlay.Disarm();

    public static void EnsureCreated()
    {
        var logger = App.Services.GetRequiredService<ILogger<ChaosEStimOverlay>>();
        ChaosEStimGlowOverlay.EnsureCreated();
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null)
                    {
                        _active = new ChaosEStimOverlay();
                        ((global::Avalonia.Controls.Window)_active).Show();
                        _active.Hide();
                    }
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosEStimOverlay>>().LogInformation("ChaosEStim.EnsureCreated: {E}", ex.Message); }
            });
        }
        catch { }
    }

    public static void Strike(IReadOnlyList<(Point From, Point To)> boltsPx)
    {
        var logger = App.Services.GetRequiredService<ILogger<ChaosEStimOverlay>>();
        if (boltsPx == null || boltsPx.Count == 0) return;
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null)
                    {
                        _active = new ChaosEStimOverlay();
                        ((global::Avalonia.Controls.Window)_active).Show();
                    }
                    else if (!_active.IsVisible) ((global::Avalonia.Controls.Window)_active).Show();
                    AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
                    _active.BeginStrike(boltsPx);
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosEStimOverlay>>().LogInformation("ChaosEStim.Strike: {E}", ex.Message); }
            });
        }
        catch { }
    }

    public static void RaiseActive()
    {
        AvaloniaChaosWindowZ.RaiseTopmost(_active);
        ChaosEStimGlowOverlay.RaiseActive();
    }

    public static void CloseActive()
    {
        ChaosEStimGlowOverlay.CloseActive();
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var w = _active;
                    _active = null;
                    if (w != null) { w._tick.Stop(); w._hold?.Stop(); w.Close(); }
                }
                catch { }
            });
        }
        catch { }
    }

    public ChaosEStimOverlay()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<ChaosEStimOverlay>>();
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

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FLICKER_MS) };
        _tick.Tick += StrikeTick;

        Opened += (_, _) => ApplyExStyles();
    }

    private void BeginStrike(IReadOnlyList<(Point From, Point To)> boltsPx)
    {
        // px is physical screen px; the canvas is laid out in the window's DIPs at RenderScaling,
        // so convert the physical offset from the (physical-px) window origin into DIPs.
        double scale = RenderScaling <= 0 ? 1.0 : RenderScaling;
        Point Local(Point px) => new Point((px.X - Position.X) / scale, (px.Y - Position.Y) / scale);

        _tick.Stop();
        _lifeFade?.Dispose();
        _hold?.Stop();
        _canvas.Children.Clear();
        _segments.Clear();
        _glows.Clear();
        _cores.Clear();
        _elapsedMs = 0;

        var flashBrush = new RadialGradientBrush { GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative) };
        flashBrush.GradientStops.Add(new GradientStop(Color.FromArgb(130, 0xFF, 0xFF, 0xFF), 0.0));
        flashBrush.GradientStops.Add(new GradientStop(Color.FromArgb(70, CoreColor.R, CoreColor.G, CoreColor.B), 0.45));
        flashBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, GlowColor.R, GlowColor.G, GlowColor.B), 1.0));

        foreach (var (fromPx, toPx) in boltsPx)
        {
            var a = Local(fromPx);
            var b = Local(toPx);
            _segments.Add((a, b));

            var glow = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(80, GlowColor.R, GlowColor.G, GlowColor.B)),
                StrokeThickness = 5.5,
                StrokeJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
            };
            var core = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(190, CoreColor.R, CoreColor.G, CoreColor.B)),
                StrokeThickness = 1.7,
                StrokeJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
            };
            _glows.Add(glow);
            _cores.Add(core);
            _canvas.Children.Add(glow);
            _canvas.Children.Add(core);

            var flash = new Ellipse
            {
                Width = FLASH_SIZE, Height = FLASH_SIZE,
                Fill = flashBrush, IsHitTestVisible = false,
            };
            Canvas.SetLeft(flash, b.X - FLASH_SIZE / 2);
            Canvas.SetTop(flash, b.Y - FLASH_SIZE / 2);
            _canvas.Children.Add(flash);
        }

        JitterBolts();
        _tick.Start();

        int seq = ++_strikeSeq;
        Opacity = STRIKE_OPACITY;
        // Hold the bolt hot for HOT_MS, THEN fade over FADE_MS (WPF keyframes: discrete hold at 0
        // and HOT_MS, linear to 0 at HOT_MS+FADE_MS) — not one continuous fade from full brightness.
        _hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HOT_MS) };
        _hold.Tick += (_, _) =>
        {
            _hold?.Stop();
            if (seq != _strikeSeq) return;
            _lifeFade = new OpacityFade(this, STRIKE_OPACITY, 0, FADE_MS, () =>
            {
                if (seq != _strikeSeq) return;
                _tick.Stop();
                _canvas.Children.Clear();
                try { Hide(); } catch { }
            });
        };
        _hold.Start();
    }

    private void JitterBolts()
    {
        for (int i = 0; i < _segments.Count; i++)
        {
            var (a, b) = _segments[i];
            var pts = new List<Point> { a };
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            int mids = Math.Max(2, (int)(len / SEG_LEN));
            double px = len > 0.001 ? -dy / len : 0, py = len > 0.001 ? dx / len : 0;
            for (int m = 1; m <= mids; m++)
            {
                double f = m / (double)(mids + 1);
                double off =
(_rng.NextDouble() * 2 - 1) * JITTER;
                pts.Add(new Point(a.X + dx * f + px * off, a.Y + dy * f + py * off));
            }
            pts.Add(b);
            _glows[i].Points = pts;
            _cores[i].Points = pts;
        }
    }

    private void StrikeTick(object? sender, EventArgs e)
    {
        try
        {
            _elapsedMs += FLICKER_MS;
            if (_elapsedMs >= HOT_MS) { _tick.Stop(); return; }
            JitterBolts();
        }
        catch (Exception ex) { _logger?.LogInformation("ChaosEStim tick: {E}", ex.Message); }
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);
}
