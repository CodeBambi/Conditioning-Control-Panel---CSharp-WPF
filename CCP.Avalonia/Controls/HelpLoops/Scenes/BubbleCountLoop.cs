using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Controls.HelpLoops.Scenes
{
    /// <summary>Bubble Count: seven bubbles pop onto a dimmed screen under a draining timer, vanish,
    /// and a "How many?" card asks for the number; the cursor picks 7 for XP
    /// (WPF Controls/HelpLoops/Scenes/BubbleCountLoop.cs, same timings).</summary>
    internal sealed class BubbleCountLoop : HelpLoopScene
    {
        private static readonly Point[] Spots =
        {
            new(90, 70), new(170, 140), new(250, 60), new(320, 150), new(400, 90), new(140, 210), new(360, 210),
        };

        private static readonly string[] Options = { "5", "6", "7", "8" };

        // Ask card (120,70) 240x120; four 40px buttons 10 apart, centred, 50 below the card top.
        private static readonly Rect AskRect = new(120, 70, 240, 120);
        private const double RowX = 120 + (240 - (4 * 40 + 3 * 10)) / 2.0, RowY = 70 + 50;
        // The mockup's cursor target (lands on the third button).
        private const double PickX = 120 + 14 + 2 * 50 + 20 + 6, PickY = 70 + 14 + 28 + 12 + 20;

        private static readonly (double, double, double)[] CursorPath =
            { (3400, 420, 236), (4400, PickX, PickY), (5400, PickX, PickY), (6500, 430, 236) };

        private static readonly IBrush DimBrush = LoopPalette.Solid("#07040f99");
        private static readonly IBrush OkText = LoopPalette.Solid("#10231d");
        // Palette Border / Mint (fixed colours), 1 wide.
        private static readonly Pen Edge = new(LoopPalette.Solid("#342a55"), 1);
        private static readonly Pen OkEdge = new(LoopPalette.Solid("#5fffd0"), 1);

        public override string Id => "BubbleCount";
        public override double DurationMs => 7400;
        public override double StillMs => 2000;

        public override IReadOnlyList<HelpLoopStep> Steps { get; } = new[]
        {
            new HelpLoopStep("help_loop_bubblecount_1", 600, 2800),
            new HelpLoopStep("help_loop_bubblecount_2", 2800, 3300),
            new HelpLoopStep("help_loop_bubblecount_3", 3300, 6400),
        };

        public override void Draw(LoopFrame f, double t)
        {
            var p = f.P;
            var dc = f.Front;
            f.Desktop();

            double d = LoopMath.Seg(t, 500, 800) * (1 - LoopMath.Seg(t, 6400, 6900));
            if (d > 0)
                using (dc.PushOpacity(d))
                    dc.DrawRectangle(DimBrush, null, new Rect(0, 0, LoopFrame.StageWidth, LoopFrame.StageHeight));

            double gone = LoopMath.Seg(t, 2800, 3050);
            for (int i = 0; i < Spots.Length; i++)
            {
                double k = LoopMath.Seg(t, 700 + i * 90, 950 + i * 90);
                double s = LoopMath.Lerp(.3, 1, LoopMath.Back(k)) * (1 - .6 * gone);
                f.Bubble(Spots[i].X, Spots[i].Y, s, k * (1 - gone));
            }

            // Timer bar, drains while the bubbles are up.
            double tmO = LoopMath.Seg(t, 900, 1100) * (1 - LoopMath.Seg(t, 2800, 3000));
            if (tmO > 0)
                using (dc.PushOpacity(tmO))
                {
                    dc.DrawRoundedRectangle(p.Track, null, new Rect(60, 14, 360, 5), 2.5, 2.5);
                    double w = 360 * (1 - LoopMath.Seg(t, 1100, 2800));
                    if (w > 0) dc.DrawRoundedRectangle(p.Lilac, null, new Rect(60, 14, w, 5), 2.5, 2.5);
                }

            // The question.
            double ak = LoopMath.Seg(t, 3150, 3450), aout = LoopMath.Seg(t, 6300, 6700);
            double askO = ak * (1 - aout);
            if (askO > 0)
            {
                double sc = LoopMath.Lerp(.85, 1, LoopMath.Back(ak));
                using (dc.PushOpacity(askO))
                using (f.At(dc, AskRect.Center.X, AskRect.Center.Y, sc))
                {
                    f.Card(AskRect, p.Lilac);
                    f.Text("How many?", AskRect.X + AskRect.Width / 2, AskRect.Y + 14, 16, p.Text, align: TextAlignment.Center);
                    for (int i = 0; i < Options.Length; i++)
                    {
                        bool ok = i == 2 && t > 4550;
                        var r = new Rect(RowX + i * 50, RowY, 40, 40);
                        dc.DrawRoundedRectangle(ok ? p.Mint : p.Panel2, ok ? OkEdge : Edge, LoopFrame.Inset(r, .5), 9.5, 9.5);
                        f.Text(Options[i], r.X + 20, r.Y + 8, 17, ok ? OkText : p.Text, bold: true, align: TextAlignment.Center);
                    }
                }
            }

            f.Floater(PickX - 8, PickY - 34, LoopMath.Seg(t, 4550, 5500), "+XP");

            var cur = LoopMath.Path(CursorPath, t);
            f.Ripple(PickX, PickY, LoopMath.Seg(t, 4520, 4950));
            f.Cursor(cur.X, cur.Y, t > 4500 && t < 4650);
        }
    }
}
