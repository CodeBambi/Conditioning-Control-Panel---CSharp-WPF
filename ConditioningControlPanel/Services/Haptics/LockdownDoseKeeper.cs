using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Possession;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Haptics;

/// <summary>
/// THE DOSE - a lockdown refuses to run empty.
///
/// Play-test (2026-08-23) found the hole: you could activate a lockdown with the engine off, or turn
/// every feature off while it ran, and sit out the timer in a perfectly quiet room. The lock was
/// theatre. This keeper closes it in the warden's voice - "you aren't picking anything, so I pick":
///
///   * ACTIVATION: engine off -> the engine is started for the user (about two seconds in, so the
///     activation UI settles first). Nothing switched on -> the warden conscripts a dose first.
///   * WHILE LOCKED: the engine stops (a session ended, the ramp finished, the scheduler window
///     closed, a remote Stop...) -> restarted after a short grace. The dose goes empty (the user
///     switched everything off) -> that is an escape attempt (<see cref="EscapeKinds.Starve"/>, so
///     Possession answers like it answers the X), and after a grace that SHRINKS with every round
///     the warden turns features back on: two the first time, one more each round, four at most.
///     The features the user had on at activation come back first ("you turned it off; I turned it
///     back on"), then the gentle starter mix, and from round two the heavier pool.
///   * A RUNNING SESSION owns the dose (MainWindow.IsSessionFeatureLockActive) - the keeper stands
///     down until it ends, then the engine-restart branch catches the quiet room.
///   * DEACTIVATION gives everything back: every toggle the keeper flipped returns to its
///     pre-lockdown value and an engine the keeper started is stopped - the same "borrow, then
///     restore" contract the Safeties follow. A tiny recovery file (flipped keys) survives a crash
///     so a killed lockdown cannot leave Flash stuck on for a user who never turned it on.
///
/// Never a safety control: the keeper only touches the wall feature toggles
/// (<c>MainWindow.SetWallFeature</c>) and Start/Stop of the engine. Values, frequencies, volumes,
/// the panic key and Strict Lock are not its business. Fails OPEN on every exception.
/// Switched off by AppSettings.LockdownDoseKeeperEnabled (a Safeties toggle on the Lockdown card).
/// </summary>
public sealed class LockdownDoseKeeper : IDisposable
{
    // ---------------------------------------------------------------------------------------------
    //  Catalog
    // ---------------------------------------------------------------------------------------------

    /// <summary>A wall feature the keeper can read (and, for Tier 0/1, switch on). Keys are the wall
    /// keys <c>MainWindow.SetWallFeature</c> understands. Tier 0 = starter picks (cheap, visual, need no
    /// special assets); Tier 1 = escalation picks (round 2+, only when their assets exist); Tier 2 =
    /// counts as a dose when ON but is never picked (too loud, level-gated, or audio-dependent).</summary>
    public sealed record DoseFeature(string Key, string Name, Func<AppSettings, bool> IsOn, int Tier);

    public static readonly IReadOnlyList<DoseFeature> Catalog = new DoseFeature[]
    {
        new("flash",        "Flash",            s => s.FlashEnabled,           0),
        new("subliminal",   "Subliminals",      s => s.SubliminalEnabled,      0),
        new("spiral",       "the Spiral",       s => s.SpiralEnabled,          0),
        new("pinkfilter",   "the Pink Filter",  s => s.PinkFilterEnabled,      0),
        new("bouncingtext", "Bouncing Text",    s => s.BouncingTextEnabled,    0),
        new("bubbles",      "Bubbles",          s => s.BubblesEnabled,         0),
        new("video",        "Mandatory Videos", s => s.MandatoryVideosEnabled, 1),
        new("mindwipe",     "Mind Wipe",        s => s.MindWipeEnabled,        2),
        new("lockcard",     "the Lock Card",    s => s.LockCardEnabled,        2),
        new("bubblecount",  "Bubble Count",     s => s.BubbleCountEnabled,     2),
        new("braindrain",   "Brain Drain",      s => s.BrainDrainEnabled,      2),
    };

