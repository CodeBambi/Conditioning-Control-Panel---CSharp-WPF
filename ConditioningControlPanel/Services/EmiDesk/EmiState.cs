using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// EMI Desk's persisted state: <c>%LOCALAPPDATA%\ConditioningControlPanel\emi-desk.json</c>.
///
/// This is the runtime ledger, NOT settings. Settings (the switches the user flips) live in
/// <see cref="Models.AppSettings"/>; everything here is state the widget writes about itself:
/// where she was parked and on which monitor, which cards are pinned, how often each target has
/// been opened, which lines each pool has already dealt, the global recent-id ring, the ignore
/// streak, the bedtime cutoff and whether she has ever been summoned.
///
/// Load is lazy and never throws: a corrupt or half-written file is logged once and replaced by a
/// fresh state, because losing "which line she told you last week" is not worth a crash dialog.
/// Save is debounced 500 ms on the dispatcher so the ring's rapid-fire counter bumps collapse into
/// one write.
/// </summary>
public sealed class EmiState
{
    /// <summary>Schema version. Bump when a field's meaning changes, not when one is added.</summary>
    [JsonProperty("version")]
    public int Version { get; set; } = 1;

    // ---- window ----------------------------------------------------------------

    /// <summary>
    /// Her last window rect in PHYSICAL PIXELS, not DIPs. The DIPs-vs-pixels trap is the same one
    /// the gaze audit documents: a rect stored in DIPs and restored on a differently scaled
    /// monitor lands somewhere else entirely.
    /// </summary>
    [JsonProperty("winLeftPx")] public double WinLeftPx { get; set; } = double.NaN;

    /// <inheritdoc cref="WinLeftPx"/>
    [JsonProperty("winTopPx")] public double WinTopPx { get; set; } = double.NaN;

    /// <summary>
    /// The <c>Screen.DeviceName</c> she was parked on (e.g. <c>\\.\DISPLAY2</c>). On restore, a
    /// monitor that is gone means "fall back to the main window's monitor", never "park her at a
    /// coordinate nobody can see".
    /// </summary>
    [JsonProperty("monitor")] public string? Monitor { get; set; }

    // ---- ring ------------------------------------------------------------------

    /// <summary>Pinned ring target ids, in slot order. Chunk B2 owns the semantics.</summary>
    [JsonProperty("pins")] public List<string> Pins { get; set; } = new();

    /// <summary>
    /// Per-target open counts, the suggester's raw input. Chunk B2 applies the 7-day half-life
    /// decay; this file only ever holds the tally and the timestamps beside it.
    /// </summary>
    [JsonProperty("usage")] public Dictionary<string, int> Usage { get; set; } = new();

    /// <summary>Per-target last-open time (UTC ticks), the decay's clock.</summary>
    [JsonProperty("usageAt")] public Dictionary<string, long> UsageAt { get; set; } = new();

    /// <summary>
    /// Per-target DECAYED open score: the sum, over every open ever recorded, of
    /// <c>0.5 ^ (ageInDays / 7)</c>. Kept in the incremental form
    /// <c>score = score * 0.5^(age/7) + 1</c> on each open, which is algebraically the same sum
    /// and costs one double per target instead of a list of timestamps. Read it back through
    /// <c>EmiSuggester.ScoreOf</c>, which applies the remaining decay from <see cref="UsageAt"/>
    /// to now; the number stored here is only current as of that target's last open.
    /// </summary>
    [JsonProperty("openScore")] public Dictionary<string, double> OpenScore { get; set; } = new();

    /// <summary>
    /// Consecutive ring opens that were closed again without picking a card. Three in a row and
    /// the ring fires <c>suggestionIgnored3x</c>. Reset by any pick. Chunk B2 owns it; it is
    /// deliberately NOT <see cref="IgnoreStreak"/>, which counts unanswered offers.
    /// </summary>
    [JsonProperty("ringIgnoreStreak")] public int RingIgnoreStreak { get; set; }

    // ---- lines -----------------------------------------------------------------

