using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops.Scenes;

/// <summary>
/// Intensity Ramp: a curve climbs on a small graph while flashes on the desktop come faster;
/// then the Range mode draws a falling line and the flashes thin out.
/// </summary>
internal sealed class IntensityRampLoop : HelpLoopScene
{
    private static readonly Rect Graph = new(316, 118, 148, 104);
    private const double ClimbStart = 500, ClimbEnd = 4800, FallStart = 5300, FallEnd = 7100;

    // Flash pops: the gaps shrink while the ramp climbs, then grow while it winds down.
    private static readonly double[] Pops = { 500, 1650, 2600, 3350, 3950, 4450, 4880, 5500, 6250 };
    private static readonly Point[] Spots =
    {
        new(92, 72), new(214, 118), new(128, 176), new(250, 64), new(64, 150),
        new(190, 196), new(150, 92), new(262, 164), new(100, 110),
    };
    private const double PopLife = 620;

    private static readonly Brush GridLine = LoopPalette.Solid("#3b306080");

    private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
    {
        new HelpLoopStep("help_loop_intensityramp_1", 0, 2000),
        new HelpLoopStep("help_loop_intensityramp_2", 2000, 5000),
        new HelpLoopStep("help_loop_intensityramp_3", 5000, 7400),
    };

    public override string Id => "IntensityRamp";
    public override double DurationMs => 7600;
    public override double StillMs => 4800;
    public override IReadOnlyList<HelpLoopStep> Steps => StepList;

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
        var plot = new Rect(Graph.X + 16, Graph.Y + 14, Graph.Width - 30, Graph.Height - 30);
        var axis = new Pen(p.Dim, 1);
        dc.DrawLine(axis, new Point(plot.X, plot.Y), new Point(plot.X, plot.Bottom));
        dc.DrawLine(axis, new Point(plot.X, plot.Bottom), new Point(plot.Right, plot.Bottom));
        var grid = new Pen(GridLine, 1);
        for (int i = 1; i <= 2; i++)
            dc.DrawLine(grid, new Point(plot.X + 1, plot.Y + plot.Height * i / 3), new Point(plot.Right, plot.Y + plot.Height * i / 3));
        f.DrawText(dc, "time", plot.Right, plot.Bottom + 2, 9, p.Dim, LoopFrame.Mono, FontWeights.Medium, TextAlignment.Right);

        double climbO = Seg(t, 0, 300) * (1 - Seg(t, 5000, 5300));
        double fallO = 1 - Seg(t, 7150, 7550);
        double cu = Seg(t, ClimbStart, ClimbEnd);
        double fu = Seg(t, FallStart, FallEnd);

        if (climbO > 0)
            using (f.Fade(dc, climbO))
                Curve(f, plot, Climb, cu, p.Accent);
        if (t > FallStart && fallO > 0)
            using (f.Fade(dc, fallO))
                Curve(f, plot, Fall, fu, p.Lilac);

        // --- the readout ---
        if (t < 5150)
        {
            double mult = 1 + (Climb(cu) - Climb(0)) / (Climb(1) - Climb(0));
            string s = "RAMP " + mult.ToString("0.0", CultureInfo.InvariantCulture) + "x";
            f.Chip(Graph.X, Graph.Y - 30, s, hot: cu > 0 && cu < 1, opacity: Seg(t, 0, 300) * (1 - Seg(t, 4950, 5150)));
        }
        else
        {
            int pct = (int)Math.Round(Lerp(100, 10, fu) / 10) * 10;
            string s = "RANGE " + pct.ToString(CultureInfo.InvariantCulture) + "%";
            f.Chip(Graph.X, Graph.Y - 30, s, hot: fu > 0 && fu < 1, opacity: Seg(t, 5150, 5350) * fallO);
        }
    }

    /// <summary>The curve drawn from the left edge to u (0..1) with a dot at its head.</summary>
    private static void Curve(LoopFrame f, Rect plot, Func<double, double> y, double u, Brush brush)
    {
        const int n = 40;
        Point At(double x) => new(plot.X + plot.Width * x, plot.Bottom - plot.Height * y(x));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(At(0), false, false);
            int m = (int)Math.Ceiling(u * n);
            for (int i = 1; i <= m; i++) c.LineTo(At(Math.Min(u, i / (double)n)), true, true);
        }
        g.Freeze();
        f.Front.DrawGeometry(null, new Pen(brush, 2.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
        var head = At(u);
        f.Front.DrawEllipse(f.P.White, new Pen(brush, 2), head, 4, 4);
    }
}
