using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The Premium page's "this one is yours" livery (polish 12 round 2, owner: "make it feel
    /// rich"): a soft rim glow in the card's tier colour that breathes, and now and then a
    /// four-point glint on the top-right corner. Drawn on an adorner with gradient-free pens and a
    /// small shape child, never an Effect (Depth law: no Effect on chrome).
    ///
    /// <para>Motion: Full = breath + glint; Reduced = a slow, shallow breath, no glint; Off = a
    /// static rim at mid strength. One opacity clock per card at 24 fps, parked with the page.</para>
    /// </summary>
    internal sealed class VaultCardAura : Adorner
    {
        /// <summary>Breath period at Full, seconds (each card adds its own small offset).</summary>
        internal const double BreathSeconds = 2.6;
        /// <summary>Glint period range at Full, seconds.</summary>
        internal const double GlintMinSeconds = 5.0, GlintMaxSeconds = 9.0;
        internal const double StaticOpacity = 0.6;
        private const double GlintSize = 26;
        private const int FrameRate = 24;

        private readonly Pen[] _halo;
        private readonly Pen _rim;
        private readonly double _radius;
        private readonly Path _glint;
        private readonly ScaleTransform _glintScale = new(0.2, 0.2);
        private readonly RotateTransform _glintTurn = new(0);
        private readonly VisualCollection _children;

        /// <summary>True while a breath clock runs (tests).</summary>
        internal bool Breathing { get; private set; }

        /// <summary>True while the glint clock runs (tests).</summary>
        internal bool Glinting { get; private set; }

        internal Color Hue { get; }

        internal VaultCardAura(UIElement adorned, Color hue, double cornerRadius) : base(adorned)
        {
            IsHitTestVisible = false;
            Hue = hue;
            _radius = Math.Max(0, cornerRadius);
            _halo = new Pen[5];
            byte[] alphas = { 0x5A, 0x40, 0x2A, 0x18, 0x0C };
            for (int i = 0; i < _halo.Length; i++)
                _halo[i] = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(alphas[i], hue.R, hue.G, hue.B))), 3));
            _rim = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xC8, hue.R, hue.G, hue.B))), 1.5));

            var core = new RadialGradientBrush
            {
                GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Color.FromArgb(0xE0, hue.R, hue.G, hue.B), 0.45), new GradientStop(Color.FromArgb(0, hue.R, hue.G, hue.B), 1) },
            };
            core.Freeze();
            _glint = new Path
            {
                Data = Star(GlintSize),
                Fill = core,
                Width = GlintSize,
                Height = GlintSize,
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TransformGroup { Children = { _glintScale, _glintTurn } },
            };
            _children = new VisualCollection(this) { _glint };
            Opacity = StaticOpacity;
        }

        /// <summary>A four-point sparkle: concave arms meeting in a bright centre.</summary>
        internal static Geometry Star(double size)
        {
            double c = size / 2, r = size / 2, k = size * 0.09;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c, c - r), true, true);
                ctx.QuadraticBezierTo(new Point(c + k, c - k), new Point(c + r, c), true, true);
                ctx.QuadraticBezierTo(new Point(c + k, c + k), new Point(c, c + r), true, true);
                ctx.QuadraticBezierTo(new Point(c - k, c + k), new Point(c - r, c), true, true);
                ctx.QuadraticBezierTo(new Point(c - k, c - k), new Point(c, c - r), true, true);
            }
            g.Freeze();
            return g;
        }

        // Null-safe: the base constructor and property setters ask before our fields exist.
        protected override int VisualChildrenCount => _children?.Count ?? 0;
        protected override Visual GetVisualChild(int index) => _children[index];

        protected override Size MeasureOverride(Size constraint)
        {
            _glint.Measure(new Size(GlintSize, GlintSize));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = AdornedElement.RenderSize;
            // On the top-right corner, a little outside it, where a light would catch the edge.
            _glint.Arrange(new Rect(size.Width - GlintSize * 0.62, -GlintSize * 0.38, GlintSize, GlintSize));
            return finalSize;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = AdornedElement.RenderSize;
            if (size.Width <= 0 || size.Height <= 0) return;
            var r = new Rect(size);
            for (int i = _halo.Length - 1; i >= 0; i--)
            {
                var g = r;
                g.Inflate(2 + i * 3, 2 + i * 3);
                dc.DrawRoundedRectangle(null, _halo[i], g, _radius + 2 + i * 3, _radius + 2 + i * 3);
            }
            var inner = r;
            inner.Inflate(-0.75, -0.75);
            dc.DrawRoundedRectangle(null, _rim, inner, _radius, _radius);
        }

        /// <summary>Starts (or restarts) the loops for <paramref name="level"/>. <paramref name="seed"/>
        /// spreads the clocks so a row of cards never breathes in step.</summary>
        internal void Start(MotionLevel level, int seed)
        {
            Stop();
            double jitter = (Math.Abs(seed) % 7) * 0.13;
            if (level == MotionLevel.Off)
            {
                Opacity = StaticOpacity;
                return;
            }

            bool full = level == MotionLevel.Full;
            var breath = new DoubleAnimation(full ? 0.32 : 0.5, full ? 0.95 : 0.72,
                TimeSpan.FromSeconds((full ? BreathSeconds : BreathSeconds * 2) + jitter))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                BeginTime = TimeSpan.FromSeconds(-jitter * 3),
            };
            Timeline.SetDesiredFrameRate(breath, FrameRate);
            BeginAnimation(OpacityProperty, breath);
            Breathing = true;
            if (!full) return;

            double period = GlintMinSeconds + (Math.Abs(seed * 37) % 100) / 100.0 * (GlintMaxSeconds - GlintMinSeconds);
            var begin = TimeSpan.FromSeconds(0.6 + (Math.Abs(seed * 13) % 100) / 100.0 * 2.4);
            var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(period), RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), new QuadraticEase()));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            var grow = new DoubleAnimationUsingKeyFrames { Duration = fade.Duration, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
            grow.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            grow.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)), new BackEase { Amplitude = 0.4 }));
            grow.KeyFrames.Add(new EasingDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620))));
            var turnLoop = new DoubleAnimationUsingKeyFrames { Duration = fade.Duration, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
            turnLoop.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            turnLoop.KeyFrames.Add(new LinearDoubleKeyFrame(90, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620))));
            foreach (var t in new Timeline[] { fade, grow, turnLoop }) Timeline.SetDesiredFrameRate(t, FrameRate);
            _glint.BeginAnimation(UIElement.OpacityProperty, fade);
            _glintScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            _glintScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            _glintTurn.BeginAnimation(RotateTransform.AngleProperty, turnLoop);
            Glinting = true;
        }

        /// <summary>Parks every clock and leaves the static rim.</summary>
        internal void Stop()
        {
            BeginAnimation(OpacityProperty, null);
            _glint.BeginAnimation(UIElement.OpacityProperty, null);
            _glintScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _glintScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _glintTurn.BeginAnimation(RotateTransform.AngleProperty, null);
            _glint.Opacity = 0;
            Opacity = StaticOpacity;
            Breathing = Glinting = false;
        }

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }
}
