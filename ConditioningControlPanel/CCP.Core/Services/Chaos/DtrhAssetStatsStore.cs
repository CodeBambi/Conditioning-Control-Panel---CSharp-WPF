using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Ported (byte-for-byte semantics) from ConditioningControlPanel/Services/Chaos/DtrhAssetStatsStore.cs
// in the WPF head. The WPF file is an internal-static helper bound to App.UserDataPath / App.Logger;
// here it is a public sealed INSTANCE service injecting the Core seams (IAppEnvironment,
// ILogger<DtrhAssetStatsStore>). The on-disk JSON schema (dtrh_asset_stats.json) and observable
// behavior are unchanged so existing on-disk JSON keeps loading. Cited line numbers refer to the
// WPF source on branch feat/crossplatform.

namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Cumulative per-asset engagement record for the DtRH web game. The page posts
/// {type:'asset-stats'} DELTAS (weighted on-screen attention + paddle interactions -
/// grabs/pops/defuses/flings - per user image/gif); we SUM them into
/// dtrh_asset_stats.json so future features can bias toward the media the user
/// actually engages with.
///
/// Purely additive telemetry with no gameplay authority - it's safe to lose, so the
/// write is best-effort. Volume is tiny (a delta batch every ~15s + at run end), so
/// we just persist on each merge rather than debounce.
/// </summary>
public sealed class DtrhAssetStatsStore
{
    public sealed class AssetStat
    {
        // WPF DtrhAssetStatsStore.cs:21-32 — public so tests can assert; JSON property names frozen.
        public string Kind { get; set; } = "image";
        public double Seconds { get; set; }   // raw on-screen seconds
        public double Weighted { get; set; }  // attention-weighted seconds (size + centering)
        public long Grabs { get; set; }        // times grabbed as a paddle
        public long Pops { get; set; }         // treats popped while held
        public long Defuses { get; set; }      // lives snap-defused while held
        public long Flings { get; set; }       // rabbits flung while held
        public DateTime LastSeenUtc { get; set; }
    }

    private readonly IAppEnvironment _environment;
    private readonly ILogger<DtrhAssetStatsStore> _logger;

    private readonly object _lock = new();
    private readonly object _writeLock = new();   // WPF DtrhAssetStatsStore.cs:40 — serializes the background disk write so overlapping flushes never interleave
    private Dictionary<string, AssetStat>? _stats;
    private Task? _pendingWrite;                  // ONLY behavioral addition: exposes the last background write for tests

    public DtrhAssetStatsStore(IAppEnvironment environment, ILogger<DtrhAssetStatsStore> logger)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Resolves when the most recent background write has completed (best-effort; never throws).</summary>
    internal Task WhenSaved => _pendingWrite ?? Task.CompletedTask;

    private string FilePath => Path.Combine(_environment.UserDataPath, "dtrh_asset_stats.json");   // WPF: App.UserDataPath

    private void EnsureLoaded()
    {
        // WPF DtrhAssetStatsStore.cs:47-61
        if (_stats != null) return;
        Dictionary<string, AssetStat>? loaded = null;
        try
        {
            if (File.Exists(FilePath))
                loaded = JsonConvert.DeserializeObject<Dictionary<string, AssetStat>>(File.ReadAllText(FilePath));
        }
        catch (Exception ex) { _logger.LogWarning("DtrhAssetStatsStore load failed: {E}", ex.Message); }

        // Rebuild with a case-insensitive comparer regardless of source (asset names are filenames).
        _stats = new Dictionary<string, AssetStat>(StringComparer.OrdinalIgnoreCase);
        if (loaded != null)
            foreach (var kv in loaded)
                if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null) _stats[kv.Key] = kv.Value;
    }

    /// <summary>Merge one {type:'asset-stats'} message (o["stats"] = array of per-asset delta rows).</summary>
    public void Merge(JObject o)
    {
        // WPF DtrhAssetStatsStore.cs:63-95
        if (o["stats"] is not JArray rows || rows.Count == 0) return;
        lock (_lock)
        {
            EnsureLoaded();
            var now = DateTime.UtcNow;
            int touched = 0;
            foreach (var r in rows)
            {
                var name = (string?)r["name"];
                if (string.IsNullOrWhiteSpace(name) || name!.Length > 260) continue;
                if (!_stats!.TryGetValue(name, out var s)) { s = new AssetStat(); _stats[name] = s; }
                var kind = (string?)r["kind"];
                if (!string.IsNullOrEmpty(kind)) s.Kind = kind!;
                s.Seconds  += Math.Max(0, (double?)r["seconds"] ?? 0);
                s.Weighted += Math.Max(0, (double?)r["weighted"] ?? 0);
                s.Grabs    += Math.Max(0, (long?)r["grabs"] ?? 0);
                s.Pops     += Math.Max(0, (long?)r["pops"] ?? 0);
                s.Defuses  += Math.Max(0, (long?)r["defuses"] ?? 0);
                s.Flings   += Math.Max(0, (long?)r["flings"] ?? 0);
                s.LastSeenUtc = now;
                touched++;
            }
            if (touched > 0) SaveNow();
        }
    }

    /// <summary>
    /// Top-N asset names ranked by cumulative engagement - the "read-back" this
    /// store was built for. Grabs weigh heaviest (a deliberate act), then pops
    /// taken while held, then raw weighted attention. The DtRH page receives
    /// these as {type:'favorites'} so biomes can bias toward what the user
    /// actually engages with (Hall of Mirrors' Mirror Moments etc.).
    /// </summary>
    public List<string> TopAssets(int n)
    {
        // WPF DtrhAssetStatsStore.cs:105-115 — ordering key: Weighted + Grabs*8 + Pops*2 descending
        lock (_lock)
        {
            EnsureLoaded();
            return _stats!
                .Where(kv => kv.Value != null)
                .OrderByDescending(kv => kv.Value.Weighted + kv.Value.Grabs * 8 + kv.Value.Pops * 2)
                .Take(Math.Max(0, n))
                .Select(kv => kv.Key)
                .ToList();
        }
    }

    private void SaveNow()
    {
        // WPF DtrhAssetStatsStore.cs:117-131 — caller holds _lock; serialize here (snapshot) but push the
        // disk write off the UI thread (OnPageMessage runs on the dispatcher thread, and this fires every
        // ~15s batch + at run end). The write is tracked in _pendingWrite (test-only addition).
        string json;
        try { json = JsonConvert.SerializeObject(_stats, Formatting.Indented); }
        catch (Exception ex) { _logger.LogWarning("DtrhAssetStatsStore serialize failed: {E}", ex.Message); return; }
        var path = FilePath;
        _pendingWrite = Task.Run(() =>
        {
            lock (_writeLock)
            {
                try { File.WriteAllText(path, json); }
                catch (Exception ex) { _logger.LogWarning("DtrhAssetStatsStore save failed: {E}", ex.Message); }
            }
        });
    }
}
