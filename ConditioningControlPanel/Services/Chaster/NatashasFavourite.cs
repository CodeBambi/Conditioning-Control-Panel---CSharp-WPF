using System;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>
/// Natasha's favourite: about one ambient bubble in ten and one flash in ten wears a faint red.
/// Pop that bubble yourself, or see that flash, and 5:00 goes on the tab. Hold the bubble
/// instead and it is 1:00 off (see "pop, hold, let go" below) (owner, 2026-09-26: it is the
/// one price that meets everyday use, so it should sting, and the red should hide better). The cue is meant to mix in
/// with the others (a thin red halo, and every couple of seconds a short red blink across the
/// picture), visible to someone who knows, easy to miss for someone who does not.
///
/// <para>Everything that decides is here and pure: the roll, the colour, the halo strength and
/// the blink envelope. BubbleService and FlashService only ask.</para>
/// </summary>
public static class NatashasFavourite
{
    public const string EventId = "natasha";

    /// <summary>+5:00.</summary>
    public const int Seconds = 300;

    /// <summary>One in this many bubbles, and one in this many flashes.</summary>
    public const int OneIn = 10;

    public const byte R = 0xE0, G = 0x2A, B = 0x4A;

    /// <summary>The still half of the cue: a thin red halo. Deliberately under the lucky gold
    /// (0.55) and the magnet's steel blue (0.55) so it does not shout.</summary>
    public const double HaloOpacity = 0.2;
    public const double HaloBlurDip = 14;

    /// <summary>The moving half: a red wash across the body, peaking at this alpha.</summary>
    public const double WashPeak = 0.12;

    /// <summary>Seconds between blinks. Not a multiple of the drain's or magnet's pulse, so a
    /// field of three kinds never blinks in step.</summary>
    public const double BlinkPeriodSec = 2.7;

    /// <summary>The blink is two short pulses at the start of each period.</summary>
    public const double PulseSec = 0.09;
    public const double PulseGapSec = 0.12;

    public static bool Roll(Random rng) => rng.Next(OneIn) == 0;

    // ============================== pop, hold, let go ==============================
    // Tier-2 feedback (2026-09-29): red "feels like RNG, I didn't do anything to deserve that".
    // So the red bubble is a choice now. A quick click pops it (+5:00, as before). Press and HOLD
    // it for HoldMs and a mint ring fills round it: it shrinks away and books a small credit.
    // Letting it float past books nothing, and so does any pop the player did not cause.

    /// <summary>The credit row for a held red bubble.</summary>
    public const string HeldEventId = "natasha_held";

    /// <summary>How long a press must stay down on the red bubble to resist it.</summary>
    public const int HoldMs = 1200;

    /// <summary>-1:00, the credit a resisted red bubble books.</summary>
    public const int HeldSeconds = -60;

    /// <summary>Where a press on the red bubble stands after one tick.</summary>
    public enum HoldStep
    {
        /// <summary>Still down, still on it: the ring keeps filling.</summary>
        Holding,
        /// <summary>Let go before the ring filled: it pops, like a click.</summary>
        Popped,
        /// <summary>The ring filled: resisted.</summary>
        Resisted,
        /// <summary>The pointer slid off it while down: nothing happens, it floats on.</summary>
        SlidOff,
    }

    /// <summary>One tick of a held press. A full ring wins even on the tick the button comes up,
    /// so a hold that reached 1.2 s is never read as a click.</summary>
    public static HoldStep StepHold(double heldMs, bool released, bool onBubble)
    {
        if (heldMs >= HoldMs) return HoldStep.Resisted;
        if (released) return HoldStep.Popped;
        if (!onBubble) return HoldStep.SlidOff;
        return HoldStep.Holding;
    }

    /// <summary>0..1, how full the mint ring is.</summary>
    public static double HoldProgress(double heldMs) =>
        double.IsNaN(heldMs) ? 0 : Math.Clamp(heldMs / HoldMs, 0, 1);

    /// <summary>The ring's mint (the house mint, #5FFFD0).</summary>
    public const byte MintR = 0x5F, MintG = 0xFF, MintB = 0xD0;

    /// <summary>How far the bubble shrinks inside the ring over a full hold.</summary>
    public const double HoldShrink = 0.2;

    /// <summary>Who ended a red bubble.</summary>
    public enum PopCause
    {
        /// <summary>The app did it: the companion, a chain, a sweep, a clear. Never books.</summary>
        Programmatic,
        /// <summary>The player's own click or stare.</summary>
        Player,
        /// <summary>The player held it until the ring filled.</summary>
        Resisted,
    }

    /// <summary>The row a bubble's end books, or null. Only a red bubble books, and only when the
    /// player caused it.</summary>
    public static string? RowFor(bool isNatasha, PopCause cause) =>
        !isNatasha ? null
        : cause == PopCause.Player ? EventId
        : cause == PopCause.Resisted ? HeldEventId
        : null;

    /// <summary>0..1 envelope of the blink at <paramref name="aliveSec"/>: two 90 ms triangles,
    /// then dark until the next period. Multiply by <see cref="WashPeak"/> for the alpha.</summary>
    public static double BlinkAt(double aliveSec)
    {
        if (aliveSec < 0 || double.IsNaN(aliveSec)) return 0;
        var t = aliveSec % BlinkPeriodSec;
        var a = Pulse(t);
        var b = Pulse(t - PulseSec - PulseGapSec);
        return Math.Max(a, b);
    }

    /// <summary>A persistent bubble tint survives disabled glow and other variant halos. Kept
    /// faint (owner, 2026-09-26: make the red less noticeable): a hint, not a warning.</summary>
    public const double BubbleWashBase = 0.06;
    public static double BubbleWashAt(double aliveSec, bool animate) =>
        BubbleWashBase + (animate ? WashAlphaAt(aliveSec) : 0);

    public static double WashAlphaAt(double aliveSec) => WashPeak * BlinkAt(aliveSec);

    private static double Pulse(double t)
    {
        if (t < 0 || t >= PulseSec) return 0;
        var k = t / PulseSec;             // 0..1
        return 1 - Math.Abs(k * 2 - 1);   // 0..1..0
    }
}
