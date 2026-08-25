using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Possession.Scenes;

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
    private readonly List<IPossessionScene> _scenes = PossessionSceneCatalog.CreateAll();
    private readonly PossessionEvents _events = new();
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
    private DateTime _lastReactiveAt = DateTime.MinValue;
    private DateTime _lastRestartAt = DateTime.MinValue;
    private bool _picking;
    private bool _disposed;

    /// <summary>Bumped on every activation. Anything with a long tail of awaits (the reassembly exit
    /// is three seconds of undo plus up to fifteen of warden) captures it first and bails when it has
    /// moved: by then a NEW lockdown may own the room, and the previous one's curtain call must not
    /// strip its outlines or reset its rung.</summary>
    private int _generation;

    private static readonly TimeSpan TripwireThrottle = TimeSpan.FromSeconds(1.5);

    /// <summary>How long after a timer restart a rung change counts as "the restart's own". The
    /// restart sets the rung, pulses the edge and spends the rung bark itself; a tick landing inside
    /// this window must not do any of it a second time, whichever of the two events arrived first.</summary>
    private static readonly TimeSpan RestartQuiet = TimeSpan.FromSeconds(2);

    /// <summary>Floor between event-driven ghosts (B15). The reactive layer answers what the USER does,
    /// and a user clicking around a tab generates events far faster than the room should answer them -
    /// without this, moving the mouse would turn the window into a strobe of ember charges.</summary>
    private static readonly TimeSpan ReactiveThrottle = TimeSpan.FromSeconds(6);

    /// <summary>From Melt upwards, one pick in this many is a SCENE rather than a single effect.</summary>
    private const int SceneEveryNthPick = 3;
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
        _lockdown.TimerRestarted += OnTimerRestarted;
    }

    // ---------------------------------------------------------------------------------------------
    //  Public surface (see the bottom of PossessionContracts.cs)
    // ---------------------------------------------------------------------------------------------

    public PossessionRung CurrentRung { get; private set; } = PossessionRung.Settle;
    public event Action<PossessionRung>? RungChanged;
    public bool IsHaunting { get; private set; }
    public IPossessionWarden? Warden { get; set; }
    public int LiveEffectCount => _live.Count;

    /// <summary>Raised the moment a haunt is committed (effect id, victim key or null for a window
    /// effect, whether the warden will name it). The audio layer rides this for its ember tick rather
    /// than hooking every effect, which keeps the effects ignorant of sound.</summary>
    public event Action<string, string?, bool>? EffectStarted;

    /// <summary>Raised when a tripwire reaction actually RUNS (after the throttle), not when the
    /// attempt is reported. Consumers can assume a pulse/bark just happened.</summary>
    public event Action<EscapeAttempt>? TripwireReacted;

    public void AttachHost(IPossessionHost host)
    {
        if (host == null || _disposed) return;
        DispatcherHelper.RunOnUI(() =>
        {
            if (_hosts.Any(h => ReferenceEquals(h.Host, host))) return;
            _hosts.Add(new HostEntry(host, new EmberAttribution(host, () => App.Settings?.Current?.LockdownPhotosafe == true)));
            try { _events.Attach(this, host); } catch (Exception ex) { App.Logger?.Warning("Possession: reactive adapter attach failed: {Error}", ex.Message); }
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
            try { _events.Detach(host); } catch { }
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
            // Release() (inside UndoGhostSync) is what owns the ledger; these two are a belt, and they
            // are only safe HERE because there is no await above them for a new ghost to arrive in.
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
            // Bumped before the enabled check so a lockdown that runs WITHOUT possession still
            // invalidates a reassembly the previous one left in flight.
            _generation++;

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
            // The live ledger belongs to Release(). Wiping it wholesale would orphan any ghost the
            // PREVIOUS lockdown's reassembly is still unwinding underneath us - its control would go
            // back into the pool while it is still possessed. Sweep it only when nothing is live,
            // which is the case this is here for (a leaked key must never ban a control forever).
            if (_live.Count == 0) _liveKeys.Clear();
            _lastTargetKey = null;
            _lastTripwireAt = DateTime.MinValue;
            _lastStareAt = DateTime.MinValue;
            _lastReactiveAt = DateTime.MinValue;
            _lastRestartAt = DateTime.MinValue;
            // A pick that died with a torn-down window never reaches StartOneAsync's finally, and a
            // stuck flag shuts the cadence loop for every LATER lockdown as well.
            _picking = false;
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

                // Belt for the timer restart: a restart sets the rung, pulses the edge at 0.6 and
                // spends that rung's bark itself, so a tick landing on the same moment would flare
                // and announce it twice. LockdownService raises TimerRestarted and CountdownTick from
                // the same rewind; this makes the pair idempotent whichever order they arrive in.
                var justRestarted = DateTime.Now - _lastRestartAt < RestartQuiet;

                // The rung change itself is an event: the whole window flares once so the escalation is
                // never something the user only notices in hindsight.
                if (!justRestarted) PulseAll(0.35 + 0.15 * (int)rung);
                if (_barkedRungs.Add(rung) && !justRestarted) { try { App.Bark?.NotifyPossessionRung((int)rung); } catch { } }

                if (rung == PossessionRung.ItKnows && _wardenEnabled && Warden?.IsAvailable == true)
                    FireAndForget(RunWardenAsync(ct => Warden!.LeaveAsync(ct), "leave"), "warden leave");
            }

            if (_picking) return;
            if (DateTime.Now < _nextDue) return;
            if (!PossessionDeck.FitsConcurrency(LiveSlots, 1, rung)) return;
            // A3 (wave 2): the video check that used to live here is GONE. It read as "content is king",
            // but in practice a lockdown run is mostly video, so the haunt spent most of its life
            // paused and the owner's first live run felt empty. What still stops us is the host's own
            // IsUsable, which covers the cases that actually matter: minimized, not loaded, a content
            // window that has taken the screen, or the Lock Card holding the user's input.

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

            // A6: from Melt up, one pick in three is a scene instead of a single effect. Elected here
            // rather than dealt from the deck because a scene claims several victims of several roles,
            // which the deck's one-effect-one-victim pick cannot express.
            if (rung >= PossessionRung.Melt && _rng.Next(SceneEveryNthPick) == 0
                && await TryStartSceneAsync(host, rung, remaining, frac).ConfigureAwait(true))
            {
                _nextDue = DateTime.Now + PossessionDeck.NextDelay(rung, _intensity, _rng);
                return;
            }

            var targets = SnapshotTargets(host, now, out var targetMetas);
            var effectMetas = _effects.Select(PossessionDeck.MetaOf).ToList();

            // A5: half the picks look where the user is looking first. A ghost on a control at the far
            // end of the window is a tree falling in an empty forest.
            IReadOnlyCollection<int>? near = null;
            if (PossessionDeck.ShouldUseProximity(_rng)) near = NearTargetIndexes(host, targets);

            var pick = PossessionDeck.Pick(effectMetas, targetMetas, rung, _intensity, _photosafe, _lastTargetKey, _rng, near);
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
            try { EffectStarted?.Invoke(effect.Id, target?.Key, effect.IsBig); } catch { }

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

    /// <summary>Total concurrency weight in flight. A scene counts as its beat count, so a
    /// three-beat choreography plus a full deck cannot stack past the rung's cap.</summary>
    private int LiveSlots
    {
        get
        {
            var n = 0;
            foreach (var g in _live) n += g.Slots;
            return n;
        }
    }

    /// <summary>Indexes of the targets sitting within <see cref="PossessionDeck.ProximityRadius"/> of the
    /// cursor. Null when we have no cursor reading yet (nothing has moved the mouse since launch) or
    /// when fewer than two controls qualify - in both cases the plain full-pool pick is the honest
    /// answer, and the deck falls back to it anyway.</summary>
    private static IReadOnlyCollection<int>? NearTargetIndexes(HostEntry host, List<PossessionTarget> targets)
    {
        try
        {
            var origin = PossessionPointer.Position;
            if (origin.X <= 0 && origin.Y <= 0) return null;

            var centres = new List<(double X, double Y)>(targets.Count);
            foreach (var t in targets)
            {
                try
                {
                    var r = Effects.PossessionVisual.BoundsOf(host.Host, t.Element);
                    centres.Add(r.IsEmpty ? (double.NaN, double.NaN) : (r.X + r.Width / 2, r.Y + r.Height / 2));
                }
                catch { centres.Add((double.NaN, double.NaN)); }
            }

            var hits = PossessionDeck.WithinRadius(centres, origin.X, origin.Y, PossessionDeck.ProximityRadius);
            return hits.Count >= 2 ? hits : null;
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------------------------------------
    //  Scenes (A6)
    // ---------------------------------------------------------------------------------------------

    /// <summary>Elect and run one scene. Returns false when no scene is eligible, so the caller can
    /// fall through to a normal pick rather than wasting the beat.</summary>
    private async Task<bool> TryStartSceneAsync(HostEntry host, PossessionRung rung, TimeSpan remaining, double frac)
    {
        LiveGhost? ghost = null;
        try
        {
            var eligible = new List<IPossessionScene>();
            foreach (var sc in _scenes)
            {
                if (rung < sc.MinRung) continue;
                if (sc is PossessionSceneBase { IsRunning: true }) continue;
                if (_live.Exists(g => ReferenceEquals((g.Effect as PossessionSceneEffect)?.Scene, sc))) continue;
                eligible.Add(sc);
            }
            if (eligible.Count == 0) return false;

            // Elect FIRST, then check the cap against what THIS scene actually costs. The old order
            // checked a flat "+2" and then booked Math.Max(1, scene.Beats): a three-beat scene elected
            // into a Melt room (cap 3) that already had one ghost live pushed the room to four.
            var scene = eligible[_rng.Next(eligible.Count)];
            var slots = Math.Max(1, scene.Beats);
            if (!PossessionDeck.FitsConcurrency(LiveSlots, slots, rung))
            {
                App.Logger?.Debug("Possession scene {Scene} ({Slots} slots) does not fit at rung {Rung} (live {Live}/{Max})",
                    scene.Id, slots, rung, LiveSlots, PossessionDeck.MaxLive(rung));
                return false;
            }

            // The scene books its own victims through this callback so the director keeps ownership of
            // the ledger: everything it claims is marked live here and released (with a cooldown) when
            // the scene ends, exactly like a single effect's victim.
            var claimed = new List<PossessionTarget>();
            PossessionTarget? Picker(PossessionRole role)
            {
                try
                {
                    var now = DateTime.Now;
                    var pool = SnapshotTargets(host, now, out _);
                    var matches = new List<PossessionTarget>();
                    foreach (var t in pool)
                    {
                        if (t.Role != role || t.IsLive) continue;
                        if (_liveKeys.Contains(t.Key)) continue;
                        if (t.CooldownUntil > now) continue;
                        if (_cooldowns.TryGetValue(t.Key, out var until) && until > now) continue;
                        if (claimed.Contains(t)) continue;
                        matches.Add(t);
                    }
                    if (matches.Count == 0) return null;

                    // Nearest the cursor wins when we know where it is - the same reason A5 exists.
                    var origin = PossessionPointer.Position;
                    if (origin.X > 0 || origin.Y > 0)
                    {
                        matches.Sort((a, b) => Dist(a).CompareTo(Dist(b)));
                        double Dist(PossessionTarget t)
                        {
                            try
                            {
                                var r = Effects.PossessionVisual.BoundsOf(host.Host, t.Element);
                                if (r.IsEmpty) return double.MaxValue;
                                var dx = r.X + r.Width / 2 - origin.X;
                                var dy = r.Y + r.Height / 2 - origin.Y;
                                return dx * dx + dy * dy;
                            }
                            catch { return double.MaxValue; }
                        }
                    }

                    var chosen = matches[0];
                    chosen.IsLive = true;
                    _liveKeys.Add(chosen.Key);
                    claimed.Add(chosen);
                    return chosen;
                }
                catch { return null; }
            }

            var adapter = new PossessionSceneEffect(scene, Picker);
            var cts = new CancellationTokenSource();
            ghost = new LiveGhost(adapter, null, host, cts) { Slots = slots };
            ghost.OnRelease = () =>
            {
                var until = DateTime.Now + PossessionDeck.TargetCooldown;
                foreach (var t in claimed)
                {
                    try
                    {
                        t.IsLive = false;
                        t.CooldownUntil = until;
                        _cooldowns[t.Key] = until;
                        _liveKeys.Remove(t.Key);
                    }
                    catch { }
                }
                claimed.Clear();
            };
            _live.Add(ghost);

            App.Logger?.Information("Possession scene: {Scene} at rung {Rung} (live slots {Slots})",
                scene.Id, rung, LiveSlots);
            try { EffectStarted?.Invoke(scene.Id, null, true); } catch { }

            var ctx = BuildContext(host, rung, remaining, frac, () => adapter);
            try { await adapter.ApplyAsync(ctx, null, cts.Token).ConfigureAwait(true); }
            catch (OperationCanceledException) { }

            if (adapter.HoldFor > TimeSpan.Zero) FireAndForget(HoldThenUndoAsync(ghost, adapter.HoldFor), "scene hold");
            else await UndoGhostAsync(ghost, TimeSpan.FromMilliseconds(600)).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession scene start failed: {Error}", ex.Message);
            if (ghost != null) { try { await UndoGhostAsync(ghost, TimeSpan.Zero).ConfigureAwait(true); } catch { } }
            return true;   // the beat was spent either way; do not immediately try a second thing
        }
    }

    // ---------------------------------------------------------------------------------------------
    //  Event-driven ghosts (B15)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// "The room answers what you just did." Called by <see cref="PossessionEvents"/> when the user
    /// clicks a card, changes a setting, hovers Stop, or opens a door. Heavily throttled and rung-gated:
    /// a reaction the user can predict stops being uncanny, and one that fires on every event is a
    /// strobe. Silently does nothing when it is not the moment - callers never have to check.
    /// </summary>
    public void RequestReactive(string effectId, PossessionTarget? target, PossessionRung minRung = PossessionRung.Settle)
    {
        if (_disposed || !IsHaunting || string.IsNullOrEmpty(effectId)) return;

        // Safe from any thread. Most callers are WPF input handlers, but the SettingChanged reaction
        // rides AppSettings.PropertyChanged, which fires on whatever thread wrote the setter, and the
        // body below mutates the live ledger and walks the visual tree. RunOnUI executes inline when
        // we are already on the UI thread, so the input path is unchanged.
        DispatcherHelper.RunOnUI(() => RequestReactiveCore(effectId, target, minRung));
    }

    private void RequestReactiveCore(string effectId, PossessionTarget? target, PossessionRung minRung)
    {
        // Re-checked on the UI thread: a queued request can land after the lockdown has ended.
        if (_disposed || !IsHaunting) return;
        try
        {
            var rung = CurrentRung;
            if (rung < minRung) return;

            var now = DateTime.Now;
            if (now - _lastReactiveAt < ReactiveThrottle) return;
            if (!PossessionDeck.FitsConcurrency(LiveSlots, 1, rung)) return;

            var host = _hosts.FirstOrDefault(h => SafeIsUsable(h.Host));
            if (host == null) return;

            var effect = _effects.FirstOrDefault(e => string.Equals(e.Id, effectId, StringComparison.OrdinalIgnoreCase));
            if (effect == null || effect.IsLive) return;
            if (rung < effect.MinRung) return;
            if ((int)effect.MinIntensity > (int)_intensity) return;
            if (_photosafe && effect.UsesFlicker) return;

            if (target != null)
            {
                if (target.IsLive || _liveKeys.Contains(target.Key)) return;
                if (target.CooldownUntil > now) return;
                if (_cooldowns.TryGetValue(target.Key, out var until) && until > now) return;
            }

            var ctx = BuildContext(host, rung, _lockdown.Remaining, _lockdown.ElapsedFraction, () => effect);
            if (!effect.CanApply(ctx, target)) return;

            _lastReactiveAt = now;
            if (target != null)
            {
                target.IsLive = true;
                _liveKeys.Add(target.Key);
                _lastTargetKey = target.Key;
            }

            var cts = new CancellationTokenSource();
            var ghost = new LiveGhost(effect, target, host, cts);
            _live.Add(ghost);

            App.Logger?.Information("Possession reactive: {Effect} on {Target} at rung {Rung}",
                effect.Id, target?.Key ?? "(window)", rung);
            try { EffectStarted?.Invoke(effect.Id, target?.Key, effect.IsBig); } catch { }

            FireAndForget(RunReactiveAsync(ghost, ctx, effect, target, cts), "reactive");
        }
        catch (Exception ex) { App.Logger?.Warning("Possession reactive request failed: {Error}", ex.Message); }
    }

    private async Task RunReactiveAsync(LiveGhost ghost, PossessionContext ctx, IPossessionEffect effect,
                                        PossessionTarget? target, CancellationTokenSource cts)
    {
        try
        {
            await effect.ApplyAsync(ctx, target, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession reactive {Effect} failed: {Error}", effect.Id, ex.Message);
            await UndoGhostAsync(ghost, TimeSpan.Zero).ConfigureAwait(true);
            return;
        }

        if (effect.HoldFor > TimeSpan.Zero)
            await HoldThenUndoAsync(ghost, effect.HoldFor).ConfigureAwait(true);
    }

    /// <summary>Find the registered target for an element, or for the nearest ancestor that has one.
    /// The reactive layer gets raw hit-test results; a click on a card lands on the TextBlock inside it.</summary>
    public PossessionTarget? TargetFor(DependencyObject? element, PossessionRole? role = null)
    {
        try
        {
            var host = _hosts.FirstOrDefault();
            if (host == null || element == null) return null;
            IReadOnlyList<PossessionTarget> targets;
            try { targets = host.Host.Targets ?? Array.Empty<PossessionTarget>(); }
            catch { return null; }

            var node = element;
            for (int depth = 0; node != null && depth < 24; depth++)
            {
                foreach (var t in targets)
                {
                    if (!ReferenceEquals(t.Element, node)) continue;
                    if (role.HasValue && t.Role != role.Value) break;   // keep climbing for the asked role
                    return t;
                }
                node = node is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>The registered target of a role that sits nearest the cursor right now (the label the
    /// user was reading when they flipped a setting).</summary>
    public PossessionTarget? NearestTarget(PossessionRole role)
    {
        try
        {
            var host = _hosts.FirstOrDefault();
            if (host == null) return null;
            IReadOnlyList<PossessionTarget> targets;
            try { targets = host.Host.Targets ?? Array.Empty<PossessionTarget>(); }
            catch { return null; }

            var origin = PossessionPointer.Position;
            PossessionTarget? best = null;
            double bestDist = double.MaxValue;
            foreach (var t in targets)
            {
                if (t.Role != role || t.IsLive) continue;
                try
                {
                    var r = Effects.PossessionVisual.BoundsOf(host.Host, t.Element);
                    if (r.IsEmpty) continue;
                    var dx = r.X + r.Width / 2 - origin.X;
                    var dy = r.Y + r.Height / 2 - origin.Y;
                    var d = dx * dx + dy * dy;
                    if (d < bestDist) { bestDist = d; best = t; }
                }
                catch { }
            }
            return best;
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------------------------------------
    //  Timer restart (Emergency Exit sent them back in)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The lockdown clock was rewound to its FULL duration (EMERGENCY_EXIT.md "sendback"). The elapsed
    /// fraction is back near zero, so the ladder has to go back to Settle with it - otherwise the room
    /// would keep collapsing over a timer that just started, which reads as the restart having failed.
    ///
    /// <para>The room reassembles quickly (about a second, not the three of a real exit) and then gets
    /// a full first-wait of quiet: the punchline of being sent back is that it all starts again, and
    /// that only lands if there is a silence to notice.</para>
    /// </summary>
    private void OnTimerRestarted(string reason)
    {
        if (!IsHaunting || _disposed) return;
        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                var frac = _lockdown.ElapsedFraction;
                _lastRestartAt = DateTime.Now;
                ApplyRestartReset(PossessionDeck.RungFor(frac, _intensity));

                FireAndForget(QuickUndoAsync(), "restart undo");
                PulseAll(0.6);
                BarkTimerRestarted(reason, SafeRestartCount());

                _nextDue = DateTime.Now + PossessionDeck.FirstDelay(CurrentRung, _intensity, _rng);
                try { RungChanged?.Invoke(CurrentRung); } catch { }

                App.Logger?.Information("Possession: timer restarted ({Reason}), rung reset to {Rung}, next haunt in {Sec:F0}s",
                    reason, CurrentRung, (_nextDue - DateTime.Now).TotalSeconds);
            }
            catch (Exception ex) { App.Logger?.Warning("Possession timer-restart handling failed: {Error}", ex.Message); }
        });
    }

    private int SafeRestartCount()
    {
        try { return _lockdown.RestartCount; } catch { return 0; }
    }

    /// <summary>
    /// Everything the ladder has to FORGET when the clock is rewound to its full duration. The room is
    /// claiming to start over, so anything that contradicts that goes with it: the rung, the rung
    /// barks, the last victim, the reactive floor, and every per-target cooldown - 45 s of banned
    /// controls is a memory of a lockdown that supposedly just began.
    ///
    /// <para>The rung the restart just set is added straight back to <c>_barkedRungs</c>: a
    /// <c>CountdownTick</c> from the same rewind sees the new elapsed fraction and would otherwise
    /// announce that rung a second time (the spurious Settle bark). That also makes this idempotent -
    /// it does not matter whether the tick or the restart arrives first, or how often it is called.</para>
    /// </summary>
    internal void ApplyRestartReset(PossessionRung rung)
    {
        CurrentRung = rung;

        _barkedRungs.Clear();
        _barkedRungs.Add(rung);

        _lastTargetKey = null;
        _lastReactiveAt = DateTime.MinValue;

        // A pick still in flight owns this flag through StartOneAsync's finally and will clear it
        // again in a moment; one that died with a torn-down window never does, and the cadence loop
        // stays shut for the rest of the lockdown. The restart hands out a full FirstDelay of quiet
        // straight after this, so nothing can slip a second pick in behind a live one.
        _picking = false;

        _cooldowns.Clear();
        foreach (var h in _hosts)
        {
            IReadOnlyList<PossessionTarget> targets;
            try { targets = h.Host.Targets ?? Array.Empty<PossessionTarget>(); }
            catch { continue; }
            foreach (var t in targets)
            {
                // A victim that is still possessed keeps its booking; Release() gives it a fresh
                // cooldown when its ghost finally comes down.
                try { if (t != null && !t.IsLive) t.CooldownUntil = DateTime.MinValue; } catch { }
            }
        }
    }

    /// <summary>Rungs whose one-per-rung bark has already been spent. Exposed for the restart tests,
    /// which pin that a rewind re-arms the ladder WITHOUT letting the next tick re-announce the rung
    /// the rewind itself just set.</summary>
    internal IReadOnlyCollection<PossessionRung> AnnouncedRungs => _barkedRungs;

    /// <summary>The reassembly path, but quick: everything comes back over about a second, in parallel
    /// rather than staggered, because this is a reset and not a curtain call.</summary>
    private async Task QuickUndoAsync()
    {
        try
        {
            var ghosts = _live.ToArray();
            Array.Reverse(ghosts);
            foreach (var g in ghosts)
            {
                try { await UndoGhostAsync(g, TimeSpan.FromMilliseconds(500)).ConfigureAwait(true); }
                catch (Exception ex) { App.Logger?.Warning("Possession restart undo step failed: {Error}", ex.Message); }
            }

            // No Clear() here. Release() already took every ghost in the snapshot off the books, and
            // IsHaunting stays TRUE across a restart - so the loop above is a ~2 s window in which the
            // reactive layer can add a brand new ghost. Wiping the ledger would drop it without ever
            // undoing it: its control stays possessed forever, the shared catalog effect's IsLive
            // stays true so CanApply refuses it for the rest of the session, and a HoldFor==Zero
            // effect (RoomWarp) never runs its Undo at all, leaking its event handlers with it.
        }
        catch (Exception ex) { App.Logger?.Warning("Possession restart undo failed: {Error}", ex.Message); }
    }

    private static void BarkTimerRestarted(string reason, int restart)
    {
        try { App.Bark?.NotifyPossessionTimerRestarted(reason ?? "", restart); }
        catch (Exception ex) { App.Logger?.Debug("Possession: timer-restart bark failed: {Error}", ex.Message); }
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
        try { ghost.OnRelease?.Invoke(); } catch { }

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
        // Captured before the first await: the whole tail below belongs to THIS lockdown, and the
        // three seconds of undo plus up to fifteen of warden are long enough for the next one to start.
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
                catch (Exception ex) { App.Logger?.Warning("Possession reassembly step failed: {Error}", ex.Message); }
            }

            // No Clear() here either: Release() took every ghost in the snapshot off the books, so
            // anything still on them was added underneath us and belongs to a NEW lockdown.
            if (!ShouldFinishReassembly(generation, _generation, IsHaunting))
            {
                App.Logger?.Debug("Possession: reassembly tail skipped, a new lockdown started underneath it");
                return;
            }

            foreach (var h in _hosts) { try { h.Attribution.ReleaseAll(); } catch { } }

            if (_wardenEnabled && Warden != null)
                await RunWardenAsync(ct => Warden.ReturnAsync(ct), "return").ConfigureAwait(true);

            // The warden's return is up to 15 s of awaiting on its own, so ask again before stamping
            // the rung: a lockdown that started inside it owns the readout now.
            if (!ShouldFinishReassembly(generation, _generation, IsHaunting)) return;

            CurrentRung = PossessionRung.Settle;
            try { RungChanged?.Invoke(CurrentRung); } catch { }
            App.Logger?.Information("Possession: reassembled, room is quiet");
        }
        catch (Exception ex) { App.Logger?.Warning("Possession reassembly failed: {Error}", ex.Message); }
    }

    /// <summary>Is the reassembly that started at <paramref name="startGeneration"/> still the room's
    /// current business? Every activation bumps the generation and turns the haunt back on, and the
    /// reassembly tail is destructive: it strips every ember outline and the cursor ring off every
    /// host and forces the rung display back to Settle. Run against a lockdown that started inside
    /// those three seconds it would silently un-dress a room that is actively haunting.
    /// Pure so the invariant can be pinned in PossessionDeckTests.</summary>
    internal static bool ShouldFinishReassembly(int startGeneration, int currentGeneration, bool haunting)
        => startGeneration == currentGeneration && !haunting;

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
            try { TripwireReacted?.Invoke(attempt); } catch { }
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

    /// <summary>Window-edge ember pulse on every attached host, for OTHER lockdown layers that did
    /// something the room should own (the Dose keeper switching a feature back on). No-op unless
    /// haunting - when Possession is off the warden line alone carries the attribution.</summary>
    public void PulseEdges(double strength)
    {
        if (_disposed || !IsHaunting) return;
        try { PulseAll(Math.Clamp(strength, 0.0, 1.0)); } catch { }
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
            _lockdown.TimerRestarted -= OnTimerRestarted;
        }
        catch { }
        try { _events.DetachAll(); } catch { }
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
        /// <summary>Concurrency weight (a scene is worth its beats).</summary>
        public int Slots { get; init; } = 1;
        /// <summary>Extra bookkeeping to unwind when this ghost is released (a scene's claimed victims).</summary>
        public Action? OnRelease { get; set; }

        public void Dispose() { try { Cts.Dispose(); } catch { } }
    }
}
