using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Games.BackRoom;

/// <summary>One screen of a Back Room overlay: borderless, transparent, topmost, never activated, with a
/// free stage. The host makes it click-through after Show, or never keeps it (WPF BackRoomOverlayWindow).</summary>
internal sealed class RoomOverlayWindow : Window
{
    internal readonly Canvas Stage = new() { ClipToBounds = true, IsHitTestVisible = false };

    public RoomOverlayWindow()
    {
        SystemDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = 640;
        Height = 400;
        Content = Stage;
    }

    internal bool Primary { get; private set; }

    /// <summary>The monitor's scale (physical px per DIP) and its physical bounds, for mapping a desktop rect in.</summary>
    internal double Scale { get; private set; } = 1;
    internal Rect ScreenPx { get; private set; }

    /// <summary>Fills exactly one monitor (Position is physical, Width / Height are DIPs).</summary>
    public void PlaceOn(Screen screen)
    {
        var b = screen.Bounds;
        var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
        Position = new PixelPoint(b.X, b.Y);
        Width = b.Width / scale;
        Height = b.Height / scale;
        Primary = screen.IsPrimary;
        Scale = scale;
        ScreenPx = new Rect(b.X, b.Y, b.Width, b.Height);
    }
}

/// <summary>A decoded picture: the animated lane's frames (two or more) or one still.</summary>
internal sealed record RoomPicture(List<Bitmap> Frames, TimeSpan Delay);

/// <summary>The curves the Hypno v3 overlays sample each frame (WPF BackRoomOverlayMath, same numbers).</summary>
internal static class RoomOverlayMath
{
    public const double CentreBoxW = 60, CentreBoxH = 44;
    public const int GrowMs = 700, GifFadeMs = 900;
    public const double DimAlpha = 0.55;
    public const double WashPictureHeight = 0.42;
    public const int SpiralFadeInMs = 1250, SpiralFadeOutMs = 500;
    /// <summary>WPF ChaosFlashOverlay: a full picture comes up over 500 ms and leaves over 700 ms.</summary>
    public const int FullInMs = 500, FullOutMs = 700, FullMinMs = 600;

    public static double WashPictureAlpha(double envelope, double peak)
        => Math.Clamp(Math.Min(1, envelope * 1.3) * 0.85 * Math.Clamp(peak / BackRoomFxPlan.WashPeak, 0, 1), 0, 1);

    public static Rect WashPictureBox(double w, double h)
    {
        double bh = h * WashPictureHeight, bw = bh * 4 / 3;
        return new Rect((w - bw) / 2, (h - bh) / 2, bw, bh);
    }

    public static double EaseInOut(double p)
    {
        p = Math.Clamp(p, 0, 1);
        return p < 0.5 ? 2 * p * p : 1 - Math.Pow(-2 * p + 2, 2) / 2;
    }

    /// <summary>The picture's box, its alpha and the dim behind it at <paramref name="ageMs"/>: it grows from
    /// <paramref name="from"/> to a centred box covering the screen over 700 ms and fades over the last 900 ms.</summary>
    public static (Rect Image, double ImageAlpha, double DimAlpha) GifFrom(double ageMs, int durationMs, Rect from, double w, double h,
        double aspect, double scale, double dimLevel)
    {
        if (ageMs < 0 || ageMs >= durationMs) return (from, 0, 0);
        double ar = aspect > 0.05 && double.IsFinite(aspect) ? aspect : 4.0 / 3;
        double tw = Math.Max(w, h * ar) * scale, th = tw / ar;
        var cover = new Rect((w - tw) / 2, (h - th) / 2, tw, th);
        double grow = EaseInOut(ageMs / GrowMs);
        double fade = ageMs > durationMs - GifFadeMs ? Math.Max(0, (durationMs - ageMs) / GifFadeMs) : 1;
        var image = new Rect(Lerp(from.X, cover.X, grow), Lerp(from.Y, cover.Y, grow), Lerp(from.Width, cover.Width, grow), Lerp(from.Height, cover.Height, grow));
        double dim = scale >= 1 ? DimAlpha * fade * (0.55 + 0.45 * grow) * dimLevel : 0;
        return (image, Math.Clamp(fade, 0, 1), Math.Clamp(dim, 0, 1));
    }

