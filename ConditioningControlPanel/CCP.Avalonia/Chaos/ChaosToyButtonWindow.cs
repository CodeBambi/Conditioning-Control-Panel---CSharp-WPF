using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosToyButtonWindow: a small topmost hero button for an equipped active toy.
/// </summary>
public sealed class ChaosToyButtonWindow : Window
{
    private const double DISC = 138;
    private const double PAD = 14;

    private readonly ChaosToyState _toy;
    private readonly IChaosService? _chaos;
    private readonly Border _disc;
    private readonly TextBlock _glyph;
    private readonly TextBlock _status;
    private readonly ScaleTransform _press = new(1, 1);
    private readonly Border _glow;
    private VisualState _vstate = (VisualState)(-1);
    private DispatcherTimer? _pulseTimer;

    private enum VisualState { Cooling, Ready, Active }

    private static readonly IBrush DiscBg = Frozen(Color.FromArgb(0xE6, 0x15, 0x12, 0x26));
    private static readonly IBrush ReadyBorder = Frozen(Color.FromRgb(0xFF, 0x69, 0xB4));
    private static readonly IBrush ActiveBorder = Frozen(Color.FromRgb(0xFF, 0xC8, 0xE8));
    private static readonly IBrush CoolBorder = Frozen(Color.FromArgb(0x55, 0x9A, 0x9A, 0xA8));

    public ChaosToyButtonWindow() : this(new ChaosToyState(), null, 0) { }

    public ChaosToyButtonWindow(ChaosToyState toy, IChaosService? chaos, int slotIndex)
    {
        _toy = toy;
        _chaos = chaos;

        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = AvaloniaChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = true;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = DISC + PAD * 2;
        Height = DISC + PAD * 2;

        var wa = AvaloniaChaosWindowZ.GetScreens()?.Primary?.WorkingArea;
        double wr = wa?.Width ?? 1920;
        double hb = wa?.Height ?? 1080;
        Position = new PixelPoint((int)(wr - 16 - Width - slotIndex * (DISC + 20)),
                                  (int)(hb - DISC - PAD * 2 - 14));
        Cursor = new Cursor(StandardCursorType.Hand);

        _glyph = new TextBlock
        {
            Text = toy.Glyph,
            FontSize = 50,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20),
        };
        _status = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = Frozen(Color.FromRgb(0xE8, 0xA0, 0xD8)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 18),
        };
        var keyBadge = new Border
        {
            Background = Frozen(Color.FromArgb(0xDD, 0x12, 0x10, 0x26)),
            BorderBrush = ReadyBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(9, 3, 9, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Child = new TextBlock
            {
                Text = toy.KeyLabel,
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                Foreground = Frozen(Color.FromRgb(0xFF, 0x69, 0xB4)),
            },
        };

        _glow = new Border
        {
            Width = DISC,
            Height = DISC,
            CornerRadius = new CornerRadius(DISC / 2),
            Background = DiscBg,
            BorderBrush = ReadyBorder,
            BorderThickness = new Thickness(3.5),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Color = Color.FromArgb(0, 0xFF, 0x69, 0xB4),
                Blur = 26,
                Spread = 0,
            }),
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = _press,
            Child = new Grid { Children = { _glyph, _status } },
        };
        _disc = _glow;

        Content = new Grid { Children = { _glow, keyBadge } };

        PointerPressed += (_, _) => { PressPulse(); _chaos?.UseToyById(_toy.Id); };
        _toy.PropertyChanged += (_, _) => Dispatcher.UIThread.Post(UpdateVisual);
        Opened += (_, _) => ApplyExStyles();
        ChaosTips.Attach(_disc, $"{toy.Glyph} {toy.Name} · {toy.KeyLabel}", toy.Desc,
            string.IsNullOrEmpty(toy.CapstoneDesc) ? null : "max: " + toy.CapstoneDesc,
            flavor: toy.Flavor);
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        try
        {
            _status.Text = _toy.StatusText;
            var vs = _toy.IsEffectActive ? VisualState.Active
                   : _toy.IsReady ? VisualState.Ready
                   : VisualState.Cooling;
            if (vs == _vstate) return;
            _vstate = vs;
            _pulseTimer?.Stop();

            switch (vs)
            {
                case VisualState.Active:
                    _glow.BorderBrush = ActiveBorder;
                    _glow.Opacity = 1.0;
                    _glyph.Opacity = 1.0;
                    _glow.BoxShadow = GlowFor(Color.FromRgb(0xFF, 0xC8, 0xE8), 1.0);
                    break;

                case VisualState.Ready:
                    _glow.BorderBrush = ReadyBorder;
                    _glow.Opacity = 1.0;
                    _glyph.Opacity = 1.0;
                    StartGlowPulse();
                    break;

                default:
                    _glow.Opacity = 0.45;
                    _glow.BorderBrush = CoolBorder;
                    _glyph.Opacity = 0.55;
                    _glow.BoxShadow = GlowFor(Color.FromRgb(0xFF, 0x69, 0xB4), 0);
                    break;
            }
        }
        catch { }
    }

    private void StartGlowPulse()
    {
        if (_pulseTimer != null) { _pulseTimer.Start(); return; }
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double startMs = Environment.TickCount64;
        _pulseTimer.Tick += (_, _) =>
        {
            double t = (Environment.TickCount64 - startMs) / 950.0;
            double o = 0.25 + 0.7 * ((Math.Sin(t * Math.PI * 2) + 1) / 2);
            _glow.BoxShadow = GlowFor(Color.FromRgb(0xFF, 0x69, 0xB4), o);
        };
        _pulseTimer.Start();
    }

    private static BoxShadows GlowFor(Color c, double opacity)
    {
        byte a = (byte)Math.Clamp(opacity * 255, 0, 255);
        return new BoxShadows(new BoxShadow
        {
            Color = Color.FromArgb(a, c.R, c.G, c.B),
            Blur = 26,
            Spread = 0,
        });
    }

    private void PressPulse()
    {
        _press.ScaleX = _press.ScaleY = 0.86;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double startMs = Environment.TickCount64;
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (Environment.TickCount64 - startMs) / 180.0);
            double v = 0.86 + 0.14 * EaseOutBack(t);
            _press.ScaleX = _press.ScaleY = v;
            if (t >= 1) timer.Stop();
        };
        timer.Start();
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    /// <summary>Stop the Ready-state glow pulse (a 16ms <see cref="DispatcherTimer"/>) on close. Without
    /// this, closing the button while Ready leaves the pulse ticking against a torn-down window — the
    /// visual-state change that normally stops it never fires. WPF stops render-thread animations
    /// implicitly on window teardown; the ported DispatcherTimer does not.</summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { _pulseTimer?.Stop(); } catch { }
        _pulseTimer = null;
    }

    public void RaiseToTopmost() => AvaloniaChaosWindowZ.RaiseTopmost(this);

    private static IBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        return b;
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, false);
}
