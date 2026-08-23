using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  "IT REMEMBERS" - the haunt's one line of memory. Read Services/Possession/POSSESSION.md first.
//
//  Wave 2, item C2. Everything else Possession does is bounded by the lockdown timer: the room misbehaves,
//  the timer ends, the room reassembles, and the whole thing is over. That containment is what makes the
//  feature safe, and it is also what makes it forgettable. So exactly ONE thing survives the timer, and
//  only from a Full Doki run: about twenty seconds into the NEXT launch, the Lockdown door takes a single
//  ember charge and the companion says one line. Nothing moves, nothing is haunted, no effect starts. It
//  is a look, not a haunt.
//
//  RULES that keep it from becoming a haunt outside a lockdown:
//    - Full Doki only, read at DEACTIVATE time (the intensity dial may be turned down before next launch;
//      what matters is the intensity of the run the user actually lived through).
//    - Once. The flag is cleared the moment this service starts, BEFORE anything is scheduled, so a crash,
//      a kill or a second instance can never leave it stuck on and charging every launch forever.
//    - Never while a lockdown is running (crash recovery can hand us one), never without a tube, never
//      without a visible door to charge.
//
//  This file also owns the per-lockdown escape counter, because it is the one thing that already lives
//  across the whole lockdown lifecycle and nothing else exposes it: LockdownService counts escapes into a
//  private field and publishes the total only on the EscapeAttempted argument. The companion's chat prompt
//  (PromptAssembler, wave-2 item B12) reads it from here so the warden knows how many times they pulled at
//  the door.
// =====================================================================================================

/// <summary>The "it remembers" charge and the per-lockdown escape counter. See the file header.</summary>
public static class PossessionRemember
{
    /// <summary>Bark trigger for the remembered line. One text-only variant per mod pack.</summary>
    public const string RememberTrigger = "PossessionRemember";

    /// <summary>How long after the main window is up the charge lands.</summary>
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(20);

    /// <summary>Give up if the main window never shows (a headless/preview run, an early crash).</summary>
    private static readonly TimeSpan WaitForWindowTimeout = TimeSpan.FromMinutes(3);

    private static bool _installed;
    private static bool _spent;
    private static int _escapeAttempts;
    private static DispatcherTimer? _timer;
    private static DateTime _startedAt;
    private static DateTime _windowReadyAt = DateTime.MinValue;

    /// <summary>
    /// How many escape attempts the CURRENT lockdown has seen, all kinds combined. Zero when no lockdown
    /// is running. Read by the companion prompt so the warden can tease them about it.
    /// </summary>
    public static int EscapeAttempts => Volatile.Read(ref _escapeAttempts);

    /// <summary>
    /// Wire up the lockdown lifecycle and, if the last Full Doki run armed it, schedule this launch's one
    /// charge. Called once from App.OnStartup next to the director. Safe to call twice.
    /// </summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;

        try
        {
            var lockdown = App.Lockdown;
            if (lockdown != null)
            {
                lockdown.LockdownActivated += OnLockdownActivated;
                lockdown.LockdownDeactivated += OnLockdownDeactivated;
                lockdown.EscapeAttempted += OnEscapeAttempted;
            }
        }
        catch (Exception ex) { App.Logger?.Warning("PossessionRemember install failed: {Error}", ex.Message); }

