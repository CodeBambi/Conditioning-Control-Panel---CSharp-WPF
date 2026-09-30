using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Controls.HelpLoops.Scenes;

/// <summary>
/// Spiral Overlay: six arms turn over the whole desktop, the cursor drags the opacity
/// slider up and back down, then clicks SAVE straight through the spiral.
/// </summary>
internal sealed class SpiralOverlayLoop : HelpLoopScene
{
    // 7200 ms x 0.0011636 rad/ms = 8 pi / 3, a whole number of arm pairs, so the loop is seamless.
    private const double RotRadPerMs = 0.0011636;
    private static readonly Point Centre = new(240, 135);
    private static readonly StreamGeometry[] Arms = BuildArms();

    private static readonly (double T, double V)[] Opacity =
        { (0, .15), (2000, .15), (3000, .45), (4300, .45), (5200, .25) };

    // SAVE button inside the default window (60,28,320,190): right 14, bottom 14, 20 high.
    private const double BtnX = 60 + 320 - 14 - 22, BtnY = 28 + 190 - 14 - 10;
    private static readonly Rect BtnRect = new(BtnX - 22, BtnY - 10, 44, 20);
    private static readonly Brush BtnFill = LoopPalette.Solid("#3b2f63");
    private static readonly Brush BtnText = LoopPalette.Solid("#d9ccff");
    private static readonly Brush BtnTextHot = LoopPalette.Solid("#10231d");

    private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
    {
        new HelpLoopStep("help_loop_spiraloverlay_1", 0, 1800),
        new HelpLoopStep("help_loop_spiraloverlay_2", 1800, 5400),
        new HelpLoopStep("help_loop_spiraloverlay_3", 5400, 7000),
    };

    public override string Id => "SpiralOverlay";
    public override double DurationMs => 7200;
    public override double StillMs => 3200;
    public override IReadOnlyList<HelpLoopStep> Steps => StepList;

    public override void Draw(LoopFrame f, double t)
    {
        var p = f.P;
        f.Desktop();

        bool saved = t > 6180 && t < 6700;
        f.Back.DrawRoundedRectangle(saved ? p.Mint : BtnFill, null, BtnRect, 6, 6);
        f.DrawText(f.Back, "SAVE", BtnX, BtnY - 7, 10, saved ? BtnTextHot : BtnText, LoopFrame.Body, FontWeights.SemiBold, TextAlignment.Center);

        double o = LoopMath.Schedule(Opacity, t);
        var dc = f.Front;
        dc.PushOpacity(o);
        dc.PushTransform(new RotateTransform(t * RotRadPerMs * 180 / Math.PI, Centre.X, Centre.Y));
        for (int k = 0; k < Arms.Length; k++)
        {
            var pen = new Pen(k % 2 == 1 ? p.Accent : p.Lilac, 14) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, pen, Arms[k]);
        }
        dc.Pop();
        dc.Pop();

        f.Slider(14, 12, "SPIRAL OPACITY", o);
        var v1 = f.SliderKnob(14, 12, .15);
        var v2 = f.SliderKnob(14, 12, .45);
        var v3 = f.SliderKnob(14, 12, .25);

        var c = LoopMath.Path(new (double, double, double)[]
        {
            (1200, 300, 230), (1900, v1.X, v1.Y), (2000, v1.X, v1.Y), (3000, v2.X, v2.Y), (4300, v2.X, v2.Y),
            (5200, v3.X, v3.Y), (5500, v3.X, v3.Y), (6100, BtnX, BtnY), (6800, BtnX, BtnY),
        }, t);
        f.Ripple(BtnX, BtnY, LoopMath.Seg(t, 6180, 6600));
        f.Cursor(c.X, c.Y, (t > 1950 && t < 3050) || (t > 4250 && t < 5250) || (t > 6150 && t < 6300));
    }

    private static StreamGeometry[] BuildArms()
    {
        var arms = new StreamGeometry[6];
        for (int k = 0; k < 6; k++)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                for (int r = 2; r < 300; r += 4)
                {
                    double a = k * Math.PI / 3 + r * 0.028;
                    var pt = new Point(Centre.X + Math.Cos(a) * r, Centre.Y + Math.Sin(a) * r);
                    if (r == 2) ctx.BeginFigure(pt, false, false);
                    else ctx.LineTo(pt, true, true);
                }
            }
            g.Freeze();
            arms[k] = g;
        }
        return arms;
    }
}
