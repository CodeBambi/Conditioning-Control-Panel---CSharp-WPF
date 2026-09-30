using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops.Scenes;

/// <summary>
/// Presets: a slider is dragged, Save turns the settings into a card, the sliders get moved
/// away, and one click on the card glides them back.
/// </summary>
internal sealed class PresetsLoop : HelpLoopScene
{
    private static readonly Rect Win = new(24, 22, 432, 200);
    private const double SX = 44, S1Y = 58, S2Y = 102;
    private static readonly Rect SaveBtn = new(44, 150, 78, 26);
    private static readonly Rect NewCard = new(362, 56, 78, 70);
    private static readonly (string Name, double A, double B, Rect R)[] OldCards =
    {
        ("Calm", .25, .7, new Rect(194, 56, 78, 70)),
        ("Evening", .6, .35, new Rect(278, 56, 78, 70)),
    };

    // Slider 1: dragged up, knocked away, clicked back, reset for the wrap.
    private static readonly (double, double)[] S1 =
        { (500, .3), (1600, .8), (3300, .8), (3500, .15), (4500, .15), (5300, .8), (6200, .8), (6700, .3) };
    private static readonly (double, double)[] S2 =
        { (3300, .5), (3500, .9), (4500, .9), (5300, .5) };

    private static readonly (double, double, double)[] CursorPath;

    static PresetsLoop()
    {
        var k0 = new Point(SX + 9 + .3 * 81, S1Y + 25);
        var k1 = new Point(SX + 9 + .8 * 81, S1Y + 25);
        CursorPath = new[]
        {
            (0.0, 150.0, 236.0), (450, k0.X, k0.Y), (500, k0.X, k0.Y), (1600, k1.X, k1.Y),
            (1800, k1.X, k1.Y), (2300, SaveBtn.X + 40, SaveBtn.Y + 14), (2700, SaveBtn.X + 40, SaveBtn.Y + 14),
            (3200, 330, 200), (3700, 330, 200), (4300, NewCard.X + 40, NewCard.Y + 38),
            (4800, NewCard.X + 40, NewCard.Y + 38), (5600, 300, 236), (6200, 300, 236), (6700, 150, 236),
        };
    }

    private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
    {
        new HelpLoopStep("help_loop_presets_1", 0, 2000),
        new HelpLoopStep("help_loop_presets_2", 2000, 3800),
        new HelpLoopStep("help_loop_presets_3", 3800, 6600),
    };

    public override string Id => "Presets";
    public override double DurationMs => 6800;
    public override double StillMs => 3000;
    public override IReadOnlyList<HelpLoopStep> Steps => StepList;

    public override void Draw(LoopFrame f, double t)
    {
        var p = f.P;
        var dc = f.Front;
        f.Desktop(Win, drawLines: false);

        double v1 = Schedule(S1, t), v2 = Schedule(S2, t);
        bool dragging = t > 500 && t < 1600;
        bool recalled = t > 4400 && t < 5600;
        f.Slider(SX, S1Y, "FLASH SIZE", v1, dragging || recalled ? p.Accent : null);
        f.Slider(SX, S2Y, "OPACITY", v2, recalled ? p.Accent : null);

        // Save
        bool saveDown = t > 2380 && t < 2520;
        double saveLit = Seg(t, 2400, 2500) * (1 - Seg(t, 2900, 3300));
        dc.DrawRoundedRectangle(saveLit > 0 ? p.Accent : p.Panel2, new Pen(saveLit > 0 ? p.Accent : p.Border, 1),
            LoopFrame.Inset(new Rect(SaveBtn.X, SaveBtn.Y + (saveDown ? 1 : 0), SaveBtn.Width, SaveBtn.Height), .5), 8, 8);
        f.DrawText(dc, "Save", SaveBtn.X + SaveBtn.Width / 2, SaveBtn.Y + 5, 12, saveLit > 0 ? p.Ink : p.Text,
            LoopFrame.Display, FontWeights.SemiBold, TextAlignment.Center);

        f.DrawText(dc, "PRESETS", 194, 36, 9, p.Dim, LoopFrame.Mono, FontWeights.Medium, TextAlignment.Left);
        foreach (var c in OldCards) PresetCard(f, c.R, c.Name, c.A, c.B, false, 1, 1);

        // the new card slides in on Save and leaves again for the wrap
        double inK = Seg(t, 2500, 2950);
        double gone = Seg(t, 6200, 6600);
        double o = inK * (1 - gone);
        if (o > 0)
        {
            double dy = Lerp(24, 0, EaseOut(inK));
            double click = Seg(t, 4400, 5000);
            double pulse = click > 0 && click < 1 ? Math.Sin(click * Math.PI) : 0;
            var r = new Rect(NewCard.X, NewCard.Y + dy, NewCard.Width, NewCard.Height);
            PresetCard(f, r, "Mine", .8, .5, t > 2500 && t < 3300 || recalled, o, Lerp(.7, 1, Back(inK)) + .05 * pulse);
        }

        f.Floater(NewCard.X + 18, NewCard.Bottom + 24, Seg(t, 2600, 3500), "saved");

        var cp = Path(CursorPath, t);
        f.Ripple(SaveBtn.X + 40, SaveBtn.Y + 14, Seg(t, 2400, 2850));
        f.Ripple(NewCard.X + 40, NewCard.Y + 38, Seg(t, 4400, 4850));
        f.Cursor(cp.X, cp.Y, dragging || saveDown || (t > 4380 && t < 4520));
    }

    /// <summary>A preset card: name, and two small bars showing what it stores.</summary>
    private static void PresetCard(LoopFrame f, Rect r, string name, double a, double b, bool hot, double opacity, double scale)
    {
        var p = f.P;
        var dc = f.Front;
        using (f.At(dc, r.X + r.Width / 2, r.Y + r.Height / 2, scale, opacity))
        {
            LoopFrame.SoftShadow(dc, r, 9, 8, 18, 0x99);
            dc.DrawRoundedRectangle(p.Panel, new Pen(hot ? p.Accent : p.Border, hot ? 1.5 : 1), LoopFrame.Inset(r, .5), 9, 9);
            f.DrawText(dc, name, r.X + 9, r.Y + 8, 12, p.Text, LoopFrame.Display, FontWeights.SemiBold, TextAlignment.Left);
            for (int i = 0; i < 2; i++)
            {
                var tr = new Rect(r.X + 9, r.Y + 36 + i * 12, r.Width - 18, 4);
                dc.DrawRoundedRectangle(p.Track, null, tr, 2, 2);
                dc.DrawRoundedRectangle(i == 0 ? p.Accent : p.Lilac, null, new Rect(tr.X, tr.Y, tr.Width * (i == 0 ? a : b), 4), 2, 2);
            }
        }
    }
}
