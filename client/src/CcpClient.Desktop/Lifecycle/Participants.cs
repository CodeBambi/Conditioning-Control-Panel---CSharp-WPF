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
/// Demonstrator no-op participant proving the ownership rule end-to-end (contract §5.4).
/// It does no work and is not a product feature; start/stop counts let tests prove the
/// exactly-once teardown guarantee. Construction starts nothing (contract §4.4).
/// </summary>
public sealed class HeartbeatParticipant : IBackgroundParticipant
{
    public string Name => "Heartbeat";

    public bool Running { get; private set; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Running)
        {
            return Task.CompletedTask;
        }

        Running = true;
        StartCount++;
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
        return Task.CompletedTask;
    }
}
