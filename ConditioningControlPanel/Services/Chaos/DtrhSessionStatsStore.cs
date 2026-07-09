using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Lifetime cumulative per-run engagement metrics for the DtRH web game. Each run,
/// the page ships a {run-ended sessionStats} snapshot (bubbles popped, effects shown,
/// boons/curses taken, no-picks, junctions forced, subliminals shown, depth, ...); the
/// host folds in its natively-measured video/voice/subliminal totals + the payout, and
/// we SUM the combined per-run object into dtrh_session_stats.json plus keep a small
/// rolling history of recent runs.
///
/// The point is a future end-of-run RECAP CARD (gated behind progression unlocks, drafted
/// later) and other features that want to read what the user actually engages with. Purely
/// additive telemetry with no gameplay authority and LOCAL ONLY - never sent to the server.
/// Best-effort like <see cref="DtrhAssetStatsStore"/>: safe to lose, written on each record.
/// </summary>
internal static class DtrhSessionStatsStore
{
    /// <summary>Cumulative lifetime totals. All fields SUM across runs except
    /// <see cref="BestComboEver"/> (a max) and <see cref="LastRunUtc"/> (a stamp).</summary>
    internal sealed class SessionTotals
    {
        public long Runs { get; set; }

        // ---- JS-side (shipped in run-ended.sessionStats) ----
        public long BubblesPopped { get; set; }
        public long EffectsShown { get; set; }
        public double GifEffectSeconds { get; set; }        // BUSY_SEC estimate sum
        public double VideoPayloadSecEstimate { get; set; } // in-world video payloads (est.)
        public long BoonsReceived { get; set; }
        public long CursesReceived { get; set; }
        public long DraftSkips { get; set; }
        public long DraftAutopicks { get; set; }
        public long JunctionsTaken { get; set; }
        public long JunctionsForced { get; set; }
        public long JunctionsPassive { get; set; }
        public long SubliminalsShown { get; set; }
        public long Defused { get; set; }
        public long Detonated { get; set; }
        public long Loops { get; set; }
        public double DepthMeters { get; set; }
        public double ElapsedSec { get; set; }
        public long BestComboEver { get; set; }             // MAX, not sum

        // ---- host-measured natives (merged in DtrhHostService.OnRunEnded) ----
        public double VideoWatchSec { get; set; }
        public long VideosShown { get; set; }
        public long VideosSkipped { get; set; }
        public long VoicelinesHeard { get; set; }
        public double VoiceoverSec { get; set; }
        public long SubliminalsHeard { get; set; }

        // ---- payout ----
        public long SparksEarned { get; set; }
        public double XpEarned { get; set; }

        public Dictionary<string, long> EffectsByKind { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

        public DateTime LastRunUtc { get; set; }
    }

    /// <summary>One completed run, kept in a small rolling window for the recap / future analysis.</summary>
    internal sealed class RunRecord
    {
        public DateTime Utc { get; set; }
        public string Difficulty { get; set; } = "Gentle";
        public JObject Snapshot { get; set; } = new();   // the merged per-run object as recorded
    }

    internal sealed class Root
    {
        public SessionTotals Lifetime { get; set; } = new();
        public List<RunRecord> Recent { get; set; } = new();   // capped at HistoryCap
    }

    private const int HistoryCap = 25;
    private static readonly object _lock = new();
    private static readonly object _writeLock = new();   // serializes the background disk write so overlapping flushes never interleave
    private static Root? _root;

    private static string FilePath => Path.Combine(App.UserDataPath, "dtrh_session_stats.json");

    private static void EnsureLoaded()
    {
        if (_root != null) return;
        try
        {
            if (File.Exists(FilePath))
                _root = JsonConvert.DeserializeObject<Root>(File.ReadAllText(FilePath));
        }
        catch (Exception ex) { App.Logger?.Warning("DtrhSessionStatsStore load failed: {E}", ex.Message); }

        _root ??= new Root();
        _root.Lifetime ??= new SessionTotals();
        _root.Lifetime.EffectsByKind ??= new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        _root.Recent ??= new List<RunRecord>();
    }

    /// <summary>Record ONE completed run. <paramref name="run"/> is the JS sessionStats block
    /// already merged with the host's native totals + payout (built in DtrhHostService.OnRunEnded).
    /// Returns the fresh lifetime totals so the caller can log / echo them.</summary>
    public static SessionTotals Record(JObject run, string difficulty)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var t = _root!.Lifetime;

            long L(string k) => (long?)run[k] ?? 0;
            double D(string k) => (double?)run[k] ?? 0;

            t.Runs++;
            t.BubblesPopped          += L("bubblesPopped");
            t.EffectsShown           += L("effectsShown");
            t.GifEffectSeconds       += D("gifEffectSeconds");
            t.VideoPayloadSecEstimate += D("videoPayloadSecEstimate");
            t.BoonsReceived          += L("boonsReceived");
            t.CursesReceived         += L("cursesReceived");
            t.DraftSkips             += L("draftSkips");
            t.DraftAutopicks         += L("draftAutopicks");
            t.JunctionsTaken         += L("junctionsTaken");
            t.JunctionsForced        += L("junctionsForced");
            t.JunctionsPassive       += L("junctionsPassive");
            t.SubliminalsShown       += L("subliminalsShown");
            t.Defused                += L("defused");
            t.Detonated              += L("detonated");
            t.Loops                  += L("loops");
            t.DepthMeters            += D("depthMeters");
            t.ElapsedSec             += D("elapsedSec");
            t.BestComboEver           = Math.Max(t.BestComboEver, L("bestCombo"));

            t.VideoWatchSec          += D("videoWatchSec");
            t.VideosShown            += L("videosShown");
            t.VideosSkipped          += L("videosSkipped");
            t.VoicelinesHeard        += L("voicelinesHeard");
            t.VoiceoverSec           += D("voiceoverSec");
            t.SubliminalsHeard       += L("subliminalsHeard");

            t.SparksEarned           += L("sparksEarned");
            t.XpEarned               += D("xpEarned");

            if (run["effectsByKind"] is JObject byKind)
                foreach (var p in byKind)
                    t.EffectsByKind[p.Key] = t.EffectsByKind.GetValueOrDefault(p.Key) + ((long?)p.Value ?? 0);

            t.LastRunUtc = DateTime.UtcNow;

            _root.Recent.Add(new RunRecord
            {
                Utc = DateTime.UtcNow,
                Difficulty = string.IsNullOrWhiteSpace(difficulty) ? "Gentle" : difficulty,
                Snapshot = run,
            });
            if (_root.Recent.Count > HistoryCap)
                _root.Recent.RemoveRange(0, _root.Recent.Count - HistoryCap);

            SaveNow();
            return t;
        }
    }

    private static void SaveNow()
    {
        // caller holds _lock; serialize the snapshot here but push the disk write off the UI thread
        // (Record runs on the dispatcher thread via OnRunEnded).
        string json;
        try { json = JsonConvert.SerializeObject(_root, Formatting.Indented); }
        catch (Exception ex) { App.Logger?.Warning("DtrhSessionStatsStore serialize failed: {E}", ex.Message); return; }
        var path = FilePath;
        Task.Run(() =>
        {
            lock (_writeLock)
            {
                try { File.WriteAllText(path, json); }
                catch (Exception ex) { App.Logger?.Warning("DtrhSessionStatsStore save failed: {E}", ex.Message); }
            }
        });
    }
}
