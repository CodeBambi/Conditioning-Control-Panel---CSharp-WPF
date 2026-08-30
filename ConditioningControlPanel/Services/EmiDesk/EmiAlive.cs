using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>Which micro-fidget she just picked. <see cref="None"/> only ever means "none yet".</summary>
public enum EmiFidget
{
    /// <summary>Nothing has been picked yet (the scheduler's starting state).</summary>
    None,

    /// <summary>A 2 DIP antenna twitch: the smallest thing she does.</summary>
    Twitch,

    /// <summary>A one degree weight shift, held two seconds and released.</summary>
    WeightShift,

    /// <summary>The canon glance chain, plus a small lean to the side she looked at.</summary>
    Glance,

    /// <summary>
    /// She checks something: a plate comes up at her right hand for a couple of seconds and goes
    /// away again. The only fidget that puts ART on the screen rather than moving her, and so the
    /// only one that can no-op because a file is missing (see <c>EmiProps</c>).
    /// </summary>
    Prop,

    /// <summary>
    /// She puts something on her own glass: pong, a spiral, a burst, gif rain, a film strip. The
    /// channels already existed behind a stillness gate of their own
    /// (<c>EmiChannels.IdleBeforeFlip</c>, ~10 s since the 2026-08-30 campus port); this is the same flip, reached from the fidget wheel so
    /// that the screen takes its turn among the small body moves instead of being a separate
    /// clock. Like <see cref="Prop"/> it can no-op - a channel needs a library to draw from and the
    /// desk has to have been left alone - which is why <c>RunFidget</c> reports whether it did
    /// anything.
    /// </summary>
    Screen
}

/// <summary>What a completed pat on her body means once the poke ladder has looked at it.</summary>
public enum EmiPokeStep
{
    /// <summary>An ordinary pat: whatever the pet path was going to do anyway.</summary>
    Pat,

    /// <summary>The second poke inside the window: the same flick, wearing <c>-_-</c>.</summary>
    Annoyed,

    /// <summary>The third: the rage beat, wordless, and then a truce.</summary>
    Rage
}

/// <summary>
/// THE PURE HALF OF WAVE A ("making her alive", <c>docs/emi-desk/ALIVE-PLAN.md</c> section 6).
///
/// <para>Every number the wave introduced lives here as a named constant, and every decision it
/// makes that does not need a window - the gaze lean, the approach test, the poke ladder, the
/// fidget scheduler and the priority gate - is a pure function or a tiny state machine with an
/// injected clock. That is what lets <c>EmiAliveTests</c> walk the ladder, the jitter bounds and
/// the yield rule in a millisecond, the same property <see cref="EmiRingLayout"/> and
/// <see cref="EmiNudgeMachine"/> protect: the moment one of these reads a <c>Window</c> it stops
/// being testable and the feel rots quietly.</para>
///
/// <para>The numbers marked CAMPUS are ported from <c>Resources/web/arcademy/emi/widget.js</c>
/// (<c>DIALS</c>) and are the campus feel verbatim. Do not retune them here: a retune is an owner
/// call and lands in the plan first.</para>
/// </summary>
public static class EmiAlive
{
    // ---------------------------------------------------------------- the poll

    /// <summary>
    /// The ONE clock wave A runs on, in ms. Ten cursor reads a second is free, and everything the
    /// wave watches for - the lean, the approach, the hover linger, the fidget due-time, the
    /// stretch due-time - is resolved off this single tick rather than off a timer each.
    /// </summary>
    public const int PollMs = 100;

    // ---------------------------------------------------------------- blink

    /// <summary>CAMPUS <c>BLINK_EVERY_MS</c>: the resting blink cadence, in ms.</summary>
    public const int BlinkEveryMs = 5200;

    /// <summary>
    /// Plus or minus this much of jitter on each blink, in ms. The campus blinks on a bare
    /// interval because a browser tab has plenty else moving; on a desktop a perfectly regular
    /// 5.2 s blink reads as a metronome, so the clock wanders inside a second.
    /// </summary>
    public const int BlinkJitterMs = 600;

