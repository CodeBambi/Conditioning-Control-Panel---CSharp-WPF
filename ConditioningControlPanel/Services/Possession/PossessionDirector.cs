using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  PossessionDirector - the state machine behind the haunt. Read Services/Possession/POSSESSION.md.
//
//  It owns exactly four things:
//    1. WHERE on the ladder we are (elapsed fraction of the lockdown timer -> rung, capped by intensity)
//    2. WHEN the next ghost starts (cadence per rung, concurrency cap, per-target cooldown)
//    3. WHAT the warden says and does (it names the big effects; it knocks, stares, leaves, returns)
//    4. HOW it all comes back (the reassembly exit, and a synchronous crash-safe UndoAll)
//
//  All of the RULES live in PossessionDeck (pure, unit-tested). This file is the plumbing: dispatcher
//  safety, cancellation, live-effect bookkeeping and the event wiring to LockdownService. Nothing here
//  may throw into a WPF event handler, and nothing may block the UI thread - a wedged director would
//  freeze the app for the whole lockdown, which is exactly the bug the theme is imitating.
// =====================================================================================================
public sealed class PossessionDirector : IDisposable
{
    private readonly Services.LockdownService _lockdown;
    private readonly List<IPossessionEffect> _effects;
    private readonly List<HostEntry> _hosts = new();
    private readonly List<LiveGhost> _live = new();
    private readonly Random _rng = new();

    // Cooldowns and live flags are mirrored by KEY as well as on the PossessionTarget itself: a host is
    // free to rebuild its Targets list between picks (collapsed tabs come and go), and the ledger must
    // survive that - otherwise a rebuilt list hands us a "fresh" victim we possessed ten seconds ago.
    private readonly Dictionary<string, DateTime> _cooldowns = new(StringComparer.Ordinal);
    private readonly HashSet<string> _liveKeys = new(StringComparer.Ordinal);
    private readonly HashSet<PossessionRung> _barkedRungs = new();

    private PossessionIntensity _intensity = PossessionIntensity.Eerie;
    private bool _photosafe;
    private bool _tripwiresEnabled = true;
    private bool _wardenEnabled = true;

    private DateTime _nextDue = DateTime.MaxValue;
    private string? _lastTargetKey;
    private DateTime _lastTripwireAt = DateTime.MinValue;
    private DateTime _lastStareAt = DateTime.MinValue;
    private bool _picking;
    private bool _disposed;

    private static readonly TimeSpan TripwireThrottle = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan StareCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan WardenVerbTimeout = TimeSpan.FromSeconds(15);
    private const string FallEffectId = "fall";

    public PossessionDirector(Services.LockdownService lockdown, IEnumerable<IPossessionEffect>? effects)
    {
        _lockdown = lockdown ?? throw new ArgumentNullException(nameof(lockdown));
        _effects = (effects ?? Enumerable.Empty<IPossessionEffect>()).Where(e => e != null).ToList();

        _lockdown.LockdownActivated += OnLockdownActivated;
        _lockdown.LockdownDeactivated += OnLockdownDeactivated;
        _lockdown.CountdownTick += OnCountdownTick;
        _lockdown.EscapeAttempted += OnEscapeAttempted;
    }

    // ---------------------------------------------------------------------------------------------
    //  Public surface (see the bottom of PossessionContracts.cs)
    // ---------------------------------------------------------------------------------------------

    public PossessionRung CurrentRung { get; private set; } = PossessionRung.Settle;
    public event Action<PossessionRung>? RungChanged;
    public bool IsHaunting { get; private set; }
    public IPossessionWarden? Warden { get; set; }
    public int LiveEffectCount => _live.Count;

    public void AttachHost(IPossessionHost host)
    {
        if (host == null || _disposed) return;
        DispatcherHelper.RunOnUI(() =>
        {
            if (_hosts.Any(h => ReferenceEquals(h.Host, host))) return;
            _hosts.Add(new HostEntry(host, new EmberAttribution(host, () => App.Settings?.Current?.LockdownPhotosafe == true)));
            App.Logger?.Debug("Possession: host attached ({Host})", host.GetType().Name);
        });
    }

    public void DetachHost(IPossessionHost host)
    {
        if (host == null) return;
        DispatcherHelper.RunOnUI(() =>
        {
            var entry = _hosts.FirstOrDefault(h => ReferenceEquals(h.Host, host));
            if (entry == null) return;
            // Anything still riding this window has nowhere to be undone TO once it is gone, so take it
            // down now rather than leaking a ghost that outlives its room.
            foreach (var g in _live.Where(g => ReferenceEquals(g.Host.Host, host)).ToArray())
                UndoGhostSync(g);
            try { entry.Attribution.ReleaseAll(); } catch { }
            _hosts.Remove(entry);
        });
    }

