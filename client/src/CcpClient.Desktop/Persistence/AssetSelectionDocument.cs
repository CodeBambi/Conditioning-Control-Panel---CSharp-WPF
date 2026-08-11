using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Persistence;

/// <summary>
/// SP-055: the user's asset selection (WPF AppSettings parity — `DisabledAssetPaths`
/// AppSettings.cs:1613-1635 + `UseAssetWhitelist` :1637-1646), the persisted half of the
/// ONE active-pool definition in <c>Features/Dtrh/DtrhUserMedia.cs</c>. App-global (it
/// spans every user-media consumer), so it lives beside the other SP-005 documents, not
/// inside a feature. New dedicated file <c>asset_selection.json</c> in the shared
/// &lt;dataDir&gt; — additive by construction (a NEW document: no schema bump, no
/// absent-member case; a Missing outcome is fresh defaults).
///
/// The set stays EMPTY until the future Assets-tree row owns the write path (this row
/// ships the seam, not the UI). Two readers, zero writers this row: the DTRH host and the
/// intake host each open a read-only store at host start; the tree row must resolve
/// single-writer ownership when it adds mutations.
/// </summary>
public sealed class AssetSelectionDocument
{
    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Relative paths (assets-root-relative) the user deselected. Items NOT in
    /// this set are active (:1613-1635 — "the inverse of a whitelist"). Normalization
    /// happens at the seam (<c>DtrhUserMedia.BuildDisabledSet</c>), matching
    /// FlashService.GetMediaFiles exactly.</summary>
    public List<string> DisabledAssetPaths { get; set; } = [];

    /// <summary>When true, files in <see cref="DisabledAssetPaths"/> are excluded from
    /// use; when false (default), all files are active (:1637-1646 documented contract).
    /// The seam gates the whole mechanism on this flag.</summary>
    public bool UseAssetWhitelist { get; set; }

    /// <summary>Unknown-member preservation (contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Starts the per-host read-only store over <c>asset_selection.json</c> (the
/// _barkStore/IntakeHostContext store-start conventions: own named owner, typed-Degraded
/// log, flagged defaults). A Degraded load yields an EMPTY set + whitelist OFF → ALL
/// assets active — never silently drop content over a store fault.
/// </summary>
public static class AssetSelectionStore
{
    public const string FileName = "asset_selection.json";

    /// <param name="ownerName">Unique OperationRegistry owner name (the registry's
    /// uniqueness convention — e.g. "DtrhAssetSelection" / "IntakeAssetSelection").</param>
    public static PersistenceStore<AssetSelectionDocument> Start(ApplicationHost host, string dataDir, string ownerName)
    {
        var store = new PersistenceStore<AssetSelectionDocument>(
            host.Registry.OwnerFor(ownerName),
            new HostLogSink(host),
            Path.Combine(dataDir, FileName),
            AssetSelectionDocument.CurrentSchemaVersion);
        store.StartAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
        if (store.LastLoadOutcome is not LoadOutcome.Loaded and not LoadOutcome.Missing)
        {
            host.LogDiagnostic($"asset-selection: load → {store.LastLoadOutcome?.GetType().Name} (typed Degraded — flagged defaults: empty set + whitelist off → ALL assets active)");
        }

        return store;
    }

    private sealed class HostLogSink(ApplicationHost host) : ILogSink
    {
        public void Log(string message) => host.LogDiagnostic(message);
    }
}
