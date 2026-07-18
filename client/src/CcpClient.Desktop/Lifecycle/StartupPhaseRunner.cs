namespace CcpClient.Desktop.Lifecycle;

/// <summary>One named startup phase (contract §1). The delegate observes the shared cancellation token.</summary>
public sealed record StartupPhase(string Name, Func<CancellationToken, Task<StartupOutcome>> Run)
{
    /// <summary>Wraps a synchronous phase body.</summary>
    public static StartupPhase FromSync(string name, Func<CancellationToken, StartupOutcome> run) =>
        new(name, ct => Task.FromResult(run(ct)));
}

/// <summary>User-visible record of phase outcomes (contract §10 integration proof).</summary>
public sealed class StartupTrace
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public void Add(string entry) => _entries.Add(entry);
}

/// <summary>
/// Runs startup phases in order with a shared token (contract §1, §3).
/// Cancellation before phase N means phases N..last never run. An exception escaping a
/// phase is trapped once here and converted to <see cref="InitFailureKind.Fatal"/>; nothing
/// else swallows it.
/// </summary>
public static class StartupPhaseRunner
{
    public static async Task<StartupOutcome> RunAsync(
        IReadOnlyList<StartupPhase> phases,
        StartupTrace trace,
        CancellationToken cancellationToken)
    {
        foreach (var phase in phases)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                trace.Add($"{phase.Name}: cancelled (not run)");
                return StartupOutcome.Cancelled.Instance;
            }

            StartupOutcome outcome;
            try
            {
                outcome = await phase.Run(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                trace.Add($"{phase.Name}: cancelled");
                return StartupOutcome.Cancelled.Instance;
            }
            catch (Exception ex)
            {
                var failure = new InitFailure(phase.Name, InitFailureKind.Fatal, $"Unhandled exception: {ex.Message}", ex);
                trace.Add($"{phase.Name}: failed (Fatal): {failure.Reason}");
                return new StartupOutcome.Failed(failure);
            }

            switch (outcome)
            {
                case StartupOutcome.Success:
                    trace.Add($"{phase.Name}: ok");
                    break;
                case StartupOutcome.Cancelled:
                    trace.Add($"{phase.Name}: cancelled");
                    return outcome;
                case StartupOutcome.Failed failed:
                    trace.Add($"{phase.Name}: failed ({failed.Failure.Kind}): {failed.Failure.Reason}");
                    return outcome;
            }
        }

        return StartupOutcome.Success.Instance;
    }
}
