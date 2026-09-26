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
        var dk = f.Desktop(Win, drawLines: false);

        double e = -1e9;
        foreach (var x in Events) if (t >= x) e = x;
        double pk = LoopMath.Seg(t, e, e + 340);
        double backK = LoopMath.Seg(t, e + 340, e + 900);

        // Lines the scan has passed fade and shiver, then come back.
        for (int i = 0; i < dk.Lines.Count; i++)
        {
            var l = dk.Lines[i];
            bool passed = pk * Win.Height > l.Y - dk.Window.Y && backK < 1;
            double o = passed ? 1 - .92 * (1 - backK) : 1;
            double dx = passed ? Math.Sin(t / 23 + i) * 4 * (1 - backK) : 0;
            f.DesktopLine(f.Back, new Rect(l.X + dx, l.Y, l.Width, l.Height), i, o);
        }

        // The scan line, clipped to the window.
        if (pk > 0 && pk < 1)
        {
            var dc = f.Front;
            var w = dk.Window;
            double y = w.Y + 18 + pk * (Win.Height - 18);
            using (f.ClipTo(dc, new RectangleGeometry(w, 8, 8)))
            {
                using (f.Fade(dc, .18)) dc.DrawRectangle(p.Lilac, null, new Rect(w.X, y - 10, w.Width, 23));
                using (f.Fade(dc, .45)) dc.DrawRectangle(p.White, null, new Rect(w.X, y - 4, w.Width, 11));
                dc.DrawRectangle(p.White, null, new Rect(w.X, y, w.Width, 3));
            }
        }

        double wk = LoopMath.Seg(t, e, e + 650);
        f.Speaker(406, 30, wk > 0 && wk < 1 ? wk : 0, false, 3);

        DrawTimeline(f, t);

        f.Slider(330, 200, "WIPE VOLUME", .55, t > 5400 ? p.Accent : null);
    }

    private static void DrawTimeline(LoopFrame f, double t)
    {
        var p = f.P;
        var dc = f.Front;
        const double left = 20, width = 440, top = 270 - 30 - 22;
        using (f.Fade(dc, t > 2400 ? 1 : .55))
        {
            dc.DrawRectangle(p.Track, null, new Rect(left, top + 10, width, 2));
            for (int i = 0; i < Events.Length; i++)
            {
                double x = left + Events[i] / Dur * width;
                bool lit = t >= Events[i];
                var r = new Rect(x - 1, top + 4, 3, 14);
                if (lit)
                    using (f.Fade(dc, .35))
                        dc.DrawRoundedRectangle(p.Lilac, null, new Rect(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), 3, 3);
                dc.DrawRoundedRectangle(lit ? p.Lilac : p.LineA, null, r, 1.5, 1.5);
            }
            dc.DrawRectangle(p.Accent, null, new Rect(left + t / Dur * width, top, 2, 22));
        }
    }
}