    /// <summary>Synchronous, crash-safe reset: cancel everything, tell every live effect to undo with no
    /// animation, strip the ember layer. Used by Dispose and by any recovery path that just needs the UI
    /// back RIGHT NOW. Never awaits and never throws.</summary>
    public void UndoAll()
    {
        try
        {
            foreach (var g in _live.ToArray()) UndoGhostSync(g);
            _live.Clear();
            _liveKeys.Clear();
            foreach (var h in _hosts) { try { h.Attribution.ReleaseAll(); } catch { } }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession UndoAll failed: {Error}", ex.Message); }
    }

    // ---------------------------------------------------------------------------------------------
    //  Lockdown lifecycle
    // ---------------------------------------------------------------------------------------------

    private void OnLockdownActivated()
    {
        try
        {
            var s = App.Settings?.Current;
            if (s == null || !s.LockdownPossessionEnabled)
            {
                IsHaunting = false;
                return;
            }

            ReadLiveSettings();
            _tripwiresEnabled = s.LockdownTripwiresEnabled;
            _wardenEnabled = s.LockdownWardenEnabled;

            _barkedRungs.Clear();
            _cooldowns.Clear();
            _liveKeys.Clear();
            _lastTargetKey = null;
            _lastTripwireAt = DateTime.MinValue;
            _lastStareAt = DateTime.MinValue;
            CurrentRung = PossessionRung.Settle;
            IsHaunting = true;

            // Let the room settle before the first twitch (PossessionDeck.FirstWait).
            _nextDue = DateTime.Now + PossessionDeck.FirstDelay(CurrentRung, _intensity, _rng);
            App.Logger?.Information("Possession armed: intensity={Intensity}, first haunt in {Sec:F0}s, {Count} effects",
                _intensity, (_nextDue - DateTime.Now).TotalSeconds, _effects.Count);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession activate failed: {Error}", ex.Message); }
    }

    private void OnLockdownDeactivated()
    {
        if (!IsHaunting) { CurrentRung = PossessionRung.Settle; return; }
        IsHaunting = false;
        _nextDue = DateTime.MaxValue;
        FireAndForget(ReassembleAsync(), "reassembly");
    }

    private void OnCountdownTick(TimeSpan remaining)
    {
        if (!IsHaunting || _disposed) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;

        try
        {
            ReadLiveSettings();      // intensity / photosafe are live toggles, not activation snapshots
            EnsureHost();

            var frac = _lockdown.ElapsedFraction;
            var rung = PossessionDeck.RungFor(frac, _intensity);
            if (rung != CurrentRung)
            {
                var previous = CurrentRung;
                CurrentRung = rung;
                App.Logger?.Information("Possession rung {From} -> {To} at {Pct:P0}", previous, rung, frac);
                try { RungChanged?.Invoke(rung); } catch { }

                // The rung change itself is an event: the whole window flares once so the escalation is
                // never something the user only notices in hindsight.
                PulseAll(0.35 + 0.15 * (int)rung);
                if (_barkedRungs.Add(rung)) { try { App.Bark?.NotifyPossessionRung((int)rung); } catch { } }

                if (rung == PossessionRung.ItKnows && _wardenEnabled && Warden?.IsAvailable == true)
                    FireAndForget(RunWardenAsync(ct => Warden!.LeaveAsync(ct), "leave"), "warden leave");
            }

            if (_picking) return;
            if (DateTime.Now < _nextDue) return;
            if (_live.Count >= PossessionDeck.MaxLive(rung)) return;
            // Content is king: a video playing means the user is being conditioned, not haunted.
            if (App.Video is { IsPlaying: true }) return;

            var host = _hosts.FirstOrDefault(h => SafeIsUsable(h.Host));
            if (host == null) return;

            _picking = true;
            FireAndForget(StartOneAsync(host, rung, remaining, frac), "haunt");
        }
        catch (Exception ex)
        {
            _picking = false;
            App.Logger?.Warning("Possession tick failed: {Error}", ex.Message);
        }
    }

    private void ReadLiveSettings()
    {
        var s = App.Settings?.Current;
        if (s == null) return;
        _intensity = (PossessionIntensity)Math.Clamp(s.LockdownPossessionIntensity, 0, 2);
        _photosafe = s.LockdownPhotosafe;
    }

    /// <summary>Phase 1 haunts the main window. It may finish loading after the lockdown starts, so we
    /// keep looking for it on every tick instead of only at activation.</summary>
    private void EnsureHost()
    {
        if (_hosts.Count > 0) return;
        if (App.MainWindowRef is IPossessionHost host) AttachHost(host);
    }

    private static bool SafeIsUsable(IPossessionHost host)
    {
        try { return host.IsUsable; }
        catch { return false; }
    }

    // ---------------------------------------------------------------------------------------------
    //  Picking and running one haunt
    // ---------------------------------------------------------------------------------------------

    private async Task StartOneAsync(HostEntry host, PossessionRung rung, TimeSpan remaining, double frac)
    {
        IPossessionEffect? effect = null;
        PossessionTarget? target = null;
        CancellationTokenSource? cts = null;
        LiveGhost? ghost = null;

        try
        {
            var now = DateTime.Now;
            var targets = SnapshotTargets(host, now, out var targetMetas);
            var effectMetas = _effects.Select(PossessionDeck.MetaOf).ToList();

            var pick = PossessionDeck.Pick(effectMetas, targetMetas, rung, _intensity, _photosafe, _lastTargetKey, _rng);
            if (pick == null)
            {
                // Nothing may run right now (everything cooling down, or the deck is empty). Try again
                // on the next cadence beat rather than hammering the pick every second.
                _nextDue = now + PossessionDeck.NextDelay(rung, _intensity, _rng);
                return;
            }

            effect = _effects[pick.Value.EffectIndex];
            target = pick.Value.TargetIndex >= 0 ? targets[pick.Value.TargetIndex] : null;

            // The warden knocking a card over is the loudest verb we have, so it stays rare: only once
            // the room is collapsing, only on cards, and only about one pick in four.
            var knock = rung >= PossessionRung.Collapse
                        && _wardenEnabled
                        && target?.Role == PossessionRole.Card
                        && Warden?.IsAvailable == true
                        && _rng.Next(4) == 0;

            var ctx = BuildContext(host, rung, remaining, frac, () => effect!);
            if (knock)
            {
                // A knock wants the card to FALL; use the catalog's fall effect when it exists so the
                // choreography and the result read as one action.
                var fall = _effects.FirstOrDefault(e => string.Equals(e.Id, FallEffectId, StringComparison.OrdinalIgnoreCase));
                if (fall != null && !fall.IsLive && fall.CanApply(ctx, target)) effect = fall;
            }

            if (!effect.CanApply(ctx, target))
            {
                _nextDue = now + PossessionDeck.NextDelay(rung, _intensity, _rng);
                return;
            }

            // Book the victim BEFORE any await so a second tick cannot double-book it.
            if (target != null)
            {
                target.IsLive = true;
                _liveKeys.Add(target.Key);
                _lastTargetKey = target.Key;
            }
            cts = new CancellationTokenSource();
            ghost = new LiveGhost(effect, target, host, cts);
            _live.Add(ghost);
            _nextDue = now + PossessionDeck.NextDelay(rung, _intensity, _rng);

            App.Logger?.Information("Possession: {Effect} on {Target} at rung {Rung} (live {Live})",
                effect.Id, target?.Key ?? "(window)", rung, _live.Count);

            if (knock && Warden != null)
                await RunWardenAsync(ct => Warden.KnockAsync(target!, ct), "knock").ConfigureAwait(true);

            try
            {
                await effect.ApplyAsync(ctx, target, cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { /* exit or UndoAll pulled it */ }
            catch (Exception ex)
            {
                App.Logger?.Warning("Possession effect {Effect} failed: {Error}", effect.Id, ex.Message);
                await UndoGhostAsync(ghost, TimeSpan.Zero).ConfigureAwait(true);
                return;
            }

            // The hold runs on its own: keeping the pick slot open for the whole hold would stall the
            // ladder behind a single long-lived ghost.
            if (effect.HoldFor > TimeSpan.Zero)
                FireAndForget(HoldThenUndoAsync(ghost, effect.HoldFor), "hold");
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession pick failed: {Error}", ex.Message);
            if (ghost != null) { try { await UndoGhostAsync(ghost, TimeSpan.Zero).ConfigureAwait(true); } catch { } }
        }
        finally
        {
            _picking = false;
        }
    }

    private async Task HoldThenUndoAsync(LiveGhost ghost, TimeSpan hold)
    {
        CancellationToken token;
        try { token = ghost.Cts.Token; }
        catch (ObjectDisposedException) { return; }      // already released underneath us
        try { await Task.Delay(hold, token).ConfigureAwait(true); }
        catch (OperationCanceledException) { return; }   // exit / UndoAll already took it down
        catch (ObjectDisposedException) { return; }
        await UndoGhostAsync(ghost, TimeSpan.FromMilliseconds(600)).ConfigureAwait(true);
    }

    private PossessionContext BuildContext(HostEntry host, PossessionRung rung, TimeSpan remaining, double frac, Func<IPossessionEffect> effect)
    {
        return new PossessionContext
        {
            Host = host.Host,
            Attribution = host.Attribution,
            Rung = rung,
            Intensity = _intensity,
            Photosafe = _photosafe,
            Rng = _rng,
            ElapsedFraction = frac,
            Remaining = remaining,
            // The warden names the BIG ones only. R0/R1 micro-tics still carry charge and tint, but a
            // bark for every one-pixel nudge would turn the companion into a narrator.
            Name = (id, targetName) =>
            {
                try
                {
                    if (effect().IsBig) App.Bark?.NotifyPossessionEffect(id, targetName);
                }
                catch { }
            },
        };
    }

    private List<PossessionTarget> SnapshotTargets(HostEntry host, DateTime now, out List<PossessionTargetMeta> metas)
    {
        var list = new List<PossessionTarget>();
        metas = new List<PossessionTargetMeta>();
        IReadOnlyList<PossessionTarget> raw;
        try { raw = host.Host.Targets ?? Array.Empty<PossessionTarget>(); }
        catch { return list; }

        foreach (var t in raw)
        {
            if (t?.Element == null || string.IsNullOrEmpty(t.Key)) continue;
            bool visible;
            try { visible = t.Element.IsVisible; } catch { continue; }
            if (!visible) continue;

            var live = t.IsLive || _liveKeys.Contains(t.Key);
            var cool = t.CooldownUntil > now || (_cooldowns.TryGetValue(t.Key, out var until) && until > now);
            list.Add(t);
            metas.Add(new PossessionTargetMeta(t.Key, t.Role, live, cool));
        }
        return list;
    }

    // ---------------------------------------------------------------------------------------------
    //  Undo
    // ---------------------------------------------------------------------------------------------

    private async Task UndoGhostAsync(LiveGhost ghost, TimeSpan duration)
    {
        if (!Release(ghost)) return;
        try { await ghost.Effect.UndoAsync(duration).ConfigureAwait(true); }
        catch (Exception ex) { App.Logger?.Warning("Possession undo {Effect} failed: {Error}", ghost.Effect.Id, ex.Message); }
        finally { ghost.Dispose(); }
    }

    private void UndoGhostSync(LiveGhost ghost)
    {
        if (!Release(ghost)) return;
        try
        {
            var t = ghost.Effect.UndoAsync(TimeSpan.Zero);
            if (t != null)
            {
                t.ContinueWith(x => App.Logger?.Warning("Possession undo {Effect} failed: {Error}",
                        ghost.Effect.Id, x.Exception?.GetBaseException().Message),
                    TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession undo {Effect} threw: {Error}", ghost.Effect.Id, ex.Message); }
        finally { ghost.Dispose(); }
    }

    /// <summary>Take the ghost off the books and start its victim's cooldown. Returns false when some
    /// other path already released it (undo must always be safe to call twice).</summary>
    private bool Release(LiveGhost ghost)
    {
        if (ghost == null || ghost.Released) return false;
        ghost.Released = true;
        _live.Remove(ghost);
        try { ghost.Cts.Cancel(); } catch { }

        var target = ghost.Target;
        if (target != null)
        {
            target.IsLive = false;
            var until = DateTime.Now + PossessionDeck.TargetCooldown;
            target.CooldownUntil = until;
            _cooldowns[target.Key] = until;
            _liveKeys.Remove(target.Key);
        }
        return true;
    }

    /// <summary>The exit is the haunt in reverse: every live ghost undoes itself, newest first, over
    /// about three seconds. The room reassembles rather than snapping back, which is what makes the
    /// whole thing read as authored instead of broken.</summary>
    private async Task ReassembleAsync()
    {
        try
        {
            var ghosts = _live.ToArray();
            Array.Reverse(ghosts);
            var per = ghosts.Length > 0
                ? TimeSpan.FromMilliseconds(Math.Max(150, 3000.0 / ghosts.Length))
                : TimeSpan.Zero;

            foreach (var g in ghosts)
            {
                try { await UndoGhostAsync(g, per).ConfigureAwait(true); }
                catch (Exception ex) { App.Logger?.Warning("Possession reassembly step failed: {Error}", ex.Message); }
            }

            _live.Clear();
            _liveKeys.Clear();
            foreach (var h in _hosts) { try { h.Attribution.ReleaseAll(); } catch { } }

            if (_wardenEnabled && Warden != null)
                await RunWardenAsync(ct => Warden.ReturnAsync(ct), "return").ConfigureAwait(true);

            CurrentRung = PossessionRung.Settle;
            try { RungChanged?.Invoke(CurrentRung); } catch { }
            App.Logger?.Information("Possession: reassembled, room is quiet");
        }
        catch (Exception ex) { App.Logger?.Warning("Possession reassembly failed: {Error}", ex.Message); }
    }

    // ---------------------------------------------------------------------------------------------
    //  Tripwires
    // ---------------------------------------------------------------------------------------------

    private void OnEscapeAttempted(EscapeAttempt attempt)
    {
        if (!IsHaunting || !_tripwiresEnabled) return;
        try
        {
            // One reaction per 1.5 s no matter how many tripwires fire: a held key or a frantic click
            // storm must not stack into a strobe.
            var now = DateTime.Now;
            if (now - _lastTripwireAt < TripwireThrottle) return;
            _lastTripwireAt = now;

            var repeat = attempt.Repeat;
            var strength = repeat <= 1 ? 0.5 : 0.8;
            PulseAll(strength);
            try { App.Bark?.NotifyPossessionTripwire(attempt.Kind, attempt.Repeat, attempt.Total); } catch { }

            if (repeat >= 2)
            {
                if (!_photosafe)
                {
                    // A blink, not a strobe: one extra flare 120 ms later plus a short shake.
                    FireAndForget(BlinkAsync(), "tripwire blink");
                    try { App.ScreenShake?.Shake(0.4, 250); } catch { }
                }
            }

            if (repeat >= 3 && _wardenEnabled && Warden?.IsAvailable == true && now - _lastStareAt >= StareCooldown)
            {
                _lastStareAt = now;
                FireAndForget(RunWardenAsync(ct => Warden!.StareAsync(attempt.Kind, ct), "stare"), "warden stare");
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession tripwire failed: {Error}", ex.Message); }
    }

    private async Task BlinkAsync()
    {
        await Task.Delay(120).ConfigureAwait(true);
        PulseAll(1.0);
    }

    private void PulseAll(double strength)
    {
        foreach (var h in _hosts)
        {
            try { h.Attribution.EdgePulse(strength); } catch { }
        }
    }

    // ---------------------------------------------------------------------------------------------
    //  Warden plumbing
    // ---------------------------------------------------------------------------------------------

    /// <summary>Run one warden verb, never waiting more than 15 s. The tube can be busy with a bubble
    /// egg or an AI turn; a verb that never returns must not wedge the ladder behind it.</summary>
    private async Task RunWardenAsync(Func<CancellationToken, Task> verb, string name)
    {
        var warden = Warden;
        if (warden == null) return;
        using var cts = new CancellationTokenSource(WardenVerbTimeout);
        try
        {
            var t = verb(cts.Token);
            if (t != null) await t.ConfigureAwait(true);
        }
        catch (OperationCanceledException) { App.Logger?.Debug("Possession warden {Verb} timed out", name); }
        catch (Exception ex) { App.Logger?.Warning("Possession warden {Verb} failed: {Error}", name, ex.Message); }
    }

    private static void FireAndForget(Task? task, string what)
    {
        if (task == null) return;
        task.ContinueWith(t => App.Logger?.Warning("Possession {What} faulted: {Error}",
                what, t.Exception?.GetBaseException().Message),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    // ---------------------------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _lockdown.LockdownActivated -= OnLockdownActivated;
            _lockdown.LockdownDeactivated -= OnLockdownDeactivated;
            _lockdown.CountdownTick -= OnCountdownTick;
            _lockdown.EscapeAttempted -= OnEscapeAttempted;
        }
        catch { }
        IsHaunting = false;
        UndoAll();
    }

    private sealed class HostEntry
    {
        public HostEntry(IPossessionHost host, EmberAttribution attribution)
        {
            Host = host;
            Attribution = attribution;
        }
        public IPossessionHost Host { get; }
        public EmberAttribution Attribution { get; }
    }

    private sealed class LiveGhost
    {
        public LiveGhost(IPossessionEffect effect, PossessionTarget? target, HostEntry host, CancellationTokenSource cts)
        {
            Effect = effect;
            Target = target;
            Host = host;
            Cts = cts;
        }
        public IPossessionEffect Effect { get; }
        public PossessionTarget? Target { get; }
        public HostEntry Host { get; }
        public CancellationTokenSource Cts { get; }
        public bool Released { get; set; }

        public void Dispose() { try { Cts.Dispose(); } catch { } }
    }
}
