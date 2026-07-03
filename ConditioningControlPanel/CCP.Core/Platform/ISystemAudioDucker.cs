namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Ducks other applications' audio while CCP audio is playing.
/// Platform support varies; no-ops are acceptable.
/// </summary>
/// <remarks>
/// Semantics mirror the WPF <c>AudioService</c> ducking contract (WS0 lot 4):
/// <list type="bullet">
/// <item><description><see cref="Duck(int)"/> lowers each other application's session volume
/// RELATIVE to its current level: <c>new = current * (1 - strengthPercent/100)</c>.</description></item>
/// <item><description>Duck/Unduck are REFERENCE-COUNTED. Each <see cref="Duck(int)"/> must be
/// paired with one <see cref="Unduck"/>; volumes are only restored when the last consumer
/// releases. Overlapping consumers (video + keyword trigger + remote) must not fight.</description></item>
/// <item><description><see cref="ForceUnduck"/> resets the count and restores immediately
/// (panic/stop-everything paths).</description></item>
/// </list>
/// </remarks>
public interface ISystemAudioDucker
{
    /// <summary>
    /// Duck other applications' audio by the default strength (80% reduction,
    /// matching the WPF <c>DuckingLevel</c> default).
    /// </summary>
    void Duck();

    /// <summary>
    /// Duck other applications' audio, reducing each session's volume by
    /// <paramref name="strengthPercent"/> percent of its current level (0-100).
    /// Default implementation forwards to <see cref="Duck()"/> so legacy
    /// platform implementations keep compiling; real implementations should
    /// override and honor the strength.
    /// </summary>
    void Duck(int strengthPercent) => Duck();

    /// <summary>Release one duck reference; restores volumes when the count reaches zero.</summary>
    void Unduck();

    /// <summary>
    /// Reset the duck count and restore all volumes immediately (panic / session teardown).
    /// Default implementation forwards to <see cref="Unduck"/> for legacy implementations.
    /// </summary>
    void ForceUnduck() => Unduck();
}
