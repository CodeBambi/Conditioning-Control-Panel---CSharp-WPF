using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Services.BackRoom.Overlays;

/// <summary>
/// Hypno v3 <c>wash</c> (CONTRACT 10.13.B): a soft colour wash over the whole virtual screen, up over
/// 80 ms and decaying at 4.5/s, gone at 900 ms, never white (the dispatcher already capped the colour
/// and dropped anything inside 360 ms of the previous wash). An optional dealt picture sits in the
/// middle of the room window's screen at 42% of its height. With a picture the wash's clock starts
/// when the picture's first frame is on (or after a short wait without it), so a slow decode never
/// leaves an empty wash. The caller's <c>shown</c> runs only when the picture itself goes on (its first
/// frame in time), never for a refused or picture-less wash.
/// </summary>
internal sealed class BackRoomWashOverlay : BackRoomOverlayWindow
{
    /// <summary>How long a wash waits for its picture before it plays without one.</summary>
    private const int PictureWaitMs = 400;

    private static BackRoomWashOverlay? _instance;

    private readonly Rectangle _fill = new() { IsHitTestVisible = false };
    private readonly Border _box = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly Image _picture = new() { Stretch = Stretch.UniformToFill };
    private long _startedAt, _waitSince;
    private double _peak;
    private PxRect? _pictureScreenPx;
    private bool _active, _hasPicture;

    private BackRoomWashOverlay() : base(frameMs: 33, zRank: 2)
    {
        _box.Child = _picture;
        Stage.Children.Add(_fill);
        Stage.Children.Add(_box);
        Clear();
    }

    public static void Show(FxRgb color, double peak, string? picturePath, PxRect? pictureScreenPx, Action? shown = null) => OnUi(() =>
    {
        if (!MayCreate(_instance)) return;
        (_instance ??= new BackRoomWashOverlay()).Start(color, peak, picturePath, pictureScreenPx, shown);
    });

    public static void Stop() => OnUi(() => { if (_instance is { _active: true } w) { w.Clear(); w.Sleep(); } });

    private void Start(FxRgb color, double peak, string? picturePath, PxRect? screenPx, Action? shown)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        _fill.Fill = brush;
        _peak = Math.Clamp(peak, 0, BackRoomFxPlan.WashPeak);
        _fill.Opacity = _box.Opacity = 0;
        _hasPicture = false;
        _active = true;
        Wake();   // first, so the layout below reads this window's own DPI
        _pictureScreenPx = picturePath != null ? screenPx : null;
        OnRescaled();
        if (_pictureScreenPx == null)
        {
            ClearPicture(_picture);
            _startedAt = Environment.TickCount64;
            return;
        }
        _startedAt = 0;
        _waitSince = Environment.TickCount64;
        SetPicture(_picture, picturePath!, maxDim: 640, maxFrames: 40, budgetMb: 32, ok =>
        {
            if (!_active || _startedAt != 0) return;   // it already went on without the picture
            _hasPicture = ok;
            _startedAt = Environment.TickCount64;
            if (ok) shown?.Invoke();
        });
    }

    protected override void OnRescaled()
    {
        var surface = Surface;
        _fill.Width = surface.W;
        _fill.Height = surface.H;
        if (_pictureScreenPx is not { } px) return;
        var screen = Local(px);
        var box = BackRoomOverlayMath.WashPictureBox(screen.W, screen.H);
        Canvas.SetLeft(_box, screen.X + box.X);
        Canvas.SetTop(_box, screen.Y + box.Y);
        _box.Width = box.W;
        _box.Height = box.H;
    }

    protected override bool Frame(long nowMs, long dtMs)
    {
        if (!_active) return false;
        if (_startedAt == 0)
        {
            if (nowMs - _waitSince < PictureWaitMs) return true;
            _startedAt = nowMs;   // no picture in time: the wash goes on without it
        }
        double age = nowMs - _startedAt;
        double env = BackRoomOverlayMath.WashEnvelope(age);
        if (age >= BackRoomFxPlan.WashMs) { Clear(); return false; }
        _fill.Opacity = env * _peak;
        _box.Opacity = _hasPicture ? BackRoomOverlayMath.WashPictureAlpha(env, _peak) : 0;
        return true;
    }

    private void Clear()
    {
        _active = false;
        _fill.Opacity = 0;
        _box.Opacity = 0;
        _pictureScreenPx = null;
        ClearPicture(_picture);
    }
}
