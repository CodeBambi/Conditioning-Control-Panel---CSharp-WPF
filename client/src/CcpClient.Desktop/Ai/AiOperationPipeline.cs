using System.Diagnostics;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Ai;

/// <summary>
/// The AI operation pipeline (admission §8 slice c1; contract §§1–4, 11). Every
/// interactive/awareness operation is an SP-004 owned operation on the pipeline's single
/// <see cref="AsyncOperationOwner"/> — no new async machinery.
///
/// Semantics fixed here:
/// - Switch = generation invalidation → token cancellation → stale-application discard
///   (contract §3 rule 2): <see cref="SelectProvider"/> begins a new generation; a reply
///   started under provider A can never be displayed/executed/remembered after a switch
///   to B. The point-of-application check is <see cref="AsyncOperationOwner.IsLive"/>
///   (generation current AND not cancelled — IsCurrent alone is insufficient because
///   Cancel() does not invalidate application; pre-approach consult, record.md §4.1).
/// - Selection ≠ availability (contract §3 rule 3): readiness is the SP-006 capability
///   state named "ai.provider.{id}"; registration/selection/OS checks never yield
///   availability. A selected-but-unproven provider yields AiReply.Unavailable
///   (provider-unproven). The cloud descriptor registers with NO implementation and a
///   credentials-absent probe — typed absence is live product behavior (admission §2
///   rule 6); no credentials exist and none are invented.
/// - Offline = zero network (contract §11 rule 3): admission checks short-circuit BEFORE
///   <see cref="SendAttempts"/> is incremented, which happens exactly once per operation
///   at the single provider-I/O gateway. With no proven provider the counter stays 0.
/// - Panic (contract §2 rule 3): <see cref="PanicAsync"/> cancels the generation and
///   drains this pipeline's outstanding completions with a bound. c2 re-verifies live
///   against a real network operation; c7 carries the UI-quiet headed proof.
///
/// Construction starts nothing (SP-003): the owner's first generation is armed lazily on
/// first use (<see cref="EnsureArmed"/>).
/// </summary>
public sealed class AiOperationPipeline
{
    private readonly CapabilityRegistry _capabilities;
    private readonly IAiEndpointAdmissionPolicy _admissionPolicy;
    private readonly IAiDiagnosticsSink _diagnostics;
    private readonly AsyncOperationOwner _owner;
    private readonly object _gate = new();
    private readonly Dictionary<AiProviderId, IAiProvider> _providers = [];
    private readonly Dictionary<AiProviderId, AiProviderDescriptor> _descriptors = [];
    private readonly List<Task<OperationOutcome>> _outstanding = [];
    private AiProviderId? _selected;
    private int _sendAttempts;

