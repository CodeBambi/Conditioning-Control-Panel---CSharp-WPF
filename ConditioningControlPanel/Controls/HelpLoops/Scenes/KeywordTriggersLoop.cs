using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops.Scenes;

/// <summary>
/// Keyword Triggers: a note is typed as usual, the last word is a trigger, it is picked out
/// and something answers (a flash lands, a sound plays).
/// </summary>
internal sealed class KeywordTriggersLoop : HelpLoopScene
{
    private const string Note = "just a quick note, then relax";
    private const string Word = "relax";
    private const double TypeStart = 400, TypeEnd = 2500;
    private const double Hit = 2650;
    private static readonly Rect Field = new(78, 58, 290, 22);
    private static readonly Rect PhotoRect = new(282, 106, 132, 92);

    private static readonly Brush Glow = LoopPalette.Solid("#ff6fb540");

    private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
    {
        new HelpLoopStep("help_loop_keywordtriggers_1", 300, 2600),
        new HelpLoopStep("help_loop_keywordtriggers_2", 2600, 4200),
        new HelpLoopStep("help_loop_keywordtriggers_3", 4200, 6600),
    };

    public override string Id => "KeywordTriggers";
    public override double DurationMs => 7000;
    public override double StillMs => 3300;
    public override IReadOnlyList<HelpLoopStep> Steps => StepList;

    public override void Draw(LoopFrame f, double t)
    {
        var p = f.P;
        var dc = f.Front;
        var desk = f.Desktop(null, drawLines: false);
        // The note keeps a few faint lines under the typed one.
        for (int i = 2; i < desk.Lines.Count; i++) f.DesktopLine(f.Back, desk.Lines[i], i, .45);

        double outK = Seg(t, 6400, 6900);
        double o = 1 - outK;

        using (f.Fade(dc, o))
        {
            // The word, found: a glow under it and a pink underline.
            var prefix = Note[..^Word.Length];
            double x0 = Field.X + f.Format(prefix, 13, p.Text, LoopFrame.Mono, FontWeights.Medium).WidthIncludingTrailingWhitespace;
            double ww = f.Format(Word, 13, p.Text, LoopFrame.Mono, FontWeights.Medium).WidthIncludingTrailingWhitespace;
            double hk = Seg(t, Hit, Hit + 220);
            if (hk > 0)
            {
                using (f.Fade(dc, hk))
                    dc.DrawRoundedRectangle(Glow, null, new Rect(x0 - 3, Field.Y + 1, ww + 6, Field.Height - 2), 4, 4);
                dc.DrawRoundedRectangle(p.Accent, null, new Rect(x0, Field.Bottom - 2, ww * EaseOut(hk), 2), 1, 1);
            }

            f.TypeInto(Field, Note, Seg(t, TypeStart, TypeEnd), t, caret: t < 6000, box: false);

            // The trigger chip pops above the word.
            double ck = Seg(t, Hit + 120, Hit + 420);
            if (ck > 0)
            {
                double cw = f.ChipWidth("TRIGGER: " + Word);
                double cx = x0 + ww / 2, cy = Field.Y - 18;
                using (f.At(dc, cx, cy + 11, Lerp(.5, 1, Back(ck)), ck))
                    f.Chip(cx - cw / 2, cy, "TRIGGER: " + Word, hot: true);
            }

            // Something answers: a flash lands, the speaker plays.
            double pk = Seg(t, 4300, 4600);
            if (pk > 0)
                using (f.At(dc, PhotoRect.X + PhotoRect.Width / 2, PhotoRect.Y + PhotoRect.Height / 2, Lerp(.6, 1, Back(pk)), pk))
                    f.Photo(PhotoRect, PhotoLook.Pink);

            if (t > 4500)
            {
                double wave = ((t - 4500) % 900) / 900;
                using (f.Fade(dc, Seg(t, 4500, 4700)))
                    f.Speaker(230, 150, wave);
            }
        }

        f.Chip(14, 12, "LISTENING", opacity: 1);
        // Hands on the keyboard: the pointer waits off to the side.
        f.Cursor(430, 232, false);
    }
}
