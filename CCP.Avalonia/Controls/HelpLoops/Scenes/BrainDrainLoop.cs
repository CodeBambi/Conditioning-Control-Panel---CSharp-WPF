using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ConditioningControlPanel.Avalonia.Controls.HelpLoops.Scenes
{
    /// <summary>Brain Drain: the desktop blurs, melting mode warps it and drips run down from the top,
    /// and a drop icon pulses each time a clip plays. The melt is a slow skew of the whole desktop plus
    /// a sideways sine wobble of each text line (WPF Controls/HelpLoops/Scenes/BrainDrainLoop.cs,
    /// same timings).</summary>
    internal sealed class BrainDrainLoop : HelpLoopScene
    {
        private static readonly (double T, double V)[] Blur = { (500, 0), (2200, .6), (6600, .6), (7300, 0) };
        private static readonly double[] Clips = { 1500, 3900, 6000 };
        private static readonly double[] DripX = { 70, 140, 215, 300, 365 };

        // WPF LinearGradientBrush(a, b, 90): top to bottom of each drip's bounds.
        private static readonly IBrush DripBrush = new ImmutableLinearGradientBrush(new[]
            {
                new ImmutableGradientStop(0, LoopPalette.Css("#ff6fb5cc")),
                new ImmutableGradientStop(1, LoopPalette.Css("#b99cffcc")),
            }, 1, null, null, GradientSpreadMethod.Pad,
            new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative));

        public override string Id => "BrainDrain";
        public override double DurationMs => 7600;
        public override double StillMs => 4300;

        public override IReadOnlyList<HelpLoopStep> Steps { get; } = new[]
        {
            new HelpLoopStep("help_loop_braindrain_1", 500, 2800),
            new HelpLoopStep("help_loop_braindrain_2", 2800, 5600),
            new HelpLoopStep("help_loop_braindrain_3", 5600, 7400),
        };

        public override void Draw(LoopFrame f, double t)
        {
            double b = LoopMath.Schedule(Blur, t);
            double m = LoopMath.Seg(t, 2800, 3600) * (1 - LoopMath.Seg(t, 6400, 7200));

            f.BackBlur = b * 7;

            // Desktop, warped while melting: WPF SkewTransform about (240,135), then ScaleTransform about (240,0).
            var back = f.Back;
            DrawingContext.PushedState? warp = null;
            if (m > 0.01)
            {
                double ax = m * 3 * Math.Sin(t / 700) * Math.PI / 180, ay = m * 1.5 * Math.Sin(t / 900 + 1) * Math.PI / 180;
                var skew = Matrix.CreateTranslation(-240, -135) * Matrix.CreateSkew(ax, ay) * Matrix.CreateTranslation(240, 135);
                var scale = Matrix.CreateTranslation(-240, 0) * Matrix.CreateScale(1, 1 + m * .03 * (1 + Math.Sin(t / 520))) * Matrix.CreateTranslation(240, 0);
                warp = back.PushTransform(skew * scale);
            }
            var dk = f.Desktop(null, drawLines: false);
            for (int i = 0; i < dk.Lines.Count; i++)
            {
                var l = dk.Lines[i];
                double dx = m * 7 * Math.Sin(t / 260 + i * 1.3);
                double dy = m * 2 * Math.Sin(t / 330 + i);
                f.DesktopLine(back, new Rect(l.X + dx, l.Y + dy, l.Width, l.Height), i);
            }
            warp?.Dispose();

            // Drips hang from the top edge, over the blur.
            var dc = f.Front;
            if (m > 0)
            {
                using (f.Fade(dc, m))
                {
                    for (int i = 0; i < DripX.Length; i++)
                    {
                        double w = 9 + i % 3 * 4;
                        double h = m * (40 + i * 23 + 10 * Math.Sin(t / 600 + i));
                        if (h > 0) dc.DrawGeometry(DripBrush, null, DripShape(DripX[i], w, h));
                    }
                }
            }

            f.Slider(14, 12, "BLUR", b);

            double c = -1e9;
            foreach (var x in Clips) if (t >= x) c = x;
            double wk = LoopMath.Seg(t, c, c + 700);
            bool pulsing = wk > 0 && wk < 1;
            f.Speaker(420, 16, pulsing ? wk : 0, true, 2, 1 + .25 * (pulsing ? Math.Sin(wk * Math.PI) : 0));
        }

        /// <summary>A drip: flat top at y 0, straight sides, rounded bottom (radius up to 8, like the mockup).</summary>
        private static Geometry DripShape(double x, double w, double h)
        {
            double r = Math.Min(Math.Min(8, w / 2), h);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x, 0), true);
                ctx.LineTo(new Point(x + w, 0));
                ctx.LineTo(new Point(x + w, h - r));
                ctx.ArcTo(new Point(x + w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise);
                ctx.LineTo(new Point(x + r, h));
                ctx.ArcTo(new Point(x, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
                ctx.EndFigure(true);
            }
            return g;
        }
    }
}