    /// <summary>CAMPUS <c>BLINK_HOLD_MS</c>: how long the lid stays shut, in ms.</summary>
    public const int BlinkHoldMs = 110;

    /// <summary>Roughly one blink in this many is a quick double.</summary>
    public const int DoubleBlinkOneIn = 7;

    /// <summary>The eyes-open gap in the middle of a double blink, in ms.</summary>
    public const int DoubleBlinkGapMs = 120;

    /// <summary>The lid face. CAMPUS <c>BLINK_FACE</c>.</summary>
    public const string BlinkFace = "-_-";

    /// <summary>One blink interval, jittered. Always inside <c>BlinkEveryMs +/- BlinkJitterMs</c>.</summary>
    public static int BlinkDelayMs(Random rng)
    {
        if (rng == null) return BlinkEveryMs;
        return BlinkEveryMs - BlinkJitterMs + rng.Next(BlinkJitterMs * 2 + 1);
    }

    // ---------------------------------------------------------------- gaze

    /// <summary>CAMPUS <c>GAZE_MAX_PX</c>: the hardest the face ever leans, at the reference size.</summary>
    public const double GazeMaxDip = 3.0;

    /// <summary>CAMPUS <c>GAZE_DIV</c>: cursor distance per unit of lean, at the reference size.</summary>
    public const double GazeDiv = 60.0;

    /// <summary>CAMPUS <c>GAZE_EASE</c>: how far the lean closes on its target per ANIMATION FRAME.</summary>
    public const double GazeEasePerFrame = 0.15;

    /// <summary>The frame the campus number is quoted at (60 fps), in ms.</summary>
    public const double GazeEaseFrameMs = 1000.0 / 60.0;

    /// <summary>
    /// CAMPUS <c>W_DEFAULT</c>: the body width the campus gaze numbers were tuned at. The desktop
    /// widget is resizable from 152 to 420 DIPs, so both the cap and the divisor are scaled by
    /// <see cref="GazeScale"/> - a 3 DIP lean on a 420 DIP EMI is not a lean, it is a jitter.
    /// </summary>
    public const double GazeRefBodyWidth = 150.0;

    /// <summary>How much bigger she is than the size the campus gaze was tuned at.</summary>
    public static double GazeScale(double bodyWidth)
    {
        if (double.IsNaN(bodyWidth) || double.IsInfinity(bodyWidth) || bodyWidth <= 0) return 1.0;
        return bodyWidth / GazeRefBodyWidth;
    }

    /// <summary>
    /// The campus's per-frame easing, re-expressed for one <see cref="PollMs"/> tick. The lean is
    /// driven off the 10 Hz cursor poll rather than off a render hook, so applying 0.15 raw would
    /// make her eyes lag the pointer by more than half a second. This keeps the campus TIME
    /// CONSTANT instead of the campus per-frame number: about 0.62 per 100 ms.
    /// </summary>
    public static double GazeEasePerPoll =>
        1.0 - Math.Pow(1.0 - GazeEasePerFrame, PollMs / GazeEaseFrameMs);

    /// <summary>One easing step toward <paramref name="target"/>.</summary>
    public static double Ease(double current, double target, double k)
        => current + (target - current) * k;

