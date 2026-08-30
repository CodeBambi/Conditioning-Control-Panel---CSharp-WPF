using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BOOK DEMO BATCH: the ones that take control. The loops for the cards in
/// <see cref="EmiBookDeckControl"/>, one painter per card id.
///
/// <para>A loop is a sentence: 4 to 7 seconds, one idea, legible with the sound off and the words
/// unread. If a loop needs its nudge to be understood then the loop is wrong. See
/// <see cref="EmiDemoPainter"/> for the contract and <see cref="EmiPix"/> for the vocabulary.</para>
///
/// <para><b>These three are drawn straight, and that is a decision.</b> The cards they sit on are
/// the ones where somebody is handing over control, so a loop that leered would be the panel
/// undercutting the copy beside it - the reader is being asked to consent to something and the
/// picture has to be evidence, not atmosphere. So: no strobe, no lunge, nothing that arrives at the
/// viewer. Each loop shows a mechanism doing its ordinary job, and the unsettling part is left to be
/// the mechanism itself. The awareness scan is a line that keeps coming back; the lockdown is a shut
/// padlock and a bar that only goes one way; the takeover is a cursor moving with nobody on it.</para>
/// </summary>
internal static class EmiBookDemosControl
{
    /// <summary>This batch's painters.</summary>
    public static readonly EmiDemoPainter[] All =
    {
        new AwarenessDemo(), new LockdownDemo(), new TakeoverDemo(),
    };

    // =====================================================================================
    //  THE AWARENESS ENGINE  -  the scan comes back, and this time a word matches
    // =====================================================================================

    /// <summary>
    /// A page of text sits on the desk. A scan line crosses it, finds nothing, and goes away. Then
    /// it comes back, and on the second pass one word lights up and an effect lands.
    ///
    /// <para><b>Two passes, not one.</b> A single sweep would say "it looked at your screen once".
    /// The feature's whole character is that it looks again every few seconds forever, and the only
    /// way to draw "again" is to actually do it twice inside the loop - the first pass exists purely
    /// so the second one is a repeat rather than an event.</para>
    ///
    /// <para><b>The word is boxed, never re-drawn.</b> The highlight is a rectangle over the block
    /// word the <see cref="EmiPix.Phrase"/> prop already put there, so the loop never has to render
    /// legible text: the book ships no trigger vocabulary, on this card least of all. Its lifetime
    /// is the real <c>KeywordHighlightDurationMs</c> default of 1500 ms, which is also long enough
    /// to be read at 3x scale.</para>
    /// </summary>
    private sealed class AwarenessDemo : EmiDemoPainter
    {
        public override string Id => "awareness";
        public override int LoopMs => 6000;

        /// <summary>Both halves of the sentence are on screen here: the box is still up (2800-4300)
        /// and the effect it fired has landed (3600-4800). Either one alone reads as half a demo.
        /// </summary>
        public override double StillMs => 4200;

        // The page. Phrase() lays its first row of block words at y + 4, starting at x + 4, with
        // widths 5/7/4/6 and a 3-cell gap - so the second word of the top row occupies x 22..28 at
        // y 18. The box below is that rectangle plus a one-cell margin. Hard-coded rather than
        // computed because Phrase's layout is fixed and a helper would only hide the coupling.
        private const int PageX = 10, PageY = 14, PageW = 58, PageH = 20;
        private const int HitX = 21, HitY = 17, HitW = 9, HitH = 5;

        private const int PassAStart = 200, PassBStart = 2400, PassMs = 1500;
        private const int BoxOn = 2800, BoxOff = 4300;
        private const int FxOn = 3600, FxOff = 4800;

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);
            EmiPix.Phrase(p, PageX, PageY, PageW, PageH);

            Sweep(p, t - PassAStart);
            Sweep(p, t - PassBStart);

            // The highlight box: a wash plus a one-cell frame, in the app's own default highlight
            // colour (KeywordHighlightColor is #FF69B4, which is EmiPix.Pink).
            if (t >= BoxOn && t < BoxOff)
            {
                p.RectA(HitX, HitY, HitW, HitH, EmiPix.Pink, 0.35);
                p.Rect(HitX, HitY, HitW, 1, EmiPix.Pink);
                p.Rect(HitX, HitY + HitH - 1, HitW, 1, EmiPix.Pink);
                p.Rect(HitX, HitY, 1, HitH, EmiPix.Pink);
                p.Rect(HitX + HitW - 1, HitY, 1, HitH, EmiPix.Pink);
            }

