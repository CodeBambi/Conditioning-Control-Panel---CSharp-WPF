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

/// <summary>
/// The real clock, on <see cref="System.Threading.Timer"/>.
///
/// <para><b>A faulting callback must not take the process with it (SP-101).</b> A timer callback
/// runs on a thread-pool thread with no caller above it, so an exception escaping it is an
/// UNHANDLED exception and .NET terminates the process — from a module's scheduler, with no
/// diagnostic and no stack anyone sees. Measured, not theorised: the first fact written against
/// this class killed the test host outright. Fifteen modules will pace on this seam and every one
/// of them will do real work inside its callback.</para>
///
/// <para>So the fault is CAUGHT and REPORTED, never swallowed: the exception is handed to
/// <c>onCallbackFault</c>, which the session participant wires to the host log. A silent catch
/// would be the worse half of the same defect — the module would simply stop working and nothing
/// would say why.</para>
/// </summary>
/// <param name="onCallbackFault">Where a faulting scheduled callback is reported. Null means the
/// fault is contained but unreported, which is only correct for a caller that has no log — every
/// product path supplies one.</param>
public sealed class SystemSessionClock(Action<Exception>? onCallbackFault = null) : ISessionClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public IDisposable Schedule(TimeSpan due, Action fire)
    {
        ArgumentNullException.ThrowIfNull(fire);
        var ms = Math.Max(0, (long)due.TotalMilliseconds);
        return new Timer(_ => Run(fire), null, ms, Timeout.Infinite);
    }

    private void Run(Action fire)
    {
        try
        {
            fire();
        }
        catch (Exception ex)
        {
            // Reported, not swallowed. And deliberately not re-thrown: there is nothing above this
            // frame to catch it, so re-throwing is exactly the process kill this exists to prevent.
            onCallbackFault?.Invoke(ex);
        }
    }
}
