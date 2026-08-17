using System;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// THE ZERO SHOW's canvas — the crack, the drain, the bloom and the Year One spiral, drawn in
    /// plain WPF (CONTRACT-FUSE-0816 §2.3/§2.4).
    ///
    /// <para><b>Why a raw <see cref="FrameworkElement"/> and not a Canvas of Shapes.</b> The freeze
    /// beat is ninety-odd sparks and the ignition is two hundred arm segments; as Shapes that is
    /// three hundred FrameworkElements going through measure, arrange and hit-testing every frame
    /// for a surface nobody can click. One <see cref="OnRender"/> with a
    /// <see cref="DrawingContext"/> draws the same picture with no visual tree at all.</para>
    ///
    /// <para><b>NO WEBVIEW2, and no layered window either</b> (contract §0). The show runs inside an
    /// opaque window, which is what keeps it off the fragile path: a layered fullscreen surface
    /// created while other layered surfaces animate can wedge the shared WPF render thread, and
    /// that is not a risk worth taking on the one night this window exists for.</para>
    ///
    /// <para><b>Nothing allocates in the render loop.</b> Every brush and pen comes from a frozen
    /// static ramp indexed by alpha, the spark field and the hairlines are laid out once in the
    /// constructor in UNIT space (so a resize costs nothing and a multi-monitor DPI change is just a
    /// different multiplier), and every per-frame value is a struct. At fifty frames a second for
    /// eight seconds that is the difference between a show and a stutter.</para>
    ///
    /// <para><b>The mod's accent, resolved once.</b> The bloom is the ceremony's glow, so it takes
    /// the mod's pink rather than the fuse's gold — but a brush frozen in a constructor cannot
    /// follow a later mod switch, which is the 2026-08-13 sweep's whole bug class. That is safe here
    /// and only here: this element lives for the six seconds of one show, and the mod cannot change
    /// underneath a fullscreen topmost window. The colour is read through
    /// <see cref="FrameworkElement.TryFindResource"/> at construction and never cached statically.</para>
    /// </summary>
    public sealed class DescentFuseStageVisual : FrameworkElement
    {
        // ================================ the ramps ================================
        //
        // Alpha is the only thing that varies frame to frame, so the brushes are pre-built at 33
        // steps and picked by index. 33 is under a pixel of banding at these opacities and costs
        // sixty-six frozen objects for the life of the process.

        private const int RampSteps = 32;

        /// <summary>The fuse's gold. Not an accent resource and never mod-tinted — same colour the
        /// header spark burns, so the crack is visibly the spark's own light coming apart.</summary>
        private static readonly Color Gold = Color.FromRgb(0xE0, 0xB0, 0x52);

        private static readonly Color GoldHot = Color.FromRgb(0xFF, 0xE9, 0xB8);

        private static readonly SolidColorBrush[] GoldRamp = BuildRamp(Gold);
        private static readonly SolidColorBrush[] GoldHotRamp = BuildRamp(GoldHot);

        /// <summary>Hairline pens, one per alpha step. Round caps so a half-drawn line has an end.</summary>
        private static readonly Pen[] HairPens = BuildPens(GoldRamp, 1.7);

        /// <summary>The sealed glyph's rings.</summary>
        private static readonly Pen[] RingPens = BuildPens(GoldRamp, 2.2);

        private static SolidColorBrush[] BuildRamp(Color color)
        {
            var ramp = new SolidColorBrush[RampSteps + 1];
            for (int i = 0; i <= RampSteps; i++)
            {
                var a = (byte)Math.Clamp(Math.Round(255.0 * i / RampSteps), 0, 255);
                var b = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
                b.Freeze();
                ramp[i] = b;
            }
            return ramp;
        }

        private static Pen[] BuildPens(SolidColorBrush[] ramp, double thickness)
        {
            var pens = new Pen[ramp.Length];
            for (int i = 0; i < ramp.Length; i++)
            {
                var p = new Pen(ramp[i], thickness)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                };
                p.Freeze();
                pens[i] = p;
            }
            return pens;
        }

        /// <summary>Index into a ramp for an opacity in 0..1. Anything at or below zero lands on the
        /// fully transparent entry, which draws nothing rather than being skipped by a branch.</summary>
        private static int Step(double alpha)
        {
            if (double.IsNaN(alpha) || alpha <= 0) return 0;
            if (alpha >= 1) return RampSteps;
            return (int)(alpha * RampSteps);
        }

        // ================================ the field ================================

        /// <summary>Sparks in the hanging field. Enough to read as dust, few enough to draw free.</summary>
        private const int SparkCount = 96;

        /// <summary>Hairlines out of the sealed glyph.</summary>
        private const int HairCount = 9;

        /// <summary>Vertices per hairline. Ten reads as a fracture; two reads as a clock hand.</summary>
        private const int HairVertices = 10;

        /// <summary>Samples along the ignition's arm.</summary>
        private const int ArmSamples = 200;

        /// <summary>Turns of the Year One spiral. Wider than <see cref="SpiralGlyph"/>'s 2.5 because
        /// this one is drawn at eight hundred pixels, not at twenty-two.</summary>
        private const double ArmTurns = 3.0;

        /// <summary>Outer radius of the arm as a fraction of the short half-side.</summary>
        private const double ArmRadius = 0.80;

        // Unit-space layout, built once. Angles in radians, radii as fractions.
        private readonly double[] _sparkAngle = new double[SparkCount];
        private readonly double[] _sparkRadius = new double[SparkCount];
        private readonly double[] _sparkSize = new double[SparkCount];
        private readonly double[] _sparkAlpha = new double[SparkCount];

        /// <summary>Hairline vertices as (angle, radiusFraction) pairs, flattened per line.</summary>
        private readonly double[,] _hairAngle = new double[HairCount, HairVertices];
        private readonly double[,] _hairRadius = new double[HairCount, HairVertices];

        /// <summary>The arm, in unit space (offsets from centre, fraction of the short half-side).</summary>
        private readonly Point[] _arm = new Point[ArmSamples + 1];

        private readonly Brush _bloomBrush;

        /// <summary>The arm's pens, in the mod's accent. Built with the ramps, not per frame.</summary>
        private readonly Pen[] _armPens;

        // ---- the live frame ----

        private DescentShowKind _kind = DescentShowKind.Live;
        private bool _reduced;
        private DescentFuseFrame _frame = new(DescentFuseStage.Freeze, 0, -1);
        private double _ignitionElapsed;

        /// <summary>
        /// Lay the show out. <paramref name="accent"/> is the mod's pink, read by the window from
        /// the live resource dictionary — see the note in the class summary about why freezing it
        /// here is safe exactly once.
        /// </summary>
        public DescentFuseStageVisual(Color accent)
        {
            IsHitTestVisible = false;

            // A FIXED SEED. The field is different from the glyph but identical from run to run,
            // which is what lets a bug report about "the crack looked wrong" be reproduced at all.
            var rng = new Random(0x0DE5CE7);

            for (int i = 0; i < SparkCount; i++)
            {
                _sparkAngle[i] = rng.NextDouble() * Math.PI * 2;
                // sqrt keeps the field even across the disc instead of clumping at the centre.
                _sparkRadius[i] = Math.Sqrt(rng.NextDouble());
                _sparkSize[i] = 0.9 + rng.NextDouble() * 1.9;
                _sparkAlpha[i] = 0.22 + rng.NextDouble() * 0.72;
            }

            for (int h = 0; h < HairCount; h++)
            {
                // Evenly spaced spokes with a nudge, so the break is not a snowflake.
                var baseAngle = (h / (double)HairCount) * Math.PI * 2 + (rng.NextDouble() - 0.5) * 0.30;
                var reach = 0.45 + rng.NextDouble() * 0.55;
                for (int v = 0; v < HairVertices; v++)
                {
                    var t = v / (double)(HairVertices - 1);
                    _hairAngle[h, v] = baseAngle + (rng.NextDouble() - 0.5) * 0.16 * t;
                    _hairRadius[h, v] = t * reach;
                }
            }

            for (int i = 0; i <= ArmSamples; i++)
            {
                var t = i / (double)ArmSamples;
                var angle = t * ArmTurns * Math.PI * 2;
                var r = ArmRadius * t;
                _arm[i] = new Point(r * Math.Cos(angle), r * Math.Sin(angle));
            }

            var bloom = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
            };
            bloom.GradientStops.Add(new GradientStop(Color.FromArgb(0xC8, accent.R, accent.G, accent.B), 0.0));
            bloom.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, accent.R, accent.G, accent.B), 0.34));
            bloom.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1.0));
            bloom.Freeze();
            _bloomBrush = bloom;

            _armPens = BuildPens(BuildRamp(accent), 3.4);
        }

        // ================================ the driver ================================

        /// <summary>Point the canvas at a show. Called once, before the clock starts.</summary>
        public void Begin(DescentShowKind kind, bool reducedMotion)
        {
            _kind = kind;
            _reduced = reducedMotion;
        }

        /// <summary>Hand the canvas the crack's current frame and repaint.</summary>
        public void SetFrame(DescentFuseFrame frame)
        {
            _frame = frame;
            InvalidateVisual();
        }

        /// <summary>Hand the canvas the ignition's clock and repaint.</summary>
        public void SetIgnition(double elapsedSeconds)
        {
            _ignitionElapsed = elapsedSeconds;
            InvalidateVisual();
        }

        // ================================ the paint ================================

        protected override void OnRender(DrawingContext dc)
        {
            try
            {
                double w = ActualWidth, h = ActualHeight;
                if (w <= 1 || h <= 1) return;

                var centre = new Point(w / 2, h / 2);
                // The short half-side. Everything is authored against it, so the show is the same
                // shape on a 16:9 laptop and on an ultrawide — the field simply reaches further out.
                double unit = Math.Min(w, h) / 2.0;

                if (_kind == DescentShowKind.Ignition) { RenderIgnition(dc, centre, unit); return; }

                RenderCrack(dc, centre, unit, Math.Max(w, h));
            }
            catch (Exception ex)
            {
                // A show that throws must go black, not take the window down. There is no recovery
                // to attempt: the next frame draws again from the same pure inputs.
                App.Logger?.Debug("[Fuse] Show frame failed: {E}", ex.Message);
            }
        }

        // ---------------------------------------------------------------- the crack

        private void RenderCrack(DrawingContext dc, Point centre, double unit, double longSide)
        {
            var stage = _frame.Stage;
            var p = Math.Clamp(_frame.Progress, 0, 1);

            // The field's reach: past the corners, so the sparks do not stop in a visible circle.
            double spread = longSide * 0.62;

            switch (stage)
            {
                case DescentFuseStage.Freeze:
                    // EVERYTHING STILL. The only thing that moves in a second and a half is the
                    // opacity coming up — no drift, no twinkle, no parallax. The stillness is what
                    // makes the crack land.
                    DrawSparks(dc, centre, spread, collapse: 1.0, opacity: Math.Min(1.0, p * 2.4));
                    DrawSealedGlyph(dc, centre, unit, opacity: Math.Max(0, (p - 0.45) / 0.55) * 0.55, scale: 1.0);
                    break;

                case DescentFuseStage.Crack:
                {
                    DrawSparks(dc, centre, spread, collapse: 1.0, opacity: 1.0);
                    DrawSealedGlyph(dc, centre, unit, opacity: 0.55 + 0.45 * p, scale: 1.0);
                    DrawHairlines(dc, centre, unit, EaseOut(p));

                    // ONE flash, late and brief. It is the sound the picture cannot make.
                    var flash = Pulse(p, 0.62, 0.30);
                    if (flash > 0.004)
                    {
                        dc.DrawRectangle(GoldRamp[Step(flash * 0.34)], null,
                            new Rect(0, 0, ActualWidth, ActualHeight));
                        dc.DrawEllipse(GoldHotRamp[Step(flash)], null, centre,
                            unit * 0.10 * flash, unit * 0.10 * flash);
                    }
                    break;
                }

                case DescentFuseStage.Drain:
                {
                    // Accelerating inward — cubic, so the last third of the collapse takes almost
                    // no time at all and the light appears to be pulled rather than to fall.
                    var k = 1.0 - (p * p * p);
                    DrawSparks(dc, centre, spread, collapse: k, opacity: 0.25 + 0.75 * k);
                    DrawSealedGlyph(dc, centre, unit, opacity: k, scale: k);
                    DrawHairlines(dc, centre, unit, 1.0, radiusScale: k, opacity: k);

                    // The core the whole field is being poured into. Brightest at the very end.
                    var core = 1.0 - k;
                    dc.DrawEllipse(GoldHotRamp[Step(core * core)], null, centre,
                        unit * (0.004 + 0.028 * core), unit * (0.004 + 0.028 * core));
                    break;
                }

                case DescentFuseStage.Black:
                    // Nothing. One full second of the ground, which is the beat that makes the bloom
                    // feel like it arrives rather than continues.
                    break;

                case DescentFuseStage.Bloom:
                    DrawBloom(dc, centre, unit, EaseOut(p), p);
                    break;

                case DescentFuseStage.Held:
                    DrawBloom(dc, centre, unit, 1.0, 1.0);
                    break;
            }
        }

        private void DrawSparks(DrawingContext dc, Point centre, double spread, double collapse, double opacity)
        {
            if (opacity <= 0.004) return;

            for (int i = 0; i < SparkCount; i++)
            {
                var r = _sparkRadius[i] * spread * collapse;
                var x = centre.X + Math.Cos(_sparkAngle[i]) * r;
                var y = centre.Y + Math.Sin(_sparkAngle[i]) * r;
                var size = _sparkSize[i] * (0.35 + 0.65 * collapse);
                var a = _sparkAlpha[i] * opacity;
                if (a <= 0.004) continue;
                dc.DrawEllipse(GoldRamp[Step(a)], null, new Point(x, y), size, size);
            }
        }

        /// <summary>
        /// The sealed glyph: two rings and a held dot. Deliberately not a spiral — the spiral is
        /// what the ignition draws, and showing it here would spend the reveal twenty minutes early
        /// on a surface the contract keeps sealed until the ceremony is taken (§2.5).
        /// </summary>
        private void DrawSealedGlyph(DrawingContext dc, Point centre, double unit, double opacity, double scale)
        {
            if (opacity <= 0.004 || scale <= 0.004) return;

            var pen = RingPens[Step(opacity)];
            var outer = unit * 0.175 * scale;
            dc.DrawEllipse(null, pen, centre, outer, outer);
            dc.DrawEllipse(null, pen, centre, outer * 0.56, outer * 0.56);
            dc.DrawEllipse(GoldRamp[Step(opacity)], null, centre, unit * 0.011 * scale, unit * 0.011 * scale);
        }

        private void DrawHairlines(DrawingContext dc, Point centre, double unit, double drawn,
                                   double radiusScale = 1.0, double opacity = 1.0)
        {
            if (drawn <= 0 || opacity <= 0.004) return;

            // The hairlines run past the glyph's rim out toward the edge of the short side.
            var reach = unit * 0.95 * radiusScale;

            for (int hIndex = 0; hIndex < HairCount; hIndex++)
            {
                for (int v = 1; v < HairVertices; v++)
                {
                    var t0 = (v - 1) / (double)(HairVertices - 1);
                    if (t0 > drawn) break;

                    var t1 = v / (double)(HairVertices - 1);
                    var clamped = Math.Min(t1, drawn);

                    var a0 = _hairAngle[hIndex, v - 1];
                    var r0 = _hairRadius[hIndex, v - 1] * reach;
                    var a1 = _hairAngle[hIndex, v];
                    // Interpolate the final partial segment so the crack grows smoothly instead of
                    // snapping one vertex at a time.
                    var frac = t1 > t0 ? (clamped - t0) / (t1 - t0) : 1.0;
                    var r1 = (_hairRadius[hIndex, v - 1]
                              + (_hairRadius[hIndex, v] - _hairRadius[hIndex, v - 1]) * frac) * reach;

                    var p0 = new Point(centre.X + Math.Cos(a0) * r0, centre.Y + Math.Sin(a0) * r0);
                    var p1 = new Point(centre.X + Math.Cos(a1) * r1, centre.Y + Math.Sin(a1) * r1);

                    // Fainter the further out it has run, so the break reads as spreading from the
                    // glyph rather than as a set of drawn spokes.
                    var a = opacity * (1.0 - 0.55 * t1);
                    dc.DrawLine(HairPens[Step(a)], p0, p1);
                }
            }
        }

        private void DrawBloom(DrawingContext dc, Point centre, double unit, double grow, double fadeIn)
        {
            // Out of the SAME centre point the drain collapsed into. That continuity is the whole
            // trick: the light does not return, it comes back out.
            var radius = unit * 2.35 * Math.Max(0.001, grow);
            var opacity = Math.Min(1.0, fadeIn * 2.2);
            if (opacity <= 0.004) return;

            dc.PushOpacity(opacity);
            dc.DrawEllipse(_bloomBrush, null, centre, radius, radius);
            dc.Pop();

            // A hot core that settles as the glow spreads.
            var core = Math.Max(0, 1.0 - grow);
            if (core > 0.01)
                dc.DrawEllipse(GoldHotRamp[Step(core * 0.7)], null, centre,
                    unit * 0.03 * core, unit * 0.03 * core);
        }

        // ------------------------------------------------------------- the ignition

        private void RenderIgnition(DrawingContext dc, Point centre, double unit)
        {
            var drawn = DescentIgnitionTimeline.DrawFraction(_ignitionElapsed, _reduced);
            if (drawn <= 0) return;

            int last = (int)Math.Round(drawn * ArmSamples);
            if (last < 1) last = 1;

            // A soft pool under the arm so the spiral sits in light rather than on black.
            dc.PushOpacity(Math.Min(1.0, drawn * 1.4) * 0.55);
            dc.DrawEllipse(_bloomBrush, null, centre, unit * 1.6, unit * 1.6);
            dc.Pop();

            for (int i = 1; i <= last && i <= ArmSamples; i++)
            {
                var p0 = new Point(centre.X + _arm[i - 1].X * unit, centre.Y + _arm[i - 1].Y * unit);
                var p1 = new Point(centre.X + _arm[i].X * unit, centre.Y + _arm[i].Y * unit);

                // Brightest just behind the head, settling to a steady arm further back.
                var behind = (last - i) / (double)ArmSamples;
                var a = 0.62 + 0.38 * Math.Max(0, 1.0 - behind * 8.0);
                dc.DrawLine(_armPens[Step(a)], p0, p1);
            }

            // The head: a travelling light, not a line end. Absent once the arm is finished.
            if (drawn < 1.0)
            {
                var head = new Point(centre.X + _arm[last].X * unit, centre.Y + _arm[last].Y * unit);
                dc.DrawEllipse(GoldHotRamp[Step(0.55)], null, head, unit * 0.012, unit * 0.012);
            }

            // The stations. Gold, because the notches are the fuse's colour arriving on the
            // ceremony's spiral — the two halves of the night meeting on one glyph.
            foreach (var at in DescentIgnitionTimeline.NotchAt)
            {
                var g = DescentIgnitionTimeline.NotchGlow(_ignitionElapsed, at, _reduced);
                if (g <= 0.004) continue;

                int idx = (int)Math.Round(at * ArmSamples);
                if (idx > ArmSamples) idx = ArmSamples;
                var pt = new Point(centre.X + _arm[idx].X * unit, centre.Y + _arm[idx].Y * unit);

                dc.DrawEllipse(GoldRamp[Step(0.30 * g)], null, pt, unit * (0.006 + 0.020 * g), unit * (0.006 + 0.020 * g));
                dc.DrawEllipse(GoldHotRamp[Step(g)], null, pt, unit * 0.0085, unit * 0.0085);
            }
        }

        // ================================ easing ================================

        /// <summary>Cubic ease-out. The crack lunges and settles; nothing here eases in.</summary>
        private static double EaseOut(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            var inv = 1 - t;
            return 1 - inv * inv * inv;
        }

        /// <summary>
        /// A one-shot bump: zero, up to one at <paramref name="peak"/>, back to zero, over a window
        /// of <paramref name="width"/>. Used for the single flash, which must not linger.
        /// </summary>
        private static double Pulse(double t, double peak, double width)
        {
            var d = Math.Abs(t - peak) / (width * 0.5);
            if (d >= 1) return 0;
            var s = 1 - d;
            return s * s;
        }
    }
}
