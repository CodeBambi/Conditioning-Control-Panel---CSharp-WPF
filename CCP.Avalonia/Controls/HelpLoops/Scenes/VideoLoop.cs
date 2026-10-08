using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using static ConditioningControlPanel.Avalonia.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Avalonia.Controls.HelpLoops.Scenes
{
    /// <summary>Mandatory Video: the video takes the screen, a target shows up, click it in time
    /// (WPF Controls/HelpLoops/Scenes/VideoLoop.cs, same timings).</summary>
    internal sealed class VideoLoop : HelpLoopScene
    {
        private const double TargetX = 330, TargetY = 96;

        private static readonly (double, double, double)[] CursorPath =
            { (2900, 120, 220), (4300, TargetX + 4, TargetY + 6), (5400, TargetX + 4, TargetY + 6), (6300, 130, 230) };

        private static readonly ImmutableGradientStop[] Stops =
        {
            new(0, LoopPalette.Css("#3a1450")),
            new(1 / 3.0, LoopPalette.Css("#ff6fb5")),
            new(2 / 3.0, LoopPalette.Css("#6b3cff")),
            new(1, LoopPalette.Css("#3a1450")),
        };
        private static readonly Vector Dir = new(Math.Sin(120 * Math.PI / 180), -Math.Cos(120 * Math.PI / 180));
        private static readonly IBrush BarTrack = LoopPalette.Solid("#ffffff33");
        private static readonly IBrush RingRest = LoopPalette.Solid("#ffffff26");

        public override string Id => "Video";
        public override double DurationMs => 8000;
        public override double StillMs => 3800;

        public override IReadOnlyList<HelpLoopStep> Steps { get; } = new[]
        {
            new HelpLoopStep("help_loop_video_1", 400, 2600),
            new HelpLoopStep("help_loop_video_2", 2600, 4400),
            new HelpLoopStep("help_loop_video_3", 4400, 7000),
        };

        public override void Draw(LoopFrame f, double t)
        {
            f.Desktop();
            var dc = f.Front;
            const double W = LoopFrame.StageWidth, H = LoopFrame.StageHeight;

            var k = EaseOut(Seg(t, 400, 1200)) * (1 - EaseInOut(Seg(t, 7000, 7600)));
            var r = new Rect(Lerp(200, 0, k), Lerp(110, 0, k), Lerp(80, W, k), Lerp(45, H, k));
            var vo = Seg(k, 0, .15);
            if (vo > 0)
            {
                using (f.Fade(dc, vo))
                {
                    // background-size 300%, sliding: a repeating gradient three boxes wide.
                    var span = r.Width * 3;
                    var shift = -2 * r.Width * ((t / 40) % 300) / 100.0;
                    var start = new Point(r.X + shift, r.Y);
                    var brush = new ImmutableLinearGradientBrush(Stops, 1, null, null, GradientSpreadMethod.Repeat,
                        new RelativePoint(start, RelativeUnit.Absolute), new RelativePoint(start + Dir * span, RelativeUnit.Absolute));
                    dc.DrawRoundedRectangle(brush, null, r, 6, 6);
                    if (r.Width > 24 && r.Height > 16)
                    {
                        var track = new Rect(r.X + 10, r.Bottom - 14, r.Width - 20, 4);
                        dc.DrawRoundedRectangle(BarTrack, null, track, 2, 2);
                        var fill = track.Width * Seg(t, 1200, 7000);
                        if (fill > 0) dc.DrawRoundedRectangle(f.P.White, null, new Rect(track.X, track.Y, fill, 4), 2, 2);
                    }
                }
            }

            f.Chip(14, 12, "STRICT: NO SKIP", opacity: k > .95 ? 1 : 0);

            var ta = Seg(t, 2700, 3000);
            var gone = Seg(t, 4550, 4800);
            var o = ta * (1 - gone);
            if (o > 0)
            {
                var left = 1 - Seg(t, 2700, 5700);
                var sc = Lerp(.4, 1, Back(ta)) * (1 + .3 * gone);
                using (f.At(dc, TargetX, TargetY, sc, o))
                {
                    var c = new Point(TargetX, TargetY);
                    dc.DrawEllipse(RingRest, null, c, 33, 33);
                    LoopFrame.Sweep(dc, c, 33, left * 360, f.P.Mint);
                    dc.DrawEllipse(f.P.Panel, null, c, 27, 27);
                    f.DrawText(dc, "WATCHING", TargetX, TargetY - 7, 10, f.P.Text, LoopFrame.Display, FontWeight.SemiBold, TextAlignment.Center);
                }
            }

            f.Floater(TargetX - 10, TargetY - 30, Seg(t, 4550, 5600), "+XP");

            var p = Path(CursorPath, t);
            f.Ripple(TargetX, TargetY, Seg(t, 4470, 4900));
            f.Cursor(p.X, p.Y, t > 4450 && t < 4600);
        }
    }
}
