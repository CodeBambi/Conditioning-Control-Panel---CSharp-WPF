using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>
/// The Arcademy's persisted meta blob (<c>arcademy_meta.json</c> in <see cref="App.UserDataPath"/>):
/// attendance streak, per-class grade history, per-game progression (grade tiers, baselines) and
/// the shell's seen-once flags.
///
/// Same ownership model as <see cref="Chaos.DtrhMetaBridge"/> — C# owns the file, the page holds a
/// rev-numbered read snapshot and sends COMMANDS (<c>meta-command {op,key,value}</c>) which are
/// shape-validated here, applied, debounce-saved and answered. Unlike the DtRH bridge this state is
/// a free-form JSON object rather than a typed model: V1 per-game meta is authored page-side
/// (SYNTHESIS-NOTES #15) and pinning a C# class to it would make every game a C# change.
///
/// Validation is integrity, not anti-cheat (offline, the user's own save): unknown ops are logged
/// and ignored, keys and values are size-capped, and the two host-owned regions
/// (<see cref="AttendanceKey"/> / <see cref="StreakKey"/>) are written only by
/// <see cref="RecordAttendance"/> so a stale page cannot mint a streak.
/// </summary>
internal sealed class ArcademyMetaStore
{
    /// <summary>Host-owned: the local date (yyyy-MM-dd) attendance was last credited for.</summary>
    public const string AttendanceKey = "lastAttendanceLocalDate";

    /// <summary>Host-owned: consecutive local days with at least one completed class.</summary>
    public const string StreakKey = "streak";

    /// <summary>Host-owned: lifetime count of local days where all 3 classes were completed.</summary>
    public const string PerfectKey = "perfectAttendance";

    /// <summary>Host-owned: game keys completed on <see cref="AttendanceKey"/>'s day.</summary>
    public const string TodayClassesKey = "todayClasses";

    /// <summary>Host-owned: the XP ledger, <c>{ "&lt;utcDay&gt;": ["&lt;gameKey&gt;", …] }</c>. A class pays
    /// its XP once per UTC day; every later run of the same class that day is a free replay.</summary>
    public const string XpPaidKey = "xpPaidDays";

    /// <summary>Classes in a day — the timetable's fixed size (GROUND-RULES §4).</summary>
    private const int ClassesPerDay = 3;

    /// <summary>UTC days of XP ledger kept. Older days can no longer be replayed into (the page
    /// only ever ends a class on today's seed), so keeping them would just grow the blob.</summary>
    private const int XpPaidDayHistory = 14;

    private const int MaxKeyLength = 128;
    private const int MaxBlobChars = 512 * 1024;   // a meta file this big is a bug, not a save

    /// <summary>Keys the page may never write: the host mints attendance and the streak from
    /// <c>class-ended</c>, so a set/merge naming one is dropped (and logged) rather than applied.</summary>
    private static readonly HashSet<string> HostOwnedKeys = new(StringComparer.Ordinal)
    {
        AttendanceKey, StreakKey, PerfectKey, TodayClassesKey, XpPaidKey,
    };

    private readonly object _lock = new();
    private readonly Action<object> _broadcast;
    private readonly string _path;
    private JObject _state;
    private DispatcherTimer? _saveDebounce;
    private bool _dirty;

    public ArcademyMetaStore(Action<object> broadcast)
    {
        _broadcast = broadcast;
        _path = Path.Combine(App.UserDataPath, "arcademy_meta.json");
        _state = Load(_path);
    }

    public int Rev { get; private set; }

    /// <summary>The full blob for the <c>init</c> projection. A deep clone: the page's snapshot must
    /// not be a live handle onto the state a later command mutates.</summary>
    public JObject Snapshot()
    {
        lock (_lock) return (JObject)_state.DeepClone();
    }

    /// <summary>The <c>{type:"meta"}</c> broadcast body (whole-blob push, used after a host-side
    /// write such as an attendance credit).</summary>
    public object SnapshotMessage()
    {
        lock (_lock)
        {
            return new { type = "meta", rev = Rev, state = (JObject)_state.DeepClone() };
        }
    }

