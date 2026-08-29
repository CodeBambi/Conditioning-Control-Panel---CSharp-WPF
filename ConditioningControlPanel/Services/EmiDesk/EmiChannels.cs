using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// The glass channels: what she shows on her own screen while nobody is looking, and what a tap on
/// it fires. Ported from <c>docs/emi-desk/reference/pitch-demo.js</c> CHANNELS, drawn with WPF
/// shapes on <see cref="EmiDeskWindow.GlassHost"/> instead of a canvas 2d context.
///
/// LOCAL ASSETS ONLY. The video and burst and rain channels read the user's own
/// <c>EffectiveAssetsPath</c> folders through <see cref="EmiOffers"/>; nothing here fetches
/// anything, and the app-wide remote-media consent only ever matters downstream, inside the
/// services that already own their own remote helpers.
///
/// Every painter draws into a host the window owns and clears on teardown. The face keeps painting
/// underneath the whole time, so killing a channel is hiding one node and never touches the locked
/// face renderer.
/// </summary>
public static class EmiChannels
{
    /// <summary>The channel ids, in the order the ambient rotation offers them.</summary>
    public static readonly IReadOnlyList<string> All = new[] { "spiral", "video", "burst", "rain" };

    /// <summary>How long a channel sits on the glass before she gives up on it (BRIEF 6).</summary>
    public static readonly TimeSpan ChannelLife = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Idle time before the glass flips (BRIEF 6). Ninety seconds is the owner lock; the only thing
    /// that can move it is the EMI_DESK_IDLE_MS QA override (see <see cref="EmiDebug"/>), which is
    /// absent in every normal launch.
    /// </summary>
    public static TimeSpan IdleBeforeFlip =>
        EmiDebug.IdleMs is int ms ? TimeSpan.FromMilliseconds(ms) : TimeSpan.FromSeconds(90);

    /// <summary>The glitch flip's length: three to four torn frames.</summary>
    public const int GlitchMs = 220;

    private static readonly Color Pink = Color.FromRgb(0xFF, 0x69, 0xB4);
    private static readonly Color Screen = Color.FromRgb(0x0E, 0x0E, 0x1C);
    private static readonly Color Cream = Color.FromRgb(0xF5, 0xF0, 0xE1);

    /// <summary>The pink she is drawn in, frozen.</summary>
    public static readonly SolidColorBrush PinkBrush = Freeze(new SolidColorBrush(Pink));

    /// <summary>The dead-screen navy behind every channel, frozen.</summary>
    public static readonly SolidColorBrush ScreenBrush = Freeze(new SolidColorBrush(Screen));

    private static readonly SolidColorBrush CreamBrush = Freeze(new SolidColorBrush(Cream));
    private static readonly SolidColorBrush LavenderBrush =
        Freeze(new SolidColorBrush(Color.FromRgb(0xB9, 0xA7, 0xF5)));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static readonly Random Rng = new();

