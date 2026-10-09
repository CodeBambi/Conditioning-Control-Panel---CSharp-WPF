using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ConditioningControlPanel.Services.Descent;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// THE ZERO SHOW's canvas: the crack, the drain, the bloom and the Year One spiral. Port of
    /// ConditioningControlPanel/Controls/DescentFuseStageVisual.cs - same fixed seed, same unit-space
    /// layout, same per-stage drawing, one <see cref="Render"/> pass and no visual tree. Brushes and
    /// pens are immutable ramps indexed by alpha, so the 50 fps loop allocates nothing but structs.
    /// </summary>
    public sealed class DescentFuseStageVisual : Control
    {
        private const int RampSteps = 32;
        private static readonly Color Gold = Color.FromRgb(0xE0, 0xB0, 0x52);
        private static readonly Color GoldHot = Color.FromRgb(0xFF, 0xE9, 0xB8);
        private static readonly IBrush[] GoldRamp = BuildRamp(Gold);
        private static readonly IBrush[] GoldHotRamp = BuildRamp(GoldHot);
        private static readonly IPen[] HairPens = BuildPens(GoldRamp, 1.7);
        private static readonly IPen[] RingPens = BuildPens(GoldRamp, 2.2);

        private static IBrush[] BuildRamp(Color color)
        {
            var ramp = new IBrush[RampSteps + 1];
            for (int i = 0; i <= RampSteps; i++)
            {
                var a = (byte)Math.Clamp(Math.Round(255.0 * i / RampSteps), 0, 255);
                ramp[i] = new ImmutableSolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
            }
            return ramp;
        }

        private static IPen[] BuildPens(IBrush[] ramp, double thickness)
        {
            var pens = new IPen[ramp.Length];
            for (int i = 0; i < ramp.Length; i++)
                pens[i] = new ImmutablePen((IImmutableBrush)ramp[i], thickness, lineCap: PenLineCap.Round);
            return pens;
        }

        private static int Step(double alpha)
        {
            if (double.IsNaN(alpha) || alpha <= 0) return 0;
            if (alpha >= 1) return RampSteps;
            return (int)(alpha * RampSteps);
        }

        private const int SparkCount = 96;
        private const int HairCount = 9;
        private const int HairVertices = 10;
        private const int ArmSamples = 200;
        private const double ArmTurns = 3.0;
        private const double ArmRadius = 0.80;

        private readonly double[] _sparkAngle = new double[SparkCount];
        private readonly double[] _sparkRadius = new double[SparkCount];
        private readonly double[] _sparkSize = new double[SparkCount];
        private readonly double[] _sparkAlpha = new double[SparkCount];
        private readonly double[,] _hairAngle = new double[HairCount, HairVertices];
        private readonly double[,] _hairRadius = new double[HairCount, HairVertices];
        private readonly Point[] _arm = new Point[ArmSamples + 1];
        private readonly IBrush _bloomBrush;
        private readonly IPen[] _armPens;

        private DescentShowKind _kind = DescentShowKind.Live;
        private bool _reduced;
        private DescentFuseFrame _frame = new(DescentFuseStage.Freeze, 0, -1);
        private double _ignitionElapsed;

        /// <summary>Test seam: how many primitives the last <see cref="Render"/> drew.</summary>
        internal int LastDrawCount { get; private set; }

        internal DescentFuseFrame Frame => _frame;

        /// <summary><paramref name="accent"/> is the mod's pink, read once by the window (WPF
        /// DescentFuseWindow.ResolveAccent): the show lives seconds under a fullscreen window.</summary>
        public DescentFuseStageVisual(Color accent)
        {
            IsHitTestVisible = false;

            // A FIXED SEED (WPF value): identical from run to run, so a report is reproducible.
            var rng = new Random(0x0DE5CE7);
            for (int i = 0; i < SparkCount; i++)
            {
                _sparkAngle[i] = rng.NextDouble() * Math.PI * 2;
                _sparkRadius[i] = Math.Sqrt(rng.NextDouble());
                _sparkSize[i] = 0.9 + rng.NextDouble() * 1.9;
                _sparkAlpha[i] = 0.22 + rng.NextDouble() * 0.72;
            }
            for (int h = 0; h < HairCount; h++)
            {
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
                _arm[i] = new Point(ArmRadius * t * Math.Cos(angle), ArmRadius * t * Math.Sin(angle));
            }

            _bloomBrush = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xC8, accent.R, accent.G, accent.B), 0.0),
                    new GradientStop(Color.FromArgb(0x70, accent.R, accent.G, accent.B), 0.34),
                    new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1.0),
                },
            }.ToImmutable();
            _armPens = BuildPens(BuildRamp(accent), 3.4);
        }

        public void Begin(DescentShowKind kind, bool reducedMotion)
        {
            _kind = kind;
            _reduced = reducedMotion;
        }

        public void SetFrame(DescentFuseFrame frame)
        {
            _frame = frame;
            InvalidateVisual();
        }

        public void SetIgnition(double elapsedSeconds)
        {
            _ignitionElapsed = elapsedSeconds;
            InvalidateVisual();
        }

        public override void Render(DrawingContext dc)
        {
            LastDrawCount = 0;
            try
            {
                double w = Bounds.Width, h = Bounds.Height;
                if (w <= 1 || h <= 1) return;
                var centre = new Point(w / 2, h / 2);
                double unit = Math.Min(w, h) / 2.0;
                if (_kind == DescentShowKind.Ignition) { RenderIgnition(dc, centre, unit); return; }
                RenderCrack(dc, centre, unit, Math.Max(w, h));
            }
            catch (Exception ex)
            {
                // A show that throws goes black rather than taking the window down.
                Log.Debug("[Fuse] Show frame failed: {E}", ex.Message);
            }
        }

        private void Ellipse(DrawingContext dc, IBrush? fill, IPen? pen, Point c, double r)
        {
            dc.DrawEllipse(fill, pen, c, r, r);
            LastDrawCount++;
        }

        private void Line(DrawingContext dc, IPen pen, Point a, Point b)
        {
            dc.DrawLine(pen, a, b);
            LastDrawCount++;
        }

        private void RenderCrack(DrawingContext dc, Point centre, double unit, double longSide)
        {
            var p = Math.Clamp(_frame.Progress, 0, 1);
            double spread = longSide * 0.62;

            switch (_frame.Stage)
            {
                case DescentFuseStage.Freeze:
                    DrawSparks(dc, centre, spread, 1.0, Math.Min(1.0, p * 2.4));
                    DrawSealedGlyph(dc, centre, unit, Math.Max(0, (p - 0.45) / 0.55) * 0.55, 1.0);
                    break;

                case DescentFuseStage.Crack:
                {
                    DrawSparks(dc, centre, spread, 1.0, 1.0);
                    DrawSealedGlyph(dc, centre, unit, 0.55 + 0.45 * p, 1.0);
                    DrawHairlines(dc, centre, unit, EaseOut(p));
                    var flash = Pulse(p, 0.62, 0.30);
                    if (flash > 0.004)
                    {
                        dc.DrawRectangle(GoldRamp[Step(flash * 0.34)], null, new Rect(Bounds.Size));
                        Ellipse(dc, GoldHotRamp[Step(flash)], null, centre, unit * 0.10 * flash);
                    }
                    break;
                }

                case DescentFuseStage.Drain:
                {
                    var k = 1.0 - (p * p * p);
                    DrawSparks(dc, centre, spread, k, 0.25 + 0.75 * k);
                    DrawSealedGlyph(dc, centre, unit, k, k);
                    DrawHairlines(dc, centre, unit, 1.0, k, k);
                    var core = 1.0 - k;
                    Ellipse(dc, GoldHotRamp[Step(core * core)], null, centre, unit * (0.004 + 0.028 * core));
                    break;
                }

                case DescentFuseStage.Black:
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
                var a = _sparkAlpha[i] * opacity;
                if (a <= 0.004) continue;
                var pt = new Point(centre.X + Math.Cos(_sparkAngle[i]) * r, centre.Y + Math.Sin(_sparkAngle[i]) * r);
                Ellipse(dc, GoldRamp[Step(a)], null, pt, _sparkSize[i] * (0.35 + 0.65 * collapse));
            }
        }

        /// <summary>Two rings and a held dot - deliberately not a spiral (WPF: the reveal stays sealed).</summary>
        private void DrawSealedGlyph(DrawingContext dc, Point centre, double unit, double opacity, double scale)
        {
            if (opacity <= 0.004 || scale <= 0.004) return;
            var pen = RingPens[Step(opacity)];
            var outer = unit * 0.175 * scale;
            Ellipse(dc, null, pen, centre, outer);
            Ellipse(dc, null, pen, centre, outer * 0.56);
            Ellipse(dc, GoldRamp[Step(opacity)], null, centre, unit * 0.011 * scale);
        }

        private void DrawHairlines(DrawingContext dc, Point centre, double unit, double drawn,
                                   double radiusScale = 1.0, double opacity = 1.0)
        {
            if (drawn <= 0 || opacity <= 0.004) return;
            var reach = unit * 0.95 * radiusScale;
            for (int hi = 0; hi < HairCount; hi++)
            {
                for (int v = 1; v < HairVertices; v++)
                {
                    var t0 = (v - 1) / (double)(HairVertices - 1);
                    if (t0 > drawn) break;
                    var t1 = v / (double)(HairVertices - 1);
                    var clamped = Math.Min(t1, drawn);
                    var a0 = _hairAngle[hi, v - 1];
                    var r0 = _hairRadius[hi, v - 1] * reach;
                    var a1 = _hairAngle[hi, v];
                    var frac = t1 > t0 ? (clamped - t0) / (t1 - t0) : 1.0;
                    var r1 = (_hairRadius[hi, v - 1] + (_hairRadius[hi, v] - _hairRadius[hi, v - 1]) * frac) * reach;
                    var p0 = new Point(centre.X + Math.Cos(a0) * r0, centre.Y + Math.Sin(a0) * r0);
                    var p1 = new Point(centre.X + Math.Cos(a1) * r1, centre.Y + Math.Sin(a1) * r1);
                    Line(dc, HairPens[Step(opacity * (1.0 - 0.55 * t1))], p0, p1);
                }
            }
        }

        private void DrawBloom(DrawingContext dc, Point centre, double unit, double grow, double fadeIn)
        {
            var radius = unit * 2.35 * Math.Max(0.001, grow);
            var opacity = Math.Min(1.0, fadeIn * 2.2);
            if (opacity <= 0.004) return;
            using (dc.PushOpacity(opacity)) Ellipse(dc, _bloomBrush, null, centre, radius);
            var core = Math.Max(0, 1.0 - grow);
            if (core > 0.01) Ellipse(dc, GoldHotRamp[Step(core * 0.7)], null, centre, unit * 0.03 * core);
        }

        private void RenderIgnition(DrawingContext dc, Point centre, double unit)
        {
            var drawn = DescentIgnitionTimeline.DrawFraction(_ignitionElapsed, _reduced);
            if (drawn <= 0) return;
            int last = Math.Max(1, (int)Math.Round(drawn * ArmSamples));

            using (dc.PushOpacity(Math.Min(1.0, drawn * 1.4) * 0.55))
                Ellipse(dc, _bloomBrush, null, centre, unit * 1.6);

            Point At(int i) => new(centre.X + _arm[i].X * unit, centre.Y + _arm[i].Y * unit);
            for (int i = 1; i <= last && i <= ArmSamples; i++)
            {
                var behind = (last - i) / (double)ArmSamples;
                Line(dc, _armPens[Step(0.62 + 0.38 * Math.Max(0, 1.0 - behind * 8.0))], At(i - 1), At(i));
            }

            if (drawn < 1.0) Ellipse(dc, GoldHotRamp[Step(0.55)], null, At(Math.Min(last, ArmSamples)), unit * 0.012);

            foreach (var at in DescentIgnitionTimeline.NotchAt)
            {
                var g = DescentIgnitionTimeline.NotchGlow(_ignitionElapsed, at, _reduced);
                if (g <= 0.004) continue;
                var pt = At(Math.Min(ArmSamples, (int)Math.Round(at * ArmSamples)));
                Ellipse(dc, GoldRamp[Step(0.30 * g)], null, pt, unit * (0.006 + 0.020 * g));
                Ellipse(dc, GoldHotRamp[Step(g)], null, pt, unit * 0.0085);
            }
        }

        private static double EaseOut(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            var inv = 1 - t;
            return 1 - inv * inv * inv;
        }

        private static double Pulse(double t, double peak, double width)
        {
            var d = Math.Abs(t - peak) / (width * 0.5);
            if (d >= 1) return 0;
            var s = 1 - d;
            return s * s;
        }
    }
}
