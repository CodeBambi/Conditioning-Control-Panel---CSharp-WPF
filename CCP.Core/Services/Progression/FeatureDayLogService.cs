using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Keeps <see cref="FeatureDayLog"/> current. Ported from WPF 7.1.5
/// Services/Progression/FeatureDayLogService.cs: every 60 s, at start, and whenever the profile
/// sync asks via <see cref="Flush"/>, it reads the lifetime counters the app already keeps
/// (AchievementProgress.Total* and the total conditioning minutes in settings), credits today's
/// entry with the whole units each one moved since the last credit, and moves the baseline
/// forward. Fractions of a minute stay in the baseline until they add up to one. A counter that
/// went DOWN (reset, profile restore) re-baselines instead of writing a negative. No network.
///
/// <para>Head-free: the counters come from a reader the head passes in, the clock is a
/// <see cref="Timer"/> (WPF used a DispatcherTimer; <see cref="Tick"/> was always safe from any
/// thread). Internal because the in-tree WPF app still carries its own class of this name.</para>
/// </summary>
internal sealed class FeatureDayLogService : IDisposable
{
    /// <summary>The live service (WPF <c>App.FeatureDayLog</c>). Null = this run records no day log.</summary>
    public static volatile FeatureDayLogService? Current;

    /// <summary>Newest days the day log may carry over the wire (WPF ProfileSyncService.FeatureDayLogWireCap).</summary>
    public const int WireCap = 400;

    private readonly string _path;
    private readonly object _lock = new();
    private readonly Func<Dictionary<string, double>?> _readCounters;
    private readonly Func<DateTime> _today;
    private readonly Timer? _timer;
    private bool _dirty;
    private bool _disposed;

    public FeatureDayLog Log { get; private set; }

    public static string DefaultPath => Path.Combine(CorePaths.UserData, "feature_day_log.json");

    /// <param name="readCounters">Current lifetime values keyed by wire name; null when the sources are not up yet.</param>
    /// <param name="startTimer">False in tests: nothing ticks unless the test says so.</param>
    public FeatureDayLogService(string path, Func<Dictionary<string, double>?> readCounters, Func<DateTime>? today = null, bool startTimer = true)
    {
        _path = path;
        _readCounters = readCounters;
        _today = today ?? (() => DateTime.Today);
        Log = LoadLog();

        try { Tick(); } catch (Exception ex) { Serilog.Log.Debug(ex, "[FeatureDayLog] initial tick failed"); }

        if (startTimer) _timer = new Timer(_ => OnTimerTick(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    /// <summary>WPF ReadCounters: the eleven lifetime sources, by wire name.</summary>
    public static Dictionary<string, double>? ReadCounters(AchievementProgress? p, AppSettings? settings)
    {
        if (p == null || settings == null) return null;
        return new Dictionary<string, double>
        {
            ["xp"] = p.TotalXPEarned,
            ["cm"] = settings.TotalConditioningMinutes,
            ["fl"] = p.TotalFlashImages,
            ["bb"] = p.TotalBubblesPopped,
            ["pf"] = p.TotalPinkFilterMinutes,
            ["sp"] = p.TotalSpiralMinutes,
            ["vd"] = p.TotalVideoMinutes,
            ["lk"] = p.TotalLockCardsCompleted,
            ["ac"] = p.TotalAttentionChecksPassed,
            ["bc"] = p.TotalBubbleCountGames,
            ["ss"] = p.TotalSessionsStarted,
        };
    }

    private void OnTimerTick()
    {
        if (_disposed) return;
        try
        {
            Tick();
            SaveIfDirtyAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "[FeatureDayLog] tick failed");
        }
    }

    /// <summary>The local day key, identical to the quest log's (QuestService.DayKey).</summary>
    public static string DayKey(DateTime day) =>
        day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Credit today with whatever the lifetime counters gained since the last credit. Safe from
    /// any thread; cheap enough for a 60 s timer.
    /// </summary>
    public void Tick()
    {
        var current = _readCounters();
        if (current == null) return;

        lock (_lock)
        {
            var today = _today().Date;
            var todayKey = DayKey(today);
            FeatureDayEntry? entry = null;

            foreach (var key in FeatureDayEntry.CounterKeys)
            {
                if (!current.TryGetValue(key, out var now)) continue;
                if (double.IsNaN(now) || double.IsInfinity(now)) continue;

                if (!Log.Baseline.TryGetValue(key, out var prev) || now < prev)
                {
                    // First sight of this counter, or it dropped: start counting from here.
                    Log.Baseline[key] = now;
                    _dirty = true;
                    continue;
                }

                var whole = (int)Math.Floor(now - prev);
                if (whole <= 0) continue;

                entry ??= Log.GetOrAddDay(todayKey);
                entry.Add(key, whole);
                Log.Baseline[key] = prev + whole;
                _dirty = true;
            }

            if (entry != null)
            {
                Log.Prune(DayKey(today.AddDays(-FeatureDayLog.MaxDays)));
            }
        }
    }

    /// <summary>
    /// Credit today with one engagement event (<see cref="FeatureDayEntry.EventKeys"/>). There is
    /// no lifetime source to diff against, so the hook IS the record: a key outside the whitelist
    /// is dropped here rather than stored. Safe from any thread; the 60 s timer (or the next
    /// sync's Flush) writes it to disk.
    /// </summary>
    public void Note(string eventKey, int amount = 1)
    {
        if (amount <= 0 || string.IsNullOrEmpty(eventKey) || !FeatureDayEntry.IsEventKey(eventKey)) return;
        lock (_lock)
        {
            var today = _today().Date;
            Log.GetOrAddDay(DayKey(today)).Add(eventKey, amount);
            Log.Prune(DayKey(today.AddDays(-FeatureDayLog.MaxDays)));
            _dirty = true;
        }
    }

    /// <summary>
    /// Move every baseline to the current lifetime value without crediting the difference. For
    /// the moment a cloud merge lifts the local counters to what another device banked: that
    /// gain belongs to other days, not to today.
    /// </summary>
    public void Rebaseline(string reason)
    {
        var current = _readCounters();
        if (current == null) return;
        lock (_lock)
        {
            foreach (var key in FeatureDayEntry.CounterKeys)
            {
                if (!current.TryGetValue(key, out var now)) continue;
                if (double.IsNaN(now) || double.IsInfinity(now)) continue;
                Log.Baseline[key] = now;
            }
            _dirty = true;
        }
        Serilog.Log.Debug("[FeatureDayLog] re-baselined ({Reason})", reason);
        SaveIfDirtyAsync();
    }

    /// <summary>Tick and write to disk now. The profile sync calls this before building its body.</summary>
    public void Flush()
    {
        try
        {
            Tick();
            SaveNow();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "[FeatureDayLog] flush failed");
        }
    }

    /// <summary>Conditioning minutes booked on <paramref name="dayKey"/>; 0 for a day with no entry.</summary>
    public int MinutesOn(string dayKey)
    {
        lock (_lock)
        {
            foreach (var entry in Log.Days)
                if (entry != null && entry.D == dayKey) return entry.Cm;
            return 0;
        }
    }

    /// <summary>
    /// Outbound <c>stats.feature_day_log</c>: newest <paramref name="cap"/> non-empty days, each
    /// with <c>d</c> and only its non-zero counters.
    /// </summary>
    public List<Dictionary<string, object>> BuildWirePayload(int cap)
    {
        lock (_lock)
        {
            return Log.Days
                .Where(e => e != null && !string.IsNullOrEmpty(e.D) && !e.IsEmpty)
                .OrderByDescending(e => e.D, StringComparer.Ordinal)
                .Take(cap)
                .OrderBy(e => e.D, StringComparer.Ordinal)
                .Select(e => e.ToWire())
                .ToList();
        }
    }

    /// <summary>
    /// WPF ProfileSyncService: Flush, then BuildFeatureDayLogPayload. Null when no service runs
    /// (the sync then leaves the key out); any failure sends none.
    /// </summary>
    public static List<Dictionary<string, object>>? WirePayloadForSync()
    {
        var service = Current;
        if (service == null) return null;
        try
        {
            service.Flush();
            return service.BuildWirePayload(WireCap);
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "[FeatureDayLog] wire payload failed, sending none");
            return new List<Dictionary<string, object>>();
        }
    }

