using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BOOK DEMO BATCH: the setup a new user does first. The loops for the cards in
/// <see cref="EmiBookDeckSetup"/>, one painter per card id.
///
/// <para>A loop is a sentence: 4 to 7 seconds, one idea, legible with the sound off and the words
/// unread. If a loop needs its nudge to be understood then the loop is wrong. See
/// <see cref="EmiDemoPainter"/> for the contract and <see cref="EmiPix"/> for the vocabulary.</para>
///
/// <para><b>All three loops here are about a SOURCE feeding a SINK</b>, which is what makes them
/// read as one batch: a folder or an aerial feeding a stack of pictures, a clock hand feeding four
/// lanes, three finished things feeding a bar. Nothing in the batch animates the feature itself,
/// because none of these three features has a look - they are the plumbing behind the ones that do.
/// </para>
/// </summary>
internal static class EmiBookDemosSetup
{
    /// <summary>This batch's painters.</summary>
    public static readonly EmiDemoPainter[] All =
    {
        new YourMediaDemo(), new SessionsDemo(), new ProgressionDemo(),
    };

    // =====================================================================================
    //  shared ink
    // =====================================================================================

    /// <summary>
    /// A dotted run from a source to a thing it produced. Dotted rather than solid because a solid
    /// line at this size reads as a BORDER between two regions, which is the exact opposite of what
    /// a feed line is for; and the phase argument marches the gaps along so the direction of travel
    /// is visible without drawing an arrowhead nobody could resolve in three cells.
    /// </summary>
    private static void Feed(EmiPixelCanvas p, double x0, double y0, double x1, double y1, int phase)
    {
        double dx = x1 - x0, dy = y1 - y0;
        int n = (int)Math.Max(1, Math.Round(Math.Max(Math.Abs(dx), Math.Abs(dy))));
        for (int i = 0; i <= n; i++)
        {
            if (((i + phase) % 4) != 0) continue;
            p.Px((int)Math.Round(x0 + dx * i / n), (int)Math.Round(y0 + dy * i / n), EmiPix.Lav);
        }
    }

    // =====================================================================================
    //  05  YOUR MEDIA  -  same three frames, two different suppliers
    // =====================================================================================

    /// <summary>
    /// THE SENTENCE: "these come out of a folder. or they come out of the air."
    ///
    /// <para>The trick that makes it one sentence instead of two is that both halves fill the SAME
    /// three slots. An earlier cut gave the local pictures and the remote pictures their own places
    /// on the stage, and it read as six pictures and two unrelated features rather than as one
    /// stack with a switch behind it - which is precisely the thing the card is trying to say, so
    /// the loop was saying the opposite of its own nudges.</para>
    ///
    /// <para>The folder is drawn for the whole loop and the aerial only for the second half. That
    /// asymmetry is deliberate and it is the honest one: your folder does not go away when you turn
    /// the wire on (that is what "mixed" is), but the wire genuinely is not there until you switch
    /// it on and answer the consent box.</para>
    /// </summary>
    private sealed class YourMediaDemo : EmiDemoPainter
    {
        public override string Id => "your-media";
        public override int LoopMs => 6000;

        /// <summary>
        /// Late in the remote half, with all three slots full. It is the only frame in the loop
        /// where the folder, the aerial and a full stack are on the stage together, so it is the
        /// only single frame that carries the whole "or" the card is built on.
        /// </summary>
        public override double StillMs => 4900;

        /// <summary>x, y, w, h. One column, because a column is obviously ONE stack being fed.</summary>
        private static readonly int[][] Slots =
        {
            new[] { 31, 6, 24, 16 },
            new[] { 31, 25, 24, 16 },
            new[] { 31, 44, 24, 16 },
        };

        /// <summary>Where the folder hands a picture over: its own right edge, at its waist.</summary>
        private const int FolderX = 5, FolderY = 46;

        /// <summary>Where the aerial broadcasts from. Off to the right, clear of the slots.</summary>
        private const int AerialX = 88, AerialY = 12;

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);
            EmiPix.Folder(p, FolderX, FolderY);

            bool remote = t >= 3100;
            if (remote) Aerial(p, t);

