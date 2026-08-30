using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BOOK DEMO BATCH: the places to go. The loops for the cards in
/// <see cref="EmiBookDeckPlaces"/>, one painter per card id.
///
/// <para>A loop is a sentence: 4 to 7 seconds, one idea, legible with the sound off and the words
/// unread. If a loop needs its nudge to be understood then the loop is wrong. See
/// <see cref="EmiDemoPainter"/> for the contract and <see cref="EmiPix"/> for the vocabulary.</para>
///
/// <para><b>NONE OF THESE THREE STAND ON THE DESK.</b> Every wave A loop opens with
/// <see cref="EmiPix.Desk"/> because every wave A card is about something that happens OVER the
/// user's desktop, and the taskbar strip is what makes "over" legible. These three cards are about
/// places that take the whole screen and hand it back afterwards, so a desk under them would be
/// drawing the exact relationship the feature does not have. All three are full-bleed instead, and
/// the absence of the taskbar is the first thing that tells a reader flicking through the DEEPER
/// tab that these cards are a different kind of thing.</para>
///
/// <para><b>WHY EACH ONE IS A ROOM RATHER THAN AN ACTION.</b> A destination cannot be demonstrated
/// the way a flash can - there is no single gesture that IS the Arcademy. So each loop draws the
/// place and then lets one thing happen to it: rooms light, doors fill, a lock opens. One verb per
/// loop, and the verb is always the thing the card is asking the reader to go and do.</para>
/// </summary>
internal static class EmiBookDemosPlaces
{
    /// <summary>This batch's painters.</summary>
    public static readonly EmiDemoPainter[] All =
    {
        new ArcademyDemo(), new GamesDemo(), new VaultDemo(),
    };

    // =====================================================================================
    //  THE ARCADEMY  -  a corridor, six rooms, and four of them open tonight
    // =====================================================================================

    /// <summary>
    /// THE SENTENCE: "this is a building, and tonight four of its rooms are lit."
    ///
    /// <para>The first draft dealt four split-flap plates, because the board is literally the
    /// Arcademy's first screen. It was wrong for the same reason a screenshot of a menu is a bad
    /// advert: four rectangles turning over says "a list" and the whole point of the place is that
    /// it is a PLACE. So the loop draws the plan instead - a corridor with rooms bolted to both
    /// sides, which is the shape shell/campus.js actually lays out - and lights them.</para>
    ///
    /// <para><b>TWO ROOMS STAY DARK ON PURPOSE, AND THEY ARE THE HONEST PART.</b> Six drawn, four
    /// lit, and the timetable only ever deals four a night (core/timetable.js:68). A loop that lit
    /// every room would be promising a whole school every evening, which is a thing the card's own
    /// first nudge then has to take back. The two dark rooms do the work of the word "four" for a
    /// reader who never gets as far as the bullets.</para>
    ///
    /// <para>The walker exists so the corridor reads as a corridor rather than as a divider. It is
    /// two pixels wide and it never arrives anywhere; it is punctuation, not a character.</para>
    /// </summary>
    private sealed class ArcademyDemo : EmiDemoPainter
    {
        public override string Id => "arcademy";
        public override int LoopMs => 6500;

        /// <summary>All four rooms lit and the walker still mid-hall. The last room lands at
        /// 3700 ms, so the default 55% still (3575) would freeze one beat too early and show a
        /// three-room night, which is the one number on this card that has to be right.</summary>
        public override double StillMs => 4700;

        /// <summary>x, y of each room's top-left. Three on the north side of the hall, three on
        /// the south, on the same three columns - the plan's own arrangement.</summary>
        private static readonly int[][] Rooms =
        {
            new[] { 6, 6 }, new[] { 36, 6 }, new[] { 66, 6 },
            new[] { 6, 41 }, new[] { 36, 41 }, new[] { 66, 41 },
        };

        /// <summary>Which rooms open, in the order they open. Deliberately not 0,1,2,3: a night's
        /// four are dealt from the whole pool, so they land on both sides of the hall and skip
        /// about, and a left-to-right sweep would read as a progress bar.</summary>
        private static readonly int[] Lit = { 1, 4, 0, 5 };

        /// <summary>A composition per room. NOT the room index: PixImage has five motifs and takes
        /// its seed modulo five, so plain indices would give rooms 0 and 5 the same picture - and
        /// those two are both in tonight's four, three seconds apart. Hand-picked so the four that
        /// light are four different pictures, which is the whole reason the prop takes a seed.</summary>
        private static readonly int[] Seeds = { 0, 1, 2, 3, 4, 2 };

        private const int RoomW = 24;
        private const int RoomH = 25;

