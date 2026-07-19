namespace CcpClient.Desktop.Lifecycle;

/// <summary>
/// The registry of owned async operations (async-lifecycle-fault-contract §1). One owner
/// per background participant, created at composition; the host's single guarded teardown
/// entry point cancels every generation and drains every owned completion (contract §6).
/// Required work is never detached: every operation's completion is tracked here.
/// </summary>
public sealed class OperationRegistry
{
    private readonly object _gate = new();
    private readonly List<AsyncOperationOwner> _owners = [];
    private readonly HashSet<OperationEntry> _outstanding = [];

    /// <summary>Completions discarded because their generation was stale (contract §3.2).</summary>
    public int DiscardedStaleCompletions { get; private set; }

    /// <summary>Operations still incomplete after teardown's bounded drain (contract §6.3).</summary>
    public int UnobservedOperations { get; private set; }

    /// <summary>Operations currently in flight.</summary>
    public int OutstandingOperations
    {
        get { lock (_gate) { return _outstanding.Count; } }
    }

    /// <summary>Creates the single owner for one participant. Composition root calls this once per participant.</summary>
    public AsyncOperationOwner OwnerFor(string participantName)
    {
        lock (_gate)
        {
            var owner = new AsyncOperationOwner(this, participantName);
            _owners.Add(owner);
            return owner;
        }
    }

    internal void Track(OperationEntry entry)
    {
        lock (_gate)
        {
            _outstanding.Add(entry);
        }
    }

    internal void Complete(OperationEntry entry, OperationOutcome outcome)
    {
        lock (_gate)
        {
            _outstanding.Remove(entry);
            // Generation check at the point of application (contract §3.3): a stale
            // completion cannot overwrite a newer generation's state.
            if (entry.Owner.IsCurrent(entry.Generation))
            {
                entry.Owner.SetOutcome(outcome);
            }
            else
            {
                DiscardedStaleCompletions++;
            }
        }
    }

    /// <summary>
    /// Teardown steps 1–3 of contract §6: cancel every generation, await every owned
    /// completion (bounded wait is a backstop only — cancellation terminates well-behaved
    /// operations), then record whatever did not finish. Never throws (SP-003 teardown
    /// invariant); the zero-unobserved guarantee is asserted by tests reading
    /// <see cref="UnobservedOperations"/>.
    /// </summary>
    public async Task CancelAndDrainAsync(ILogSink log, TimeSpan boundedWait)
    {
        AsyncOperationOwner[] owners;
        lock (_gate)
        {
            owners = _owners.ToArray();
        }

        foreach (var owner in owners)
        {
            owner.Cancel();
        }

        Task[] completions;
        lock (_gate)
        {
            // ponytail: an entry with null Completion registered within the last
            // nanoseconds before teardown; it observes the cancelled token like any
            // other op and is counted by the re-snapshot below if still outstanding.
            completions = _outstanding.Select(e => e.Completion).OfType<Task>().Cast<Task>().ToArray();
        }

        if (completions.Length > 0)
        {
            // Owned completion tasks never fault (outcomes are typed), so WhenAll cannot throw.
            await Task.WhenAny(Task.WhenAll(completions), Task.Delay(boundedWait)).ConfigureAwait(false);
        }

        OperationEntry[] leaked;
        lock (_gate)
        {
            leaked = _outstanding.ToArray();
            UnobservedOperations += leaked.Length;
        }

        foreach (var entry in leaked)
        {
            log.Log($"teardown: operation '{entry.Name}' (owner '{entry.Owner.Name}', generation {entry.Generation}) unobserved at teardown");
        }
    }
}

/// <summary>
/// One participant's ownership handle: its monotonic cancellation generation, its
/// operation registration, and its last applied outcome (async-lifecycle-fault-contract
/// §1, §3). Exactly one owner per participant; the generation increments on every
/// (re)start, invalidating all prior in-flight work.
/// </summary>
public sealed class AsyncOperationOwner
{
    private readonly OperationRegistry _registry;
    private readonly object _gate = new();
    private CancellationTokenSource? _generationCts;
    private int _generation = -1; // no live generation until Begin
    private volatile OperationOutcome? _lastOutcome;

