using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// One card's demo loop.
///
/// <para>Deliberately the same shape as <see cref="EmiChannelPainter"/>: the window owns the clock
/// and the surface, the painter only knows how to fill a buffer and how to move forward. The
/// difference is that a channel painter attaches visuals once and mutates them, while a demo
/// repaints the whole 96 x 72 buffer every frame. At this size that is cheaper than tracking
/// state, and it means a painter can never leave a stale pixel behind between cards.</para>
///
/// <para><b>A loop is a sentence.</b> Every one of these runs 4 to 7 seconds and says exactly one
/// thing. If a loop needs a legend to be understood it has failed and the fix is a simpler loop,
/// not a longer nudge.</para>
/// </summary>
public abstract class EmiDemoPainter
{
    /// <summary>The card id this painter belongs to.</summary>
    public abstract string Id { get; }

    /// <summary>Loop length in ms. The window wraps <c>tMs</c> at this.</summary>
    public abstract int LoopMs { get; }

    /// <summary>Paint the frame at <paramref name="tMs"/> ms into the loop.</summary>
    public abstract void Draw(EmiPixelCanvas p, double tMs);

    /// <summary>
    /// The frame to freeze on when the user has asked the OS for reduced motion. Defaults to a bit
    /// past halfway, which for every loop in the book is its most legible moment.
    /// </summary>
    public virtual double StillMs => LoopMs * 0.55;
}

/// <summary>
/// The book's demo catalogue. The four wave A loops live below; every later batch lives in its own
/// <c>EmiBookDemos.*.cs</c> so that a wave of cards can be drawn in parallel. A card with no painter
/// draws a blank stage, which is the one failure a reader definitely notices and the log definitely
/// does not mention, so <c>EmiBookCardsTests.Every_card_has_a_demo</c> refuses the deck without one.
/// </summary>
public static class EmiBookDemos
{
    private static readonly Dictionary<string, EmiDemoPainter> Map = Build();

    private static Dictionary<string, EmiDemoPainter> Build()
    {
        var all = new List<EmiDemoPainter> { new CcpDemo(), new PanicDemo(), new FlashesDemo(), new SublimDemo() };
        all.AddRange(EmiBookDemosSetup.All);
        all.AddRange(EmiBookDemosDesk.All);
        all.AddRange(EmiBookDemosOverlays.All);
        all.AddRange(EmiBookDemosMachines.All);
        all.AddRange(EmiBookDemosControl.All);
        all.AddRange(EmiBookDemosPlaces.All);
        var d = new Dictionary<string, EmiDemoPainter>(StringComparer.Ordinal);
        foreach (var p in all) d[p.Id] = p;
        return d;
    }

    /// <summary>The painter for a card, or null when that card has no loop yet.</summary>
    public static EmiDemoPainter? For(string? cardId)
        => cardId != null && Map.TryGetValue(cardId, out var p) ? p : null;

    // =====================================================================================
    //  01  THE CCP  -  everything at once, then it all clears and rebuilds
    // =====================================================================================

    private sealed class CcpDemo : EmiDemoPainter
    {
        public override string Id => "the-ccp";
        public override int LoopMs => 6000;

        private static readonly int[][] Spots =
        {
            new[] { 6, 8, 20, 15 }, new[] { 36, 6, 22, 16 }, new[] { 14, 32, 24, 14 },
            new[] { 50, 30, 26, 18 }, new[] { 66, 10, 20, 13 },
        };

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);

