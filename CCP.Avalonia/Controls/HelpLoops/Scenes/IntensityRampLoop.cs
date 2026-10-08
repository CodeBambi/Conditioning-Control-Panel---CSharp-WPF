using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using static ConditioningControlPanel.Avalonia.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Avalonia.Controls.HelpLoops.Scenes
{
    /// <summary>Intensity Ramp: a curve climbs on a small graph while flashes on the desktop come faster;
    /// then the Range mode draws a falling line and the flashes thin out
    /// (WPF Controls/HelpLoops/Scenes/IntensityRampLoop.cs, same timings).</summary>
    internal sealed class IntensityRampLoop : HelpLoopScene
    {
        private static readonly Rect Graph = new(316, 118, 148, 104);
        private static readonly Rect Plot = new(Graph.X + 16, Graph.Y + 14, Graph.Width - 30, Graph.Height - 30);
        private const double ClimbStart = 500, ClimbEnd = 4800, FallStart = 5300, FallEnd = 7100;
        private const int N = 40;

        // Flash pops: the gaps shrink while the ramp climbs, then grow while it winds down.
        private static readonly double[] Pops = { 500, 1650, 2600, 3350, 3950, 4450, 4880, 5500, 6250 };
        private static readonly Point[] Spots =
        {
            new(92, 72), new(214, 118), new(128, 176), new(250, 64), new(64, 150),
            new(190, 196), new(150, 92), new(262, 164), new(100, 110),
        };
        private const double PopLife = 620;

        private static readonly Pen Axis = new(LoopPalette.Solid("#a497c4"), 1);   // palette Dim
        private static readonly Pen Grid = new(LoopPalette.Solid("#3b306080"), 1);
        private static readonly Pen LilacLine = Line(LoopPalette.Solid("#b99cff"));
        private static readonly Pen LilacRim = new(LoopPalette.Solid("#b99cff"), 2);

        // WPF draws the curve's 40 straight segments up to u each frame; the same polyline, built
        // once and clipped at the head, looks the same: the cut and the missing round end cap sit under
        // the head dot (pinned by HelpLoopScenesTests.IntensityRampCurveStopsAtItsHead).
        private static readonly StreamGeometry ClimbCurve = Polyline(Climb), FallCurve = Polyline(Fall);

        // The readouts the WPF code formats each frame: RAMP 1.0x..2.0x, RANGE 0%..100%.
        private static readonly string[] RampLabels = Enumerable.Range(0, 11)
            .Select(k => "RAMP " + (1 + k / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + "x").ToArray();
        private static readonly string[] RangeLabels = Enumerable.Range(0, 11)
            .Select(k => "RANGE " + (k * 10).ToString(CultureInfo.InvariantCulture) + "%").ToArray();

        private Pen? _accentLine, _accentRim;

        public override string Id => "IntensityRamp";
        public override double DurationMs => 7600;
        public override double StillMs => 4800;

        public override IReadOnlyList<HelpLoopStep> Steps { get; } = new[]
        {
            new HelpLoopStep("help_loop_intensityramp_1", 0, 2000),
            new HelpLoopStep("help_loop_intensityramp_2", 2000, 5000),
            new HelpLoopStep("help_loop_intensityramp_3", 5000, 7400),
        };

        // Climb: an eased rise. Fall: a straight line from full to a tenth.
        private static double Climb(double u) => .12 + .78 * EaseInOut(u);
        private static double Fall(double u) => Lerp(.9, .18, u);

        public override void Draw(LoopFrame f, double t)
        {
            var p = f.P;
            var dc = f.Front;
            f.Desktop();

            // --- flashes on the desktop ---
            for (int i = 0; i < Pops.Length; i++)
            {
                double k = Seg(t, Pops[i], Pops[i] + 240);
                double gone = Seg(t, Pops[i] + PopLife, Pops[i] + PopLife + 220);
                double o = k * (1 - gone);
                if (o <= .001) continue;
                double w = 92, h = 66;
                var r = new Rect(Spots[i].X - w / 2, Spots[i].Y - h / 2, w, h);
                using (f.At(dc, Spots[i].X, Spots[i].Y, Lerp(.6, 1, Back(k)), o))
                    f.Photo(r, (PhotoLook)(i % 3));
            }

            // --- the graph card ---
            f.Card(Graph, p.Border);
            dc.DrawLine(Axis, new Point(Plot.X, Plot.Y), new Point(Plot.X, Plot.Bottom));
            dc.DrawLine(Axis, new Point(Plot.X, Plot.Bottom), new Point(Plot.Right, Plot.Bottom));
            for (int i = 1; i <= 2; i++)
                dc.DrawLine(Grid, new Point(Plot.X + 1, Plot.Y + Plot.Height * i / 3), new Point(Plot.Right, Plot.Y + Plot.Height * i / 3));
            f.DrawText(dc, "time", Plot.Right, Plot.Bottom + 2, 9, p.Dim, LoopFrame.Mono, FontWeight.Medium, TextAlignment.Right);

            double climbO = Seg(t, 0, 300) * (1 - Seg(t, 5000, 5300));
            double fallO = 1 - Seg(t, 7150, 7550);
            double cu = Seg(t, ClimbStart, ClimbEnd);
            double fu = Seg(t, FallStart, FallEnd);

            if (_accentLine?.Brush != p.Accent)
            {
                _accentLine = Line(p.Accent);
                _accentRim = new Pen(p.Accent, 2);
            }
            if (climbO > 0)
                using (f.Fade(dc, climbO))
                    Curve(f, ClimbCurve, Climb, cu, _accentLine, _accentRim!);
            if (t > FallStart && fallO > 0)
                using (f.Fade(dc, fallO))
                    Curve(f, FallCurve, Fall, fu, LilacLine, LilacRim);

            // --- the readout ---
            if (t < 5150)
            {
                double mult = 1 + (Climb(cu) - Climb(0)) / (Climb(1) - Climb(0));
                string s = RampLabels[(int)Math.Round((mult - 1) * 10, MidpointRounding.AwayFromZero)];
                f.Chip(Graph.X, Graph.Y - 30, s, hot: cu > 0 && cu < 1, opacity: Seg(t, 0, 300) * (1 - Seg(t, 4950, 5150)));
            }
            else
            {
                int pct = (int)Math.Round(Lerp(100, 10, fu) / 10) * 10;
                f.Chip(Graph.X, Graph.Y - 30, RangeLabels[pct / 10], hot: fu > 0 && fu < 1, opacity: Seg(t, 5150, 5350) * fallO);
            }
        }

        private static Pen Line(IBrush brush) => new(brush, 2.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        private static Point At(Func<double, double> y, double x) => new(Plot.X + Plot.Width * x, Plot.Bottom - Plot.Height * y(x));

        private static StreamGeometry Polyline(Func<double, double> y)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(At(y, 0), false);
                for (int i = 1; i <= N; i++) c.LineTo(At(y, i / (double)N));
                c.EndFigure(false);
            }
            return g;
        }

        /// <summary>The curve drawn from the left edge to u (0..1) with a dot at its head.</summary>
        private static void Curve(LoopFrame f, StreamGeometry curve, Func<double, double> y, double u, Pen line, Pen rim)
        {
            var head = At(y, u);
            if (u > 0)
                using (f.Front.PushClip(new Rect(Plot.X - 10, Plot.Y - 10, head.X - Plot.X + 10, Plot.Height + 20)))
                    f.Front.DrawGeometry(null, line, curve);
            f.Front.DrawEllipse(f.P.White, rim, head, 4, 4);
        }
    }
}
