using System;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>
/// Natasha's favourite: about one ambient bubble in ten and one flash in ten wears a faint red.
/// Pop that bubble, or see that flash, and 3:00 goes on the tab. The cue is meant to mix in
/// with the others (a thin red halo, and every couple of seconds a short red blink across the
/// picture), visible to someone who knows, easy to miss for someone who does not.
///
/// <para>Everything that decides is here and pure: the roll, the colour, the halo strength and
/// the blink envelope. BubbleService and FlashService only ask.</para>
/// </summary>
public static class NatashasFavourite
{
    public const string EventId = "natasha";

    /// <summary>+3:00.</summary>
    public const int Seconds = 180;

    /// <summary>One in this many bubbles, and one in this many flashes.</summary>
    public const int OneIn = 10;

    public const byte R = 0xE0, G = 0x2A, B = 0x4A;

    /// <summary>The still half of the cue: a thin red halo. Deliberately under the lucky gold
    /// (0.55) and the magnet's steel blue (0.55) so it does not shout.</summary>
    public const double HaloOpacity = 0.32;
    public const double HaloBlurDip = 14;

    /// <summary>The moving half: a red wash across the body, peaking at this alpha.</summary>
    public const double WashPeak = 0.22;

    /// <summary>Seconds between blinks. Not a multiple of the drain's or magnet's pulse, so a
    /// field of three kinds never blinks in step.</summary>
    public const double BlinkPeriodSec = 2.7;

    /// <summary>The blink is two short pulses at the start of each period.</summary>
    public const double PulseSec = 0.09;
    public const double PulseGapSec = 0.12;

    public static bool Roll(Random rng) => rng.Next(OneIn) == 0;

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

    public static double WashAlphaAt(double aliveSec) => WashPeak * BlinkAt(aliveSec);

    private static double Pulse(double t)
    {
        if (t < 0 || t >= PulseSec) return 0;
        var k = t / PulseSec;             // 0..1
        return 1 - Math.Abs(k * 2 - 1);   // 0..1..0
    }
}
