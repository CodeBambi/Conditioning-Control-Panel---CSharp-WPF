using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Ai;

/// <summary>One remembered turn (contract §5). Content is user data under the persistence contract's authority; it NEVER flows into diagnostics (§12) and is never a secret (§10).</summary>
public sealed record AiMemoryTurn(AiMemoryRole Role, string Text);

/// <summary>Serialized as readable role names (the WPF persisted shape, LocalAiService.cs PersistedTurn Role strings) — never opaque numbers in a user-data document.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AiMemoryRole
{
    User,
    Assistant,
}

/// <summary>
/// Local memory seam (contract §5) — DECLARED ONLY. No implementation exists in this
/// slice; per SP-006's honesty rule this declaration is NOT a capability claim. The
/// first consumer row implements it and proves the behavior.
///
/// Contract rules binding on any future implementation:
/// 1. Writes occur only under an explicit memory-consent state (consent gating as
///    contract). Consent scope (global vs per-feature) and retention (cap values;
///    disable = retain dormant vs delete) are PENDING-OWNER — the seam is shaped so
///    either answer is implementable without contract change.
/// 2. <see cref="Clear"/> is the explicit-clear operation; it must exist and be
///    user-reachable (WPF precedent: ClearHistory + "Reset Memory" UI, AI_AUDIT §2).
/// 3. Memory is provider-neutral state: switching providers never implicitly clears it
///    (contract §5 rule 3).
/// </summary>
public interface IAiMemoryStore
{
    /// <summary>Reads up to <paramref name="maxTurns"/> most recent turns, oldest first.</summary>
    IReadOnlyList<AiMemoryTurn> ReadRecent(int maxTurns);

    /// <summary>Appends one turn. Called only under an explicit memory-consent state (contract §5 rule 2).</summary>
    void Append(AiMemoryTurn turn);

    /// <summary>The explicit-clear operation (contract §5 rule 1). Always explicit; never a side effect of provider switching.</summary>
    void Clear();
}
