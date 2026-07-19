using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Persistence;

/// <summary>
/// One DOM-level, idempotent document migration (persistence-migration-contract §7).
/// Migrations mutate the <see cref="JsonObject"/>, not the typed model.
/// </summary>
public interface IDocumentMigration
{
    /// <summary>Stable, unique migration ID recorded in the document's migration journal.</summary>
    string Id { get; }

    /// <summary>The schema version the document reaches after this migration.</summary>
    int TargetVersion { get; }

    /// <summary>Must be idempotent: running twice on the same document yields the same document.</summary>
    void Migrate(JsonObject document);
}

/// <summary>
/// The demonstrator settings model (persistence-migration-contract §1 rule 4): exercises
/// the whole contract; it is NOT a feature model. Extension data round-trips unknown
/// members verbatim (contract §6 — required on every persisted model type).
/// </summary>
public sealed class DemoSettings
{
    /// <summary>The schema version this build writes (persistence contract §1): the store is the single authority that reads and advances it.</summary>
    public const int CurrentSchemaVersion = 1;

    public string Greeting { get; set; } = "hello";

    public int Volume { get; set; } = 50;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// The single demonstrator migration (contract §7 rule 4): v0 → v1 renames the v0 member
/// <c>greeting_text</c> to <c>greeting</c>. Idempotent by construction — a second run finds
/// no <c>greeting_text</c> and changes nothing. Proves journal + idempotence; not a framework.
/// </summary>
public sealed class DemoMigrationV0ToV1 : IDocumentMigration
{
    public string Id => "demo.v0-to-v1";

    public int TargetVersion => 1;

    public void Migrate(JsonObject document)
    {
        if (document.Remove("greeting_text", out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text))
        {
            document["greeting"] = text;
        }
    }
}
