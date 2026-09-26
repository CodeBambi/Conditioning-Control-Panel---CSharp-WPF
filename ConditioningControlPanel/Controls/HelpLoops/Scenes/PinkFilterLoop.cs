using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Controls.HelpLoops.Scenes;

/// <summary>
/// Pink Filter: a pink wash rises over the desktop, the cursor nudges the opacity from a
/// whisper to a flood and back, then ticks a checkbox through the filter.
/// </summary>
internal sealed class PinkFilterLoop : HelpLoopScene
{
    private static readonly (double T, double V)[] Opacity =
        { (400, 0), (1400, .12), (2700, .12), (3600, .38), (4600, .38), (5200, .2) };

    // Checkbox inside the default window (60,28,320,190): left 16, bottom 16, 14 square.
    private const double ChkX = 60 + 16 + 7, ChkY = 28 + 190 - 16 - 7;
    private static readonly Rect ChkRect = new(ChkX - 7, ChkY - 7, 14, 14);

    private static readonly Brush PinkWash = Frozen(Color.FromRgb(0xFF, 0x4F, 0xA8));
    private static readonly Brush ChkEdge = Frozen(Color.FromRgb(0x8F, 0x7F, 0xC4));
    private static readonly Brush LabelBrush = Frozen(Color.FromRgb(0xD9, 0xCC, 0xFF));
    private static readonly Typeface Label = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
    {
        new HelpLoopStep("help_loop_pinkfilter_1", 400, 2400),
        new HelpLoopStep("help_loop_pinkfilter_2", 2400, 5200),
        new HelpLoopStep("help_loop_pinkfilter_3", 5200, 7000),
    };

    public override string Id => "PinkFilter";
    public override double DurationMs => 7200;
    public override double StillMs => 4400;
    public override IReadOnlyList<HelpLoopStep> Steps => StepList;

    public override void Draw(LoopFrame f, double t)
    {
        var p = f.P;
        f.Desktop();

        // Checkbox + label live on the desktop, under the wash.
        bool ticked = t > 6080;
        var back = f.Back;
        var edge = new Pen(ticked ? p.Mint : ChkEdge, 2);
        back.DrawRoundedRectangle(ticked ? p.Mint : null, edge, new Rect(ChkRect.X + 1, ChkRect.Y + 1, 12, 12), 3, 3);
        var label = new FormattedText("Remember me", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Label, 10, LabelBrush, 1.0);
        back.DrawText(label, new Point(60 + 38, ChkY - label.Height / 2));

        double o = Schedule(Opacity, t);
        if (o > 0)
        {
            f.Front.PushOpacity(o);
            f.Front.DrawRectangle(PinkWash, null, new Rect(0, 0, 480, 270));
            f.Front.Pop();
        }

        f.Slider(14, 12, "PINK OPACITY", o);
        var a = f.SliderKnob(14, 12, .12);
        var b = f.SliderKnob(14, 12, .38);
        var c = f.SliderKnob(14, 12, .2);

        var cur = PathAt(new (double, double, double)[]
        {
            (1800, 300, 230), (2600, a.X, a.Y), (2700, a.X, a.Y), (3600, b.X, b.Y), (4600, b.X, b.Y),
            (5200, c.X, c.Y), (5400, c.X, c.Y), (6000, ChkX, ChkY), (6900, ChkX, ChkY),
        }, t);
        f.Ripple(ChkX, ChkY, LoopMath.Seg(t, 6080, 6500));
        f.Cursor(cur.X, cur.Y, (t > 2650 && t < 3650) || (t > 4550 && t < 5250) || (t > 6050 && t < 6200));
    }

    private static double Schedule((double T, double V)[] pts, double t)
    {
        if (t <= pts[0].T) return pts[0].V;
        for (int i = 1; i < pts.Length; i++)
            if (t <= pts[i].T)
                return LoopMath.Lerp(pts[i - 1].V, pts[i].V, LoopMath.EaseInOut(LoopMath.Seg(t, pts[i - 1].T, pts[i].T)));
        return pts[^1].V;
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