    /// <summary>0..1: in over 1250 ms (smoothstep), out over 500 ms from the hold's end or a release.</summary>
    public static double SpiralEnvelope(double ageMs, int holdMs, double? releasedAtAgeMs)
    {
        if (ageMs < 0) return 0;
        double a = Math.Min(1, ageMs / SpiralFadeInMs);
        a = a * a * (3 - 2 * a);
        double outFrom = releasedAtAgeMs is { } r ? Math.Min(r, holdMs) : holdMs;
        if (ageMs > outFrom) a *= 1 - Math.Min(1, (ageMs - outFrom) / SpiralFadeOutMs);
        return Math.Clamp(a, 0, 1);
    }

    public static bool SpiralDone(double ageMs, int holdMs, double? releasedAtAgeMs)
        => ageMs >= (releasedAtAgeMs is { } r ? Math.Min(r, holdMs) : holdMs) + SpiralFadeOutMs;

    /// <summary>A full picture's alpha: up to <paramref name="peak"/> over 500 ms, held, then out over 700 ms.</summary>
    public static double FullEnvelope(double ageMs, int lifeMs, double peak)
    {
        if (ageMs < 0 || ageMs >= lifeMs + FullOutMs) return 0;
        double a = Math.Min(1, ageMs / FullInMs);
        if (ageMs > lifeMs) a *= 1 - (ageMs - lifeMs) / FullOutMs;
        return Math.Clamp(a * peak, 0, 1);
    }

