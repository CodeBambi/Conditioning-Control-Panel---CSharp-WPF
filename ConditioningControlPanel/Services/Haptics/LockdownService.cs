using System;
using System.IO;
using System.Windows.Threading;
using System.Collections.Generic;
using Newtonsoft.Json;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Manages lockdown mode — a timed state that forces strict lock ON, panic key OFF,
/// and blocks all escape mechanisms. State is ephemeral (not persisted to settings.json),
/// but the pre-lockdown values are written to a tiny recovery file. If anything calls
/// settings.Save() while lockdown is active (which is common — many code paths do), the
/// false PanicKeyEnabled would otherwise stick on disk and survive the lockdown window
/// and a reboot, leaving the panic key permanently broken (#162). On next start, the
/// recovery file lets us restore the user's real values.
/// </summary>
public class LockdownService : IDisposable
{
    private bool _isActive;
    private DateTime _activatedAt;
    private TimeSpan _duration;
    private DispatcherTimer? _countdownTimer;
    private bool _preStrictLock;
    private bool _prePanicKeyEnabled;
    private bool _isDisposed;

    private static string RecoveryFilePath =>
        Path.Combine(App.UserDataPath, "lockdown_recovery.json");

    private sealed class RecoveryState
    {
        public bool StrictLockEnabled { get; set; }
        public bool PanicKeyEnabled { get; set; }
    }

    public event Action? LockdownActivated;
    public event Action? LockdownDeactivated;
    public event Action<TimeSpan>? CountdownTick;

    /// <summary>
    /// Tripwire: something tried to leave / interrupt while lockdown was active (Alt+F4, the X,
    /// minimize, a suppressed system key, Stop, a wrong secret phrase...). The Possession layer
    /// reacts; Lockdown itself only counts. See Services/Possession/POSSESSION.md - Tripwires.
    /// </summary>
    public event Action<EscapeAttempt>? EscapeAttempted;

    private readonly Dictionary<string, int> _escapeRepeats = new(StringComparer.OrdinalIgnoreCase);
    private int _escapeTotal;
    private DateTime _lastSysKeyAttempt = DateTime.MinValue;

    /// <summary>Total duration of the running lockdown (TimeSpan.Zero when inactive).</summary>
    public TimeSpan Duration => _isActive ? _duration : TimeSpan.Zero;

    /// <summary>0..1 fraction of the lockdown already elapsed (0 when inactive). Drives the Possession ladder.</summary>
    public double ElapsedFraction
    {
        get
        {
            if (!_isActive || _duration <= TimeSpan.Zero) return 0;
            var f = (DateTime.Now - _activatedAt).TotalSeconds / _duration.TotalSeconds;
            return f < 0 ? 0 : f > 1 ? 1 : f;
        }
    }

    /// <summary>
    /// Call from every interception point (window close, minimize, suppressed system key, Stop,
    /// wrong phrase, greyed safety toggle). No-op when lockdown is inactive or tripwires are off.
    /// System-key attempts are throttled to one per 2 s so a held key cannot spam the scare.
    /// Kinds: <see cref="EscapeKinds"/>. Raises <see cref="EscapeAttempted"/> on the UI thread.
    /// </summary>
    public void NotifyEscapeAttempt(string kind)
    {
        if (!_isActive || string.IsNullOrWhiteSpace(kind)) return;
        if (App.Settings?.Current?.LockdownTripwiresEnabled == false) return;
        var now = DateTime.Now;
        if (string.Equals(kind, EscapeKinds.SystemKey, StringComparison.OrdinalIgnoreCase))
        {
            if ((now - _lastSysKeyAttempt).TotalSeconds < 2) return;
            _lastSysKeyAttempt = now;
        }
        _escapeRepeats.TryGetValue(kind, out var rep);
        rep++;
        _escapeRepeats[kind] = rep;
        _escapeTotal++;
        var attempt = new EscapeAttempt(kind, rep, _escapeTotal, now);
        App.Logger?.Debug("Lockdown tripwire: {Kind} x{Repeat} (total {Total})", kind, rep, _escapeTotal);
        Helpers.DispatcherHelper.RunOnUI(() =>
        {
            try { EscapeAttempted?.Invoke(attempt); }
            catch (Exception ex) { App.Logger?.Warning("Lockdown tripwire handler failed: {Error}", ex.Message); }
        });
    }

    public bool IsActive => _isActive;

    /// <summary>
    /// How long the most recently ended lockdown stayed active (set on Deactivate).
    /// This is the single source of truth for "how long did the user sit through a
    /// lockdown" — gamification reads this instead of tracking its own start time, so
    /// the value can't desync from the service. TimeSpan.Zero before the first lockdown.
    /// </summary>
    public TimeSpan LastActiveDuration { get; private set; }

