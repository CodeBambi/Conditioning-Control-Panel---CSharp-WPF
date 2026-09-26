using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    /// <summary>Flash Images: pictures pop in, one is clicked away, Hydra brings two back.</summary>
    internal sealed class FlashImagesLoop : HelpLoopScene
    {
        private sealed record Photo(double X, double Y, double W, double H, double Appear, PhotoLook Look, double Hit = -1);

        private static readonly Photo[] Photos =
        {
            new(28, 22, 130, 92, 700, PhotoLook.Pink),
            new(250, 50, 112, 130, 1400, PhotoLook.Stripes, 3400),
            new(140, 128, 132, 88, 2100, PhotoLook.Sea),
            new(330, 14, 92, 66, 3700, PhotoLook.Pink),
            new(350, 160, 96, 66, 3950, PhotoLook.Sea),
        };

        private static readonly (double, double, double)[] CursorPath =
            { (2300, 430, 236), (3300, 306, 112), (4400, 306, 112), (5300, 440, 230) };


        public override string Id => "FlashImages";
        public override double DurationMs => 7000;
        public override double StillMs => 2900;

        public override IReadOnlyList<HelpLoopStep> Steps { get; } = new[]
        {
            new HelpLoopStep("help_loop_flashimages_1", 600, 2600),
            new HelpLoopStep("help_loop_flashimages_2", 2600, 3600),
            new HelpLoopStep("help_loop_flashimages_3", 3600, 6600),
        };

        public override void Draw(LoopFrame f, double t)
        {
            f.Desktop();
            var dc = f.Front;
            var fadeOut = Seg(t, 6000, 6500);

            foreach (var p in Photos)
            {
                var k = Seg(t, p.Appear, p.Appear + 280);
                var o = k * (1 - fadeOut);
                var sc = Lerp(.6, 1, Back(k));
                if (p.Hit >= 0)
                {
                    var h = Seg(t, p.Hit, p.Hit + 220);
                    o *= 1 - h;
                    sc *= 1 - .25 * h;
                }
                if (o <= 0.001) continue;
                using (f.At(dc, p.X + p.W / 2, p.Y + p.H / 2, sc, o))
                    f.Photo(dc, new Rect(p.X, p.Y, p.W, p.H), p.Look);
            }

            f.Chip(356, 96, "HYDRA x2", hot: true, opacity: Seg(t, 3700, 3900) * (1 - fadeOut));

            var c = Path(CursorPath, t);
            f.Ripple(306, 112, Seg(t, 3350, 3800));
            f.Cursor(c.X, c.Y, t > 3330 && t < 3480);
        }
    }
}