    /// <summary>Per screen, R = hypot(w, h) / 2: clear inside R(1 - 0.72k), darkest at R(1.12 - 0.5k). The centre never closes.</summary>
    public static (double Inner, double Outer, double Alpha) Tunnel(double w, double h, double k)
    {
        k = Math.Clamp(k, 0, 1);
        double r = Math.Sqrt(w * w + h * h) / 2;
        return (r * (1 - 0.72 * k), r * (1.12 - 0.5 * k), 0.94 * Math.Min(1, 1.3 * k));
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

/// <summary>The tunnel's level over time (WPF TunnelModel): eases toward the wanted level at 1.4/s closing and
/// 2.2/s opening and lets go by itself when nobody refreshed the want for 1500 ms.</summary>
internal sealed class RoomTunnelModel
{
    public const double CloseRate = 1.4, OpenRate = 2.2;
    public const int StaleMs = 1500;
    public const double Epsilon = 0.01;
    private long _refreshedAt;

    public double Want { get; private set; }
    public double Level { get; private set; }
    public bool Idle => Want <= 0 && Level <= 0;

    public void Set(double level, long nowMs)
    {
        Want = double.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;
        _refreshedAt = nowMs;
    }

    public void Cancel() { Want = 0; Level = 0; }

    public double Step(long nowMs, double dtMs)
    {
        if (Want > 0 && nowMs - _refreshedAt > StaleMs) Want = 0;
        double rate = Want > Level ? CloseRate : OpenRate;
        Level += (Want - Level) * Math.Min(1, Math.Max(0, dtMs) / 1000 * rate);
        if (Want <= 0 && Level < Epsilon) Level = 0;
        return Level;
    }
}

/// <summary>
/// The Back Room's own overlay windows on this head (WPF Services/BackRoom/Overlays + the two
/// ChaosFlashOverlay instances the room borrows): the hero picture (gif-full), the glitch wash, the
/// picture that grows out of the page (gif-from), the picture inside a wash, tunnel vision and the
/// Loom spiral (on <see cref="SpiralOverlay"/>'s hold). Every one has an IN and an OUT, is driven by a
/// 33 ms timer that exists only while it is up, and is click-through or not shown at all.
/// </summary>
internal static class BackRoomOverlays
{
    /// <summary>Headless tests have no native window to make click-through. Never set outside tests.</summary>
    internal static bool SkipPlatformChecksForTests;

    private static bool _refused;
    private const int FrameMs = 33;
    private static long Now => Environment.TickCount64;

    // ---- windows ------------------------------------------------------------------------------

    /// <summary>One click-through window per screen, or null when the platform cannot make them
    /// click-through (refused for the session: a topmost full-screen window that eats clicks locks the desktop).</summary>
    internal static List<RoomOverlayWindow>? Open(Visual host)
    {
        if (_refused || (!SkipPlatformChecksForTests && !X11Overlay.IsAvailable)) return null;
        var list = new List<RoomOverlayWindow>();
        try
        {
            foreach (var screen in ScreenList.Enumerate(host))
            {
                var w = new RoomOverlayWindow();
                w.PlaceOn(screen);
                w.Show();
                if (!SkipPlatformChecksForTests
                    && (!X11Overlay.SetClickThrough(w, true) || w.ActualTransparencyLevel == WindowTransparencyLevel.None))
                {
                    _refused = true;
                    Log.Warning("[BackRoom] the platform refused a click-through overlay window, so the room's pictures are not shown");
                    try { w.Close(); } catch { }
                    Close(list);
                    return null;
                }
                list.Add(w);
            }
        }
        catch (Exception ex) { Log.Debug("[BackRoom] overlay open: {E}", ex.Message); Close(list); return null; }
        if (list.Count == 0) return null;
        return list;
    }

    private static RoomOverlayWindow PrimaryOf(List<RoomOverlayWindow> list) => list.FirstOrDefault(w => w.Primary) ?? list[0];

    private static void Close(List<RoomOverlayWindow>? list)
    {
        if (list == null) return;
        foreach (var w in list) { try { w.Close(); } catch { } }
        list.Clear();
    }

    // ---- pictures -----------------------------------------------------------------------------

    /// <summary>Decodes a local picture off the UI thread: the animated lane first (a GIF is a clip, never a
    /// poster), else one still. Tests swap it.</summary>
    internal static Func<string, int, RoomPicture?> LoadPicture = DefaultLoad;

    private static RoomPicture? DefaultLoad(string path, int longSide)
    {
        try
        {
            int w, h;
            using (var codec = SKCodec.Create(path))
            {
                if (codec == null) return null;
                (w, h) = (codec.Info.Width, codec.Info.Height);
            }
            if (w <= 0 || h <= 0) return null;
            double k = Math.Min(1, longSide / (double)Math.Max(w, h));
            int tw = Math.Max(2, (int)Math.Round(w * k)), th = Math.Max(2, (int)Math.Round(h * k));
            if (FlashGifFrames.Decode(path, tw, th) is { } anim) return new RoomPicture(anim.Frames, anim.FrameDelay);
            using var stream = File.OpenRead(path);
            return new RoomPicture(new List<Bitmap> { Bitmap.DecodeToWidth(stream, tw) }, TimeSpan.Zero);
        }
        catch (Exception ex) { Log.Debug("[BackRoom] picture decode failed: {E}", ex.Message); return null; }
    }

    /// <summary>The decode in flight, if any (tests await it).</summary>
    internal static Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>One effect on screen: its windows, its clock, its picture. Stop is the whole teardown.</summary>
    private sealed class Run
    {
        public List<RoomOverlayWindow>? Windows;
        public DispatcherTimer? Clock, FrameClock;
        public RoomPicture? Picture;
        public readonly List<Image> Images = new();
        public long Start = Now;
        private int _frame;

        public void Animate()
        {
            if (Picture is not { } p || p.Frames.Count == 0) return;
            foreach (var i in Images) i.Source = p.Frames[0];
            if (p.Frames.Count < 2) return;
            FrameClock = new DispatcherTimer(DispatcherPriority.Render) { Interval = p.Delay > TimeSpan.Zero ? p.Delay : TimeSpan.FromMilliseconds(100) };
            FrameClock.Tick += (_, _) =>
            {
                _frame = (_frame + 1) % p.Frames.Count;
                foreach (var i in Images) i.Source = p.Frames[_frame];
            };
            FrameClock.Start();
        }

        public void Every(Action<double> frame)
        {
            Clock = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(FrameMs) };
            Clock.Tick += (_, _) => frame(Now - Start);
            Clock.Start();
            frame(0);
        }

        public void Stop()
        {
            try { Clock?.Stop(); FrameClock?.Stop(); } catch { }
            Clock = FrameClock = null;
            foreach (var i in Images) i.Source = null;
            Images.Clear();
            Close(Windows);
            Windows = null;
            if (Picture is { } p) foreach (var f in p.Frames) { try { f.Dispose(); } catch { } }
            Picture = null;
        }
    }

    // One slot per primitive: a new fire replaces what the slot shows; the generation drops a decode that lands late.
    private static readonly Dictionary<string, Run> Slots = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> Gens = new(StringComparer.Ordinal);
    internal const string Hero = "hero", Glitch = "glitch", From = "from", WashPicture = "wash", TunnelSlot = "tunnel";

    internal static bool IsUp(string slot) => Slots.ContainsKey(slot);
    internal static IReadOnlyList<RoomOverlayWindow> WindowsOf(string slot) =>
        Slots.TryGetValue(slot, out var r) && r.Windows != null ? r.Windows : Array.Empty<RoomOverlayWindow>();
    internal static IReadOnlyList<Image> ImagesOf(string slot) =>
        Slots.TryGetValue(slot, out var r) ? r.Images : Array.Empty<Image>();

    internal static void StopSlot(string slot)
    {
        Gens[slot] = Gens.GetValueOrDefault(slot) + 1;
        if (Slots.Remove(slot, out var run)) run.Stop();
    }

    /// <summary>Decode, then build on the UI thread when the slot was not stopped or refired meanwhile.</summary>
    private static void Begin(string slot, Visual host, string path, int longSide, Action<Run> build, Action? shown)
    {
        StopSlot(slot);
        int gen = Gens[slot];
        Loading = Go();

        async Task Go()
        {
            RoomPicture? picture = null;
            try
            {
                picture = await Task.Run(() => LoadPicture(path, longSide));
                if (picture == null || picture.Frames.Count == 0) return;
                if (Gens.GetValueOrDefault(slot) != gen) { Drop(picture); return; }
                var windows = Open(host);
                if (windows == null) { Drop(picture); return; }
                var run = new Run { Windows = windows, Picture = picture };
                Slots[slot] = run;
                build(run);
                run.Animate();
                try { shown?.Invoke(); } catch (Exception ex) { Log.Debug("[BackRoom] shown: {E}", ex.Message); }
            }
            catch (Exception ex)
            {
                Log.Debug("[BackRoom] {Slot} overlay: {E}", slot, ex.Message);
                if (Slots.TryGetValue(slot, out var r) && ReferenceEquals(r.Picture, picture)) StopSlot(slot); else Drop(picture);
            }
        }

        static void Drop(RoomPicture? p)
        {
            if (p == null) return;
            foreach (var f in p.Frames) { try { f.Dispose(); } catch { } }
        }
    }

    /// <summary>gif-full and the glitch wash: the picture covering every screen, up over 500 ms to
    /// <paramref name="opacity"/>, down over 700 ms after <paramref name="durationMs"/>.</summary>
    internal static void Full(string slot, Visual host, string path, int durationMs, double opacity, Action? shown)
    {
        double peak = Math.Clamp(opacity, 0.02, 1.0);
        int life = Math.Max(RoomOverlayMath.FullMinMs, durationMs);
        Begin(slot, host, path, 1280, run =>
        {
            foreach (var w in run.Windows!)
            {
                var image = new Image { Stretch = Stretch.UniformToFill, Width = w.Width, Height = w.Height, Opacity = 0 };
                w.Stage.Children.Add(image);
                run.Images.Add(image);
            }
            run.Every(age =>
            {
                if (age >= life + RoomOverlayMath.FullOutMs) { StopSlot(slot); return; }
                double a = RoomOverlayMath.FullEnvelope(age, life, peak);
                foreach (var i in run.Images) i.Opacity = a;
            });
        }, shown);
    }

    /// <summary>The rect the last gif-from started from, in its stage's DIPs, and whether it was a centre box (tests).</summary>
    internal static (Rect From, bool Centred, bool Primary) LastFrom { get; private set; }

    /// <summary>gif-from: the picture grows out of the page's rect (WPF MapFrom: mapped through the room's
    /// viewport onto the screen the room is on; a centre box when the rect or the viewport is missing) to cover
    /// that screen while every screen dims, then fades.</summary>
    internal static void GifFrom(Visual host, string path, double aspect, int durationMs, double scale, double dim, Action? shown,
        FxCssRect? css = null, RoomViewport? viewport = null)
    {
        Begin(From, host, path, 1280, run =>
        {
            var dims = new List<(Border Fill, SolidColorBrush Brush)>();
            foreach (var w in run.Windows!)
            {
                var brush = new SolidColorBrush(Color.FromArgb(0, 8, 4, 14));
                var fill = new Border { Width = w.Width, Height = w.Height, Background = brush };
                w.Stage.Children.Add(fill);
                dims.Add((fill, brush));
            }
            // Physical px on the desktop -> the DIPs of the stage on that screen.
            var target = BackRoomFromMap.MapFrom(css, viewport, run.Windows.Select(w => (w.ScreenPx, w.Primary)).ToList());
            var stage = target.ScreenIndex >= 0 ? run.Windows[target.ScreenIndex] : PrimaryOf(run.Windows);
            var image = new Image { Stretch = Stretch.Fill, Opacity = 0 };
            stage.Stage.Children.Add(image);
            run.Images.Add(image);
            double sw = stage.Width, sh = stage.Height, k = stage.Scale > 0 ? stage.Scale : 1;
            var from = target.ScreenIndex >= 0 && target.RectPx.Width > 0
                ? new Rect((target.RectPx.X - stage.ScreenPx.X) / k, (target.RectPx.Y - stage.ScreenPx.Y) / k, target.RectPx.Width / k, target.RectPx.Height / k)
                : new Rect((sw - RoomOverlayMath.CentreBoxW) / 2, (sh - RoomOverlayMath.CentreBoxH) / 2, RoomOverlayMath.CentreBoxW, RoomOverlayMath.CentreBoxH);
            LastFrom = (from, target.Centred, stage.Primary);
            run.Every(age =>
            {
                if (age >= durationMs) { StopSlot(From); return; }
                var f = RoomOverlayMath.GifFrom(age, durationMs, from, sw, sh, aspect, scale, dim);
                Canvas.SetLeft(image, f.Image.X);
                Canvas.SetTop(image, f.Image.Y);
                image.Width = Math.Max(1, f.Image.Width);
                image.Height = Math.Max(1, f.Image.Height);
                image.Opacity = f.ImageAlpha;
                byte alpha = (byte)Math.Round(255 * f.DimAlpha);
                foreach (var d in dims) d.Brush.Color = Color.FromArgb(alpha, 8, 4, 14);
            });
        }, shown);
    }

    /// <summary>The picture in the middle of a wash: a 4:3 box, 42% of the primary screen's height, on the wash's own envelope.</summary>
    internal static void WashWithPicture(Visual host, string path, double peak, Func<double, double> envelope, Action? shown)
    {
        Begin(WashPicture, host, path, 720, run =>
        {
            var stage = PrimaryOf(run.Windows!);
            var box = RoomOverlayMath.WashPictureBox(stage.Width, stage.Height);
            var image = new Image { Stretch = Stretch.UniformToFill, Width = box.Width, Height = box.Height, Opacity = 0 };
            var clip = new Border { Width = box.Width, Height = box.Height, ClipToBounds = true, Child = image };
            Canvas.SetLeft(clip, box.X);
            Canvas.SetTop(clip, box.Y);
            stage.Stage.Children.Add(clip);
            run.Images.Add(image);
            run.Every(age =>
            {
                if (age >= BackRoomFxPlan.WashMs) { StopSlot(WashPicture); return; }
                image.Opacity = RoomOverlayMath.WashPictureAlpha(envelope(age), peak);
            });
        }, shown);
    }

    // ---- tunnel vision ------------------------------------------------------------------------

    private static readonly RoomTunnelModel TunnelModel = new();
    private static readonly Color Ink = Color.FromRgb(6, 3, 12);   // WPF BackRoomTunnelOverlay.Ink
    private static long _tunnelLast;

    internal static double TunnelLevel => TunnelModel.Level;

    /// <summary>The wanted level; the vignette eases toward it and lets go 1500 ms after the last call.</summary>
    internal static void Tunnel(Visual host, double level)
    {
        if (level <= 0 && (!Slots.ContainsKey(TunnelSlot) || TunnelModel.Idle)) return;
        TunnelModel.Set(level, Now);
        if (Slots.ContainsKey(TunnelSlot)) return;
        var windows = Open(host);
        if (windows == null) { TunnelModel.Cancel(); return; }
        var run = new Run { Windows = windows };
        Slots[TunnelSlot] = run;
        var parts = new List<(Border Fill, RadialGradientBrush Brush, GradientStop Clear, GradientStop Dark, double W, double H)>();
        foreach (var w in windows)
        {
            var clear0 = new GradientStop(Color.FromArgb(0, Ink.R, Ink.G, Ink.B), 0);
            var clear = new GradientStop(Color.FromArgb(0, Ink.R, Ink.G, Ink.B), 0);
            var dark = new GradientStop(Color.FromArgb(0, Ink.R, Ink.G, Ink.B), 1);
            var brush = new RadialGradientBrush
            {
                Center = new RelativePoint(w.Width / 2, w.Height / 2, RelativeUnit.Absolute),
                GradientOrigin = new RelativePoint(w.Width / 2, w.Height / 2, RelativeUnit.Absolute),
                GradientStops = { clear0, clear, dark },
            };
            var fill = new Border { Width = w.Width, Height = w.Height, Background = brush, Opacity = 0 };
            w.Stage.Children.Add(fill);
            parts.Add((fill, brush, clear, dark, w.Width, w.Height));
        }
        _tunnelLast = Now;
        run.Every(_ =>
        {
            long now = Now;
            double level = TunnelModel.Step(now, now - _tunnelLast);
            _tunnelLast = now;
            if (TunnelModel.Idle) { StopSlot(TunnelSlot); return; }
            foreach (var p in parts)
            {
                if (level < RoomTunnelModel.Epsilon) { p.Fill.Opacity = 0; continue; }
                var shape = RoomOverlayMath.Tunnel(p.W, p.H, level);
                p.Brush.RadiusX = p.Brush.RadiusY = new RelativeScalar(shape.Outer, RelativeUnit.Absolute);
                p.Clear.Offset = shape.Outer > 0 ? Math.Clamp(shape.Inner / shape.Outer, 0, 1) : 0;
                p.Dark.Color = Color.FromArgb((byte)Math.Round(255 * Math.Clamp(shape.Alpha, 0, 1)), Ink.R, Ink.G, Ink.B);
                p.Fill.Opacity = 1;
            }
        });
    }

    /// <summary>Gone at once (suspend, close, exit, station-close).</summary>
    internal static void CancelTunnel()
    {
        TunnelModel.Cancel();
        StopSlot(TunnelSlot);
    }

    // ---- the Loom spiral, on the spiral overlay's hold ------------------------------------------

    private static DispatcherTimer? _spiralClock;
    private static long _spiralStart;
    private static double? _spiralReleasedAt;

    /// <summary>The app window a hold repaints from once the room itself is gone.</summary>
    private static Visual? Shell => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    internal static bool SpiralUp => _spiralClock != null;

    /// <summary>The woven spiral over every screen: in over 1250 ms to <paramref name="alpha"/>, out over 500 ms at
    /// <paramref name="durationMs"/> or on a release. <paramref name="slow"/> = half speed (reduced motion).</summary>
    internal static void Spiral(Visual host, string gifPath, int durationMs, double alpha, bool slow)
    {
        StopSpiralClock();
        _spiralStart = Now;
        _spiralReleasedAt = null;
        double peak = Math.Clamp(alpha, 0, 1);
        _spiralClock = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(FrameMs) };
        _spiralClock.Tick += (_, _) => Frame();
        _spiralClock.Start();
        Frame();

        void Frame()
        {
            double age = Now - _spiralStart;
            if (RoomOverlayMath.SpiralDone(age, durationMs, _spiralReleasedAt)) { StopSpiral(); return; }
            double a = RoomOverlayMath.SpiralEnvelope(age, durationMs, _spiralReleasedAt) * peak;
            SpiralOverlay.Hold(BackRoomFxHead.Host ?? host, SpiralOverlay.RoomOwner, new SpiralHold(a, gifPath, AllScreens: true, Slow: slow));
        }
    }

