namespace CcpClient.Desktop.Ai;

// Typed vocabulary for the provider-neutral AI operation contract
// (client/docs/ai-operation-contract.md). DEFINE-ONLY slice: no providers, no network
// calls, no UI, no moderation engine. SP-006 honesty rule: nothing here is a capability
// claim; the first consumer rows prove the seams.

/// <summary>
/// Typed endpoint class (contract §6). Classification is pure host-shape analysis —
/// resolvable from configuration alone, no probe, no network call.
/// </summary>
public enum AiEndpointClass
{
    /// <summary>No endpoint was involved (e.g. no provider selected). Diagnostics only — never a destination.</summary>
    Unspecified,

    /// <summary>Traffic stays on this machine (default Ollama http://localhost:11434/).</summary>
    Loopback,

    /// <summary>The operated first-party proxy (AI_AUDIT §10 inventory fact, not an admission).</summary>
    FirstPartyCloud,

    /// <summary>Any other remote host (e.g. an OpenAI-compatible provider).</summary>
    ThirdPartyCloud,

    /// <summary>
    /// A user-configured NON-loopback Ollama host. Remote, classified like third-party
    /// cloud for disclosure — first-attempt rejected assumption: "Selecting local AI
    /// guarantees local-only data. A configurable non-loopback Ollama host is remote."
    /// </summary>
    RemoteHostOllama,
}

/// <summary>
/// Pure endpoint classification (contract §6 rule 2). No network I/O. Endpoint allow-list
/// governance (which remote hosts are admissible) is pending-owner; this type fixes only
/// the classification vocabulary and the disclosure rule.
/// </summary>
public static class AiEndpointClassifier
{
    /// <summary>Operated first-party proxy host (AI_AUDIT §10; hardcoded in WPF Services/AiService.cs:27).</summary>
    public const string FirstPartyProxyHost = "codebambi-proxy.vercel.app";

    /// <summary>An Ollama host is Loopback only when it is actually loopback; anything else is remote (contract §6 rule 1).</summary>
    public static AiEndpointClass ClassifyOllamaHost(Uri host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.IsLoopback ? AiEndpointClass.Loopback : AiEndpointClass.RemoteHostOllama;
    }

    /// <summary>A general provider endpoint: loopback, the operated first-party proxy, or third-party cloud.</summary>
    public static AiEndpointClass ClassifyProviderEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.IsLoopback) return AiEndpointClass.Loopback;
        return string.Equals(endpoint.Host, FirstPartyProxyHost, StringComparison.OrdinalIgnoreCase)
            ? AiEndpointClass.FirstPartyCloud
            : AiEndpointClass.ThirdPartyCloud;
    }
}

/// <summary>Operation classes with distinct contracts (contract §4).</summary>
public enum AiOperationClass
{
    /// <summary>User-initiated, user-visible request/response (companion chat, quiz-style completions).</summary>
    Interactive,

    /// <summary>System-initiated reactions from observed context; consent-gated and cooldown-bound (contract §4).</summary>
    Awareness,
}

/// <summary>Why an awareness operation was suppressed instead of admitted (contract §4 rules 1–2). Values pending-owner; semantics fixed here.</summary>
public enum AiSuppressionKind
{
    /// <summary>Awareness consent state does not admit the operation (code-enforced at admission, not a prompt convention).</summary>
    ConsentDenied,

    /// <summary>A cooldown (per-trigger, global, or per-keyword) suppressed the operation. Observable typed result, never a silent no-op.</summary>
    Cooldown,
}

/// <summary>
/// Admission decision for an awareness operation (contract §4). A suppressed awareness
/// operation terminates Completed with this typed result — never a silent drop.
/// </summary>
public abstract record AiAdmission
{
    private AiAdmission() { }

    /// <summary>The operation may run.</summary>
    public sealed record Admitted : AiAdmission
    {
        public static readonly Admitted Instance = new();
    }

    /// <summary>The operation is suppressed with a typed reason (consent or cooldown).</summary>
    public sealed record Suppressed(AiSuppressionKind Kind) : AiAdmission;
}

