using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace ConditioningControlPanel.Services.BackRoom.Overlays;

/// <summary>
/// Hypno v3 <c>spiral-loom</c> (CONTRACT 10.13.B, owner law: every Back Room spiral is Loom-woven): one
/// woven spiral GIF as one field over the whole virtual screen (UniformToFill, so on a multi-monitor
/// desktop its eye sits at the middle of the whole desktop), in over 800 ms, out over 1200 ms when its
/// time or the 20 s hold cap runs out or it is released. A new spiral replaces the running one, which
/// fades out on its own layer while the new one fades in; a third layer keeps a quick third spiral from
/// cutting a fade short. Section 4's spiral-full plays here too.
///
/// <para>Frames decode off the UI thread with every frame of the loop kept (a woven loop is at most 72
/// frames): 640 px on the long side, less for a squarer weave so all 72 fit the budget. One decode is
/// kept while the window is up (a second Start on the same path waits for the running decode) and
/// dropped when it hides after a quiet spell. Still (MotionLevel Off) decodes and shows the first frame
/// only.</para>
/// </summary>
internal sealed class BackRoomLoomSpiralOverlay : BackRoomOverlayWindow
{
    private const int DecodeLongSide = 640, DecodeMinSide = 360;
    private const int MaxLoopFrames = 72;
    private const double DecodeBudgetMb = 72;

    private static BackRoomLoomSpiralOverlay? _instance;

    private sealed class Layer
    {
        public readonly Image Image = new() { Stretch = Stretch.UniformToFill, IsHitTestVisible = false, Opacity = 0 };
        public object? Token;
        public string? Path;
        public bool Still;
        public long StartedAt;        // 0 until the picture is on
        public int HoldMs;
        public double Alpha;
        public long? ReleasedAt;
        public bool Active;
    }

    private readonly Layer[] _layers = { new(), new(), new() };
    private int _current;
    private (string Path, List<BitmapSource> Frames, TimeSpan Delay)? _decoded;
    private readonly HashSet<string> _decoding = new(StringComparer.OrdinalIgnoreCase);

    private BackRoomLoomSpiralOverlay() : base(frameMs: 33, zRank: 0)
    {
        foreach (var l in _layers) Stage.Children.Add(l.Image);
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
        // An idle layer if there is one, else the faintest fading one (never a cut on a bright layer).
        var others = _layers.Where((_, i) => i != _current).ToList();
        var layer = others.FirstOrDefault(l => !l.Active) ?? others.OrderBy(l => l.Image.Opacity).First();
        _current = Array.IndexOf(_layers, layer);
        End(layer);
        var token = layer.Token = new object();
        layer.Active = true;
        layer.Path = path;
        layer.Still = still;
        layer.HoldMs = Math.Clamp(durationMs, 0, BackRoomFxPlan.HoldCapMs);
        layer.Alpha = Math.Clamp(alpha, 0, 1);
        Wake();   // first, so the layers are sized in this window's own DPI
        OnRescaled();

        if (_decoded is { } hit && string.Equals(hit.Path, path, StringComparison.OrdinalIgnoreCase)) { Attach(layer, hit.Frames, hit.Delay); return; }
        if (still)
        {
            Task.Run(() =>
            {
                BitmapSource? first = null;
                try { first = DecodeStill(path, DecodeLongSide); }
                catch (Exception ex) { App.Logger?.Debug("[BackRoom] spiral still decode: {E}", ex.Message); }
                OnUi(() =>
                {
                    if (!ReferenceEquals(layer.Token, token)) return;
                    if (first != null) Attach(layer, new List<BitmapSource> { first }, TimeSpan.Zero); else End(layer);
                });
            });
            return;
        }
        if (!_decoding.Add(path)) return;   // the running decode attaches this layer too
        Task.Run(() =>
        {
            (List<BitmapSource> Frames, TimeSpan Delay)? frames = null;
            BitmapSource? first = null;
            try
            {
                var d = AnimatedWebp.DecodeFrames(path, FitLongSide(Probe(path)), MaxLoopFrames, DecodeBudgetMb);
                if (d is { } ok) frames = (ok.Frames, ok.FrameDelay);
                else first = DecodeStill(path, DecodeLongSide);
            }
            catch (Exception ex) { App.Logger?.Debug("[BackRoom] spiral decode: {E}", ex.Message); }
            OnUi(() =>
            {
                _decoding.Remove(path);
                if (frames is { } f) _decoded = (path, f.Frames, f.Delay);
                foreach (var l in _layers.Where(l => l.Active && l.StartedAt == 0 && l.Path == path))
                {
                    if (frames is { } ff) Attach(l, ff.Frames, ff.Delay);
                    else if (first != null) Attach(l, new List<BitmapSource> { first }, TimeSpan.Zero);
                    else End(l);
                }
            });
        });
    }

    /// <summary>(width, height, frames) of a weave, or null. Cheap: the header only, under the shared gate.</summary>
    private static (int W, int H, int Frames)? Probe(string path) => AnimatedWebp.RunGatedDecode(() =>
    {
        using var codec = SKCodec.Create(path);
        return codec == null ? ((int, int, int)?)null : (codec.Info.Width, codec.Info.Height, codec.FrameCount);
    });

    /// <summary>The long side to decode at so every frame of the loop fits the budget: 640, less for a squarer
    /// (or longer) weave, never under 360.</summary>
    internal static int FitLongSide((int W, int H, int Frames)? info)
    {
        if (info is not { W: > 0, H: > 0 } i) return DecodeLongSide;
        double longest = Math.Max(i.W, i.H), k = Math.Min(1, DecodeLongSide / longest);
        double bytes = i.W * k * (i.H * k) * 4 * Math.Clamp(i.Frames, 1, MaxLoopFrames);
        double budget = DecodeBudgetMb * 1024 * 1024 * 0.98;   // a little under, so rounding never drops a frame
        int side = (int)Math.Min(DecodeLongSide, longest);
        if (bytes > budget) side = (int)Math.Floor(side * Math.Sqrt(budget / bytes));
        return Math.Max(DecodeMinSide, side);
    }

    private void Attach(Layer layer, List<BitmapSource> frames, TimeSpan delay)
    {
        PlayFrames(layer.Image, frames, delay, layer.Still);
        // The fades count from the first frame on screen, so a slow decode never eats the fade in.
        layer.StartedAt = Environment.TickCount64;
    }

    protected override void OnRescaled()
    {
        var surface = Surface;
        foreach (var l in _layers)
        {
            l.Image.Width = surface.W;
            l.Image.Height = surface.H;
        }
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
        l.Path = null;
        l.StartedAt = 0;
        l.ReleasedAt = null;
        l.Image.Opacity = 0;
        l.Image.BeginAnimation(Image.SourceProperty, null);
        l.Image.Source = null;
    }

    protected override void OnIdleHidden() => _decoded = null;
}