    /// <summary>Flags outside the wall that still mean "something is running" - the keeper must not
    /// talk over an audio-only bed, a corner GIF or a takeover just because the wall toggles are off.
    /// (Whisper audio is NOT here on purpose: SubliminalService plays it, so it is already covered by
    /// the subliminal toggle and means nothing on its own.)
    ///
    /// <para>Pop Quiz belongs here rather than in <see cref="Catalog"/>: <c>StartEngine</c> starts it
    /// off <c>PopQuizEnabled</c> like any wall feature, so a lockdown with only Pop Quiz on is NOT an
    /// empty room - but it is not a wall card, so <c>MainWindow.SetWallFeature</c> does not know its
    /// key and it must never be conscriptable. Living outside the catalog makes that true by
    /// construction: it counts, and it can never be picked.</para></summary>
    private static bool HasOffWallDose(AppSettings s) =>
        s.AudioOnlySession
        || s.PopQuizEnabled
        || (s.CornerGifOverlays?.Any(o => o != null && o.Enabled) == true)
        || (s.AutonomyModeEnabled && s.AutonomyConsentGiven);

    /// <summary>Test seam for the off-wall half of the census (Pop Quiz, the audio bed, corner GIFs,
    /// takeover). Public so the suite can pin what counts without a live lockdown.</summary>
    public static bool CountsAsOffWallDose(AppSettings s) => s != null && HasOffWallDose(s);

    /// <summary>True when NOTHING would run - no wall feature on and no off-wall dose either.</summary>
    public static bool DoseIsEmpty(AppSettings s)
    {
        if (s == null) return false;          // unknown = not our call; fail open
        if (HasOffWallDose(s)) return false;
        foreach (var f in Catalog)
        {
            try { if (f.IsOn(s)) return false; } catch { }
        }
        return true;
    }

    /// <summary>Keys currently ON, catalog order.</summary>
    public static List<string> KeysOn(AppSettings s)
    {
        var list = new List<string>();
        if (s == null) return list;
        foreach (var f in Catalog)
        {
            try { if (f.IsOn(s)) list.Add(f.Key); } catch { }
        }
        return list;
    }

    /// <summary>How many features round <paramref name="round"/> (1-based) conscripts: 2, 3, 4, 4...</summary>
    public static int WantedFor(int round) => Math.Clamp(1 + Math.Max(1, round), 2, 4);

    /// <summary>Grace (seconds) before an empty dose is refilled: 6 s the first time, then 4, then 2.
    /// Shrinks so the "switch it off again" game gets shorter every round, never longer.</summary>
    public static int DoseGraceFor(int roundsSoFar) => Math.Max(2, 6 - 2 * roundsSoFar);

    /// <summary>Grace (seconds) before a stopped engine is restarted mid-lockdown.</summary>
    public const int EngineGraceSeconds = 4;

