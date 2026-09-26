using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    /// <summary>Bouncing Text: a phrase drifts DVD-style, every edge pays +15 XP, capped a minute.</summary>
    internal sealed class BouncingTextLoop : HelpLoopScene
    {
        private const double BoxW = 104, BoxH = 34;
        private static readonly string[] Words = { "GOOD", "DEEPER", "LET GO", "RELAX", "SINK", "OBEY" };

        // Per-loop state: cleared by Reset (and by t going backwards, as the mockup does).
        private double _prevT = -1;
        private int _hits;
        private readonly List<(double T, double X, double Y)> _recent = new();

        public override string Id => "BouncingText";
        public override double DurationMs => 8000;
        public override double StillMs => 1600;

        public override IReadOnlyList<HelpLoopStep> Steps { get; } = new[]
        {
            new HelpLoopStep("help_loop_bouncingtext_1", 0, 2700),
            new HelpLoopStep("help_loop_bouncingtext_2", 2700, 5400),
            new HelpLoopStep("help_loop_bouncingtext_3", 5400, 8000),
        };

        public override void Reset()
        {
            _prevT = -1;
            _hits = 0;
            _recent.Clear();
        }

        public override void Draw(LoopFrame f, double t)
        {
            if (t < _prevT) Reset();
            f.Desktop();
            var dc = f.Front;

            double px = t / 2000, py = t / 1333.333 + .4;
            double x = Tri(px) * (LoopFrame.StageWidth - BoxW);
            double y = Tri(py) * (LoopFrame.StageHeight - 20 - BoxH);

            if (_prevT >= 0 && t > _prevT)
            {
                bool edgeX = Math.Floor(_prevT / 2000) != Math.Floor(px);
                bool edgeY = Math.Floor(_prevT / 1333.333 + .4) != Math.Floor(py);
                if (edgeX || edgeY)
                {
                    _hits++;
                    _recent.Add((t, x + 20, y));
                }
            }
            _prevT = t;
            _recent.RemoveAll(h => t - h.T >= 900);

            var colours = new[] { f.P.Accent, f.P.Mint, f.P.Lilac, f.P.Gold };
            var word = Words[_hits % Words.Length];
            var ft = f.Format(word, 16, f.P.Ink, LoopFrame.Display, FontWeights.SemiBold);
            var box = new Rect(x, y, ft.Width + 28, BoxH);
            dc.DrawRoundedRectangle(colours[_hits % colours.Length], null, box, 8, 8);
            dc.DrawText(ft, new Point(x + 14, y + (BoxH - ft.Height) / 2));

            for (int i = 0; i < Math.Min(4, _recent.Count); i++)
            {
                var h = _recent[i];
                f.Floater(h.X, h.Y, (t - h.T) / 900, "+15 XP");
            }

            f.Chip(14, 12, "XP " + (_hits * 15).ToString(CultureInfo.InvariantCulture), hot: t > 5400);
        }
    }
}
