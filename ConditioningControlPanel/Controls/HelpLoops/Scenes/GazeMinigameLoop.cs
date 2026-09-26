using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    /// <summary>
    /// Gaze Minigame: two pictures, a target (mint) and a distractor (red). Resting the look on the
    /// target fills its meter and scores; a look at the distractor fills the other meter instead.
    /// </summary>
    internal sealed class GazeMinigameLoop : HelpLoopScene
    {
        private const double Dur = 7400;
        private static readonly Rect TargetCard = new(58, 46, 150, 112);
        private static readonly Rect OtherCard = new(272, 46, 150, 112);
        private static readonly Point TargetMid = new(133, 102);
        private static readonly double[] Scores = { 2300, 6150 };

        private static readonly (double T, double X, double Y)[] Look =
        {
            (0, 240, 128), (650, 133, 102), (2650, 133, 102), (2850, 347, 102),
            (3950, 347, 102), (4250, 133, 102), (6600, 133, 102), (7250, 240, 128),
        };

        public override string Id => "GazeMinigame";
        public override double DurationMs => Dur;
        public override double StillMs => 3000;

        public override IReadOnlyList<HelpLoopStep> Steps { get; } = new[]
        {
            new HelpLoopStep("help_loop_gazeminigame_1", 0, 2600),
            new HelpLoopStep("help_loop_gazeminigame_2", 2600, 5000),
            new HelpLoopStep("help_loop_gazeminigame_3", 5000, 7200),
        };

        private static double Wave(double t, int periods) => Math.Sin(2 * Math.PI * periods * t / Dur);

        private static void Rim(LoopFrame f, Rect r, Brush brush, double opacity)
        {
            using (f.Fade(f.Front, opacity))
                f.Front.DrawRoundedRectangle(null, new Pen(brush, 2.5), LoopFrame.Inset(r, -3), 6, 6);
        }

        public override void Draw(LoopFrame f, double t)
        {
            f.Desktop();
            var dc = f.Front;

            var show = Seg(t, 0, 300) * (1 - Seg(t, 7000, 7400));

            // Meters: the target's fills while looked at and resets on a score; the distractor's
            // creeps up during the stray look and drains once the look is back.
            var good = t < Scores[0]
                ? EaseInOut(Seg(t, 750, Scores[0]))
                : t < 4300 ? 1 - Seg(t, Scores[0], Scores[0] + 250)
                : EaseInOut(Seg(t, 4400, Scores[1])) * (1 - Seg(t, Scores[1], Scores[1] + 250));
            var bad = .55 * Seg(t, 2900, 3950) * (1 - Seg(t, 4250, 4900));

            var onTarget = t > 700 && t < 2650 || t > 4300 && t < 6600;
            var onOther = t > 2900 && t < 3950;

            // The target always wears mint and the distractor lilac; a look lights the rim, and a
            // look at the distractor turns its rim red.
            f.Card(LoopFrame.Inset(TargetCard, -4), f.P.Border);
            f.Photo(dc, TargetCard, PhotoLook.Sea, shadow: false);
            Rim(f, TargetCard, f.P.Mint, onTarget ? 1 : .45);
            f.Card(LoopFrame.Inset(OtherCard, -4), f.P.Border);
            f.Photo(dc, OtherCard, PhotoLook.Stripes, shadow: false);
            Rim(f, OtherCard, onOther ? f.P.Red : f.P.Lilac, onOther ? 1 : .45);

            f.Chip(TargetCard.X, TargetCard.Bottom + 10, "target", hot: onTarget);
            f.Chip(OtherCard.X, OtherCard.Bottom + 10, "ignore", hot: onOther);
            f.Meter(new Rect(TargetCard.X + 70, TargetCard.Bottom + 18, 80, 7), good, f.P.Mint);
            f.Meter(new Rect(OtherCard.X + 70, OtherCard.Bottom + 18, 80, 7), bad, f.P.Red);

            // The score resets out of sight: its chip fades across the wrap.
            int score = 3;
            foreach (var s in Scores) if (t >= s) score++;
            f.Chip(207, 12, "score " + score.ToString(CultureInfo.InvariantCulture),
                hot: t >= Scores[0] && t < Scores[0] + 500 || t >= Scores[1] && t < Scores[1] + 500, opacity: show);

            foreach (var s in Scores)
                f.Floater(TargetMid.X - 8, TargetMid.Y - 8, Seg(t, s, s + 1300), "+1", f.P.Gold);

            var g = Path(Look, t);
            var gx = g.X + 1.4 * Wave(t, 71);
            var gy = g.Y + 1.2 * Wave(t + 500, 53);
            dc.DrawEllipse(f.P.Glass, null, new Point(gx, gy), 10, 10);
            f.GazeDot(gx, gy);

            f.Eye(240, 240, 1 - Tri(Seg(t, 4950, 5170) * 2),
                Clamp((gx - 240) / 120, -1, 1), Clamp((gy - 128) / 90, -1, 1), .9);
        }
    }
}
