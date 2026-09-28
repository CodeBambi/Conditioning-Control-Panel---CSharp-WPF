using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    /// <summary>
    /// Blink Trainer: a see-through picture sits over the desktop, the eye blinks and the picture
    /// cuts to a new one each time. The cursor clicks a line under it: the overlay lets clicks through.
    /// </summary>
    internal sealed class BlinkTrainerLoop : HelpLoopScene
    {
        private const double Dur = 6000, Shut = 120;
        private static readonly double[] Blinks = { 2000, 3300, 4600 };
        private static readonly Rect Overlay = new(34, 14, 412, 226);
        private const double ClickT = 5050;

        private static readonly (double, double, double)[] CursorPath =
            { (0, 440, 236), (4200, 440, 236), (5000, 150, 111), (5350, 150, 111), (5900, 440, 236) };

        public override string Id => "BlinkTrainer";
        public override double DurationMs => Dur;
        public override double StillMs => 1600;

        public override IReadOnlyList<HelpLoopStep> Steps { get; } = new[]
        {
            new HelpLoopStep("help_loop_blinktrainer_1", 0, 1800),
            new HelpLoopStep("help_loop_blinktrainer_2", 1800, 3600),
            new HelpLoopStep("help_loop_blinktrainer_3", 3600, 5800),
        };

        /// <summary>How shut the eye is at t: down over 120 ms, up over 120 ms.</summary>
        private static double Lid(double t)
        {
            foreach (var b in Blinks)
            {
                if (t >= b - Shut && t < b) return EaseInOut(Seg(t, b - Shut, b));
                if (t >= b && t < b + Shut) return 1 - EaseInOut(Seg(t, b, b + Shut));
            }
            return 0;
        }

        public override void Draw(LoopFrame f, double t)
        {
            var desk = f.Desktop();
            var dc = f.Front;

            // The picture changes at the bottom of each blink: a cut, never a fade.
            int look = 0; // Pink, Stripes, Sea
            foreach (var b in Blinks) if (t >= b) look++;
            look %= 3;

            // The line the cursor clicks, lit on the desktop under the overlay.
            var line = desk.Lines[3];
            var lit = Seg(t, ClickT, ClickT + 120) * (1 - Seg(t, 5500, 5900));
            if (lit > 0)
                using (f.Fade(f.Back, lit))
                    f.Back.DrawRoundedRectangle(f.P.Mint, null, LoopFrame.Inset(line, -1), 4, 4);

            using (f.Fade(dc, .5))
                f.Photo(dc, Overlay, (PhotoLook)look);

            // Eye in a small glass card, top left over the picture.
            f.Card(new Rect(14, 10, 72, 40), f.P.Border);
            f.Eye(50, 30, 1 - Lid(t), 0, 0, .9);

            // A blink ticks a small mark by the eye.
            foreach (var b in Blinks)
            {
                var k = Seg(t, b, b + 700);
                if (k > 0 && k < 1)
                    using (f.At(dc, 118, 21, Lerp(.8, 1, Back(Seg(k, 0, .25)))))
                        f.Chip(92, 10, "blink", hot: true, opacity: 1 - Seg(k, .6, 1));
            }

            f.Chip(356, 20, "click-through", hot: true, opacity: Seg(t, ClickT, ClickT + 200) * (1 - Seg(t, 5600, 5900)));

            f.Ripple(150, 111, Seg(t, ClickT, ClickT + 420));
            var c = Path(CursorPath, t);
            f.Cursor(c.X, c.Y, t > ClickT - 60 && t < ClickT + 110);
        }
    }
}