            for (int i = 0; i < Slots.Length; i++)
            {
                // Two identical cadences, 3100 ms apart. Seeds differ across the halves so the
                // second pass is visibly OTHER pictures rather than a replay of the first.
                double due = remote ? 3300 + i * 620 : 300 + i * 620;
                if (t < due) continue;
                if (!remote && t > 2900) continue;

                int seed = remote ? i + 3 : i;
                var s = Slots[i];

                // The feed line is drawn only for the second or so after a picture lands. Held for
                // the whole loop it becomes wallpaper and stops meaning "this one came from there".
                double age = t - due;
                if (age < 1100)
                {
                    int phase = (int)(t / 90) % 4;
                    if (remote) Feed(p, AerialX - 4, AerialY + 2, s[0] + s[2] + 1, s[1] + s[3] / 2, phase);
                    else Feed(p, FolderX + 23, FolderY + 8, s[0] - 1, s[1] + s[3] / 2, phase);
                }

                EmiPix.PixImage(p, s[0], s[1], s[2], s[3], seed, age < 200 ? age / 200.0 : 1.0);
            }
        }

        /// <summary>
        /// Three arcs off a dot: the one glyph that says "over the air" without a globe, a cloud or
        /// a wifi logo, none of which exist in the props kit and none of which would survive being
        /// drawn at this size. The rings breathe outward so it reads as transmitting rather than as
        /// a decorative fan.
        /// </summary>
        private static void Aerial(EmiPixelCanvas p, double t)
        {
            p.Rect(AerialX - 1, AerialY - 1, 3, 3, EmiPix.Cream);
            for (int i = 0; i < 3; i++)
            {
                // Each ring is one third of a cycle ahead of the next, so at any instant the three
                // are at three different radii and the motion is outward, never pulsing in unison.
                double phase = ((t / 900.0) + i / 3.0) % 1.0;
                double r = 4 + phase * 12;
                for (double a = Math.PI * 0.66; a <= Math.PI * 1.34; a += 0.05)
                    p.Px((int)Math.Round(AerialX + Math.Cos(a) * r),
                         (int)Math.Round(AerialY + Math.Sin(a) * r),
                         phase > 0.75 ? EmiPix.Mid : EmiPix.Pink);
            }
        }
    }

    // =====================================================================================
    //  06  SESSIONS  -  four lanes, one hand, and an empty stage at the end
    // =====================================================================================

    /// <summary>
    /// THE SENTENCE: "a clock walks across four tools, each on its own stretch, two of them growing
    /// as it goes."
    ///
    /// <para>Lanes rather than a desktop. Every other loop in the book is staged on
    /// <see cref="EmiPix.Desk(EmiPixelCanvas, bool)"/> because every other card is about something
    /// you SEE; a session is a schedule, and the honest picture of a schedule is a schedule. The
    /// taskbar is off for the same reason - it would promise a desktop this loop never shows.</para>
    ///
    /// <para><b>The empty tail is load-bearing.</b> The last 700 ms draw the four tracks with
    /// nothing in them, which is nudge four ("your own settings are put back") and is also the only
    /// way the loop can say that the thing ENDS. Without it the sweep just wraps and a session
    /// reads as something that runs forever, which is the single most common fear about handing an
    /// app a timer.</para>
    /// </summary>
    private sealed class SessionsDemo : EmiDemoPainter
    {
        public override string Id => "sessions";
        public override int LoopMs => 6000;

        /// <summary>
        /// Roughly three quarters across: all four lanes have started, both ramps are visibly
        /// mid-growth, and the played and unplayed halves of every segment are both on screen. Any
        /// earlier and the ramp has not grown enough to be read as a ramp.
        /// </summary>
        public override double StillMs => 3900;

        /// <summary>How long the hand takes to cross. The rest of the loop is the empty tail.</summary>
        private const double SweepMs = 5250;

        private const int TrackX = 8, TrackW = 80;

        /// <summary>y, start fraction (x1000), end fraction (x1000), ramps (1) or not (0).</summary>
        private static readonly int[][] Lanes =
        {
            new[] { 10,   0, 1000, 0 },
            new[] { 24, 150,  760, 1 },
            new[] { 38, 340, 1000, 0 },
            new[] { 52, 550,  900, 1 },
        };

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p, false);

            double head = Math.Min(1.0, t / SweepMs);
            bool over = t > SweepMs + 50;

            foreach (var lane in Lanes)
            {
                int y = lane[0];
                p.Rect(TrackX, y, TrackW, 8, EmiPix.Ink);
                p.Rect(TrackX, y, TrackW, 1, EmiPix.Mid);
                if (over) continue;

                double s = lane[1] / 1000.0, e = lane[2] / 1000.0;
                double x0 = TrackX + s * TrackW, x1 = TrackX + e * TrackW;

                // The whole booked stretch, dim: what is SCHEDULED. Without it the lanes would only
                // show what has already happened, and a timeline you cannot see the future of is
                // just a progress bar.
                p.Rect(x0, y + 3, Math.Max(1, x1 - x0), 2, EmiPix.Mid);

                double played = Math.Min(x1, TrackX + head * TrackW);
                if (played <= x0) continue;

                if (lane[3] == 0)
                {
                    p.Rect(x0, y + 2, played - x0, 4, EmiPix.Pink);
                }
                else
                {
                    // A ramp is drawn as a wedge rather than as a colour change, because at 96 cells
                    // wide a two-stop gradient in one hue is four pixels of nothing. Height is the
                    // only channel this canvas has that survives being read at a glance.
                    for (double x = x0; x <= played; x++)
                    {
                        double f = (x - x0) / Math.Max(1, x1 - x0);
                        double h = 1 + f * 5;
                        p.Rect(x, y + 7 - h, 1, h, EmiPix.Pink);
                    }
                }
            }

            // Minute ticks under the stack. They cost nine pixels and they are what stops the
            // sweep reading as a loading bar.
            for (int i = 0; i <= 8; i++) p.Px(TrackX + i * 10, 64, EmiPix.Mid);

            if (!over) p.Rect(TrackX + head * TrackW, 6, 1, 58, EmiPix.Cream);
        }
    }

    // =====================================================================================
    //  07  PROGRESSION  -  three things done, one bar, one token
    // =====================================================================================

    /// <summary>
    /// THE SENTENCE: "the things you do fill a bar, and a full bar pays you a token."
    ///
    /// <para><b>No padlock anywhere in this loop</b>, and that is the whole design note. A level-up
    /// demo wants a lock springing open, the props kit has one, and it would be a lie:
    /// <c>AppSettings.IsLevelUnlocked</c> returns true unconditionally, so a level opens nothing.
    /// The gold token is what a level actually hands over (one skill point,
    /// <c>SkillTreeService.PointsPerLevel</c>), and the row it lands in is the only claim this loop
    /// makes about what levelling is FOR.</para>
    ///
    /// <para>The bar refills to a sliver rather than to zero after the level, because that is what
    /// carry-over XP looks like and a bar that snaps to empty says "start again".</para>
    /// </summary>
    private sealed class ProgressionDemo : EmiDemoPainter
    {
        public override string Id => "progression";
        public override int LoopMs => 6000;

        /// <summary>
        /// After the token has landed. Three finished things, a bar that has clearly been round
        /// once, and three tokens where there were two - the payout is the point, so the still is
        /// the frame that has the payout IN it rather than the frame where it is being earned.
        /// </summary>
        public override double StillMs => 4900;

        private const double LevelMs = 3300;
        private const int BarX = 8, BarY = 42, BarW = 80, BarH = 8;
        private const int TokenY = 62, TokenX0 = 12, TokenGap = 8;

        /// <summary>Three things done, left to right, each one worth a visible step of the bar.</summary>
        private static readonly int[][] Deeds =
        {
            new[] { 10, 16 }, new[] { 38, 16 }, new[] { 66, 16 },
        };

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p, false);

            // The deeds stay up once earned. They are a record, not an animation: clearing them
            // would say the XP was taken back.
            int done = 0;
            for (int i = 0; i < Deeds.Length; i++)
            {
                double due = 400 + i * 900;
                if (t < due) continue;
                done++;
                EmiPix.PixImage(p, Deeds[i][0], Deeds[i][1], 20, 14, i, Math.Min(1, (t - due) / 200.0));
            }

            // Stepped, not lerped. XP arrives in lumps when a thing finishes, and a bar that crept
            // forward continuously would be drawing a per-second trickle the app does not pay.
            // 0.16 + 3 x 0.28 lands exactly on 1.0, so the third deed visibly COMPLETES the bar
            // rather than leaving it a cell short - a bar that stops at 98% and levels anyway reads
            // as the demo cheating.
            double frac = t >= LevelMs ? 0.12 : 0.16 + done * 0.28;
            EmiPix.Bar(p, BarX, BarY, BarW, BarH, Math.Min(1, frac), EmiPix.Pink);

            // The level itself: one bright frame across the full bar. Short on purpose - the event
            // is not the thing worth looking at, the token it produces is.
            if (t >= LevelMs && t < LevelMs + 140) p.Rect(BarX, BarY, BarW, BarH, EmiPix.Cream);

            // Two already banked, and the third arrives out of the bar it was paid by.
            int banked = t >= LevelMs + 1200 ? 3 : 2;
            for (int i = 0; i < banked; i++) Token(p, TokenX0 + i * TokenGap, TokenY);

            if (t >= LevelMs + 140 && t < LevelMs + 1200)
            {
                double f = (t - LevelMs - 140) / 1060.0;
                Token(p, (int)Math.Round(48 + (TokenX0 + 2 * TokenGap - 48) * f),
                         (int)Math.Round(BarY + 4 + (TokenY - BarY - 4) * f));
            }
        }

        /// <summary>A five-cell gold pip. Small enough that a row of them counts at a glance.</summary>
        private static void Token(EmiPixelCanvas p, int cx, int cy)
        {
            p.Rect(cx - 2, cy, 5, 1, EmiPix.Gold);
            p.Rect(cx - 1, cy - 1, 3, 3, EmiPix.Gold);
            p.Px(cx, cy - 2, EmiPix.Gold);
            p.Px(cx, cy + 2, EmiPix.Gold);
        }
    }
}
