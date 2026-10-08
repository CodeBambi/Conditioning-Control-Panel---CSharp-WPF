using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Motion;
using ConditioningControlPanel.Services.Billboard;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>
    /// The juice (WPF 7.1.5 BillboardDeckView.Fx), ported from the mockup and Breakout's rules: every
    /// move has an in and an out, nothing flashes white, nothing animates an effect. Pushes and rises
    /// are transform and opacity tweens; sparks and motes paint into one half-resolution
    /// <see cref="FxSurface"/> on a <see cref="FrameClock"/> that runs only while a particle lives.
    /// </summary>
    public sealed partial class BillboardDeckView
    {
        private const int MaxParticles = 160;

        private readonly FxSurface _fxSurface = new() { ResolutionScale = 0.5, IsHitTestVisible = false };
        private readonly List<Particle> _particles = new();
        private readonly Random _rng = new();
        private FrameClock? _fxClock;
        private DateTime _fxLast;

        /// <summary>Live sparks and motes (tests).</summary>
        internal int ParticleCount => _particles.Count;

        private struct Particle
        {
            public double X, Y, Vx, Vy, G, Life, Max, R;
            public SKColor Hue;
        }

        private static int Ms(int fullMs) => DepthRules.Ms(fullMs, BoardMotion.Level);

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
            double w = Math.Max(1, _stage.Bounds.Width);
            int ms = Math.Max(1, Ms(DashboardBillboard.PushMs));
            Func<double, double> ease = Easings.CubicOut;

            _tw.To(old.Move, "x", () => old.Move.X, v => old.Move.X = v, 0, -w * DashboardBillboard.PushOldShift, ms, ease,
                done: () => Retire(old));

            double start = w * DashboardBillboard.PushNewStart;
            _tw.To(next.Move, "x", () => next.Move.X, v => next.Move.X = v, start, 0, ms, ease);
            _tw.To(next.Zoom, "s", () => next.Zoom.ScaleX, v => { next.Zoom.ScaleX = v; next.Zoom.ScaleY = v; },
                DashboardBillboard.PushNewScale, 1, ms, ease);

            // The glint: a narrow band in the hue, its bright edge on the seam, fading as it lands.
            var hue = _card != null ? HueOf(_card) : Color.FromRgb(0xff, 0x4f, 0xa8);
            _glint.Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, hue.R, hue.G, hue.B), 0),
                    new GradientStop(Color.FromArgb(0x73, hue.R, hue.G, hue.B), 0.75),
                    new GradientStop(Color.FromArgb(0, hue.R, hue.G, hue.B), 1),
                },
            };
            double band = _glint.Width;
            _tw.To(_glintMove, "x", () => _glintMove.X, v => _glintMove.X = v, start - band * 0.75, -band * 0.75, ms, ease);
            _tw.To(_glint, "opacity", () => _glint.Opacity, v => _glint.Opacity = v, 1, 0, ms);
        }

        // ---- the words ---------------------------------------------------------------------------

        /// <summary>Eyebrow, title and line rise 60 ms apart; the button thuds in after them; the
        /// badge pops.</summary>
        private void LandWords(DeckCard card)
        {
            ResetFold();
            Func<double, double> thud = Easings.Thud;
            int rise = Ms(DashboardBillboard.TextRiseMs);
            int step = Ms(DashboardBillboard.TextStaggerMs);
            int k = 1;
            foreach (var t in new[] { _eyebrow, _title, _line })
            {
                if (!t.IsVisible) continue;
                Rise(t, step * k++, rise, thud);
            }

            int thudMs = Ms(DashboardBillboard.ThudMs);
            double begin = Ms(DashboardBillboard.ThudDelayMs);
            Anim(_cta, "opacity", () => _cta.Opacity, v => _cta.Opacity = v, 0, 1, begin, thudMs, thud);
            Anim(_ctaLandScale, "s", () => _ctaLandScale.ScaleX, v => { _ctaLandScale.ScaleX = v; _ctaLandScale.ScaleY = v; }, 0.6, 1, begin, thudMs, thud);
            Anim(_ctaLandMove, "y", () => _ctaLandMove.Y, v => _ctaLandMove.Y = v, 6, 0, begin, thudMs, thud);

            if (_badge.IsVisible)
            {
                double bBegin = Ms(DashboardBillboard.BadgeDelayMs);
                Anim(_badge, "opacity", () => _badge.Opacity, v => _badge.Opacity = v, 0, 1, bBegin, thudMs, thud);
                Anim(_badgeScale, "s", () => _badgeScale.ScaleX, v => { _badgeScale.ScaleX = v; _badgeScale.ScaleY = v; }, 0.6, 1, bBegin, thudMs, thud);
            }

            // The button lands with a flare and a shine across its face, then breathes.
            RefreshCtaFx(landed: true, begin + thudMs * 0.55);
        }

        private void Rise(TextBlock e, int delayMs, int ms, Func<double, double> ease)
        {
            var move = (TranslateTransform)e.RenderTransform!;
            Anim(e, "opacity", () => e.Opacity, v => e.Opacity = v, 0, 1, delayMs, ms, ease);
            Anim(move, "y", () => move.Y, v => move.Y = v, DashboardBillboard.TextRisePx, 0, delayMs, ms, ease);
        }

        /// <summary>Everything at rest, no animation (Motion Off, the first card, the same card again).</summary>
        private void SettleWords()
        {
            ResetFold();
            foreach (var e in new Control[] { _eyebrow, _title, _line, _cta, _badge })
            {
                _tw.Stop(e, "opacity");
                e.Opacity = 1;
                if (e.RenderTransform is TranslateTransform t) { _tw.Stop(t, "y"); t.Y = 0; }
            }
            _tw.Stop(_ctaLandScale, "s"); _ctaLandScale.ScaleX = _ctaLandScale.ScaleY = 1;
            _tw.Stop(_ctaLandMove, "y"); _ctaLandMove.Y = 0;
            _tw.Stop(_badgeScale, "s"); _badgeScale.ScaleX = _badgeScale.ScaleY = 1;
            _tw.Stop(_badgeMove, "y"); _badgeMove.Y = 0;
            RefreshCtaFx(landed: false, 0);
        }

        /// <summary>Snooze: the words and the badge fold away (fade, drop 10 px).</summary>
        private void Fold()
        {
            if (!BoardMotion.AllowTransitions) return;
            Func<double, double> ease = Easings.QuadIn;
            _meta.RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative);
            _tw.To(_meta, "opacity", () => _meta.Opacity, v => _meta.Opacity = v, null, 0, 200, ease);
            _tw.To(_metaMove, "y", () => _metaMove.Y, v => _metaMove.Y = v, null, 10, 250, ease);
            _tw.To(_badge, "opacity", () => _badge.Opacity, v => _badge.Opacity = v, null, 0, 200, ease);
            _tw.To(_badgeMove, "y", () => _badgeMove.Y, v => _badgeMove.Y = v, null, 10, 250, ease);
        }

        private void ResetFold()
        {
            _tw.Stop(_meta, "opacity"); _meta.Opacity = 1;
            _tw.Stop(_metaMove, "y"); _metaMove.Y = 0;
            _tw.Stop(_badge, "opacity"); _badge.Opacity = 1;
            _tw.Stop(_badgeMove, "y"); _badgeMove.Y = 0;
        }

        /// <summary>A tween that starts at <paramref name="from"/> NOW (held through its delay), as
        /// WPF's BeginTime + HoldEnd did; no time at all sets the end value.</summary>
        private void Anim(object target, string name, Func<double> get, Action<double> set, double from, double to,
            double delayMs, int ms, Func<double, double> ease)
        {
            if (ms <= 0)
            {
                _tw.Stop(target, name);
                set(to);
                return;
            }
            _tw.To(target, name, get, set, from, to, ms, ease, delayMs);
        }

        // ---- the press ---------------------------------------------------------------------------

        /// <summary>Hover lifts the button (depth law); press sinks it 2 px in 90 ms.</summary>
        private void Lift(bool on)
        {
            CtaHover(on);
            if (_ctaPress.Y > 0) return;
            double to = DepthRules.TravelFor(true, false, false, on);
            MoveCta(to, Ms(DepthRules.HoverMs));
            double sh = DepthRules.ShadowFor(true, false, false, on);
            _ctaDrop.Margin = new Thickness(0, sh, 0, -sh);
        }

        private void PressDown()
        {
            MoveCta(DepthRules.PressTravelPx, Ms(DepthRules.PressMs));
            CtaSquash(true);
            _ctaDrop.Opacity = 0; // WPF Visibility.Hidden: keeps its slot, draws nothing
            Res(_ctaFace, Border.BorderBrushProperty, "DepthPressedBevel");
        }

        /// <summary>Release: back up in 140 ms with a 1 px overshoot.</summary>
        private void PressUp()
        {
            _ctaDrop.Opacity = 1;
            Res(_ctaFace, Border.BorderBrushProperty, "DepthRaisedBevel");
            CtaSquash(false);
            int ms = Ms(DepthRules.ReleaseMs);
            if (ms <= 0) { MoveCta(0, 0); return; }
            // Two keyframes: past rest by the overshoot at 60%, then home.
            _tw.To(_ctaPress, "y", () => _ctaPress.Y, v => _ctaPress.Y = v, null, -DepthRules.ReleaseOvershootPx, ms * 0.6, Easings.QuadOut,
                done: () => _tw.To(_ctaPress, "y", () => _ctaPress.Y, v => _ctaPress.Y = v, null, 0, ms * 0.4, Easings.QuadInOut));
        }

        private void MoveCta(double to, int ms)
        {
            if (ms <= 0)
            {
                _tw.Stop(_ctaPress, "y");
                _ctaPress.Y = to;
                return;
            }
            _tw.To(_ctaPress, "y", () => _ctaPress.Y, v => _ctaPress.Y = v, null, to, ms);
        }

        // ---- the toast ---------------------------------------------------------------------------

        private void Toast(string text)
        {
            _toastText.Text = text;
            if (BoardMotion.AllowTransitions)
            {
                _tw.To(_toast, "opacity", () => _toast.Opacity, v => _toast.Opacity = v, 0, 1, 250);
                _tw.To(_toastMove, "y", () => _toastMove.Y, v => _toastMove.Y = v, 8, 0, 250);
            }
            else
            {
                _tw.Stop(_toast, "opacity");
                _toast.Opacity = 1;
            }
            _toastTimer?.Dispose();
            _toastTimer = global::Avalonia.Threading.DispatcherTimer.RunOnce(() => FadeTo(_toast, 0, 250), TimeSpan.FromMilliseconds(1800));
        }

        /// <summary>The toast's words (tests).</summary>
        internal string ToastText => _toastText.Text ?? string.Empty;

        // ---- particles ---------------------------------------------------------------------------

        /// <summary>Sparks in the card's hue flying out of a point, falling under a little gravity.</summary>
        private void Burst(Point at, Color hue, int count, double speed)
        {
            if (!BoardMotion.AllowParticles) return;
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
            if (!BoardMotion.AllowParticles || _meta.Bounds.Width <= 0) return;
            var hue = HueOf(card);
            var c = new SKColor(hue.R, hue.G, hue.B);
            var origin = _meta.TranslatePoint(new Point(0, 0), _stage) ?? default;
            var box = new Rect(origin, _meta.Bounds.Size);
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
            if (_fxClock == null)
            {
                _fxClock = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
                _fxClock.Tick += OnFxTick;
            }
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

        private void PaintParticles(object? sender, FxPaintEventArgs e)
        {
            if (_particles.Count == 0) return;
            double aw = Math.Max(1, _fxSurface.Bounds.Width);
            float s = (float)(e.Info.Width / aw);
            using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
            var canvas = e.Canvas;
            foreach (var p in _particles)
            {
                if (p.Life < 0) continue;
                double k = 1 - p.Life / p.Max;
                paint.Color = p.Hue.WithAlpha((byte)Math.Round(255 * Math.Clamp(k, 0, 1)));
                canvas.DrawCircle((float)p.X * s, (float)p.Y * s, (float)(p.R * (0.6 + 0.4 * k)) * s, paint);
            }
        }
    }
}
