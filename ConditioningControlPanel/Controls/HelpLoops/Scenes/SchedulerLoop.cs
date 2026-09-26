using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops.Scenes;

/// <summary>
/// Scheduler: days and hours are picked on a small card, the taskbar clock reaches the window,
/// and the app starts on its own (the card lights, the first flash lands).
/// </summary>
internal sealed class SchedulerLoop : HelpLoopScene
{
    private static readonly Rect CardRect = new(84, 44, 276, 124);
    private static readonly string[] Days = { "M", "T", "W", "T", "F", "S", "S" };
    private static readonly bool[] Lit = { true, false, true, false, true, false, false };
    private const int Picked = 5;
    private const double DayY = 44 + 52, DayX0 = 84 + 30, DayGap = 36, DayR = 13;
    private const double Click = 1400;

    private static readonly (double, double, double)[] CursorPath =
    {
        (0, 250, 214), (300, 250, 214), (1250, DayX0 + Picked * DayGap + 3, DayY + 4),
        (1650, DayX0 + Picked * DayGap + 3, DayY + 4), (2500, 250, 214),
    };

    private static readonly Brush OnText = LoopPalette.Solid("#1a1030");

    private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
    {
        new HelpLoopStep("help_loop_scheduler_1", 300, 2600),
        new HelpLoopStep("help_loop_scheduler_2", 2600, 4600),
        new HelpLoopStep("help_loop_scheduler_3", 4600, 6800),
    };

    public override string Id => "Scheduler";
    public override double DurationMs => 7000;
    public override double StillMs => 4200;
    public override IReadOnlyList<HelpLoopStep> Steps => StepList;

    public override void Draw(LoopFrame f, double t)
    {
        var p = f.P;
        var dc = f.Front;
        f.Desktop();

        double outK = Seg(t, 6400, 6900);
        double started = Seg(t, 4600, 4800) * (1 - outK);

        // --- the schedule card ---
        f.Card(CardRect, started > .5 ? p.Accent : p.Border);
        f.Text("Scheduler", CardRect.X + 16, CardRect.Y + 12, 13, p.Text, bold: true);
        for (int i = 0; i < Days.Length; i++)
        {
            bool on = Lit[i] || (i == Picked && t > Click && t < 6600);
            double pulse = i == Picked ? Seg(t, Click, Click + 250) * (1 - Seg(t, 6600, 6800)) : 0;
            var c = new Point(DayX0 + i * DayGap, DayY);
            double r = DayR * (1 + .15 * Tri(pulse * 2));
            dc.DrawEllipse(on ? p.Accent : p.Panel2, new Pen(on ? p.Accent : p.Border, 1), c, r, r);
            f.DrawText(dc, Days[i], c.X, c.Y - 8, 11, on ? OnText : p.Dim, LoopFrame.Display, FontWeights.SemiBold, TextAlignment.Center);
        }
        f.DrawText(dc, "HOURS", CardRect.X + 16, CardRect.Y + 86, 9, p.Dim, LoopFrame.Mono, FontWeights.Medium, TextAlignment.Left);
        f.Chip(CardRect.X + 66, CardRect.Y + 80, "20:00 - 22:00", hot: started > .5);

        // --- the clock in the corner ---
        string clock = t < 3200 ? "19:58" : t < 4200 ? "19:59" : "20:00";
        double tick = t >= 4200 ? Seg(t, 4200, 4450) : t >= 3200 ? Seg(t, 3200, 3450) : 0;
        double clockO = Seg(t, 0, 300) * (1 - outK);
        using (f.At(dc, 428, 234, 1 + .12 * Tri(tick * 2), clockO))
            f.Chip(402, 223, clock, hot: t >= 4200);

        // --- it starts by itself ---
        if (started > 0)
        {
            double ring = (t - 4600) % 1000 / 1000;
            using (f.Fade(dc, started * (1 - ring)))
                dc.DrawEllipse(null, new Pen(p.Accent, 2), new Point(CardRect.Right - 22, CardRect.Y + 22), 5 + 10 * ring, 5 + 10 * ring);
            using (f.Fade(dc, started))
                dc.DrawEllipse(p.Accent, null, new Point(CardRect.Right - 22, CardRect.Y + 22), 5, 5);
            f.Chip(CardRect.Right - 96, CardRect.Bottom + 8, "STARTED", hot: true, opacity: started);
        }

        double pk = Seg(t, 5000, 5280) * (1 - outK);
        if (pk > 0)
        {
            var r = new Rect(372, 66, 96, 70);
            using (f.At(dc, r.X + r.Width / 2, r.Y + r.Height / 2, Lerp(.6, 1, Back(pk)), pk))
                f.Photo(r, PhotoLook.Sea);
        }

        var cur = Path(CursorPath, t);
        f.Ripple(DayX0 + Picked * DayGap, DayY, Seg(t, Click, Click + 450));
        f.Cursor(cur.X, cur.Y, t > Click - 30 && t < Click + 110);
    }
}
