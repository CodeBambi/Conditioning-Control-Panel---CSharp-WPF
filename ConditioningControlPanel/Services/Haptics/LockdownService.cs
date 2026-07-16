using System;
using System.IO;
using System.Windows.Threading;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Manages "Dark Patterns" mode — a timed state that ratchets in-app escape friction to an
/// absurd, satirical degree instead of actually locking the user in. Nothing about the OS is
/// blocked: Alt+F4, the Windows key and Task Manager all still work, the panic key still fires
/// (through a deliberately obnoxious confirm chain), the "let me out" phrase still exits, and the
/// real countdown always expires on schedule. Every friction effect it drives (inverted window
/// chrome, tiny/fleeing buttons, tiny-X flashes, the anti-panic chain) is applied in-memory, so a
/// crash or force-kill naturally resets everything on relaunch — there is nothing dangerous to
/// persist. (Historically this was a hard lockout that forced StrictLock on / PanicKey off and
/// suppressed system keys; that behaviour and its settings-recovery machinery were removed when
/// the concept pivoted from lockout to friction.)
/// </summary>
public class LockdownService : IDisposable
{
    private bool _isActive;
    private DateTime _activatedAt;
    private TimeSpan _duration;
    private DispatcherTimer? _countdownTimer;
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

        // Dark Patterns is pure in-app friction: it does NOT force StrictLock / disable the panic
        // key / suppress system keys the way the old hard-lockout did. So there are no dangerous
        // settings to snapshot or recover — activation just starts the timer and lets the UI layer
        // (MainWindow.Lab / FlashService / the anti-panic chain) apply its friction effects.

        _duration = duration;
        _activatedAt = DateTime.Now;
        _isActive = true;

        // Start countdown timer (ticks every second)
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();

        App.Logger?.Information("Dark Patterns activated for {Duration} minutes", duration.TotalMinutes);
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

        // Nothing to restore on disk — Dark Patterns never mutated any persisted setting.

        // Capture how long this lockdown ran BEFORE clearing _isActive, so gamification
        // (throw_away_the_key, "60+ minute lockdown") reads an authoritative duration
        // straight from the service rather than maintaining its own start timestamp.
        LastActiveDuration = DateTime.Now - _activatedAt;
        _isActive = false;

        App.Logger?.Information("Dark Patterns deactivated after {Minutes:F1} minutes", LastActiveDuration.TotalMinutes);
        LockdownDeactivated?.Invoke();
    }

    /// <summary>
    /// Called once at app startup. Legacy safety net: older builds of this mode were a hard lockout
    /// that force-disabled the panic key and wrote a recovery file so a mid-lockdown crash couldn't
    /// leave the panic key stuck off (#162). Dark Patterns no longer writes that file, but we still
    /// honour any stale one left by an upgrade-in-place so an interrupted old lockdown can't strand a
    /// disabled panic key. Then it self-deletes.
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
