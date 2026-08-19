namespace CcpClient.Desktop.Effects;

/// <summary>
/// How many bubbles a clip carries — WPF's own three-value enum
/// (<c>Services/BubbleCountService.cs:23</c>).
/// </summary>
public enum BubbleCountDifficulty
{
    /// <summary>Three bubbles per thirty seconds of clip
    /// (<c>Windows/BubbleCountWindow.xaml.cs:1141</c>).</summary>
    Easy = 0,

    /// <summary>Five (<c>:1142</c>), and upstream's fall-through default (<c>:1144</c>).</summary>
    Medium = 1,

    /// <summary>Eight (<c>:1143</c>).</summary>
    Hard = 2,
}

/// <summary>
/// The Bubble Count pacing law — an INTERVAL law, and it is NOT Mandatory Video's.
///
/// <para><c>3600.0 / gamesPerHour</c>, jittered ±20 % and floored at SIXTY seconds
/// (<c>Services/BubbleCountService.cs:88-96</c>). Three things differ from
/// <see cref="MandatoryVideoSchedule"/> and each is upstream's: the dial is clamped 1..<b>10</b>
/// rather than 1..20 (<c>:88</c>, <c>Math.Max(1, Math.Min(10, …))</c>), the jitter is expressed as a
/// SPAN either side of the base rather than as a factor (<c>:92-94</c>,
/// <c>base + (roll*variance*2 - variance)</c> with <c>variance = base * 0.2</c>), and the floor is
/// its own <c>Math.Max(60, interval)</c> (<c>:95</c>) with upstream's own comment, "Minimum 1 minute
/// between games".</para>
///
/// <para><b>The two jitter forms are arithmetically identical and are still ported separately.</b>
/// <c>b + (r*0.4b - 0.2b) == b*(0.8 + 0.4r)</c>, so this class could have called the video one. It
/// does not, because they are two upstream sites with two dials and two clamps, and a shared helper
/// would make a later change to one silently change the other — the same reason SP-109's two audio
/// modules kept two schedules over one shape.</para>
/// </summary>
public static class BubbleCountSchedule
{
    /// <summary>WPF's slider default (<c>Views/Controls/Studio/BubbleCountFeatureControl.xaml</c>'s
    /// frequency slider, and <c>CCP.Core/Models/AppSettings.cs</c>'s own default of 2).</summary>
    public const int DefaultPerHour = 2;

    /// <summary>Upstream's own floor at the point of use (<c>BubbleCountService.cs:88</c>).</summary>
    public const int MinPerHour = 1;

    /// <summary>Upstream's own ceiling at the point of use (<c>:88</c>) — TEN, not the video
    /// module's twenty. Upstream's comment on the same line: "Frequency is games per hour
    /// (1-10)".</summary>
    public const int MaxPerHour = 10;

    /// <summary>Seconds in the hour the dial is denominated in (<c>:89</c>).</summary>
    public const double SecondsPerHour = 3600.0;

    /// <summary>The jitter, as a fraction of the base interval either side (<c>:92</c>).</summary>
    public const double JitterFraction = 0.2;

    /// <summary>Upstream's <c>Math.Max(60, interval)</c> (<c>:95</c>).</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The interval until the next game.
    /// </summary>
    /// <param name="perHour">The dial, floored and capped here as well as in the document's clamp
    /// because upstream clamps it at the point of use too (<c>:88</c>) and a preset file is not the
    /// only way in.</param>
    /// <param name="roll">A uniform draw in [0,1) — upstream's <c>_random.NextDouble()</c>.</param>
    public static TimeSpan Interval(int perHour, double roll)
    {
        var effective = Math.Clamp(perHour, MinPerHour, MaxPerHour);
        var baseInterval = SecondsPerHour / effective;
        var variance = baseInterval * JitterFraction;
        var seconds = baseInterval + ((roll * variance * 2) - variance);
        return TimeSpan.FromSeconds(Math.Max(MinimumInterval.TotalSeconds, seconds));
    }
}