        try { SchedulePendingCharge(); }
        catch (Exception ex) { App.Logger?.Warning("PossessionRemember schedule failed: {Error}", ex.Message); }
    }

    // -------------------------------------------------------------------------------------------
    //  Lockdown lifecycle
    // -------------------------------------------------------------------------------------------

    private static void OnLockdownActivated() => Interlocked.Exchange(ref _escapeAttempts, 0);

    private static void OnEscapeAttempted(EscapeAttempt attempt)
    {
        // Total, not Repeat: the prompt wants "they have tried K times", across every kind.
        Volatile.Write(ref _escapeAttempts, attempt.Total);
    }

    private static void OnLockdownDeactivated()
    {
        try
        {
            Interlocked.Exchange(ref _escapeAttempts, 0);

            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Read the intensity HERE, at the end of the run that earned it. Reading it next launch
            // instead would let a user who dialled back to Eerie in the meantime get a memory of a
            // run they never had - and, worse, hide one they did.
            if (settings.LockdownPossessionIntensity != (int)PossessionIntensity.FullDoki) return;
            if (settings.LockdownPossessionEnabled != true) return;

            settings.LockdownPossessionRememberPending = true;
            App.Logger?.Debug("PossessionRemember armed (Full Doki lockdown ended)");
        }
        catch (Exception ex) { App.Logger?.Warning("PossessionRemember arm failed: {Error}", ex.Message); }
    }

    // -------------------------------------------------------------------------------------------
    //  The one charge
    // -------------------------------------------------------------------------------------------

    private static void SchedulePendingCharge()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;
        if (!settings.LockdownPossessionRememberPending) return;

        // FAILSAFE, and the reason it is the very first thing that happens: the flag is spent the moment
        // we read it, not when the charge lands. Everything below can fail, be cancelled, or never get a
        // window - and the next launch still starts clean.
        settings.LockdownPossessionRememberPending = false;

        if (settings.LockdownPossessionEnabled != true) return;

        _startedAt = DateTime.UtcNow;
        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                _timer?.Stop();
                _timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _timer.Tick += (_, __) => Tick();
                _timer.Start();
            }
            catch (Exception ex) { App.Logger?.Warning("PossessionRemember timer failed: {Error}", ex.Message); }
        });
    }

    private static void Tick()
    {
        try
        {
            if (_spent) { Stop(); return; }
            if (DateTime.UtcNow - _startedAt > WaitForWindowTimeout) { Stop(); return; }

            var window = App.MainWindowRef;
            if (window == null || !window.IsLoaded || !window.IsVisible)
            {
                // The clock only starts once the room is actually on screen: twenty seconds of a
                // splash screen is not twenty seconds of someone sitting there.
                _windowReadyAt = DateTime.MinValue;
                return;
            }

            if (_windowReadyAt == DateTime.MinValue) _windowReadyAt = DateTime.UtcNow;
            if (DateTime.UtcNow - _windowReadyAt < Delay) return;

            Stop();
            if (_spent) return;
            _spent = true;
            Fire(window);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("PossessionRemember tick failed: {Error}", ex.Message);
            Stop();
        }
    }

    private static void Stop()
    {
        try { _timer?.Stop(); } catch { }
        _timer = null;
    }

    private static void Fire(MainWindow window)
    {
        try
        {
            // A lockdown that started while we were counting owns the room now; a memory of the LAST one
            // on top of a live haunt is just noise.
            if (App.Lockdown?.IsActive == true) return;

            // No tube, no line, no point: the charge alone reads as a glitch rather than a message.
            if (App.AvatarWindow == null) return;

            var door = ResolveDoor(window);
            if (door == null) return;

            App.Logger?.Information("Possession: spending the remembered charge on {Door}", door.Name);

            // The attribution layer, borrowed for one ripple. The director owns an EmberAttribution per
            // attached host, but it only builds them while it is haunting and it never exposes them, so
            // this makes its own over the same host and drops it. ChargeAsync tears its own overlay down
            // (see EmberAttribution.StartCharge / Finish), so there is nothing to release afterwards.
            if (window is IPossessionHost host)
            {
                var attribution = new EmberAttribution(host, () => App.Settings?.Current?.LockdownPhotosafe == true);
                _ = attribution.ChargeAsync(door, CancellationToken.None, 600);
            }

            SayRemembered();
        }
        catch (Exception ex) { App.Logger?.Warning("PossessionRemember fire failed: {Error}", ex.Message); }
    }

    /// <summary>
    /// The most Lockdown-ish thing currently on screen. The nav rail's Lockdown entry when its door is
    /// open, otherwise the Play door that contains it - charging a control inside a collapsed accordion
    /// (Height 0, IsHitTestVisible false) would produce an ember ripple nobody can see.
    /// </summary>
    private static FrameworkElement? ResolveDoor(MainWindow window)
    {
        foreach (var name in new[] { "BtnNavLockdown", "DoorPlay" })
        {
            try
            {
                if (window.FindName(name) is FrameworkElement fe
                    && fe.IsVisible && fe.ActualWidth > 1 && fe.ActualHeight > 1)
                {
                    return fe;
                }
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// One line, in the mod's own voice. The packs own the words (trigger
    /// <see cref="RememberTrigger"/>, text-only variants); the hardcoded line below exists only for the
    /// case where there is no bark service at all, exactly like Warden.Say's fallback.
    ///
    /// <para>Goes through BarkService.NotifyPossessionRemember (rule poss_remember in every pack);
    /// the bubble fallback only covers a mod pack without that rule.</para>
    /// </summary>
    private static void SayRemembered()
    {
        try
        {
            if (App.Bark != null && App.Bark.NotifyPossessionRemember()) return;

            App.AvatarWindow?.GigglePriority("i remember.", playSound: false,
                aiGenerated: false, mood: "possessive");
        }
        catch (Exception ex) { App.Logger?.Debug("PossessionRemember line failed: {Error}", ex.Message); }
    }
}
