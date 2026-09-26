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
        private enum Look { Pink, Stripes, Sea }

        private sealed record Photo(double X, double Y, double W, double H, double Appear, Look Look, double Hit = -1);

        private static readonly Photo[] Photos =
        {
            new(28, 22, 130, 92, 700, Look.Pink),
            new(250, 50, 112, 130, 1400, Look.Stripes, 3400),
            new(140, 128, 132, 88, 2100, Look.Sea),
            new(330, 14, 92, 66, 3700, Look.Pink),
            new(350, 160, 96, 66, 3950, Look.Sea),
        };

        private static readonly (double, double, double)[] CursorPath =
            { (2300, 430, 236), (3300, 306, 112), (4400, 306, 112), (5300, 440, 230) };

        private static readonly Brush PinkFill = LoopPalette.Freeze(new LinearGradientBrush(
            LoopPalette.Css("#ff8fc8"), LoopPalette.Css("#9b5cff"), new Point(0, 0), new Point(1, 1)));
        private static readonly Brush PinkShine = LoopPalette.Freeze(new RadialGradientBrush
        {
            Center = new Point(.4, .35),
            GradientOrigin = new Point(.4, .35),
            RadiusX = .85,
            RadiusY = .85,
            GradientStops =
            {
                new GradientStop(LoopPalette.Css("#ffffff99"), 0),
                new GradientStop(LoopPalette.Css("#ffffff00"), .6),
            }
        });
        private static readonly Brush SeaFill = LoopPalette.Freeze(new LinearGradientBrush(
            LoopPalette.Css("#5fffd0"), LoopPalette.Css("#2b6bff"), new Point(.33, .03), new Point(.67, .97)));
        private static readonly Brush SeaShade = LoopPalette.Freeze(new LinearGradientBrush(
            LoopPalette.Css("#00000000"), LoopPalette.Css("#00000066"), new Point(0, 0), new Point(0, 1)));
        private static readonly Brush StripeA = LoopPalette.Solid("#ff6fb5");
        private static readonly Brush StripeB = LoopPalette.Solid("#2b124a");
        private static readonly Brush Shadow = LoopPalette.Solid("#00000055");

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
                    DrawPhoto(f, dc, new Rect(p.X, p.Y, p.W, p.H), p.Look);
            }

            f.Chip(356, 96, "HYDRA x2", hot: true, opacity: Seg(t, 3700, 3900) * (1 - fadeOut));

            var c = Path(CursorPath, t);
            f.Ripple(306, 112, Seg(t, 3350, 3800));
            f.Cursor(c.X, c.Y, t > 3330 && t < 3480);
        }

        /// <summary>A photo: white 3px frame, rounded 4, a drop shadow faked as darker stacked rects.</summary>
        private static void DrawPhoto(LoopFrame f, DrawingContext dc, Rect r, Look look)
        {
            LoopFrame.SoftShadow(dc, r, 4, 10, 22, 0xAA);
            dc.DrawRoundedRectangle(Brushes.White, null, r, 4, 4);
            var inner = LoopFrame.Inset(r, 3);
            using (f.ClipTo(dc, new RectangleGeometry(inner, 1.5, 1.5)))
            {
                switch (look)
                {
                    case Look.Pink:
                        dc.DrawRectangle(PinkFill, null, inner);
                        dc.DrawEllipse(PinkShine, null,
                            new Point(inner.X + inner.Width * .5, inner.Y + inner.Height * .5),
                            inner.Width * .2, inner.Height * .22);
                        break;
                    case Look.Stripes:
                        dc.DrawRectangle(StripeB, null, inner);
                        DrawWedges(dc, inner);
                        break;
                    default:
                        dc.DrawRectangle(SeaFill, null, inner);
                        dc.DrawRectangle(SeaShade, null, new Rect(inner.X, inner.Bottom - inner.Height * .4, inner.Width, inner.Height * .4));
                        break;
                }
            }
        }

        /// <summary>repeating-conic-gradient: 18 wedges of 20 degrees, pink every other one, from 12 o'clock.</summary>
        private static void DrawWedges(DrawingContext dc, Rect r)
        {
            var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            double rad = Math.Sqrt(r.Width * r.Width + r.Height * r.Height);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                for (int i = 0; i < 18; i += 2)
                {
                    double a0 = (i * 20 - 90) * Math.PI / 180, a1 = ((i + 1) * 20 - 90) * Math.PI / 180;
                    ctx.BeginFigure(c, true, true);
                    ctx.LineTo(new Point(c.X + Math.Cos(a0) * rad, c.Y + Math.Sin(a0) * rad), false, false);
                    ctx.LineTo(new Point(c.X + Math.Cos(a1) * rad, c.Y + Math.Sin(a1) * rad), false, false);
                }
            }
            g.Freeze();
            dc.DrawGeometry(StripeA, null, g);
        }
    }
}
