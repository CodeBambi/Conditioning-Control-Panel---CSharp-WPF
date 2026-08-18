namespace CcpClient.Desktop.Effects;

/// <summary>
/// The Flash Images pacing law, as a pure function — WPF <c>ScheduleNextFlash</c>
/// (<c>Services/Flash/FlashService.cs:538-563</c>). It is separated from the effect for one
/// reason: it is the whole behaviour-visible part of the schedule, and a formula in a pure
/// function can be pinned exactly, at its boundaries, without a clock or a session.
/// </summary>
public static class FlashSchedule
{
    /// <summary>WPF's variance band: ±30 % of the base interval (<c>FlashService.cs:552-554</c>).</summary>
    public const double VarianceFraction = 0.3;

    /// <summary>WPF's floor: never less than 3 seconds between flashes (<c>FlashService.cs:555</c>).</summary>
    public const double MinimumIntervalSeconds = 3.0;

    /// <summary>Seconds in the hour the frequency dial is expressed in (<c>FlashService.cs:550</c>).</summary>
    public const double SecondsPerHour = 3600.0;

    /// <summary>
    /// The unvaried spacing implied by the dial: <c>3600.0 / max(1, flashesPerHour)</c>
    /// (<c>FlashService.cs:549-550</c>). The <c>max(1, …)</c> is WPF's and is kept even though
    /// the persisted dial already clamps at 1 — it is the guard that stops a zero from
    /// dividing, and removing it makes the function depend on its caller's clamp.
    /// </summary>
    public static double BaseIntervalSeconds(int flashesPerHour) =>
        SecondsPerHour / Math.Max(1, flashesPerHour);

    /// <summary>The earliest the next flash can be scheduled for this dial (the floor applies).</summary>
    public static TimeSpan MinimumInterval(int flashesPerHour) =>
        TimeSpan.FromSeconds(Math.Max(MinimumIntervalSeconds,
            BaseIntervalSeconds(flashesPerHour) * (1.0 - VarianceFraction)));

    /// <summary>
    /// The latest the next flash can be scheduled for this dial. Advancing a test clock by
    /// this is what makes "a flash is due" deterministic without pinning the random draw.
    /// </summary>
    public static TimeSpan MaximumInterval(int flashesPerHour) =>
        TimeSpan.FromSeconds(Math.Max(MinimumIntervalSeconds,
            BaseIntervalSeconds(flashesPerHour) * (1.0 + VarianceFraction)));

    /// <summary>
    /// One interval, WPF's arithmetic in WPF's order (<c>FlashService.cs:549-555</c>):
    /// base, then a uniform ±30 % offset, then the 3-second floor. The floor is applied LAST,
    /// so at high frequencies it truncates the bottom of the variance band rather than
    /// shifting the whole band up.
    /// </summary>
    public static TimeSpan NextInterval(int flashesPerHour, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var baseInterval = BaseIntervalSeconds(flashesPerHour);
        var variance = baseInterval * VarianceFraction;
        var interval = baseInterval + ((random.NextDouble() * variance * 2) - variance);
        interval = Math.Max(MinimumIntervalSeconds, interval);
        return TimeSpan.FromSeconds(interval);
    }
}
