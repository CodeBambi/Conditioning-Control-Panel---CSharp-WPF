using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BOOK DEMO BATCH: the big machines. The loops for the cards in
/// <see cref="EmiBookDeckMachines"/>, one painter per card id.
///
/// <para>A loop is a sentence: 4 to 7 seconds, one idea, legible with the sound off and the words
/// unread. If a loop needs its nudge to be understood then the loop is wrong. See
/// <see cref="EmiDemoPainter"/> for the contract and <see cref="EmiPix"/> for the vocabulary.</para>
///
/// <para><b>All three of these are about TIME</b>, which is the one thing the four wave A loops did
/// not have to say. A corner overlay is defined by outlasting whatever else is on screen, a Deeper
/// timeline is literally a clock with your work nailed to it, and a chat is a turn you take followed
/// by a turn she takes. So each loop below spends its length on a before and an after rather than on
/// a state, and each one's <c>StillMs</c> deliberately lands on the AFTER - the frame where the
/// consequence is visible - because a reduced-motion reader gets exactly one frame and a frame of
/// "nothing has happened yet" is a blank stage with extra steps.</para>
/// </summary>
internal static class EmiBookDemosMachines
{
    /// <summary>This batch's painters.</summary>
    public static readonly EmiDemoPainter[] All =
    {
        new CornerGifsDemo(), new DeeperDemo(), new CompanionDemo(),
    };

    // =====================================================================================
    //  CORNER GIFS  -  the work changes, the corner does not
    // =====================================================================================

    private sealed class CornerGifsDemo : EmiDemoPainter
    {
        public override string Id => "corner-gifs";
        public override int LoopMs => 5200;

        /// <summary>
        /// Late, with both slots up and the cursor already past the first one. "Always on" is
        /// not a property that can be drawn in a single frame - only outlasting something can
        /// show it - so the still instead carries the two facts that CAN survive a freeze: there
        /// are two of these, and the pointer is sitting on top of one of them unbothered.
        /// </summary>
        public override double StillMs => 4300;

        /// <summary>
        /// Three PixImage compositions for the one window in the middle. Different seeds, not a
        /// shifted dither: the whole sentence is "this changed and that did not", so the three
        /// have to be plainly three pictures rather than three tiles of the same wallpaper.
        /// </summary>
        private static readonly int[] Swaps = { 0, 2, 3 };

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);

            // The thing you were actually doing. It gets a title bar so it reads as somebody
            // else's window rather than as a third overlay - the corner GIFs are the only
            // things in this frame the app put there.
            p.Rect(24, 8, 50, 40, EmiPix.Mid);
            p.Rect(24, 8, 50, 4, EmiPix.Ink);
            int swap = (int)(t / 1500.0);
            if (swap > 2) swap = 2;
            EmiPix.PixImage(p, 26, 14, 46, 32, Swaps[swap]);

            // SLOT 1. Up on frame zero, still up on the last frame, turning the whole time. A
            // spiral rather than a PixImage because the card's word is "looping": a still
            // picture in a corner is a sticker, and the difference between a sticker and this
            // is the only thing the reader has to take away.
            EmiPix.Spiral(p, 11, 52, 9, t / 340.0, EmiPix.Pink);

            // SLOT 2, arriving late and in the opposite corner - which is exactly what the
            // config window's own defaults do (CornerGifWindow.EnsureSlots seeds slot 2 to the
            // opposite bottom corner). Late so that "there are two" reads as a choice the user
            // made rather than as decoration that was always there.
            if (t > 3200) EmiPix.Spiral(p, 85, 52, 9, t / 340.0, EmiPix.Pink);

