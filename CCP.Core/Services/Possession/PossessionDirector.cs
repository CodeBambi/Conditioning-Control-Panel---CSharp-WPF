// PORTED from ConditioningControlPanel/Services/Possession/PossessionDirector.cs (7.1.5, 1,156 lines).
// The state machine behind the Lockdown haunt, head-neutral: WHERE on the ladder we are, WHEN the next
// ghost starts, the tripwire answer, the timer-restart reset and HOW it all comes back. Every RULE is
// PossessionDeck (pure); this is the plumbing over a PossessionHost seam.
//
// Carried from WPF: activation / deactivation / tick / tripwire / restart handlers, the one-pick-at-a-time
// cadence, the live ledger mirrored by key, hold-then-undo, the reassembly exit with its generation
// guard, UndoAll (sync, never throws), PulseEdges for the Dose keeper, the barks.
// Scenes (one pick in three from Melt) are elected here and played by the head (IPossessionScene).
// Not on this director (each logged once when it would have run):
// the warden verbs (knock, stare, leave, return), the proximity pick (no pointer reading), the reactive
// layer (RequestReactive / PossessionEvents) and the ember charge / outline around a victim.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Services.Possession;

public sealed class PossessionDirector : IDisposable
{
    /// <summary>The head's director (WPF App.Possession). Null on a head with none.</summary>
    public static PossessionDirector? Current { get; set; }

    private readonly LockdownService _lockdown;
    private readonly List<IPossessionEffect> _effects;
    private readonly PossessionHost _host;
    private readonly List<LiveGhost> _live = new();
    private readonly Random _rng;

    private readonly Dictionary<string, DateTime> _cooldowns = new(StringComparer.Ordinal);
    private readonly HashSet<string> _liveKeys = new(StringComparer.Ordinal);
    private readonly HashSet<PossessionRung> _barkedRungs = new();
    private readonly HashSet<string> _loggedMissing = new(StringComparer.Ordinal);

    private PossessionIntensity _intensity = PossessionIntensity.Eerie;
    private bool _photosafe;
    private bool _tripwiresEnabled = true;

    private DateTime _nextDue = DateTime.MaxValue;
    private string? _lastTargetKey;
    private DateTime _lastTripwireAt = DateTime.MinValue;
    private DateTime _lastRestartAt = DateTime.MinValue;
    private bool _picking;
    private bool _disposed;
    private int _generation;

    private static readonly TimeSpan TripwireThrottle = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan RestartQuiet = TimeSpan.FromSeconds(2);
    private const int SceneEveryNthPick = 3;

    /// <summary>The director's clock (tests step it).</summary>
    internal Func<DateTime> Now = () => DateTime.Now;

    public PossessionDirector(LockdownService lockdown, IEnumerable<IPossessionEffect>? effects, PossessionHost host, Random? rng = null)
    {
        _lockdown = lockdown ?? throw new ArgumentNullException(nameof(lockdown));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _effects = (effects ?? Enumerable.Empty<IPossessionEffect>()).Where(e => e != null).ToList();
        _rng = rng ?? new Random();

        _lockdown.LockdownActivated += OnLockdownActivated;
        _lockdown.LockdownDeactivated += OnLockdownDeactivated;
        _lockdown.CountdownTick += OnCountdownTick;
        _lockdown.EscapeAttempted += OnEscapeAttempted;
        _lockdown.TimerRestarted += OnTimerRestarted;
    }

    public PossessionRung CurrentRung { get; private set; } = PossessionRung.Settle;
    public event Action<PossessionRung>? RungChanged;
    public bool IsHaunting { get; private set; }
    public int LiveEffectCount => _live.Count;

    /// <summary>Raised the moment a haunt is committed (effect id, victim key or null, big).</summary>
    public event Action<string, string?, bool>? EffectStarted;

    /// <summary>Raised when a tripwire reaction actually RUNS (after the throttle).</summary>
    public event Action<EscapeAttempt>? TripwireReacted;

    /// <summary>Synchronous, crash-safe reset: cancel everything, every live effect undoes with no
    /// animation. Dispose, panic and any recovery path that needs the UI back RIGHT NOW. Never throws.</summary>
    public void UndoAll()
    {
        try
        {
            foreach (var g in _live.ToArray()) UndoGhostSync(g);
            _live.Clear();
            _liveKeys.Clear();
        }
        catch (Exception ex) { Log.Warning("Possession UndoAll failed: {Error}", ex.Message); }
    }

