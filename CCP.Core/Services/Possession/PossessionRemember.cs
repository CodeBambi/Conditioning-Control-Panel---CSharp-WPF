// PORTED from ConditioningControlPanel/Services/Possession/PossessionRemember.cs (7.1.5), head-neutral.
// "It remembers": a Full Doki lockdown that ends arms one charge; on the NEXT launch, twenty seconds
// after the window is up and only while no lockdown runs, the room pulses once and the warden says so.
// Once per launch, spent whether or not anything could be shown. It never starts a haunt.
//
// The WPF file charges an ember ripple on the Lockdown door and falls back to a spoken-bubble line when
// no bark pack answers; this head has neither the ripple nor that bubble road, so the charge is the
// host's edge pulse and the line is the bark only.

using System;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Services.Possession;

public sealed class PossessionRemember : IDisposable
{
    /// <summary>The head's instance (WPF: a static class installed from App).</summary>
    public static PossessionRemember? Current { get; set; }

    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan WaitForWindowTimeout = TimeSpan.FromMinutes(3);
    /// <summary>Softer than any tripwire: a memory, not an event.</summary>
    public const double ChargeStrength = 0.3;

    private readonly LockdownService _lockdown;
    private readonly PossessionHost _host;
    private int _escapeAttempts;
    private bool _waiting;
    private bool _spent;
    private DateTime _startedAt;
    private DateTime _windowReadyAt = DateTime.MinValue;

    public PossessionRemember(LockdownService lockdown, PossessionHost host)
    {
        _lockdown = lockdown ?? throw new ArgumentNullException(nameof(lockdown));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _lockdown.LockdownActivated += OnLockdownActivated;
        _lockdown.LockdownDeactivated += OnLockdownDeactivated;
        _lockdown.EscapeAttempted += OnEscapeAttempted;
    }

    /// <summary>Escape attempts in the running lockdown (the companion's prompt reads it).</summary>
    public int EscapeAttempts => Volatile.Read(ref _escapeAttempts);
    /// <summary>True while a remembered charge waits for the window (the head ticks once a second).</summary>
    public bool IsWaiting => _waiting && !_spent;
    public bool Spent => _spent;

    private void OnLockdownActivated() => Interlocked.Exchange(ref _escapeAttempts, 0);
    private void OnEscapeAttempted(EscapeAttempt attempt) => Volatile.Write(ref _escapeAttempts, attempt.Total);

    private void OnLockdownDeactivated()
    {
        try
        {
            Interlocked.Exchange(ref _escapeAttempts, 0);
            var s = CoreSettings.Current;
            if (s == null) return;
            if (s.LockdownPossessionIntensity != (int)PossessionIntensity.FullDoki) return;
            if (!s.LockdownPossessionEnabled) return;
            s.LockdownPossessionRememberPending = true;
            Log.Debug("PossessionRemember armed (Full Doki lockdown ended)");
        }
        catch (Exception ex) { Log.Warning("PossessionRemember arm failed: {Error}", ex.Message); }
    }

    /// <summary>Startup, once: take the pending charge off the settings and start waiting for the
    /// window. The flag is cleared even when Possession has been switched off since.</summary>
    public void SchedulePendingCharge(DateTime utcNow)
    {
        var s = CoreSettings.Current;
        if (s == null || !s.LockdownPossessionRememberPending) return;
        s.LockdownPossessionRememberPending = false;
        if (!s.LockdownPossessionEnabled) return;
        _startedAt = utcNow;
        _windowReadyAt = DateTime.MinValue;
        _waiting = true;
    }

    /// <summary>One beat of the wait. The window has to stay usable for the whole delay.</summary>
    public void Tick(DateTime utcNow)
    {
        if (!IsWaiting) return;
        try
        {
            if (utcNow - _startedAt > WaitForWindowTimeout) { _waiting = false; return; }
            bool ready;
            try { ready = _host.IsUsable(); } catch { ready = false; }
            if (!ready) { _windowReadyAt = DateTime.MinValue; return; }
            if (_windowReadyAt == DateTime.MinValue) _windowReadyAt = utcNow;
            if (utcNow - _windowReadyAt < Delay) return;
            _waiting = false;
            _spent = true;
            if (_lockdown.IsActive) return;   // a lockdown is running: the haunt speaks for itself
            Log.Information("Possession: spending the remembered charge");
            _host.OnUi(() =>
            {
                try { _host.EdgePulse(ChargeStrength); } catch (Exception ex) { Diag.Swallowed(ex); }
                try { CoreBark.Raise(PossessionBarkTriggers.Remember, null); } catch (Exception ex) { Diag.Swallowed(ex); }
            });
        }
        catch (Exception ex)
        {
            Log.Warning("PossessionRemember tick failed: {Error}", ex.Message);
            _waiting = false;
        }
    }

    public void Dispose()
    {
        _waiting = false;
        _lockdown.LockdownActivated -= OnLockdownActivated;
        _lockdown.LockdownDeactivated -= OnLockdownDeactivated;
        _lockdown.EscapeAttempted -= OnEscapeAttempted;
    }
}
