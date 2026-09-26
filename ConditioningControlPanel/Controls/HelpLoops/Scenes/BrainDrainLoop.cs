using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Controls.HelpLoops.Scenes;

/// <summary>
/// Brain Drain: the desktop blurs, melting mode warps it and drips run down from the top,
/// and a drop icon pulses each time a clip plays.
/// WPF has no displacement filter, so the melt is a slow skew of the whole desktop plus a
/// sideways sine wobble of each text line.
/// </summary>
internal sealed class BrainDrainLoop : HelpLoopScene
{
    private static readonly (double T, double V)[] Blur = { (500, 0), (2200, .6), (6600, .6), (7300, 0) };
    private static readonly double[] Clips = { 1500, 3900, 6000 };
    private static readonly double[] DripX = { 70, 140, 215, 300, 365 };

    private static readonly Brush DripBrush = BuildDripBrush();

    private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
    {
        new HelpLoopStep("help_loop_braindrain_1", 500, 2800),
        new HelpLoopStep("help_loop_braindrain_2", 2800, 5600),
        new HelpLoopStep("help_loop_braindrain_3", 5600, 7400),
    };

    public override string Id => "BrainDrain";
    public override double DurationMs => 7600;
    public override double StillMs => 4300;
    public override IReadOnlyList<HelpLoopStep> Steps => StepList;

    public override void Draw(LoopFrame f, double t)
    {
        var p = f.P;
        double b = Schedule(Blur, t);
        double m = LoopMath.Seg(t, 2800, 3600) * (1 - LoopMath.Seg(t, 6400, 7200));

        f.BackBlur = b * 7;

        // Desktop, warped while melting.
        var back = f.Back;
        bool warp = m > 0.01;
        if (warp)
        {
            var g = new TransformGroup();
            g.Children.Add(new SkewTransform(m * 3 * Math.Sin(t / 700), m * 1.5 * Math.Sin(t / 900 + 1), 240, 135));
            g.Children.Add(new ScaleTransform(1, 1 + m * .03 * (1 + Math.Sin(t / 520)), 240, 0));
            back.PushTransform(g);
        }
        var dk = f.Desktop();
        if (warp)
        {
            for (int i = 0; i < dk.Lines.Count; i++)
            {
                var l = dk.Lines[i];
                double dx = m * 7 * Math.Sin(t / 260 + i * 1.3);
                var cover = l;
                cover.Inflate(1.5, 1.5);
                back.DrawRectangle(p.Window, null, cover);
                back.DrawRoundedRectangle(i % 3 == 1 ? p.LineB : p.LineA, null,
                    new Rect(l.X + dx, l.Y + m * 2 * Math.Sin(t / 330 + i), l.Width, l.Height), l.Height / 2, l.Height / 2);
            }
            back.Pop();
        }

        // Drips hang from the top edge, over the blur.
        var dc = f.Front;
        if (m > 0)
        {
            dc.PushOpacity(m);
            for (int i = 0; i < DripX.Length; i++)
            {
                double w = 9 + i % 3 * 4;
                double h = m * (40 + i * 23 + 10 * Math.Sin(t / 600 + i));
                if (h <= 0) continue;
                dc.DrawGeometry(DripBrush, null, DripShape(DripX[i], w, h));
            }
            dc.Pop();
        }

        f.Slider(14, 12, "BLUR", b);

        double c = -1e9;
        foreach (var x in Clips) if (t >= x) c = x;
        double wk = LoopMath.Seg(t, c, c + 700);
        bool pulsing = wk > 0 && wk < 1;
        double s = 1 + .25 * (pulsing ? Math.Sin(wk * Math.PI) : 0);
        dc.PushTransform(new ScaleTransform(s, s, 420 + 13, 16 + 13));
        f.Speaker(420, 16, pulsing ? wk : 0, drop: true);
        dc.Pop();
    }

    /// <summary>A drip: straight sides, flat top at y 0, round bottom (radius 8, like the mockup).</summary>
    private static Geometry DripShape(double x, double w, double h)
    {
        double r = Math.Min(Math.Min(8, w / 2), h);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x, 0), true, true);
            ctx.LineTo(new Point(x + w, 0), true, false);
            ctx.LineTo(new Point(x + w, h - r), true, false);
            ctx.ArcTo(new Point(x + w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(x + r, h), true, false);
            ctx.ArcTo(new Point(x, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    private static Brush BuildDripBrush()
    {
        var b = new LinearGradientBrush(Color.FromArgb(0xCC, 0xFF, 0x6F, 0xB5), Color.FromArgb(0xCC, 0xB9, 0x9C, 0xFF), 90);
        b.Freeze();
        return b;
    }

    private static double Schedule((double T, double V)[] pts, double t)
    {
        if (t <= pts[0].T) return pts[0].V;
        for (int i = 1; i < pts.Length; i++)
            if (t <= pts[i].T)
                return LoopMath.Lerp(pts[i - 1].V, pts[i].V, LoopMath.EaseInOut(LoopMath.Seg(t, pts[i - 1].T, pts[i].T)));
        return pts[^1].V;
    }
}
