using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Chaos;

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
internal static class DtrhAssetStatsStore
{
    internal sealed class AssetStat
    {
        public string Kind { get; set; } = "image";
        public double Seconds { get; set; }   // raw on-screen seconds
        public double Weighted { get; set; }  // attention-weighted seconds (size + centering)
        public long Grabs { get; set; }        // times grabbed as a paddle
        public long Pops { get; set; }         // treats popped while held
        public long Defuses { get; set; }      // lives snap-defused while held
        public long Flings { get; set; }       // rabbits flung while held
        public DateTime LastSeenUtc { get; set; }
    }

    private static readonly object _lock = new();
    private static Dictionary<string, AssetStat>? _stats;

    private static string FilePath => Path.Combine(App.UserDataPath, "dtrh_asset_stats.json");

    private static void EnsureLoaded()
    {
        if (_stats != null) return;
        Dictionary<string, AssetStat>? loaded = null;
        try
        {
            if (File.Exists(FilePath))
                loaded = JsonConvert.DeserializeObject<Dictionary<string, AssetStat>>(File.ReadAllText(FilePath));
        }
        catch (Exception ex) { App.Logger?.Warning("DtrhAssetStatsStore load failed: {E}", ex.Message); }

        // Rebuild with a case-insensitive comparer regardless of source (asset names are filenames).
        _stats = new Dictionary<string, AssetStat>(StringComparer.OrdinalIgnoreCase);
        if (loaded != null)
            foreach (var kv in loaded)
                if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null) _stats[kv.Key] = kv.Value;
    }

    /// <summary>Merge one {type:'asset-stats'} message (o["stats"] = array of per-asset delta rows).</summary>
    public static void Merge(JObject o)
    {
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

    private static void SaveNow()
    {
        // caller holds _lock
        try
        {
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(_stats, Formatting.Indented));
        }
        catch (Exception ex) { App.Logger?.Warning("DtrhAssetStatsStore save failed: {E}", ex.Message); }
    }
}