    /// <summary>Panic: everything comes back at once and the room holds a full FirstDelay of quiet, so
    /// nothing twitches again under the press. The lockdown itself is LockdownService's business.</summary>
    public void PanicStop()
    {
        UndoAll();
        _picking = false;
        if (IsHaunting) _nextDue = Now() + PossessionDeck.FirstDelay(CurrentRung, _intensity, _rng);
    }

    // ---- Lockdown lifecycle ----------------------------------------------------------------------

    private void OnLockdownActivated() => OnUi(() =>
    {
        // Bumped before the enabled check so a lockdown that runs WITHOUT possession still
        // invalidates a reassembly the previous one left in flight.
        _generation++;

        var s = CoreSettings.Current;
        if (s == null || !s.LockdownPossessionEnabled)
        {
            IsHaunting = false;
            return;
        }

        ReadLiveSettings();
        _tripwiresEnabled = s.LockdownTripwiresEnabled;

        _barkedRungs.Clear();
        _cooldowns.Clear();
        if (_live.Count == 0) _liveKeys.Clear();
        _lastTargetKey = null;
        _lastTripwireAt = DateTime.MinValue;
        _lastRestartAt = DateTime.MinValue;
        _picking = false;
        CurrentRung = PossessionRung.Settle;
        IsHaunting = true;

        // Let the room settle before the first twitch (PossessionDeck.FirstWait).
        _nextDue = Now() + PossessionDeck.FirstDelay(CurrentRung, _intensity, _rng);
        Log.Information("Possession armed: intensity={Intensity}, first haunt in {Sec:F0}s, {Count} effects",
            _intensity, (_nextDue - Now()).TotalSeconds, _effects.Count);
    }, "activate");

    private void OnLockdownDeactivated() => OnUi(() =>
    {
        if (!IsHaunting) { CurrentRung = PossessionRung.Settle; return; }
        IsHaunting = false;
        _nextDue = DateTime.MaxValue;
        FireAndForget(ReassembleAsync(), "reassembly");
    }, "deactivate");

    private void OnCountdownTick(TimeSpan remaining)
    {
        if (!IsHaunting || _disposed) return;
        OnUi(() => Tick(remaining), "tick");
    }

    internal void Tick(TimeSpan remaining)
    {
        if (!IsHaunting || _disposed) return;
        try
        {
            ReadLiveSettings();      // intensity / photosafe are live toggles, not activation snapshots

            var frac = _lockdown.ElapsedFraction;
            var rung = PossessionDeck.RungFor(frac, _intensity);
            if (rung != CurrentRung)
            {
                var previous = CurrentRung;
                CurrentRung = rung;
                Log.Information("Possession rung {From} -> {To} at {Pct:P0}", previous, rung, frac);
                try { RungChanged?.Invoke(rung); } catch (Exception ex) { Diag.Swallowed(ex); }

                // A restart sets the rung, pulses the edge and spends that rung's bark itself; a tick
                // landing on the same moment must not do any of it twice.
                var justRestarted = Now() - _lastRestartAt < RestartQuiet;
                if (!justRestarted) Pulse(0.35 + 0.15 * (int)rung);
                if (_barkedRungs.Add(rung) && !justRestarted)
                    Bark(PossessionBarkTriggers.RungChanged, ("rung", (double)(int)rung));

                if (rung == PossessionRung.ItKnows) LogMissingOnce("warden leave");
            }

            if (_picking) return;
            if (Now() < _nextDue) return;
            if (!PossessionDeck.FitsConcurrency(LiveSlots, 1, rung)) return;
            if (!SafeIsUsable()) return;

            _picking = true;
            FireAndForget(StartOneAsync(rung, remaining, frac), "haunt");
        }
        catch (Exception ex)
        {
            _picking = false;
            Log.Warning("Possession tick failed: {Error}", ex.Message);
        }
    }

    private void ReadLiveSettings()
    {
        var s = CoreSettings.Current;
        if (s == null) return;
        _intensity = (PossessionIntensity)Math.Clamp(s.LockdownPossessionIntensity, 0, 2);
        _photosafe = s.LockdownPhotosafe;
    }

