using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// The living poster's motion, pure (owner, 2026-10-07: "give those still images the same
    /// treatment we gave to the animated logo"). The poster lives at rest and quickens on hover, on
    /// the logo's own drive (<see cref="AnimatedLogoImage.Drive"/>) and clock
    /// (<see cref="AnimatedLogoImage.PhaseRate"/>): a slow Ken Burns breath and pan, a shallow lean
    /// toward the pointer, a small zoom pop when the card lands. Every pose keeps the picture covering
    /// the card, and the settled crop never passes about 4 percent.
    /// </summary>
    internal static class PosterMotion
    {
        /// <summary>The settled zoom floor: 3.4 percent of overscan to spend on motion.</summary>
        internal const double BaseScale = 1.034;
        /// <summary>The Ken Burns breath on top of the floor (settled max 1.039).</summary>
        internal const double BreathScale = 0.005;
        /// <summary>The landing pop's extra zoom at its peak, gone in <see cref="LandSeconds"/>.</summary>
        internal const double LandKick = 0.010;
        internal const double LandSeconds = 0.62;
        /// <summary>Slow pan, as a fraction of the card's width and height.</summary>
        internal const double PanX = 0.0035, PanY = 0.0025;
        /// <summary>Pointer parallax, as a fraction of width and height at full hover.</summary>
        internal const double ParallaxX = 0.005, ParallaxY = 0.004;
        /// <summary>The lean toward the pointer and the idle sway, in degrees, before the aspect cap.</summary>
        internal const double LeanDegrees = 0.45, SwayDegrees = 0.12;
        /// <summary>How far a lean may move a far corner, as a fraction of the short axis.</summary>
        internal const double LeanShiftCap = 0.006;

        internal readonly record struct Pose(double Scale, double X, double Y, double SkewX, double SkewY);

        /// <summary>The still pose (Motion Off, ambient loops off, never played).</summary>
        internal static Pose Still => new(BaseScale, 0, 0, 0, 0);

        /// <summary>The landing pop's zoom at <paramref name="seconds"/> after the card landed:
        /// up to the kick, a small dip past rest, then rest.</summary>
        internal static double Land(double seconds)
        {
            if (!(seconds >= 0) || seconds >= LandSeconds) return 0;
            double u = seconds / LandSeconds;
            // A damped spring: the kick at the start, a small dip past rest near two thirds, 0 at the end.
            return LandKick * Math.Cos(u * Math.PI * 1.5) * (1 - u) * (1 - u);
        }

        /// <summary>
        /// The pose at clock <paramref name="phase"/> (radians), pointer <paramref name="pointer"/>
        /// (-1..1 each axis, already eased), hover <paramref name="energy"/> (0..1) and
        /// <paramref name="land"/> (extra zoom from <see cref="Land"/>), for a card w x h.
        /// </summary>
        internal static Pose At(double phase, Vector pointer, double energy, double land, double w, double h)
        {
            if (!double.IsFinite(phase)) phase = 0;
            energy = double.IsFinite(energy) ? Math.Clamp(energy, 0, 1) : 0;
            double px = double.IsFinite(pointer.X) ? Math.Clamp(pointer.X, -1, 1) : 0;
            double py = double.IsFinite(pointer.Y) ? Math.Clamp(pointer.Y, -1, 1) : 0;
            w = Math.Max(1, w); h = Math.Max(1, h);

            double scale = BaseScale + BreathScale * 0.5 * (1 - Math.Cos(phase)) + Math.Clamp(land, -LandKick, LandKick);
            double x = PanX * w * Math.Sin(phase * 0.7 + 0.4) - ParallaxX * w * px * energy;
            double y = PanY * h * Math.Sin(phase * 0.53 + 1.1) - ParallaxY * h * py * energy;

            // The lean: toward the pointer on hover, a slow sway at rest. A lean of a degrees moves a far
            // corner by half the OTHER axis times tan(a), so the cap follows the card's aspect.
            double capY = Math.Atan(LeanShiftCap * 2 * h / w) * 180 / Math.PI;
            double capX = Math.Atan(LeanShiftCap * 2 * w / h) * 180 / Math.PI;
            double skewY = Math.Clamp(-px * energy * LeanDegrees + SwayDegrees * Math.Sin(phase * 0.9), -capY, capY);
            double skewX = Math.Clamp(py * energy * LeanDegrees * 0.7 + SwayDegrees * 0.6 * Math.Sin(phase * 0.6 + 2), -capX, capX);
            return EnsureCover(new Pose(scale, x, y, skewX, skewY), w, h);
        }

        /// <summary>The image matrix for a pose, about the card's centre (render origin 0,0).</summary>
        internal static Matrix ToMatrix(Pose p, double w, double h)
        {
            double cx = w / 2, cy = h / 2;
            var m = Matrix.Identity;
            m.Translate(-cx, -cy);
            m.Skew(p.SkewX, p.SkewY);
            m.Scale(p.Scale, p.Scale);
            m.Translate(cx + p.X, cy + p.Y);
            return m;
        }

        /// <summary>True when the transformed picture still covers every corner of the card.</summary>
        internal static bool Covers(Pose p, double w, double h)
        {
            var m = ToMatrix(p, w, h);
            if (!m.HasInverse) return false;
            m.Invert();
            const double eps = 1e-6;
            foreach (var c in new[] { new Point(0, 0), new Point(w, 0), new Point(0, h), new Point(w, h) })
            {
                var s = m.Transform(c);
                if (s.X < -eps || s.Y < -eps || s.X > w + eps || s.Y > h + eps) return false;
            }
            return true;
        }

        /// <summary>The safety net: zooms in a hair until the picture covers the card (never needed by
        /// the budgets above on a 16:9 or wider card, but a strange card shape must not show an edge).</summary>
        internal static Pose EnsureCover(Pose p, double w, double h)
        {
            for (int i = 0; i < 40 && !Covers(p, w, h); i++) p = p with { Scale = p.Scale + 0.001 };
            return p;
        }

        /// <summary>Where the sheen band is, -1 (off) or 0..1 across the card. The band sweeps for
        /// <see cref="SweepSeconds"/> at the start of every cycle; hover runs the cycle three times as fast.</summary>
        internal static double Sheen(double sheenSeconds)
        {
            if (!(sheenSeconds >= 0)) return -1;
            double inCycle = sheenSeconds % SheenCycleSeconds;
            return inCycle < SweepSeconds ? inCycle / SweepSeconds : -1;
        }

        internal const double SheenCycleSeconds = 7.5, SweepSeconds = 1.3;
    }

    /// <summary>
    /// A 16:9 poster that lives a little (house cards: Discord, Web App, Remix, Loom, Support). The
    /// picture breathes and pans on a matrix, leans toward the pointer, and a light overlay paints a
    /// sheen now and then and a few drifting glints, all faster on hover. FX PERF RULES: one
    /// <see cref="FrameClock"/> at 30 fps; a tick sets one matrix and re-opens one
    /// <see cref="DrawingGroup"/>; no Effect, no layout. Ambient loops off = the still pose; Motion
    /// Off = no motion at all. Pause and Release stop the clock.
    /// </summary>
    public sealed class PosterArtView : Grid, IBillboardArtView, IAccentedArt
    {
        private const int MaxMotes = 16;
        private static readonly Color DefaultTint = Color.FromRgb(0xff, 0xd3, 0xf9);

        private readonly CoverImage _image = new();
        private readonly MatrixTransform _matrix = new();
        private readonly PosterLight _light = new();
        private readonly Stopwatch _watch = new();
        private FrameClock? _clock;
        private double _last, _phase, _energy, _sheenT = PosterMotion.SweepSeconds, _moteT, _landT = -1;
        private Vector _pointer;
        private bool _released, _landed;

        public PosterArtView(string? path)
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0d, 0x1a));
            _image.RenderTransform = _matrix;
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            var uri = BuiltInArt.PosterUri(path);
            if (uri != null)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(uri, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    _image.Source = bmp;
                }
                catch (Exception ex) { App.Logger?.Debug("Billboard poster {P} did not load: {E}", path, ex.Message); }
            }
            Children.Add(_image);
            Children.Add(_light);
            SizeChanged += (_, _) => Apply();
            Apply();
        }

        /// <summary>The card's hue: the glints and the pointer light lean toward it.</summary>
        public Color Accent { get => _light.Tint; set => _light.Tint = value; }

        /// <summary>True while the motion clock runs (tests).</summary>
        internal bool IsAnimating => _clock?.IsEnabled == true;

        /// <summary>The image matrix on screen now (tests).</summary>
        internal Matrix CurrentMatrix => _matrix.Matrix;

        public void Play()
        {
            if (_released) return;
            if (!MotionFx.AllowAmbientLoops)
            {
                StopClock();
                _phase = 0; _energy = 0; _landT = -1; _pointer = default;
                _light.Clear();
                Apply();
                return;
            }
            if (!_landed)
            {
                _landed = true;
                if (MotionFx.AllowTransitions) { _landT = 0; _sheenT = 0; }
            }
            _clock ??= new FrameClock { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            _clock.Tick -= OnTick;
            _clock.Tick += OnTick;
            _last = 0;
            _watch.Restart();
            _clock.Start();
        }

        public void Pause() => StopClock();

        public void Release()
        {
            StopClock();
            if (_clock != null) _clock.Tick -= OnTick;
            _clock = null;
            _released = true;
            _image.Source = null;
            _light.Clear();
        }

        public void Touch(Point normalized) { }

        private void StopClock()
        {
            _clock?.Stop();
            _watch.Reset();
        }

        /// <summary>One frame: ease the hover, advance the clocks, set the matrix, paint the light.</summary>
        internal void Step(double dt, bool hovered, Point pointerNormalized)
        {
            dt = Math.Clamp(dt, 0, 0.1);
            double target = hovered ? 1 : 0;
            bool rising = target > _energy;
            if (rising && _energy < 0.05 && PosterMotion.Sheen(_sheenT) < 0) _sheenT = 0; // a sweep greets the pointer
            _energy += (target - _energy) * (1 - Math.Exp(-dt / (rising ? .18 : .48)));
            var aim = hovered ? new Vector(pointerNormalized.X * 2 - 1, pointerNormalized.Y * 2 - 1) : new Vector(0, 0);
            _pointer += (aim - _pointer) * (1 - Math.Exp(-dt / .22));
            _phase = (_phase + dt * AnimatedLogoImage.PhaseRate(_energy)) % Math.Tau;
            _sheenT += dt * (1 + 2 * _energy);
            _moteT += dt * (0.55 + 0.9 * _energy);
            if (_landT >= 0) { _landT += dt; if (_landT >= PosterMotion.LandSeconds) _landT = -1; }
            Apply();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_released || !IsVisible) return;
            if (!MotionFx.AllowAmbientLoops) { Play(); return; }
            double now = _watch.Elapsed.TotalSeconds, dt = now - _last;
            _last = now;
            double w = ActualWidth, h = ActualHeight;
            bool hovered = false;
            var at = new Point(0.5, 0.5);
            try
            {
                if (w > 0 && h > 0 && Window.GetWindow(this) is { IsMouseOver: true })
                {
                    var p = Mouse.GetPosition(this);
                    hovered = p.X >= 0 && p.Y >= 0 && p.X <= w && p.Y <= h;
                    if (hovered) at = new Point(p.X / w, p.Y / h);
                }
            }
            catch { hovered = false; }
            Step(dt, hovered, at);
        }

        private void Apply()
        {
            double w = ActualWidth, h = ActualHeight;
            if (!(w > 0) || !(h > 0)) return;
            bool live = _clock?.IsEnabled == true || _landT >= 0 || _energy > 0;
            var pose = live
                ? PosterMotion.At(_phase, _pointer, _energy, _landT >= 0 ? PosterMotion.Land(_landT) : 0, w, h)
                : PosterMotion.EnsureCover(PosterMotion.Still, w, h);
            _matrix.Matrix = PosterMotion.ToMatrix(pose, w, h);
            if (live) _light.Paint(w, h, PosterMotion.Sheen(_sheenT), _moteT, _energy, _pointer, MotionFx.AllowParticles ? MaxMotes : 0);
        }

        /// <summary>The picture, cover-fitted and centred in exactly the card's box, so the pose
        /// maths (which treats the picture as the card's rectangle) is exact or conservative.</summary>
        private sealed class CoverImage : FrameworkElement
        {
            private ImageSource? _source;

            public CoverImage() { IsHitTestVisible = false; }

            public ImageSource? Source
            {
                get => _source;
                set { _source = value; InvalidateVisual(); }
            }

            protected override void OnRender(DrawingContext dc)
            {
                double w = ActualWidth, h = ActualHeight;
                if (_source == null || !(w > 0) || !(h > 0)) return;
                double iw = Math.Max(1, _source.Width), ih = Math.Max(1, _source.Height);
                double k = Math.Max(w / iw, h / ih);
                double dw = iw * k, dh = ih * k;
                // The art leans right (the old 0.62 origin): a wide picture keeps its right side.
                dc.DrawImage(_source, new Rect((w - dw) * 0.62, (h - dh) * 0.5, dw, dh));
            }
        }

        /// <summary>The light layer: the sheen band, the pointer's glow, a shade on the far side of the
        /// lean, and the glints. One retained drawing, re-opened per frame.</summary>
        private sealed class PosterLight : FrameworkElement
        {
            private readonly DrawingGroup _drawing = new();
            private LinearGradientBrush? _sheen;
            private RadialGradientBrush? _dot, _pool;
            private Pen? _glint;
            private Color _tint = DefaultTint;

            public PosterLight() { IsHitTestVisible = false; }

            public Color Tint
            {
                get => _tint;
                set { _tint = value; _sheen = null; _dot = null; _pool = null; _glint = null; }
            }

            public void Clear() { using (_drawing.Open()) { } }

            private void Build()
            {
                // The sheen leans toward the card's hue the way the logo's leans pink; never white.
                var sheen = Mix(DefaultTint, _tint, 0.35);
                _sheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                _sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0, sheen.R, sheen.G, sheen.B), 0));
                _sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x3c, sheen.R, sheen.G, sheen.B), 0.5));
                _sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0, sheen.R, sheen.G, sheen.B), 1));
                _sheen.Freeze();
                var glow = Mix(DefaultTint, _tint, 0.6);
                _dot = new RadialGradientBrush(Color.FromArgb(0xe6, glow.R, glow.G, glow.B), Color.FromArgb(0, glow.R, glow.G, glow.B));
                _dot.Freeze();
                _pool = new RadialGradientBrush(Color.FromArgb(0x38, glow.R, glow.G, glow.B), Color.FromArgb(0, glow.R, glow.G, glow.B));
                _pool.Freeze();
                _glint = new Pen(new SolidColorBrush(Color.FromArgb(0xd0, 0xff, 0xf0, 0xfb)), 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                _glint.Freeze();
            }

            public void Paint(double w, double h, double sheen, double moteT, double energy, Vector pointer, int maxMotes)
            {
                if (_sheen == null) Build();
                using var dc = _drawing.Open();

                // The pointer's light: a soft pool where the pointer is, only on hover.
                if (energy > 0.02)
                {
                    var c = new Point(w * (0.5 + pointer.X * 0.5), h * (0.5 + pointer.Y * 0.5));
                    dc.PushOpacity(Math.Clamp(energy, 0, 1));
                    dc.DrawEllipse(_pool, null, c, w * 0.32, w * 0.32);
                    dc.Pop();
                }

                // The sheen: a skewed band crossing the card now and then.
                if (sheen >= 0)
                {
                    double band = w * 0.22;
                    double x = -band * 1.5 + sheen * (w + band * 3);
                    double fade = Math.Sin(Math.PI * sheen);
                    dc.PushOpacity(Math.Clamp(fade * (0.75 + 0.25 * energy), 0, 1));
                    dc.PushTransform(new MatrixTransform(1, 0, -0.35, 1, x + h * 0.175, 0));
                    dc.DrawRectangle(_sheen, null, new Rect(0, 0, band, h));
                    dc.Pop();
                    dc.Pop();
                }

                // The glints: a few motes drifting up, more on hover, a cross on every third.
                int count = Math.Min(maxMotes, (int)Math.Round(5 + 11 * Math.Clamp(energy, 0, 1)));
                for (int i = 0; i < count; i++)
                {
                    double life = (moteT * 0.16 + i * 0.381966) % 1;
                    double seed = Math.Sin(i * 12.9898) * 43758.5453;
                    double fx = seed - Math.Floor(seed);
                    double x = w * (0.08 + 0.84 * fx) + Math.Sin(life * Math.Tau + i) * 8;
                    double y = h * (0.92 - 0.8 * life);
                    double a = Math.Pow(Math.Sin(life * Math.PI), 3) * (0.45 + 0.45 * energy);
                    if (a < 0.02) continue;
                    double r = 1.6 + (i % 3) * 0.8 + energy * 1.2;
                    dc.PushOpacity(a);
                    dc.DrawEllipse(_dot, null, new Point(x, y), r * 2.2, r * 2.2);
                    if (i % 3 == 0)
                    {
                        double s = r * 2.4;
                        dc.DrawLine(_glint, new Point(x - s, y), new Point(x + s, y));
                        dc.DrawLine(_glint, new Point(x, y - s), new Point(x, y + s));
                    }
                    dc.Pop();
                }
            }

            protected override void OnRender(DrawingContext dc) => dc.DrawDrawing(_drawing);

            private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
        }
    }
}
