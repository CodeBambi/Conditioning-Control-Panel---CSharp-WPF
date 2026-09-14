using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Services.BackRoom.Overlays;

/// <summary>
/// Hypno v3 <c>spiral-loom</c> (CONTRACT 10.13.B, owner law: every Back Room spiral is Loom-woven): one
/// woven spiral GIF over the whole virtual screen, in over 800 ms, out over 1200 ms when its time or the
/// 20 s hold cap runs out or it is released. A new spiral replaces the running one, which fades out on
/// its own layer while the new one fades in. Section 4's spiral-full plays here too.
///
/// <para>Frames decode off the UI thread at 640 px on the long side with every frame of the loop kept
/// (a woven loop is at most 72 frames, about 66 MB there), so the spiral turns as smoothly as the Loom
/// wove it; the decode is kept while the window is up and dropped when it hides after a quiet spell.
/// Still (MotionLevel Off) shows the first frame only.</para>
/// </summary>
internal sealed class BackRoomLoomSpiralOverlay : BackRoomOverlayWindow
{
    private const int DecodeLongSide = 640;
    private const int MaxLoopFrames = 72;
    private const double DecodeBudgetMb = 72;

    private static BackRoomLoomSpiralOverlay? _instance;

    private sealed class Layer
    {
        public readonly Image Image = new() { Stretch = Stretch.UniformToFill, IsHitTestVisible = false, Opacity = 0 };
        public object? Token;
        public long StartedAt;        // 0 until the picture is on
        public int HoldMs;
        public double Alpha;
        public long? ReleasedAt;
        public bool Active;
    }

    private readonly Layer[] _layers = { new(), new() };
    private int _current;
    private readonly Dictionary<string, (List<BitmapSource> Frames, TimeSpan Delay)> _decoded = new(StringComparer.OrdinalIgnoreCase);

    private BackRoomLoomSpiralOverlay() : base(frameMs: 33)
    {
        foreach (var l in _layers)
        {
            l.Image.Width = Width;
            l.Image.Height = Height;
            Stage.Children.Add(l.Image);
        }
    }

    public static void Show(string gifPath, int durationMs, double alpha, bool still) => OnUi(() =>
    {
        if (!MayCreate(_instance)) return;
        (_instance ??= new BackRoomLoomSpiralOverlay()).Start(gifPath, durationMs, alpha, still);
    });

    /// <summary>Fade the running spiral out now.</summary>
    public static void Release() => OnUi(() => _instance?.ReleaseCurrent());

    /// <summary>Gone now (suspend, close).</summary>
    public static void Stop() => OnUi(() =>
    {
        if (_instance is not { } w) return;
        foreach (var l in w._layers) w.End(l);
        w.Sleep();
    });

    private void Start(string path, int durationMs, double alpha, bool still)
    {
        ReleaseCurrent();
        _current = (_current + 1) % _layers.Length;
        var layer = _layers[_current];
        End(layer);
        var token = layer.Token = new object();
        layer.Active = true;
        layer.HoldMs = Math.Clamp(durationMs, 0, BackRoomFxPlan.HoldCapMs);
        layer.Alpha = Math.Clamp(alpha, 0, 1);
        Wake();

        if (_decoded.TryGetValue(path, out var hit)) { Attach(layer, hit, still); return; }
        Task.Run(() =>
        {
            (List<BitmapSource> Frames, TimeSpan Delay)? frames = null;
            BitmapSource? first = null;
            try
            {
                var d = AnimatedWebp.DecodeFrames(path, DecodeLongSide, MaxLoopFrames, DecodeBudgetMb);
                if (d is { } ok) frames = (ok.Frames, ok.FrameDelay);
                else first = DecodeStill(path);
            }
            catch (Exception ex) { App.Logger?.Debug("[BackRoom] spiral decode: {E}", ex.Message); }
            OnUi(() =>
            {
                if (!ReferenceEquals(layer.Token, token) || !layer.Active) return;
                if (frames is { } f) { _decoded[path] = f; Attach(layer, f, still); }
                else if (first != null) Attach(layer, (new List<BitmapSource> { first }, TimeSpan.FromMilliseconds(100)), still);
                else End(layer);
            });
        });
    }

    private void Attach(Layer layer, (List<BitmapSource> Frames, TimeSpan Delay) d, bool still)
    {
        layer.Image.BeginAnimation(Image.SourceProperty, null);
        layer.Image.Source = d.Frames[0];
        if (!still && d.Frames.Count > 1)
        {
            var anim = new ObjectAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(d.Delay.TotalMilliseconds * d.Frames.Count),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            for (int i = 0; i < d.Frames.Count; i++)
                anim.KeyFrames.Add(new DiscreteObjectKeyFrame(d.Frames[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(d.Delay.TotalMilliseconds * i))));
            anim.Freeze();
            layer.Image.BeginAnimation(Image.SourceProperty, anim);
        }
        // The fades count from the first frame on screen, so a slow decode never eats the fade in.
        layer.StartedAt = Environment.TickCount64;
    }

    private void ReleaseCurrent()
    {
        var l = _layers[_current];
        if (l.Active && l.ReleasedAt == null) l.ReleasedAt = Environment.TickCount64;
    }

    protected override bool Frame(long nowMs, long dtMs)
    {
        bool any = false;
        foreach (var l in _layers)
        {
            if (!l.Active) continue;
            if (l.StartedAt == 0)
            {
                // Still decoding: a release before the first frame means it never shows.
                if (l.ReleasedAt != null) End(l); else any = true;
                continue;
            }
            double age = nowMs - l.StartedAt;
            double? released = l.ReleasedAt is { } r ? Math.Max(0, r - l.StartedAt) : null;
            if (BackRoomOverlayMath.SpiralDone(age, l.HoldMs, released)) { End(l); continue; }
            l.Image.Opacity = BackRoomOverlayMath.SpiralEnvelope(age, l.HoldMs, released) * l.Alpha;
            any = true;
        }
        return any;
    }

    private void End(Layer l)
    {
        l.Active = false;
        l.Token = null;
        l.StartedAt = 0;
        l.ReleasedAt = null;
        l.Image.Opacity = 0;
        l.Image.BeginAnimation(Image.SourceProperty, null);
        l.Image.Source = null;
    }

    protected override void OnIdleHidden() => _decoded.Clear();

    private static BitmapSource? DecodeStill(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = DecodeLongSide;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        if (bmp.CanFreeze) bmp.Freeze();
        return bmp;
    }
}
