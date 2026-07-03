using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosDvdOverlay: DVD-screensaver text logo that drifts across the screen.
/// Pooled and recycled; windows only close at run teardown.
/// </summary>
public partial class ChaosDvdOverlay : Window
{
    private readonly ILogger<ChaosDvdOverlay> _logger;


    private const double BASE_FONT = 46;
    private const double BASE_SPEED = 230;
    private const double PEAK_OPAC = 0.85;
    private const int TICK_MS = 33;
    private const int MAX_THOUGHTS = 8;
    private const int POOL_MAX = 12;
    private const int MAX_TOY_LOGOS = 8;

    private static readonly Color[] Hues =
    {
        Color.FromRgb(0xFF, 0x4D, 0xC4), Color.FromRgb(0x7A, 0xE0, 0xFF), Color.FromRgb(0xFF, 0xD7, 0x00),
        Color.FromRgb(0x9C, 0xE8, 0xA0), Color.FromRgb(0xD2, 0x4D, 0xFF), Color.FromRgb(0xFF, 0x8A, 0x5C),
    };

    private static readonly List<ChaosDvdOverlay> _active = new();
    private static readonly Stack<ChaosDvdOverlay> _pool = new();
    private static readonly Random _rng = new();

    public static Func<bool>? SpankerRedirect;

    private readonly AvaloniaOutlinedText _label;
    private readonly Grid _host;
    private bool _isThought;
    private bool _splitOnRabbit;
    private bool _clickable;
    private double _fontScale = 1.0;
    private bool _splitSpent;
    private int _splitBouncesLeft;
    private bool _closed;
    private double _vx, _vy;          // DIP/s (BASE_SPEED is a DIP/s feel constant, WPF parity)
    private double _x, _y;           // DIP motion state; converted to physical px only at Position
    private double _scale = 1.0;     // primary screen scaling (px = DIP * _scale)
    private static DateTime _lastBounceCue;
    private double _remainingSec;
    private int _hueIndex;
    private DispatcherTimer? _tick;
    private OpacityFade? _fade;

