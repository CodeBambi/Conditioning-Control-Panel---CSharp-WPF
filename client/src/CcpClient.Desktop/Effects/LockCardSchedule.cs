namespace CcpClient.Desktop.Effects;

/// <summary>
/// Lock Card's pacing arithmetic, ported verbatim from
/// <c>Services/LockCard/LockCardService.cs</c> and kept pure so every clamp and every constant is
/// pinnable without a clock, a window or a session.
///
/// <para><b>The first card is not spaced like the others, and that asymmetry is a fix upstream made
/// on purpose.</b> Subsequent cards sit at <c>60/perHour</c> ±30 % (<c>:127-134</c>). The FIRST is
/// an OFFSET INTO the opening interval, <c>roll * (60/perHour)</c> — upstream's own comment at
/// <c>:144-155</c> explains why: scheduling it as a whole inter-arrival gap put the earliest
/// possible card at 1/hour at minute 42, so a 30-minute session could never produce one, which
/// hard-blocked every program day whose task required a card.</para>
/// </summary>
public static class LockCardSchedule
{
    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 10)</c> (<c>CCP.Core/Models/AppSettings.cs:3342</c>,
    /// which the shipping app compiles in through its own project reference).</summary>
    public const int MinPerHour = 1;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 10)</c> (<c>AppSettings.cs:3342</c>).</summary>
    public const int MaxPerHour = 10;

    /// <summary>WPF default <c>_lockCardFrequency = 2</c> (<c>AppSettings.cs:3338</c>).</summary>
    public const int DefaultPerHour = 2;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 10)</c> on the repeat count
    /// (<c>AppSettings.cs:3349</c>).</summary>
    public const int MinRepeats = 1;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 10)</c> (<c>AppSettings.cs:3349</c>).</summary>
    public const int MaxRepeats = 10;

    /// <summary>WPF default <c>_lockCardRepeats = 3</c> (<c>AppSettings.cs:3345</c>).</summary>
    public const int DefaultRepeats = 3;

    /// <summary>The ±30 % jitter around the nominal interval (<c>LockCardService.cs:129-130</c>:
    /// <c>minInterval = intervalMinutes * 0.7</c>).</summary>
    public const double JitterLow = 0.7;

    /// <summary>(<c>LockCardService.cs:130</c>: <c>maxInterval = intervalMinutes * 1.3</c>.)</summary>
    public const double JitterHigh = 1.3;

    /// <summary>
    /// Minutes until the FIRST card of a run — <c>ComputeFirstCardDelayMinutes</c>
    /// (<c>LockCardService.cs:159-168</c>), verbatim.
    /// </summary>
    /// <param name="perHour">Cards per hour; values below 1 are treated as 1 (<c>:161</c>).</param>
    /// <param name="windowMinutes">
    /// How long the caller expects to keep running, or null for open-ended. <b>The port always
    /// passes null</b>, and that is upstream's own dashboard path rather than a simplification: the
    /// clamp exists for the session engine's remaining-minutes window (<c>:54-58</c>, issue #736)
    /// and this port has no scripted session with a declared length (D99). Kept as a parameter, and
    /// pinned in both arms, so the day the port grows one the arithmetic is already here rather than
    /// re-derived.
    /// </param>
    /// <param name="roll">A uniform sample in [0,1).</param>
    public static double FirstCardDelayMinutes(int perHour, double? windowMinutes, double roll)
    {
        var intervalMinutes = 60.0 / Math.Max(1, perHour);

        var maxFirst = intervalMinutes;
        if (windowMinutes is > 0)
        {
            maxFirst = Math.Min(maxFirst, windowMinutes.Value * 0.8);
        }

        return roll * maxFirst;
    }

    /// <summary>
    /// The interval between cards after the first — <c>Timer_Tick</c>
    /// (<c>LockCardService.cs:127-134</c>), verbatim including the <c>Math.Max(1, ...)</c> floor on
    /// the dial and the <c>roll * (max - min) + min</c> shape.
    /// </summary>
    public static TimeSpan Interval(int perHour, double roll)
    {
        var intervalMinutes = 60.0 / Math.Max(1, perHour);
        var minInterval = intervalMinutes * JitterLow;
        var maxInterval = intervalMinutes * JitterHigh;
        return TimeSpan.FromMinutes((roll * (maxInterval - minInterval)) + minInterval);
    }
}
