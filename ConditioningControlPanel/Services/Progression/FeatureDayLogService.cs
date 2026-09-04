using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Keeps <see cref="FeatureDayLog"/> current (Spiral rail, desktop lane). It never hooks a
/// feature: every 60 s, at start, and whenever the profile sync asks via <see cref="Flush"/>, it
/// reads the lifetime counters the app already keeps (AchievementProgress.Total* and the total
/// conditioning minutes in settings), credits today's entry with the whole units each one moved
/// since the last credit, and moves the baseline forward. Fractions of a minute stay in the
/// baseline until they add up to one, so slow counters are never floored away. A counter that
/// went DOWN (reset, profile restore) re-baselines instead of writing a negative. No network.
/// </summary>
public class FeatureDayLogService : IDisposable
{
    private readonly string _path;
    private readonly object _lock = new();
    private readonly DispatcherTimer _timer;
    private bool _dirty;
    private bool _disposed;

    public FeatureDayLog Log { get; private set; }

    public FeatureDayLogService()
    {
        _path = Path.Combine(App.UserDataPath, "feature_day_log.json");
        Log = LoadLog();

        try { Tick(); } catch (Exception ex) { App.Logger?.Debug(ex, "[FeatureDayLog] initial tick failed"); }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted) return;
        try
        {
            Tick();
            SaveIfDirtyAsync();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug(ex, "[FeatureDayLog] tick failed");
        }
    }

    /// <summary>The local day key, identical to the quest log's (QuestService.DayKey).</summary>
    private static string DayKey(DateTime day) =>
        day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Current lifetime values keyed by wire name. Null when the sources are not up yet.</summary>
    private static Dictionary<string, double>? ReadCounters()
    {
        var p = App.Achievements?.Progress;
        var settings = App.Settings?.Current;
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

    /// <summary>
    /// Credit today with whatever the lifetime counters gained since the last credit. Safe from
    /// any thread; cheap enough for a 60 s timer.
    /// </summary>
    public void Tick()
    {
        var current = ReadCounters();
        if (current == null) return;

        lock (_lock)
        {
            var today = DateTime.Today;
            var todayKey = DayKey(today);
            FeatureDayEntry? entry = null;

            foreach (var key in FeatureDayEntry.CounterKeys)
            {
                var now = current[key];
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
    /// Move every baseline to the current lifetime value without crediting the difference. For
    /// the moment a cloud merge lifts the local counters to what another device banked: that
    /// gain belongs to other days, not to today.
    /// </summary>
    public void Rebaseline(string reason)
    {
        var current = ReadCounters();
        if (current == null) return;
        lock (_lock)
        {
            foreach (var key in FeatureDayEntry.CounterKeys)
            {
                var now = current[key];
                if (double.IsNaN(now) || double.IsInfinity(now)) continue;
                Log.Baseline[key] = now;
            }
            _dirty = true;
        }
        App.Logger?.Debug("[FeatureDayLog] re-baselined ({Reason})", reason);
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
            App.Logger?.Debug(ex, "[FeatureDayLog] flush failed");
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
            App.Logger?.Warning(ex, "[FeatureDayLog] feature_day_log.json unreadable, starting a fresh log");
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

    private void WriteFile(string json)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }

    private void SaveIfDirtyAsync()
    {
        var json = SerializeIfDirty();
        if (json == null) return;
        _ = Task.Run(() =>
        {
            try { WriteFile(json); }
            catch (Exception ex) { App.Logger?.Error(ex, "[FeatureDayLog] save failed"); }
        });
    }

    /// <summary>Synchronous save of any pending change (shutdown, sync flush).</summary>
    public void SaveNow()
    {
        var json = SerializeIfDirty();
        if (json == null) return;
        try { WriteFile(json); }
        catch (Exception ex) { App.Logger?.Error(ex, "[FeatureDayLog] save failed"); }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _timer.Stop(); } catch { }
        try { Tick(); } catch { }
        SaveNow();
    }
}
