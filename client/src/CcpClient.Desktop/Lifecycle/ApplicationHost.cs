namespace CcpClient.Desktop.Lifecycle;

/// <summary>
/// The single owner of every background participant (contract §5.2): the only caller of
/// <see cref="IBackgroundParticipant.StartAsync"/> (phase 3) and
/// <see cref="IBackgroundParticipant.StopAsync"/> (teardown, §6).
/// </summary>
public sealed class ApplicationHost
{
    private readonly ILogSink _log;
    private readonly IReadOnlyList<IBackgroundParticipant> _participants;
    private int _shutdownStarted;

    public ApplicationHost(ILogSink log, IReadOnlyList<IBackgroundParticipant> participants, StartupTrace trace)
    {
        _log = log;
        _participants = participants;
        Trace = trace;
    }

    public IReadOnlyList<IBackgroundParticipant> Participants => _participants;

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
    /// The single idempotent teardown entry point (contract §6). Runs exactly once per
    /// process; every later invocation from any path is a no-op. Stops participants in
    /// reverse start order and never throws: one participant's stop failure is logged and
    /// teardown continues to the rest. The settings-flush ordering slot is reserved at the
    /// head of this sequence for row 4.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        // ponytail: settings flush slot goes here when row 4 lands (contract §6 rule 5).

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
