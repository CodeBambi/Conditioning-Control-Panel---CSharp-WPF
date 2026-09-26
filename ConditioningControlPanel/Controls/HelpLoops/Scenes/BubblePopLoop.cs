using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    /// <summary>Bubble Pop: bubbles float up, a click pops one for XP, the hundredth pays a Sparkle.</summary>
    internal sealed class BubblePopLoop : HelpLoopScene
    {
        private const double Life = 5500, Dur = 7700;
        private static readonly double[] Xs = { 70, 160, 250, 330, 410, 120, 300 };
        private static readonly (double T, int Bubble)[] Pops = { (1900, 1), (3700, 3), (5500, 4) };
        private static readonly Point[] PopAt = new Point[Pops.Length];
        private static readonly (double, double, double)[] CursorPath;

        private static readonly Brush Droplet = LoopPalette.Solid("#ffc2e2");

        static BubblePopLoop()
        {
            for (int j = 0; j < Pops.Length; j++)
            {
                var (_, x, y) = Pos(Pops[j].Bubble, Pops[j].T);
                PopAt[j] = new Point(x, y);
            }
            CursorPath = new[]
            {
                (1100.0, 440.0, 236.0),
                (1900, PopAt[0].X, PopAt[0].Y), (2700, PopAt[0].X, PopAt[0].Y),
                (3700, PopAt[1].X, PopAt[1].Y), (4700, PopAt[1].X, PopAt[1].Y),
                (5500, PopAt[2].X, PopAt[2].Y), (6300, PopAt[2].X, PopAt[2].Y),
                (7300, 440, 236),
            };
        }

        public override string Id => "BubblePop";
        public override double DurationMs => Dur;
        public override double StillMs => 3700;

        public override IReadOnlyList<HelpLoopStep> Steps { get; } = new[]
        {
            new HelpLoopStep("help_loop_bubblepop_1", 0, 1800),
            new HelpLoopStep("help_loop_bubblepop_2", 1800, 4400),
            new HelpLoopStep("help_loop_bubblepop_3", 4400, 7300),
        };

        private static (double Age, double X, double Y) Pos(int i, double t)
        {
            var age = ((t - (i * 1100 - 1500)) % Dur + Dur) % Dur;
            return (age, Xs[i] + 14 * Math.Sin(age / 420 + i), Lerp(290, -50, age / Life));
        }

        public override void Draw(LoopFrame f, double t)
        {
            f.Desktop();
            var dc = f.Front;

            for (int i = 0; i < Xs.Length; i++)
            {
                var (age, x, y) = Pos(i, t);
                double o = age < Life ? 1 : 0;
                foreach (var (pt, pi) in Pops)
                    if (pi == i && t >= pt && t < pt + 2400) o = 0;
                f.Bubble(x, y, 1 + .04 * Math.Sin(age / 300), o);
            }

            for (int j = 0; j < Pops.Length; j++)
            {
                var pt = Pops[j].T;
                var k = Seg(t, pt, pt + 450);
                if (k > 0 && k < 1)
                {
                    using (f.Fade(dc, 1 - k))
                        for (int m = 0; m < 6; m++)
                        {
                            var a = m / 6.0 * Math.PI * 2;
                            var d = 26 * EaseOut(k);
                            dc.DrawEllipse(Droplet, null, new Point(PopAt[j].X + Math.Cos(a) * d, PopAt[j].Y + Math.Sin(a) * d), 3, 3);
                        }
                }
                f.Floater(PopAt[j].X - 10, PopAt[j].Y - 26, Seg(t, pt, pt + 1300),
                    j == 2 ? "+1 Sparkle" : "+XP", j == 2 ? f.P.Gold : null);
            }

            int popped = 0;
            foreach (var p in Pops) if (t >= p.T) popped++;
            int c = 97 + popped;
            f.Chip(14, 12, c >= 100 ? "100 / 100  +1 Sparkle" : c.ToString(CultureInfo.InvariantCulture) + " / 100", hot: c >= 100);

            int lastPop = -1;
            for (int j = 0; j < Pops.Length; j++) if (t >= Pops[j].T) lastPop = j;
            if (lastPop >= 0) f.Ripple(PopAt[lastPop].X, PopAt[lastPop].Y, Seg(t, Pops[lastPop].T, Pops[lastPop].T + 400));

            bool down = false;
            foreach (var p in Pops) if (t > p.T - 60 && t < p.T + 90) down = true;
            var cur = Path(CursorPath, t);
            f.Cursor(cur.X, cur.Y, down);
        }
    }
}
