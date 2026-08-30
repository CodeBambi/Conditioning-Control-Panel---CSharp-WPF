using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BOOK DEMO BATCH: the effects that paint over the screen. The loops for the cards in
/// <see cref="EmiBookDeckOverlays"/>, one painter per card id.
///
/// <para>A loop is a sentence: 4 to 7 seconds, one idea, legible with the sound off and the words
/// unread. If a loop needs its nudge to be understood then the loop is wrong. See
/// <see cref="EmiDemoPainter"/> for the contract and <see cref="EmiPix"/> for the vocabulary.</para>
///
/// <para><b>These three are the visual cards, so the stage does the explaining.</b> All three open
/// on <see cref="EmiPix.Desk"/> with the reader's pictures on it and then do something TO that
/// desk, because every one of these features is a thing that happens over your ordinary screen
/// rather than a window you go and look at. Nothing here invents geometry the props kit already
/// has, with two exceptions that are called out where they are defined: a mosaic (the only way to
/// say "blur" at 96 x 72) and a bubble ring (there is no circle in the kit).</para>
/// </summary>
internal static class EmiBookDemosOverlays
{
    /// <summary>This batch's painters.</summary>
    public static readonly EmiDemoPainter[] All =
    {
        new SpiralDemo(), new OverlaysDemo(), new BubblesDemo(),
    };

    // =====================================================================================
    //  THE SPIRAL  -  it grows until it runs off the edges, and it never stops turning
    // =====================================================================================

    private sealed class SpiralDemo : EmiDemoPainter
    {
        public override string Id => "spiral";
        public override int LoopMs => 6000;

        /// <summary>
        /// Full size, mid-turn. The growth is the only part of this loop that is not true of the
        /// real overlay - the spiral comes up at its final size - so the still deliberately lands
        /// after the growth is over rather than during it.
        /// </summary>
        public override double StillMs => 3800;

        private static readonly int[][] Spots =
        {
            new[] { 6, 8, 22, 16 }, new[] { 58, 10, 26, 17 }, new[] { 20, 36, 28, 15 },
        };

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);

            // The desk stays crisp under the arms for the whole loop. That is the point of the
            // opacity dial and it is the difference between this card and SCREEN EFFECTS: the
            // spiral is drawn ON your desktop, it does not replace it.
            for (int i = 0; i < Spots.Length; i++)
                EmiPix.PixImage(p, Spots[i][0], Spots[i][1], Spots[i][2], Spots[i][3], i);

            int cx = p.W / 2, cy = (p.H - 6) / 2;
            double r = 5 + Math.Min(1.0, t / 1500.0) * 61;   // 66 > half the diagonal, so it clips
            double ang = t / 340.0;

