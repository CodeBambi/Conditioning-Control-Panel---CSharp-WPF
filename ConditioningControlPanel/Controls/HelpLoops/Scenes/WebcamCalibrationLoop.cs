using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    /// <summary>
    /// Webcam Calibration: a pink dot walks a 4x4 grid on a dark screen, an eye at the bottom
    /// follows each hop, the counter climbs and the grid goes mint at 16 of 16. Hands off: no cursor.
    /// </summary>
    internal sealed class WebcamCalibrationLoop : HelpLoopScene
    {
        private const double Dur = 7200, FirstAt = 300, HopStart = 2400, HopMs = 340, Glide = 140, DoneAt = 5200;
        private const int Visible = 9; // hops actually walked before the counter jumps to 16
        private static readonly double[] Cols = { 60, 180, 300, 420 };
        private static readonly double[] Rows = { 46, 94, 142, 190 };
        private static readonly Point EyeAt = new(240, 238);

        private static readonly Brush Veil = LoopPalette.Solid("#07050dcc");
        private static readonly Brush Faint = LoopPalette.Solid("#3b306088");

        public override string Id => "WebcamCalibration";
        public override double DurationMs => Dur;
        public override double StillMs => 1700;

        public override IReadOnlyList<HelpLoopStep> Steps { get; } = new[]
        {
            new HelpLoopStep("help_loop_webcamcalibration_1", 0, 2400),
            new HelpLoopStep("help_loop_webcamcalibration_2", 2400, 5200),
            new HelpLoopStep("help_loop_webcamcalibration_3", 5200, 7000),
        };

        /// <summary>Grid point n (0..15) in the order the dot walks it: a snake, row by row.</summary>
        private static Point Grid(int n)
        {
            int r = n / 4, c = n % 4;
            if (r % 2 == 1) c = 3 - c;
            return new Point(Cols[c], Rows[r]);
        }

        /// <summary>When the dot lands on hop n (0-based).</summary>
        private static double LandAt(int n) => n == 0 ? FirstAt : HopStart + (n - 1) * HopMs;

        /// <summary>The hop the dot is on at t (-1 before the first), and where it is.</summary>
        private static (int Hop, Point At) Dot(double t)
        {
            int hop = -1;
            for (int n = 0; n < Visible; n++) if (t >= LandAt(n) - Glide) hop = n;
            if (hop <= 0) return (hop, Grid(0));
            var k = EaseInOut(Seg(t, LandAt(hop) - Glide, LandAt(hop)));
            var a = Grid(hop - 1);
            var b = Grid(hop);
            return (hop, new Point(Lerp(a.X, b.X, k), Lerp(a.Y, b.Y, k)));
        }

        /// <summary>Points read so far: the one the dot left counts, the rest land at once when done.</summary>
        private static int Count(double t, int hop)
        {
            if (t >= DoneAt) return 16;
            if (hop == 0) return t >= 2250 ? 1 : 0;
            return Math.Max(0, hop);
        }

        public override void Draw(LoopFrame f, double t)
        {
            var dc = f.Front;
            f.Desktop();
            dc.DrawRectangle(Veil, null, new Rect(0, 0, LoopFrame.StageWidth, LoopFrame.StageHeight));

            var fadeOut = Seg(t, 6600, 7000);
            var done = Seg(t, DoneAt, DoneAt + 320);
            var (hop, at) = Dot(t);

            using (f.Fade(dc, 1 - fadeOut))
            {
                // The grid: faint rings, then a mint tick for every point already read.
                int read = Count(t, hop);
                for (int n = 0; n < 16; n++)
                {
                    var p = Grid(n);
                    dc.DrawEllipse(null, new Pen(Faint, 1), p, 5, 5);
                    if (n < read)
                    {
                        var pop = n < Visible - 1 ? 1 : Back(Seg(t, DoneAt + n * 25, DoneAt + n * 25 + 260));
                        using (f.At(dc, p.X, p.Y, pop))
                            dc.DrawEllipse(f.P.Mint, null, p, 3, 3);
                    }
                    if (done > 0)
                        using (f.Fade(dc, (1 - Seg(t, DoneAt + 320, DoneAt + 1100)) * .5))
                            dc.DrawEllipse(null, new Pen(f.P.Mint, 2), p, 5 + 8 * EaseOut(done), 5 + 8 * EaseOut(done));
                }

                // The dot: grows in, breathes while it is being read, shrinks as the hop leaves.
                if (hop >= 0 && t < DoneAt)
                {
                    var land = LandAt(hop);
                    var grow = hop == 0 ? Back(Seg(t, FirstAt, FirstAt + 380)) : 1;
                    var hold = hop == 0 ? 2100 : HopMs - Glide;
                    var read01 = Seg(t, land, land + hold);
                    var size = grow * (1 - .35 * read01);
                    if (size > .01) using (f.At(dc, at.X, at.Y, 1.4 * size)) f.GazeDot(at.X, at.Y);
                    if (hop == 0 && t > 800) f.DwellRing(at.X, at.Y, 16, Seg(t, 800, 2250), f.P.Accent, 2.5);
                }

                // The eye follows the dot, a beat late.
                var look = Dot(Math.Max(0, t - 90)).At;
                if (t >= DoneAt) look = new Point(240, 118);
                var dx = Clamp((look.X - 240) / 180, -1, 1);
                var dy = Clamp((look.Y - 118) / 72, -1, 1);
                var blink = Tri(Seg(t, 5900, 6140) * 2);
                f.Eye(EyeAt.X, EyeAt.Y, 1 - blink, dx, dy, 1.1);

                int count = Count(t, hop);
                var label = count >= 16 ? "16 / 16  done" : count.ToString(CultureInfo.InvariantCulture) + " / 16";
                f.Chip(14, 12, label, hot: count >= 16, opacity: Seg(t, 0, 300));
            }
        }
    }
}