    /// <summary>
    /// Line ids already dealt out of each pool's shuffle bag. A line is never re-proposed until
    /// its pool is exhausted; when the bag empties the pool reshuffles and this list resets.
    /// Chunk B3 owns the rotation; this file just remembers it across launches.
    /// </summary>
    [JsonProperty("seenByPool")] public Dictionary<string, List<string>> SeenByPool { get; set; } = new();

    /// <summary>The global recent ring: the last 40 line ids, newest last.</summary>
    [JsonProperty("recentIds")] public List<string> RecentIds { get; set; } = new();

    // ---- offers ----------------------------------------------------------------

    /// <summary>Consecutive ignored offers. Three in a row and she stops offering for the session.</summary>
    [JsonProperty("ignoreStreak")] public int IgnoreStreak { get; set; }

    /// <summary>
    /// Offers are muted until this UTC time (the bedtime effect, which runs to 06:00 and never
    /// closes the app). Default (<c>MinValue</c>) means no bedtime is set.
    /// </summary>
    [JsonProperty("bedtimeUntil")] public DateTime BedtimeUntil { get; set; } = DateTime.MinValue;

    /// <summary>
    /// Persisted line-engine limit buckets (LINES-SCHEMA 2.1): the <c>ever</c>, <c>day</c>,
    /// <c>night</c>, <c>featureDay</c> and <c>version</c> counters. The volatile buckets (launch,
    /// video, run, lockdown, rush, per-target) die with the process and never reach this file.
    /// Yesterday's day / night / featureDay keys are pruned on the engine's first load.
    /// </summary>
    [JsonProperty("limits")] public Dictionary<string, int> Limits { get; set; } = new();

    // ---- first boot ------------------------------------------------------------

    /// <summary>
    /// How many times she has EVER been summoned. The offer cadence reads it: she never asks
    /// anything before the third summon (BRIEF 7), across launches, not per launch.
    /// </summary>
    [JsonProperty("summonCount")] public int SummonCount { get; set; }

    /// <summary>True once she has been summoned at least once (gates the desktopFirstBoot moment).</summary>
    [JsonProperty("firstBootSeen")] public bool FirstBootSeen { get; set; }

    // ---- onboarding (the nudge machine, wave 3) --------------------------------

    /// <summary>
    /// How many times she has EVER been patted, by any route: a left click on her body or the
    /// 1.2 s hover on her head. It is the pet nudge's stop condition and the <c>{n}</c> the
    /// <c>petted</c> pool counts out loud, so a gesture she visibly answered counts even when the
    /// pet cooldown swallowed the performance - the user learned the gesture either way, which is
    /// the only thing the nudge is trying to teach.
    /// </summary>
    [JsonProperty("petsTotal")] public int PetsTotal { get; set; }

    /// <summary>
    /// The pet nudge is DONE, forever. Latched once <see cref="PetsTotal"/> reaches
    /// <c>EmiNudgeMachine.PetGistCount</c> and never cleared by anything but the QA reset: a
    /// tutorial that comes back after you have learned the thing is the definition of nagging.
    /// </summary>
    [JsonProperty("petGistGot")] public bool PetGistGot { get; set; }

    /// <summary>How many times the ring has ever been opened. The ring nudge's stop condition.</summary>
    [JsonProperty("ringOpens")] public int RingOpens { get; set; }

    /// <inheritdoc cref="PetGistGot"/>
    [JsonProperty("ringGistGot")] public bool RingGistGot { get; set; }

    /// <summary>True once the user has pinned anything at all, by the ring or by the settings picker.</summary>
    [JsonProperty("pinGistGot")] public bool PinGistGot { get; set; }

    /// <summary>
    /// Lifetime fire count per nudge track. The hard cap
    /// (<c>EmiNudgeMachine.LifetimeCap</c>) is enforced against this, on top of the lines file's
    /// own <c>limit: {per:"ever"}</c>, so a pool that is re-authored without a limit still cannot
    /// nag a user forever.
    /// </summary>
    [JsonProperty("nudgeFires")] public Dictionary<string, int> NudgeFires { get; set; } = new();

    // ============================================================================
    // load / save
    // ============================================================================

