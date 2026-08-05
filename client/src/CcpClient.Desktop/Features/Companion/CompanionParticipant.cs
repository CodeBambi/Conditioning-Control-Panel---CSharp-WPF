using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Companion;

/// <summary>
/// The companion feature participant (SP-046 slice c7; admission §8): the PRODUCT
/// composition point for the full AI chain — pipeline, provider seam, moderation
/// boundary, memory store, awareness service, command executor — owned by ONE
/// participant (the DtrhParticipant precedent: a participant owns feature machinery;
/// construction starts nothing, SP-003). Until this slice no product CompositionRoot
/// constructed the pipeline (SP-040 record §7 item 9).
///
/// Composition facts:
/// - <see cref="Pipeline"/>: the c1 pipeline over the shared capability registry;
///   moderation = the c3 boundary with <see cref="AiModerationPolicy.Empty"/> (the
///   verdict-rejected-shape-only posture — no policy VALUES invented, §9.2 #1);
///   admission = the loopback-only placeholder policy (§9.2 #2 owner-pending);
///   diagnostics = a <see cref="CollectingAiDiagnosticsSink"/> (content-free by
///   construction, contract §12).
/// - Provider: the c2 <see cref="LoopbackOllamaProvider"/> registered (its probe is the
///   ONLY availability authority — SP-006); the cloud is an INVENTORY descriptor with
///   typed absence (credentials-absent — admission §2 rule 6; never selected).
///   Default selection = LocalOllama (recorded decision, SP-046 record.md §2.3: the
///   only admissible endpoint class; selection ≠ availability).
/// - <see cref="Memory"/>: the c4 store with its OWN named owner "AiMemory" (the SP-024
///   lesson), consent read from <see cref="MemoryConsent"/> at every write admission.
/// - <see cref="Awareness"/>: the c5 service; consent + cooldown values are the typed
///   states the surface drives (session-scoped placeholders — never persisted, §9.2
///   #3/#4 owner-pending).
/// - <see cref="Executor"/>: the c6 executor composed with the none-admitted default
///   posture (admission §9.2 #5): the pipeline executes only the owner-admitted subset,
///   default NONE. The c7 surface does NOT dispatch reply commands — no effect backends
///   exist and the admissible set is owner-pending (SP-044 record §3.1.7: dispatch
///   timing is an owner-facing behavior decision). The executor is COMPOSED (the wiring
///   this slice owns), not consumed.
///
/// Session-state holders (<see cref="MemoryConsent"/>) are runtime-only: every default
/// is an owner-pending placeholder, so no persistence schema is decided here (the
/// SP-040 RetentionMaxPairs-null discipline — a persisted value reads as a decision).
/// </summary>
public sealed class CompanionParticipant : IBackgroundParticipant
{
    private readonly AiMemoryStore _memory;

    public CompanionParticipant(
        ParticipantInfrastructure infra,
        CapabilityRegistry capabilities,
        string dataDirectory,
        string? ollamaHostOverride = null,
        IAiProvider? providerOverride = null)
    {
        ArgumentNullException.ThrowIfNull(infra);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        Capabilities = capabilities;

        Diagnostics = new CollectingAiDiagnosticsSink();
        var moderation = new AiModerationBoundary(AiModerationPolicy.Empty);
        _memory = new AiMemoryStore(
            infra.OwnerFor("AiMemory"), infra.Log,
            Path.Combine(dataDirectory, AiMemoryStore.FileName),
            consent: () => MemoryConsent);
        Pipeline = new AiOperationPipeline(
            infra.Registry, capabilities, LoopbackOnlyAdmissionPolicy.Instance, Diagnostics, moderation, _memory);

        // The provider probe registers into the SHARED registry at construction so the
        // CapabilityProbes phase proves it (registration never yields Available — SP-006).
        Pipeline.RegisterProvider(providerOverride ?? new LoopbackOllamaProvider(
            ollamaHostOverride is { Length: > 0 } host
                ? new LoopbackOllamaProviderOptions { Host = new Uri(host, UriKind.Absolute) }
                : null));

        // Cloud = inventory, not admission (contract §6 rule 4): typed absence — no
        // credentials exist and none are invented.
        Pipeline.RegisterDescriptor(
            new AiProviderDescriptor(AiProviderId.Cloud, AiEndpointClass.FirstPartyCloud),
            new CapabilityReason("credentials-absent",
                "cloud provider is inventory only: no credentials exist on this device and none are invented (admission §2 rule 6)"));

        // Default selection (recorded decision, SP-046 record.md §2.3): the only provider
        // whose endpoint class the placeholder policy admits. Selection ≠ availability.
        Pipeline.SelectProvider(AiProviderId.LocalOllama);

        Awareness = new AiAwarenessService(Pipeline, moderation, Diagnostics, capabilities);
        Executor = new AiCommandExecutor();
    }

    public string Name => "Companion";

    public bool Running => _memory.Running;

    /// <summary>The c1 operation pipeline (product-composed by this participant).</summary>
    public AiOperationPipeline Pipeline { get; }

    /// <summary>The c4 memory store (explicit clear is user-reachable via the surface).</summary>
    public AiMemoryStore Memory => _memory;

    /// <summary>The c5 awareness service (consent + cooldown typed states).</summary>
    public AiAwarenessService Awareness { get; }

    /// <summary>The c6 command executor (composed; none-admitted default — see class doc).</summary>
    public AiCommandExecutor Executor { get; }

    /// <summary>The content-free diagnostics sink (contract §12).</summary>
    public CollectingAiDiagnosticsSink Diagnostics { get; }

    /// <summary>The shared capability registry (status surfaces read typed states from here — never registration/selection facts).</summary>
    public CapabilityRegistry Capabilities { get; }

    /// <summary>
    /// The typed memory-consent state the store consults at every write admission
    /// (contract §5 rule 2). Placeholder default = Denied (conservative posture — the
    /// c3-Empty/c6-none-admitted precedent; WPF baseline FACT: ChatMemoryEnabled default
    /// true, CompanionPromptSettings.cs:120; §9.2 #3 owner-pending). Session-scoped.
    /// </summary>
    public AiMemoryConsent MemoryConsent { get; set; } = AiMemoryConsent.Denied;

    public Task StartAsync(CancellationToken cancellationToken) => _memory.StartAsync(cancellationToken);

    public Task StopAsync() => _memory.StopAsync();

    /// <summary>Teardown flush (SP-005 contract §11) — wired into the host's reserved pre-drain slot by the composition root.</summary>
    public Task FlushAsync(TimeSpan boundedWait) => _memory.FlushAsync(boundedWait);
}
