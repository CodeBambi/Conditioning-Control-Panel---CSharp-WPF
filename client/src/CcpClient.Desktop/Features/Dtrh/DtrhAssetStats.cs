using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>One asset's cumulative engagement row (WPF: DtrhAssetStatsStore.AssetStat,
/// DtrhAssetStatsStore.cs:24-33).</summary>
public sealed class DtrhAssetStat
{
    public string Kind { get; set; } = "image";

    public double Seconds { get; set; }

    public double Weighted { get; set; }

    public long Grabs { get; set; }

    public long Pops { get; set; }

    public long Defuses { get; set; }

    public long Flings { get; set; }

    public DateTime LastSeenUtc { get; set; }
}

/// <summary>The cumulative per-asset engagement document (b4): name → stat row. WPF writes
/// a bare dictionary (DtrhAssetStatsStore.cs:88-113); the greenfield wraps it in the SP-005
/// envelope (schema/journal) — no WPF back-compat need (no legacy installs, SP-024).
/// Names are case-insensitive (they are filenames; WPF rebuilds the comparer on load,
/// DtrhAssetStatsStore.cs:46-55).</summary>
public sealed class DtrhAssetStatsDocument
{
    public const int CurrentSchemaVersion = 1;

    public Dictionary<string, DtrhAssetStat> Stats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// The asset-stats accumulator (b4; dtrh-admission.md §7): the page posts
/// {type:'asset-stats'} DELTAS (weighted attention + paddle interactions, payload send site
/// chaosRun.js:487-492) and the host SUMS them into the app-global store so the
/// favorites ranking can bias future draws (hostMedia.js setFavorites). Semantics ported
/// from WPF DtrhAssetStatsStore.cs: name ≤260 chars, every delta max(0,·) (:60-80),
/// persist on each merge (:88-113 — WPF writes each merge too; here the SP-005 enqueue),
/// case-insensitive names (:46-55), TopAssets ranking weighted + grabs*8 + pops*2
/// (:96-110). Purely additive telemetry with no gameplay authority — safe to lose.
/// Media-logging rule (packet honesty framing c): merges log PRESENCE+SHAPE only
/// (row count) — asset names are user-media filenames, never logged.
/// </summary>
public sealed class DtrhAssetStats
{
    private readonly PersistenceStore<DtrhAssetStatsDocument> _store;
    private readonly Action<string> _log;

    public DtrhAssetStats(PersistenceStore<DtrhAssetStatsDocument> store, Action<string> log)
    {
        _store = store;
        _log = log;
        NormalizeComparer();
    }

    /// <summary>WPF rebuilds the dictionary case-insensitive on load (:46-55); a
    /// deserialized dictionary is case-sensitive, so a non-empty document with the wrong
    /// comparer is normalized once here (empty documents already compare fine).</summary>
    private void NormalizeComparer()
    {
        var stats = _store.Current.Stats;
        if (stats.Count > 0 && stats.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            _store.Mutate(d => d.Stats = new Dictionary<string, DtrhAssetStat>(stats, StringComparer.OrdinalIgnoreCase));
            _log("dtrh-meta: asset-stats name comparer normalized (case-insensitive, WPF load parity)");
        }
    }

    /// <summary>Merge one asset-stats message's stats array. Tolerates every malformed
    /// shape (WPF wraps the whole call in try/catch, DtrhHostService.cs:280-284) — never
    /// throws, returns the number of rows touched.</summary>
    public int Merge(JsonElement raw)
    {
        try
        {
            if (!raw.TryGetProperty("stats", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            var touched = 0;
            var now = DateTime.UtcNow;
            _store.Mutate(doc =>
            {
                foreach (var r in rows.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var name = r.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(name) || name.Length > 260)
                    {
                        continue;
                    }

                    if (!doc.Stats.TryGetValue(name, out var s))
                    {
                        s = new DtrhAssetStat();
                        doc.Stats[name] = s;
                    }

                    var kind = r.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
                        ? k.GetString()
                        : null;
                    if (!string.IsNullOrEmpty(kind))
                    {
                        s.Kind = kind!;
                    }

                    s.Seconds += Math.Max(0, GetDouble(r, "seconds"));
                    s.Weighted += Math.Max(0, GetDouble(r, "weighted"));
                    s.Grabs += Math.Max(0, GetLong(r, "grabs"));
                    s.Pops += Math.Max(0, GetLong(r, "pops"));
                    s.Defuses += Math.Max(0, GetLong(r, "defuses"));
                    s.Flings += Math.Max(0, GetLong(r, "flings"));
                    s.LastSeenUtc = now;
                    touched++;
                }
            });

            if (touched > 0)
            {
                _ = _store.Save(); // WPF persists on each merge (:88-113); SP-005 serializes the write
                _log($"dtrh-meta: asset-stats merged ({touched} row(s))");
            }

            return touched;
        }
        catch (Exception ex)
        {
            _log($"dtrh-meta: asset-stats merge failed ({ex.GetType().Name}) — ignored (WPF try/catch parity)");
            return 0;
        }
    }

    /// <summary>Top-N names ranked by cumulative engagement (Weighted + Grabs*8 + Pops*2,
    /// DtrhAssetStatsStore.cs:96-110) — the favorites message's read-back.</summary>
    public IReadOnlyList<string> TopAssets(int n)
    {
        if (n <= 0)
        {
            return [];
        }

        return _store.Current.Stats
            .OrderByDescending(kv => kv.Value.Weighted + kv.Value.Grabs * 8 + kv.Value.Pops * 2)
            .Take(n)
            .Select(kv => kv.Key)
            .ToList();
    }

    private static double GetDouble(JsonElement r, string name) =>
        r.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var v) ? v : 0;

    private static long GetLong(JsonElement r, string name) =>
        r.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v) ? v : 0;
}