/// <summary>Side of the moderation boundary that produced a refusal (contract §7 rule 3).</summary>
public enum AiModerationSource
{
    /// <summary>User-authored content tripped the guard before it was sent.</summary>
    Input,

    /// <summary>Model-produced content tripped the guard before it was shown/executed.</summary>
    Output,
}

/// <summary>
/// Moderation verdict taxonomy (contract §7 rule 3), adapted from the first-attempt
/// ModerationResult. The category is a stable machine token; the category LIST and
/// wordlist are policy values pending-owner — this slice defines the taxonomy, not the policy.
/// </summary>
public abstract record AiModerationVerdict
{
    private AiModerationVerdict() { }

    /// <summary>No category matched.</summary>
    public sealed record Pass : AiModerationVerdict
    {
        public static readonly Pass Instance = new();
    }

    /// <summary>Log-worthy but not blocking (first-attempt soft-hit shape, e.g. ProfessionalAdvice).</summary>
    public sealed record SoftHit(string CategoryCode) : AiModerationVerdict;

    /// <summary>Hard block; the content must not be sent, shown, or executed.</summary>
    public sealed record Block(string CategoryCode) : AiModerationVerdict;
}

/// <summary>
/// Typed refusal carrier (contract §1/§7). REPLACES the WPF/first-attempt sentinel-string
/// channel (ModerationRefusal.cs:44-61 — rejected).
/// </summary>
public sealed record AiModerationRefusal(string CategoryCode, AiModerationSource Source);

/// <summary>
/// AI operation domain result (contract §1). Rides inside the SP-004 OperationOutcome's
/// Completed payload; the terminal outcome type itself is OperationOutcome, unchanged.
/// The variants are distinguishable BY TYPE — the WPF string API's fallback/refusal
/// ambiguity (AI_AUDIT §7) and catch-and-return-null are rejected shapes.
/// </summary>
public abstract record AiReply
{
    private AiReply() { }

    /// <summary>A real model reply. Provenance names the endpoint class that produced it; UI badge honesty derives from this variant only.</summary>
    public sealed record Generated(string Text, AiEndpointClass Provenance) : AiReply;

    /// <summary>Moderation blocked input or output; typed refusal, never a sentinel string.</summary>
    public sealed record Refused(AiModerationRefusal Refusal) : AiReply;

    /// <summary>The provider could not produce a reply (offline, unconfigured, not proven per SP-006). Not an error; distinct from Refused. Code is a stable machine token.</summary>
    public sealed record Unavailable(string Code) : AiReply;

    /// <summary>A canned, non-model response. Always distinguishable from Generated by type. Code is a stable machine token naming why the fallback fired.</summary>
    public sealed record Fallback(string Text, string Code) : AiReply;
}

/// <summary>Stable reason codes for AiReply.Unavailable/Fallback and diagnostics. Additive; new codes land with their consumer row.</summary>
public static class AiReplyCodes
{
    /// <summary>No provider is selected/configured.</summary>
    public const string NotConfigured = "not-configured";

    /// <summary>The selected provider's capability state is not Available (SP-006; selection/registration never prove availability).</summary>
    public const string ProviderUnproven = "provider-unproven";

    /// <summary>The endpoint is unreachable or offline (typed per operation class, contract §11).</summary>
    public const string Offline = "offline";

    /// <summary>The provider hit a request quota/rate limit (WPF 100 free / 1000 Patreon daily — inventory fact, values pending-owner).</summary>
    public const string QuotaExhausted = "quota-exhausted";

    /// <summary>The provider timed out (a failure classification, never the cancellation mechanism — contract §2 rule 1).</summary>
    public const string Timeout = "timeout";

    /// <summary>The model's output could not be validated against the strict envelope schema (contract §8 — zero repair).</summary>
    public const string MalformedOutput = "malformed-output";

    /// <summary>The selected provider's endpoint class is not admitted by the admission policy (contract §6; rejection happens BEFORE any socket opens).</summary>
    public const string EndpointNotAdmitted = "endpoint-not-admitted";
}
