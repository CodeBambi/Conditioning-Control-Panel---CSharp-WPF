using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    /// <summary>
    /// Focus Gaze: the gaze dot settles on a bubble, a ring fills for about a second and the bubble
    /// pops by itself. On a second bubble the look drifts off early and the ring empties.
    /// </summary>
    internal sealed class FocusGazeLoop : HelpLoopScene
    {
        private const double Dur = 6400, PopT = 3750;
        private const double TargetX = 170;

        // Background bubbles: x and a phase so each rises on its own clock and wraps unseen.
        private static readonly (double X, double Phase)[] Drifters =
            { (70, 800), (262, 3300), (420, 1700), (338, 4509) };
        private const int Second = 3; // the one the look slides off

        private static readonly (double T, double X, double Y)[] Wander =
        {
            (0, 408, 70), (900, 330, 60), (1800, 250, 120), (3900, 240, 90),
            (4400, 300, 110), (5700, 400, 150), (6400, 408, 70),
        };

        private static readonly Brush Droplet = LoopPalette.Solid("#ffc2e2");

        public override string Id => "FocusGaze";
        public override double DurationMs => Dur;
        public override double StillMs => 3400;

        public override IReadOnlyList<HelpLoopStep> Steps { get; } = new[]
        {
            new HelpLoopStep("help_loop_focusgaze_1", 400, 2400),
            new HelpLoopStep("help_loop_focusgaze_2", 2400, 3800),
            new HelpLoopStep("help_loop_focusgaze_3", 3800, 6000),
        };

        /// <summary>The bubble the look pops: rises in a straight line, gone after the pop.</summary>
        private static Point Target(double t) => new(TargetX + 10 * Math.Sin(t / 520), 282 - .055 * t);

        private static Point Drifter(int i, double t)
        {
            var age = ((t + Drifters[i].Phase) % Dur + Dur) % Dur;
            return new Point(Drifters[i].X + 12 * Math.Sin(age / 460 + i), Lerp(300, -50, age / Dur));
        }

        /// <summary>Where the tracker thinks you are looking: a wander, pulled onto a bubble while dwelling.</summary>
        private static Point Gaze(double t)
        {
            var free = Path(Wander, t);
            var onA = EaseInOut(Seg(t, 1700, 2450)) * (1 - EaseInOut(Seg(t, 3900, 4400)));
            var onB = EaseInOut(Seg(t, 4400, 4850)) * (1 - EaseInOut(Seg(t, 5250, 5650)));
            var a = Target(Math.Min(t, PopT));
            var b = Drifter(Second, t);
            double x = free.X, y = free.Y;
            x = Lerp(x, a.X, onA); y = Lerp(y, a.Y, onA);
            x = Lerp(x, b.X, onB); y = Lerp(y, b.Y, onB);
            // The tracker never sits perfectly still.
            return new Point(x + 1.4 * Wave(t, 66), y + 1.2 * Wave(t + 400, 49));
        }

        /// <summary>A sine with a whole number of periods per loop, so the wrap is seamless.</summary>
        private static double Wave(double t, int periods) => Math.Sin(2 * Math.PI * periods * t / Dur);

        /// <summary>Pop droplets around (x,y), k 0..1 over the burst (as in Bubble Pop).</summary>
        private static void Burst(LoopFrame f, double x, double y, double k)
        {
            if (k <= 0 || k >= 1) return;
            var dc = f.Front;
            using (f.Fade(dc, 1 - k))
                for (int m = 0; m < 6; m++)
                {
                    var a = m / 6.0 * Math.PI * 2;
                    var d = 26 * EaseOut(k);
                    dc.DrawEllipse(Droplet, null, new Point(x + Math.Cos(a) * d, y + Math.Sin(a) * d), 3, 3);
                }
        }

        public override void Draw(LoopFrame f, double t)
        {
            f.Desktop();
            var dc = f.Front;

            for (int i = 0; i < Drifters.Length; i++)
            {
                var p = Drifter(i, t);
                f.Bubble(p.X, p.Y, 1 + .04 * Wave(t + i * 700, 21));
            }

            var a = Target(t);
            if (t < PopT) f.Bubble(a.X, a.Y, 1 + .04 * Math.Sin(t / 300) + .08 * Seg(t, 3450, PopT));
            var at = Target(PopT);
            Burst(f, at.X, at.Y, Seg(t, PopT, PopT + 450));
            f.Floater(at.X - 10, at.Y - 26, Seg(t, PopT, PopT + 1300), "+XP");

            // Rings: the first fills to a pop, the second fills part way and empties when the look leaves.
            var ringA = Seg(t, 2650, PopT);
            if (t < PopT) using (f.Fade(dc, Seg(t, 2300, 2650))) f.DwellRing(a.X, a.Y, 27, ringA);
            var b = Drifter(Second, t);
            var ringB = .6 * Seg(t, 4800, 5250) * (1 - Seg(t, 5250, 5650));
            var ringBo = Seg(t, 4650, 4800) * (1 - Seg(t, 5650, 5800));
            if (ringBo > 0) using (f.Fade(dc, ringBo)) f.DwellRing(b.X, b.Y, 27, ringB);

            var g = Gaze(t);
            f.GazeDot(g.X, g.Y);

            // A small eye in the corner, looking where the dot is.
            var dx = Clamp((g.X - 240) / 200, -1, 1);
            var dy = Clamp((g.Y - 120) / 110, -1, 1);
            f.Card(new Rect(404, 10, 64, 34), f.P.Border);
            f.Eye(436, 27, 1 - Tri(Seg(t, 4300, 4520) * 2), dx, dy, .7);

            f.Chip(14, 12, "hold 1s", hot: t > 2650 && t < PopT + 300);
        }
    }
}
