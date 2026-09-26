using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Controls.HelpLoops.Scenes;

/// <summary>
/// Bubble Count: seven bubbles pop onto a dimmed screen under a draining timer, vanish,
/// and a "How many?" card asks for the number; the cursor picks 7 for XP.
/// </summary>
internal sealed class BubbleCountLoop : HelpLoopScene
{
    private static readonly Point[] Spots =
    {
        new(90, 70), new(170, 140), new(250, 60), new(320, 150), new(400, 90), new(140, 210), new(360, 210),
    };

    private static readonly string[] Options = { "5", "6", "7", "8" };

    // Ask card (120,70) 240x120; four 40px buttons, 10 apart, centred; row 50 below the top.
    private static readonly Rect AskRect = new(120, 70, 240, 120);
    private const double RowX = 120 + (240 - (4 * 40 + 3 * 10)) / 2.0, RowY = 70 + 50;
    // The mockup's cursor target (lands on the third button).
    private const double PickX = 120 + 14 + 2 * 50 + 20 + 6, PickY = 70 + 14 + 28 + 12 + 20;

    private static readonly Brush DimBrush = Frozen(Color.FromArgb(0x99, 0x07, 0x04, 0x0F));
    private static readonly Brush TimerTrack = Frozen(Color.FromRgb(0x3B, 0x30, 0x60));
    private static readonly Brush Panel2 = Frozen(Color.FromRgb(0x24, 0x1C, 0x3D));
    private static readonly Brush LineBrush = Frozen(Color.FromRgb(0x34, 0x2A, 0x55));
    private static readonly Brush OkText = Frozen(Color.FromRgb(0x10, 0x23, 0x1D));

    private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
    {
        new HelpLoopStep("help_loop_bubblecount_1", 600, 2800),
        new HelpLoopStep("help_loop_bubblecount_2", 2800, 3300),
        new HelpLoopStep("help_loop_bubblecount_3", 3300, 6400),
    };

    public override string Id => "BubbleCount";
    public override double DurationMs => 7400;
    public override double StillMs => 2000;
    public override IReadOnlyList<HelpLoopStep> Steps => StepList;

    public override void Draw(LoopFrame f, double t)
    {
        var p = f.P;
        var dc = f.Front;
        f.Desktop();

        double d = LoopMath.Seg(t, 500, 800) * (1 - LoopMath.Seg(t, 6400, 6900));
        if (d > 0)
        {
            dc.PushOpacity(d);
            dc.DrawRectangle(DimBrush, null, new Rect(0, 0, 480, 270));
            dc.Pop();
        }

        double gone = LoopMath.Seg(t, 2800, 3050);
        for (int i = 0; i < Spots.Length; i++)
        {
            double k = LoopMath.Seg(t, 700 + i * 90, 950 + i * 90);
            double o = k * (1 - gone);
            if (o <= 0) continue;
            double s = LoopMath.Lerp(.3, 1, LoopMath.Back(k)) * (1 - .6 * gone);
            f.Bubble(Spots[i].X, Spots[i].Y, s, o);
        }

        // Timer bar, drains while the bubbles are up.
        double tmO = LoopMath.Seg(t, 900, 1100) * (1 - LoopMath.Seg(t, 2800, 3000));
        if (tmO > 0)
        {
            dc.PushOpacity(tmO);
            dc.DrawRoundedRectangle(TimerTrack, null, new Rect(60, 14, 360, 5), 2.5, 2.5);
            double w = 360 * (1 - LoopMath.Seg(t, 1100, 2800));
            if (w > 0) dc.DrawRoundedRectangle(p.Lilac, null, new Rect(60, 14, w, 5), 2.5, 2.5);
            dc.Pop();
        }

        // The question.
        double ak = LoopMath.Seg(t, 3150, 3450), aout = LoopMath.Seg(t, 6300, 6700);
        double askO = ak * (1 - aout);
        if (askO > 0)
        {
            double sc = LoopMath.Lerp(.85, 1, LoopMath.Back(ak));
            dc.PushOpacity(askO);
            dc.PushTransform(new ScaleTransform(sc, sc, AskRect.X + AskRect.Width / 2, AskRect.Y + AskRect.Height / 2));
            f.Card(AskRect, p.Lilac);
            f.Text("How many?", AskRect.X + AskRect.Width / 2, AskRect.Y + 14, 16, p.Text, align: TextAlignment.Center);
            var edge = new Pen(LineBrush, 1);
            for (int i = 0; i < Options.Length; i++)
            {
                bool ok = i == 2 && t > 4550;
                var r = new Rect(RowX + i * 50, RowY, 40, 40);
                dc.DrawRoundedRectangle(ok ? p.Mint : Panel2, ok ? new Pen(p.Mint, 1) : edge, r, 10, 10);
                f.Text(Options[i], r.X + 20, r.Y + 8, 17, ok ? OkText : p.Text, bold: true, align: TextAlignment.Center);
            }
            dc.Pop();
            dc.Pop();
        }

        f.Floater(PickX - 8, PickY - 34, LoopMath.Seg(t, 4550, 5500), "+XP");

        var cur = PathAt(new (double, double, double)[]
        {
            (3400, 420, 236), (4400, PickX, PickY), (5400, PickX, PickY), (6500, 430, 236),
        }, t);
        f.Ripple(PickX, PickY, LoopMath.Seg(t, 4520, 4950));
        f.Cursor(cur.X, cur.Y, t > 4500 && t < 4650);
    }

    private static Point PathAt((double T, double X, double Y)[] pts, double t)
    {
        if (t <= pts[0].T) return new Point(pts[0].X, pts[0].Y);
        for (int i = 1; i < pts.Length; i++)
        {
            if (t <= pts[i].T)
            {
                var a = pts[i - 1];
                var b = pts[i];
                double k = LoopMath.EaseInOut(LoopMath.Seg(t, a.T, b.T));
                return new Point(LoopMath.Lerp(a.X, b.X, k), LoopMath.Lerp(a.Y, b.Y, k));
            }
        }
        return new Point(pts[^1].X, pts[^1].Y);
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
