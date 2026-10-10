using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// The juice, ported from the mockup and Breakout's rules: every move has an in and an out,
    /// nothing flashes white, nothing animates an Effect. Pushes and rises are transform and opacity
    /// animations (render-thread cheap); sparks and motes paint into one half-resolution
    /// <see cref="FxSurface"/> on a <see cref="FrameClock"/> that runs only while a particle lives.
    /// </summary>
    public sealed partial class BillboardCardHost
    {
        private const int MaxParticles = 160;

        private readonly FxSurface _fxSurface = new() { ResolutionScale = 0.5 };
        private readonly List<Particle> _particles = new();
        private readonly Random _rng = new();
        private FrameClock? _fxClock;
        private DateTime _fxLast;

        private struct Particle
        {
            public double X, Y, Vx, Vy, G, Life, Max, R;
            public SKColor Hue;
        }

        private void BuildFx()
        {
            _fxSurface.PaintSurface += PaintParticles;
            _stage.Children.Add(_fxSurface);
        }

        // ---- the push ----------------------------------------------------------------------------

        /// <summary>The new card pushes in from the right over the old one (450 ms, ease-out cubic),
        /// the old one slides a little left under it, and a glint in the new card's hue rides the seam.</summary>
        private void Push(Slide old, Slide next)
        {
            double w = Math.Max(1, _stage.ActualWidth);
            int ms = Math.Max(1, DepthRules.Ms(DashboardBillboard.PushMs, MotionFx.Level));
            var dur = TimeSpan.FromMilliseconds(ms);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ease.Freeze();

            var outMove = new DoubleAnimation(0, -w * DashboardBillboard.PushOldShift, dur) { EasingFunction = ease };
            outMove.Completed += (_, _) => Retire(old);
            old.Move.BeginAnimation(TranslateTransform.XProperty, outMove);

            double start = w * DashboardBillboard.PushNewStart;
            next.Move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(start, 0, dur) { EasingFunction = ease });
            var zoom = new DoubleAnimation(DashboardBillboard.PushNewScale, 1, dur) { EasingFunction = ease };
            next.Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
            next.Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);

            // The glint: a narrow band in the hue, its bright edge on the seam, fading as it lands.
            var hue = _card != null ? HueOf(_card) : Color.FromRgb(0xff, 0x4f, 0xa8);
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, hue.R, hue.G, hue.B), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x73, hue.R, hue.G, hue.B), 0.75));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, hue.R, hue.G, hue.B), 1));
            g.Freeze();
            _glint.Fill = g;
            double band = _glint.Width;
            _glintMove.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(start - band * 0.75, -band * 0.75, dur) { EasingFunction = ease });
            _glint.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, dur));
        }

        // ---- the words ---------------------------------------------------------------------------

        /// <summary>Eyebrow, title and line rise 60 ms apart; the button thuds in after them; the
        /// badge pops.</summary>
        private void LandWords(DeckCard card)
        {
            ResetFold();
            var thud = CubicBezierEase.Thud();
            int rise = DepthRules.Ms(DashboardBillboard.TextRiseMs, MotionFx.Level);
            int step = DepthRules.Ms(DashboardBillboard.TextStaggerMs, MotionFx.Level);
            int k = 1;
            foreach (var t in new[] { _eyebrow, _title, _line })
            {
                if (t.Visibility != Visibility.Visible) continue;
                Rise(t, step * k++, rise, thud);
            }

            var group = (TransformGroup)((FrameworkElement)_cta.Content).RenderTransform;
            var scale = (ScaleTransform)group.Children[0];
            var move = (TranslateTransform)group.Children[1];
            int thudMs = DepthRules.Ms(DashboardBillboard.ThudMs, MotionFx.Level);
            var begin = TimeSpan.FromMilliseconds(DepthRules.Ms(DashboardBillboard.ThudDelayMs, MotionFx.Level));
            Animate(_cta, OpacityProperty, 0, 1, begin, thudMs, thud);
            Animate(scale, ScaleTransform.ScaleXProperty, 0.6, 1, begin, thudMs, thud);
            Animate(scale, ScaleTransform.ScaleYProperty, 0.6, 1, begin, thudMs, thud);
            Animate(move, TranslateTransform.YProperty, 6, 0, begin, thudMs, thud);

            if (_badge.Visibility == Visibility.Visible)
            {
                var bg = (TransformGroup)_badge.RenderTransform;
                var bBegin = TimeSpan.FromMilliseconds(DepthRules.Ms(DashboardBillboard.BadgeDelayMs, MotionFx.Level));
                Animate(_badge, OpacityProperty, 0, 1, bBegin, thudMs, thud);
                Animate(bg.Children[0], ScaleTransform.ScaleXProperty, 0.6, 1, bBegin, thudMs, thud);
                Animate(bg.Children[0], ScaleTransform.ScaleYProperty, 0.6, 1, bBegin, thudMs, thud);
            }

            // The button lands with a flare and a shine across its face, then breathes.
            RefreshCtaFx(landed: true, begin + TimeSpan.FromMilliseconds(thudMs * 0.55));
        }

        private void Rise(UIElement e, int delayMs, int ms, IEasingFunction ease)
        {
            if (e.RenderTransform is not TranslateTransform move)
            {
                move = new TranslateTransform();
                e.RenderTransform = move;
            }
            var begin = TimeSpan.FromMilliseconds(delayMs);
            Animate(e, OpacityProperty, 0, 1, begin, ms, ease);
            Animate(move, TranslateTransform.YProperty, DashboardBillboard.TextRisePx, 0, begin, ms, ease);
        }

        /// <summary>Everything at rest, no animation (Motion Off, the first card, the same card again).</summary>
        private void SettleWords()
        {
            ResetFold();
            foreach (var e in new UIElement[] { _eyebrow, _title, _line, _cta, _badge })
            {
                e.BeginAnimation(OpacityProperty, null);
                e.Opacity = 1;
                if (e.RenderTransform is TranslateTransform t) { t.BeginAnimation(TranslateTransform.YProperty, null); t.Y = 0; }
            }
            ClearGroup((TransformGroup)((FrameworkElement)_cta.Content).RenderTransform);
            ClearGroup((TransformGroup)_badge.RenderTransform);
            RefreshCtaFx(landed: false, TimeSpan.Zero);
        }

        private static void ClearGroup(TransformGroup g)
        {
            var s = (ScaleTransform)g.Children[0];
            var m = (TranslateTransform)g.Children[1];
            s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            m.BeginAnimation(TranslateTransform.YProperty, null);
            s.ScaleX = s.ScaleY = 1;
            m.Y = 0;
        }

        /// <summary>Snooze: the words and the badge fold away (fade, drop 10 px, shrink a touch).</summary>
        private void Fold()
        {
            if (!MotionFx.AllowTransitions) return;
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            ease.Freeze();
            var dur = TimeSpan.FromMilliseconds(250);
            foreach (FrameworkElement e in new FrameworkElement[] { _meta, _badge })
            {
                e.RenderTransformOrigin = new Point(0.5, 1);
                var move = e == _meta ? (TranslateTransform)_meta.RenderTransform : (TranslateTransform)((TransformGroup)_badge.RenderTransform).Children[1];
                e.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, dur) { EasingFunction = ease });
            }
        }

        private void ResetFold()
        {
            _meta.BeginAnimation(OpacityProperty, null);
            _meta.Opacity = 1;
            var m = (TranslateTransform)_meta.RenderTransform;
            m.BeginAnimation(TranslateTransform.YProperty, null);
            m.Y = 0;
            _badge.BeginAnimation(OpacityProperty, null);
            _badge.Opacity = 1;
        }

        private static void Animate(DependencyObject target, DependencyProperty prop, double from, double to, TimeSpan begin, int ms, IEasingFunction ease)
        {
            if (target is not IAnimatable a) return;
            if (ms <= 0)
            {
                a.BeginAnimation(prop, null);
                target.SetValue(prop, to);
                return;
            }
            target.SetValue(prop, from);
            a.BeginAnimation(prop, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                BeginTime = begin,
                EasingFunction = ease,
                FillBehavior = FillBehavior.HoldEnd,
            });
        }

        // ---- the press ---------------------------------------------------------------------------

        /// <summary>Hover lifts the button (depth law); press sinks it 2 px in 90 ms.</summary>
        private void Lift(bool on)
        {
            CtaHover(on);
            if (_ctaPress.Y > 0) return;
            double to = DepthRules.TravelFor(true, false, false, on);
            int ms = DepthRules.Ms(DepthRules.HoverMs, MotionFx.Level);
            MoveCta(to, ms, null);
            _ctaDrop.Margin = new Thickness(0, DepthRules.ShadowFor(true, false, false, on), 0, -DepthRules.ShadowFor(true, false, false, on));
        }

        private void PressDown()
        {
            MoveCta(DepthRules.PressTravelPx, DepthRules.Ms(DepthRules.PressMs, MotionFx.Level), null);
            CtaSquash(true);
            _ctaDrop.Visibility = Visibility.Hidden;
            _ctaFace.SetResourceReference(Border.BorderBrushProperty, "DepthPressedBevel");
        }

        /// <summary>Release: back up in 140 ms with a 1 px overshoot.</summary>
        private void PressUp()
        {
            _ctaDrop.Visibility = Visibility.Visible;
            _ctaFace.SetResourceReference(Border.BorderBrushProperty, "DepthRaisedBevel");
            CtaSquash(false);
            int ms = DepthRules.Ms(DepthRules.ReleaseMs, MotionFx.Level);
            if (ms <= 0) { MoveCta(0, 0, null); return; }
            var spring = new DoubleAnimationUsingKeyFrames();
            spring.KeyFrames.Add(new EasingDoubleKeyFrame(-DepthRules.ReleaseOvershootPx, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * 0.6)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            spring.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
            _ctaPress.BeginAnimation(TranslateTransform.YProperty, spring);
        }

        private void MoveCta(double to, int ms, IEasingFunction? ease)
        {
            if (ms <= 0)
            {
                _ctaPress.BeginAnimation(TranslateTransform.YProperty, null);
                _ctaPress.Y = to;
                return;
            }
            _ctaPress.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
        }

        // ---- the toast ---------------------------------------------------------------------------

        private void Toast(string text)
        {
            _toastText.Text = text;
            var move = (TranslateTransform)_toast.RenderTransform;
            if (MotionFx.AllowTransitions)
            {
                _toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(250)));
            }
            else
            {
                _toast.BeginAnimation(OpacityProperty, null);
                _toast.Opacity = 1;
            }
            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimerLite(Dispatcher, TimeSpan.FromMilliseconds(1800), () => FadeTo(_toast, 0, 250)).Timer;
        }

        // ---- particles ---------------------------------------------------------------------------

        /// <summary>Sparks in the card's hue flying out of a point, falling under a little gravity.</summary>
        private void Burst(Point at, Color hue, int count, double speed)
        {
            if (!MotionFx.AllowParticles) return;
            var c = new SKColor(hue.R, hue.G, hue.B);
            for (int i = 0; i < count && _particles.Count < MaxParticles; i++)
            {
                double a = _rng.NextDouble() * Math.PI * 2, v = (70 + _rng.NextDouble() * 230) * speed;
                _particles.Add(new Particle
                {
                    X = at.X, Y = at.Y,
                    Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v - 70,
                    G = 420, Life = 0, Max = 0.45 + _rng.NextDouble() * 0.45,
                    R = 1.6 + _rng.NextDouble() * 2.4, Hue = c,
                });
            }
            StartFx();
        }

        /// <summary>Arrival motes: a few soft dots drifting up out of the words.</summary>
        private void Motes(DeckCard card)
        {
            if (!MotionFx.AllowParticles || _meta.ActualWidth <= 0) return;
            var hue = HueOf(card);
            var c = new SKColor(hue.R, hue.G, hue.B);
            var box = _meta.TransformToAncestor(_stage).TransformBounds(new Rect(0, 0, _meta.ActualWidth, _meta.ActualHeight));
            for (int i = 0; i < 14 && _particles.Count < MaxParticles; i++)
            {
                _particles.Add(new Particle
                {
                    X = box.Left + _rng.NextDouble() * box.Width,
                    Y = box.Bottom - _rng.NextDouble() * box.Height * 0.5,
                    Vx = (_rng.NextDouble() - 0.5) * 26, Vy = -(28 + _rng.NextDouble() * 60),
                    G = 0, Life = -_rng.NextDouble() * 0.35, Max = 0.9 + _rng.NextDouble() * 0.7,
                    R = 1 + _rng.NextDouble() * 1.7, Hue = c,
                });
            }
            StartFx();
        }

        private void StartFx()
        {
            if (_particles.Count == 0) return;
            _fxClock ??= new FrameClock { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            _fxClock.Tick -= OnFxTick;
            _fxClock.Tick += OnFxTick;
            if (!_fxClock.IsEnabled) _fxLast = DateTime.UtcNow;
            _fxClock.Start();
        }

        private void OnFxTick(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            double dt = Math.Min(0.05, (now - _fxLast).TotalSeconds);
            _fxLast = now;
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Life += dt;
                if (p.Life >= p.Max) { _particles.RemoveAt(i); continue; }
                if (p.Life >= 0)
                {
                    p.Vy += p.G * dt;
                    p.Vx *= 0.985;
                    p.X += p.Vx * dt;
                    p.Y += p.Vy * dt;
                }
                _particles[i] = p;
            }
            _fxSurface.Redraw();
            if (_particles.Count == 0) _fxClock?.Stop();
        }

        private void PaintParticles(object? sender, SKPaintSurfaceEventArgs e)
        {
            if (_particles.Count == 0) return;
            double aw = Math.Max(1, _fxSurface.ActualWidth);
            float s = (float)(e.Info.Width / aw);
            using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
            var canvas = e.Surface.Canvas;
            foreach (var p in _particles)
            {
                if (p.Life < 0) continue;
                double k = 1 - p.Life / p.Max;
                paint.Color = p.Hue.WithAlpha((byte)Math.Round(255 * Math.Clamp(k, 0, 1)));
                canvas.DrawCircle((float)p.X * s, (float)p.Y * s, (float)(p.R * (0.6 + 0.4 * k)) * s, paint);
            }
        }

        /// <summary>A one-shot timer that stops itself.</summary>
        private sealed class DispatcherTimerLite
        {
            public readonly System.Windows.Threading.DispatcherTimer Timer;

            public DispatcherTimerLite(System.Windows.Threading.Dispatcher d, TimeSpan after, Action run)
            {
                Timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal, d) { Interval = after };
                Timer.Tick += (_, _) =>
                {
                    Timer.Stop();
                    try { run(); } catch { }
                };
                Timer.Start();
            }
        }
    }
}