    internal AsyncOperationOwner(OperationRegistry registry, string name)
    {
        _registry = registry;
        Name = name;
    }

    public string Name { get; }

    public int Generation
    {
        get { lock (_gate) { return _generation; } }
    }

    /// <summary>The last non-stale outcome applied to this owner (contract §4.3).</summary>
    public OperationOutcome? LastOutcome => _lastOutcome;

    /// <summary>
    /// Starts a new generation: cancels the previous generation's token and increments
    /// the counter. Called by the participant's start path (phase 3); every later
    /// completion captured under an older generation is stale.
    /// </summary>
    public int Begin()
    {
        lock (_gate)
        {
            _generationCts?.Cancel();
            _generationCts?.Dispose();
            _generation++;
            _generationCts = new CancellationTokenSource();
            return _generation;
        }
    }

    /// <summary>Cancels the current generation. Idempotent; does not invalidate outcome application (only a new Begin does).</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _generationCts?.Cancel();
        }
    }

    /// <summary>Outcome-application check (contract §3.3): same generation, regardless of cancellation.</summary>
    internal bool IsCurrent(int generation) => Generation == generation;

    /// <summary>
    /// Projection check for UI posts (contract §5.4/§5.5): same generation AND not
    /// cancelled. A posted delegate runs this on the UI thread and must be harmless when
    /// it answers false (stale, torn down, or never-run post).
    /// </summary>
    public bool IsLive(int generation)
    {
        lock (_gate)
        {
            return _generation == generation && _generationCts is not null && !_generationCts.IsCancellationRequested;
        }
    }

    internal void SetOutcome(OperationOutcome outcome) => _lastOutcome = outcome;

    /// <summary>
    /// Registers and starts an owned operation in the current generation (contract §1).
    /// The returned task is the owned completion: it completes with the operation's
    /// typed terminal outcome and never faults. The registry traps exceptions once, at
    /// this boundary, and classifies them via the owner-supplied
    /// <paramref name="classify"/> (default: Fatal). Required work is never detached;
    /// fire-and-forget callers are banned for required operations (contract §7).
    /// </summary>
    public Task<OperationOutcome> RunAsync(
        string operationName,
        Func<CancellationToken, Task<OperationOutcome>> body,
        Func<Exception, InitFailureKind>? classify = null)
    {
        int generation;
        CancellationToken token;
        lock (_gate)
        {
            if (_generationCts is null)
            {
                throw new InvalidOperationException(
                    $"Owner '{Name}' has no live generation: Begin (phase 3 start) must run before operations are registered.");
            }

            generation = _generation;
            token = _generationCts.Token;
        }

        var entry = new OperationEntry(this, generation, operationName);
        _registry.Track(entry);
        entry.Completion = Task.Run(async () =>
        {
            OperationOutcome outcome;
            try
            {
                outcome = await body(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                outcome = OperationOutcome.Cancelled.Instance;
            }
            catch (Exception ex)
            {
                InitFailureKind kind;
                try
                {
                    kind = classify?.Invoke(ex) ?? InitFailureKind.Fatal;
                }
                catch (Exception classifyEx)
                {
                    kind = InitFailureKind.Fatal;
                    ex = new InvalidOperationException($"classifier threw: {classifyEx.Message}; original: {ex.Message}", ex);
                }

                outcome = new OperationOutcome.Failed(kind, ex.Message, ex);
            }

            _registry.Complete(entry, outcome);
            return outcome;
        });
        return entry.Completion;
    }
}

/// <summary>One registered operation's tracking record.</summary>
internal sealed class OperationEntry(AsyncOperationOwner owner, int generation, string name)
{
    public AsyncOperationOwner Owner { get; } = owner;

    public int Generation { get; } = generation;

    public string Name { get; } = name;

    public Task<OperationOutcome>? Completion { get; set; }
}