    /// <summary>
    /// The lean the face wants, in DIPs, for a cursor at <paramref name="cursorDip"/> and a body
    /// silhouette at <paramref name="bodyDip"/> (both in screen DIPs). Proportional to the offset
    /// from her centre and capped, exactly as the campus does it.
    ///
    /// <para>THE CAP SCALES WITH HER, THE DIVISOR DOES NOT, and that pairing is the whole trick:
    /// the lean tops out <c>cap x div</c> DIPs from her centre, which with a scaled cap and the
    /// campus's fixed 60 is <b>1.2 body widths at every size</b> - the campus geometry on a 150 px
    /// EMI, held true out to her 420 DIP maximum. Scaling both halves would push the saturation
    /// distance out with the SQUARE of her size; scaling neither would make a three DIP lean on a
    /// 420 DIP EMI invisible.</para>
    /// </summary>
    public static (double X, double Y) GazeTarget(
        System.Windows.Point cursorDip, System.Windows.Rect bodyDip, double bodyWidth)
    {
        double cap = GazeMaxDip * GazeScale(bodyWidth);
        const double div = GazeDiv;

        double dx = cursorDip.X - (bodyDip.X + bodyDip.Width / 2.0);
        double dy = cursorDip.Y - (bodyDip.Y + bodyDip.Height / 2.0);
        return (Clamp(dx / div, -cap, cap), Clamp(dy / div, -cap, cap));
    }

    /// <summary>A one-axis lean of <paramref name="dir"/> (-1..1), for the glance fidget's look.</summary>
    public static double GazeNudge(double dir, double bodyWidth)
        => Clamp(dir, -1, 1) * GazeMaxDip * GazeScale(bodyWidth);

    // ---------------------------------------------------------------- approach

    /// <summary>CAMPUS <c>APPROACH_PX</c>: cursor this near her EDGE (DIPs) and she notices.</summary>
    public const double ApproachDip = 120.0;

    /// <summary>CAMPUS <c>APPROACH_COOLDOWN_MS</c>: one noticing per this window, so she is not a bell you can keep ringing.</summary>
    public const int ApproachCooldownMs = 30_000;

    /// <summary>CAMPUS <c>GLANCE_SPEED</c>: arriving faster than this (DIPs/ms) earns the glance chain.</summary>
    public const double GlanceSpeedDipPerMs = 1.2;

    /// <summary>How long the quiet perk face is held, in ms. CAMPUS <c>raw('o_o', { hold: 900 })</c>.</summary>
    public const int PerkHoldMs = 900;

    /// <summary>The face a walked-up-to approach earns.</summary>
    public const string PerkFace = "o_o";

    /// <summary>
    /// Is the cursor inside the approach radius? Measured from her EDGE (her half-width plus
    /// <see cref="ApproachDip"/>), so a bigger EMI is not a wider trigger.
    /// </summary>
    public static bool WithinApproach(System.Windows.Point cursorDip, System.Windows.Rect bodyDip)
    {
        double dx = cursorDip.X - (bodyDip.X + bodyDip.Width / 2.0);
        double dy = cursorDip.Y - (bodyDip.Y + bodyDip.Height / 2.0);
        return Math.Sqrt(dx * dx + dy * dy) < bodyDip.Width / 2.0 + ApproachDip;
    }

    // ---------------------------------------------------------------- hover linger

    /// <summary>
    /// Hover ON HER this long with no click and she looks expectant (ms, from the moment the
    /// pointer arrived). PLAN section 3; the campus counts the same 2 s from its wider approach
    /// radius, and the desktop uses her silhouette because the pointer resting on a widget is a
    /// much stronger signal than a pointer somewhere near one.
    /// </summary>
    public const int LingerMs = 2_000;

    /// <summary>...and still there at this mark, still unpetted, she looks away (ms from arrival).</summary>
    public const int LingerAwayMs = 4_000;

    /// <summary>How long each linger face is held, in ms.</summary>
    public const int LingerHoldMs = 1_100;

    /// <summary>The expectant face.</summary>
    public const string LingerFace = "^_^";

    /// <summary>
    /// The look-away: she is not hurt, she is making a point. Written as an escape, like every
    /// other non-ascii face in <see cref="EmiChains"/>, so it cannot drift from the canon string
    /// the pose map is keyed on.
    /// </summary>
    public const string LingerAwayFace = "\u00AC_\u00AC";

    // ---------------------------------------------------------------- fidgets

    /// <summary>A micro-fidget arrives no sooner than this after the last one, in ms.</summary>
    public const int FidgetMinMs = 25_000;

    /// <summary>...and no later than this, in ms.</summary>
    public const int FidgetMaxMs = 50_000;