    /// <summary>A held spiral lets go: it fades out from now.</summary>
    internal static void ReleaseSpiral()
    {
        if (_spiralClock != null) _spiralReleasedAt ??= Now - _spiralStart;
    }

    private static void StopSpiralClock()
    {
        try { _spiralClock?.Stop(); } catch { }
        _spiralClock = null;
    }

    /// <summary>Down now, and the spiral goes back to the user's own switch.</summary>
    internal static void StopSpiral()
    {
        bool was = _spiralClock != null;
        StopSpiralClock();
        if (was || SpiralOverlay.IsHeldBy(SpiralOverlay.RoomOwner))
            SpiralOverlay.Release(SpiralOverlay.RoomOwner, BackRoomFxHead.Host ?? Shell);
    }

    /// <summary>Panic, suspend, close: every window this class opened goes now.</summary>
    internal static void StopAll()
    {
        foreach (var slot in Slots.Keys.Concat(Gens.Keys).Distinct().ToList()) StopSlot(slot);
        TunnelModel.Cancel();
        StopSpiral();
        GifCascadeOverlay.CloseOwned(RainOwner);   // the room's own rain; somebody else's keeps falling
    }

    /// <summary>The owner tag of a cascade the room started (<see cref="GifCascadeOverlay"/>).</summary>
    internal const string RainOwner = "backroom";
}