    private static readonly object Gate = new();
    private static EmiState? _current;
    private static DispatcherTimer? _saveTimer;
    private static bool _dirty;
    private static bool _loadWarned;

    /// <summary>The state file's absolute path.</summary>
    public static string FilePath
    {
        get
        {
            try { return Path.Combine(App.UserDataPath, "emi-desk.json"); }
            catch { return Path.Combine(Path.GetTempPath(), "emi-desk.json"); }
        }
    }

    /// <summary>
    /// The live state, loaded on first touch. Never null and never throws: a missing or corrupt
    /// file yields a fresh instance (logged once).
    /// </summary>
    public static EmiState Current
    {
        get
        {
            if (_current != null) return _current;
            lock (Gate)
            {
                if (_current != null) return _current;
                _current = Load();
                return _current;
            }
        }
    }

    private static EmiState Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                Log.Information("[EmiDesk] no state file yet, starting fresh");
                return new EmiState();
            }
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return new EmiState();
            var s = JsonConvert.DeserializeObject<EmiState>(json);
            if (s == null) return new EmiState();
            // A hand-edited file can carry nulls where the ctor put collections.
            s.Pins ??= new List<string>();
            s.Usage ??= new Dictionary<string, int>();
            s.OpenScore ??= new Dictionary<string, double>();
            s.UsageAt ??= new Dictionary<string, long>();
            s.SeenByPool ??= new Dictionary<string, List<string>>();
            s.RecentIds ??= new List<string>();
            s.Limits ??= new Dictionary<string, int>();
            s.NudgeFires ??= new Dictionary<string, int>();
            Log.Information("[EmiDesk] state loaded ({Pins} pins, {Usage} tracked targets)",
                s.Pins.Count, s.Usage.Count);
            return s;
        }
        catch (Exception ex)
        {
            if (!_loadWarned)
            {
                _loadWarned = true;
                Log.Warning(ex, "[EmiDesk] state file unreadable, starting fresh");
            }
            return new EmiState();
        }
    }

    /// <summary>
    /// Mark the state dirty and schedule a write 500 ms out on the dispatcher. Rapid-fire callers
    /// (a ring open bumping four counters) collapse into one write. Safe from any thread.
    /// </summary>
    public static void SaveSoon()
    {
        try
        {
            _dirty = true;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted)
            {
                SaveNow();
                return;
            }
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(SaveSoon));
                return;
            }
            if (_saveTimer == null)
            {
                _saveTimer = new DispatcherTimer(DispatcherPriority.Background, disp)
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _saveTimer.Tick += (_, _) =>
                {
                    try
                    {
                        _saveTimer?.Stop();
                        SaveNow();
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "[EmiDesk] debounced save failed");
                    }
                };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] SaveSoon failed");
        }
    }

    /// <summary>
    /// Write the state now (app shutdown, or a caller that cannot wait for the debounce). Writes
    /// to a temp file and moves it into place so a kill mid-write cannot leave a half file.
    /// </summary>
    public static void SaveNow()
    {
        if (!_dirty && _current == null) return;
        lock (Gate)
        {
            try
            {
                _dirty = false;
                var s = _current;
                if (s == null) return;
                var path = FilePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(s, Formatting.Indented);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] state save failed");
            }
        }
    }

    // ---- small helpers the later chunks lean on --------------------------------

    /// <summary>Bump a target's open counter and stamp its clock. Debounced save.</summary>
    public static void NoteUsage(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return;
        try
        {
            var s = Current;
            s.Usage.TryGetValue(targetId, out int n);
            s.Usage[targetId] = n + 1;

            // The decayed score, folded forward before the clock moves: an open worth 1.0 today
            // is worth 0.5 in a week. Doing it here rather than in the ring means EVERY open
            // counts, however the feature was reached.
            s.OpenScore.TryGetValue(targetId, out double score);
            if (score > 0 && s.UsageAt.TryGetValue(targetId, out long lastAt))
            {
                double days = (DateTime.UtcNow - new DateTime(lastAt, DateTimeKind.Utc)).TotalDays;
                if (days > 0) score *= Math.Pow(0.5, days / 7.0);
                if (double.IsNaN(score) || double.IsInfinity(score) || score < 0) score = 0;
            }
            s.OpenScore[targetId] = score + 1.0;

            s.UsageAt[targetId] = DateTime.UtcNow.Ticks;
            SaveSoon();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] NoteUsage failed for {Target}", targetId);
        }
    }

    /// <summary>Count one summon and remember she has been out at least once. Debounced save.</summary>
    public static int NoteSummon()
    {
        try
        {
            var s = Current;
            s.SummonCount++;
            s.FirstBootSeen = true;
            SaveSoon();
            return s.SummonCount;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] NoteSummon failed");
            return 0;
        }
    }

    // ---- onboarding counters ---------------------------------------------------

    /// <summary>
    /// Count one pat and return the new total. Latches <see cref="PetGistGot"/> at the machine's
    /// gist count, which is what makes the pet nudge stop for good.
    /// </summary>
    public static int NotePet()
    {
        try
        {
            var s = Current;
            s.PetsTotal++;
            if (!s.PetGistGot && s.PetsTotal >= EmiNudgeMachine.PetGistCount)
            {
                s.PetGistGot = true;
                Log.Information("[EmiDesk] pet nudge retired: {N} pats", s.PetsTotal);
            }
            SaveSoon();
            return s.PetsTotal;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] NotePet failed");
            return 0;
        }
    }

    /// <summary>Count one ring open and return the new total. Latches <see cref="RingGistGot"/>.</summary>
    public static int NoteRingOpen()
    {
        try
        {
            var s = Current;
            s.RingOpens++;
            if (!s.RingGistGot && s.RingOpens >= EmiNudgeMachine.RingGistCount)
            {
                s.RingGistGot = true;
                Log.Information("[EmiDesk] ring nudge retired: {N} opens", s.RingOpens);
            }
            SaveSoon();
            return s.RingOpens;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] NoteRingOpen failed");
            return 0;
        }
    }

    /// <summary>The user pinned something, anywhere. Retires the pin nudge for good.</summary>
    public static void NotePinMade()
    {
        try
        {
            var s = Current;
            if (s.PinGistGot) return;
            s.PinGistGot = true;
            Log.Information("[EmiDesk] pin nudge retired: first pin made");
            SaveSoon();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] NotePinMade failed");
        }
    }

    /// <summary>One nudge landed on screen. Bumps that track's lifetime counter.</summary>
    public static void NoteNudgeFired(string track)
    {
        if (string.IsNullOrWhiteSpace(track)) return;
        try
        {
            var s = Current;
            s.NudgeFires.TryGetValue(track, out int n);
            s.NudgeFires[track] = n + 1;
            SaveSoon();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] NoteNudgeFired({Track}) failed", track);
        }
    }

    /// <summary>
    /// QA ONLY: put the onboarding tracker back to a fresh install so the three nudges can be
    /// replayed without deleting the whole ledger (which would also throw away the pins, the
    /// usage scores and every shuffle bag). Reached through <see cref="EmiDebug"/>.
    /// </summary>
    public static void ResetOnboarding()
    {
        try
        {
            var s = Current;
            s.PetsTotal = 0;
            s.PetGistGot = false;
            s.RingOpens = 0;
            s.RingGistGot = false;
            s.PinGistGot = false;
            s.NudgeFires.Clear();
            SaveNow();
            Log.Information("[EmiDesk] onboarding tracker reset (QA)");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] onboarding reset failed");
        }
    }

    /// <summary>Push a line id onto the global recent ring, capped at 40. Debounced save.</summary>
    public static void NoteLine(string lineId)
    {
        if (string.IsNullOrWhiteSpace(lineId)) return;
        try
        {
            var s = Current;
            s.RecentIds.Remove(lineId);
            s.RecentIds.Add(lineId);
            while (s.RecentIds.Count > 40) s.RecentIds.RemoveAt(0);
            SaveSoon();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] NoteLine failed for {Line}", lineId);
        }
    }
}