    /// <summary>
    /// How long the desk has to have gone untouched before the <see cref="EmiFidget.Screen"/> beat
    /// will take its turn, in ms. This sat at 30 s against the glass's 90 s owner lock; on
    /// 2026-08-30 the owner unlocked the rotation to ~10 s for the campus channel port, and the
    /// wheel's floor drops with it - the two clocks are the same statement (an untouched desk
    /// drifts off to a channel) arriving down different wires, and they should agree.
    /// </summary>
    public const int ScreenBeatRestMs = 10_000;

    /// <summary>The antenna twitch's travel, in DIPs.</summary>
    public const double TwitchDip = 2.0;

    /// <summary>The weight shift's lean, in degrees.</summary>
    public const double WeightShiftDeg = 1.0;

    /// <summary>...how long it is held there, in ms.</summary>
    public const int WeightShiftHoldMs = 2_000;

    /// <summary>...and how long it takes to lean in and to come back, in ms.</summary>
    public const int WeightShiftTravelMs = 320;

    /// <summary>The stretch is worth waiting for: no sooner than this after the last one, in ms.</summary>
    public const int StretchMinMs = 20 * 60_000;

    /// <summary>...and no later than this, in ms.</summary>
    public const int StretchMaxMs = 40 * 60_000;

    /// <summary>How much bigger she gets at the top of a stretch (1.04 = four percent).</summary>
    public const double StretchScale = 1.04;

    /// <summary>How long the stretch takes to reach the top, in ms.</summary>
    public const int StretchUpMs = 400;

    /// <summary>...and to settle back down, in ms.</summary>
    public const int StretchDownMs = 520;

    /// <summary>The face at the top of the stretch.</summary>
    public const string StretchFace = ">_<";

    /// <summary>...and the one she settles on.</summary>
    public const string StretchSettleFace = "^_^";

    /// <summary>
    /// When the next fidget and the next stretch are due, and which fidget it is. Pure: an injected
    /// <see cref="Random"/>, no clock of its own, so the jitter bounds and the "never twice in a
    /// row" rule can be walked ten thousand times in a test.
    /// </summary>
    public sealed class FidgetScheduler
    {
        private readonly Random _rng;

        /// <param name="rng">The source of jitter. Null takes a fresh one.</param>
        public FidgetScheduler(Random? rng = null) => _rng = rng ?? new Random();

        /// <summary>The kind the last <see cref="Next"/> returned.</summary>
        public EmiFidget Last { get; private set; } = EmiFidget.None;

        /// <summary>Milliseconds until the next micro-fidget: 25 to 50 seconds.</summary>
        public int NextDelayMs() => FidgetMinMs + _rng.Next(FidgetMaxMs - FidgetMinMs + 1);

        /// <summary>Milliseconds until the next stretch: 20 to 40 minutes.</summary>
        public int NextStretchDelayMs() => StretchMinMs + _rng.Next(StretchMaxMs - StretchMinMs + 1);

        /// <summary>
        /// The next fidget kind. NEVER the same as the last one: two antenna twitches in a row read
        /// as a stuck sprite rather than as a living thing.
        /// </summary>
        public EmiFidget Next()
        {
            Span<EmiFidget> all = stackalloc EmiFidget[5]
            {
                EmiFidget.Twitch, EmiFidget.WeightShift, EmiFidget.Glance, EmiFidget.Prop,
                EmiFidget.Screen
            };

            // Draw from the kinds that are NOT the last one, so the rule costs one roll, never a
            // retry loop that could in principle spin.
            Span<EmiFidget> pool = stackalloc EmiFidget[5];
            int n = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != Last) pool[n++] = all[i];
            }

