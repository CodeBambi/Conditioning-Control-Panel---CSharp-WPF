using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Controls.HelpLoops.Scenes;

/// <summary>
/// Lock Card: the screen dims, a card drops in, the phrase is typed three times
/// (one typo on the second round), the dots fill and the card lifts away.
/// </summary>
internal sealed class LockCardLoop : HelpLoopScene
{
    private const string Phrase = "I let go and relax";
    private const double CharMs = 52;
    private static readonly double[] RoundStarts = { 1400, 2700, 4200 };

    private static readonly Brush DimBrush = Frozen(Color.FromArgb(0xCC, 0x07, 0x04, 0x0F));
    private static readonly Brush InputFill = Frozen(Color.FromRgb(0x0F, 0x0B, 0x1C));
    private static readonly Brush LineBrush = Frozen(Color.FromRgb(0x34, 0x2A, 0x55));
    private static readonly Typeface Mono = new(new FontFamily("Consolas, Courier New"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
    {
        new HelpLoopStep("help_loop_lockcard_1", 400, 1300),
        new HelpLoopStep("help_loop_lockcard_2", 1300, 4300),
        new HelpLoopStep("help_loop_lockcard_3", 4300, 6600),
    };

    public override string Id => "LockCard";
    public override double DurationMs => 7600;
    public override double StillMs => 2300;
    public override IReadOnlyList<HelpLoopStep> Steps => StepList;

    public override void Draw(LoopFrame f, double t)
    {
        f.Desktop();
        var dc = f.Front;
        var p = f.P;

        double inK = LoopMath.EaseOut(LoopMath.Seg(t, 500, 950));
        double outK = LoopMath.EaseInOut(LoopMath.Seg(t, 5900, 6400));
        double dimO = inK * (1 - outK);

        if (dimO > 0)
        {
            dc.PushOpacity(dimO);
            dc.DrawRectangle(DimBrush, null, new Rect(0, 0, 480, 270));
            dc.Pop();
        }

        // --- typing state, same arithmetic as the mockup ---
        string text = "", wrong = "";
        int done = 0;
        bool flash = false;
        for (int r = 0; r < RoundStarts.Length; r++)
        {
            bool typo = r == 1;
            int len = Phrase.Length;
            double tt = t - RoundStarts[r];
            if (tt < 0) continue;
            int n = (int)Math.Floor(tt / CharMs);
            if (typo)
            {
                if (n >= 7 && tt < 7 * CharMs + 320) { n = 7; wrong = "x"; }
                else if (tt >= 7 * CharMs + 320) n = 7 + (int)Math.Floor((tt - 7 * CharMs - 320) / CharMs);
            }
            double end = typo ? 7 * CharMs + 320 + (len - 7) * CharMs : len * CharMs;
            if (tt < end) { text = Phrase[..Math.Min(n, len)]; if (!(typo && wrong.Length > 0)) wrong = ""; }
            else if (tt < end + 260) { text = Phrase; flash = true; done = r + 1; wrong = ""; }
            else { done = r + 1; text = ""; wrong = ""; }
        }
        if (t >= RoundStarts[2] + Phrase.Length * CharMs + 260) { text = Phrase; done = 3; }

        // --- the card ---
        double cardO = 1 - outK;
        double top = LoopMath.Lerp(-170, 58, inK) - LoopMath.Lerp(0, 40, outK);
        if (cardO > 0 && top > -160)
        {
            dc.PushOpacity(cardO);
            f.Card(new Rect(90, top, 300, 150), p.Accent);
            f.Text("Type it 3 times", 106, top + 13, 13, p.Dim);
            f.Text(Phrase, 106, top + 34, 17, p.Text, bold: true);

            double shake = wrong.Length > 0 ? Math.Sin(t / 18) * 3 : 0;
            var input = new Rect(106 + shake, top + 72, 268, 30);
            Brush border = wrong.Length > 0 ? p.Red : (flash || done == 3 ? p.Mint : LineBrush);
            dc.DrawRoundedRectangle(InputFill, new Pen(border, 1), input, 7, 7);

            double x = input.X + 10;
            double midY = input.Y + input.Height / 2;
            x += MonoText(dc, text, x, midY, p.Text);
            x += MonoText(dc, wrong, x, midY, p.Red);
            bool caretOn = ((int)Math.Floor(t / 400) % 2 != 0) || text.Length > 0;
            dc.PushOpacity(caretOn ? 1 : 0.2);
            dc.DrawRectangle(p.Accent, null, new Rect(x + 1, midY - 7.5, 2, 15));
            dc.Pop();

            var lilacPen = new Pen(p.Lilac, 2);
            var mintPen = new Pen(p.Mint, 2);
            for (int i = 0; i < 3; i++)
            {
                var c = new Point(106 + 5 + i * 16, top + 116 + 5);
                bool on = i < done;
                dc.DrawEllipse(on ? p.Mint : null, on ? mintPen : lilacPen, c, 4, 4);
            }
            dc.Pop();
        }

        f.Chip(390, 232, "ESC: OFF", false, inK * (1 - outK));
    }

    /// <summary>Draws mono text vertically centred on <paramref name="midY"/>; returns its advance width.</summary>
    private static double MonoText(DrawingContext dc, string s, double x, double midY, Brush brush)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 13, brush, 1.0);
        dc.DrawText(ft, new Point(x, midY - ft.Height / 2));
        return ft.WidthIncludingTrailingWhitespace;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
