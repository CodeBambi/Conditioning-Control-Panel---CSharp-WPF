using System.Diagnostics;

namespace CcpClient.Desktop.Session;

/// <summary>
/// The scripted session's clock seam: <b>two</b> clocks and a timer.
///
/// <para><b>Why two.</b> Upstream's scripted session deliberately reads the wall clock and a
/// monotonic <see cref="Stopwatch"/> and compares them, because a session's remaining time is a
/// number the user is watching and the wall clock moves for reasons that have nothing to do with
/// elapsed time — an NTP correction, a resume from sleep, or a user who set the clock forward on
/// purpose (<c>Services/Session/SessionEngine.cs:75-76</c>, <c>:96-113</c>). A seam with one clock
/// cannot express that comparison, so it cannot be tested, so the guard would be assumed rather
/// than proved. See <see cref="ScriptedSessionRun.Elapsed"/>.</para>
///
/// <para><b>Why this is a THIRD interface</b> beside <see cref="ISessionClock"/> and
/// <see cref="Scheduling.IScheduleClock"/>, for the reason <c>Scheduling/ScheduleClock.cs:5-31</c>
/// already gives at length: <see cref="ISessionClock"/> has roughly two dozen hand-written
/// implementations under <c>client/tests/**</c> plus seventeen module consumers, and adding a
/// member is a change every one of them must absorb. The seams are declared where they are owned;
/// unifying them later would be a refactor with no behaviour in it.</para>
///
/// <para><b>The wall clock here is UTC, and upstream's is local</b> (<c>DateTime.Now</c>,
/// <c>SessionEngine.cs:96</c>). A recorded, deliberate divergence with the same reasoning the
/// scheduler's seam gives (<c>Scheduling/ScheduleClock.cs:14-20</c>): at a daylight-saving
/// transition a local reading jumps an hour, and upstream's session survives that only BECAUSE the
/// jump guard catches it. Reading UTC means the DST hour is not a jump at all, so the session's
/// clock is right before the guard is consulted rather than because of it. Every other jump the
/// guard exists for — NTP, sleep/resume, a hand-set clock — moves UTC too and is caught exactly as
/// upstream catches it.</para>
/// </summary>
public interface IScriptedClock
{
    /// <summary>The wall clock — upstream's <c>DateTime.Now</c> (<c>SessionEngine.cs:96</c>), in
    /// UTC. It can jump; that is the point.</summary>
    DateTimeOffset Now { get; }

    /// <summary>
    /// A monotonic reading — upstream's <c>_wallClockStopwatch.Elapsed</c>
    /// (<c>SessionEngine.cs:98</c>). Only DIFFERENCES between two readings are meaningful; the
    /// origin is arbitrary, exactly as a <see cref="Stopwatch"/>'s is.
    /// </summary>
    TimeSpan Monotonic { get; }

    /// <summary>
    /// One-shot scheduled callback; <paramref name="due"/> &lt;= 0 fires as soon as possible.
    /// Disposing the handle cancels it. Same contract as <see cref="ISessionClock.Schedule"/>,
    /// deliberately, so <see cref="ScheduledFire"/> works against either.
    /// </summary>
    IDisposable Schedule(TimeSpan due, Action fire);
}

/// <summary>
/// The real clock: <see cref="DateTimeOffset.UtcNow"/>, a process-wide <see cref="Stopwatch"/>,
/// and <see cref="System.Threading.Timer"/>.
///
/// <para>The fault containment is the one both sibling clocks already carry
/// (<c>Session/SessionClock.cs:46-71</c>, <c>Scheduling/ScheduleClock.cs:70-93</c>): a timer
/// callback runs on a pool thread with no caller above it, so an exception escaping it terminates
/// the process. It is caught and REPORTED, never swallowed.</para>
/// </summary>
/// <param name="onCallbackFault">Where a faulting scheduled callback is reported. Null contains the
/// fault unreported, which is only correct for a caller with no log.</param>
public sealed class SystemScriptedClock(Action<Exception>? onCallbackFault = null) : IScriptedClock
{
    private readonly Stopwatch _monotonic = Stopwatch.StartNew();

    /// <inheritdoc/>
    public DateTimeOffset Now => DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public TimeSpan Monotonic => _monotonic.Elapsed;

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
            // Reported, not swallowed, and deliberately not re-thrown: there is nothing above this
            // frame to catch it, so re-throwing is the process kill this exists to prevent.
            onCallbackFault?.Invoke(ex);
        }
    }
}
