using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>A view the host can tint with the card's hue after it is made.</summary>
    internal interface IAccentedArt
    {
        Color Accent { set; }
    }

    /// <summary>
    /// Base for the deck's simple vector art (tables, wheel, spiral, calendar, tip). FX PERF RULES:
    /// the picture lives in ONE <see cref="DrawingGroup"/> that <see cref="OnRender"/> draws once;
    /// a frame re-opens that group and redraws into it, so a tick is a retained-drawing update with
    /// no arrange and no visual-tree invalidation. The clock is a <see cref="FrameClock"/> at 30 fps,
    /// running only between <see cref="Play"/> and <see cref="Pause"/> and only when ambient loops
    /// are allowed; otherwise the art holds one still frame.
    /// </summary>
    public abstract class BillboardVectorArt : FrameworkElement, IBillboardArtView, IAccentedArt
    {
        /// <summary>The frame a still card shows (Motion Off, or never played): far enough in that
        /// every entrance has landed.</summary>
        public const double StillSeconds = 1.6;

        private readonly DrawingGroup _drawing = new();
        private readonly Stopwatch _watch = new();
        private FrameClock? _clock;
        private double _offset;
        private bool _released;

        protected BillboardVectorArt()
        {
            IsHitTestVisible = false;
            SnapsToDevicePixels = false;
            SizeChanged += (_, _) => Redraw();
            Loaded += (_, _) => Redraw();
        }

        /// <summary>The card's hue.</summary>
        public Color Accent { get; set; } = Color.FromRgb(0xff, 0x4f, 0xa8);

        /// <summary>Seconds of animation shown so far.</summary>
        protected double Seconds => SecondsForTests ?? (_watch.IsRunning ? _offset + _watch.Elapsed.TotalSeconds : (_offset > 0 ? _offset : StillSeconds));

        /// <summary>Tests and shot harnesses pin the clock here to paint a chosen moment.</summary>
        internal double? SecondsForTests { get; set; }

        public void Play()
        {
            if (_released) return;
            if (!MotionFx.AllowAmbientLoops) { Pause(); Redraw(); return; }
            if (_offset <= 0 && !_watch.IsRunning) _offset = 0.0001;
            _watch.Start();
            _clock ??= new FrameClock { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            _clock.Tick -= OnTick;
            _clock.Tick += OnTick;
            _clock.Start();
        }

        public void Pause()
        {
            _clock?.Stop();
            if (_watch.IsRunning)
            {
                _offset += _watch.Elapsed.TotalSeconds;
                _watch.Reset();
            }
        }

        public void Release()
        {
            Pause();
            if (_clock != null) _clock.Tick -= OnTick;
            _clock = null;
            _released = true;
            _layers.Clear();
            _brushes.Clear();
            using (_drawing.Open()) { }
        }

        public virtual void Touch(Point normalized) { }

        private void OnTick(object? sender, EventArgs e) => Redraw();

        /// <summary>Redraws the picture at the current time.</summary>
        public void Redraw()
        {
            if (_released) return;
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0 || double.IsNaN(w) || double.IsNaN(h)) return;
            using var dc = _drawing.Open();
            try { Paint(dc, w, h, Seconds); }
            catch (Exception ex) { App.Logger?.Debug("Billboard art paint failed: {E}", ex.Message); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            dc.DrawDrawing(_drawing);
        }

        /// <summary>Draws one frame at time <paramref name="t"/> seconds.</summary>
        protected abstract void Paint(DrawingContext dc, double w, double h, double t);

        // ---- shared paint helpers ---------------------------------------------------------------

        /// <summary>The card ground: a radial pool of the hue's dark tone, off to the right where
        /// the art sits, fading to the well floor.</summary>
        protected static void Ground(DrawingContext dc, double w, double h, Color inner)
        {
            var g = new RadialGradientBrush(inner, Color.FromRgb(0x0c, 0x0d, 0x1a))
            {
                Center = new Point(0.72, 0.45),
                GradientOrigin = new Point(0.72, 0.45),
                RadiusX = 0.75,
                RadiusY = 0.75 * w / Math.Max(1, h),
            };
            g.Freeze();
            dc.DrawRectangle(g, null, new Rect(0, 0, w, h));
        }

        /// <summary>A dark tone of a hue for the ground (about a fifth of its brightness).</summary>
        protected static Color Deep(Color c) => Color.FromRgb((byte)(c.R * 0.2 + 8), (byte)(c.G * 0.16 + 9), (byte)(c.B * 0.22 + 18));

        protected static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

        protected static SolidColorBrush Solid(Color c, double a = 1)
        {
            var b = new SolidColorBrush(WithAlpha(c, a));
            b.Freeze();
            return b;
        }

        protected static Pen Stroke(Color c, double thickness, double a = 1)
        {
            var p = new Pen(Solid(c, a), Math.Max(0.5, thickness)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            p.Freeze();
            return p;
        }

        /// <summary>Ease-out cubic, clamped.</summary>
        protected static double EaseOut(double t)
        {
            t = Math.Clamp(t, 0, 1);
            return 1 - Math.Pow(1 - t, 3);
        }

        /// <summary>A soft round glow (no Effect): a radial brush that fades to clear.</summary>
        protected static void Glow(DrawingContext dc, Point c, double r, Color hue, double a)
        {
            var g = new RadialGradientBrush(WithAlpha(hue, a), WithAlpha(hue, 0));
            g.Freeze();
            dc.DrawEllipse(g, null, c, r, r);
        }

        // ---- cached layers and the shared juice (one lamp, top left) ------------------------------

        private readonly Dictionary<string, Drawing> _layers = new();
        private readonly Dictionary<long, Brush> _brushes = new();
        private readonly Dictionary<int, Pen> _bevels = new();
        private Size _layerSize;
        private Color _layerAccent;

        /// <summary>
        /// A static layer painted once and kept as a frozen drawing until the size or hue changes,
        /// so a frame only re-draws what moves. Draw it straight or under a transform.
        /// </summary>
        protected Drawing Layer(string key, double w, double h, Action<DrawingContext> paint)
        {
            if (_layerSize.Width != w || _layerSize.Height != h || _layerAccent != Accent)
            {
                _layers.Clear();
                _layerSize = new Size(w, h);
                _layerAccent = Accent;
            }
            if (_layers.TryGetValue(key, out var cached)) return cached;
            var g = new DrawingGroup();
            using (var dc = g.Open()) paint(dc);
            g.Freeze();
            _layers[key] = g;
            return g;
        }

        /// <summary>The ground plus a soft vignette, cached.</summary>
        protected void CachedGround(DrawingContext dc, double w, double h, Color inner) =>
            dc.DrawDrawing(Layer("ground", w, h, d =>
            {
                Ground(d, w, h, inner);
                var v = new RadialGradientBrush();
                v.Center = v.GradientOrigin = new Point(0.62, 0.48);
                v.RadiusX = v.RadiusY = 0.85;
                v.GradientStops.Add(new GradientStop(Color.FromArgb(0, 6, 6, 14), 0.55));
                v.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 6, 6, 14), 1));
                v.Freeze();
                d.DrawRectangle(v, null, new Rect(0, 0, w, h));
            }));

        /// <summary>A frozen solid brush from a small per-view cache (alpha in 1/64 steps).</summary>
        protected Brush Fill(Color c, double a = 1)
        {
            int q = (int)Math.Round(Math.Clamp(a, 0, 1) * 64);
            long key = ((long)c.R << 24) | ((long)c.G << 16) | ((long)c.B << 8) | (long)q;
            if (_brushes.TryGetValue(key, out var b)) return b;
            b = Solid(c, q / 64.0);
            if (_brushes.Count < 256) _brushes[key] = b;
            return b;
        }

        /// <summary>True while particles may run (perf tier allows them), or on a still frame.</summary>
        protected bool Particles
        {
            get
            {
                if (!_watch.IsRunning) return true;
                try { return MotionFx.AllowParticles; } catch { return true; }
            }
        }

        /// <summary>A fixed pseudo random in [0, 1) for slot <paramref name="i"/>, never a live rng.</summary>
        protected static double Hash(int seed, int i, int salt)
        {
            double x = Math.Sin(seed * 12.9898 + i * 78.233 + salt * 37.719) * 43758.5453;
            return x - Math.Floor(x);
        }

        /// <summary>A small idle bob of about <paramref name="amp"/> px, never in step with its neighbours.</summary>
        protected static double Bob(double t, double amp, double phase = 0) =>
            Math.Sin(t * 1.35 + phase) * amp + Math.Sin(t * 0.61 + phase * 1.7) * amp * 0.35;

        /// <summary>
        /// Motes rising through an area (normalised to the card). Two calls with different speeds
        /// and sizes make the parallax: far ones small, dim and slow, near ones larger and quicker.
        /// </summary>
        protected void Motes(DrawingContext dc, double w, double h, double t, Color hue, int count, int seed,
            Rect area, double speed, double size, double alpha)
        {
            if (!Particles) return;
            var brush = Fill(hue);
            for (int i = 0; i < count; i++)
            {
                double hx = Hash(seed, i, 1), hy = Hash(seed, i, 2), hs = Hash(seed, i, 3);
                double life = (hy + t * speed * (0.6 + 0.8 * hs)) % 1.0;
                double x = (area.X + hx * area.Width + Math.Sin(t * 0.9 + i * 1.7) * 0.012) * w;
                double y = (area.Bottom - life * area.Height) * h;
                double a = alpha * Math.Sin(life * Math.PI) * (0.55 + 0.45 * Math.Sin(t * (2 + hs * 3) + i));
                if (a <= 0.02) continue;
                double r = size * h * (0.6 + 0.8 * hs);
                dc.PushOpacity(a);
                dc.DrawEllipse(brush, null, new Point(x, y), r, r);
                dc.Pop();
            }
        }

        private static readonly Geometry SparkShape = MakeSpark();

        private static Geometry MakeSpark()
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, -1), true, true);
                ctx.QuadraticBezierTo(new Point(0.12, -0.12), new Point(1, 0), false, false);
                ctx.QuadraticBezierTo(new Point(0.12, 0.12), new Point(0, 1), false, false);
                ctx.QuadraticBezierTo(new Point(-0.12, 0.12), new Point(-1, 0), false, false);
                ctx.QuadraticBezierTo(new Point(-0.12, -0.12), new Point(0, -1), false, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>A four point glint, the shape a sparkle leaves (no glow ball).</summary>
        protected void Spark(DrawingContext dc, Point c, double r, Color hue, double a, double turn = 0)
        {
            if (a <= 0.02 || r <= 0.3) return;
            double cs = Math.Cos(turn) * r, sn = Math.Sin(turn) * r;
            dc.PushTransform(new MatrixTransform(cs, sn, -sn, cs, c.X, c.Y));
            dc.PushOpacity(Math.Min(1, a));
            dc.DrawGeometry(Fill(hue), null, SparkShape);
            dc.Pop();
            dc.Pop();
        }

        /// <summary>Sparkles that pop in, hold a beat and fade, each on its own slow cycle and spot.</summary>
        protected void Twinkles(DrawingContext dc, double w, double h, double t, Color hue, int count, int seed, Rect area, double size)
        {
            if (!Particles) return;
            for (int i = 0; i < count; i++)
            {
                double period = 2.2 + Hash(seed, i, 4) * 2.4;
                double u = t / period + Hash(seed, i, 5);
                double ph = u - Math.Floor(u);
                int cycle = (int)Math.Floor(u);
                double life = ph < 0.35 ? Math.Sin(ph / 0.35 * Math.PI) : 0;
                if (life <= 0) continue;
                double x = (area.X + Hash(seed, i * 7 + cycle, 6) * area.Width) * w;
                double y = (area.Y + Hash(seed, i * 7 + cycle, 7) * area.Height) * h;
                Spark(dc, new Point(x, y), size * h * (0.5 + 0.5 * life), i % 3 == 0 ? Colors.White : hue, life, ph * 1.2);
            }
        }

        /// <summary>
        /// The lamp sits top left: a raised plate has a lit top-left edge and a shaded bottom-right;
        /// a pressed one (on is pressed in) the reverse.
        /// </summary>
        protected Pen Bevel(double thickness, bool pressed = false)
        {
            int key = (int)Math.Round(thickness * 4) * 2 + (pressed ? 1 : 0);
            if (_bevels.TryGetValue(key, out var pen)) return pen;
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            if (!pressed)
            {
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 255, 255, 255), 0));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.45));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.6));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 0, 0, 0), 1));
            }
            else
            {
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 0, 0, 0), 0));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.45));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.6));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, 255, 255, 255), 1));
            }
            g.Freeze();
            pen = new Pen(g, Math.Max(0.75, thickness));
            pen.Freeze();
            if (_bevels.Count < 32) _bevels[key] = pen;
            return pen;
        }

        /// <summary>A soft drop shadow down and right of a rounded rect (two stacked shades, no Effect).</summary>
        protected void Shadow(DrawingContext dc, Rect r, double radius, double lift)
        {
            if (lift <= 0) return;
            var ink = Color.FromRgb(4, 4, 12);
            dc.DrawRoundedRectangle(Fill(ink, 0.22), null, new Rect(r.X + lift * 0.9, r.Y + lift * 1.4, r.Width + lift * 0.4, r.Height + lift * 0.4), radius + lift * 0.4, radius + lift * 0.4);
            dc.DrawRoundedRectangle(Fill(ink, 0.32), null, new Rect(r.X + lift * 0.45, r.Y + lift * 0.7, r.Width, r.Height), radius, radius);
        }

        /// <summary>A rounded plate with depth: shadow when raised, the lamp's bevel either way.</summary>
        protected void Plate(DrawingContext dc, Rect r, double radius, Brush fill, double lift, bool pressed = false)
        {
            if (!pressed) Shadow(dc, r, radius, lift);
            dc.DrawRoundedRectangle(fill, null, r, radius, radius);
            double th = Math.Clamp(Math.Min(r.Width, r.Height) * 0.05, 1, 2.6);
            var inner = new Rect(r.X + th / 2, r.Y + th / 2, Math.Max(0, r.Width - th), Math.Max(0, r.Height - th));
            dc.DrawRoundedRectangle(null, Bevel(th, pressed), inner, Math.Max(0, radius - th / 2), Math.Max(0, radius - th / 2));
        }

        private static readonly Brush SheenBrush = MakeSheen();

        private static Brush MakeSheen()
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 255, 255, 255), 0.5));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            g.Freeze();
            return g;
        }

        /// <summary>
        /// A slanted highlight band crossing a shape once per <paramref name="period"/> seconds,
        /// clipped to it. It is off the shape most of the period, so it reads as a glint, not a strobe.
        /// </summary>
        protected static void Sheen(DrawingContext dc, Geometry clip, Rect bounds, double t, double period, double offset = 0, double strength = 1)
        {
            double u = (t + offset) / period;
            double k = (u - Math.Floor(u)) / 0.32;
            if (k <= 0 || k >= 1) return;
            double band = Math.Max(8, bounds.Width * 0.22);
            double x = bounds.Left - band + EaseOut(k) * (bounds.Width + band * 2 + bounds.Height * 0.5);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x, bounds.Top), true, true);
                ctx.LineTo(new Point(x + band, bounds.Top), false, false);
                ctx.LineTo(new Point(x + band - bounds.Height * 0.5, bounds.Bottom), false, false);
                ctx.LineTo(new Point(x - bounds.Height * 0.5, bounds.Bottom), false, false);
            }
            g.Freeze();
            dc.PushClip(clip);
            dc.PushOpacity(Math.Clamp(strength, 0, 1) * Math.Sin(k * Math.PI));
            dc.DrawGeometry(SheenBrush, null, g);
            dc.Pop();
            dc.Pop();
        }

        /// <summary>Back-out ease (a small overshoot), clamped at 0 and settling on 1.</summary>
        protected static double BackOut(double t, double s = 1.6)
        {
            t = Math.Clamp(t, 0, 1);
            double u = t - 1;
            return 1 + (s + 1) * u * u * u + s * u * u;
        }

        /// <summary>Two hues mixed: toward white for a lit face, toward ink for shade.</summary>
        protected static Color Mix(Color a, Color b, double k)
        {
            k = Math.Clamp(k, 0, 1);
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
        }

        public static Color ParseHue(string? hex, Color fallback)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color c) return c;
            }
            catch { }
            return fallback;
        }
    }
}
