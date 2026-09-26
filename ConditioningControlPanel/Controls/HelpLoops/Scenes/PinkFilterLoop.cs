using System.Collections.Generic;
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

    private static readonly Brush PinkWash = LoopPalette.Solid("#ff4fa8");
    private static readonly Brush ChkEdge = LoopPalette.Solid("#8f7fc4");
    private static readonly Brush LabelBrush = LoopPalette.Solid("#d9ccff");

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
        back.DrawRoundedRectangle(ticked ? p.Mint : null, new Pen(ticked ? p.Mint : ChkEdge, 2),
            new Rect(ChkX - 6, ChkY - 6, 12, 12), 3, 3);
        f.DrawText(back, "Remember me", 60 + 38, ChkY - 7, 10, LabelBrush, LoopFrame.Body, FontWeights.SemiBold, TextAlignment.Left);

        double o = LoopMath.Schedule(Opacity, t);
        if (o > 0)
        {
            f.Front.PushOpacity(o);
            f.Front.DrawRectangle(PinkWash, null, new Rect(0, 0, LoopFrame.StageWidth, LoopFrame.StageHeight));
            f.Front.Pop();
        }

        f.Slider(14, 12, "PINK OPACITY", o);
        var a = f.SliderKnob(14, 12, .12);
        var b = f.SliderKnob(14, 12, .38);
        var c = f.SliderKnob(14, 12, .2);

        var cur = LoopMath.Path(new (double, double, double)[]
        {
            (1800, 300, 230), (2600, a.X, a.Y), (2700, a.X, a.Y), (3600, b.X, b.Y), (4600, b.X, b.Y),
            (5200, c.X, c.Y), (5400, c.X, c.Y), (6000, ChkX, ChkY), (6900, ChkX, ChkY),
        }, t);
        f.Ripple(ChkX, ChkY, LoopMath.Seg(t, 6080, 6500));
        f.Cursor(cur.X, cur.Y, (t > 2650 && t < 3650) || (t > 4550 && t < 5250) || (t > 6050 && t < 6200));
    }
}
