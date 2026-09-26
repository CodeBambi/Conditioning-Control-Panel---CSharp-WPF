using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Controls.HelpLoops.Scenes;

/// <summary>
/// Mind Wipe: each wipe sound sends a white scan down the window and the text lines
/// fade and shiver, then come back. The hits come closer together along a timeline,
/// and the wipe has its own volume dial.
/// </summary>
internal sealed class MindWipeLoop : HelpLoopScene
{
    private const double Dur = 7600;
    private static readonly Rect Win = new(60, 22, 320, 170);
    private static readonly double[] Events = { 800, 2900, 4400, 5400, 6050, 6500 };

    private static readonly Brush TlBase = Frozen(Color.FromRgb(0x3B, 0x30, 0x60));
    private static readonly Brush TickOff = Frozen(Color.FromRgb(0x4A, 0x3D, 0x73));
    private static readonly Brush ScanCore = Frozen(Colors.White);

    private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
    {
        new HelpLoopStep("help_loop_mindwipe_1", 0, 2600),
        new HelpLoopStep("help_loop_mindwipe_2", 2600, 5400),
        new HelpLoopStep("help_loop_mindwipe_3", 5400, 7400),
    };

    public override string Id => "MindWipe";
    public override double DurationMs => Dur;
    public override double StillMs => 3050;
    public override IReadOnlyList<HelpLoopStep> Steps => StepList;

    public override void Draw(LoopFrame f, double t)
    {
        var p = f.P;
        var dk = f.Desktop(Win);

        double e = -1e9;
        foreach (var x in Events) if (t >= x) e = x;
        double pk = LoopMath.Seg(t, e, e + 340);
        double backK = LoopMath.Seg(t, e + 340, e + 900);

        // Lines the scan has passed fade and shiver: paint over the desktop's line with the
        // window colour, then redraw it faded and nudged sideways.
        var back = f.Back;
        for (int i = 0; i < dk.Lines.Count; i++)
        {
            var l = dk.Lines[i];
            double ly = l.Y - dk.Window.Y;
            bool passed = pk * Win.Height > ly;
            if (!passed || backK >= 1) continue;
            double o = 1 - .92 * (1 - backK);
            double dx = Math.Sin(t / 23 + i) * 4 * (1 - backK);
            var cover = l;
            cover.Inflate(1.5, 1.5);
            back.DrawRectangle(p.Window, null, cover);
            back.PushOpacity(o);
            back.DrawRoundedRectangle(i % 3 == 1 ? p.LineB : p.LineA, null, new Rect(l.X + dx, l.Y, l.Width, l.Height), l.Height / 2, l.Height / 2);
            back.Pop();
        }

        // The scan line, clipped to the window.
        if (pk > 0 && pk < 1)
        {
            var dc = f.Front;
            double y = dk.Window.Y + 18 + pk * (Win.Height - 18);
            dc.PushClip(new RectangleGeometry(dk.Window, 8, 8));
            dc.PushOpacity(.18);
            dc.DrawRectangle(p.Lilac, null, new Rect(dk.Window.X, y - 10, dk.Window.Width, 23));
            dc.Pop();
            dc.PushOpacity(.45);
            dc.DrawRectangle(ScanCore, null, new Rect(dk.Window.X, y - 4, dk.Window.Width, 11));
            dc.Pop();
            dc.DrawRectangle(ScanCore, null, new Rect(dk.Window.X, y, dk.Window.Width, 3));
            dc.Pop();
        }

        double wk = LoopMath.Seg(t, e, e + 650);
        f.Speaker(406, 30, wk > 0 && wk < 1 ? wk : 0);

        DrawTimeline(f, t);

        f.Slider(330, 200, "WIPE VOLUME", .55);
        if (t > 5400)
            f.Front.DrawRoundedRectangle(null, new Pen(p.Accent, 1), new Rect(330.5, 200.5, 129, 33), 9, 9);
    }

    private static void DrawTimeline(LoopFrame f, double t)
    {
        var p = f.P;
        var dc = f.Front;
        const double left = 20, width = 440, top = 270 - 30 - 22;
        dc.PushOpacity(t > 2400 ? 1 : .55);
        dc.DrawRectangle(TlBase, null, new Rect(left, top + 10, width, 2));
        for (int i = 0; i < Events.Length; i++)
        {
            double x = left + Events[i] / Dur * width;
            bool lit = t >= Events[i];
            var r = new Rect(x - 1, top + 4, 3, 14);
            if (lit)
            {
                dc.PushOpacity(.35);
                dc.DrawRoundedRectangle(p.Lilac, null, new Rect(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), 3, 3);
                dc.Pop();
            }
            dc.DrawRoundedRectangle(lit ? p.Lilac : TickOff, null, r, 1.5, 1.5);
        }
        dc.DrawRectangle(p.Accent, null, new Rect(left + t / Dur * width, top, 2, 22));
        dc.Pop();
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
