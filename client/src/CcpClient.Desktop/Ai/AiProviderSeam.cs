namespace CcpClient.Desktop.Ai;

// Provider strategy seam (contract §3; admission §2). Providers carry stable typed IDs;
// exactly one is SELECTED at a time (a settings-level fact). Selection NEVER claims
// availability: readiness is an SP-006 capability state, and only a real probe of the
// selected backend yields Available. The rejected shapes are WPF's identity-check
// IsAvailable (Services/AiService.cs:52) and local always-true (LocalAiService.cs:46).

/// <summary>
/// Stable typed provider identifier (contract §3 rule 1). Closed set — new providers land
/// with their consumer slice. <see cref="Cloud"/> is INVENTORY, not admission (contract §6
/// rule 4): no credentials exist and none are invented, so no cloud implementation exists.
/// </summary>
public sealed record AiProviderId
{
    /// <summary>The operated first-party proxy (inventory fact, AiService.cs:27). Typed absence only.</summary>
    public static readonly AiProviderId Cloud = new("cloud");

    /// <summary>A loopback Ollama host (the c2 slice's provider).</summary>
    public static readonly AiProviderId LocalOllama = new("local-ollama");

    private AiProviderId(string value) => Value = value;

    /// <summary>The stable machine token (capability names, diagnostics — never a secret, never user text).</summary>
    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// What the pipeline knows about a provider WITHOUT an implementation: its typed ID and
/// its endpoint class (contract §6 disclosure — resolvable from configuration alone, no probe).
/// </summary>
public sealed record AiProviderDescriptor(AiProviderId Id, AiEndpointClass EndpointClass);

/// <summary>
/// One AI request. The prompt is user/model text: it NEVER enters diagnostics (contract §12)
/// or logs; the pipeline carries it only to the selected provider.
/// </summary>
public sealed record AiRequest(string Prompt);

/// <summary>
/// A provider implementation (c1: test fakes only; c2 lands the first real one). The probe
/// is the ONLY availability authority (SP-006): a provider without a probe, or whose probe
/// has not run, is selected-but-unproven → typed Unavailable.
/// </summary>
public interface IAiProvider
{
    AiProviderDescriptor Descriptor { get; }

    /// <summary>The SP-006 probe for this provider's capability state. Null = never provable (registration/selection prove nothing).</summary>
    Func<CancellationToken, Task<Capabilities.CapabilityState>>? Probe => null;

    /// <summary>Produces a reply. MUST observe the cancellation token — cancellation, never a timeout alone, unblocks in-flight work (contract §2 rule 1).</summary>
    Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The typed result of one AI operation (contract §1): the terminal outcome IS SP-004's
/// OperationOutcome; the domain payload (<see cref="Reply"/>) rides only with Completed.
/// <see cref="Admission"/> carries the awareness suppression decision (Admitted for
/// interactive operations).
/// </summary>
public sealed record AiOperationResult(
    Lifecycle.OperationOutcome Outcome,
    AiReply? Reply,
    AiAdmission Admission);
