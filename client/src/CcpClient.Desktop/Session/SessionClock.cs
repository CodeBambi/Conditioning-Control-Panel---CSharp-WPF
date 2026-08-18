namespace CcpClient.Desktop.Session;

/// <summary>
/// The session spine's injectable clock/timer seam. Every effect module that paces itself
/// takes one of these and NEVER touches <c>DateTime.Now</c> or a bare <c>Task.Delay</c>, so
/// its pacing is unit-testable with no wall-clock wait at all.
///
/// <para>Shape taken verbatim from the established audio precedent
/// (<see cref="CcpClient.Desktop.Audio.ISoundClock"/>, <c>Audio/AudioSeams.cs:118-137</c>).
/// It is DECLARED here rather than reused so that an effect owning a timer does not have to
/// take a dependency on the audio stack: fourteen more modules follow this spine and most of
/// them make no sound. The two interfaces are structurally identical on purpose — if a later
/// row unifies them, the unification is a refactor with no behaviour in it.</para>
/// </summary>
public interface ISessionClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// One-shot scheduled callback; <paramref name="due"/> &lt;= 0 fires as soon as possible.
    /// Disposing the handle cancels it. On the real clock a disposed timer's callback is
    /// best-effort suppressed, so every callback re-checks its own liveness before doing work
    /// (async-lifecycle-fault-contract §5.5).
    /// </summary>
    IDisposable Schedule(TimeSpan due, Action fire);
}

/// <summary>The real clock, on <see cref="System.Threading.Timer"/>.</summary>
public sealed class SystemSessionClock : ISessionClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public IDisposable Schedule(TimeSpan due, Action fire)
    {
        var ms = Math.Max(0, (long)due.TotalMilliseconds);
        return new Timer(_ => fire(), null, ms, Timeout.Infinite);
    }
}
