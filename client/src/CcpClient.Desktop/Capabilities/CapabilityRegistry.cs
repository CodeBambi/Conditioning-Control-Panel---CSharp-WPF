using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Capabilities;

/// <summary>
/// The capability registry (runtime-capability-contract §3). Each capability declares its
/// probe at registration; registration alone NEVER yields Available — a registered
/// capability whose probe has not run reports <c>Unavailable(not-probed)</c>, and an
/// unregistered name reports <c>Unavailable(unknown-capability)</c>. The query API never
/// throws-and-claims.
/// </summary>
public sealed class CapabilityRegistry
{
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private readonly Dictionary<string, CapabilityState> _states = new(StringComparer.Ordinal);

    /// <summary>Declares a capability and its probe. The probe is NOT run here (contract §3 rule 1: no constructor-started work).</summary>
    public void Register(string name, Func<CancellationToken, Task<CapabilityState>> probe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(probe);
        lock (_gate)
        {
            _entries.Add(new Entry(name, probe));
        }
    }

    /// <summary>Registered capability names, in registration order.</summary>
    public IReadOnlyList<string> Names
    {
        get { lock (_gate) { return _entries.Select(e => e.Name).ToArray(); } }
    }

    /// <summary>The current typed state. Unknown name → Unavailable(unknown-capability); registered but unprobed → Unavailable(not-probed).</summary>
    public CapabilityState GetState(string name)
    {
        lock (_gate)
        {
            if (_states.TryGetValue(name, out var state))
            {
                return state;
            }

            return _entries.Any(e => e.Name == name)
                ? new CapabilityState.Unavailable(new CapabilityReason(
                    CapabilityReasonCodes.NotProbed, $"'{name}' is registered but its probe has not run"))
                : new CapabilityState.Unavailable(new CapabilityReason(
                    CapabilityReasonCodes.UnknownCapability, $"no capability registered as '{name}'"));
        }
    }

    /// <summary>The declared probes (name + delegate) for the probe runner.</summary>
    internal IReadOnlyList<(string Name, Func<CancellationToken, Task<CapabilityState>> Probe)> Probes()
    {
        lock (_gate)
        {
            return _entries.Select(e => (e.Name, e.Probe)).ToArray();
        }
    }

    /// <summary>Applies a probe-produced state. Probe runner only.</summary>
    internal void Apply(string name, CapabilityState state)
    {
        lock (_gate)
        {
            _states[name] = state;
        }
    }

    private sealed record Entry(string Name, Func<CancellationToken, Task<CapabilityState>> Probe);
}

/// <summary>
/// Runs every registered probe as an SP-004 owned operation (contract §3) and applies the
/// outcome→state mapping — the only bridge across the row-3/row-5 boundary (contract §6):
/// probe-returned state → applied; Failed → Faulted with the exception class; Cancelled →
/// stays not-probed. Probe exceptions are trapped once at the SP-004 registry boundary —
/// never unhandled here, never Available.
/// </summary>
public sealed class CapabilityProbeRunner(AsyncOperationOwner owner, CapabilityRegistry registry)
{
    /// <summary>
    /// Begins the owner's generation, then probes sequentially, observing the startup
    /// cancellation token between probes (a cancelled startup leaves the remaining
    /// capabilities honestly not-probed). Awaits every probe's owned completion, so when
    /// this returns every probed capability's state is final.
    /// </summary>
    public async Task RunAllAsync(CancellationToken cancellationToken)
    {
        owner.Begin();
        foreach (var (name, probe) in registry.Probes())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            CapabilityState? probed = null;
            var outcome = await owner.RunAsync($"probe:{name}", async token =>
            {
                probed = await probe(token).ConfigureAwait(false);
                return OperationOutcome.Completed.Instance;
            }).ConfigureAwait(false);

            switch (outcome)
            {
                case OperationOutcome.Completed:
                    registry.Apply(name, probed ?? new CapabilityState.Faulted(new CapabilityReason(
                        CapabilityReasonCodes.ProbeFault, "probe completed without producing a state")));
                    break;
                case OperationOutcome.Failed failed:
                    registry.Apply(name, new CapabilityState.Faulted(new CapabilityReason(
                        CapabilityReasonCodes.ProbeFault,
                        $"{failed.Exception?.GetType().Name ?? "exception"}: {failed.Reason}")));
                    break;
                case OperationOutcome.Cancelled:
                    // Teardown raced the probe: the honest state is not-probed (contract §3 rule 3).
                    break;
            }
        }
    }
}