    public TimeSpan Remaining
    {
        get
        {
            if (!_isActive) return TimeSpan.Zero;
            var elapsed = DateTime.Now - _activatedAt;
            var remaining = _duration - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public void Activate(TimeSpan duration)
    {
        if (_isActive) return;

        var settings = App.Settings?.Current;
        if (settings == null) return;

        // Save current settings (so we can restore on deactivate)
        _preStrictLock = settings.StrictLockEnabled;
        _prePanicKeyEnabled = settings.PanicKeyEnabled;

        // Persist pre-lockdown values to a recovery file BEFORE overriding. If the app
        // crashes / is killed mid-lockdown, App.OnStartup -> RecoverIfNeeded() restores
        // these so the panic key isn't stuck off forever.
        WriteRecoveryFile(_preStrictLock, _prePanicKeyEnabled);

        // Force lockdown settings — do NOT call Save() so these are never persisted. Since the
        // Possession rework these are the default-on "Safeties" toggles on the Lockdown card; a
        // toggle the user switched off leaves the real value alone (and restore is then a no-op).
        if (settings.LockdownForceStrictLock) settings.StrictLockEnabled = true;
        if (settings.LockdownDisablePanicKey) settings.PanicKeyEnabled = false;
        // Move the keyboard hook and the Settings ▸ Devices checkbox with the flag. Without this
        // the hook stays armed (the panic key still works during lockdown) and the stale checkbox
        // is what SaveSettings used to read back on START, undoing the lockdown.
        SyncPanicKeyUi();

        _duration = duration;
        _activatedAt = DateTime.Now;
        _isActive = true;
        _escapeRepeats.Clear();
        _escapeTotal = 0;

        // Start countdown timer (ticks every second)
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();

        App.Logger?.Information("Lockdown activated for {Duration} minutes", duration.TotalMinutes);
        LockdownActivated?.Invoke();
    }

    public void Deactivate()
    {
        if (!_isActive) return;

        // Stop timer
        if (_countdownTimer != null)
        {
            _countdownTimer.Stop();
            _countdownTimer.Tick -= OnCountdownTick;
            _countdownTimer = null;
        }

        // Restore saved settings. Some other code path may have already called
        // settings.Save() while lockdown was active (persisting the false PanicKeyEnabled),
        // so we explicitly Save here to overwrite that on disk with the real values.
        var settings = App.Settings?.Current;
        if (settings != null)
        {
            settings.StrictLockEnabled = _preStrictLock;
            settings.PanicKeyEnabled = _prePanicKeyEnabled;
            try { App.Settings?.SaveImmediate(); } catch { /* best-effort */ }
            SyncPanicKeyUi();
        }

        DeleteRecoveryFile();

        // Capture how long this lockdown ran BEFORE clearing _isActive, so gamification
        // (throw_away_the_key, "60+ minute lockdown") reads an authoritative duration
        // straight from the service rather than maintaining its own start timestamp.
        LastActiveDuration = DateTime.Now - _activatedAt;
        _isActive = false;

        App.Logger?.Information("Lockdown deactivated after {Minutes:F1} minutes", LastActiveDuration.TotalMinutes);
        LockdownDeactivated?.Invoke();
    }

    /// <summary>
    /// Called once at app startup. If the recovery file exists, the previous run was
    /// killed/crashed mid-lockdown — restore the user's real PanicKeyEnabled / StrictLock
    /// values so the panic key isn't permanently stuck off.
    /// </summary>
    public static void RecoverIfNeeded()
    {
        try
        {
            if (!File.Exists(RecoveryFilePath)) return;

            var json = File.ReadAllText(RecoveryFilePath);
            var state = JsonConvert.DeserializeObject<RecoveryState>(json);
            if (state != null && App.Settings?.Current != null)
            {
                App.Settings.Current.StrictLockEnabled = state.StrictLockEnabled;
                App.Settings.Current.PanicKeyEnabled = state.PanicKeyEnabled;
                App.Settings.SaveImmediate();
                SyncPanicKeyUi();
                App.Logger?.Information(
                    "Lockdown recovery: restored PanicKeyEnabled={Panic}, StrictLockEnabled={Strict} from prior interrupted lockdown",
                    state.PanicKeyEnabled, state.StrictLockEnabled);
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Lockdown recovery failed: {Error}", ex.Message);
        }
        finally
        {
            DeleteRecoveryFile();
        }
    }

    /// <summary>
    /// Pushes a PanicKeyEnabled change made from here into the UI layer: the global keyboard hook
    /// and the Settings ▸ Devices checkbox. MainWindow.SyncNoPanicState touches WPF controls, so it
    /// must run on the UI thread; it is a no-op before MainWindow exists (startup recovery).
    /// </summary>
    private static void SyncPanicKeyUi()
    {
        Helpers.DispatcherHelper.RunOnUI(() =>
        {
            try { App.MainWindowRef?.SyncNoPanicState(); }
            catch (Exception ex) { App.Logger?.Warning("Lockdown panic-key UI sync failed: {Error}", ex.Message); }
        });
    }

    private static void WriteRecoveryFile(bool strictLock, bool panicKey)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecoveryFilePath)!);
            var json = JsonConvert.SerializeObject(new RecoveryState
            {
                StrictLockEnabled = strictLock,
                PanicKeyEnabled = panicKey
            });
            File.WriteAllText(RecoveryFilePath, json);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Lockdown: failed to write recovery file: {Error}", ex.Message);
        }
    }

    private static void DeleteRecoveryFile()
    {
        try { if (File.Exists(RecoveryFilePath)) File.Delete(RecoveryFilePath); }
        catch { }
    }

    /// <summary>
    /// Secret exit mechanism. Returns true if phrase matches and lockdown was deactivated.
    /// </summary>
    public bool TryExitWithPhrase(string phrase)
    {
        if (!_isActive) return false;

        if (string.Equals(phrase?.Trim(), "let me out", StringComparison.OrdinalIgnoreCase))
        {
            App.Logger?.Information("Lockdown deactivated via secret exit phrase");
            Deactivate();
            return true;
        }

        return false;
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        var remaining = Remaining;
        CountdownTick?.Invoke(remaining);

        if (remaining <= TimeSpan.Zero)
        {
            Deactivate();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_isActive)
        {
            Deactivate();
        }

        _countdownTimer?.Stop();
    }
}
