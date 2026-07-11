using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Ported (byte-for-byte semantics) from ConditioningControlPanel/Services/Chaos/DtrhSessionStatsStore.cs
// in the WPF head. The WPF file is an internal-static helper bound to App.UserDataPath / App.Logger;
// here it is a public sealed INSTANCE service injecting the Core seams (IAppEnvironment,
// ILogger<DtrhSessionStatsStore>). The on-disk JSON schema (dtrh_session_stats.json) and observable
// behavior are unchanged so existing on-disk JSON keeps loading. Cited line numbers refer to the
// WPF source on branch feat/crossplatform.

namespace ConditioningControlPanel.Core.Services.Chaos;

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
public sealed class DtrhSessionStatsStore
{
    /// <summary>Cumulative lifetime totals. All fields SUM across runs except
    /// <see cref="BestComboEver"/> (a max) and <see cref="LastRunUtc"/> (a stamp).</summary>
    public sealed class SessionTotals
    {
        // WPF DtrhSessionStatsStore.cs:28-67 — public so tests can assert; JSON property names frozen.
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
    public sealed class RunRecord
    {
        // WPF DtrhSessionStatsStore.cs:70-75
        public DateTime Utc { get; set; }
        public string Difficulty { get; set; } = "Gentle";
        public JObject Snapshot { get; set; } = new();   // the merged per-run object as recorded
    }

    public sealed class Root
    {
        // WPF DtrhSessionStatsStore.cs:77-81
        public SessionTotals Lifetime { get; set; } = new();
        public List<RunRecord> Recent { get; set; } = new();   // capped at HistoryCap
    }

    private const int HistoryCap = 25;                  // WPF DtrhSessionStatsStore.cs:84
    private readonly IAppEnvironment _environment;
    private readonly ILogger<DtrhSessionStatsStore> _logger;

    private readonly object _lock = new();
    private readonly object _writeLock = new();   // WPF DtrhSessionStatsStore.cs:86 — serializes the background disk write so overlapping flushes never interleave
    private Root? _root;
    private Task _pendingWrite = Task.CompletedTask;   // background writes are CHAINED (FIFO) off this; also the test-visible flush handle

    public DtrhSessionStatsStore(IAppEnvironment environment, ILogger<DtrhSessionStatsStore> logger)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Resolves when all queued background writes have completed (best-effort; never throws).</summary>
    internal Task WhenSaved => _pendingWrite;

    private string FilePath => Path.Combine(_environment.UserDataPath, "dtrh_session_stats.json");   // WPF: App.UserDataPath

    private void EnsureLoaded()
    {
        // WPF DtrhSessionStatsStore.cs:92-104
        if (_root != null) return;
        try
        {
            if (File.Exists(FilePath))
                _root = JsonConvert.DeserializeObject<Root>(File.ReadAllText(FilePath));
        }
        catch (Exception ex) { _logger.LogWarning("DtrhSessionStatsStore load failed: {E}", ex.Message); }

        _root ??= new Root();
        _root.Lifetime ??= new SessionTotals();
        _root.Lifetime.EffectsByKind ??= new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        _root.Recent ??= new List<RunRecord>();
    }

    /// <summary>Record ONE completed run. <paramref name="run"/> is the JS sessionStats block
    /// already merged with the host's native totals + payout (built in DtrhHostService.OnRunEnded).
    /// Returns the fresh lifetime totals so the caller can log / echo them.</summary>
    public SessionTotals Record(JObject run, string difficulty)
    {
        // WPF DtrhSessionStatsStore.cs:110-167
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

    private void SaveNow()
    {
        // WPF DtrhSessionStatsStore.cs:169-184 — caller holds _lock; serialize the snapshot here but push the
        // disk write off the UI thread (Record runs on the dispatcher thread via OnRunEnded). The write is
        // tracked in _pendingWrite (test-only addition).
        string json;
        try { json = JsonConvert.SerializeObject(_root, Formatting.Indented); }
        catch (Exception ex) { _logger.LogWarning("DtrhSessionStatsStore serialize failed: {E}", ex.Message); return; }
        var path = FilePath;
        // Chain each write off the previous so they run in enqueue order (latest snapshot wins) and
        // WhenSaved awaits the whole chain. Independent Task.Run calls gave no ordering guarantee across
        // overlapping saves, letting a stale snapshot land last under threadpool contention.
        _pendingWrite = _pendingWrite.ContinueWith(_ =>
        {
            lock (_writeLock)
            {
                try { File.WriteAllText(path, json); }
                catch (Exception ex) { _logger.LogWarning("DtrhSessionStatsStore save failed: {E}", ex.Message); }
            }
        }, TaskScheduler.Default);
    }
}