            var pick = pool[_rng.Next(n)];
            Last = pick;
            return pick;
        }
    }

    // ---------------------------------------------------------------- the poke ladder

    /// <summary>How long a run of pokes stays a run, in ms. CAMPUS <c>PET_WINDOW_MS</c>.</summary>
    public const int PokeWindowMs = 4_000;

    /// <summary>The poke inside the window that earns <c>-_-</c>.</summary>
    public const int PokeAnnoyAt = 2;

    /// <summary>...and the one that earns the rage beat.</summary>
    public const int PokeRageAt = 3;

    /// <summary>After the rage, this long of peace: the ladder cannot climb again, in ms.</summary>
    public const int PokeTruceMs = 60_000;

    /// <summary>How long the wordless glare is held at the end of the rage, in ms.</summary>
    public const int PokeRageHoldMs = 1_500;

    /// <summary>The glare. Wordless on purpose: annoyance is funnier silent.</summary>
    public const string PokeRageFace = ">:(";

    /// <summary>The second poke's face.</summary>
    public const string PokeAnnoyFace = "-_-";

    /// <summary>
    /// THE POKE LADDER. Three pats inside <see cref="PokeWindowMs"/> climb: the first is an
    /// ordinary pat, the second wears <c>-_-</c>, the third is the rage beat and then she calls a
    /// truce for a minute.
    ///
    /// <para>It counts EVERY completed pat, including the one that drew a line, because a poke is a
    /// poke however the pet path chose to answer it. It does not decide what a pat does - that is
    /// still <c>PetFromClick</c>'s cooldown - it only says which flick the cooldown branch should
    /// wear, which is why the two cannot fight.</para>
    /// </summary>
    public sealed class PokeLadder
    {
        private int _count;
        private DateTime _lastAt = DateTime.MinValue;
        private DateTime _truceUntil = DateTime.MinValue;

        /// <summary>How many pokes are in the current run.</summary>
        public int Count => _count;

        /// <summary>True while the post-rage truce is on: the ladder refuses to climb.</summary>
        public bool InTruce(DateTime nowUtc) => nowUtc < _truceUntil;

        /// <summary>Book one poke and say what it earned.</summary>
        public EmiPokeStep Note(DateTime nowUtc)
        {
            if (nowUtc < _truceUntil)
            {
                // A truce is a truce: pats still land, they simply stop climbing.
                _count = 0;
                _lastAt = nowUtc;
                return EmiPokeStep.Pat;
            }

            if (_lastAt == DateTime.MinValue || (nowUtc - _lastAt).TotalMilliseconds > PokeWindowMs) _count = 0;
            _lastAt = nowUtc;
            _count++;

            if (_count >= PokeRageAt)
            {
                _count = 0;
                _truceUntil = nowUtc.AddMilliseconds(PokeTruceMs);
                return EmiPokeStep.Rage;
            }
            return _count == PokeAnnoyAt ? EmiPokeStep.Annoyed : EmiPokeStep.Pat;
        }

        /// <summary>Forget the run (she left, or she was dismissed). The truce is NOT forgotten.</summary>
        public void Reset()
        {
            _count = 0;
            _lastAt = DateTime.MinValue;
        }
    }

    // ---------------------------------------------------------------- the yield rule

    /// <summary>
    /// MAY A WAVE-A FACE GO UP RIGHT NOW? The whole wave is the LOWEST priority thing she owns: a
    /// perk, a linger, a fidget or a stretch may only start when nothing else has the face, and
    /// anything that starts afterwards - a pet, a chain, an ask, a hold, panic - simply takes it,
    /// because every one of those already cancels the running chain.
    ///
    /// <para>The campus's <c>canPerk()</c>, with the desktop's two extra owners (a resize in
    /// progress, and the engine hold that covers panic and every safety moment).</para>
    /// </summary>
    public static bool CanPerk(
        bool busy, bool chainLive, bool askLive, bool holdActive, bool dragging, bool resizing)
        => !busy && !chainLive && !askLive && !holdActive && !dragging && !resizing;

    // ---------------------------------------------------------------- util

    private static double Clamp(double v, double lo, double hi)
    {
        if (double.IsNaN(v)) return 0;
        return v < lo ? lo : v > hi ? hi : v;
    }
}
