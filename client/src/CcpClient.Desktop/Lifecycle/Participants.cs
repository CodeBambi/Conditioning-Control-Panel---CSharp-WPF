namespace CcpClient.Desktop.Lifecycle;

/// <summary>
/// Anything with a start/stop lifecycle that outlives a single call (contract §5).
/// Implementations must be idempotent: repeated start/stop are no-ops, and stopping a
/// never-started participant is a no-op.
/// </summary>
public interface IBackgroundParticipant
{
    string Name { get; }

    bool Running { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}

/// <summary>
/// Demonstrator participant proving the ownership rule end-to-end (SP-003 contract §5.4)
/// and the async/fault shape (async-lifecycle-fault-contract): its tick loop is a
/// registered operation on its own generation, and one user-visible update (the tick
/// text in the placeholder window) flows through the real dispatch boundary. It is not a
/// product feature. Construction starts nothing (SP-003 contract §4.4).
/// </summary>
public sealed class HeartbeatParticipant : IBackgroundParticipant
{
    private readonly AsyncOperationOwner _owner;
    private readonly UiDispatchBoundary _ui;
    private readonly TimeSpan _interval;
    private int _tickCount;

    public HeartbeatParticipant(AsyncOperationOwner owner, UiDispatchBoundary ui, TimeSpan? interval = null)
    {
        _owner = owner;
        _ui = ui;
        _interval = interval ?? TimeSpan.FromMilliseconds(250);
    }

    public string Name => "Heartbeat";

    public bool Running { get; private set; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public int TickCount => Volatile.Read(ref _tickCount);

    /// <summary>The tick loop's owned completion (contract §1: every required op's completion is owned and observed).</summary>
    public Task<OperationOutcome>? Completion { get; private set; }

    /// <summary>Set by the window (phase 4). Invoked only on the UI thread, inside the boundary post.</summary>
    public Action<string>? TickReporter { get; set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Running)
        {
            return Task.CompletedTask;
        }

        Running = true;
        StartCount++;
        var generation = _owner.Begin();
        Completion = _owner.RunAsync("tick", token => TickLoopAsync(generation, token));
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!Running)
        {
            return Task.CompletedTask;
        }

        Running = false;
        StopCount++;
        _owner.Cancel();
        return Task.CompletedTask;
    }

    private async Task<OperationOutcome> TickLoopAsync(int generation, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var tick = Interlocked.Increment(ref _tickCount);
            // Skip-until-bound (contract §5.3): the participant starts in phase 3, the
            // boundary binds in phase 4; projection is skipped, never faulted, until then.
            if (_ui.IsBound && _owner.IsLive(generation))
            {
                var text = $"Heartbeat: tick {tick}";
                _ui.Post(() =>
                {
                    // Stale check inside the delegate on the UI thread (contract §5.5):
                    // a late or never-run post during teardown is harmless.
                    if (_owner.IsLive(generation))
                    {
                        TickReporter?.Invoke(text);
                    }
                });
            }

            await Task.Delay(_interval, token).ConfigureAwait(false);
        }

        return OperationOutcome.Completed.Instance;
    }
}
