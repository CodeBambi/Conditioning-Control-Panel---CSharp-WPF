using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Scheduling;

/// <summary>
/// The scheduler's APP-LIFETIME owner: the participant that keeps a tick on the clock while no
/// session exists.
///
/// <para><b>Why this is a participant and not part of the session.</b> Every other
/// ported row lives inside <see cref="SessionEngine.Effects"/> and is armed by START. This one has
/// to run when nothing is running — that is the whole feature — so it is owned where the app is
/// owned. WPF's is owned the same way: the timer is a <c>MainWindow</c> field created during
/// window construction (<c>MainWindow/MainWindow.xaml.cs:206</c>, <c>:616-620</c>), started after a
/// grace (<c>:634</c>) and stopped when the window closes
/// (<c>MainWindow/MainWindow.WindowChrome.cs:166</c>).</para>
///
/// <para><b>No second lifetime model.</b> This is the port's existing
/// <see cref="IBackgroundParticipant"/>, registered LAST in
/// <c>CompositionRoot.DefaultParticipants</c>, which buys four properties from machinery that
/// already exists rather than inventing them: phase 3 starts it AFTER the session's preset load,
/// because registration order is start order; teardown stops it FIRST, because participant stop is
/// reverse order, so the poll is dead before the session tears down; its store flushes in the
/// reserved pre-drain slot; and its poll runs on an owned generation, so the host's
/// cancel-and-drain kills it too.</para>
///
/// <para><b>Construction starts nothing</b> (<c>startup-shutdown-contract.md</c> §4.4). Nothing here can begin a
/// session until phase 3 has started the participant AND the 60-second grace has elapsed.</para>
/// </summary>
public sealed class SchedulerParticipant : IBackgroundParticipant
{
    private readonly AsyncOperationOwner _owner;
    private readonly ILogSink _log;
    private readonly PersistenceStore<SchedulerPresetDocument> _store;
    private readonly IScheduleClock _clock;

    private ScheduledFire? _pending;
    private int _generation = -1;
    private bool _running;
    private bool _gracePassed;

    /// <param name="infra">The participant infrastructure: the operation owner, the late-bound UI
    /// boundary and the log.</param>
    /// <param name="dataDirectory">Where the ten settings live, beside every other document.</param>
    /// <param name="engine">The REAL session this scheduler owns from outside.</param>
    /// <param name="clock">The LOCAL clock. Defaults to the real one; a test injects a manual one so
    /// nothing in the suite waits sixty seconds on a wall clock.</param>
    /// <param name="onSignalThread">Signal-thread test (see <see cref="EffectSignal"/>).</param>
    public SchedulerParticipant(
        ParticipantInfrastructure infra,
        string dataDirectory,
        SessionEngine engine,
        IScheduleClock? clock = null,
        Func<bool>? onSignalThread = null)
    {
        ArgumentNullException.ThrowIfNull(infra);
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        ArgumentNullException.ThrowIfNull(engine);

        _owner = infra.OwnerFor("Scheduler");
        _log = infra.Log;
        _clock = clock ?? new SystemScheduleClock(ex =>
            infra.Log.Log($"scheduler: a scheduled callback faulted and was contained ({ex.GetType().Name}: {ex.Message})"));

        _store = new PersistenceStore<SchedulerPresetDocument>(
            infra.OwnerFor("SchedulerPreset"), infra.Log,
            Path.Combine(dataDirectory, SchedulerPresetDocument.FileName),
            SchedulerPresetDocument.CurrentSchemaVersion);

        Scheduler = new SessionScheduler(
            _store, engine, _clock, new EffectSignal(infra.UiDispatch, onSignalThread));
    }

    /// <inheritdoc/>
    public string Name => "Scheduler";

    /// <inheritdoc/>
    public bool Running => _running;

    /// <summary>The decision machine. The shell drives its manual-toggle note and renders its dot;
    /// nothing else may touch it.</summary>
    public SessionScheduler Scheduler { get; }

    /// <summary>The persisted settings store (public so the panel can save a box it ticked, the
    /// same shape every module's store has).</summary>
    public PersistenceStore<SchedulerPresetDocument> Preset => _store;

    /// <summary>True once the 60-second start-up grace has elapsed and the poll has really begun.
    /// Before that the scheduler cannot act at all, which is what its dot reports.</summary>
    public bool GracePassed => Volatile.Read(ref _gracePassed);

    /// <summary>
    /// Whether this participant's operation generation is still live. It is the participant
    /// contract's own property — stop cancels the generation (<c>startup-shutdown-contract.md</c> §5.3) — and it is the second
    /// half of the guard every scheduled callback re-checks before it does anything
    /// (<c>_running</c> being the first). Exposed because a scheduler that can start a session is
    /// the one participant whose "am I still allowed to act" answer is worth being able to ask
    /// from outside.
    /// </summary>
    public bool GenerationLive => _owner.IsLive(_generation);