            // FOUR PASSES, not one. EmiPix.Spiral steps its parameter by a fixed 0.09 rad, which
            // at the radius this loop needs leaves roughly six cells of daylight between plotted
            // points on the outermost arm - a dotted line, not a spiral. Four copies spaced
            // 0.0225 apart sum to exactly the prop's own step and close the arm completely.
            for (int k = 0; k < 4; k++)
                EmiPix.Spiral(p, cx, cy, r, ang - 0.30 + k * 0.0225, EmiPix.Lav);
            for (int k = 0; k < 4; k++)
                EmiPix.Spiral(p, cx, cy, r, ang + k * 0.0225, EmiPix.Pink);
        }
    }

    // =====================================================================================
    //  SCREEN EFFECTS  -  wash, then blur, then a thing you can only hear
    // =====================================================================================

    private sealed class OverlaysDemo : EmiDemoPainter
    {
        public override string Id => "overlays";
        public override int LoopMs => 6500;

        /// <summary>Everything on at once: wash, blur and the meter still moving. The card's whole
        /// claim is that these three stack, and this is the only frame that shows all three.</summary>
        public override double StillMs => 5600;

        private const int Block = 6;

        private static readonly int[][] Spots =
        {
            new[] { 8, 8, 24, 17 }, new[] { 54, 10, 28, 18 }, new[] { 22, 36, 30, 16 },
        };

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);
            for (int i = 0; i < Spots.Length; i++)
                EmiPix.PixImage(p, Spots[i][0], Spots[i][1], Spots[i][2], Spots[i][3], i);

            // BEAT 2 IS PAINTED BEFORE BEAT 1. The blur lands under the wash on purpose: in the
            // app the pink tint is a separate surface over the blurred desktop, so a mosaic drawn
            // on top of the wash would wash the pink out and quietly contradict the "they stack"
            // bullet. Time order is still wash first - the gates below say so.
            if (t > 3100) Mosaic(p, Math.Min(1.0, (t - 3100) / 800.0) * 0.85);

            // BEAT 1, the wash, and it NEVER LEAVES. A curtain rather than a fade, so a frame
            // caught halfway still reads as "something came down over this", and it holds for the
            // rest of the loop because that is what an overlay does: it stays until you stop it.
            if (t > 1500)
                p.RectA(0, 0, p.W, (p.H - 6) * Math.Min(1.0, (t - 1500) / 700.0), EmiPix.Pink, 0.34);

            // BEAT 3, the wipe. NOTHING ON THE GLASS CHANGES. A meter moves at the foot of the
            // screen and the picture does not, which is the entire third bullet drawn: Mind Wipe
            // is an audio scheduler and a reader who expects a third overlay from it gets none.
            if (t > 4900) EmiPix.Wave(p, 33, p.H - 22, 30, 14, t, 1.0, EmiPix.Cream, 6);
        }

        /// <summary>
        /// THE BLUR, and the one prop this batch had to invent.
        ///
        /// <para>A gaussian is not available at 96 x 72 - there are not enough cells for a kernel
        /// to have anywhere to put the light it moves. The pixel-art idiom that reads as blur is
        /// coarse blocks plus a dropped palette, so each 6 x 6 cell takes ONE tone and the five
        /// composition motifs underneath collapse into a two-tone dither.</para>
        ///
        /// <para><b>The bleed is what sells it.</b> Each picture's rectangle is grown by one whole
        /// block on every side before the hit test, so colour ends up outside the edge it came
        /// from. Without that this is a mosaic filter - a thing that squares up an image but keeps
        /// it exactly where it was - and a mosaic does not look like a blur, it looks like
        /// censorship.</para>
        /// </summary>
        private static void Mosaic(EmiPixelCanvas p, double alpha)
        {
            for (int by = 0; by < p.H - Block; by += Block)
            {
                for (int bx = 0; bx < p.W; bx += Block)
                {
                    int mx = bx + Block / 2, my = by + Block / 2;
                    uint tone = EmiPix.Navy;

                    foreach (var s in Spots)
                    {
                        if (mx < s[0] - Block || mx >= s[0] + s[2] + Block) continue;
                        if (my < s[1] - Block || my >= s[1] + s[3] + Block) continue;

                        bool inside = mx >= s[0] && mx < s[0] + s[2] && my >= s[1] && my < s[1] + s[3];
                        // Inside, the two tones alternate per block: the picture is still THERE,
                        // it just has no detail left. The bled ring outside is the duller of the
                        // two, so the shape keeps a soft edge instead of a second hard one.
                        tone = inside ? (((bx + by) / Block) % 2 == 0 ? EmiPix.Lav : EmiPix.Mid) : EmiPix.Mid;
                    }

                    p.RectA(bx, by, Block, Block, tone, alpha);
                }
            }
        }
    }

    // =====================================================================================
    //  BUBBLES  -  they rise, you reach for one, it goes
    // =====================================================================================

    private sealed class BubblesDemo : EmiDemoPainter
    {
        public override string Id => "bubbles";
        public override int LoopMs => 5500;

        /// <summary>The last frame before the click: the hero bubble high on the glass with the
        /// cursor already on it. The pop is 260 ms long and freezing there would show a cream ring
        /// with nothing left on screen to say what it used to be.</summary>
        public override double StillMs => 2900;

        /// <summary>x, radius, launch offset. The ambience: six of them, staggered so that three
        /// or four are always on the glass and the stage is never empty.</summary>
        private static readonly double[][] Drift =
        {
            new[] { 12.0, 6.0,    0.0 },
            new[] { 30.0, 4.0,  900.0 },
            new[] { 86.0, 7.0,  450.0 },
            new[] { 70.0, 5.0, 1800.0 },
            new[] { 42.0, 6.0, 2600.0 },
            new[] { 20.0, 4.0, 3900.0 },
        };

        private const double DriftMs = 3400;

        // The hero: one bubble on a fixed timeline rather than the modular one above, because the
        // cursor has to meet it and a bubble whose position wraps is a bubble the cursor misses.
        private const double HeroFrom = 600, HeroTo = 3000;
        private const double HeroX = 58, HeroTopY = 26;
        private const double PopMs = 3000;

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);

            foreach (var b in Drift)
            {
                double lt = (t - b[2] + LoopMs) % LoopMs;
                if (lt > DriftMs) continue;
                double f = lt / DriftMs;
                // Off the bottom to off the top: a bubble that appeared and vanished inside the
                // frame would read as a flash, and the feature is the drift.
                double cy = (p.H + b[1]) - f * (p.H + b[1] * 2 + 4);
                Ring(p, b[0] + Math.Sin(lt / 380.0) * 2.5, cy, b[1], EmiPix.Pink);
            }

            if (t > HeroFrom && t < PopMs)
            {
                double hf = Math.Min(1.0, (t - HeroFrom) / (HeroTo - HeroFrom));
                Ring(p, HeroX, (p.H + 8) - hf * (p.H + 8 - HeroTopY), 8, EmiPix.Pink);
            }

            // THE POP. A cream ring that blows outward for a fifth of a second and four cells
            // thrown clear of it - not a fade. A bubble that faded would read as one that drifted
            // off, and the whole card turns on the difference between those two endings.
            if (t >= PopMs && t < PopMs + 260)
            {
                double f = (t - PopMs) / 260.0;
                Ring(p, HeroX, HeroTopY, 8 + f * 8, EmiPix.Cream, highlight: false);
                for (int k = 0; k < 4; k++)
                {
                    double a = k * Math.PI / 2 + 0.6;
                    p.Px((int)Math.Round(HeroX + Math.Cos(a) * (10 + f * 12)),
                         (int)Math.Round(HeroTopY + Math.Sin(a) * (10 + f * 12)), EmiPix.Cream);
                }
            }

            // The XP that pop paid for, floating off the way a score does. Gold, because gold is
            // what the kit already uses for a reward everywhere else in the book.
            if (t >= PopMs + 120 && t < PopMs + 1200)
            {
                double f = (t - PopMs - 120) / 1080.0;
                double y = HeroTopY - 4 - f * 14;
                p.Rect(HeroX - 3, y, 7, 1, EmiPix.Gold);
                p.Rect(HeroX, y - 3, 1, 7, EmiPix.Gold);
            }

            // The reach. Starts well before the bubble arrives so the two meet rather than the
            // cursor teleporting onto a target that is already there.
            if (t > 1500 && t < PopMs + 500)
            {
                double f = Math.Min(1.0, (t - 1500) / 1500.0);
                // Aimed at where the bubble WILL BE, not at where it is: tracking the live hy
                // made the arrow twitch every frame the bubble climbed past it, and after the pop
                // hy is parked off-screen, which would have snapped the cursor away mid-gesture.
                EmiPix.Cursor(p, 14 + f * (HeroX - 16), 56 + f * (HeroTopY + 4 - 56));
            }
        }

        /// <summary>
        /// A bubble. The props kit has no circle - every one of its ten props is axis-aligned -
        /// so this is the second and last thing this batch draws for itself.
        ///
        /// <para>The angular step is <c>0.8 / r</c> rather than a constant: a fixed step leaves a
        /// dotted outline on the big bubbles and burns hundreds of overlapping writes on the small
        /// ones. Two cream cells at the upper left are the whole reason it reads as glass rather
        /// than as a ring.</para>
        /// </summary>
        private static void Ring(EmiPixelCanvas p, double cx, double cy, double r, uint col, bool highlight = true)
        {
            double step = 0.8 / Math.Max(2.0, r);
            for (double a = 0; a < Math.PI * 2; a += step)
                p.Px((int)Math.Round(cx + Math.Cos(a) * r), (int)Math.Round(cy + Math.Sin(a) * r), col);

            if (!highlight) return;
            p.Px((int)Math.Round(cx - r * 0.48), (int)Math.Round(cy - r * 0.42), EmiPix.Cream);
            p.Px((int)Math.Round(cx - r * 0.16), (int)Math.Round(cy - r * 0.62), EmiPix.Cream);
        }
    }
}