        public override void Draw(EmiPixelCanvas p, double t)
        {
            p.Clear(EmiPix.Navy);
            p.Rect(0, 0, p.W, 1, EmiPixelCanvas.Rgb(0x24, 0x24, 0x40));

            // THE MAIN HALL. Drawn as a dark well between two bright edges rather than as a bright
            // band: a lit corridor would out-shine the rooms, and the rooms are the subject.
            p.Rect(2, 32, 92, 8, EmiPix.Ink);
            p.Rect(2, 32, 92, 1, EmiPix.Mid);
            p.Rect(2, 39, 92, 1, EmiPix.Mid);

            for (int i = 0; i < Rooms.Length; i++)
            {
                int x = Rooms[i][0], y = Rooms[i][1];
                bool north = y < 32;

                // When a room opens is a property of the ORDER it was dealt, not of where it sits
                // on the plan, so the lookup runs over Lit rather than over the room index.
                int slot = Array.IndexOf(Lit, i);
                bool lit = slot >= 0 && t > 700 + slot * 1000;

                if (lit)
                {
                    // PixImage puts a pink border round its own composition, so a lit room needs
                    // no separate frame - the border IS the frame, and that is what makes an open
                    // room read as brighter EDGE as well as brighter middle.
                    EmiPix.PixImage(p, x, y, RoomW, RoomH, Seeds[i]);
                }
                else
                {
                    p.Rect(x, y, RoomW, RoomH, EmiPix.Mid);
                    p.Rect(x + 1, y + 1, RoomW - 2, RoomH - 2, EmiPix.Dark);
                }

                // The door onto the hall. Pink when the room is open, which is the only cue a
                // reader gets that the lit rooms are the ones they can walk into.
                p.Rect(x + 10, north ? 30 : 39, 4, 3, lit ? EmiPix.Pink : EmiPix.Mid);
            }

            // The walk. One pass of the hall per loop, so the loop's length and the walk's length
            // are the same fact and the demo never looks like it restarted mid-stride.
            double walk = 4 + (t / LoopMs) * 86;
            p.Rect(walk, 34, 2, 4, EmiPix.Cream);
        }
    }

    // =====================================================================================
    //  THE OTHER GAMES  -  four doorways, and each one has a whole thing behind it
    // =====================================================================================

    /// <summary>
    /// THE SENTENCE: "there are four more doors and none of them are empty."
    ///
    /// <para>The card's job is discovery, so the loop is built around the moment of finding out
    /// rather than around any one game. Four identical dark doorways stand in a row - identical
    /// because a reader who has never opened them cannot tell them apart either - and then each
    /// fills with a different picture. Four DIFFERENT pictures is the whole argument: the shapes
    /// behind the doors have nothing in common, which is the true thing about a roguelite, a feed,
    /// a quiz and a duel sitting on one card.</para>
    ///
    /// <para>They fill rather than swing. A door leaf that opens needs perspective to read at 96
    /// cells wide and would cost most of the frame to draw badly; light arriving in a dark opening
    /// says the same thing in one <c>PixImage</c> call and cannot be misread as a glitch.</para>
    ///
    /// <para><b>NOTHING SHUTS AGAIN.</b> All four stay lit for the last two and a half seconds,
    /// which is a third of the loop spent on a still picture and is deliberate: the frame a reader
    /// carries away has to be four open doors, not one.</para>
    /// </summary>
    private sealed class GamesDemo : EmiDemoPainter
    {
        public override string Id => "the-games";
        public override int LoopMs => 6000;

        /// <summary>The fourth door lands at 3450 ms. Anything before that freezes on a card that
        /// promises four games and shows three.</summary>
        public override double StillMs => 4400;

        private const int DoorW = 18;
        private const int DoorY = 12;
        private const int DoorH = 46;
        private const int FloorY = 58;

        public override void Draw(EmiPixelCanvas p, double t)
        {
            p.Clear(EmiPix.Navy);
            p.Rect(0, 0, p.W, 1, EmiPixelCanvas.Rgb(0x24, 0x24, 0x40));

            // The floor is what makes these doorways rather than windows. One dark plane with a
            // lit lip, and every door stands on it.
            p.Rect(0, FloorY, p.W, p.H - FloorY, EmiPix.Ink);
            p.Rect(0, FloorY, p.W, 1, EmiPix.Mid);

            for (int i = 0; i < 4; i++)
            {
                int x = 5 + i * 23;
                bool open = t > 600 + i * 950;

                if (open)
                {
                    EmiPix.PixImage(p, x, DoorY, DoorW, DoorH, i);

                    // The spill. A lit opening with no light on the floor in front of it reads as
                    // a poster on a wall; four cells of stained floor is what turns it into a way
                    // through. Alpha rather than a flat fill so the floor stays one plane.
                    p.RectA(x, FloorY + 1, DoorW, 5, EmiPix.Lav, 0.34);
                }
                else
                {
                    p.Rect(x, DoorY, DoorW, DoorH, EmiPix.Mid);
                    p.Rect(x + 1, DoorY + 1, DoorW - 2, DoorH - 2, EmiPix.Dark);
                }

                // A lintel over every door, lit or not, so the row still reads as four doorways in
                // the second before any of them opens.
                p.Rect(x - 1, DoorY - 2, DoorW + 2, 2, open ? EmiPix.Pink : EmiPix.Mid);
            }
        }
    }