            // What the hit costs you. One picture, off to the side so it never covers the box that
            // caused it - cause and effect have to be in frame together or the loop is two demos.
            if (t >= FxOn && t < FxOff)
            {
                double a = t < FxOn + 200 ? (t - FxOn) / 200.0
                         : t > FxOff - 400 ? (FxOff - t) / 400.0 : 1.0;
                EmiPix.PixImage(p, 60, 40, 30, 22, 1, a);
            }
        }

        /// <summary>One downward pass of the scan line. <paramref name="lt"/> is ms into the pass;
        /// anything outside the window draws nothing, so the caller can offset freely.</summary>
        private static void Sweep(EmiPixelCanvas p, double lt)
        {
            if (lt < 0 || lt >= PassMs) return;
            double f = lt / PassMs;
            int y = (int)Math.Round(2 + f * (p.H - 12));
            p.Rect(0, y, p.W, 1, EmiPix.Lav);
            p.RectA(0, y + 1, p.W, 2, EmiPix.Lav, 0.30);
        }
    }

    // =====================================================================================
    //  LOCKDOWN  -  the padlock never opens and the bar only goes one way
    // =====================================================================================

    /// <summary>
    /// A window with a closed padlock on it. The bar under the lock drains for the whole loop. A
    /// cursor walks up to the close button, presses it twice, and nothing at all happens.
    ///
    /// <para><b>Nothing happening is the content.</b> Every other loop in the book pays off a click;
    /// this one deliberately does not, because that is the feature. The two presses are drawn as a
    /// brief colour change on the button and nowhere else - no shake, no alarm, no ember. A loop
    /// that punished the click would be selling the haunt, and the haunt is a separate nudge; this
    /// panel only has to establish that the door does not answer.</para>
    ///
    /// <para><b>The shackle is drawn down on every single frame</b>, and <see cref="EmiPix.Padlock"/>
    /// can open. That is the point of using a prop that can: a viewer who has seen the padlock lift
    /// on another card reads this one as a lock that chose not to.</para>
    /// </summary>
    private sealed class LockdownDemo : EmiDemoPainter
    {
        public override string Id => "lockdown";
        public override int LoopMs => 6000;

        /// <summary>Mid-press: cursor parked on the close button, lock shut, bar around half. The
        /// frozen frame has to contain the refusal, and the refusal is only legible while somebody
        /// is actually pressing.</summary>
        public override double StillMs => 3300;

        private const int WinX = 12, WinY = 8, WinW = 72, WinH = 44;
        private const int XBoxX = 74, XBoxY = 10, XBoxW = 8, XBoxH = 5;

        private const int WalkStart = 1200, WalkMs = 1400;
        private const int LeaveStart = 4200, LeaveMs = 1200;

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);

            // The window: frame, well, title strip.
            p.Rect(WinX, WinY, WinW, WinH, EmiPix.Mid);
            p.Rect(WinX + 1, WinY + 8, WinW - 2, WinH - 9, EmiPix.Ink);

            // The close button. It goes pink on a press and back again, which is the only thing on
            // this stage that a click is allowed to change.
            bool pressed = (t >= 2900 && t < 3040) || (t >= 3400 && t < 3540);
            p.Rect(XBoxX, XBoxY, XBoxW, XBoxH, pressed ? EmiPix.Pink : EmiPix.Ink);
            p.Line(XBoxX + 2, XBoxY + 1, XBoxX + XBoxW - 3, XBoxY + XBoxH - 2, EmiPix.Cream);
            p.Line(XBoxX + XBoxW - 3, XBoxY + 1, XBoxX + 2, XBoxY + XBoxH - 2, EmiPix.Cream);

            // Shut, all the way through. Never call this with open: true.
            EmiPix.Padlock(p, 42, 22, false);

            // The one thing that moves. Gold because Gold is the book's lock-and-ramp colour, and
            // because a pink bar here would read as an effect rather than as a clock.
            EmiPix.Bar(p, 18, 44, 60, 5, 1.0 - t / LoopMs, EmiPix.Gold);

            DrawCursor(p, t);
        }

        /// <summary>Up to the button, a pause on it while the presses land, then away again. The
        /// pause is what makes the two presses read as one person trying twice rather than as two
        /// unrelated blips.</summary>
        private static void DrawCursor(EmiPixelCanvas p, double t)
        {
            const double HomeX = 26, HomeY = 38, ButtonX = 73, ButtonY = 11;

            if (t < WalkStart) { EmiPix.Cursor(p, HomeX, HomeY); return; }

            if (t < WalkStart + WalkMs)
            {
                double f = (t - WalkStart) / WalkMs;
                EmiPix.Cursor(p, HomeX + (ButtonX - HomeX) * f, HomeY + (ButtonY - HomeY) * f);
                return;
            }

            if (t < LeaveStart) { EmiPix.Cursor(p, ButtonX, ButtonY); return; }

            double g = Math.Min(1, (t - LeaveStart) / LeaveMs);
            EmiPix.Cursor(p, ButtonX + (HomeX - ButtonX) * g, ButtonY + (HomeY - ButtonY) * g);
        }
    }

    // =====================================================================================
    //  TAKEOVER  -  a cursor with nobody on it
    // =====================================================================================

    /// <summary>
    /// The countdown bar at the top empties, the cursor moves to a spot on its own, and something
    /// lands there. Three times, at three different places, and the bar refills after each one.
    ///
    /// <para><b>The bar is the whole argument.</b> A cursor that moves by itself is ambiguous at 96
    /// pixels wide - it could just as easily be a demo of a cursor. Pairing every arrival with a
    /// meter that has just run out says the moves are being SCHEDULED, which is what separates this
    /// card from every other loop in the book that also has a cursor in it. It is drawn from the
    /// real feature too: <c>ShowTakeoverCountdownBar</c> puts exactly this bar under the avatar,
    /// draining toward the next random action.</para>
    ///
    /// <para><b>Nothing is ever clicked ON.</b> The cursor never lands on a control, it lands on
    /// empty desk and a picture appears - so the loop cannot be misread as somebody being shown how
    /// to use the app. The effects arrive where the cursor stops, which is the app acting, not
    /// somebody driving.</para>
    /// </summary>
    private sealed class TakeoverDemo : EmiDemoPainter
    {
        public override string Id => "takeover";
        public override int LoopMs => 5600;

        /// <summary>Just after the third arrival: all three pictures up, the cursor still on the
        /// last one, the bar at the bottom of its run. The one frame that contains the whole
        /// pattern rather than a moment of it.</summary>
        public override double StillMs => 3450;

        /// <summary>x, y, w, h of each thing the schedule drops, in the order it drops them. The
        /// cursor's tip parks at the picture's top-left corner, so these double as waypoints.</summary>
        private static readonly int[][] Drops =
        {
            new[] { 28, 12, 26, 18 },
            new[] { 60, 30, 26, 18 },
            new[] { 14, 34, 24, 16 },
        };

        /// <summary>When each drop lands. The gaps are uneven on purpose: the real interval is an
        /// average with a roll on it, and three evenly spaced beats would draw a metronome.</summary>
        private static readonly double[] At = { 1000, 2200, 3400 };

        private const double StartX = 10, StartY = 50;

        public override void Draw(EmiPixelCanvas p, double t)
        {
            EmiPix.Desk(p);
            EmiPix.Bar(p, 4, 3, p.W - 8, 4, Charge(t), EmiPix.Pink);

            for (int i = 0; i < Drops.Length; i++)
            {
                if (t < At[i]) continue;
                var d = Drops[i];
                double a = t < At[i] + 180 ? (t - At[i]) / 180.0 : 1.0;
                EmiPix.PixImage(p, d[0], d[1], d[2], d[3], i + 1, a);
            }

            var (cx, cy) = CursorAt(t);
            EmiPix.Cursor(p, cx, cy);
        }

        /// <summary>How full the countdown bar is: it drains across the gap before each drop and
        /// snaps back to full the instant one lands. After the last drop it keeps draining toward
        /// the loop's end, so the wrap looks like a fourth action rather than a reset.</summary>
        private static double Charge(double t)
        {
            double from = 0, to = At[0];
            for (int i = 0; i < At.Length; i++)
            {
                if (t < At[i]) { to = At[i]; break; }
                from = At[i];
                to = i + 1 < At.Length ? At[i + 1] : LoopMsConst;
            }
            double span = Math.Max(1, to - from);
            return Math.Max(0, Math.Min(1, 1.0 - (t - from) / span));
        }

        /// <summary>Kept as a constant so <see cref="Charge"/> stays static and testable; it must
        /// equal <see cref="LoopMs"/>.</summary>
        private const double LoopMsConst = 5600;

        /// <summary>Where the arrow is. It flies for the first 700 ms of each gap and then waits on
        /// the spot for the drop, which is what makes the arrival look intended rather than lucky.
        /// </summary>
        private static (double x, double y) CursorAt(double t)
        {
            double px = StartX, py = StartY, prev = 0;

            for (int i = 0; i < At.Length; i++)
            {
                double tx = Drops[i][0], ty = Drops[i][1];
                if (t < At[i])
                {
                    double f = Math.Min(1, (t - prev) / Math.Max(1, Math.Min(700, At[i] - prev)));
                    return (px + (tx - px) * f, py + (ty - py) * f);
                }
                px = tx; py = ty; prev = At[i];
            }

            // Tail of the loop: drift back to where it started, so the next pass begins from rest.
            double g = Math.Min(1, (t - prev) / 1400.0);
            return (px + (StartX - px) * g, py + (StartY - py) * g);
        }
    }
}
