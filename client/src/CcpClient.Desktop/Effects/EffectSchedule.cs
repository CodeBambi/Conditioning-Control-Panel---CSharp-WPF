namespace CcpClient.Desktop.Effects;

/// <summary>
/// One paced effect's interval law, as DATA.
///
/// <para><b>Why this exists (SP-101).</b> WPF paces Flash Images and Subliminals with the same four
/// lines and three different numbers: a base spacing implied by a frequency dial, a uniform variance
/// band around it, and a floor applied last —
/// <c>Services/Flash/FlashService.cs:549-555</c> and
/// <c>Services/Subliminal/SubliminalService.cs:175-183</c> side by side:</para>
///
/// <code>
/// // FlashService.cs:549-555               // SubliminalService.cs:175-183
/// baseFreq  = Math.Max(1, FlashFrequency); freq      = Math.Max(1, SubliminalFrequency);
/// base      = 3600.0 / baseFreq;           base      = 60.0 / freq;
/// variance  = base * 0.3;                  variance  = base * 0.3;
/// interval  = base + U(-var, +var);        interval  = base + U(-var, +var);
/// interval  = Math.Max(3, interval);       interval  = Math.Max(1, interval);
/// </code>
///
/// <para>Thirteen more modules follow. Copying those five lines per module is how a floor or a
/// variance band silently drifts in one of them, so the arithmetic lives HERE once and each module
/// contributes only its three numbers. The per-module facades (<see cref="FlashSchedule"/>,
/// <see cref="SubliminalSchedule"/>) stay, because the NUMBERS are the behaviour and each one wants
/// its own citation and its own boundary tests.</para>
/// </summary>
/// <param name="SecondsPerUnit">The seconds in the period the frequency dial counts against: 3600
/// for a per-hour dial (<c>FlashService.cs:550</c>), 60 for a per-minute one
/// (<c>SubliminalService.cs:177</c>).</param>
/// <param name="VarianceFraction">The half-width of the uniform band, as a fraction of the base
/// interval. Both ported modules use 0.3.</param>
/// <param name="MinimumSeconds">The floor, applied LAST — after the variance, never before
/// (<c>FlashService.cs:555</c>, <c>SubliminalService.cs:181</c>).</param>
public readonly record struct IntervalLaw(double SecondsPerUnit, double VarianceFraction, double MinimumSeconds);

/// <summary>
/// The shared pacing arithmetic every paced module runs on. Pure: no clock, no session, no state —
/// which is what lets a formula be pinned exactly at its boundaries.
/// </summary>
public static class EffectSchedule
{
    /// <summary>
    /// The unvaried spacing implied by the dial: <c>secondsPerUnit / max(1, frequency)</c>. The
    /// <c>max(1, …)</c> is upstream's, in both services (<c>FlashService.cs:549</c>,
    /// <c>SubliminalService.cs:175</c>), and it is kept even though both persisted dials already
    /// clamp at 1 — it is the guard that stops this function depending on its caller's clamp, and
    /// removing it turns a bad caller into a divide-by-zero or a negative delay.
    /// </summary>
    public static double BaseIntervalSeconds(IntervalLaw law, int frequency) =>
        law.SecondsPerUnit / Math.Max(1, frequency);

    /// <summary>The earliest the next firing can be scheduled for this dial (the floor applies).</summary>
    public static TimeSpan MinimumInterval(IntervalLaw law, int frequency) =>
        TimeSpan.FromSeconds(Math.Max(
            law.MinimumSeconds,
            BaseIntervalSeconds(law, frequency) * (1.0 - law.VarianceFraction)));

    /// <summary>
    /// The latest the next firing can be scheduled for this dial. Advancing a test clock by this is
    /// what makes "a firing is due" deterministic without pinning the random draw.
    /// </summary>
    public static TimeSpan MaximumInterval(IntervalLaw law, int frequency) =>
        TimeSpan.FromSeconds(Math.Max(
            law.MinimumSeconds,
            BaseIntervalSeconds(law, frequency) * (1.0 + law.VarianceFraction)));

    /// <summary>
    /// One interval, in upstream's order: base, then a uniform ± band offset, then the floor. The
    /// floor is applied LAST, so at high frequencies it truncates the bottom of the variance band
    /// rather than shifting the whole band up.
    /// </summary>
    public static TimeSpan NextInterval(IntervalLaw law, int frequency, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var baseInterval = BaseIntervalSeconds(law, frequency);
        var variance = baseInterval * law.VarianceFraction;
        var interval = baseInterval + ((random.NextDouble() * variance * 2) - variance);
        interval = Math.Max(law.MinimumSeconds, interval);
        return TimeSpan.FromSeconds(interval);
    }
}
