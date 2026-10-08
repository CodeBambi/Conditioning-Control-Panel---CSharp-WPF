using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BOOK DEMO BATCH: her desk, and the video surface. The loops for the cards in
/// <see cref="EmiBookDeckDesk"/>, one painter per card id.
///
/// <para>A loop is a sentence: 4 to 7 seconds, one idea, legible with the sound off and the words
/// unread. If a loop needs its nudge to be understood then the loop is wrong. See
/// <see cref="EmiDemoPainter"/> for the contract and <see cref="EmiPix"/> for the vocabulary.</para>
/// </summary>
internal static class EmiBookDemosDesk
{
    /// <summary>This batch's painters.</summary>
    public static readonly EmiDemoPainter[] All =
    {
        new DeskDemo(), new VideoDemo(), new WhisperDemo(),
    };

    // =====================================================================================
    //  THE DESK  -  the cards come out of HER
    // =====================================================================================
    //
    // THE SENTENCE: a cursor arrives, six tiles fan out of her chest, one lights, the fan folds
    // away. That is the ring, and the fan is doing the work that the word "menu" would do badly at
    // this size - a stack of six rows would read as a settings list, while six tiles leaving one
    // point and returning to it read as belonging to the thing they came out of.
    //
    // SHE IS DRAWN FROM RECTANGLES, NOT FROM HER SPRITE SHEET. The book's stage is a 96 x 72
    // software buffer with no image loader in it, and a painter that reached for her real art would
    // be the one demo in the deck that can fail on a missing file. Five rectangles and four pixels
    // of eye are enough to be recognisably her at this scale, and cost nothing at startup.
    //
    // THE FAN GEOMETRY IS AN ARC OVER HER, NOT AROUND HER. A full circle would put two tiles under
    // the taskbar and one off the right edge, because she is parked bottom-right the way the real
    // avatar is. 172 to 282 degrees keeps every tile on stage with margin, and reading up-and-left
    // from her is also the direction the real ring opens into the screen rather than off it.

    private sealed class DeskDemo : EmiDemoPainter
    {
        public override string Id => "the-desk";
        public override int LoopMs => 6000;

        /// <summary>
        /// Freeze on the lit tile, not on the open fan. An open fan alone says "she has a menu";
        /// the lit tile says "and you press one of them", which is the half of the sentence a
        /// reduced-motion reader would otherwise never get.
        /// </summary>
        public override double StillMs => 4300;

        // Centre of each tile at full extension. Hand-placed from a 30 cell radius about her chest
        // at 172 / 194 / 216 / 238 / 260 / 282 degrees, rounded, so the arc is a table lookup
        // rather than six trig calls a frame.
        private static readonly int[][] Fan =
        {
            new[] { 42, 52 }, new[] { 43, 41 }, new[] { 48, 30 },
            new[] { 56, 23 }, new[] { 67, 19 }, new[] { 78, 19 },
        };

        // Her chest, which is where the tiles are born and where they go back to.
        private const int Hx = 72;
        private const int Hy = 58;

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);

            // The cursor arrives before anything opens. Without it the fan looks automatic, and the
            // one thing this card has to say is that SHE ANSWERS A GESTURE.
            if (t > 200 && t < 1500)
            {
                double f = Math.Min(1, (t - 200) / 900.0);
                EmiPix.Cursor(p, 14 + f * 46, 60 - f * 6);
            }

            for (int i = 0; i < Fan.Length; i++)
            {
                // One value per tile: rises as it flies out, falls as it folds back. Staggering the
                // fold faster than the open (60 ms against 110) makes the close read as one motion
                // and the open as six, which is the right emphasis - you notice things arriving.
                double g = Ramp(t, 1200 + i * 110, 240) - Ramp(t, 4800 + i * 60, 240);
                if (g <= 0.01) continue;

                double e = g * g * (3 - 2 * g);
                double cx = Hx + (Fan[i][0] - Hx) * e;
                double cy = Hy + (Fan[i][1] - Hy) * e;

                // The third tile lights while the cursor is still on stage: a pinned shortcut being
                // taken, rather than six shortcuts sitting there being available.
                bool lit = i == 2 && t > 4150 && t < 4700;
                p.Rect(cx - 6, cy - 4, 12, 9, lit ? EmiPix.Cream : EmiPix.Mid);
                p.Rect(cx - 5, cy - 3, 10, 7, lit ? EmiPix.Pink : EmiPix.Ink);
                p.Rect(cx - 3, cy - 1, 6, 2, lit ? EmiPix.Ink : EmiPix.Lav);
            }