    public static void Launch(double durationSec, double speedMult, double scale, int count = 1,
                              string? text = null, bool splitOnRabbit = false, int splitBounces = 0)
    {
        var logger = App.Services.GetRequiredService<ILogger<ChaosDvdOverlay>>();
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                for (int i = 0; i < Math.Clamp(count, 1, 2); i++)
                {
                    if (text != null && ThoughtCount() >= MAX_THOUGHTS) break;
                    Acquire().Begin(durationSec, speedMult, scale, text, splitOnRabbit, null, null, null, null, splitBounces);
                }
            }
            catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosDvdOverlay>>().LogInformation("ChaosDvdOverlay.Launch: {E}", ex.Message); }
        });
    }

    private static ChaosDvdOverlay Acquire()
    {
        while (_pool.Count > 0)
        {
            var pooled = _pool.Pop();
            if (pooled.IsLoaded && !pooled._closed) return pooled;
        }
        return new ChaosDvdOverlay();
    }

    private static int ThoughtCount() => _active.Count(w => w._isThought);
    public static bool AnyActive => _active.Count > 0;
    public static bool AnyToyActive => _active.Any(w => !w._isThought);

    public static void RaiseActive()
    {
        foreach (var w in _active) AvaloniaChaosWindowZ.RaiseTopmost(w);
    }

    public static void CloseActive()
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var w in _active.ToArray()) w.CloseNow();
            while (_pool.Count > 0)
            {
                try { _pool.Pop().CloseNow(); } catch { }
            }
        });
    }

    public ChaosDvdOverlay()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<ChaosDvdOverlay>>();
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
        Opacity = 0;

        _label = new AvaloniaOutlinedText
        {
            Stroke = FrozenBrush(Color.FromRgb(0x0B, 0x08, 0x12)),
            StrokeThickness = 2.6,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _host = new Grid { Children = { _label }, IsHitTestVisible = false };
        _host.PointerPressed += (_, e) =>
        {
            if (!_clickable) return;
            Redirect();
            e.Handled = true;
        };
        Content = _host;

        Opened += (_, _) => ApplyExStyles(false);
    }

    private void Begin(double durationSec, double speedMult, double scale,
                       string? text, bool splitOnRabbit,
                       double? startX, double? startY, double? vxOverride, double? vyOverride,
                       int splitBounces = 0)
    {
        _remainingSec = Math.Max(1, durationSec);
        _isThought = text != null;
        _splitOnRabbit = splitOnRabbit;
        _fontScale = Math.Clamp(scale, 0.5, 1.5);
        _splitSpent = false;
        _splitBouncesLeft = Math.Max(0, splitBounces);

        _clickable = SpankerRedirect?.Invoke() == true;
        IsHitTestVisible = _clickable;
        _host.IsHitTestVisible = _clickable;
        _host.Background = _clickable ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : null;
        _host.Cursor = _clickable ? new Cursor(StandardCursorType.Hand) : null;

        _hueIndex = _rng.Next(Hues.Length);
        _label.Text = text ?? "PORN";
        _label.FontSize = BASE_FONT * _fontScale;
        _label.Fill = FrozenBrush(Hues[_hueIndex]);
        _label.Build();

        // Work entirely in DIPs (WPF is all-DIP): speed is a DIP/s feel constant, Width/Height are
        // the label's DIP size, and the work area is converted to DIPs. Only Position (physical px)
        // and the bubble-engine rects (physical px) are converted back with _scale.
        _scale = AvaloniaChaosWindowZ.PrimaryScaling();
        var wa = GetWorkAreaDip(_scale);
        double speed = BASE_SPEED * Math.Clamp(speedMult, 0.3, 2.0);   // DIP/s
        double angle = _rng.NextDouble() * Math.PI / 3 + Math.PI / 9;
        _vx = vxOverride ?? speed * Math.Cos(angle) * (_rng.Next(2) == 0 ? 1 : -1);
        _vy = vyOverride ?? speed * Math.Sin(angle) * (_rng.Next(2) == 0 ? 1 : -1);

        _active.Add(this);
        ((global::Avalonia.Controls.Window)this).Show();
        AvaloniaChaosWindowZ.RaiseAboveVideo(this);
        ApplyExStyles(_clickable);

        Width = _label.Width;       // DIP
        Height = _label.Height;     // DIP
        _x = startX ?? wa.X + _rng.NextDouble() * Math.Max(1, wa.Width - Width);
        _y = startY ?? wa.Y + _rng.NextDouble() * Math.Max(1, wa.Height - Height);
        Position = new PixelPoint((int)(_x * _scale), (int)(_y * _scale));

        Opacity = 0;
        _fade?.Dispose();
        _fade = new OpacityFade(this, 0, PEAK_OPAC, 180);

        _tick?.Stop();
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TICK_MS) };
        _tick.Tick += Step;
        _tick.Start();
    }

    private void Redirect()
    {
        double spd = Math.Sqrt(_vx * _vx + _vy * _vy);
        if (spd < 1) spd = BASE_SPEED;
        spd = Math.Min(spd * 1.18, BASE_SPEED * 2.5);
        double angle = _rng.NextDouble() * Math.PI * 2;
        _vx = Math.Cos(angle) * spd;
        _vy = Math.Sin(angle) * spd;
        _hueIndex = (_hueIndex + 1) % Hues.Length;
        _label.Fill = FrozenBrush(Hues[_hueIndex]);
        _label.Build();
        AvaloniaChaosSfx.Play("dvd_bounce", 0.4f);
    }

    private void SplitInTwo()
    {
        _remainingSec += 2.0;
        if (ThoughtCount() >= MAX_THOUGHTS) return;
        double spd = Math.Sqrt(_vx * _vx + _vy * _vy);
        double baseAng = Math.Atan2(_vy, _vx);
        double ang = baseAng + (_rng.Next(2) == 0 ? 0.61 : -0.61);
        Acquire().Begin(_remainingSec, 1.0, _fontScale, _label.Text, true,
            _x, _y, Math.Cos(ang) * spd, Math.Sin(ang) * spd);
        AvaloniaChaosSfx.Play("dvd_bounce", 0.4f);
    }

    private void Step(object? sender, EventArgs e)
    {
        try
        {
            double dt = TICK_MS / 1000.0;
            _remainingSec -= dt;
            if (_remainingSec <= 0) { FadeOutAndRetire(); return; }

            var wa = GetWorkAreaDip(_scale);   // DIP
            double x = _x + _vx * dt;          // DIP
            double y = _y + _vy * dt;
            bool bounced = false;
            if (x <= wa.X) { x = wa.X; _vx = Math.Abs(_vx); bounced = true; }
            else if (x + Width >= wa.Right) { x = wa.Right - Width; _vx = -Math.Abs(_vx); bounced = true; }
            if (y <= wa.Y) { y = wa.Y; _vy = Math.Abs(_vy); bounced = true; }
            else if (y + Height >= wa.Bottom) { y = wa.Bottom - Height; _vy = -Math.Abs(_vy); bounced = true; }
            _x = x; _y = y;
            Position = new PixelPoint((int)(x * _scale), (int)(y * _scale));

            if (bounced)
            {
                _hueIndex = (_hueIndex + 1) % Hues.Length;
                _label.Fill = FrozenBrush(Hues[_hueIndex]);
                _label.Build();
                var now = DateTime.UtcNow;
                if ((now - _lastBounceCue).TotalMilliseconds >= 250)
                {
                    _lastBounceCue = now;
                    AvaloniaChaosSfx.Play("dvd_bounce", 0.35f);
                }

                if (_splitBouncesLeft > 0 && !_isThought)
                {
                    _splitBouncesLeft--;
                    if (_active.Count(w => !w._isThought) < MAX_TOY_LOGOS)
                    {
                        double spd = Math.Sqrt(_vx * _vx + _vy * _vy);
                        double baseAng = Math.Atan2(_vy, _vx);
                        double ang = baseAng + (_rng.Next(2) == 0 ? 0.61 : -0.61);
                        Acquire().Begin(_remainingSec, 1.0, _fontScale, null, false,
                            _x, _y, Math.Cos(ang) * spd, Math.Sin(ang) * spd,
                            _splitBouncesLeft);
                        AvaloniaChaosSfx.Play("dvd_launch", 0.35f);
                    }
                }
            }

            // The bubble engine works in PHYSICAL px (IAvaloniaBubbleService rects are px).
            var rectPx = new Rect(x * _scale, y * _scale, Width * _scale, Height * _scale);
            AvaloniaChaosEnv.Bubbles?.PopBubblesInRect(rectPx);

            if (_splitOnRabbit && !_splitSpent
                && AvaloniaChaosEnv.Bubbles?.AnyDarterIntersects(rectPx) == true)
            {
                _splitSpent = true;
                SplitInTwo();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogInformation("ChaosDvdOverlay step: {E}", ex.Message);
            CloseNow();
        }
    }

    private void FadeOutAndRetire()
    {
        _tick?.Stop();
        _fade?.Dispose();
        _fade = new OpacityFade(this, Opacity, 0, 240, Retire);
    }

    private void Retire()
    {
        _tick?.Stop();
        _active.Remove(this);
        if (_closed) return;
        try
        {
            Opacity = 0;
            Hide();
        }
        catch { }
        if (_pool.Count < POOL_MAX) _pool.Push(this);
        else { try { Close(); } catch { } }
    }

    private void CloseNow()
    {
        _tick?.Stop();
        _active.Remove(this);
        try { Close(); } catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _tick?.Stop();
        base.OnClosed(e);
    }

    private static IBrush FrozenBrush(Color c) => new SolidColorBrush(c);

    private static Rect GetWorkArea()
    {
        var screens = AvaloniaChaosWindowZ.GetScreens();
        var primary = screens?.Primary;
        if (primary == null) return new Rect(0, 0, 1920, 1080);
        var wa = primary.WorkingArea;
        return new Rect(wa.X, wa.Y, wa.Width, wa.Height);   // physical px
    }

    /// <summary>The primary work area in DIPs (physical px / scaling) — the space the logo's
    /// DIP-sized label and DIP/s velocity live in.</summary>
    private static Rect GetWorkAreaDip(double scale)
    {
        if (scale <= 0) scale = 1.0;
        var wa = GetWorkArea();   // physical px
        return new Rect(wa.X / scale, wa.Y / scale, wa.Width / scale, wa.Height / scale);
    }

    private void ApplyExStyles(bool clickable) => ChaosWin32Helper.ApplyOverlayExStyles(this, !clickable);
}
