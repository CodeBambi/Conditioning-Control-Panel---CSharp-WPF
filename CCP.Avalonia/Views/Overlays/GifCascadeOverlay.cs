using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games.BackRoom;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>
/// The gif rain (WPF Chaos/ChaosGifCascadeOverlay): pictures from the flash pool spawn above the top
/// of the screen on a timer, slide down growing from <c>startScale</c> to full size, and leave off
/// the bottom. Click-through, on the Back Room's overlay windows (<see cref="BackRoomOverlays.Open"/>:
/// one per screen, or nothing at all for the session when the platform cannot make them
/// click-through). The Back Room's <c>gif-rain</c> primitive is the first caller; anything else that
/// wants a cascade calls <see cref="Show"/> with its own owner tag.
///
/// <para>IN = a clip enters from above the top edge, OUT = it leaves below the bottom edge and the
/// windows close once the last one is gone. One 33 ms DispatcherTimer drives the spawn clock, the
/// spawn window, the fall and the animated frames, and exists only while it rains. Motion and growth
/// are render transforms set per tick (no layout pass, no Avalonia <c>Animation</c>, no Effect).</para>
///
/// <para>Decode budget (WPF numbers): at most <see cref="MaxConcurrent"/> clips alive, at most
/// <see cref="MaxAnimated"/> of them animating, and only files up to <see cref="AnimatedMaxBytes"/>
/// animate; the rest fall as display-size stills. Every decode runs off the UI thread. A GIF means
/// the animated lane (never a poster) while the budget has room. A picture is known by its whole
/// path, never by its file name.</para>
///
/// <para>Not on this head: remote stills (WPF rains warm https stills when the media source is
/// online); an https entry in the pool is skipped here.</para>
/// </summary>
internal static class GifCascadeOverlay
{
    internal const int MaxConcurrent = 14;
    internal const int MaxAnimated = 3;
    internal const long AnimatedMaxBytes = 3_000_000;
    private const int FrameMs = 33;
    /// <summary>WPF fall speeds are DIPs per 16 ms frame; the tick scales by real elapsed time.</summary>
    private const double TunedFrameMs = 16.0;

    /// <summary>The pool a cascade draws from (WPF FlashService.GetChaosImagePaths: the enabled disk
    /// and pack pictures, honouring the asset manager). Tests swap it.</summary>
    internal static Func<int, List<string>> PickFiles = n => FlashOverlay.GetChaosImagePaths(n);

    /// <summary>Decode one clip off the UI thread: (path, display size, animate). Tests swap it.</summary>
    internal static Func<string, int, bool, RoomPicture?> Decode = DefaultDecode;

    /// <summary>The file size the animated budget reads. Tests swap it.</summary>
    internal static Func<string, long> FileLength = p => { try { return new FileInfo(p).Length; } catch { return 0; } };