            // CLICK-THROUGH. The cursor walks off the window and straight over slot 1, and slot
            // 1 does not react - no highlight, no dismissal, no split (which is precisely what
            // the FLASHES loop does do on a click, two cards earlier, so the contrast is
            // already in the reader's hand).
            if (t > 1200 && t < 3600)
            {
                double f = Math.Min(1, (t - 1200) / 1700.0);
                EmiPix.Cursor(p, 44 - f * 36, 30 + f * 20);
            }
        }
    }

    // =====================================================================================
    //  DEEPER  -  a playhead, and the things you nailed to it
    // =====================================================================================

    private sealed class DeeperDemo : EmiDemoPainter
    {
        public override string Id => "deeper";
        public override int LoopMs => 6000;

        /// <summary>
        /// The middle mark, mid-fire. The playhead is halfway, one gold mark is behind it, one
        /// is ahead, and the effect belonging to the mark under the head is on screen - which is
        /// the whole grammar of the feature in one frame: marks cause effects, in order, on a
        /// clock. Freezing earlier would show an empty track, and later would show a wash with
        /// no visible cause.
        /// </summary>
        public override double StillMs => 3100;

        /// <summary>
        /// Where the authored items sit, as a fraction of the track. Uneven on purpose: evenly
        /// spaced marks read as a metronome, and the point is that a person chose these moments.
        /// </summary>
        private static readonly double[] Marks = { 0.22, 0.50, 0.78 };

        private const int TrackX = 6;
        private const int TrackW = 84;

        public override void Draw(EmiPixelCanvas p, double t)
        {
            // No taskbar: this stage is an EDITOR, not the desktop. Every other loop in the
            // book is something happening over your work; this one is you doing the work.
            EmiPix.Desk(p, false);

            double f = t / LoopMs;

            // The media pane. Seed 1 is the kit's figure composition - the one that reads as
            // "somebody's video" rather than as an abstract, which matters here because the
            // card's first word is "any" and an abstract would look like shipped content.
            EmiPix.PixImage(p, TrackX, 4, TrackW, 36, 1);

            // THE EFFECTS, each one owned by the mark the head is passing. They are drawn over
            // the media, never beside it, because the feature's claim is that the effects land
            // on top of the thing you are watching and not in some separate pane.
            if (t > 1320 && t < 1980)
                EmiPix.PixImage(p, 30, 10, 28, 22, 3);           // a flash
            else if (t > 3000 && t < 3620)
                EmiPix.Phrase(p, 18, 12, 58, 18);                // a subliminal
            else if (t > 4680 && t < 5400)
                p.RectA(TrackX, 4, TrackW, 36, EmiPix.Pink, 0.5); // an overlay wash

            // THE ITEM LANE: what you authored, sitting still. Gold because gold is the book's
            // colour for the thing that was placed rather than the thing that is running.
            p.Rect(TrackX, 43, TrackW, 6, EmiPix.Ink);
            foreach (double m in Marks)
                p.Rect(TrackX + TrackW * m - 3, 44, 6, 4, EmiPix.Gold);

            // THE TRANSPORT: what is running. Two rows rather than one so the reader can see
            // that the marks do not move and the fill does - a single row carrying both would
            // read as a loading bar with decorations on it.
            EmiPix.Bar(p, TrackX, 52, TrackW, 7, f, EmiPix.Pink);

            // The playhead ties the two rows together. It is the only cream thing on the stage,
            // so the eye follows it, which is the reading order the loop needs: head reaches
            // mark, effect appears.
            p.Rect(Math.Round(TrackX + TrackW * f), 41, 1, 20, EmiPix.Cream);
        }
    }

    // =====================================================================================
    //  THE COMPANION  -  your turn, then hers
    // =====================================================================================

    private sealed class CompanionDemo : EmiDemoPainter
    {
        public override string Id => "companion";
        public override int LoopMs => 5600;

        /// <summary>Her reply, on screen, with the tube it came from still in frame.</summary>
        public override double StillMs => 3300;

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);

            // She perks up one beat BEFORE the bubble, not with it. A reply that appears out of
            // a static portrait reads as a notification; a portrait that moves first and then
            // speaks reads as somebody answering. That single beat is the card's word "reacts".
            if (t > 2260 && t < 2460) p.Rect(66, 10, 28, 48, EmiPix.Pink);

            // Her tube, hard right, drawn after the flare so it sits inside it. Seed 1 is the
            // kit's head-and-shoulders composition; nothing bespoke, and no attempt at a face -
            // at 24 cells wide a face is four grey pixels and an apology.
            EmiPix.PixImage(p, 68, 12, 24, 44, 1);

            // YOUR TURN. The words build one at a time so the reader sees a person typing
            // rather than a caption appearing. Five blocks is a sentence's worth at this scale.
            if (t > 600 && t < 2200)
            {
                p.Rect(6, 50, 54, 11, EmiPix.Cream);
                int words = (int)Math.Min(5, (t - 600) / 280.0);
                for (int i = 0; i < words; i++) p.Rect(9 + i * 9, 54, 7, 3, EmiPix.Ink);
            }

            // HER TURN. The gap between 2200 and 2500 is the send: the box empties before the
            // reply lands, so the two turns never share a frame and the exchange has an order.
            if (t > 2500 && t < 4900)
            {
                EmiPix.Phrase(p, 6, 16, 56, 18);
                p.Rect(62, 24, 5, 2, EmiPix.Cream);   // the tail, pointing back at the tube
            }
        }
    }
}
