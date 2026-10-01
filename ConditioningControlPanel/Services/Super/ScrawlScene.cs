using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// Super Scrawl's live state and drawing: wall stamps, wall glows, slam shockwaves, ink sparks
    /// and the corner label, all in virtual-desktop DIP. BouncingTextService feeds it hits and the
    /// frame clock; every <see cref="ScrawlLayer"/> (one per bouncing-text window, under the words)
    /// draws it. The numbers live in <see cref="ScrawlRules"/>. Geometry is built and frozen once
    /// per stamp, brushes and pens once per scene, so a frame only pushes opacities.
    /// UI thread only.
    /// </summary>
    internal sealed class ScrawlScene
    {
        private sealed class Stamp
        {
            public Geometry Text = null!;
            public Geometry? Dots;
            public int Ink;                 // 0 pink, 1 mint, 2 lilac, 3 gold
            public bool Slam, Gold;
            public double X, Y, Age, Reach, Pulse = -1;
        }

        private struct Glow { public double X, Y, Age; public int Ink; }
        private struct Wave { public double X, Y, Age, Reach; }
        private struct Spark { public double X, Y, Vx, Vy, T, Life; public int Ink; }

        private const int Gold = 3, White = 4, MaxSparks = 400;

        private static readonly Color[] Inks =
        {
            Color.FromRgb(255, 111, 181), Color.FromRgb(95, 255, 208), Color.FromRgb(190, 150, 255),
            Color.FromRgb(255, 207, 107), Colors.White,
        };
        private static readonly Brush[] FillBrushes = Frozen(c => new SolidColorBrush(Alpha(c, ScrawlRules.FillAlpha)));
        private static readonly Brush[] DotBrushes = Frozen(c => new SolidColorBrush(Alpha(c, ScrawlRules.DotAlpha)));
        private static readonly Brush[] SparkBrushes = Frozen(c => new SolidColorBrush(c));
        private static readonly Brush[] GlowBrushes = Frozen(c => new RadialGradientBrush(Alpha(c, ScrawlRules.GlowAlpha), Alpha(c, 0)));
        private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(255, 226, 154)));

        private readonly Pen[] _strokePens, _slamPens, _haloPens;
        private readonly Pen _wavePen0, _wavePen1;
        private readonly List<Stamp> _stamps = new();
        private readonly List<Glow> _glows = new();
        private readonly List<Wave> _waves = new();
        private readonly Spark[] _sparks = new Spark[MaxSparks];
        private int _sparkCount;
        private FormattedText? _label;
        private double _labelX, _labelY, _labelAge = -1;
        private readonly Random _rng = new();
        private readonly Typeface _typeface;
        private readonly double _fs, _k, _ppd;
        private MotionLevel _motion = MotionLevel.Full;
        private double _shake;

        /// <summary>The shake offset for this frame, in DIP. The service moves the windows' canvas by it.</summary>
        public double ShakeX { get; private set; }
        public double ShakeY { get; private set; }

        /// <summary>Anything left to draw or shake. Layers stop invalidating once this goes false.</summary>
        public bool IsLive => _stamps.Count > 0 || _glows.Count > 0 || _waves.Count > 0 || _sparkCount > 0
                              || _labelAge >= 0 || _shake > 0;

        public ScrawlScene(FontFamily family, double fontSize, double pixelsPerDip)
        {
            _typeface = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            _fs = Math.Max(8, fontSize);
            _k = Math.Max(1, _fs / ScrawlRules.MockupFont);
            _ppd = pixelsPerDip <= 0 ? 1 : pixelsPerDip;
            _strokePens = Pens(ScrawlRules.StrokeWidth * _k, ScrawlRules.StrokeAlpha);
            _slamPens = Pens(ScrawlRules.SlamStrokeWidth * _k, ScrawlRules.StrokeAlpha);
            _haloPens = Pens(ScrawlRules.SlamStrokeWidth * _k * 3, 0.22);
            _wavePen0 = Freeze(new Pen(new SolidColorBrush(Alpha(Inks[1], 0.6)), 3 * _k));
            _wavePen1 = Freeze(new Pen(new SolidColorBrush(Alpha(Inks[1], 0.6)), 2 * _k));
        }

        /// <summary>A word hit a wall. A corner (both axes near) gets the gold stamp; anything else a wall stamp.</summary>
        public void WallHit(string word, ScrawlWall wall, double left, double top, double right, double bottom,
            ScrawlBounds b, Point labelAt)
        {
            if (wall == ScrawlWall.None || string.IsNullOrEmpty(word)) return;
            double cx = (left + right) / 2, cy = (top + bottom) / 2;
            Emit(cx, cy, 5, 0, 70, 0.4);
            if (ScrawlRules.IsCorner(left, top, right, bottom, b, _fs))
            {
                var p = ScrawlRules.PlaceCornerStamp(cx, cy, b, _fs);
                AddStamp(word, p, ScrawlRules.CornerStampScale, Gold, gold: true, slam: false, dots: 0, 0, 0, 0);
                var (gx, gy) = ScrawlRules.GlowPoint(cx < (b.MinX + b.MaxX) / 2 ? ScrawlWall.Left : ScrawlWall.Right, cx, cy, b);
                var (hx, hy) = ScrawlRules.GlowPoint(cy < (b.MinY + b.MaxY) / 2 ? ScrawlWall.Top : ScrawlWall.Bottom, cx, cy, b);
                _glows.Add(new Glow { X = gx, Y = gy, Ink = Gold });
                _glows.Add(new Glow { X = hx, Y = hy, Ink = Gold });
                var (sx, sy) = ScrawlRules.CornerPoint(cx, cy, b, _fs);
                Emit(sx, sy, ScrawlRules.CornerSparks, Gold, 280, 1.0);
                Emit(sx, sy, ScrawlRules.CornerWhiteSparks, White, 200, 0.8);
                _shake = Math.Max(_shake, ScrawlRules.ShakeCorner);
                _label = new FormattedText(Localization.Loc.Get("super_scrawl_corner"), CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, _typeface, _fs * 0.9, LabelBrush, _ppd);
                _labelX = labelAt.X; _labelY = labelAt.Y; _labelAge = 0;
                return;
            }
            int ink = ScrawlRules.InkFor(word);
            var pose = ScrawlRules.PlaceStamp(wall, cx, cy, b, _fs, _rng.NextDouble() * 2 - 1);
            AddStamp(word, pose, ScrawlRules.StampScale, ink, gold: false, slam: false, ScrawlRules.InkDots, 0.9, 2.0, 0.55);
            var (glx, gly) = ScrawlRules.GlowPoint(wall, cx, cy, b);
            _glows.Add(new Glow { X = glx, Y = gly, Ink = ink });
        }

        /// <summary>A slammed word arrived at the centre: the huge stamp, the shockwave, the shake, every stamp flares.</summary>
        public void SlamLanded(string word, double x, double y, double screenWidth)
        {
            _shake = ScrawlRules.ShakeSlam;
            foreach (var s in _stamps) if (!s.Slam) s.Pulse = 0;
            AddStamp(word, new StampPose(x, y, -0.14), ScrawlRules.SlamStampScale, 1, gold: false, slam: true,
                ScrawlRules.SlamDots, 2.2, 4.4, 0.6);
            if (_motion != MotionLevel.Off)
                _waves.Add(new Wave { X = x, Y = y, Reach = screenWidth * ScrawlRules.WaveReach });
            Emit(x, y, ScrawlRules.SlamMintSparks, 1, 300, 0.9);
            Emit(x, y, ScrawlRules.SlamPinkSparks, 0, 240, 0.9);
        }

        /// <summary>Advance every timer by dt. <paramref name="motion"/> is read once per frame by the caller.</summary>
        public void Tick(double dt, MotionLevel motion)
        {
            _motion = motion;
            for (int i = _stamps.Count - 1; i >= 0; i--)
            {
                var s = _stamps[i];
                s.Age += dt;
                if (s.Pulse >= 0) s.Pulse = s.Pulse + dt > ScrawlRules.PulseLife ? -1 : s.Pulse + dt;
                if (s.Age >= (s.Slam ? ScrawlRules.SlamStampLife : ScrawlRules.StampLife)) _stamps.RemoveAt(i);
            }
            for (int i = _glows.Count - 1; i >= 0; i--)
            {
                var g = _glows[i]; g.Age += dt;
                if (g.Age >= ScrawlRules.GlowLife) _glows.RemoveAt(i); else _glows[i] = g;
            }
            for (int i = _waves.Count - 1; i >= 0; i--)
            {
                var w = _waves[i]; w.Age += dt;
                if (w.Age >= ScrawlRules.WaveLife) _waves.RemoveAt(i); else _waves[i] = w;
            }
            double drag = Math.Max(0, 1 - dt * 2);
            for (int i = _sparkCount - 1; i >= 0; i--)
            {
                ref var p = ref _sparks[i];
                p.T += dt;
                if (p.T >= p.Life) { _sparks[i] = _sparks[--_sparkCount]; continue; }
                p.X += p.Vx * dt; p.Y += p.Vy * dt; p.Vx *= drag; p.Vy *= drag;
            }
            if (_labelAge >= 0) { _labelAge += dt; if (_labelAge >= ScrawlRules.FloatLife) { _labelAge = -1; _label = null; } }

            double gain = ScrawlRules.ShakeGain(motion);
            if (_shake > 0 && gain > 0)
            {
                double amp = ScrawlRules.ShakePixels * _k * _shake * gain;
                ShakeX = (_rng.NextDouble() - 0.5) * amp;
                ShakeY = (_rng.NextDouble() - 0.5) * amp;
            }
            else { ShakeX = 0; ShakeY = 0; }
            _shake = ScrawlRules.DecayShake(_shake, dt);
        }

        /// <summary>Draw in virtual DIP. The layer's own transform moves it into its window.</summary>
        public void Render(DrawingContext dc)
        {
            foreach (var s in _stamps)
            {
                double life = s.Slam ? ScrawlRules.SlamStampLife : ScrawlRules.StampLife;
                double al = ScrawlRules.StampAlpha(s.Age, life, s.Pulse);
                if (al <= 0.003) continue;
                double sc = ScrawlRules.PressScale(s.Age, s.Slam, _motion);
                bool scaled = sc > 1.0001;
                if (scaled) dc.PushTransform(new ScaleTransform(sc, sc, s.X, s.Y));
                dc.PushOpacity(al);
                if (s.Slam || s.Gold) dc.DrawGeometry(null, _haloPens[s.Ink], s.Text);
                dc.DrawGeometry(FillBrushes[s.Ink], s.Slam ? _slamPens[s.Ink] : _strokePens[s.Ink], s.Text);
                if (s.Dots != null) dc.DrawGeometry(DotBrushes[s.Ink], null, s.Dots);
                dc.Pop();
                if (scaled) dc.Pop();
            }
            double gr = ScrawlRules.GlowRadius * _fs;
            foreach (var g in _glows)
            {
                dc.PushOpacity(1 - g.Age / ScrawlRules.GlowLife);
                dc.DrawEllipse(GlowBrushes[g.Ink], null, new Point(g.X, g.Y), gr, gr);
                dc.Pop();
            }
            foreach (var w in _waves)
            {
                double u = w.Age / ScrawlRules.WaveLife;
                for (int j = 0; j < 2; j++)
                {
                    double uu = Math.Clamp(u - j * ScrawlRules.WaveLag, 0, 1);
                    if (uu <= 0) continue;
                    dc.PushOpacity(1 - uu);
                    dc.DrawEllipse(null, j == 0 ? _wavePen0 : _wavePen1, new Point(w.X, w.Y), uu * w.Reach, uu * w.Reach);
                    dc.Pop();
                }
            }
            for (int i = 0; i < _sparkCount; i++)
            {
                ref var p = ref _sparks[i];
                double a = 1 - p.T / p.Life;
                dc.PushOpacity(a);
                double r = (1 + 2.2 * a) * _k;
                dc.DrawEllipse(SparkBrushes[p.Ink], null, new Point(p.X, p.Y), r, r);
                dc.Pop();
            }
            if (_label != null && _labelAge >= 0)
            {
                double u = _labelAge / ScrawlRules.FloatLife;
                dc.PushOpacity(1 - u * u);
                dc.DrawText(_label, new Point(_labelX - _label.Width / 2, _labelY - _label.Height / 2 - u * _fs * 2));
                dc.Pop();
            }
        }

        /// <summary>Every stamp's box, for the OCR self-exclusion list (the stamps are the player's text too).</summary>
        public void AppendRects(List<Rect> into)
        {
            foreach (var s in _stamps)
                into.Add(new Rect(s.X - s.Reach, s.Y - s.Reach, s.Reach * 2, s.Reach * 2));
        }

        public void Clear()
        {
            _stamps.Clear(); _glows.Clear(); _waves.Clear(); _sparkCount = 0;
            _label = null; _labelAge = -1; _shake = 0; ShakeX = 0; ShakeY = 0;
        }

        private void AddStamp(string word, StampPose pose, double scale, int ink, bool gold, bool slam,
            int dots, double near, double far, double flatten)
        {
            var ft = new FormattedText(word, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _typeface,
                _fs * scale, Brushes.White, _ppd);
            var place = new TransformGroup();
            place.Children.Add(new RotateTransform(pose.Rotation * 180 / Math.PI));
            place.Children.Add(new TranslateTransform(pose.X, pose.Y));
            place.Freeze();
            var text = ft.BuildGeometry(new Point(-ft.Width / 2, -ft.Height / 2));
            text.Transform = place;
            text.Freeze();

            GeometryGroup? dotGeo = null;
            if (dots > 0)
            {
                Span<(double X, double Y, double R)> d = stackalloc (double, double, double)[dots];
                ScrawlRules.Dots(_rng, dots, _fs, near, far, flatten, 0.8 * _k, (slam ? 3.4 : 2.4) * _k, d);
                dotGeo = new GeometryGroup { Transform = place };
                foreach (var q in d) dotGeo.Children.Add(new EllipseGeometry(new Point(q.X, q.Y), q.R, q.R));
                dotGeo.Freeze();
            }
            _stamps.Add(new Stamp
            {
                Text = text, Dots = dotGeo, Ink = ink, Gold = gold, Slam = slam, X = pose.X, Y = pose.Y,
                Reach = Math.Max(ft.Width, ft.Height) / 2 + (dots > 0 ? far * _fs : 0),
            });
            if (!slam && _stamps.Count > ScrawlRules.StampCap)
            {
                int oldest = _stamps.FindIndex(s => !s.Slam);
                if (oldest >= 0) _stamps.RemoveAt(oldest);
            }
        }

        private void Emit(double x, double y, int n, int ink, double speed, double life)
        {
            if (_motion == MotionLevel.Off) return;
            double sp = speed * _k * (_motion == MotionLevel.Reduced ? 0.5 : 1);
            for (int i = 0; i < n; i++)
            {
                if (_sparkCount >= MaxSparks) return;
                double a = _rng.NextDouble() * Math.PI * 2, s = sp * (0.3 + _rng.NextDouble() * 0.7);
                _sparks[_sparkCount++] = new Spark
                {
                    X = x, Y = y, Vx = Math.Cos(a) * s, Vy = Math.Sin(a) * s,
                    Life = life * (0.6 + _rng.NextDouble() * 0.6), Ink = ink,
                };
            }
        }

        private Pen[] Pens(double width, double alpha)
        {
            var pens = new Pen[Inks.Length];
            for (int i = 0; i < pens.Length; i++)
                pens[i] = Freeze(new Pen(new SolidColorBrush(Alpha(Inks[i], alpha)), width) { LineJoin = PenLineJoin.Round });
            return pens;
        }

        private static Color Alpha(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

        private static Brush[] Frozen(Func<Color, Brush> make)
        {
            var arr = new Brush[Inks.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = Freeze(make(Inks[i]));
            return arr;
        }

        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }

    /// <summary>
    /// The click-through surface one bouncing-text window draws the Scrawl scene on. It sits first
    /// in the window's canvas, so the stamps are always under the words.
    /// </summary>
    internal sealed class ScrawlLayer : FrameworkElement
    {
        private readonly ScrawlScene _scene;

        public ScrawlLayer(ScrawlScene scene, double originX, double originY)
        {
            _scene = scene;
            IsHitTestVisible = false;
            Focusable = false;
            RenderTransform = Freeze(new TranslateTransform(-originX, -originY));
        }

        protected override void OnRender(DrawingContext dc) => _scene.Render(dc);

        private static Transform Freeze(Transform t) { t.Freeze(); return t; }
    }
}