    /// <summary>True when a path can animate at all (a gif, or a webp that carries frames).</summary>
    internal static Func<string, bool> CanAnimate = p =>
    {
        var ext = Path.GetExtension(p);
        if (ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)) return true;
        if (!ext.Equals(".webp", StringComparison.OrdinalIgnoreCase)) return false;
        try { return BackRoomMedia.ProbeAnimated(p).Ok; } catch { return false; }
    };

    private static RoomPicture? DefaultDecode(string path, int size, bool animate)
    {
        try
        {
            if (animate) return BackRoomOverlays.LoadPicture(path, size);
            using var stream = File.OpenRead(path);
            return new RoomPicture(new List<Bitmap> { Bitmap.DecodeToWidth(stream, Math.Max(2, size)) }, TimeSpan.Zero);
        }
        catch (Exception ex) { Log.Debug("GifCascade decode: {E}", ex.Message); return null; }
    }

    private sealed class Faller
    {
        public string Path = "";
        public Image Img = null!;
        public RoomOverlayWindow Window = null!;
        public TranslateTransform Move = null!;
        public ScaleTransform Grow = null!;
        public double Y, Speed;
        public bool Animated, Gone;
        public RoomPicture? Picture;
        public int Frame;
        public double FrameAge;
    }

    private static readonly Random Rng = new();
    private static readonly List<Faller> Fallers = new();
    private static List<RoomOverlayWindow>? _windows;
    private static List<string> _files = new();
    private static DispatcherTimer? _clock;
    private static long _last;
    private static double _spawnEveryMs, _spawnAge, _lifeLeftMs;
    private static double _gifSize = 200, _fallSpeed = 4, _opacity = 1, _startScale = 1;
    private static bool _spawning;
    private static int _animatedAlive, _gen;
    private static string? _owner, _pendingOwner;
    private static readonly List<Task> Pending = new();

    /// <summary>WPF IsRaining: spawning, or clips still falling.</summary>
    internal static bool IsRaining => _spawning || Fallers.Count > 0;
    internal static bool IsUp => _windows != null;
    internal static int Alive => Fallers.Count;
    internal static int AnimatedAlive => _animatedAlive;
    internal static string? Owner => _owner;
    internal static IReadOnlyList<RoomOverlayWindow> Windows => _windows ?? (IReadOnlyList<RoomOverlayWindow>)Array.Empty<RoomOverlayWindow>();
    internal static IEnumerable<(string Path, Image Image, bool Animated, double Y)> Clips => Fallers.Select(f => (f.Path, f.Img, f.Animated, f.Y));

    /// <summary>The pool fetch and the decodes in flight (tests await it).</summary>
    internal static Task Loading => Settle();

    private static async Task Settle()
    {
        while (true)
        {
            Task[] open;
            lock (Pending) { Pending.RemoveAll(t => t.IsCompleted); open = Pending.ToArray(); }
            if (open.Length == 0) return;
            try { await Task.WhenAll(open); } catch { }
        }
    }

    private static void Track(Task t) { lock (Pending) { Pending.RemoveAll(x => x.IsCompleted); Pending.Add(t); } }

    /// <summary>Start a cascade (WPF Show, same knobs and clamps). A cascade already up is restarted
    /// with the new pool. The pool is fetched off the UI thread: pack pictures decrypt on demand.</summary>
    internal static void Show(Visual host, string owner, double spawnRatePerSec, double durationSec, double gifSize,
        double fallSpeed, double opacity, double startScale = 1.0)
    {
        int batch = (int)Math.Clamp(Math.Ceiling(spawnRatePerSec * Math.Max(1.0, durationSec)) + 6, 8, 24);
        int gen = ++_gen;
        _pendingOwner = owner;
        Track(Go());

        async Task Go()
        {
            List<string> files;
            try
            {
                files = await Task.Run(() => PickFiles(batch) ?? new List<string>());
                // Whole paths, local files only (remote stills are not on this head).
                files = files.Where(f => !string.IsNullOrWhiteSpace(f) && !f.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                             .Distinct(PathIdentity).ToList();
            }
            catch (Exception ex) { Log.Debug("GifCascade pool: {E}", ex.Message); files = new List<string>(); }
            if (gen != _gen) return;   // closed or refired while the pool was read
            try
            {
                // An empty enabled pool is a legitimate "rain nothing" (WPF #762): never fall back to a raw folder.
                if (files.Count == 0) { Log.Debug("GifCascade: no images in pool, nothing to rain"); return; }
                StopAndClear();
                _windows ??= BackRoomOverlays.Open(host);
                if (_windows == null) return;   // refused for the session: no click-through, no window
                _owner = owner;
                _files = files;
                _gifSize = Math.Clamp(gifSize, 40, 600);
                _fallSpeed = Math.Clamp(fallSpeed, 0.5, 30);
                _opacity = Math.Clamp(opacity, 0.05, 1.0);
                _startScale = Math.Clamp(startScale, 0.1, 1.0);
                _spawnEveryMs = 1000.0 / Math.Max(0.05, spawnRatePerSec);
                _lifeLeftMs = Math.Max(1.0, durationSec) * 1000;
                _spawnAge = 0;
                _spawning = true;
                SpawnOne();
                _last = Environment.TickCount64;
                _clock = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(FrameMs) };
                _clock.Tick += (_, _) =>
                {
                    long now = Environment.TickCount64;
                    double dt = now - _last;
                    _last = now;
                    Advance(dt);
                };
                _clock.Start();
                Log.Information("GifCascade: raining (pool={N}, owner={Owner})", files.Count, owner);
            }
            catch (Exception ex) { Log.Debug("GifCascade.Show: {E}", ex.Message); CloseActive(); }
        }
    }

    /// <summary>Whole-path identity: two files with one name in different folders are two pictures.</summary>
    private static readonly IEqualityComparer<string> PathIdentity =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>One step of the cascade: <paramref name="dtMs"/> of spawn clock, spawn window, fall and
    /// frames. The timer calls it with real elapsed time; tests call it directly.</summary>
    internal static void Advance(double dtMs)
    {
        if (_windows == null) return;
        try
        {
            if (dtMs <= 0) return;
            if (dtMs > 100) dtMs = 100;   // a stalled UI thread never teleports the rain
            if (_spawning)
            {
                _lifeLeftMs -= dtMs;
                _spawnAge += dtMs;
                while (_spawning && _spawnAge >= _spawnEveryMs) { _spawnAge -= _spawnEveryMs; SpawnOne(); }
                if (_lifeLeftMs <= 0) _spawning = false;   // stop spawning; what is in flight falls out
            }
            double frames = dtMs / TunedFrameMs;
            for (int i = Fallers.Count - 1; i >= 0; i--)
            {
                var f = Fallers[i];
                f.Y += f.Speed * frames;
                double s = ScaleAt(f.Y, f.Window.Height);
                f.Grow.ScaleX = s;
                f.Grow.ScaleY = s;
                f.Move.Y = f.Y;
                if (f.Picture is { Frames.Count: > 1 } p)
                {
                    f.FrameAge += dtMs;
                    double delay = p.Delay > TimeSpan.Zero ? p.Delay.TotalMilliseconds : 100;
                    if (f.FrameAge >= delay)
                    {
                        f.FrameAge %= delay;
                        f.Frame = (f.Frame + 1) % p.Frames.Count;
                        f.Img.Source = p.Frames[f.Frame];
                    }
                }
                if (f.Y > f.Window.Height + _gifSize) { Retire(f); Fallers.RemoveAt(i); }
            }
            if (!_spawning && Fallers.Count == 0) CloseActive();   // drained: the windows and the timer go
        }
        catch (Exception ex) { Log.Debug("GifCascade step: {E}", ex.Message); }
    }

    /// <summary>WPF ScaleAt: starts at the start scale up top and eases to full by 75% of the way down.</summary>
    internal static double ScaleAt(double y, double height, double startScale)
    {
        if (startScale >= 1.0) return 1.0;
        double p = Math.Clamp(y / Math.Max(1.0, height * 0.75), 0, 1);
        return startScale + (1.0 - startScale) * p;
    }

    private static double ScaleAt(double y, double height) => ScaleAt(y, height, _startScale);

    private static void SpawnOne()
    {
        if (!_spawning || _windows == null || _windows.Count == 0 || _files.Count == 0) return;
        if (Fallers.Count >= MaxConcurrent) return;   // never let clips pile up
        try
        {
            string path = _files[Rng.Next(_files.Count)];
            // A gif (or a webp with frames) animates only while the animated budget has room and it is not huge.
            bool animate = false;
            if (_animatedAlive < MaxAnimated && CanAnimate(path))
            {
                long len = FileLength(path);
                animate = len > 0 && len <= AnimatedMaxBytes;
            }
            if (animate) _animatedAlive++;

            var window = PickWindow();
            double size = _gifSize;
            double left = Rng.NextDouble() * Math.Max(1, window.Width - size);
            double y = -size;
            var move = new TranslateTransform(0, y);
            double s0 = ScaleAt(y, window.Height);
            var grow = new ScaleTransform(s0, s0);
            var img = new Image
            {
                Stretch = Stretch.Uniform,
                Width = size,
                Opacity = Math.Clamp(_opacity, 0, 1),
                IsHitTestVisible = false,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RenderTransform = new TransformGroup { Children = { grow, move } },
            };
            Canvas.SetLeft(img, left);
            Canvas.SetTop(img, 0);
            window.Stage.Children.Add(img);
            var faller = new Faller
            {
                Path = path, Img = img, Window = window, Move = move, Grow = grow, Y = y,
                Speed = _fallSpeed * (0.7 + Rng.NextDouble() * 0.6), Animated = animate,
            };
            Fallers.Add(faller);

            // The clip falls empty for the few frames the decode takes: invisible in the rain.
            int decodeSize = (int)size;
            Track(Load());

            async Task Load()
            {
                RoomPicture? picture = null;
                try { picture = await Task.Run(() => Decode(path, decodeSize, animate)); }
                catch (Exception ex) { Log.Debug("GifCascade decode: {E}", ex.Message); }
                if (picture == null || picture.Frames.Count == 0) return;
                if (faller.Gone) { Drop(picture); return; }
                faller.Picture = picture;
                img.Source = picture.Frames[0];
            }
        }
        catch (Exception ex) { Log.Debug("GifCascade spawn: {E}", ex.Message); }
    }

    /// <summary>A screen for the next clip, by width, so the rain is even across monitors.</summary>
    private static RoomOverlayWindow PickWindow()
    {
        var list = _windows!;
        double total = list.Sum(w => Math.Max(1, w.Width));
        double at = Rng.NextDouble() * total;
        foreach (var w in list)
        {
            at -= Math.Max(1, w.Width);
            if (at <= 0) return w;
        }
        return list[^1];
    }

    private static void Retire(Faller f)
    {
        f.Gone = true;
        if (f.Animated) _animatedAlive = Math.Max(0, _animatedAlive - 1);
        try { f.Img.Source = null; f.Window.Stage.Children.Remove(f.Img); } catch { }
        Drop(f.Picture);
        f.Picture = null;
    }

    private static void Drop(RoomPicture? p)
    {
        if (p == null) return;
        foreach (var b in p.Frames) { try { b.Dispose(); } catch { } }
    }

    private static void StopAndClear()
    {
        try { _clock?.Stop(); } catch { }
        _clock = null;
        _spawning = false;
        foreach (var f in Fallers) Retire(f);
        Fallers.Clear();
        _animatedAlive = 0;
    }

    /// <summary>Down now (panic, suspend, close, or the last clip left): clips, timer and windows.</summary>
    internal static void CloseActive()
    {
        _gen++;
        _pendingOwner = null;
        StopAndClear();
        var windows = _windows;
        _windows = null;
        _owner = null;
        if (windows == null) return;
        foreach (var w in windows) { try { w.Close(); } catch { } }
        windows.Clear();
    }

    /// <summary>Closes the cascade only when <paramref name="owner"/> started it (a room teardown
    /// never takes down somebody else's rain).</summary>
    internal static void CloseOwned(string owner)
    {
        if (_owner == owner || (_windows == null && _pendingOwner == owner)) CloseActive();
    }
}
