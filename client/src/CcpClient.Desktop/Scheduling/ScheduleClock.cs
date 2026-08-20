namespace CcpClient.Desktop.Scheduling;

/// <summary>
/// The scheduler's clock seam: the LOCAL wall clock, plus a one-shot timer.
///
/// <para><b>Why this is a NEW interface and not two more members on <see cref="Session.ISessionClock"/>.</b>
/// Not tidiness, and not scope avoidance — the two clocks answer different questions and one of
/// them must never be available to a session module.</para>
///
/// <list type="number">
/// <item><b>The scheduler needs LOCAL time and the session spine needs UTC.</b> WPF reads
/// <c>DateTime.Now</c> (<c>MainWindow/MainWindow.StartStop.cs:645</c>) because the user typed
/// "16:00" meaning the clock on their wall; <see cref="Session.ISessionClock.UtcNow"/> is
/// deliberately UTC because a paced effect that read local time would fire twice, or not at all,
/// at 02:00 on a daylight-saving transition. Handing the local reading to the session spine would
/// make that defect merely a typo away, in twelve modules at once.</item>
/// <item><b>The enumeration, done before the decision rather than after.</b>
/// <c>grep -rn ISessionClock client/{src,tests}</c> answers with twelve module and presenter
/// consumers under <c>Effects/**</c>, the shared <see cref="Session.PacedSessionEffect{TFiring}"/>
/// base, <c>CompositionRoot.SessionClockFactory</c>, <c>SessionParticipant</c>, and roughly two
/// dozen hand-written implementations under <c>client/tests/**</c>. Adding a member is a change
/// every one of them must absorb, and <c>Effects/**</c> is closed to the packet that would have
/// had to make it — so the widening was not merely inadvisable, it was unreachable
/// (<c>client/docs/port-workflow.md</c>, the equivalence rule).</item>
/// </list>
///
/// <para>This is the same reasoning <c>Session/SessionClock.cs:7-13</c> already gives for declaring
/// <see cref="Session.ISessionClock"/> separately from <c>Audio.ISoundClock</c> rather than reusing
/// it: structurally similar seams are declared where they are owned, and a later unification would
/// be a refactor with no behaviour in it.</para>
/// </summary>
public interface IScheduleClock
{
    /// <summary>
    /// The local wall clock — WPF's <c>DateTime.Now</c> (<c>MainWindow.StartStop.cs:645</c>),
    /// whose <c>Kind</c> is <see cref="DateTimeKind.Local"/>. The predicate needs exactly two
    /// things from it: <see cref="DateTime.DayOfWeek"/> and <see cref="DateTime.TimeOfDay"/>.
    /// </summary>
    DateTime LocalNow { get; }

    /// <summary>
    /// One-shot scheduled callback; <paramref name="due"/> &lt;= 0 fires as soon as possible.
    /// Disposing the handle cancels it. On the real clock a disposed timer's callback is
    /// best-effort suppressed, so every callback re-checks its own liveness before doing work
    /// (async-lifecycle-fault-contract §5.5). Same contract as
    /// <see cref="Session.ISessionClock.Schedule"/>, deliberately, so
    /// <see cref="Session.ScheduledFire"/> works against either.
    /// </summary>
    IDisposable Schedule(TimeSpan due, Action fire);
}

/// <summary>
/// The real clock, on <see cref="System.Threading.Timer"/>.
///
/// <para><b>A faulting callback must not take the process with it (SP-101, measured there rather
/// than theorised).</b> A timer callback runs on a thread-pool thread with no caller above it, so
/// an exception escaping it is an UNHANDLED exception and .NET terminates the process. That
/// matters more here than anywhere else in the port: this callback runs while nothing else is
/// happening, in an app the user has left running on purpose, and a crash from it would look like
/// the app simply vanishing overnight.</para>
///
/// <para>So the fault is CAUGHT and REPORTED, never swallowed — the same shape
/// <see cref="Session.SystemSessionClock"/> uses, for the same reason.</para>
/// </summary>
/// <param name="onCallbackFault">Where a faulting scheduled callback is reported. Null means the
/// fault is contained but unreported, which is only correct for a caller that has no log — every
/// product path supplies one.</param>
public sealed class SystemScheduleClock(Action<Exception>? onCallbackFault = null) : IScheduleClock
{
    /// <inheritdoc/>
    public DateTime LocalNow => DateTime.Now;

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