    /// <summary>
    /// Handle a <c>meta-command {op:"get"|"set"|"merge", key, value?}</c>. Always answers with
    /// <c>{type:"meta", key, value}</c> carrying the POST-write value, so the page's cache converges
    /// on what C# actually stored even when a write was clamped or refused.
    /// </summary>
    public void Handle(JObject o)
    {
        var op = (string?)o["op"];
        var key = NormalizeKey((string?)o["key"]);
        if (key == null)
        {
            App.Logger?.Debug("ArcademyMetaStore: command '{Op}' with a missing/oversized key - ignored", op);
            return;
        }

        try
        {
            switch (op)
            {
                case "get":
                    break;   // the reply below IS the get
                case "set":
                    if (Guard(key)) Set(key, o["value"]);
                    break;
                case "merge":
                    if (Guard(key)) Merge(key, o["value"]);
                    break;
                default:
                    App.Logger?.Debug("ArcademyMetaStore: unknown op '{Op}' - ignored", op);
                    return;
            }

            JToken? value;
            lock (_lock) value = _state[key]?.DeepClone();
            _broadcast(new { type = "meta", key, value });
        }
        catch (Exception ex) { App.Logger?.Warning("ArcademyMetaStore.Handle({Op}): {E}", op, ex.Message); }
    }

    /// <summary>Replace one key outright.</summary>
    public void Set(string key, JToken? value)
    {
        lock (_lock)
        {
            _state[key] = value?.DeepClone() ?? JValue.CreateNull();
            Touch();
        }
    }

    /// <summary>Shallow-merge an object into one key (creating it when absent). A non-object patch
    /// is a set — the page uses merge for per-game bags, and refusing would just lose the write.</summary>
    public void Merge(string key, JToken? patch)
    {
        lock (_lock)
        {
            if (patch is JObject po)
            {
                if (_state[key] is JObject existing)
                {
                    foreach (var p in po.Properties()) existing[p.Name] = p.Value.DeepClone();
                }
                else _state[key] = po.DeepClone();
            }
            else _state[key] = patch?.DeepClone() ?? JValue.CreateNull();
            Touch();
        }
    }

    public JToken? Get(string key)
    {
        lock (_lock) return _state[key]?.DeepClone();
    }

    /// <summary>
    /// Credit one completed class to today's attendance and roll the streak. LOCAL date, always
    /// (regression #978: local midnight rolls the streak, the UTC date only seeds content).
    ///
    /// <para>Idempotent per game key: replaying the same class on the same day does not re-count it
    /// toward perfect attendance. A gap of exactly one day continues the streak, a larger gap
    /// restarts it at 1, and a day already credited leaves the streak alone.</para>
    /// </summary>
    /// <returns>(streak, perfectAttendance, classesToday) after the write.</returns>
    public (int Streak, int Perfect, int ClassesToday) RecordAttendance(string localDate, string? gameKey)
    {
        lock (_lock)
        {
            var last = (string?)_state[AttendanceKey];
            int streak = (int?)_state[StreakKey] ?? 0;
            int perfect = (int?)_state[PerfectKey] ?? 0;

            if (!string.Equals(last, localDate, StringComparison.Ordinal))
            {
                streak = IsPreviousDay(last, localDate) ? streak + 1 : 1;
                _state[AttendanceKey] = localDate;
                _state[StreakKey] = streak;
                _state[TodayClassesKey] = new JArray();
            }

            var today = _state[TodayClassesKey] as JArray;
            if (today == null) { today = new JArray(); _state[TodayClassesKey] = today; }
            if (!string.IsNullOrWhiteSpace(gameKey)
                && !today.Any(t => string.Equals((string?)t, gameKey, StringComparison.Ordinal)))
            {
                today.Add(gameKey);
                // The perfect-attendance credit fires on the transition INTO a full day, so a
                // fourth completion (a retake) can never award it twice.
                if (today.Count == ClassesPerDay)
                {
                    perfect += 1;
                    _state[PerfectKey] = perfect;
                }
            }

            Touch();
            return (streak, perfect, today.Count);
        }
    }

