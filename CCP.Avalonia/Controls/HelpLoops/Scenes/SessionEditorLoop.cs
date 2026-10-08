using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using static ConditioningControlPanel.Avalonia.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Avalonia.Controls.HelpLoops.Scenes
{
    /// <summary>Session Editor: an effect chip is dragged from the palette onto a lane, its right edge
    /// is pulled longer, then Play sweeps a playhead that lights each block and shows it in a preview
    /// (WPF Controls/HelpLoops/Scenes/SessionEditorLoop.cs, same timings). Deviation: the play glyph
    /// and spiral arms are built once and the spiral turns by transform instead of being rebuilt per
    /// frame (same picture, no per-frame geometry).</summary>
    internal sealed class SessionEditorLoop : HelpLoopScene
    {
        private static readonly Rect Win = new(20, 20, 440, 206);
        private const double LaneX0 = 64, LaneX1 = 440, Lane1Y = 118, Lane2Y = 154, LaneH = 26;
        private const double DropX = 206, EdgeFrom = 276, EdgeTo = 372;
        private const double PlayStart = 4900, PlayEnd = 7300;
        private static readonly Rect FlashBlock = new(80, Lane1Y, 116, LaneH);
        private static readonly Rect PlayBtn = new(36, 190, 64, 24);
        private static readonly Rect Preview = new(368, 40, 76, 50);
        private static readonly Point PreviewMid = new(Preview.X + Preview.Width / 2, Preview.Y + Preview.Height / 2);

        private static readonly string[] Palette = { "FLASH", "SPIRAL", "BUBBLES" };
        private static readonly IBrush SpiralBlock = LoopPalette.Solid("#6b4bb8");
        private static readonly IBrush FlashFill = LoopPalette.Solid("#b04f86");
        private static readonly IBrush PreviewFill = LoopPalette.Solid("#0f0b1c");

        private const double SpiralChipX = 94; // FLASH chip at 40 plus its width and the 8 px gap
        private static readonly (double, double, double)[] CursorPath =
        {
            (0.0, 300.0, 244.0), (800, SpiralChipX + 22, 56 + 11), (950, SpiralChipX + 22, 56 + 11),
            (1900, DropX + 20, Lane2Y + LaneH / 2), (2300, DropX + 20, Lane2Y + LaneH / 2),
            (2900, EdgeFrom, Lane2Y + LaneH / 2), (3050, EdgeFrom, Lane2Y + LaneH / 2), (4000, EdgeTo, Lane2Y + LaneH / 2),
            (4300, EdgeTo, Lane2Y + LaneH / 2), (4750, PlayBtn.X + 32, PlayBtn.Y + 12), (5000, PlayBtn.X + 32, PlayBtn.Y + 12),
            (5800, 300, 244),
        };

        private static readonly Geometry PlayGlyph = BuildPlayGlyph(new Point(PlayBtn.X + 14, PlayBtn.Y + 12));
        private static readonly Geometry[] SpiralArms = { BuildArm(PreviewMid, 17, 0), BuildArm(PreviewMid, 17, 1) };
        private Pen? _spiralPen;

        private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
        {
            new HelpLoopStep("help_loop_sessioneditor_1", 300, 2600),
            new HelpLoopStep("help_loop_sessioneditor_2", 2600, 4800),
            new HelpLoopStep("help_loop_sessioneditor_3", 4800, 7600),
        };

        public override string Id => "SessionEditor";
        public override double DurationMs => 8000;
        public override double StillMs => 5400;
        public override IReadOnlyList<HelpLoopStep> Steps => StepList;

        public override void Draw(LoopFrame f, double t)
        {
            var p = f.P;
            var dc = f.Front;
            f.Desktop(Win, drawLines: false);

            // --- palette strip ---
            f.DrawText(dc, "EFFECTS", 40, 42, 8, p.Dim, LoopFrame.Mono, FontWeight.Medium, TextAlignment.Left);
            double cx = 40;
            bool carrying = t > 950 && t < 1900;
            foreach (var name in Palette)
            {
                f.Chip(cx, 56, name, hot: name == "SPIRAL" && carrying, opacity: name == "SPIRAL" && carrying ? .45 : 1);
                cx += f.ChipWidth(name) + 8;
            }

            // --- timeline: ruler, lanes, blocks ---
            for (int i = 0; i <= 8; i++)
            {
                double x = LaneX0 + i * (LaneX1 - LaneX0) / 8;
                dc.DrawRectangle(p.LineA, null, new Rect(x, 100, 1, i % 2 == 0 ? 8 : 5));
            }
            for (int lane = 0; lane < 2; lane++)
            {
                double y = lane == 0 ? Lane1Y : Lane2Y;
                dc.DrawRoundedRectangle(p.Panel2, new Pen(p.Border, 1), new Rect(LaneX0, y, LaneX1 - LaneX0, LaneH), 6, 6);
                f.DrawText(dc, lane == 0 ? "1" : "2", 46, y + 6, 10, p.Dim, LoopFrame.Mono, FontWeight.Medium, TextAlignment.Left);
            }

            double playX = Lerp(LaneX0, LaneX1, Seg(t, PlayStart, PlayEnd));
            bool playing = t >= PlayStart && t < PlayEnd;
            double playO = Seg(t, PlayStart - 150, PlayStart) * (1 - Seg(t, PlayEnd, PlayEnd + 300));

            Block(f, FlashBlock, "FLASH", FlashFill, playing && playX >= FlashBlock.Left && playX <= FlashBlock.Right, 1, 1);

            // the dropped spiral block: pops in at the drop, its edge pulled longer, gone for the wrap
            double dropK = Seg(t, 1900, 2150);
            double gone = Seg(t, 7450, 7900);
            double right = Lerp(EdgeFrom, EdgeTo, EaseInOut(Seg(t, 3050, 4000)));
            var spiral = new Rect(DropX, Lane2Y, right - DropX, LaneH);
            if (dropK > 0 && gone < 1)
            {
                bool edgeHot = t > 2800 && t < 4200;
                Block(f, spiral, "SPIRAL", SpiralBlock, playing && playX >= spiral.Left && playX <= spiral.Right,
                    dropK * (1 - gone), Lerp(.7, 1, Back(dropK)));
                if (edgeHot)
                    using (f.Fade(dc, Seg(t, 2800, 2950) * (1 - Seg(t, 4050, 4200))))
                        dc.DrawRoundedRectangle(p.White, null, new Rect(spiral.Right - 4, spiral.Y + 5, 3, spiral.Height - 10), 1.5, 1.5);
            }

            // the chip under the cursor while it is carried
            var c = Path(CursorPath, t);
            if (carrying)
                f.Chip(c.X - 20, c.Y - 11, "SPIRAL", hot: true, opacity: .95);
            f.Ripple(DropX + 20, Lane2Y + LaneH / 2, Seg(t, 1900, 2300));

            // --- play button, playhead, preview ---
            bool playDown = t > 4930 && t < 5070;
            bool lit = t > 4950 && t < PlayEnd;
            dc.DrawRoundedRectangle(lit ? p.Accent : p.Panel2, new Pen(lit ? p.Accent : p.Border, 1),
                LoopFrame.Inset(new Rect(PlayBtn.X, PlayBtn.Y + (playDown ? 1 : 0), PlayBtn.Width, PlayBtn.Height), .5), 8, 8);
            dc.DrawGeometry(lit ? p.Ink : p.Text, null, PlayGlyph);
            f.DrawText(dc, "Play", PlayBtn.X + 24, PlayBtn.Y + 4, 11, lit ? p.Ink : p.Text, LoopFrame.Display, FontWeight.SemiBold, TextAlignment.Left);
            f.Ripple(PlayBtn.X + 32, PlayBtn.Y + 12, Seg(t, 4950, 5350));

            if (playO > 0)
                using (f.Fade(dc, playO))
                {
                    dc.DrawRectangle(p.Accent, null, new Rect(playX - 1, 96, 2, Lane2Y + LaneH - 92));
                    dc.DrawEllipse(p.Accent, null, new Point(playX, 96), 4, 4);
                }

            DrawPreview(f, t, playing, playX, spiral);

            f.Cursor(c.X, c.Y, carrying || (t > 3050 && t < 4000) || playDown || (t > 930 && t < 1000));
        }

        private static void Block(LoopFrame f, Rect r, string label, IBrush fill, bool hot, double opacity, double scale)
        {
            if (opacity <= 0) return;
            var p = f.P;
            var dc = f.Front;
            using (f.At(dc, r.X + r.Width / 2, r.Y + r.Height / 2, scale, opacity))
            {
                if (hot) dc.DrawRoundedRectangle(null, new Pen(p.Accent, 4), LoopFrame.Inset(r, -2), 7, 7);
                dc.DrawRoundedRectangle(fill, new Pen(hot ? p.White : p.WindowBorder, 1), LoopFrame.Inset(r, .5), 6, 6);
                f.DrawText(dc, label, r.X + 8, r.Y + 7, 9, p.Text, LoopFrame.Mono, FontWeight.SemiBold, TextAlignment.Left);
            }
        }

        private void DrawPreview(LoopFrame f, double t, bool playing, double playX, Rect spiral)
        {
            var p = f.P;
            var dc = f.Front;
            dc.DrawRoundedRectangle(PreviewFill, new Pen(p.Border, 1), LoopFrame.Inset(Preview, .5), 6, 6);
            f.DrawText(dc, "PREVIEW", Preview.X, Preview.Y - 12, 8, p.Dim, LoopFrame.Mono, FontWeight.Medium, TextAlignment.Left);
            if (!playing) return;

            using (f.ClipTo(dc, LoopFrame.Inset(Preview, 2), 4))
            {
                if (playX >= FlashBlock.Left && playX <= FlashBlock.Right)
                {
                    var r = new Rect(Preview.X + 14, Preview.Y + 6, 36, 28);
                    f.Photo(dc, r, PhotoLook.Pink, shadow: false);
                }
                if (playX >= spiral.Left && playX <= spiral.Right)
                {
                    // a two-arm Archimedean spiral turning t/400 radians
                    if (_spiralPen?.Brush != p.Lilac)
                        _spiralPen = new Pen(p.Lilac, 2.2) { LineCap = PenLineCap.Round };
                    using (f.At(dc, PreviewMid.X, PreviewMid.Y, rotateDeg: t / 400.0 * 180 / Math.PI))
                        foreach (var arm in SpiralArms)
                            dc.DrawGeometry(null, _spiralPen, arm);
                }
            }
        }

        private static Geometry BuildPlayGlyph(Point c)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X - 3, c.Y - 5), true);
                ctx.LineTo(new Point(c.X + 5, c.Y));
                ctx.LineTo(new Point(c.X - 3, c.Y + 5));
                ctx.EndFigure(true);
            }
            return g;
        }

        /// <summary>One arm of the spiral at turn 0 (WPF Spiral, arm index <paramref name="arm"/>).</summary>
        private static Geometry BuildArm(Point c, double radius, int arm)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                const int n = 48;
                for (int i = 0; i <= n; i++)
                {
                    double k = i / (double)n;
                    double a = arm * Math.PI + k * Math.PI * 3.2;
                    var pt = new Point(c.X + Math.Cos(a) * radius * k, c.Y + Math.Sin(a) * radius * k);
                    if (i == 0) ctx.BeginFigure(pt, false);
                    else ctx.LineTo(pt);
                }
                ctx.EndFigure(false);
            }
            return g;
        }
    }
}