            DrawEmi(p, t);
        }

        /// <summary>0 before <paramref name="at"/>, 1 after <paramref name="dur"/>, smooth between.</summary>
        private static double Ramp(double t, double at, double dur)
            => t <= at ? 0 : (t >= at + dur ? 1 : (t - at) / dur);

        /// <summary>
        /// Her, in five rectangles. Drawn LAST so the fan passes behind her rather than over her -
        /// tiles crossing her face would make the fan look like a window on top of her instead of
        /// something she is holding out.
        /// </summary>
        private static void DrawEmi(EmiPixelCanvas p, double t)
        {
            p.Rect(63, 37, 18, 9, EmiPix.Pink);          // hair
            p.Rect(66, 42, 12, 10, EmiPix.Cream);        // face
            p.Rect(69, 46, 2, 2, EmiPix.Ink);            // eyes
            p.Rect(75, 46, 2, 2, EmiPix.Ink);
            p.Rect(64, 52, 16, 12, EmiPix.Pink);         // body
            p.Rect(69, 55, 6, 6, EmiPix.Ink);            // her little screen

            // One pink cell orbiting inside the glass. It is the only thing on this stage that
            // never stops, which is what says she is running even when nothing is open.
            double a = t / 300.0;
            p.Px(72 + (int)Math.Round(Math.Cos(a) * 2), 58 + (int)Math.Round(Math.Sin(a) * 2), EmiPix.Pink);

            p.Rect(80, 53, 2, 2, EmiPix.Gold);           // the gear by her shoulder
        }
    }

    // =====================================================================================
    //  MANDATORY VIDEOS  -  it takes the screen, and a miss costs you a whole second clip
    // =====================================================================================
    //
    // THE SENTENCE: two windows, one hard cut to full bleed with a closed padlock, two targets -
    // one caught, one missed - and the missed one restarts the whole thing on a DIFFERENT picture.
    // The changed image is the entire point of the last beat. A restart that replayed the same
    // frames would say "it loops"; a restart on new content says "it fetched another one", which
    // is what the code does and what the fourth nudge promises.
    //
    // THE CUT IS ONE FRAME OF FLAT GREY, NOT A FADE, and it covers the taskbar too. This feature
    // does not arrive politely into a window - it is the whole display, over whatever you were
    // doing, and a demo that eased into it would be selling a gentler tool than the one that ships.

    private sealed class VideoDemo : EmiDemoPainter
    {
        public override string Id => "videos";
        public override int LoopMs => 6000;

        /// <summary>
        /// Freeze with the padlock shut, the bar part-run and the first target still up and
        /// unclicked. That single frame carries all three claims the card makes; the still is the
        /// card's illustration for anyone who never sees it move.
        /// </summary>
        public override double StillMs => 2600;

        public override void Draw(EmiPixelCanvas p, double t)
        {
            // ---- before: an ordinary desktop, minding its own business
            if (t < 1000)
            {
                EmiPix.Desk(p);
                EmiPix.PixImage(p, 8, 10, 26, 18, 0);
                EmiPix.PixImage(p, 52, 14, 28, 20, 1);
                return;
            }

            // ---- the takeover, on one frame
            if (t < 1150)
            {
                p.Rect(0, 0, p.W, p.H, EmiPix.Mid);
                return;
            }

            // ---- after: the video owns the display
            bool second = t >= 4900;
            p.Clear(EmiPix.Ink);
            EmiPix.PixImage(p, 0, 6, p.W, 54, second ? 3 : 1);

            // The progress bar resets to nothing at the restart. Nothing else on stage says "you
            // are doing this again from the top" as flatly as a full bar becoming an empty one.
            double frac = second ? (t - 4900) / 3600.0 : (t - 1150) / 4400.0;
            EmiPix.Bar(p, 6, 63, 84, 4, frac, EmiPix.Pink);

            // Shut, and it stays shut for the whole clip. The one prop on stage that is not doing
            // anything is the one making the strongest claim.
            EmiPix.Padlock(p, 80, 9, false);

            if (second) return;

            // TARGET ONE, caught. The cursor is drawn travelling to it so the click reads as the
            // user's doing rather than as the target expiring on its own.
            if (t > 1900 && t < 2900)
            {
                Target(p, 26, 30 + Math.Sin(t / 180.0));
                double f = Math.Min(1, (t - 1950) / 850.0);
                EmiPix.Cursor(p, 10 + f * 20, 58 - f * 24);
            }

            // TARGET TWO, missed. No cursor goes near it, and it simply stops being drawn at 4900
            // with the restart on the same frame, so the cause and the cost are one event.
            if (t > 3400) Target(p, 56, 42 + Math.Sin(t / 180.0));
        }

        /// <summary>A word chip: a pink plate with two ink word-blocks, which is what the real
        /// attention target is - a short phrase you have to find and hit.</summary>
        private static void Target(EmiPixelCanvas p, double x, double y)
        {
            p.Rect(x, y, 20, 9, EmiPix.Pink);
            p.Rect(x + 3, y + 3, 6, 3, EmiPix.Ink);
            p.Rect(x + 11, y + 3, 5, 3, EmiPix.Ink);
        }
    }

    // =====================================================================================
    //  WHISPERS  -  one sound arrives and every other sound gets out of its way
    // =====================================================================================
    //
    // THE SENTENCE: a tall lavender wave is everything else you are listening to. A phrase flashes,
    // a small pink wave opens under it, and the lavender one drops to a fifth of its height for
    // exactly as long as the pink one is there, then comes back. Two colours of the same prop, so
    // the relationship is legible without a label - and the drop being a RATIO rather than a
    // silence is the honest picture, because ducking lowers other audio, it does not stop it.
    //
    // THE PHRASE COMES FIRST BY 100 MS. The whisper is matched to the phrase it flashes, and a
    // stage where they began together would leave the order ambiguous. The lead is small enough to
    // read as cause rather than as two separate events.

    private sealed class WhisperDemo : EmiDemoPainter
    {
        public override string Id => "whispers";
        public override int LoopMs => 5500;

        /// <summary>Freeze mid-duck, with both waves up. One wave alone is not a sentence.</summary>
        public override double StillMs => 2600;

        private const double DuckIn = 1900;
        private const double DuckOut = 3400;

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);

            // Everything else you have playing. It never leaves the stage, because it never stops.
            double amp = 1.0;
            if (t >= DuckIn && t < DuckIn + 200) amp = 1.0 - 0.78 * ((t - DuckIn) / 200.0);
            else if (t >= DuckIn + 200 && t < DuckOut) amp = 0.22;
            else if (t >= DuckOut && t < DuckOut + 400) amp = 0.22 + 0.78 * ((t - DuckOut) / 400.0);

            p.Rect(4, 40, 88, 24, EmiPix.Ink);
            EmiPix.Wave(p, 6, 42, 84, 20, t, amp, EmiPix.Lav, 12);

            // The phrase, at the length the real thing runs: a blip, not a caption.
            if (t > 1800 && t < 1950) EmiPix.Phrase(p, 22, 4, 52, 16);

            // Her whisper. Fewer, wider columns than the desktop wave so it reads as one voice
            // rather than as a second mix.
            if (t >= DuckIn && t < DuckOut)
            {
                p.Rect(28, 22, 40, 14, EmiPix.Ink);
                EmiPix.Wave(p, 30, 24, 36, 10, t, 1.0, EmiPix.Pink, 6);
            }
        }
    }
}