    public AiOperationPipeline(
        OperationRegistry registry,
        CapabilityRegistry capabilities,
        IAiEndpointAdmissionPolicy admissionPolicy,
        IAiDiagnosticsSink diagnostics)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _admissionPolicy = admissionPolicy ?? throw new ArgumentNullException(nameof(admissionPolicy));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _owner = registry.OwnerFor("Ai");
    }

    /// <summary>Outbound send attempts performed through the single provider-I/O gateway. The offline zero-network proof instrument (contract §11 rule 3).</summary>
    public int SendAttempts => Volatile.Read(ref _sendAttempts);

    /// <summary>The currently selected provider (a settings-level fact — selection never claims availability).</summary>
    public AiProviderId? Selected
    {
        get { lock (_gate) { return _selected; } }
    }

    /// <summary>The SP-006 capability name for a provider's readiness state.</summary>
    public static string CapabilityName(AiProviderId id) => $"ai.provider.{id.Value}";

    /// <summary>
    /// Registers a provider implementation. Its probe (if any) is DECLARED with the
    /// capability registry — registration alone never yields Available (SP-006); the probe
    /// must run (CapabilityProbeRunner) before operations through this provider can succeed.
    /// </summary>
    public void RegisterProvider(IAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (_gate)
        {
            _providers[provider.Descriptor.Id] = provider;
            _descriptors[provider.Descriptor.Id] = provider.Descriptor;
        }

        if (provider.Probe is { } probe)
        {
            _capabilities.Register(CapabilityName(provider.Descriptor.Id), probe);
        }
    }

    /// <summary>
    /// Registers an inventory descriptor with NO implementation (contract §6 rule 4:
    /// inventory, not admission). The capability probe reports typed absence — for the
    /// cloud, credentials-absent: no credentials exist and none are invented (admission
    /// §2 rule 6). Selected → typed Unavailable; never a silent fallback, never invented.
    /// </summary>
    public void RegisterDescriptor(AiProviderDescriptor descriptor, CapabilityReason absenceReason)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(absenceReason);
        lock (_gate)
        {
            _descriptors[descriptor.Id] = descriptor;
        }

        _capabilities.Register(CapabilityName(descriptor.Id),
            _ => Task.FromResult<CapabilityState>(new CapabilityState.Unavailable(absenceReason)));
    }

    /// <summary>
    /// Provider switch (contract §3 rule 2): generation invalidation → token cancellation →
    /// stale-application discard. In-flight work under the old provider is cancelled; any
    /// racing completion is discarded at the point of application. The switch itself
    /// performs NO network call. Memory is never implicitly cleared (contract §5 rule 3).
    /// </summary>
    public void SelectProvider(AiProviderId? id)
    {
        lock (_gate)
        {
            _selected = id;
        }

        _owner.Begin();
    }

    /// <summary>Interactive operation (contract §4): user-initiated; failures surface as typed results, never silent drops.</summary>
    public Task<AiOperationResult> RunInteractiveAsync(AiRequest request) =>
        RunCoreAsync(AiOperationClass.Interactive, request);

    /// <summary>
    /// Awareness operation (contract §4): consent is code-enforced at admission. A
    /// suppressed operation terminates Completed with a typed Suppressed result —
    /// observable, never a silent no-op. (Cooldown machinery lands in c5.)
    /// </summary>
    public Task<AiOperationResult> RunAwarenessAsync(AiRequest request, bool awarenessConsent)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!awarenessConsent)
        {
            var suppressed = new AiOperationResult(
                OperationOutcome.Completed.Instance,
                null,
                new AiAdmission.Suppressed(AiSuppressionKind.ConsentDenied));
            Emit(AiOperationClass.Awareness, EndpointClassOf(Selected), AiDiagnosticOutcome.Completed,
                "suppressed:consent-denied", 0, 0);
            return Task.FromResult(suppressed);
        }

        return RunCoreAsync(AiOperationClass.Awareness, request);
    }

    /// <summary>
    /// Panic/owner-stop (contract §2 rule 3): cancels the current generation; in-flight AI
    /// work terminates with typed Cancelled and is drained with a bound. After panic the
    /// owner stays cancelled — subsequent operations terminate Cancelled until the next
    /// SelectProvider begins a new generation (asserted by tests).
    /// </summary>
    public async Task PanicAsync(TimeSpan boundedWait)
    {
        _owner.Cancel();
        Task<OperationOutcome>[] mine;
        lock (_gate)
        {
            mine = _outstanding.ToArray();
        }

        if (mine.Length > 0)
        {
            // Owned completions never fault (outcomes are typed), so WhenAll cannot throw.
            await Task.WhenAny(Task.WhenAll(mine), Task.Delay(boundedWait)).ConfigureAwait(false);
        }
    }

    private async Task<AiOperationResult> RunCoreAsync(AiOperationClass operationClass, AiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = Stopwatch.GetTimestamp();

        // Admission order (pre-approach consult, record.md §4.1): selected? → capability
        // proven? → admission policy (pre-socket). Each short-circuit is a typed result;
        // SendAttempts is untouched — offline = zero network (contract §11 rule 3).
        var selected = Selected;
        if (selected is null)
        {
            return Unavailable(operationClass, AiEndpointClass.Unspecified, AiReplyCodes.NotConfigured, started);
        }

        AiProviderDescriptor descriptor;
        IAiProvider? provider;
        lock (_gate)
        {
            _descriptors.TryGetValue(selected, out descriptor!);
            _providers.TryGetValue(selected, out provider);
        }

        if (descriptor is null)
        {
            return Unavailable(operationClass, AiEndpointClass.Unspecified, AiReplyCodes.NotConfigured, started);
        }

        if (_capabilities.GetState(CapabilityName(selected)) is not CapabilityState.Available)
        {
            return Unavailable(operationClass, descriptor.EndpointClass, AiReplyCodes.ProviderUnproven, started);
        }

        if (!_admissionPolicy.IsAdmitted(descriptor.EndpointClass))
        {
            return Unavailable(operationClass, descriptor.EndpointClass, AiReplyCodes.EndpointNotAdmitted, started);
        }

        if (provider is null)
        {
            // Inventory descriptor without implementation (cloud typed absence).
            return Unavailable(operationClass, descriptor.EndpointClass, AiReplyCodes.NotConfigured, started);
        }

        // The ONLY I/O path: an SP-004 owned operation under the current generation.
        EnsureArmed();
        AiReply? reply = null;
        var completion = _owner.RunAsync($"ai.{operationClass.ToString().ToLowerInvariant()}", async token =>
        {
            // Captured inside the body: if a switch races registration, the direction is
            // safe — a newer generation over-discards, never over-applies (record.md §4.1).
            var generation = _owner.Generation;
            Interlocked.Increment(ref _sendAttempts);
            var produced = await provider.CompleteAsync(request, token).ConfigureAwait(false);

            // The point of application (contract §2 rule 2): a stale or post-cancel
            // completion is DISCARDED — never displayed, executed, or remembered.
            if (!_owner.IsLive(generation) || token.IsCancellationRequested)
            {
                return OperationOutcome.Cancelled.Instance;
            }

            reply = produced;
            return OperationOutcome.Completed.Instance;
        });

        Track(completion);
        var outcome = await completion.ConfigureAwait(false);
        var appliedReply = outcome is OperationOutcome.Completed ? reply : null;
        Emit(operationClass, descriptor.EndpointClass, OutcomeOf(outcome, appliedReply),
            StableCodeOf(outcome, appliedReply), ElapsedMs(started), _owner.Generation);
        return new AiOperationResult(outcome, appliedReply, AiAdmission.Admitted.Instance);
    }

    private AiOperationResult Unavailable(AiOperationClass operationClass, AiEndpointClass endpointClass, string code, long started)
    {
        Emit(operationClass, endpointClass, AiDiagnosticOutcome.Unavailable, code, ElapsedMs(started), _owner.Generation);
        return new AiOperationResult(
            OperationOutcome.Completed.Instance,
            new AiReply.Unavailable(code),
            AiAdmission.Admitted.Instance);
    }

    private void EnsureArmed()
    {
        if (_owner.Generation < 0)
        {
            _owner.Begin();
        }
    }

    private void Track(Task<OperationOutcome> completion)
    {
        lock (_gate)
        {
            _outstanding.Add(completion);
        }

        _ = completion.ContinueWith(
            t =>
            {
                lock (_gate)
                {
                    _outstanding.Remove(t);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private AiEndpointClass EndpointClassOf(AiProviderId? id)
    {
        if (id is null)
        {
            return AiEndpointClass.Unspecified;
        }

        lock (_gate)
        {
            return _descriptors.TryGetValue(id, out var d) ? d.EndpointClass : AiEndpointClass.Unspecified;
        }
    }

    private void Emit(AiOperationClass operationClass, AiEndpointClass endpointClass, AiDiagnosticOutcome outcome, string? stableCode, long durationMs, int generation)
    {
        // Content-free by construction (contract §12): classes, typed outcome, stable code,
        // generation, duration, zero command data. Never text, never payloads.
        _diagnostics.Emit(new AiDiagnosticRecord(
            operationClass, endpointClass, outcome, stableCode, generation, durationMs, 0, []));
    }

    private static AiDiagnosticOutcome OutcomeOf(OperationOutcome outcome, AiReply? reply) => outcome switch
    {
        OperationOutcome.Cancelled => AiDiagnosticOutcome.Cancelled,
        OperationOutcome.Failed => AiDiagnosticOutcome.Faulted,
        _ => reply switch
        {
            AiReply.Refused => AiDiagnosticOutcome.Refused,
            AiReply.Unavailable => AiDiagnosticOutcome.Unavailable,
            AiReply.Fallback => AiDiagnosticOutcome.Fallback,
            _ => AiDiagnosticOutcome.Completed,
        },
    };

    private static string? StableCodeOf(OperationOutcome outcome, AiReply? reply) => outcome switch
    {
        OperationOutcome.Failed failed => failed.Exception?.GetType().Name ?? "exception",
        _ => reply switch
        {
            AiReply.Unavailable u => u.Code,
            AiReply.Fallback f => f.Code,
            _ => null,
        },
    };

    private static long ElapsedMs(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
