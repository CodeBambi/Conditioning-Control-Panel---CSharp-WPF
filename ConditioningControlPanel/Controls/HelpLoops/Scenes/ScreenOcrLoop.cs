using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops.Scenes;

/// <summary>
/// Screen Text Detection: a page of ordinary words sits on screen, a scan reads down it and
/// boxes each word as it passes, the trigger word gets the pink box and the app answers.
/// </summary>
internal sealed class ScreenOcrLoop : HelpLoopScene
{
    private const string Trigger = "sink";
    private static readonly Rect Win = new(40, 22, 300, 196);
    private const double ScanStart = 2400, ScanEnd = 4400;

    private static readonly string[][] Rows =
    {
        new[] { "Welcome", "back", "to", "the", "forum" },
        new[] { "New", "posts", "since", "your", "last", "visit" },
        new[] { "Tonight", "we", "sink", "a", "little", "deeper" },
        new[] { "Saved", "for", "later", "(3)" },
        new[] { "Reply", "Share", "Save" },
    };

    private static readonly Brush ScanGlow = LoopPalette.Freeze(new LinearGradientBrush(
        new GradientStopCollection
        {
            new(LoopPalette.Css("#5fffd000"), 0),
            new(LoopPalette.Css("#5fffd033"), .35),
            new(LoopPalette.Css("#ffffffa0"), .5),
            new(LoopPalette.Css("#5fffd033"), .65),
            new(LoopPalette.Css("#5fffd000"), 1),
        }, 90));
    private static readonly Brush ReadBox = LoopPalette.Solid("#b99cff26");
    private static readonly Brush HotBox = LoopPalette.Solid("#ff6fb533");
    private static readonly Brush PhotoFill = LoopPalette.Freeze(new LinearGradientBrush(
        LoopPalette.Css("#5fffd0"), LoopPalette.Css("#2b6bff"), new Point(.33, .03), new Point(.67, .97)));

    private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
    {
        new HelpLoopStep("help_loop_screenocr_1", 300, 2400),
        new HelpLoopStep("help_loop_screenocr_2", 2400, 4600),
        new HelpLoopStep("help_loop_screenocr_3", 4600, 6800),
    };

    public override string Id => "ScreenOcr";
    public override double DurationMs => 7200;
    public override double StillMs => 3600;
    public override IReadOnlyList<HelpLoopStep> Steps => StepList;

    public override void Draw(LoopFrame f, double t)
    {
        var p = f.P;
        var dc = f.Front;
        var dk = f.Desktop(Win, drawLines: false);

        double outK = Seg(t, 6600, 7100);
        double scanY = Lerp(Win.Y + 22, Win.Bottom - 6, Seg(t, ScanStart, ScanEnd));
        Rect hot = Rect.Empty;

        // --- the page: grey lines resolve into readable words, row by row ---
        for (int r = 0; r < Rows.Length; r++)
        {
            double y = Win.Y + 32 + r * 30;
            double wordsK = Seg(t, 300 + r * 220, 700 + r * 220) * (1 - outK);
            double x = Win.X + 16;
            foreach (var w in Rows[r])
            {
                var sz = f.Measure(w + " ", 13, LoopFrame.Body);
                var ws = f.Measure(w, 13, LoopFrame.Body);
                var wr = new Rect(x - 2.5, y - 1, ws.Width + 5, ws.Height + 1);
                bool isHot = w == Trigger;

                // placeholder bar before the word shows
                if (wordsK < 1)
                    f.DesktopLine(f.Back, new Rect(x, y + 6, sz.Width - 6, 7), r, 1 - wordsK);

                // a dim box as the scan passes, which lets go half a second later
                double since = ScanTime(y + sz.Height / 2) is double at ? t - at : -1;
                if (since >= 0)
                {
                    double o = 1 - Seg(since, 250, 750);
                    if (o > 0)
                        using (f.Fade(f.Back, o * (1 - outK)))
                            f.Back.DrawRoundedRectangle(ReadBox, new Pen(p.Lilac, 1), wr, 4, 4);
                }
                if (isHot) hot = wr;

                if (wordsK > 0)
                    using (f.Fade(f.Back, wordsK))
                        f.DrawText(f.Back, w, x, y, 13, isHot && t > 4400 && outK < 1 ? p.Text : p.Dim,
                            LoopFrame.Body, FontWeights.Medium, TextAlignment.Left);
                x += sz.Width;
            }
        }

        // --- the scan bar, clipped to the window ---
        if (t > ScanStart && t < ScanEnd)
            using (f.ClipTo(dc, new RectangleGeometry(dk.Window, 8, 8)))
            {
                dc.DrawRectangle(ScanGlow, null, new Rect(Win.X, scanY - 18, Win.Width, 36));
                dc.DrawRectangle(p.Mint, null, new Rect(Win.X, scanY - 1, Win.Width, 2));
            }

        // --- the match: pink box, chip, then the answer ---
        double hitK = Seg(t, 4400, 4650) * (1 - outK);
        if (hitK > 0 && !hot.IsEmpty)
        {
            double sc = Lerp(1.35, 1, EaseOut(hitK));
            using (f.At(dc, hot.X + hot.Width / 2, hot.Y + hot.Height / 2, sc, hitK))
                dc.DrawRoundedRectangle(HotBox, new Pen(p.Accent, 1.5), hot, 4, 4);
            double chipX = Win.X + 222; // just past the end of the row
            f.Chip(chipX, hot.Y + hot.Height / 2 - 11.5, Trigger, hot: true, opacity: hitK);
        }

        f.Chip(Win.Right - f.ChipWidth("READING") - 8, Win.Y + 26, "READING", hot: false,
            opacity: Seg(t, ScanStart - 200, ScanStart) * (1 - Seg(t, ScanEnd, ScanEnd + 200)));

        double photoK = Seg(t, 4900, 5200);
        double photoO = photoK * (1 - outK);
        if (photoO > 0)
        {
            var r = new Rect(352, 50, 104, 128);
            using (f.At(dc, r.X + r.Width / 2, r.Y + r.Height / 2, Lerp(.6, 1, Back(photoK)), photoO))
            {
                LoopFrame.SoftShadow(dc, r, 4, 8, 18, 0xAA);
                dc.DrawRoundedRectangle(Brushes.White, null, r, 4, 4);
                dc.DrawRoundedRectangle(PhotoFill, null, LoopFrame.Inset(r, 3), 1.5, 1.5);
            }
        }
        double wave = Seg(t, 4950, 5950);
        using (f.Fade(dc, Seg(t, 4800, 5000) * (1 - outK)))
            f.Speaker(380, 196, wave > 0 && wave < 1 ? wave : 0);
    }

    /// <summary>When the scan line reached height <paramref name="y"/>, or null if it never does.</summary>
    private static double? ScanTime(double y)
    {
        double y0 = Win.Y + 22, y1 = Win.Bottom - 6;
        if (y < y0 || y > y1) return null;
        return Lerp(ScanStart, ScanEnd, (y - y0) / (y1 - y0));
    }
}
