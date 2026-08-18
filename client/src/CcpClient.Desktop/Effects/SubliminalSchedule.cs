namespace CcpClient.Desktop.Effects;

/// <summary>
/// The Subliminals pacing law — WPF <c>SubliminalService.ScheduleNext</c>
/// (<c>Services/Subliminal/SubliminalService.cs:172-187</c>).
///
/// <para>Same arithmetic as Flash Images (<see cref="EffectSchedule"/>) and two different numbers,
/// which is the whole reason this port has a shared law and per-module facades rather than one
/// copied function per module: <b>the dial counts per MINUTE, not per hour</b> (<c>:177</c> —
/// <c>60.0 / freq</c>, and the settings comment says "Messages per minute (1-30)",
/// <c>CCP.Core/Models/AppSettings.cs:1242</c>), and <b>the floor is one second, not three</b>
/// (<c>:181</c>). A subliminal at the ceiling of its dial therefore arrives roughly every two
/// seconds; a flash at the ceiling of its arrives roughly every twenty.</para>
/// </summary>
public static class SubliminalSchedule
{
    /// <summary>WPF's variance band: ±30 % of the base interval (<c>SubliminalService.cs:179-180</c>).
    /// The same fraction as flash, arrived at independently upstream — kept as its own constant so a
    /// later change to one module's band cannot silently move the other's.</summary>
    public const double VarianceFraction = 0.3;

    /// <summary>WPF's floor: "At least 1 second" (<c>SubliminalService.cs:181</c>).</summary>
    public const double MinimumIntervalSeconds = 1.0;

    /// <summary>Seconds in the MINUTE the frequency dial is expressed in (<c>SubliminalService.cs:177</c>).</summary>
    public const double SecondsPerMinute = 60.0;

    /// <summary>Subliminals' three numbers, as the shared arithmetic consumes them.</summary>
    public static readonly IntervalLaw Law = new(SecondsPerMinute, VarianceFraction, MinimumIntervalSeconds);

    /// <summary>The unvaried spacing implied by the dial: <c>60.0 / max(1, perMinute)</c>
    /// (<c>SubliminalService.cs:175-177</c>).</summary>
    public static double BaseIntervalSeconds(int perMinute) =>
        EffectSchedule.BaseIntervalSeconds(Law, perMinute);

    /// <summary>The earliest the next subliminal can be scheduled for this dial (the floor applies).</summary>
    public static TimeSpan MinimumInterval(int perMinute) =>
        EffectSchedule.MinimumInterval(Law, perMinute);

    /// <summary>The latest the next subliminal can be scheduled for this dial — what a clock-driven
    /// test advances by, so no fact has to pin the random draw.</summary>
    public static TimeSpan MaximumInterval(int perMinute) =>
        EffectSchedule.MaximumInterval(Law, perMinute);

    /// <summary>One interval, WPF's arithmetic in WPF's order (<c>SubliminalService.cs:175-181</c>):
    /// base, then a uniform ±30 % offset, then the one-second floor LAST.</summary>
    public static TimeSpan NextInterval(int perMinute, Random random) =>
        EffectSchedule.NextInterval(Law, perMinute, random);
}
