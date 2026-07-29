using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Local-only rank history for the leaderboard. The server exposes no rank history, so
/// "you moved up 3 places" is faked honestly here: we persist a snapshot of the board and
/// diff the live board against it.
///
/// CRITICAL TIMING: the baseline is only refreshed once every <see cref="MinSnapshotAge"/>
/// (12h). The board auto-refreshes every 30 minutes, so snapshotting on every refresh would
/// always diff against ~30 minutes ago and every delta would render as 0. A 12-hour baseline
/// is what makes the delta read as "since your last visit".
///
/// CALL ORDER: <see cref="RecordIfDue"/> overwrites the baseline (in memory and on disk), so
/// a caller that records before rendering will read its own values back and see zero deltas.
/// Read the deltas first, then record.
///
/// Static, lock-guarded and cached: <see cref="GetPreviousRank"/> is called once per visible
/// row from the UI thread while <see cref="RecordIfDue"/> runs on the async refresh path.
/// Nothing here is allowed to throw — a corrupt or locked file degrades to "no rank known".
/// </summary>
public static class LeaderboardRankSnapshotService
{
    /// <summary>Minimum age of the stored snapshot before <see cref="RecordIfDue"/> rewrites it.</summary>
    private static readonly TimeSpan MinSnapshotAge = TimeSpan.FromHours(12);

    /// <summary>Hard cap on persisted rows per mode (best ranks kept) so the file cannot grow forever.</summary>
    private const int MaxEntriesPerMode = 500;

    private static readonly object _gate = new();

    private static SnapshotFile? _cache;
    private static bool _loaded;

    private static string FilePath => Path.Combine(App.UserDataPath, "leaderboard_ranks.json");

    // ---------- public API ----------

    /// <summary>Previous rank for a unified id in the given mode, or null when unknown.</summary>
    public static int? GetPreviousRank(string mode, string unifiedId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(mode) || string.IsNullOrWhiteSpace(unifiedId))
                return null;

