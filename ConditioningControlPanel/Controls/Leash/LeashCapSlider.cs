using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// The video time cap, 1 to 90 minutes (<see cref="LeashVideoCap"/>). The track is painted in
/// three bands, blue to 30, yellow to 60, red to 90; the fill and the readout wear the band the
/// value is in. Drag, click, arrows (1 min) or PageUp/PageDown (5 min). <see cref="Ceiling"/>
/// stops the thumb short of 90 (the holder's slider stops at the leashed side's maximum).
/// </summary>
internal sealed class LeashCapSlider : Grid
{
    public static readonly Color Blue = FriendsLook.Rgb(0x5E, 0xA8, 0xFF);
    public static readonly Color Yellow = FriendsLook.Gold;
    public static readonly Color Red = FriendsLook.Red;

    private const double TrackH = 6;
    private const double ThumbD = 16;

    private readonly Canvas _canvas = new() { Height = 22, Background = Brushes.Transparent, Cursor = Cursors.Hand };
    private readonly TextBlock _readout;
    private int _value;
    private int _ceiling = LeashVideoCap.Max;

    /// <summary>Raised when a drag or key settles on a new value (not on every move).</summary>
    public event Action<int>? Committed;

    public LeashCapSlider(string label, int value, string tag)
    {
        Tag = tag;
        Focusable = true;
        FocusVisualStyle = null;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var l = FriendsLook.Label(label, 11.5, FriendsLook.MutedBrush);
        Children.Add(l);
        _readout = FriendsLook.Label("", 12, FriendsLook.TextBrush, FriendsLook.Mono, FontWeights.SemiBold);
        _readout.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_readout, 1);
        Children.Add(_readout);

        Grid.SetRow(_canvas, 1);
        Grid.SetColumnSpan(_canvas, 2);
        Children.Add(_canvas);

        _value = LeashVideoCap.Clamp(value);
        _canvas.SizeChanged += (_, _) => Draw();
        _canvas.MouseLeftButtonDown += (_, e) => { Focus(); _canvas.CaptureMouse(); SetFrom(e.GetPosition(_canvas).X); e.Handled = true; };
        _canvas.MouseMove += (_, e) => { if (_canvas.IsMouseCaptured) SetFrom(e.GetPosition(_canvas).X); };
        _canvas.MouseLeftButtonUp += (_, _) => { if (_canvas.IsMouseCaptured) { _canvas.ReleaseMouseCapture(); Committed?.Invoke(_value); } };
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

    public static Color ColorOf(int minutes) => LeashVideoCap.BandOf(minutes) switch
    {
        LeashVideoCap.Band.Blue => Blue,
        LeashVideoCap.Band.Yellow => Yellow,
        _ => Red,
    };

    private void OnKey(object sender, KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Left or Key.Down => -1,
            Key.Right or Key.Up => 1,
            Key.PageDown => -5,
            Key.PageUp => 5,
            _ => 0,
        };
        if (step == 0) return;
        Value = _value + step;
        Committed?.Invoke(_value);
        e.Handled = true;
    }

    private void SetFrom(double x)
    {
        var w = Math.Max(1, _canvas.ActualWidth - ThumbD);
        var t = Math.Clamp((x - ThumbD / 2) / w, 0, 1);
        Value = (int)Math.Round(LeashVideoCap.Min + t * (LeashVideoCap.Max - LeashVideoCap.Min));
    }

    private double XOf(int minutes)
    {
        var w = Math.Max(1, _canvas.ActualWidth - ThumbD);
        return ThumbD / 2 + w * (minutes - LeashVideoCap.Min) / (double)(LeashVideoCap.Max - LeashVideoCap.Min);
    }

    private void Draw()
    {
        _readout.Text = Loc.GetF("leash_video_cap", _value);
        _readout.Foreground = FriendsLook.Frozen(ColorOf(_value));
        _canvas.Children.Clear();
        if (_canvas.ActualWidth <= ThumbD) return;
        var y = (_canvas.Height - TrackH) / 2;

        // The three bands, dim; past the ceiling dimmer still.
        void Band(int from, int to, Color c, double alpha)
        {
            var x0 = XOf(from);
            var x1 = XOf(to);
            if (x1 <= x0) return;
            var r = new Rectangle
            {
                Width = x1 - x0, Height = TrackH, RadiusX = 3, RadiusY = 3,
                Fill = FriendsLook.Frozen(Color.FromArgb((byte)(255 * alpha), c.R, c.G, c.B)),
            };
            Canvas.SetLeft(r, x0);
            Canvas.SetTop(r, y);
            _canvas.Children.Add(r);
        }
        foreach (var (a, b, c) in new[] { (LeashVideoCap.Min, 30, Blue), (30, 60, Yellow), (60, LeashVideoCap.Max, Red) })
        {
            Band(a, Math.Min(b, _ceiling), c, 0.28);
            if (b > _ceiling) Band(Math.Max(a, _ceiling), b, c, 0.08);
        }

        // The fill to the value, each band in its own full colour.
        foreach (var (a, b, c) in new[] { (LeashVideoCap.Min, 30, Blue), (30, 60, Yellow), (60, LeashVideoCap.Max, Red) })
            if (_value > a) Band(a, Math.Min(b, _value), c, 1.0);

        var thumb = new Ellipse
        {
            Width = ThumbD, Height = ThumbD,
            Fill = FriendsLook.Frozen(ColorOf(_value)),
            Stroke = LeashLook.InkBrush, StrokeThickness = 2,
        };
        Canvas.SetLeft(thumb, XOf(_value) - ThumbD / 2);
        Canvas.SetTop(thumb, (_canvas.Height - ThumbD) / 2);
        _canvas.Children.Add(thumb);
    }
}
