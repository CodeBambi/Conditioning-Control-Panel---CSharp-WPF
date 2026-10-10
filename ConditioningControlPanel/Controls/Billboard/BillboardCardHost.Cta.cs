using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// The card button's pop (owner, 2026-10-07: "make the buttons pop there too"): a glow in the
    /// card's hue that breathes under the plank, a shine that crosses the face now and then, a
    /// bigger hover (scale, glow up, a shine and a few glints) and a squash on press. FX PERF RULES:
    /// the glow is a sibling layer with a BitmapCache whose Opacity animates, never an Effect; the
    /// shine is a brush translation on one small rectangle. Depth law stays with Lift / PressDown /
    /// PressUp (2 px travel, 90 ms, release overshoot); this only adds to it. Ambient loops off = a
    /// still glow and no shine; Motion Off = nothing moves.
    /// </summary>
    public sealed partial class BillboardCardHost
    {
        internal const double CtaGlowIdle = 0.32, CtaGlowBreath = 0.62, CtaGlowHover = 0.95;
        internal const double CtaHoverScale = 1.05;
        internal const double CtaBreathSeconds = 1.6, CtaShineSeconds = 0.6, CtaShineEverySeconds = 4.6;

        private readonly Border _ctaGlow = new();
        private readonly Rectangle _ctaShine = new() { IsHitTestVisible = false, RadiusX = 9, RadiusY = 9 };
        private readonly TranslateTransform _ctaShineMove = new(-1.2, 0);
        private readonly ScaleTransform _ctaSquash = new(1, 1);
        private readonly ScaleTransform _ctaHover = new(1, 1);
        private bool _ctaHovered;
        private int _ctaFxToken;

        /// <summary>True while the button's idle breath runs (tests).</summary>
        internal bool CtaLoopsRunning { get; private set; }

        /// <summary>The glow layer, the shine and the squash, wired into the button the CTA build made.</summary>
        private void BuildCtaFx(Grid root, Grid faceGrid)
        {
            _ctaGlow.CornerRadius = new CornerRadius(18);
            _ctaGlow.Margin = new Thickness(-10, -8, -10, -6);
            _ctaGlow.IsHitTestVisible = false;
            _ctaGlow.Opacity = CtaGlowIdle;
            _ctaGlow.CacheMode = new BitmapCache { SnapsToDevicePixels = false };
            root.Children.Insert(0, _ctaGlow);

            var shine = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), RelativeTransform = _ctaShineMove };
            shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xff, 0xff, 0xff), 0.36));
            shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x8c, 0xff, 0xf6, 0xfc), 0.5));
            shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xff, 0xff, 0xff), 0.64));
            _ctaShine.Fill = shine;
            faceGrid.Children.Insert(Math.Min(1, faceGrid.Children.Count), _ctaShine);

            _ctaFace.RenderTransformOrigin = new Point(0.5, 1);
            _ctaFace.RenderTransform = new TransformGroup { Children = { _ctaSquash, _ctaPress } };
            ((TransformGroup)root.RenderTransform).Children.Add(_ctaHover);
            _cta.IsVisibleChanged += (_, _) => RefreshCtaLoops();
        }

        /// <summary>A card landed or settled: tint the glow and (re)start the breath.</summary>
        private void RefreshCtaFx(bool landed, TimeSpan shineAt)
        {
            var hue = _card != null ? HueOf(_card) : Color.FromRgb(0xff, 0x4f, 0xa8);
            var g = new RadialGradientBrush(Color.FromArgb(0xb0, hue.R, hue.G, hue.B), Color.FromArgb(0, hue.R, hue.G, hue.B))
            {
                RadiusX = 0.5,
                RadiusY = 0.5,
            };
            g.GradientStops.Insert(1, new GradientStop(Color.FromArgb(0x70, hue.R, hue.G, hue.B), 0.55));
            g.Freeze();
            _ctaGlow.Background = g;
            if (landed && MotionFx.AllowTransitions && _cta.Visibility == Visibility.Visible)
            {
                // The landing flare: the glow blooms with the thud and settles into the breath.
                int token = ++_ctaFxToken;
                var flare = new DoubleAnimation(CtaGlowHover, CtaGlowIdle, TimeSpan.FromMilliseconds(DepthRules.Ms(700, MotionFx.Level))) { BeginTime = shineAt };
                flare.Completed += (_, _) => { if (token == _ctaFxToken) RefreshCtaLoops(); };
                _ctaGlow.BeginAnimation(OpacityProperty, flare);
                ShineOnce(shineAt);
                CtaLoopsRunning = false;
                return;
            }
            RefreshCtaLoops();
        }

        /// <summary>The idle breath and the periodic shine, from the gates there are.</summary>
        private void RefreshCtaLoops()
        {
            int token = ++_ctaFxToken;
            bool loops = MotionFx.AllowAmbientLoops && _cta.IsVisible;
            if (_ctaHovered && MotionFx.AllowTransitions && _cta.IsVisible)
            {
                _ctaGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(CtaGlowHover, TimeSpan.FromMilliseconds(160)));
                CtaLoopsRunning = false;
                return;
            }
            if (!loops)
            {
                _ctaGlow.BeginAnimation(OpacityProperty, null);
                _ctaGlow.Opacity = CtaGlowIdle;
                _ctaShineMove.BeginAnimation(TranslateTransform.XProperty, null);
                _ctaShineMove.X = -1.2;
                CtaLoopsRunning = false;
                return;
            }

            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            ease.Freeze();
            // Ease from wherever the glow is to the floor, then breathe floor <-> peak forever.
            var settle = new DoubleAnimation(CtaGlowIdle, TimeSpan.FromMilliseconds(260));
            settle.Completed += (_, _) =>
            {
                if (token != _ctaFxToken) return;
                _ctaGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(CtaGlowIdle, CtaGlowBreath, TimeSpan.FromSeconds(CtaBreathSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = ease,
                });
            };
            _ctaGlow.BeginAnimation(OpacityProperty, settle);

            var sweep = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(1.2) };
            sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(-1.2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            sweep.KeyFrames.Add(new EasingDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(CtaShineSeconds)), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
            sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(CtaShineEverySeconds))));
            _ctaShineMove.BeginAnimation(TranslateTransform.XProperty, sweep);
            CtaLoopsRunning = true;
        }

        /// <summary>One shine across the face (hover, landing).</summary>
        private void ShineOnce(TimeSpan begin)
        {
            if (!MotionFx.AllowTransitions) return;
            int ms = Math.Max(1, DepthRules.Ms((int)(CtaShineSeconds * 1000), MotionFx.Level));
            var once = new DoubleAnimation(-1.2, 1.2, TimeSpan.FromMilliseconds(ms))
            {
                BeginTime = begin,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd,
            };
            _ctaShineMove.BeginAnimation(TranslateTransform.XProperty, once);
        }

        /// <summary>Hover: the plank swells, the glow comes up, a shine crosses it and a few glints
        /// jump off its corner. Leaving eases it all back into the breath.</summary>
        private void CtaHover(bool on)
        {
            _ctaHovered = on;
            if (!MotionFx.AllowTransitions)
            {
                _ctaHover.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _ctaHover.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _ctaHover.ScaleX = _ctaHover.ScaleY = 1;
                RefreshCtaLoops();
                return;
            }
            int ms = DepthRules.Ms(on ? 260 : 160, MotionFx.Level);
            var to = on ? CtaHoverScale : 1.0;
            IEasingFunction ease = on ? CubicBezierEase.Thud() : new QuadraticEase { EasingMode = EasingMode.EaseOut };
            _ctaHover.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
            _ctaHover.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
            if (on)
            {
                ++_ctaFxToken;
                CtaLoopsRunning = false;
                _ctaGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(CtaGlowHover, TimeSpan.FromMilliseconds(160)));
                ShineOnce(TimeSpan.Zero);
                if (_card != null && _cta.ActualWidth > 0)
                {
                    var corner = _cta.TranslatePoint(new Point(_cta.ActualWidth - 8, 6), _stage);
                    Burst(corner, Lighter(HueOf(_card)), 6, 0.32);
                }
            }
            else RefreshCtaLoops();
        }

        /// <summary>Press: the face squashes into its socket (wider, shorter) with the 2 px sink;
        /// release springs it back past round and settles.</summary>
        private void CtaSquash(bool down)
        {
            if (!MotionFx.AllowTransitions)
            {
                _ctaSquash.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _ctaSquash.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _ctaSquash.ScaleX = _ctaSquash.ScaleY = 1;
                return;
            }
            if (down)
            {
                var dur = TimeSpan.FromMilliseconds(Math.Max(1, DepthRules.Ms(DepthRules.PressMs, MotionFx.Level)));
                _ctaSquash.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.07, dur));
                _ctaSquash.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, dur));
                return;
            }
            int ms = Math.Max(1, DepthRules.Ms((int)(DepthRules.ReleaseMs * 1.6), MotionFx.Level));
            _ctaSquash.BeginAnimation(ScaleTransform.ScaleXProperty, Spring(0.95, ms));
            _ctaSquash.BeginAnimation(ScaleTransform.ScaleYProperty, Spring(1.07, ms));
        }

        private static DoubleAnimationUsingKeyFrames Spring(double past, int ms)
        {
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new EasingDoubleKeyFrame(past, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * 0.45)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
            return a;
        }

        private static Color Lighter(Color c) => Color.FromRgb(
            (byte)(c.R + (255 - c.R) * 0.5), (byte)(c.G + (255 - c.G) * 0.5), (byte)(c.B + (255 - c.B) * 0.5));
    }
}