    /// <summary>
    /// Pick a channel that can actually be shown right now, or null when none can. A channel whose
    /// library is empty is never offered, because the tap would land on nothing.
    /// </summary>
    public static string? Pick()
    {
        try
        {
            var pool = new List<string>(4);
            foreach (var id in All)
            {
                if (Feasible(id)) pool.Add(id);
            }
            if (pool.Count == 0) return null;
            return pool[Rng.Next(pool.Count)];
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] channel pick failed");
            return null;
        }
    }

    /// <summary>Can this channel be painted and fired right now?</summary>
    public static bool Feasible(string? id) => id switch
    {
        "spiral" => App.Overlay != null,
        "video" => EmiOffers.HasVideos(),
        "burst" => EmiOffers.HasImages(),
        "rain" => EmiOffers.HasImages(),
        _ => false
    };

    /// <summary>Build the painter for a channel, or null when the channel is unknown or unusable.</summary>
    public static EmiChannelPainter? Build(string? id, double w, double h)
    {
        try
        {
            if (w <= 4 || h <= 4) return null;
            return id switch
            {
                "spiral" => new SpiralPainter(w, h),
                "video" => VideoPainter.TryBuild(w, h),
                "burst" => BurstPainter.TryBuild(w, h),
                "rain" => RainPainter.TryBuild(w, h),
                _ => null
            };
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] channel painter build failed for {Channel}", id);
            return null;
        }
    }

    /// <summary>Fire what a tap on this channel does. See BRIEF 6 for the mapping.</summary>
    public static void Fire(string? id, string? videoPath)
    {
        try
        {
            switch (id)
            {
                case "spiral":
                    EmiOffers.FireSpiral(fromAsk: false);
                    return;
                case "video":
                    FireGlassVideo(videoPath);
                    return;
                case "burst":
                    EmiOffers.FireBurst(fromAsk: false);
                    return;
                case "rain":
                    EmiOffers.FireRain(fromAsk: false);
                    return;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] channel fire failed for {Channel}", id);
        }
    }

    /// <summary>
    /// The video channel fires the video that was ON the glass, not a fresh random one: she showed
    /// you a thing and then played a different thing is the one way this reads as a bait.
    /// </summary>
    private static void FireGlassVideo(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            EmiOffers.FireVideo(fromAsk: false);
            return;
        }
        App.Video?.PlaySpecificVideo(path!, false);
        EmiOffers.ReassertTopmost();
        App.EmiDesk?.Fire("effectFired", new { channel = "video" });
        App.EmiDesk?.Fire("videoRunning", EmiOffers.VideoCtx(path, fromAsk: false));
    }

    // ============================================================================================
    // the painters
    // ============================================================================================

    /// <summary>
    /// A five-arm spiral, rotating. One <see cref="System.Windows.Shapes.Path"/> whose geometry is
    /// rebuilt per frame:
    /// about 250 points, which is a rounding error next to decoding a gif and, unlike an animated
    /// RotateTransform on a static arm, it keeps the arms breathing outward the way face.js does.
    /// </summary>
    private sealed class SpiralPainter : EmiChannelPainter
    {
        // Fully qualified: System.IO.Path is in scope too, and CS0104 does not care which one reads better.
        private readonly System.Windows.Shapes.Path _path;
        private readonly double _w, _h;

        public SpiralPainter(double w, double h)
        {
            _w = w; _h = h;
            _path = new System.Windows.Shapes.Path
            {
                Stroke = PinkBrush,
                StrokeThickness = Math.Max(1.5, w * 0.032),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
        }

        public override string Id => "spiral";

        public override void Attach(Panel host)
        {
            AddBackdrop(host, _w, _h);
            host.Children.Add(_path);
        }

        public override void Tick(double tMs)
        {
            double cx = _w / 2, cy = _h / 2;
            double turn = tMs / 400.0;
            double maxR = Math.Max(_w, _h) * 0.72;

            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                for (int arm = 0; arm < 5; arm++)
                {
                    double phase = turn + arm * (Math.PI * 2.0 / 5.0);
                    bool first = true;
                    for (double a = 0; a < 9.0; a += 0.22)
                    {
                        double r = a / 9.0 * maxR;
                        double x = cx + Math.Cos(a + phase) * r;
                        double y = cy + Math.Sin(a + phase) * r;
                        if (first) { c.BeginFigure(new Point(x, y), false, false); first = false; }
                        else c.LineTo(new Point(x, y), true, false);
                    }
                }
            }
            g.Freeze();
            _path.Data = g;
        }
    }

    /// <summary>
    /// A film strip with the file's name on it. The app has no video thumbnailer (LibVLC is wired
    /// for playback only and spinning a second player up to grab one frame for a 60 px decoration
    /// is not a trade worth making), so the channel says WHICH video rather than showing it: a
    /// sprocketed strip, a play glyph, and the name in the pixel font.
    /// </summary>
    private sealed class VideoPainter : EmiChannelPainter
    {
        private readonly double _w, _h;
        private readonly string _path;
        private readonly List<Rectangle> _scan = new();

        private VideoPainter(double w, double h, string path) { _w = w; _h = h; _path = path; }

        public static VideoPainter? TryBuild(double w, double h)
        {
            var p = EmiOffers.RandomVideo();
            return p == null ? null : new VideoPainter(w, h, p);
        }

        public override string Id => "video";

        /// <summary>The file this channel is offering. The tap plays THIS one.</summary>
        public override string? Payload => _path;

        public override void Attach(Panel host)
        {
            var bg = new Rectangle
            {
                Width = _w,
                Height = _h,
                IsHitTestVisible = false,
                Fill = Freeze(new LinearGradientBrush(
                    Color.FromRgb(0x3A, 0x24, 0x50), Color.FromRgb(0x8B, 0x2C, 0x6A), 45))
            };
            host.Children.Add(bg);

            // sprocket holes, top and bottom
            double hole = Math.Max(2, _h * 0.055);
            for (double x = hole; x < _w - hole; x += hole * 2.2)
            {
                host.Children.Add(Box(x, hole * 0.5, hole, hole, CreamBrush, 0.35));
                host.Children.Add(Box(x, _h - hole * 1.5, hole, hole, CreamBrush, 0.35));
            }

            // the play glyph
            double r = Math.Min(_w, _h) * 0.20;
            var disc = new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Fill = Freeze(new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0))),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(disc, _w / 2 - r);
            Canvas.SetTop(disc, _h / 2 - r);
            host.Children.Add(disc);

            var tri = new Polygon
            {
                Fill = PinkBrush,
                IsHitTestVisible = false,
                Points = new PointCollection
                {
                    new Point(_w / 2 - r * 0.35, _h / 2 - r * 0.55),
                    new Point(_w / 2 + r * 0.60, _h / 2),
                    new Point(_w / 2 - r * 0.35, _h / 2 + r * 0.55)
                }
            };
            host.Children.Add(tri);

            // the name, in the pixel font, clipped to one line
            var name = new TextBlock
            {
                Text = EmiOffers.DisplayName(_path),
                FontFamily = EmiFace.PixelFont,
                FontSize = Math.Max(5, _h * 0.075),
                Foreground = CreamBrush,
                MaxWidth = _w - 6,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(name, 3);
            Canvas.SetTop(name, _h - Math.Max(9, _h * 0.16));
            host.Children.Add(name);

            // scanlines, moved per tick
            for (int i = 0; i < 6; i++)
            {
                var line = Box(0, 0, _w, Math.Max(1.5, _h * 0.04), CreamBrush, 0.06);
                _scan.Add(line);
                host.Children.Add(line);
            }
        }

        public override void Tick(double tMs)
        {
            double step = _h / _scan.Count;
            double drift = (tMs / 40.0) % step;
            for (int i = 0; i < _scan.Count; i++)
            {
                Canvas.SetTop(_scan[i], i * step + drift - step);
                _scan[i].Opacity = 0.04 + 0.03 * Math.Sin(tMs / 300.0 + i);
            }
        }
    }

    /// <summary>
    /// Three or four of the user's own images as still first frames, tiled with a small rotation
    /// each and pulsing. Decoded small (<see cref="DecodeWidth"/>) and once: a channel that
    /// re-rasters full-size art at 30 fps is the phone FX diet's own bug, in miniature.
    /// </summary>
    private sealed class BurstPainter : EmiChannelPainter
    {
        private const int DecodeWidth = 96;

        private readonly double _w, _h;
        private readonly List<string> _files;
        private readonly List<Image> _tiles = new();

        private BurstPainter(double w, double h, List<string> files) { _w = w; _h = h; _files = files; }

        public static BurstPainter? TryBuild(double w, double h)
        {
            var files = EmiOffers.RandomImages(4);
            return files.Count == 0 ? null : new BurstPainter(w, h, files);
        }

        public override string Id => "burst";

        public override void Attach(Panel host)
        {
            AddBackdrop(host, _w, _h);
            double tile = Math.Min(_w, _h) * 0.52;
            for (int i = 0; i < _files.Count; i++)
            {
                var src = Decode(_files[i], DecodeWidth);
                if (src == null) continue;
                var img = new Image
                {
                    Source = src,
                    Width = tile,
                    Height = tile,
                    Stretch = Stretch.UniformToFill,
                    IsHitTestVisible = false,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform((i % 2 == 0 ? -1 : 1) * (4 + i * 2))
                };
                Canvas.SetLeft(img, (i % 2) * (_w - tile) * 0.85 + _w * 0.06);
                Canvas.SetTop(img, (i / 2) * (_h - tile) * 0.85 + _h * 0.06);
                _tiles.Add(img);
                host.Children.Add(img);
            }
        }

        public override void Tick(double tMs)
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                double ph = ((tMs / 900.0) + i * 0.37) % 1.0;
                _tiles[i].Opacity = 0.35 + 0.65 * (1.0 - ph);
            }
        }
    }

    /// <summary>Twenty-pixel thumbnails falling down the glass. The desktop rain, in miniature.</summary>
    private sealed class RainPainter : EmiChannelPainter
    {
        private const int DecodeWidth = 40;
        private const double DropSize = 20;

        private readonly double _w, _h;
        private readonly List<Image> _drops = new();
        private readonly List<double> _speed = new();
        private readonly List<double> _offset = new();
        private readonly List<ImageSource> _sources;

        private RainPainter(double w, double h, List<ImageSource> sources) { _w = w; _h = h; _sources = sources; }

        public static RainPainter? TryBuild(double w, double h)
        {
            var files = EmiOffers.RandomImages(4);
            var sources = new List<ImageSource>();
            foreach (var f in files)
            {
                var s = Decode(f, DecodeWidth);
                if (s != null) sources.Add(s);
            }
            return sources.Count == 0 ? null : new RainPainter(w, h, sources);
        }

        public override string Id => "rain";

        public override void Attach(Panel host)
        {
            AddBackdrop(host, _w, _h);
            double size = Math.Min(DropSize, _w * 0.22);
            int count = Math.Max(5, Math.Min(12, (int)(_w / size) * 2));
            for (int i = 0; i < count; i++)
            {
                var img = new Image
                {
                    Source = _sources[i % _sources.Count],
                    Width = size,
                    Height = size,
                    Stretch = Stretch.UniformToFill,
                    Opacity = 0.9,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(img, (i * 37) % Math.Max(1, _w - size));
                _drops.Add(img);
                _speed.Add(0.035 + (i % 4) * 0.012);
                _offset.Add(Rng.NextDouble() * (_h + size));
                host.Children.Add(img);
            }
        }

        public override void Tick(double tMs)
        {
            double span = _h + DropSize * 2;
            for (int i = 0; i < _drops.Count; i++)
            {
                double y = (_offset[i] + tMs * _speed[i]) % span - DropSize;
                Canvas.SetTop(_drops[i], y);
            }
        }
    }

    // ---------------------------------------------------------------- shared bits

    private static void AddBackdrop(Panel host, double w, double h)
    {
        host.Children.Add(new Rectangle
        {
            Width = w,
            Height = h,
            Fill = ScreenBrush,
            IsHitTestVisible = false
        });
    }

    private static Rectangle Box(double x, double y, double w, double h, Brush fill, double opacity)
    {
        var r = new Rectangle
        {
            Width = Math.Max(0.5, w),
            Height = Math.Max(0.5, h),
            Fill = fill,
            Opacity = opacity,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        return r;
    }

    /// <summary>
    /// Decode one image small and frozen. A gif decodes to its FIRST FRAME here on purpose: the
    /// glass is 60 px across and an animated decoder per tile is exactly the cost the phone FX
    /// diet went and removed everywhere else.
    /// </summary>
    private static ImageSource? Decode(string path, int width)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = width;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] channel image decode failed for {Path}", path);
            return null;
        }
    }

    /// <summary>The lavender alt, for anything that needs a second colour.</summary>
    public static Brush Lavender => LavenderBrush;

    /// <summary>The cream the bubble and the chips are lit with.</summary>
    public static Brush CreamInk => CreamBrush;

    /// <summary>Format a number the way a line would (invariant, no group separators).</summary>
    public static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>
/// One glass channel's renderer. The window owns the clock (one 30 fps timer) and the host; the
/// painter only knows how to fill a rectangle and how to move it forward.
/// </summary>
public abstract class EmiChannelPainter
{
    /// <summary>The channel id this painter draws.</summary>
    public abstract string Id { get; }

    /// <summary>What a tap should act on, when the channel picked something specific (a video path).</summary>
    public virtual string? Payload => null;

    /// <summary>Hang this painter's visuals off the host. Called once, on the dispatcher.</summary>
    public abstract void Attach(Panel host);

    /// <summary>Advance to <paramref name="tMs"/> ms since the channel came up.</summary>
    public abstract void Tick(double tMs);
}
