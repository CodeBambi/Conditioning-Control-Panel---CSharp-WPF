namespace CcpClient.Desktop.Effects;

/// <summary>
/// The Flash Images pacing law, as a pure function — WPF <c>ScheduleNextFlash</c>
/// (<c>Services/Flash/FlashService.cs:538-563</c>). It is separated from the effect for one
/// reason: it is the whole behaviour-visible part of the schedule, and a formula in a pure
/// function can be pinned exactly, at its boundaries, without a clock or a session.
///
/// <para><b>SP-101.</b> The ARITHMETIC now lives in <see cref="EffectSchedule"/>, because
/// Subliminals runs the identical five lines with different numbers and thirteen more modules
/// follow. What stays here is what is actually Flash Images': the three numbers, each with its own
/// citation, and the public surface every caller and every SP-098 fact already uses. This file is
/// deliberately still a NAMED law rather than a call site passing a tuple around — a module's pacing
/// constants are its behaviour, and they are entitled to a place with a citation on them.</para>
/// </summary>
public static class FlashSchedule
{
    /// <summary>WPF's variance band: ±30 % of the base interval (<c>FlashService.cs:552-554</c>).</summary>
    public const double VarianceFraction = 0.3;

    /// <summary>WPF's floor: never less than 3 seconds between flashes (<c>FlashService.cs:555</c>).</summary>
    public const double MinimumIntervalSeconds = 3.0;

    /// <summary>Seconds in the hour the frequency dial is expressed in (<c>FlashService.cs:550</c>).</summary>
    public const double SecondsPerHour = 3600.0;

    /// <summary>Flash Images' three numbers, as the shared arithmetic consumes them.</summary>
    public static readonly IntervalLaw Law = new(SecondsPerHour, VarianceFraction, MinimumIntervalSeconds);

    /// <summary>
    /// The unvaried spacing implied by the dial: <c>3600.0 / max(1, flashesPerHour)</c>
    /// (<c>FlashService.cs:549-550</c>). The <c>max(1, …)</c> is WPF's and is kept even though
    /// the persisted dial already clamps at 1 — it is the guard that stops a zero from
    /// dividing, and removing it makes the function depend on its caller's clamp.
    /// </summary>
    public static double BaseIntervalSeconds(int flashesPerHour) =>
        EffectSchedule.BaseIntervalSeconds(Law, flashesPerHour);

    /// <summary>The earliest the next flash can be scheduled for this dial (the floor applies).</summary>
    public static TimeSpan MinimumInterval(int flashesPerHour) =>
        EffectSchedule.MinimumInterval(Law, flashesPerHour);

    /// <summary>
    /// The latest the next flash can be scheduled for this dial. Advancing a test clock by
    /// this is what makes "a flash is due" deterministic without pinning the random draw.
    /// </summary>
    public static TimeSpan MaximumInterval(int flashesPerHour) =>
        EffectSchedule.MaximumInterval(Law, flashesPerHour);

    /// <summary>
    /// One interval, WPF's arithmetic in WPF's order (<c>FlashService.cs:549-555</c>):
    /// base, then a uniform ±30 % offset, then the 3-second floor. The floor is applied LAST,
    /// so at high frequencies it truncates the bottom of the variance band rather than
    /// shifting the whole band up.
    /// </summary>
    public static TimeSpan NextInterval(int flashesPerHour, Random random) =>
        EffectSchedule.NextInterval(Law, flashesPerHour, random);
}
