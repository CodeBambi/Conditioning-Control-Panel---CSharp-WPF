using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Services.BackRoom.Overlays;

/// <summary>
/// Hypno v3 <c>gif-from</c> (CONTRACT 10.13.B, the only new picture overlay): a dealt picture grows out of
/// a game object's rect to cover the screen holding the room window over 700 ms, holds, and fades over the
/// last 900 ms while that screen dims to 45% behind it (no dim below full scale, where it sits inside a
/// running spiral). Still (MotionLevel Off): no growth, full size, 300 ms fades, first frame. One at a time;
/// the dispatcher acks a second one busy.
/// </summary>
internal sealed class BackRoomGifFromOverlay : BackRoomOverlayWindow
{
    private static BackRoomGifFromOverlay? _instance;
    private static readonly SolidColorBrush DimBrush = Frozen(Color.FromRgb(8, 4, 14));

    private readonly Rectangle _dim = new() { Fill = DimBrush, IsHitTestVisible = false };
    private readonly Border _box = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
    private long _startedAt;
    private int _durationMs;
    private PxRect _screen, _from;
    private double _aspect, _scale, _dimLevel;
    private bool _still, _active;

    private BackRoomGifFromOverlay() : base(frameMs: 16)
    {
        _box.Child = _image;
        Stage.Children.Add(_dim);
        Stage.Children.Add(_box);
        Clear();
    }

    public static void Show(string path, double aspect, FxFromTarget target, int durationMs, double scale, double dim, bool still) => OnUi(() =>
    {
        if (target.ScreenIndex < 0 || !MayCreate(_instance)) return;
        (_instance ??= new BackRoomGifFromOverlay()).Start(path, aspect, target, durationMs, scale, dim, still);
    });

    public static void Stop() => OnUi(() => { if (_instance is { _active: true } w) { w.Clear(); w.Sleep(); } });

    private void Start(string path, double aspect, FxFromTarget target, int durationMs, double scale, double dim, bool still)
    {
        _screen = Local(target.ScreenPx);
        var start = Local(target.RectPx);
        // The maths works in the target screen's own frame.
        _from = start with { X = start.X - _screen.X, Y = start.Y - _screen.Y };
        _aspect = aspect;
        _scale = scale;
        _dimLevel = dim;
        _still = still;
        _durationMs = Math.Max(1, durationMs);
        _startedAt = Environment.TickCount64;

        Canvas.SetLeft(_dim, _screen.X);
        Canvas.SetTop(_dim, _screen.Y);
        _dim.Width = _screen.W;
        _dim.Height = _screen.H;
        // The growth is at most the screen's own long edge, so decode no larger than that.
        SetPicture(_image, path, still, maxDim: (int)Math.Clamp(Math.Max(target.ScreenPx.W, target.ScreenPx.H), 320, 1280));
        _active = true;
        Frame(_startedAt, 0);
        Wake();
    }

    protected override bool Frame(long nowMs, long dtMs)
    {
        if (!_active) return false;
        double age = nowMs - _startedAt;
        if (age >= _durationMs) { Clear(); return false; }
        var f = BackRoomOverlayMath.GifFrom(age, _durationMs, _from, _screen.W, _screen.H, _aspect, _scale, _dimLevel, _still);
        Canvas.SetLeft(_box, _screen.X + f.Image.X);
        Canvas.SetTop(_box, _screen.Y + f.Image.Y);
        _box.Width = Math.Max(1, f.Image.W);
        _box.Height = Math.Max(1, f.Image.H);
        _box.Opacity = f.ImageAlpha;
        _dim.Opacity = f.DimAlpha;
        return true;
    }

    private void Clear()
    {
        _active = false;
        _box.Opacity = 0;
        _dim.Opacity = 0;
        ClearPicture(_image);
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
