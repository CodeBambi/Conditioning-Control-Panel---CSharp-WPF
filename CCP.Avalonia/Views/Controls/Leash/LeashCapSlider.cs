// PORTED from WPF 7.1.5 Controls/Leash/LeashCapSlider.cs: the video time cap, 1 to 90 minutes
// (LeashVideoCap). Three bands, blue to 30, yellow to 60, red to 90; fill and readout wear the band
// the value is in. Drag, click, arrows (1 min) or PageUp/PageDown (5 min). Ceiling stops the thumb
// short of 90 (the holder's slider stops at the leashed side's maximum). Same API as WPF:
// (label, value, tag), Value, Ceiling, ColorOf, Committed.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal sealed class LeashCapSlider : Grid
{
    public static readonly Color Blue = Color.FromRgb(0x5E, 0xA8, 0xFF);
    public static readonly Color Yellow = Color.FromRgb(0xFF, 0xCF, 0x6B);
    public static readonly Color Red = Color.FromRgb(0xFF, 0x5F, 0x7A);
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1B, 0x10, 0x26));

    private const double TrackH = 6;
    private const double ThumbD = 16;

    private readonly Canvas _canvas = new() { Height = 22, Background = Brushes.Transparent };
    private readonly TextBlock _readout;
    private int _value;
    private int _ceiling = LeashVideoCap.Max;
    private bool _dragging;

    /// <summary>Raised when a drag or key settles on a new value (not on every move).</summary>
    public event Action<int>? Committed;

    public LeashCapSlider(string label, int value, string tag)
    {
        Tag = tag;
        Focusable = true;
        ColumnDefinitions = new ColumnDefinitions("*,74");
        RowDefinitions = new RowDefinitions("Auto,Auto");
        _canvas.Cursor = FriendsDrawer.Hand();

        Children.Add(FriendsDrawer.Label(label, 11.5, FriendsDrawer.Muted));
        _readout = FriendsDrawer.Label("", 12, FriendsDrawer.Text, FriendsDrawer.Mono, FontWeight.SemiBold);
        _readout.HorizontalAlignment = HorizontalAlignment.Right;
        _readout.Tag = tag + ":readout";
        SetColumn(_readout, 1);
        Children.Add(_readout);

        SetRow(_canvas, 1);
        SetColumnSpan(_canvas, 2);
        Children.Add(_canvas);

        _value = LeashVideoCap.Clamp(value);
        _canvas.SizeChanged += (_, _) => Draw();
        _canvas.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;
            Focus();
            _dragging = true;
            e.Pointer.Capture(_canvas);
            SetFrom(e.GetPosition(_canvas).X);
            e.Handled = true;
        };
        _canvas.PointerMoved += (_, e) => { if (_dragging) SetFrom(e.GetPosition(_canvas).X); };
        _canvas.PointerReleased += (_, e) =>
        {
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
            Committed?.Invoke(_value);
        };
        _canvas.PointerCaptureLost += (_, _) => _dragging = false;
        KeyDown += OnKey;
        Draw();
    }

    public int Value
    {
        get => _value;
        set { _value = Math.Min(LeashVideoCap.Clamp(value), _ceiling); Draw(); }
    }

    public int Ceiling
    {
        get => _ceiling;
        set { _ceiling = LeashVideoCap.Clamp(value); if (_value > _ceiling) _value = _ceiling; Draw(); }
    }

    internal string ReadoutText => _readout.Text ?? "";

    public static Color ColorOf(int minutes) => LeashVideoCap.BandOf(minutes) switch
    {
        LeashVideoCap.Band.Blue => Blue,
        LeashVideoCap.Band.Yellow => Yellow,
        _ => Red,
    };

    /// <summary>The key step WPF takes: arrows 1, PageUp/PageDown 5, anything else 0.</summary>
    internal static int StepFor(Key k) => k switch
    {
        Key.Left or Key.Down => -1,
        Key.Right or Key.Up => 1,
        Key.PageDown => -5,
        Key.PageUp => 5,
        _ => 0,
    };

    internal void Nudge(int step)
    {
        if (step == 0) return;
        Value = _value + step;
        Committed?.Invoke(_value);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        var step = StepFor(e.Key);
        if (step == 0) return;
        Nudge(step);
        e.Handled = true;
    }

    /// <summary>The value a click at <paramref name="x"/> lands on, for a track <paramref name="width"/> wide.</summary>
    internal static int ValueAt(double x, double width)
    {
        var w = Math.Max(1, width - ThumbD);
        var t = Math.Clamp((x - ThumbD / 2) / w, 0, 1);
        return (int)Math.Round(LeashVideoCap.Min + t * (LeashVideoCap.Max - LeashVideoCap.Min));
    }

    private void SetFrom(double x) => Value = ValueAt(x, _canvas.Bounds.Width);

    private double XOf(int minutes)
    {
        var w = Math.Max(1, _canvas.Bounds.Width - ThumbD);
        return ThumbD / 2 + w * (minutes - LeashVideoCap.Min) / (double)(LeashVideoCap.Max - LeashVideoCap.Min);
    }

    private void Draw()
    {
        _readout.Text = Loc.GetF("leash_video_cap", _value);
        _readout.Foreground = new SolidColorBrush(ColorOf(_value));
        _canvas.Children.Clear();
        if (_canvas.Bounds.Width <= ThumbD) return;
        var y = (_canvas.Height - TrackH) / 2;

        void Band(int from, int to, Color c, double alpha)
        {
            var x0 = XOf(from);
            var x1 = XOf(to);
            if (x1 <= x0) return;
            var r = new Rectangle
            {
                Width = x1 - x0, Height = TrackH, RadiusX = 3, RadiusY = 3,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(255 * alpha), c.R, c.G, c.B)),
            };
            Canvas.SetLeft(r, x0);
            Canvas.SetTop(r, y);
            _canvas.Children.Add(r);
        }
        var bands = new[] { (LeashVideoCap.Min, 30, Blue), (30, 60, Yellow), (60, LeashVideoCap.Max, Red) };
        // The three bands, dim; past the ceiling dimmer still.
        foreach (var (a, b, c) in bands)
        {
            Band(a, Math.Min(b, _ceiling), c, 0.28);
            if (b > _ceiling) Band(Math.Max(a, _ceiling), b, c, 0.08);
        }
        // The fill to the value, each band in its own full colour.
        foreach (var (a, b, c) in bands)
            if (_value > a) Band(a, Math.Min(b, _value), c, 1.0);

        var thumb = new Ellipse
        {
            Width = ThumbD, Height = ThumbD,
            Fill = new SolidColorBrush(ColorOf(_value)),
            Stroke = Ink, StrokeThickness = 2,
            Tag = "leash-cap-thumb",
        };
        Canvas.SetLeft(thumb, XOf(_value) - ThumbD / 2);
        Canvas.SetTop(thumb, (_canvas.Height - ThumbD) / 2);
        _canvas.Children.Add(thumb);
    }
}