    #region Persistence

    private FeatureDayLog LoadLog()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var log = JsonSerializer.Deserialize<FeatureDayLog>(json);
                if (log != null)
                {
                    log.Baseline ??= new Dictionary<string, double>();
                    log.Days ??= new List<FeatureDayEntry>();
                    return log;
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[FeatureDayLog] feature_day_log.json unreadable, starting a fresh log");
        }
        return new FeatureDayLog();
    }

    private string? SerializeIfDirty()
    {
        lock (_lock)
        {
            if (!_dirty) return null;
            _dirty = false;
            return JsonSerializer.Serialize(Log, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    private readonly object _fileLock = new();

    private void WriteFile(string json)
    {
        lock (_fileLock)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
    }

    private void SaveIfDirtyAsync()
    {
        var json = SerializeIfDirty();
        if (json == null) return;
        _ = Task.Run(() =>
        {
            try { WriteFile(json); }
            catch (Exception ex) { Serilog.Log.Error(ex, "[FeatureDayLog] save failed"); }
        });
    }

    /// <summary>Synchronous save of any pending change (shutdown, sync flush).</summary>
    public void SaveNow()
    {
        var json = SerializeIfDirty();
        if (json == null) return;
        try { WriteFile(json); }
        catch (Exception ex) { Serilog.Log.Error(ex, "[FeatureDayLog] save failed"); }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _timer?.Dispose(); } catch { }
        try { Tick(); } catch { }
        SaveNow();
        if (ReferenceEquals(Current, this)) Current = null;
    }
}

/// <summary>
/// WPF <c>SeasonRecapService.TrackFeature</c>: the one hook every feature calls. It feeds the
/// per-day engagement log the server aggregates (<c>stats.feature_day_log</c> event keys; keys
/// with no wire name stay local) and the season bucket the recap card and the Ditzy Data bars
/// read (Catalog keys only: a Lab launch or a chat message counted there would skew those bars).
/// Named apart from WPF's SeasonRecapService, which stays in the WPF head with the rollover.
/// </summary>
internal static class SeasonFeatureTracker
{
    public static void TrackFeature(string featureKey)
    {
        try
        {
            var ev = SeasonFeatureKeys.ToDayLogEvent(featureKey);
            if (ev != null) FeatureDayLogService.Current?.Note(ev);

            if (SeasonFeatureKeys.Find(featureKey) == null) return;

            var s = CoreSettings.Current;
            if (s == null) return;
            s.TrackSeasonFeature(featureKey);
        }
        catch (Exception ex) { Log.Debug(ex, "[FeatureDayLog] TrackFeature({Key}) failed", featureKey); }
    }
}
