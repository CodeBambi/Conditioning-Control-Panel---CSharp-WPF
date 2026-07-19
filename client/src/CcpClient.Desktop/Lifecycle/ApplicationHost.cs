namespace CcpClient.Desktop.Lifecycle;

/// <summary>
/// The single owner of every background participant (contract §5.2): the only caller of
/// <see cref="IBackgroundParticipant.StartAsync"/> (phase 3) and
/// <see cref="IBackgroundParticipant.StopAsync"/> (teardown, §6).
/// </summary>
public sealed class ApplicationHost
{
    private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogSink _log;
    private readonly IReadOnlyList<IBackgroundParticipant> _participants;
    private readonly TimeSpan _drainTimeout;
    private int _shutdownStarted;

    public ApplicationHost(ILogSink log, IReadOnlyList<IBackgroundParticipant> participants, StartupTrace trace)
        : this(log, participants, trace, new OperationRegistry(), new UiDispatchBoundary(), DefaultDrainTimeout)
    {
    }

    public ApplicationHost(
        ILogSink log,
        IReadOnlyList<IBackgroundParticipant> participants,
        StartupTrace trace,
        OperationRegistry registry,
        UiDispatchBoundary uiDispatch,
        TimeSpan? drainTimeout = null)
    {
        _log = log;
        _participants = participants;
        Trace = trace;
        Registry = registry;
        UiDispatch = uiDispatch;
        _drainTimeout = drainTimeout ?? DefaultDrainTimeout;
    }

    public IReadOnlyList<IBackgroundParticipant> Participants => _participants;

    /// <summary>The registry owning every participant's async operations (async contract §1).</summary>
    public OperationRegistry Registry { get; }

    /// <summary>The late-bound UI dispatch boundary (async contract §5).</summary>
    public UiDispatchBoundary UiDispatch { get; }

    /// <summary>Phase 4 binding of the real dispatch boundary (async contract §5.2). Never SynchronizationContext capture.</summary>
    public void BindUiDispatch(IUiDispatch dispatch) => UiDispatch.Bind(dispatch);

    /// <summary>Phase-outcome trace displayed by the placeholder window (contract §10.1).</summary>
    public StartupTrace Trace { get; }

    public bool IsShutdown => Volatile.Read(ref _shutdownStarted) != 0;

    /// <summary>
    /// Phase 3 body: explicitly start each registered participant in registration order.
    /// A start failure is a typed Fatal outcome, not an escaped exception.
    /// </summary>
    public async Task<StartupOutcome> StartParticipantsAsync(CancellationToken cancellationToken)
    {
        foreach (var participant in _participants)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return StartupOutcome.Cancelled.Instance;
            }

            try
            {
                await participant.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return StartupOutcome.Cancelled.Instance;
            }
            catch (Exception ex)
            {
                return new StartupOutcome.Failed(new InitFailure(
                    "CoreServices", InitFailureKind.Fatal, $"Participant '{participant.Name}' failed to start: {ex.Message}", ex));
            }
        }

        return StartupOutcome.Success.Instance;
    }

    /// <summary>
    /// The single idempotent teardown entry point (SP-003 contract §6; async contract §6).
    /// Runs exactly once per process; every later invocation from any path is a no-op.
    /// Order: cancel every generation and drain every owned completion (bounded wait is a
    /// backstop only; cancellation terminates well-behaved operations), then stop
    /// participants in reverse start order. Never throws: unobserved operations are
    /// recorded in registry state, and one participant's stop failure is logged while
    /// teardown continues to the rest. The settings-flush ordering slot is reserved at
    /// the head of this sequence for row 4. There is no second teardown path for async work.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        // ponytail: settings flush slot goes here when row 4 lands (contract §6 rule 5).

        // Async contract §6 steps 1-3: cancel generations, await owned completions,
        // record unobserved (never throw).
        await Registry.CancelAndDrainAsync(_log, _drainTimeout).ConfigureAwait(false);

        for (var i = _participants.Count - 1; i >= 0; i--)
        {
            try
            {
                await _participants[i].StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Log($"teardown: participant '{_participants[i].Name}' stop failed: {ex.Message}");
            }
        }
    }
}
