using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Companion;

/// <summary>
/// The companion state document (schema v1) — disabled/muted phrase ids, persisted
/// one-shot latches, and per-rule variant rotation. Persisted ONLY through the SP-005
/// machinery (<see cref="CcpClient.Desktop.Persistence.PersistenceStore{TModel}"/>):
/// schema-versioned, atomic writes, corruption quarantine with typed Degraded — never a
/// parallel settings file (packet framing c). WPF parity: AppSettings.DisabledPhraseIds
/// (AppSettings.cs:4488), BarkLifetimeFired (:350-367), BarkVariantRotation
/// (BarkService.cs:1538-1577). IdleRotation is NOT carried — no idle dispatcher exists in
/// greenfield (lands with the idle row via a schema migration; the machinery supports it).
/// </summary>
public sealed class CompanionStateDocument
{
    /// <summary>The schema version this build writes (the store is the single authority that reads and advances it).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Muted-but-listed line ids ("Bark:{ruleId}:{stem|t_slug}" — stable across content
    /// reordering, WPF BarkService.cs:1203-1209). Enabled iff NOT in this set; re-enable =
    /// remove, effective at the next selection (no caching, WPF :1172-1180).
    /// </summary>
    public HashSet<string> DisabledPhraseIds { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Persisted one-shot latches for tier/lifetime scopes (WPF BarkLifetimeFired). The WPF
    /// tier latch key is `id@PatreonTier` (BarkService.cs:1433-1436); greenfield has no
    /// Patreon tier — the key is the plain id (tier suffix lands with the premium row).
    /// </summary>
    public HashSet<string> LifetimeFired { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Per-rule used-variant line ids (WPF BarkVariantRotation): no-repeat-until-exhaustion across restarts.</summary>
    public Dictionary<string, List<string>> VariantRotation { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted model type).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