    /// <summary>
    /// The pure picker. Order of preference: what the user themselves had on (shuffled), then the
    /// starter pool (shuffled), then - from round 2 - the escalation pool (shuffled). Anything already
    /// on is skipped, so the result is exactly the set of keys to flip. Deterministic under the
    /// supplied Random; never returns more than <see cref="WantedFor"/>.
    /// </summary>
    public static List<string> PickConscripts(int round, IReadOnlyCollection<string> previouslyOn,
        IReadOnlyList<string> starter, IReadOnlyList<string> escalation,
        IReadOnlyCollection<string> currentlyOn, Random rng)
    {
        var want = WantedFor(round);
        var picked = new List<string>(want);
        var skip = new HashSet<string>(currentlyOn ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var known = new HashSet<string>(starter.Concat(escalation), StringComparer.OrdinalIgnoreCase);

        void TakeFrom(IEnumerable<string> pool)
        {
            foreach (var k in Shuffle(pool, rng))
            {
                if (picked.Count >= want) return;
                if (string.IsNullOrEmpty(k) || skip.Contains(k) || !known.Contains(k)) continue;
                if (picked.Contains(k, StringComparer.OrdinalIgnoreCase)) continue;
                picked.Add(k);
            }
        }

        TakeFrom(previouslyOn ?? Array.Empty<string>());
        TakeFrom(starter);
        if (round >= 2) TakeFrom(escalation);
        return picked;
    }

    private static List<string> Shuffle(IEnumerable<string> src, Random rng)
    {
        var list = src.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    // ---------------------------------------------------------------------------------------------
    //  Runtime
    // ---------------------------------------------------------------------------------------------

    private readonly LockdownService _lockdown;
    private readonly Random _rng = new();
    private bool _installed;
    private bool _armed;
    private bool _busy;
    private bool _disposed;

    private int _round;                 // conscriptions so far this lockdown
    private int _engineIdle;            // consecutive ticks with the engine off
    private int _doseIdle;              // consecutive ticks with an empty dose
    private bool _wasEmpty;
    private bool _engineWasRunning;     // at activation
    private bool _weStartedEngine;
    private HashSet<string> _snapshotOn = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flipped = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _kickoff;

    /// <summary>Conscriptions so far in the current lockdown (0 = the user kept a dose going).</summary>
    public int Round => _round;
    public bool IsArmed => _armed;

    public LockdownDoseKeeper(LockdownService lockdown)
    {
        _lockdown = lockdown ?? throw new ArgumentNullException(nameof(lockdown));
    }

    public void Install()
    {
        if (_installed) return;
        _installed = true;
        _lockdown.LockdownActivated += OnActivated;
        _lockdown.LockdownDeactivated += OnDeactivated;
        _lockdown.CountdownTick += OnTick;
    }

    private static bool Enabled => App.Settings?.Current?.LockdownDoseKeeperEnabled != false;

    private void OnActivated()
    {
        try
        {
            if (!Enabled) { _armed = false; return; }
            var s = App.Settings?.Current;
            if (s == null) return;

            _armed = true;
            _round = 0;
            _engineIdle = 0;
            _doseIdle = 0;
            _flipped.Clear();
            _snapshotOn = new HashSet<string>(KeysOn(s), StringComparer.OrdinalIgnoreCase);
            _wasEmpty = DoseIsEmpty(s);
            _engineWasRunning = App.IsEngineRunning;
            _weStartedEngine = false;
            DeleteRecoveryFile();

            App.Logger?.Information("Lockdown dose: armed (engine {Engine}, dose {Dose}: {Keys})",
                _engineWasRunning ? "running" : "OFF", _wasEmpty ? "EMPTY" : "ok", string.Join(",", _snapshotOn));

            // First enforcement a beat after activation so the Activate click, the dialog and the
            // card's active-panel swap are all done before the engine lights up.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            _kickoff?.Stop();
            _kickoff = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromSeconds(2) };
            _kickoff.Tick += (_, _) =>
            {
                try { _kickoff?.Stop(); Enforce(atActivation: true); }
                catch (Exception ex) { App.Logger?.Warning("Lockdown dose: kickoff failed: {Error}", ex.Message); }
            };
            _kickoff.Start();
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Lockdown dose: arm failed: {Error}", ex.Message);
            _armed = false;
        }
    }

    private void OnTick(TimeSpan _)
    {
        if (!_armed || _disposed) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        if (!dispatcher.CheckAccess()) { dispatcher.BeginInvoke(() => OnTick(TimeSpan.Zero)); return; }
        try { Enforce(atActivation: false); }
        catch (Exception ex) { App.Logger?.Warning("Lockdown dose: tick failed: {Error}", ex.Message); }
    }

    /// <summary>One pass of the rule set. Idempotent per tick; all state is re-derived from the live
    /// settings and engine flag, nothing here latches a decision.</summary>
    private void Enforce(bool atActivation)
    {
        if (_busy || !_armed) return;
        if (!_lockdown.IsActive) { _armed = false; return; }
        if (!Enabled) return;   // turned off mid-lockdown: stand down, restore still runs at the end
        var s = App.Settings?.Current;
        // App.MainWindowRef, not Application.Current.MainWindow: the latter is null while the window
        // is hidden to tray, which is exactly where a lockdown run spends much of its time - and a
        // null here silently no-ops the whole enforcement pass. (Same read as Warden.cs / App.xaml.cs.)
        var mw = App.MainWindowRef ?? Application.Current?.MainWindow as MainWindow;
        if (s == null || mw == null) return;

        _busy = true;
        try
        {
            // A running session owns the dose (MainWindow.SessionFeatureLock rule 1). Stand down.
            //
            // _wasEmpty is deliberately NOT rewritten here. Rewriting it every tick consumed the empty
            // EDGE: a session that ends with every feature off left _wasEmpty already true, so the
            // `empty && !_wasEmpty` test below never fired and the room refilled silently, with no
            // `starve` tripwire and no warden line. The edge is what the tripwire is made of, so it
            // survives the stand-down and fires the moment the keeper is watching again.
            if (mw.IsSessionFeatureLockActive)
            {
                _engineIdle = 0; _doseIdle = 0;
                return;
            }

            var empty = DoseIsEmpty(s);

            // The user switched the last feature off. That is the dose equivalent of reaching for
            // the X: a tripwire, so Possession answers it (edge pulse, warden line, repeat count).
            if (empty && !_wasEmpty && !atActivation)
            {
                try { _lockdown.NotifyEscapeAttempt(EscapeKinds.Starve); } catch { }
            }
            _wasEmpty = empty;

            if (!App.IsEngineRunning)
            {
                _engineIdle++;
                if (atActivation || _engineIdle >= EngineGraceSeconds)
                {
                    var names = empty ? Conscript(s, mw) : null;
                    StartEngine(mw);
                    _engineIdle = 0;
                    _doseIdle = 0;
                    Bark(names, engineStarted: true);
                }
                return;
            }
            _engineIdle = 0;

            if (!empty) { _doseIdle = 0; return; }

            _doseIdle++;
            if (atActivation || _doseIdle >= DoseGraceFor(_round))
            {
                var names = Conscript(s, mw);
                _doseIdle = 0;
                Bark(names, engineStarted: false);
            }
        }
        finally { _busy = false; }
    }

    /// <summary>Turns the next batch of features on. Returns the display names (for the bark) or
    /// null when nothing could be picked.</summary>
    private string? Conscript(AppSettings s, MainWindow mw)
    {
        var round = _round + 1;
        var on = KeysOn(s);
        var starter = Catalog.Where(f => f.Tier == 0).Select(f => f.Key).ToList();
        var escalation = Catalog.Where(f => f.Tier == 1 && AssetsExistFor(f.Key)).Select(f => f.Key).ToList();
        var picks = PickConscripts(round, _snapshotOn, starter, escalation, on, _rng);
        if (picks.Count == 0)
        {
            App.Logger?.Warning("Lockdown dose: round {Round} found nothing to conscript", round);
            return null;
        }

        _round = round;
        var names = new List<string>(picks.Count);
        foreach (var key in picks)
        {
            try
            {
                mw.SetWallFeature(key, true);
                _flipped.Add(key);
                names.Add(Catalog.First(f => f.Key == key).Name);
            }
            catch (Exception ex) { App.Logger?.Warning("Lockdown dose: could not switch {Key} on: {Error}", key, ex.Message); }
        }
        WriteRecoveryFile();
        App.Logger?.Information("Lockdown dose: round {Round} conscripted {Keys}", round, string.Join(",", picks));

        // Possession, if it is haunting, gets to own the moment: the edge pulse is the grammar for
        // "the room did that" (POSSESSION.md "clarity in front"). Silent when Possession is off.
        try { App.Possession?.PulseEdges(0.8); } catch { }
        return names.Count == 0 ? null : JoinNames(names);
    }

    private static string JoinNames(List<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        2 => names[0] + " and " + names[1],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    private void StartEngine(MainWindow mw)
    {
        try
        {
            // systemInitiated: the keeper is not the user pressing START. Without it, Stop-inside-a-
            // lockdown farmed the Relapse achievement, TotalSessions grew once per engine-idle grace
            // and the mandatory-video enhancement prompt could pop inside the lockdown.
            mw.StartEngine(systemInitiated: true);
            _weStartedEngine = true;
            WriteRecoveryFile();
            App.Logger?.Information("Lockdown dose: engine started for the user");
        }
        catch (Exception ex) { App.Logger?.Warning("Lockdown dose: StartEngine failed: {Error}", ex.Message); }
    }

    private void Bark(string? names, bool engineStarted)
    {
        try { App.Bark?.NotifyLockdownConscript(names ?? "", _round, engineStarted); } catch { }
    }

    /// <summary>The extensions VideoService actually plays, mirrored from its refill scan
    /// (Services/Video/VideoService.cs, <c>RefillVideoQueues</c>). Counting ANY file made round 2
    /// conscript Mandatory Videos over a folder holding nothing but Thumbs.db, desktop.ini or the
    /// .ccpenh.json enhancement sidecars - a feature switched on with nothing to play.</summary>
    private static readonly string[] VideoExtensions = { ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm" };

    /// <summary>Escalation features only join the pool when they would actually do something.</summary>
    private static bool AssetsExistFor(string key)
    {
        try
        {
            if (key == "video")
            {
                var dir = Path.Combine(App.EffectiveAssetsPath, "videos");
                if (!Directory.Exists(dir)) return false;
                // AllDirectories to match VideoService: users organise videos into category subfolders.
                return Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                                .Any(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            }
        }
        catch { }
        return true;
    }

    /// <summary>Test seam: does this filename count as something the video feature could play?</summary>
    public static bool IsPlayableVideoFile(string path)
    {
        try { return !string.IsNullOrWhiteSpace(path) && VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()); }
        catch { return false; }
    }

    private void OnDeactivated()
    {
        try
        {
            _kickoff?.Stop();
            if (!_armed) { DeleteRecoveryFile(); return; }   // nothing was ever borrowed this lockdown
            _armed = false;
            // Restore OWNS the recovery file from here. It is the record of what is still switched on
            // against the user's wishes, so it may only be deleted once the toggles are actually back -
            // and Restore can be QUEUED to the UI thread, so deleting it here (the old `finally`) threw
            // the record away before the restore had even run.
            Restore();
        }
        catch (Exception ex) { App.Logger?.Warning("Lockdown dose: restore failed: {Error}", ex.Message); }
    }

    /// <summary>Gives back what was borrowed: flipped toggles to their pre-lockdown value, and the
    /// engine stopped if the keeper was the one who started it and it was not running before. Deletes
    /// the recovery file only when everything was actually given back; anything left flipped stays on
    /// disk so <see cref="RecoverIfNeeded"/> finishes the job at the next launch.</summary>
    private void Restore()
    {
        // Marshal FIRST. MainWindow's Application.Current.MainWindow getter VerifyAccess-throws off the
        // UI thread, so reading it before this check made the marshalling branch unreachable - the read
        // threw and the whole restore was lost to OnDeactivated's catch.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            if (dispatcher.HasShutdownStarted) return;   // recovery file kept; next launch cleans up
            dispatcher.BeginInvoke(new Action(Restore));
            return;
        }

        var mw = App.MainWindowRef ?? Application.Current?.MainWindow as MainWindow;
        if (mw == null)
        {
            App.Logger?.Warning("Lockdown dose: no main window at restore - {Count} toggle(s) left on, "
                + "recovery file kept for the next launch", _flipped.Count);
            return;
        }

        var gaveBack = new List<string>();
        var stillFlipped = new List<string>();
        foreach (var key in _flipped.ToList())
        {
            if (_snapshotOn.Contains(key)) continue;   // they had it on themselves - leave it
            try { mw.SetWallFeature(key, false); gaveBack.Add(key); }
            catch (Exception ex)
            {
                stillFlipped.Add(key);
                App.Logger?.Warning("Lockdown dose: could not give back {Key}: {Error}", key, ex.Message);
            }
        }
        _flipped.Clear();
        foreach (var key in stillFlipped) _flipped.Add(key);

        var stoppedEngine = false;
        if (_weStartedEngine && !_engineWasRunning && App.IsEngineRunning && !mw.IsSessionFeatureLockActive)
        {
            try { mw.StopEngine(); stoppedEngine = true; }
            catch (Exception ex) { App.Logger?.Warning("Lockdown dose: StopEngine failed: {Error}", ex.Message); }
        }
        App.Logger?.Information("Lockdown dose: released (gave back {Keys}; engine {Engine})",
            gaveBack.Count == 0 ? "nothing" : string.Join(",", gaveBack), stoppedEngine ? "stopped" : "left as is");

        // Only now, and only if nothing is still flipped: the file is the recovery record.
        if (stillFlipped.Count == 0) DeleteRecoveryFile();
        else WriteRecoveryFile();
    }

    // ---------------------------------------------------------------------------------------------
    //  Crash recovery - flipped keys survive a kill so they can be switched back off next launch
    // ---------------------------------------------------------------------------------------------

    private sealed class RecoveryState
    {
        public List<string> Flipped { get; set; } = new();
    }

    private static string RecoveryPath => Path.Combine(App.UserDataPath, "lockdown-dose.json");

    private void WriteRecoveryFile()
    {
        try
        {
            var toRestore = _flipped.Where(k => !_snapshotOn.Contains(k)).ToList();
            if (toRestore.Count == 0) { DeleteRecoveryFile(); return; }
            File.WriteAllText(RecoveryPath, JsonConvert.SerializeObject(new RecoveryState { Flipped = toRestore }));
        }
        catch (Exception ex) { App.Logger?.Debug("Lockdown dose: recovery write failed: {Error}", ex.Message); }
    }

    private static void DeleteRecoveryFile()
    {
        try { if (File.Exists(RecoveryPath)) File.Delete(RecoveryPath); } catch { }
    }

    /// <summary>Startup: a lockdown that died mid-run leaves the conscripted toggles on disk. Switch
    /// them back off (the engine is never running at startup, so this is settings-only).</summary>
    public static void RecoverIfNeeded()
    {
        try
        {
            if (!File.Exists(RecoveryPath)) return;
            var state = JsonConvert.DeserializeObject<RecoveryState>(File.ReadAllText(RecoveryPath));
            var s = App.Settings?.Current;
            if (state?.Flipped != null && s != null)
            {
                foreach (var key in state.Flipped)
                {
                    switch (key)
                    {
                        case "flash": s.FlashEnabled = false; break;
                        case "subliminal": s.SubliminalEnabled = false; break;
                        case "spiral": s.SpiralEnabled = false; break;
                        case "pinkfilter": s.PinkFilterEnabled = false; break;
                        case "bouncingtext": s.BouncingTextEnabled = false; break;
                        case "bubbles": s.BubblesEnabled = false; break;
                        case "video": s.MandatoryVideosEnabled = false; break;
                    }
                }
                try { App.Settings?.SaveImmediate(); } catch { }
                App.Logger?.Information("Lockdown dose recovery: switched {Keys} back off after an interrupted lockdown",
                    string.Join(",", state.Flipped));
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Lockdown dose recovery failed: {Error}", ex.Message); }
        finally { DeleteRecoveryFile(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _kickoff?.Stop(); } catch { }
        if (_installed)
        {
            _lockdown.LockdownActivated -= OnActivated;
            _lockdown.LockdownDeactivated -= OnDeactivated;
            _lockdown.CountdownTick -= OnTick;
        }
    }
}