    /// <summary>
    /// Claim the one XP payment a <paramref name="gameKey"/> is worth on <paramref name="dayUtc"/>.
    /// True the first time that pair is seen, false forever after.
    ///
    /// <para>THE FARM GUARD. A class is replayable by design (the seed is the day's, so a retake is
    /// the same script) and nothing stops a player finishing Daily Trigger ten times before lunch.
    /// The ledger lives here rather than page-side for the same reason the streak does: a stale or
    /// edited page must not be able to mint a payout. Attendance is deliberately NOT keyed off this
    /// — <see cref="RecordAttendance"/> is idempotent on its own terms and runs on the LOCAL date
    /// (#978), so a retake still cannot double-count a class but a genuine new local day still
    /// credits one.</para>
    /// </summary>
    public bool TryClaimXpDay(string? gameKey, string? dayUtc)
    {
        if (string.IsNullOrWhiteSpace(gameKey) || string.IsNullOrWhiteSpace(dayUtc)) return true;
        lock (_lock)
        {
            if (_state[XpPaidKey] is not JObject paid)
            {
                paid = new JObject();
                _state[XpPaidKey] = paid;
            }
            if (paid[dayUtc] is not JArray day)
            {
                day = new JArray();
                paid[dayUtc] = day;
            }
            if (day.Any(t => string.Equals((string?)t, gameKey, StringComparison.Ordinal))) return false;

            day.Add(gameKey);
            foreach (var stale in paid.Properties().Select(p => p.Name)
                         .OrderBy(n => n, StringComparer.Ordinal)
                         .SkipLast(XpPaidDayHistory).ToList())
            {
                paid.Remove(stale);
            }
            Touch();
            return true;
        }
    }

    /// <summary>Flush any debounced write now (teardown / app exit).</summary>
    public void FlushSave()
    {
        try { _saveDebounce?.Stop(); } catch { }
        JObject snapshot;
        lock (_lock)
        {
            if (!_dirty) return;
            _dirty = false;
            snapshot = (JObject)_state.DeepClone();
        }
        try
        {
            Directory.CreateDirectory(App.UserDataPath);
            File.WriteAllText(_path, snapshot.ToString(Formatting.Indented));
        }
        catch (Exception ex) { App.Logger?.Warning("ArcademyMetaStore.FlushSave: {E}", ex.Message); }
    }

    // ============================ internals ============================

    /// <summary>Bump the revision and arm the debounced write. Callers own the broadcast (the page
    /// gets a per-key reply; a host-side write gets a whole-blob push). Called under the lock.</summary>
    private void Touch()
    {
        Rev++;
        _dirty = true;
        DebounceSave();
    }

    private void DebounceSave()
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted)
        {
            // No dispatcher to debounce on (unit host / shutting down): write through instead of
            // silently dropping the mutation.
            _dirty = true;
            var snapshot = (JObject)_state.DeepClone();
            try
            {
                Directory.CreateDirectory(App.UserDataPath);
                File.WriteAllText(_path, snapshot.ToString(Formatting.Indented));
                _dirty = false;
            }
            catch (Exception ex) { App.Logger?.Debug("ArcademyMetaStore.DebounceSave direct: {E}", ex.Message); }
            return;
        }
        disp.BeginInvoke(() =>
        {
            if (_saveDebounce == null)
            {
                _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _saveDebounce.Tick += (_, _) => { try { _saveDebounce?.Stop(); } catch { } FlushSave(); };
            }
            _saveDebounce.Stop();
            _saveDebounce.Start();
        });
    }

    /// <summary>False (and logged) for a key the host owns. The page gets a reply either way, so a
    /// refused write shows up page-side as "the value did not change".</summary>
    private static bool Guard(string key)
    {
        if (!HostOwnedKeys.Contains(key)) return true;
        App.Logger?.Debug("ArcademyMetaStore: page tried to write host-owned key '{Key}' - refused", key);
        return false;
    }

    private static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        key = key.Trim();
        return key.Length > MaxKeyLength ? null : key;
    }

    /// <summary>Is <paramref name="last"/> exactly the calendar day before <paramref name="today"/>?
    /// Anything unparseable answers false, which restarts the streak rather than extending one from
    /// a corrupt date.</summary>
    private static bool IsPreviousDay(string? last, string today)
    {
        if (string.IsNullOrWhiteSpace(last)) return false;
        if (!DateTime.TryParseExact(last, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var prev)) return false;
        if (!DateTime.TryParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var now)) return false;
        return (now.Date - prev.Date).TotalDays == 1;
    }

    /// <summary>Read the blob, or an empty object on any failure. A corrupt meta file must not stop
    /// the Arcademy opening: the shell degrades to a fresh transcript, which is recoverable, where a
    /// throw here would be a door that never opens again.</summary>
    private static JObject Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new JObject();
            var raw = File.ReadAllText(path);
            if (raw.Length > MaxBlobChars)
            {
                App.Logger?.Warning("ArcademyMetaStore: {Path} is {N} chars - starting fresh", path, raw.Length);
                return new JObject();
            }
            return JObject.Parse(raw);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ArcademyMetaStore: load failed ({E}) - starting fresh", ex.Message);
            return new JObject();
        }
    }
}
