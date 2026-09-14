using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Services.BackRoom.Overlays;

/// <summary>
/// Hypno v3 <c>wash</c> (CONTRACT 10.13.B): a soft colour wash over the whole virtual screen, up over
/// 80 ms and decaying at 4.5/s, gone at 900 ms, never white (the dispatcher already capped the colour
/// and dropped anything inside 360 ms of the previous wash). An optional dealt picture sits in the
/// middle of the room window's screen at 42% of its height.
/// </summary>
internal sealed class BackRoomWashOverlay : BackRoomOverlayWindow
{
    private static BackRoomWashOverlay? _instance;

    private readonly Rectangle _fill = new() { IsHitTestVisible = false };
    private readonly Border _box = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly Image _picture = new() { Stretch = Stretch.UniformToFill };
    private long _startedAt;
    private double _peak;
    private bool _active, _hasPicture;

    private BackRoomWashOverlay() : base(frameMs: 33)
    {
        _fill.Width = Width;
        _fill.Height = Height;
        _box.Child = _picture;
        Stage.Children.Add(_fill);
        Stage.Children.Add(_box);
        Clear();
    }

    public static void Show(FxRgb color, double peak, string? picturePath, PxRect? pictureScreenPx) => OnUi(() =>
    {
        if (!MayCreate(_instance)) return;
        (_instance ??= new BackRoomWashOverlay()).Start(color, peak, picturePath, pictureScreenPx);
    });

    public static void Stop() => OnUi(() => { if (_instance is { _active: true } w) { w.Clear(); w.Sleep(); } });

    private void Start(FxRgb color, double peak, string? picturePath, PxRect? screenPx)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        _fill.Fill = brush;
        _peak = Math.Clamp(peak, 0, BackRoomFxPlan.WashPeak);
        _startedAt = Environment.TickCount64;
        _hasPicture = picturePath != null && screenPx != null;
        if (_hasPicture)
        {
            var screen = Local(screenPx!.Value);
            var box = BackRoomOverlayMath.WashPictureBox(screen.W, screen.H);
            Canvas.SetLeft(_box, screen.X + box.X);
            Canvas.SetTop(_box, screen.Y + box.Y);
            _box.Width = box.W;
            _box.Height = box.H;
            SetPicture(_picture, picturePath!, still: false, maxDim: 720);
        }
        else ClearPicture(_picture);
        _active = true;
        Wake();
    }

    protected override bool Frame(long nowMs, long dtMs)
    {
        if (!_active) return false;
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
        ClearPicture(_picture);
    }
}