    // =====================================================================================
    //  THE VELVET VAULT  -  a fogged case, a padlock, and three things already outside it
    // =====================================================================================

    /// <summary>
    /// THE SENTENCE: "one lock stands between you and that shelf - and this other shelf has no
    /// lock on it at all."
    ///
    /// <para><b>THE BOTTOM ROW IS THE REASON THIS LOOP EXISTS.</b> A padlock opening on a case is
    /// a complete little animation and it is also, on its own, an advert: it says "pay and this
    /// opens" and nothing else, which is exactly the register the card was written to avoid. The
    /// three plates below the case are outside the glass for the entire loop, lit from the first
    /// frame, and they never had a lock. That is the card's fourth nudge drawn instead of said,
    /// and it is what stops the demo from making a promise the copy then has to walk back.</para>
    ///
    /// <para><b>THE VEIL IS THE APP'S OWN.</b> A locked exclusive on the real shelf is not a dim
    /// picture, it is a full-colour card behind a fogged veil with a padlock breathing on it
    /// (<c>ExclusiveGateState.Locked</c>). So the plates are painted at full strength and an ink
    /// wash goes over the case interior on top of them - which is both truer to the surface the
    /// button leads to and, at this size, more legible than six flat toned rectangles.</para>
    ///
    /// <para>The cursor is here for the same reason it is on the flashes loop: without it the lock
    /// opens by itself, and a lock that opens by itself is a different and much less honest
    /// sentence.</para>
    /// </summary>
    private sealed class VaultDemo : EmiDemoPainter
    {
        public override string Id => "vault";
        public override int LoopMs => 5200;

        /// <summary>Open, unveiled, cursor gone. The whole loop exists to arrive here.</summary>
        public override double StillMs => 4300;

        private const int OpenMs = 3200;

        /// <summary>The six plates inside the case: three columns, two rows.</summary>
        private static readonly int[][] Shelf =
        {
            new[] { 12, 8 }, new[] { 37, 8 }, new[] { 62, 8 },
            new[] { 12, 23 }, new[] { 37, 23 }, new[] { 62, 23 },
        };

        /// <summary>The three that were never in the case. Wider and shorter than the shelf plates
        /// on purpose: same kind of thing, plainly not the same shelf.</summary>
        private static readonly int[][] Free = { new[] { 10, 51 }, new[] { 37, 51 }, new[] { 64, 51 } };

        public override void Draw(EmiPixelCanvas p, double t)
        {
            p.Clear(EmiPix.Navy);
            p.Rect(0, 0, p.W, 1, EmiPixelCanvas.Rgb(0x24, 0x24, 0x40));

            bool open = t >= OpenMs;

            // THE CASE. Its rim goes pink when the lock comes off, because the rim is the only
            // part of the case a reader can still see once the veil lifts and something has to
            // carry the change besides the plates themselves.
            p.Rect(8, 4, 80, 34, open ? EmiPix.Pink : EmiPix.Mid);
            p.Rect(9, 5, 78, 32, EmiPix.Ink);

            // Seeded by INDEX, never by coordinate. The first cut seeded on x+y, which looked
            // clever and put all six plates on seed 0 - PixImage takes the seed modulo five, and
            // every one of these coordinate sums happens to be a multiple of five. A shelf of six
            // identical goods is not a shelf.
            for (int i = 0; i < Shelf.Length; i++)
                EmiPix.PixImage(p, Shelf[i][0], Shelf[i][1], 22, 12, i);

            // The fog. Over the plates, inside the rim, and gone the instant the lock does.
            if (!open) p.RectA(9, 5, 78, 32, EmiPix.Ink, 0.62);

            // OUTSIDE THE GLASS, ALWAYS. No veil, no rim, no lock, and lit in frame one so that a
            // reader who only ever sees the still still sees them.
            for (int i = 0; i < Free.Length; i++)
                EmiPix.PixImage(p, Free[i][0], Free[i][1], 22, 15, i + 3);

            // The shelf they stand on, which is what stops them reading as three more things that
            // simply have not been locked up yet.
            p.Rect(6, 67, 84, 1, EmiPix.Mid);

            // The lock hangs OFF the case's bottom rim, shackle over the rim and body below it, so
            // it is unambiguously fastening the case and is not sitting on the free row. Drawn
            // last, over the fog: a padlock behind its own veil would be a lock on the wrong side
            // of the glass. Nudging it up to hang inside the case instead put the shackle across
            // the middle plate, which cost a whole sixth of the shelf to say the same thing.
            EmiPix.Padlock(p, 42, 33, open);

            if (t > 1600 && t < OpenMs + 500)
            {
                double f = Math.Min(1, (t - 1600) / 1400.0);
                EmiPix.Cursor(p, 22 + f * 34, 62 - f * 24);
            }
        }
    }
}