    private bool SafeIsUsable()
    {
        try { return _host.IsUsable(); }
        catch { return false; }
    }

    // ---- Picking and running one haunt -----------------------------------------------------------

    private async Task StartOneAsync(PossessionRung rung, TimeSpan remaining, double frac)
    {
        LiveGhost? ghost = null;
        try
        {
            var now = Now();

            // WPF A6: from Melt up one pick in three is a scene (TryStartSceneAsync). The roll is
            // spent either way so the single-effect cadence matches; with no scene registered, or none
            // that fits, the pick falls through to the deck.
            IPossessionEffect? effect = null;
            PossessionTarget? target = null;
            PossessionContext? ctx = null;
            if (rung >= PossessionRung.Melt && _rng.Next(SceneEveryNthPick) == 0)
            {
                if (Scenes.Count == 0) LogMissingOnce("scenes");
                else if (ElectScene(rung, remaining, frac, out var sceneCtx) is { } scene) { effect = scene; ctx = sceneCtx; }
            }

            if (effect == null || ctx == null)
            {
                var targets = SnapshotTargets(now, out var targetMetas);
                var effectMetas = _effects.Select(PossessionDeck.MetaOf).ToList();

                var pick = PossessionDeck.Pick(effectMetas, targetMetas, rung, _intensity, _photosafe, _lastTargetKey, _rng, null);
                if (pick == null)
                {
                    // Nothing may run right now. Try again on the next cadence beat.
                    _nextDue = now + PossessionDeck.NextDelay(rung, _intensity, _rng);
                    return;
                }

                effect = _effects[pick.Value.EffectIndex];
                target = pick.Value.TargetIndex >= 0 ? targets[pick.Value.TargetIndex] : null;
                ctx = BuildContext(rung, remaining, frac, effect);

                if (effect.IsLive || !effect.CanApply(ctx, target))
                {
                    _nextDue = now + PossessionDeck.NextDelay(rung, _intensity, _rng);
                    return;
                }
            }

            // Book the victim BEFORE any await so a second tick cannot double-book it.
            if (target != null)
            {
                target.IsLive = true;
                _liveKeys.Add(target.Key);
                _lastTargetKey = target.Key;
            }
            var cts = new CancellationTokenSource();
            ghost = new LiveGhost(effect, target, cts);
            _live.Add(ghost);
            _nextDue = now + PossessionDeck.NextDelay(rung, _intensity, _rng);

            Log.Information("Possession: {Effect} on {Target} at rung {Rung} (live {Live})",
                effect.Id, target?.Key ?? "(window)", rung, _live.Count);
            try { EffectStarted?.Invoke(effect.Id, target?.Key, effect.IsBig); } catch (Exception ex) { Diag.Swallowed(ex); }

            try
            {
                await effect.ApplyAsync(ctx, target, cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { } // exit, panic or UndoAll pulled it
            catch (Exception ex)
            {
                Log.Warning("Possession effect {Effect} failed: {Error}", effect.Id, ex.Message);
                await UndoGhostAsync(ghost, TimeSpan.Zero).ConfigureAwait(true);
                return;
            }

            // The hold runs on its own: keeping the pick slot open for the whole hold would stall the
            // ladder behind a single long-lived ghost.
            if (effect.HoldFor > TimeSpan.Zero && !ghost.Released)
                FireAndForget(HoldThenUndoAsync(ghost, effect.HoldFor), "hold");
        }
        catch (Exception ex)
        {
            Log.Warning("Possession pick failed: {Error}", ex.Message);
            if (ghost != null) { try { await UndoGhostAsync(ghost, TimeSpan.Zero).ConfigureAwait(true); } catch (Exception exIgnored) { Diag.Swallowed(exIgnored); } }
        }
        finally
        {
            _picking = false;
        }
    }

    private int LiveSlots => _live.Count;

    /// <summary>The choreographies this head can play (WPF PossessionSceneCatalog). A scene is a haunt
    /// with no target of its own: it takes its victims from the host registry, books them, and gives
    /// every one back on undo. Empty on a head with none.</summary>
    public List<IPossessionScene> Scenes { get; } = new();

    /// <summary>WPF TryStartSceneAsync, the election: a scene that is not already playing, is allowed
    /// at this rung, intensity and photosafe setting, fits the room by its beats, and says it has victims.</summary>
    private IPossessionScene? ElectScene(PossessionRung rung, TimeSpan remaining, double frac, out PossessionContext? ctx)
    {
        ctx = null;
        var eligible = new List<(IPossessionScene Scene, PossessionContext Ctx)>();
        foreach (var sc in Scenes)
        {
            try
            {
                if (sc == null || sc.IsLive) continue;
                if (sc.MinRung > rung || sc.MinIntensity > _intensity) continue;
                if (sc.UsesFlicker && _photosafe) continue;
                if (!PossessionDeck.FitsConcurrency(LiveSlots, sc.Beats, rung)) continue;
                var c = BuildContext(rung, remaining, frac, sc);
                if (sc.CanApply(c, null)) eligible.Add((sc, c));
            }
            catch (Exception ex) { Log.Debug("Possession scene {Scene} could not be asked: {Error}", sc?.Id, ex.Message); }
        }
        if (eligible.Count == 0) return null;
        var won = eligible[eligible.Count == 1 ? 0 : _rng.Next(eligible.Count)];
        ctx = won.Ctx;
        return won.Scene;
    }

    private PossessionContext BuildContext(PossessionRung rung, TimeSpan remaining, double frac, IPossessionEffect effect) => new()
    {
        Host = _host,
        Rung = rung,
        Intensity = _intensity,
        Photosafe = _photosafe,
        Rng = _rng,
        ElapsedFraction = frac,
        Remaining = remaining,
        // Only big effects are named (WPF BuildContext): micro-tics stay silent.
        Name = (id, target) =>
        {
            if (!effect.IsBig) return;
            Bark(PossessionBarkTriggers.Effect, ("effect", id), ("target", string.IsNullOrWhiteSpace(target) ? "that one" : target!));
        },
    };

    private List<PossessionTarget> SnapshotTargets(DateTime now, out List<PossessionTargetMeta> metas)
    {
        var list = new List<PossessionTarget>();
        metas = new List<PossessionTargetMeta>();
        IReadOnlyList<PossessionTarget> raw;
        try { raw = _host.Targets() ?? Array.Empty<PossessionTarget>(); }
        catch { return list; }

        foreach (var t in raw)
        {
            if (t?.Element == null || string.IsNullOrEmpty(t.Key)) continue;
            bool visible;
            try { visible = t.IsVisible(); } catch { continue; }
            if (!visible) continue;

            var live = t.IsLive || _liveKeys.Contains(t.Key);
            var cool = t.CooldownUntil > now || (_cooldowns.TryGetValue(t.Key, out var until) && until > now);
            list.Add(t);
            metas.Add(new PossessionTargetMeta(t.Key, t.Role, live, cool));
        }
        return list;
    }

    // ---- Timer restart (Emergency Exit sendback) -------------------------------------------------

    private void OnTimerRestarted(string reason)
    {
        if (!IsHaunting || _disposed) return;
        OnUi(() =>
        {
            var frac = _lockdown.ElapsedFraction;
            _lastRestartAt = Now();
            ApplyRestartReset(PossessionDeck.RungFor(frac, _intensity));

            FireAndForget(QuickUndoAsync(), "restart undo");
            Pulse(0.6);
            int restart;
            try { restart = _lockdown.RestartCount; } catch { restart = 0; }
            Bark(PossessionBarkTriggers.TimerRestarted, ("reason", reason ?? ""), ("restart", (double)restart));

            _nextDue = Now() + PossessionDeck.FirstDelay(CurrentRung, _intensity, _rng);
            try { RungChanged?.Invoke(CurrentRung); } catch (Exception ex) { Diag.Swallowed(ex); }
        }, "timer restart");
    }

    /// <summary>Everything the ladder has to FORGET when the clock is rewound to its full duration: the
    /// rung, the rung barks, the last victim and every per-target cooldown. The rung the restart just
    /// set goes straight back into the announced set, so the tick from the same rewind cannot announce
    /// it a second time. Idempotent.</summary>
    internal void ApplyRestartReset(PossessionRung rung)
    {
        CurrentRung = rung;

        _barkedRungs.Clear();
        _barkedRungs.Add(rung);

        _lastTargetKey = null;
        _picking = false;

        _cooldowns.Clear();
        IReadOnlyList<PossessionTarget> targets;
        try { targets = _host.Targets() ?? Array.Empty<PossessionTarget>(); }
        catch { return; }
        foreach (var t in targets)
        {
            // A victim that is still possessed keeps its booking; Release() gives it a fresh cooldown.
            try { if (t != null && !t.IsLive) t.CooldownUntil = DateTime.MinValue; } catch (Exception ex) { Diag.Swallowed(ex); }
        }
    }

    /// <summary>Rungs whose one-per-rung bark has already been spent (restart tests).</summary>
    internal IReadOnlyCollection<PossessionRung> AnnouncedRungs => _barkedRungs;

    private async Task QuickUndoAsync()
    {
        try
        {
            var ghosts = _live.ToArray();
            Array.Reverse(ghosts);
            foreach (var g in ghosts)
            {
                try { await UndoGhostAsync(g, TimeSpan.FromMilliseconds(500)).ConfigureAwait(true); }
                catch (Exception ex) { Log.Warning("Possession restart undo step failed: {Error}", ex.Message); }
            }
            // No Clear() here: IsHaunting stays TRUE across a restart, so a ghost added during the loop
            // belongs to the room and must keep its booking.
        }
        catch (Exception ex) { Log.Warning("Possession restart undo failed: {Error}", ex.Message); }
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

    // ---- Undo ------------------------------------------------------------------------------------

    private async Task UndoGhostAsync(LiveGhost ghost, TimeSpan duration)
    {
        if (!Release(ghost)) return;
        try { await ghost.Effect.UndoAsync(duration).ConfigureAwait(true); }
        catch (Exception ex) { Log.Warning("Possession undo {Effect} failed: {Error}", ghost.Effect.Id, ex.Message); }
        finally { ghost.Dispose(); }
    }

    private void UndoGhostSync(LiveGhost ghost)
    {
        if (!Release(ghost)) return;
        try
        {
            var t = ghost.Effect.UndoAsync(TimeSpan.Zero);
            t?.ContinueWith(x => Log.Warning("Possession undo {Effect} failed: {Error}",
                    ghost.Effect.Id, x.Exception?.GetBaseException().Message),
                TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception ex) { Log.Warning("Possession undo {Effect} threw: {Error}", ghost.Effect.Id, ex.Message); }
        finally { ghost.Dispose(); }
    }

    /// <summary>Take the ghost off the books and start its victim's cooldown. False when some other
    /// path already released it (undo must always be safe to call twice).</summary>
    private bool Release(LiveGhost ghost)
    {
        if (ghost == null || ghost.Released) return false;
        ghost.Released = true;
        _live.Remove(ghost);
        try { ghost.Cts.Cancel(); } catch (Exception ex) { Diag.Swallowed(ex); }

        var target = ghost.Target;
        if (target != null)
        {
            target.IsLive = false;
            var until = Now() + PossessionDeck.TargetCooldown;
            target.CooldownUntil = until;
            _cooldowns[target.Key] = until;
            _liveKeys.Remove(target.Key);
        }
        return true;
    }

    /// <summary>The exit is the haunt in reverse: every live ghost undoes itself, newest first, over
    /// about three seconds.</summary>
    private async Task ReassembleAsync()
    {
        var generation = _generation;
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
                catch (Exception ex) { Log.Warning("Possession reassembly step failed: {Error}", ex.Message); }
            }

            if (!ShouldFinishReassembly(generation, _generation, IsHaunting))
            {
                Log.Debug("Possession: reassembly tail skipped, a new lockdown started underneath it");
                return;
            }

            CurrentRung = PossessionRung.Settle;
            try { RungChanged?.Invoke(CurrentRung); } catch (Exception ex) { Diag.Swallowed(ex); }
            Log.Information("Possession: reassembled, room is quiet");
        }
        catch (Exception ex) { Log.Warning("Possession reassembly failed: {Error}", ex.Message); }
    }

    /// <summary>Is the reassembly that started at <paramref name="startGeneration"/> still the room's
    /// current business? Every activation bumps the generation and turns the haunt back on.</summary>
    internal static bool ShouldFinishReassembly(int startGeneration, int currentGeneration, bool haunting)
        => startGeneration == currentGeneration && !haunting;

    // ---- Tripwires -------------------------------------------------------------------------------

    private void OnEscapeAttempted(EscapeAttempt attempt)
    {
        if (!IsHaunting || !_tripwiresEnabled) return;
        OnUi(() =>
        {
            // One reaction per 1.5 s no matter how many tripwires fire: a held key or a frantic click
            // storm must not stack into a strobe.
            var now = Now();
            if (now - _lastTripwireAt < TripwireThrottle) return;
            _lastTripwireAt = now;

            var repeat = attempt.Repeat;
            Pulse(TripwireStrength(repeat));
            try { TripwireReacted?.Invoke(attempt); } catch (Exception ex) { Diag.Swallowed(ex); }
            Bark(PossessionBarkTriggers.Tripwire, ("kind", attempt.Kind), ("repeat", (double)attempt.Repeat), ("total", (double)attempt.Total));

            if (repeat >= 2 && !_photosafe)
            {
                // A blink, not a strobe: one extra flare 120 ms later plus a short shake.
                FireAndForget(BlinkAsync(), "tripwire blink");
                try { _host.Shake(0.4, 250); } catch (Exception ex) { Diag.Swallowed(ex); }
            }
            if (repeat >= 3) LogMissingOnce("warden stare");
        }, "tripwire");
    }

    /// <summary>WPF OnEscapeAttempted: the first attempt of a kind pulses at half, a repeat at 0.8.</summary>
    internal static double TripwireStrength(int repeat) => repeat <= 1 ? 0.5 : 0.8;

    private async Task BlinkAsync()
    {
        await Task.Delay(120).ConfigureAwait(true);
        if (IsHaunting && !_disposed) OnUi(() => Pulse(1.0), "blink");
    }

    /// <summary>Window-edge ember pulse for OTHER lockdown layers that did something the room should
    /// own (the Dose keeper switching a feature back on). No-op unless haunting.</summary>
    public void PulseEdges(double strength)
    {
        if (_disposed || !IsHaunting) return;
        OnUi(() => Pulse(Math.Clamp(strength, 0.0, 1.0)), "pulse");
    }

    private void Pulse(double strength)
    {
        try { _host.EdgePulse(Math.Clamp(strength, 0.0, 1.0)); } catch (Exception ex) { Diag.Swallowed(ex); }
    }

    // ---- Plumbing --------------------------------------------------------------------------------

    private static void Bark(string trigger, params (string Key, object Value)[] values)
    {
        try
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var (k, v) in values) d[k] = v;
            CoreBark.Raise(trigger, d);
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
    }

    private void LogMissingOnce(string what)
    {
        if (_loggedMissing.Add(what)) Log.Information("Possession: {What} has no head here, skipped", what);
    }

    private void OnUi(Action a, string what)
    {
        if (_disposed) return;
        try
        {
            _host.OnUi(() =>
            {
                try { a(); }
                catch (Exception ex) { Log.Warning("Possession {What} failed: {Error}", what, ex.Message); }
            });
        }
        catch (Exception ex) { Log.Warning("Possession {What} dispatch failed: {Error}", what, ex.Message); }
    }

    private static void FireAndForget(Task? task, string what)
    {
        task?.ContinueWith(t => Log.Warning("Possession {What} faulted: {Error}",
                what, t.Exception?.GetBaseException().Message),
            TaskContinuationOptions.OnlyOnFaulted);
    }

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
            _lockdown.TimerRestarted -= OnTimerRestarted;
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
        IsHaunting = false;
        UndoAll();
        if (ReferenceEquals(Current, this)) Current = null;
    }

    private sealed class LiveGhost
    {
        public LiveGhost(IPossessionEffect effect, PossessionTarget? target, CancellationTokenSource cts)
        {
            Effect = effect;
            Target = target;
            Cts = cts;
        }

        public IPossessionEffect Effect { get; }
        public PossessionTarget? Target { get; }
        public CancellationTokenSource Cts { get; }
        public bool Released { get; set; }

        public void Dispose() { try { Cts.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); } }
    }
}
