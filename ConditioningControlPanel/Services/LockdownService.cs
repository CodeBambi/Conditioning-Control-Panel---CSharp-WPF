using System;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Manages lockdown mode — a timed state that forces strict lock ON, panic key OFF,
/// and blocks all escape mechanisms. State is ephemeral (not persisted).
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

    public event Action? LockdownActivated;
    public event Action? LockdownDeactivated;
    public event Action<TimeSpan>? CountdownTick;

    public bool IsActive => _isActive;

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

        // Force lockdown settings — do NOT call Save() so these are never persisted
        settings.StrictLockEnabled = true;
        settings.PanicKeyEnabled = false;

        _duration = duration;
        _activatedAt = DateTime.Now;
        _isActive = true;

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

        // Restore saved settings (without Save — forced values were never persisted)
        var settings = App.Settings?.Current;
        if (settings != null)
        {
            settings.StrictLockEnabled = _preStrictLock;
            settings.PanicKeyEnabled = _prePanicKeyEnabled;
        }

        _isActive = false;

        App.Logger?.Information("Lockdown deactivated");
        LockdownDeactivated?.Invoke();
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