    /// <inheritdoc/>
    /// <remarks>
    /// Phase 3 loads the ten settings and arms the GRACE, and nothing else. WPF delays the
    /// scheduler by 60 s for a reason it states at <c>MainWindow/MainWindow.xaml.cs:622-623</c> —
    /// "prevents issues when restarting after an update while in a scheduled time window" — which
    /// is the difference between a user relaunching mid-window and being conditioned the instant
    /// the splash clears.
    /// </remarks>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_running)
        {
            return;
        }

        await _store.StartAsync(cancellationToken).ConfigureAwait(false);
        if (_store.LastLoadOutcome is { IsDegraded: true } outcome)
        {
            // Typed Degraded, never silent — and it matters more here than on a dial: a quarantined
            // file means the scheduler is running on DEFAULTS, and the default day set is all seven.
            _log.Log($"scheduler-preset: load → {outcome.GetType().Name} (typed Degraded — "
                + "the scheduler will run on default settings, which are all seven days, 00:00-22:00, DISABLED)");
        }

        _running = true;
        _generation = _owner.Begin();
        Arm(SessionScheduler.StartupGrace);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Teardown, and the reason this participant is registered last: it stops FIRST, so the poll is
    /// gone before <c>SessionParticipant.StopAsync</c> takes the session down. A scheduler still
    /// ticking into a halted session is the shape WPF guards twice —
    /// <c>_schedulerTimer?.Stop()</c> at close (<c>MainWindow/MainWindow.WindowChrome.cs:166</c>)
    /// and the <c>HasShutdownStarted</c> check inside the grace continuation
    /// (<c>MainWindow/MainWindow.xaml.cs:629</c>).
    /// </remarks>
    public Task StopAsync()
    {
        if (!_running)
        {
            return Task.CompletedTask;
        }

        _running = false;
        Interlocked.Exchange(ref _pending, null)?.Dispose();
        Scheduler.SetPolling(false);
        Volatile.Write(ref _gracePassed, false);
        _owner.Cancel();
        return _store.StopAsync();
    }

    /// <summary>Teardown flush for the reserved pre-drain slot (persistence contract §11): a box a
    /// user ticked on the way out is a persisted setting like any other.</summary>
    public Task FlushAsync(TimeSpan boundedWait) => _store.FlushAsync(boundedWait);

    /// <summary>
    /// Put the next tick on the clock. Published to the pending slot BEFORE the clock is asked,
    /// for the reason <see cref="ScheduledFire"/> gives: a due-immediately schedule can fire before
    /// <see cref="IScheduleClock.Schedule"/> returns.
    /// </summary>
    private void Arm(TimeSpan due)
    {
        var token = new ScheduledFire();
        Interlocked.Exchange(ref _pending, token)?.Dispose();
        token.Attach(_clock.Schedule(due, () => OnDue(token)));

        // The generation may have been cancelled between the check and the schedule. Tear the
        // handle straight back down: a stop must never leave a live one-shot that could start a
        // session after teardown began.
        if (!_running || !_owner.IsLive(_generation))
        {
            Interlocked.Exchange(ref _pending, null)?.Dispose();
            Scheduler.SetPolling(false);
        }
    }

    /// <summary>
    /// One tick comes due. WPF's order is <c>_schedulerTimer.Start()</c> and then
    /// <c>CheckSchedulerOnStartup()</c> (<c>MainWindow/MainWindow.xaml.cs:634-635</c>) — the next
    /// tick is on the clock BEFORE any decision runs, so a decision that throws cannot silently
    /// end the schedule. Reproduced here.
    /// </summary>
    private void OnDue(ScheduledFire token)
    {
        // CompareExchange, not Exchange: clear the slot only if it still holds THIS tick. If it
        // does not, this callback belongs to a schedule that was superseded or cancelled and it
        // does nothing at all (the identity rule).
        if (Interlocked.CompareExchange(ref _pending, null, token) != token)
        {
            return;
        }

        // The liveness re-check every scheduled callback owes (async contract §5.5). This is the
        // guard that stops a tick in flight from starting a session during teardown.
        if (!_running || !_owner.IsLive(_generation))
        {
            Scheduler.SetPolling(false);
            return;
        }

        var firstTick = !Volatile.Read(ref _gracePassed);
        Volatile.Write(ref _gracePassed, true);
        Arm(SessionScheduler.PollInterval);

        // The SECOND liveness check, and it is not belt-and-braces. Arm is two statements plus a
        // clock call, and teardown runs on another thread: the generation can be cancelled between
        // the check above and this line — which is the very window Arm's own post-check guards.
        // Arm tears the new one-shot back down there, but the DECISION below is what starts a
        // conditioning session, so it must be refused too. Found by the fact that reproduces that
        // window (AGenerationCancelledWHILETheClockIsBeingAsked…): without this the tick went on to
        // start a session with the host already draining.
        //
        // WPF's ordering is untouched — the next tick is on the clock before any decision runs
        // (MainWindow.xaml.cs:634-635) — and this is the port's analogue of upstream's own
        // HasShutdownStarted refusal at :629.
        var live = _running && _owner.IsLive(_generation);
        Scheduler.SetPolling(live);
        if (!live)
        {
            return;
        }

        if (firstTick)
        {
            Scheduler.RunStartupCheck();
        }
        else
        {
            Scheduler.Tick();
        }
    }
}