            lock (_gate)
            {
                var file = EnsureLoaded();
                if (file == null) return null;

                if (!file.Modes.TryGetValue(NormalizeMode(mode), out var snap) || snap?.Ranks == null)
                    return null;

                return snap.Ranks.TryGetValue(unifiedId, out var rank) ? rank : null;
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "LeaderboardRankSnapshot: GetPreviousRank failed for {Mode}", mode);
            return null;
        }
    }

    /// <summary>
    /// Persist the current board as the new baseline, but only if the stored snapshot for this
    /// mode is missing or at least <see cref="MinSnapshotAge"/> old. No-op otherwise.
    /// </summary>
    public static void RecordIfDue(string mode, IEnumerable<LeaderboardEntry> entries)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(mode) || entries == null) return;

            var key = NormalizeMode(mode);

            lock (_gate)
            {
                var file = EnsureLoaded() ?? new SnapshotFile();

                if (file.Modes.TryGetValue(key, out var existing) && existing != null)
                {
                    var age = DateTime.UtcNow - ToUtc(existing.CapturedAtUtc);
                    // A negative age means a clock change (or a hand-edited future timestamp)
                    // parked the baseline in the future — treat that as due rather than freezing
                    // deltas until the wall clock catches up.
                    if (age >= TimeSpan.Zero && age < MinSnapshotAge)
                    {
                        App.Logger?.Debug(
                            "LeaderboardRankSnapshot: {Mode} snapshot is {Hours:F1}h old, not due yet",
                            key, age.TotalHours);
                        return;
                    }
                }

                var ranks = BuildRankMap(entries);
                if (ranks.Count == 0)
                {
                    // Empty/failed fetch: keep the old baseline rather than blanking it.
                    App.Logger?.Debug("LeaderboardRankSnapshot: {Mode} produced no usable rows, keeping baseline", key);
                    return;
                }

                file.Modes[key] = new ModeSnapshot
                {
                    CapturedAtUtc = DateTime.UtcNow,
                    Ranks = ranks
                };

                if (Save(file))
                {
                    _cache = file;
                    App.Logger?.Debug("LeaderboardRankSnapshot: recorded {Count} ranks for {Mode}", ranks.Count, key);
                }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "LeaderboardRankSnapshot: RecordIfDue failed for {Mode}", mode);
        }
    }

    // ---------- internals ----------

    /// <summary>
    /// Force a timestamp into UTC. Newtonsoft's default zone handling round-trips the "Z" suffix,
    /// but a hand-edited or older file can carry an Unspecified/Local kind — subtracting one of
    /// those from <see cref="DateTime.UtcNow"/> would skew the age by the local UTC offset and
    /// could hold the baseline hostage for hours.
    /// </summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>Mode keys are stored lower-cased so "All-Time" and "all-time" resolve to one record.</summary>
    private static string NormalizeMode(string mode) => mode.Trim().ToLowerInvariant();

    /// <summary>
    /// Best <see cref="MaxEntriesPerMode"/> rows by rank, keyed by unified id. Rows without a
    /// unified id are dropped (they can never be matched again), as are non-positive ranks
    /// (a "previous rank" of 0 carries no delta). First occurrence of a duplicate id wins.
    /// </summary>
    private static Dictionary<string, int> BuildRankMap(IEnumerable<LeaderboardEntry> entries)
    {
        var map = new Dictionary<string, int>();

        foreach (var entry in entries
                     .Where(e => e != null && e.Rank > 0 && !string.IsNullOrWhiteSpace(e.UnifiedId))
                     .OrderBy(e => e.Rank))
        {
            if (map.Count >= MaxEntriesPerMode) break;
            map.TryAdd(entry.UnifiedId!, entry.Rank);
        }

        return map;
    }

    /// <summary>
    /// Read the file once and cache it. A parse failure is cached too (as an empty snapshot) so a
    /// corrupt file doesn't get re-read and re-logged once per rendered row; the next successful
    /// <see cref="RecordIfDue"/> replaces it.
    /// </summary>
    private static SnapshotFile? EnsureLoaded()
    {
        if (_loaded) return _cache;
        _loaded = true;

        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                _cache = null;
                return null;
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                _cache = null;
                return null;
            }

            var parsed = JsonConvert.DeserializeObject<SnapshotFile>(json);
            if (parsed == null)
            {
                App.Logger?.Warning("LeaderboardRankSnapshot: snapshot file parsed to null, treating as absent");
                _cache = null;
                return null;
            }

            // Newtonsoft rebuilds dictionaries with the default comparer and can hand back nulls
            // for absent members, so normalize the shape before anything reads it.
            var normalized = new SnapshotFile();
            foreach (var kvp in parsed.Modes)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Value == null) continue;
                normalized.Modes[NormalizeMode(kvp.Key)] = new ModeSnapshot
                {
                    CapturedAtUtc = ToUtc(kvp.Value.CapturedAtUtc),
                    Ranks = kvp.Value.Ranks ?? new Dictionary<string, int>()
                };
            }

            _cache = normalized;
            return _cache;
        }
        catch (Exception ex)
        {
            // Corrupt, truncated, locked or hand-edited: degrade to "no previous rank known".
            App.Logger?.Warning(ex, "LeaderboardRankSnapshot: failed to load snapshot, deltas disabled until next write");
            _cache = null;
            return null;
        }
    }

    /// <summary>
    /// Write via a temp file + move, so a crash mid-write cannot leave a half-written file that
    /// permanently kills deltas. Returns true when the file is on disk.
    /// </summary>
    private static bool Save(SnapshotFile file)
    {
        var path = FilePath;
        var temp = path + ".tmp";

        try
        {
            Directory.CreateDirectory(App.UserDataPath);
            File.WriteAllText(temp, JsonConvert.SerializeObject(file, Formatting.Indented));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "LeaderboardRankSnapshot: failed to save snapshot");
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch
            {
                // Best effort — a stray .tmp is harmless, it is overwritten on the next write.
            }
            return false;
        }
    }

    // ---------- persisted shape ----------

    /// <summary>Root document: one record per mode ("monthly" / "all-time").</summary>
    private sealed class SnapshotFile
    {
        [JsonProperty("modes")]
        public Dictionary<string, ModeSnapshot> Modes { get; set; } = new();
    }

    /// <summary>One captured board: when it was taken, and unifiedId -> rank.</summary>
    private sealed class ModeSnapshot
    {
        [JsonProperty("captured_at_utc")]
        public DateTime CapturedAtUtc { get; set; }

        [JsonProperty("ranks")]
        public Dictionary<string, int> Ranks { get; set; } = new();
    }
}
