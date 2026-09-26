using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops.Scenes;

/// <summary>
/// Session Editor: an effect chip is dragged from the palette onto a lane, its right edge is
/// pulled longer, then Play sweeps a playhead that lights each block and shows it in a preview.
/// </summary>
internal sealed class SessionEditorLoop : HelpLoopScene
{
    private static readonly Rect Win = new(20, 20, 440, 206);
    private const double LaneX0 = 64, LaneX1 = 440, Lane1Y = 118, Lane2Y = 154, LaneH = 26;
    private const double DropX = 206, EdgeFrom = 276, EdgeTo = 372;
    private const double PlayStart = 4900, PlayEnd = 7300;
    private static readonly Rect FlashBlock = new(80, Lane1Y, 116, LaneH);
    private static readonly Rect PlayBtn = new(36, 190, 64, 24);
    private static readonly Rect Preview = new(368, 40, 76, 50);

    private static readonly string[] Palette = { "FLASH", "SPIRAL", "BUBBLES" };
    private static readonly Brush SpiralBlock = LoopPalette.Solid("#6b4bb8");
    private static readonly Brush FlashFill = LoopPalette.Solid("#b04f86");
    private static readonly Brush PreviewFill = LoopPalette.Solid("#0f0b1c");

    private static readonly (double, double, double)[] CursorPath;
    private static readonly double SpiralChipX;

    static SessionEditorLoop()
    {
        SpiralChipX = 94; // FLASH chip at 40 plus its width and the 8 px gap
        double chipY = 56 + 11;
        double laneMid = Lane2Y + LaneH / 2;
        CursorPath = new[]
        {
            (0.0, 300.0, 244.0), (800, SpiralChipX + 22, chipY), (950, SpiralChipX + 22, chipY),
            (1900, DropX + 20, laneMid), (2300, DropX + 20, laneMid),
            (2900, EdgeFrom, laneMid), (3050, EdgeFrom, laneMid), (4000, EdgeTo, laneMid),
            (4300, EdgeTo, laneMid), (4750, PlayBtn.X + 32, PlayBtn.Y + 12), (5000, PlayBtn.X + 32, PlayBtn.Y + 12),
            (5800, 300, 244),
        };
    }

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
        f.DrawText(dc, "EFFECTS", 40, 42, 8, p.Dim, LoopFrame.Mono, FontWeights.Medium, TextAlignment.Left);
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
            f.DrawText(dc, (lane + 1).ToString(), 46, y + 6, 10, p.Dim, LoopFrame.Mono, FontWeights.Medium, TextAlignment.Left);
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
        PlayGlyph(dc, new Point(PlayBtn.X + 14, PlayBtn.Y + 12), lit ? p.Ink : p.Text);
        f.DrawText(dc, "Play", PlayBtn.X + 24, PlayBtn.Y + 4, 11, lit ? p.Ink : p.Text, LoopFrame.Display, FontWeights.SemiBold, TextAlignment.Left);
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

    private static void Block(LoopFrame f, Rect r, string label, Brush fill, bool hot, double opacity, double scale)
    {
        if (opacity <= 0) return;
        var p = f.P;
        var dc = f.Front;
        using (f.At(dc, r.X + r.Width / 2, r.Y + r.Height / 2, scale, opacity))
        {
            if (hot) dc.DrawRoundedRectangle(null, new Pen(p.Accent, 4) { }, LoopFrame.Inset(r, -2), 7, 7);
            dc.DrawRoundedRectangle(fill, new Pen(hot ? p.White : p.WindowBorder, 1), LoopFrame.Inset(r, .5), 6, 6);
            f.DrawText(dc, label, r.X + 8, r.Y + 7, 9, p.Text, LoopFrame.Mono, FontWeights.SemiBold, TextAlignment.Left);
        }
    }

    private static void DrawPreview(LoopFrame f, double t, bool playing, double playX, Rect spiral)
    {
        var p = f.P;
        var dc = f.Front;
        dc.DrawRoundedRectangle(PreviewFill, new Pen(p.Border, 1), LoopFrame.Inset(Preview, .5), 6, 6);
        f.DrawText(dc, "PREVIEW", Preview.X, Preview.Y - 12, 8, p.Dim, LoopFrame.Mono, FontWeights.Medium, TextAlignment.Left);
        if (!playing) return;

        using (f.ClipTo(dc, new RectangleGeometry(LoopFrame.Inset(Preview, 2), 4, 4)))
        {
            if (playX >= FlashBlock.Left && playX <= FlashBlock.Right)
            {
                var r = new Rect(Preview.X + 14, Preview.Y + 6, 36, 28);
                f.Photo(dc, r, PhotoLook.Pink, shadow: false);
            }
            if (playX >= spiral.Left && playX <= spiral.Right)
                Spiral(dc, new Point(Preview.X + Preview.Width / 2, Preview.Y + Preview.Height / 2), 17, t / 400.0, p.Lilac);
        }
    }

    private static void PlayGlyph(DrawingContext dc, Point c, Brush b)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X - 3, c.Y - 5), true, true);
            ctx.LineTo(new Point(c.X + 5, c.Y), false, false);
            ctx.LineTo(new Point(c.X - 3, c.Y + 5), false, false);
        }
        g.Freeze();
        dc.DrawGeometry(b, null, g);
    }

    /// <summary>A two-arm Archimedean spiral turning with <paramref name="turn"/> radians.</summary>
    private static void Spiral(DrawingContext dc, Point c, double radius, double turn, Brush b)
    {
        var pen = new Pen(b, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (int arm = 0; arm < 2; arm++)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                const int n = 48;
                for (int i = 0; i <= n; i++)
                {
                    double k = i / (double)n;
                    double a = turn + arm * Math.PI + k * Math.PI * 3.2;
                    var pt = new Point(c.X + Math.Cos(a) * radius * k, c.Y + Math.Sin(a) * radius * k);
                    if (i == 0) ctx.BeginFigure(pt, false, false);
                    else ctx.LineTo(pt, true, false);
                }
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
    }
}