            // The build. Five pictures land in sequence, the spiral turns up in the corner, and a
            // phrase blips past: this is the app with everything on, which is what a new user is
            // actually afraid of. The clear that follows is the reassurance.
            if (t < 4400)
            {
                for (int i = 0; i < Spots.Length; i++)
                    if (t > 300 + i * 560)
                        EmiPix.PixImage(p, Spots[i][0], Spots[i][1], Spots[i][2], Spots[i][3], i);

                if (t > 1300) EmiPix.Spiral(p, p.W - 14, p.H - 19, 11, t / 320.0, EmiPix.Pink);
                if (t > 2700 && t < 2860) EmiPix.Phrase(p, 20, 26, 54, 15);
            }
            else if (t > 5000)
            {
                EmiPix.PixImage(p, Spots[0][0], Spots[0][1], Spots[0][2], Spots[0][3], 0);
            }
        }
    }

    // =====================================================================================
    //  02  THE PANIC KEY  -  the cut is the lesson
    // =====================================================================================

    private sealed class PanicDemo : EmiDemoPainter
    {
        public override string Id => "the-panic-key";
        public override int LoopMs => 5000;

        /// <summary>
        /// Freeze just after the cut, while the key is still down. The empty screen IS the point of
        /// the card, but an empty stage on its own reads as a demo that failed to load - so the
        /// still keeps the pressed key in frame, which is what makes the emptiness a consequence.
        /// </summary>
        public override double StillMs => 3400;

        private static readonly int[][] Spots =
        {
            new[] { 8, 6, 22, 16 }, new[] { 40, 10, 24, 15 },
            new[] { 18, 34, 26, 15 }, new[] { 56, 32, 24, 16 },
        };

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);

            // ONE FRAME between full and empty. Not a fade: a fade would say "it winds down", and
            // the key does not wind down. Everything above 3260 ms simply is not drawn.
            bool dead = t >= 3260;
            if (!dead)
            {
                for (int i = 0; i < Spots.Length; i++)
                    if (t > 200 + i * 480)
                        EmiPix.PixImage(p, Spots[i][0], Spots[i][1], Spots[i][2], Spots[i][3], i + 2);

                if (t > 1400) EmiPix.Spiral(p, 20, 38, 10, t / 300.0, EmiPix.Pink);
                if (t > 2000 && t < 2100) EmiPix.Phrase(p, 22, 24, 50, 14);
            }

            if (t > 2500 && t < 3600) EmiPix.Keycap(p, p.W / 2 - 9, p.H - 24, t > 3100);

            // A single dull flash on the frame it dies, so the cut registers as an event.
            if (t >= 3260 && t < 3330) p.Rect(0, 0, p.W, p.H - 6, EmiPix.Mid);
        }
    }

    // =====================================================================================
    //  03  FLASHES  -  and the hydra wink
    // =====================================================================================

    private sealed class FlashesDemo : EmiDemoPainter
    {
        public override string Id => "flashes";
        public override int LoopMs => 5000;

        private static readonly int[][] Spots =
        {
            new[] { 8, 8, 24, 17 }, new[] { 52, 6, 26, 18 },
            new[] { 16, 34, 22, 15 }, new[] { 58, 32, 24, 16 },
        };

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);

            for (int i = 0; i < Spots.Length; i++)
            {
                double lt = (t - i * 620 + LoopMs) % LoopMs;
                if (lt >= 2600) continue;
                double a = lt < 200 ? lt / 200.0 : (lt > 2100 ? (2600 - lt) / 500.0 : 1.0);

                // The one the cursor is about to click stops being drawn whole at the click.
                if (i == 1 && t > 3300) continue;
                EmiPix.PixImage(p, Spots[i][0], Spots[i][1], Spots[i][2], Spots[i][3], i, a);
            }

            // THE HYDRA. Clicking a flash does not dismiss it, it splits it. The card says so in
            // words too, because a user who discovers this by accident reads it as a bug.
            if (t > 3300)
            {
                EmiPix.PixImage(p, 52, 6, 13, 9, 4);
                EmiPix.PixImage(p, 66, 12, 13, 9, 5);
            }

            if (t > 2500 && t < 4200)
            {
                double f = Math.Min(1, (t - 2500) / 900.0);
                EmiPix.Cursor(p, 10 + f * 52, 44 - f * 30);
            }
        }
    }

    // =====================================================================================
    //  04  SUBLIMINALS  -  two frames, and a long honest gap
    // =====================================================================================

    private sealed class SublimDemo : EmiDemoPainter
    {
        public override string Id => "subliminals";
        public override int LoopMs => 5000;

        /// <summary>The one frame worth freezing on is the phrase itself.</summary>
        public override double StillMs => 2050;

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);

            // A hundred milliseconds on, then nothing for nearly five seconds. The empty stretch is
            // the demo: it is what the feature actually looks like almost all of the time, and a
            // loop that showed the phrase more often would be selling something the app does not do.
            if (t > 2000 && t < 2100) EmiPix.Phrase(p, 20, 26, 56, 16);
            else if (t >= 2100 && t < 2200) p.Rect(20, 26, 56, 16, EmiPix.Mid);
        }
    }
}
