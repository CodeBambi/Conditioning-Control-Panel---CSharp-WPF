using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>
/// The primitive calls, one per contract primitive. The real one (<c>BackRoomFxServices</c>)
/// maps these onto the app's existing overlay services and the Hypno v3 overlays; tests use a
/// recorder. Every call arrives on the scheduler's thread (the UI dispatcher in the app).
/// </summary>
public interface IBackRoomFxSink
{
    void FlashBurst(int amount);
    void GifRain(int durationMs);
    void GlitchWash(int durationMs, double opacity);
    void Subliminal(string text);
    /// <summary><paramref name="level"/> is a fraction of the user's own brain drain intensity.</summary>
    void BrainDrain(int durationMs, double level, bool melt);
    // The picture primitives take `shown`: call it (on the UI thread) when the picture is actually on screen, never for
    // a picture that is refused (no local file, a display change settling), fails to decode or is stopped first.
    // The XP award keys off it (10.14).
    void GifFull(BackRoomGif gif, int durationMs, bool still, Action shown);
    // Hypno v3 (10.13.B). GifFrom's rect is page CSS px, mapped at play time (null = the centre).
    /// <summary><paramref name="shown"/> runs only for a wash WITH a picture, once that picture is on.</summary>
    void Wash(FxRgb color, double peak, BackRoomGif? picture, Action shown);
    /// <returns>False when nothing will show (no local file), so the one-at-a-time slot frees at once.</returns>
    bool GifFrom(BackRoomGif gif, FxCssRect? from, int durationMs, double scale, double dim, bool still, Action shown);
    /// <summary>Replaces a running one; a hold's <paramref name="durationMs"/> is the 20 s cap.</summary>
    void SpiralLoom(string gifPath, int durationMs, double alpha, bool hold, bool still);
    void ReleaseSpiralLoom();
    void ReleaseBrainDrain();
    /// <summary>The wanted level; the overlay eases toward it and lets go 1500 ms after the last call.</summary>
    void Tunnel(double level, bool still);
    void CancelTunnel();
    /// <summary>Stop everything this sink started.</summary>
    void StopAll();
}

/// <summary>A clock plus a one-shot timer. The app's runs on the UI dispatcher.</summary>
public interface IFxScheduler
{
    long NowMs { get; }
    IDisposable After(int delayMs, Action action);
}

/// <summary>The settings a fire reads, captured once per fire.</summary>
/// <param name="SpiralSource">Preset -> the Loom-woven spiral file (<see cref="BackRoomSpiralSource"/>); null = none.</param>
/// <param name="SpiralOpacity">The user's own spiral opacity 0..1, which section 4's spiral-full scales.</param>
public sealed record FxEnvironment(MotionLevel Motion, BackRoomFxIntensity Intensity, FxGates Gates,
    Func<string, string?>? SpiralSource = null, double SpiralOpacity = 0.85);

/// <summary>
/// XP for what the Back Room shows (CONTRACT 10.14: Back Room effects award XP like normal app effects), on the app's
/// own award path. In normal play only two of the room's effects pay, and both keep paying from inside their service:
/// a flash image (FlashService, 4 base XP per image shown without its sound, times the lucky flash roll, XPSource.Flash)
/// and a subliminal (SubliminalService.FlashSubliminalCustom, 10 XP, XPSource.Subliminal). So flash-burst and the
/// sub primitives are NOT paid here (no double count). The Hypno pictures that bypass FlashService are one flash
/// image each: gif-full, gif-from and a wash with a picture. Spirals, Brain Drain (melt, haze), tunnel vision, the
/// gif rain and the glitch wash pay nothing in normal play, so nothing here either. Idle suppression, the skill
/// multiplier and the login gate all live inside ProgressionService.AddXP.
/// </summary>
public static class BackRoomFxXp
{
    /// <summary>FlashService's base per image with no flash sound playing (the room's pictures never play one).</summary>
    public const int PictureXp = 4;

    /// <summary>Base XP the dispatcher pays once a primitive has actually shown (the sink's <c>shown</c>), or null when it pays nothing itself.</summary>
    public static int? For(FxPrim prim) => prim is FxPrim.GifFull or FxPrim.GifFrom or FxPrim.Wash ? PictureXp : null;
}

/// <summary>
/// The effect dispatcher (C3, H1). Resolves an fx id through <see cref="BackRoomFxPlan"/>, admits it
/// through the hero gate, paces every repeated onset under the 6 Hz ceiling, and schedules the
/// primitives on the sink. The ack goes back synchronously: what will play, and every skip.
/// Hypno v3 (10.13.B) adds the two Brake gaps that DROP instead of delaying (a wash inside 360 ms, a
/// second gif-from while one shows), holds released by token or by station, and the tunnel feed.
/// </summary>
public sealed class BackRoomFx : IBackRoomFx
{
    /// <summary>At most 10 tunnel updates a second reach the sink; a later one in the window replaces the earlier.</summary>
    public const int TunnelGapMs = 100;

    private readonly IBackRoomFxSink _sink;
    private readonly IFxScheduler _scheduler;
    private readonly Func<FxEnvironment> _env;
    private readonly Action<int>? _xp;
    private readonly Random _rng;
    private readonly FxHeroGate _gate;
    private readonly FxStrobePacer _pacer = new();
    private readonly object _lock = new();
    private readonly HashSet<PendingOnset> _pending = new();
    // Every token that still has onsets waiting or owns what is on screen, by token.
    private readonly Dictionary<string, Hold> _holds = new(StringComparer.Ordinal);
    // The hold of the last copy of each fxId queued behind a hero: a same-id fire merged into it shares it.
    private readonly Dictionary<string, Hold> _queuedHolds = new(StringComparer.Ordinal);
    private string? _spiralToken, _hazeToken, _tunnelStation;
    private long _gifFromUntil = long.MinValue;
    private long _tunnelAppliedAt = long.MinValue / 2;
    private (double Level, bool Still)? _tunnelNext;
    private IDisposable? _tunnelTimer;

    /// <param name="xp">Pays a primitive's base XP (<see cref="BackRoomFxXp"/>) once it has shown. Null = no XP (tests, rig).</param>
    public BackRoomFx(IBackRoomFxSink sink, IFxScheduler scheduler, Func<FxEnvironment> env, Random? rng = null, Action<int>? xp = null)
    {
        _sink = sink;
        _scheduler = scheduler;
        _env = env;
        _xp = xp;
        _rng = rng ?? new Random();
        _gate = new FxHeroGate(() => _scheduler.NowMs);
    }

    internal FxHeroGate Gate => _gate;

    /// <summary>Primitive onsets still waiting to run (tests, diagnostics).</summary>
    public int PendingCount { get { lock (_lock) return _pending.Count; } }

    /// <summary>Tokens still tracked (tests: holds must not pile up over a long session).</summary>
    internal int HoldCount { get { lock (_lock) return _holds.Count; } }

    public BackRoomFxAck Fire(string fxId, string station, IReadOnlyList<string> symbolKeys, BackRoomMediaDeal deal)
        => Fire(fxId, symbolKeys, deal, _env());

    public BackRoomFxAck Fire(string fxId, string station, IReadOnlyList<string> symbolKeys, BackRoomMediaDeal deal,
        BackRoomFxArgs? args, string? token)
        => Fire(fxId, symbolKeys, deal, _env(), args, token, station);

    /// <summary>Fire under an explicit environment. The room never calls this; the dev rig does, so a
    /// desk run can walk every intensity without touching the user's settings.</summary>
    internal BackRoomFxAck Fire(string fxId, IReadOnlyList<string>? symbolKeys, BackRoomMediaDeal? deal, FxEnvironment env,
        BackRoomFxArgs? args = null, string? token = null, string? station = null)
    {
        fxId ??= string.Empty;
        try
        {
            FxPlan plan;
            lock (_rng) plan = BackRoomFxPlan.Resolve(fxId, env.Intensity, env.Motion, env.Gates, symbolKeys, deal, _rng,
                args, env.SpiralSource);
            if (plan.Steps.Count == 0) return new BackRoomFxAck(Array.Empty<string>(), plan.Skipped);

            var admission = _gate.Admit(fxId, plan.HeroMs);
            switch (admission.Kind)
            {
                case FxAdmitKind.Busy:
                    var busy = plan.Skipped.Concat(plan.Fired.Distinct()
                        .Select(p => new BackRoomFxSkip(p, BackRoomFxSkipReason.Busy))).ToList();
                    return new BackRoomFxAck(Array.Empty<string>(), busy);
                case FxAdmitKind.Merged:
                    // It plays once, as the copy already on stage or in the queue; its token shares that copy's
                    // hold, so its own fx-release still lets go instead of waiting out the 20 s cap.
                    if (!string.IsNullOrEmpty(token))
                        lock (_lock)
                            if (_queuedHolds.TryGetValue(fxId, out var into) && into.Tokens.Count > 0 && into.Station == (station ?? string.Empty))
                            {
                                into.Tokens.Add(token);
                                _holds[token] = into;
                            }
                    return new BackRoomFxAck(plan.Fired, plan.Skipped);
            }

            long baseAt = _scheduler.NowMs + admission.DelayMs;
            var skipped = plan.Skipped.ToList();
            var kept = new List<FxPlannedStep>(plan.Steps.Count);
            Hold? hold = null;
            lock (_lock)
            {
                foreach (var planned in plan.Steps)
                {
                    long at = baseAt + planned.Step.AtMs;
                    // The mockup drops these, it does not delay them (Law 5, one gif-from on screen).
                    if (planned.Step.Prim == FxPrim.Wash && !_pacer.TryReserve(FxChannel.Wash, at))
                    {
                        skipped.Add(new BackRoomFxSkip(BackRoomFxPlan.WireName(FxPrim.Wash), BackRoomFxSkipReason.Busy));
                        continue;
                    }
                    if (planned.Step.Prim == FxPrim.GifFrom)
                    {
                        if (at < _gifFromUntil)
                        {
                            skipped.Add(new BackRoomFxSkip(BackRoomFxPlan.WireName(FxPrim.GifFrom), BackRoomFxSkipReason.Busy));
                            continue;
                        }
                        _gifFromUntil = at + planned.Step.DurationMs;
                    }
                    kept.Add(planned);
                }
                if (kept.Count > 0 && !string.IsNullOrEmpty(token))
                {
                    PruneHolds();
                    if (!_holds.TryGetValue(token, out hold)) _holds[token] = hold = new Hold(token, station ?? string.Empty);
                    if (admission.Kind == FxAdmitKind.Queued) _queuedHolds[fxId] = hold;
                }
            }

            foreach (var planned in kept) Schedule(planned, baseAt, env, hold);
            return new BackRoomFxAck(kept.Select(s => BackRoomFxPlan.WireName(s.Step.Prim)).ToList(), skipped);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "[BackRoom] fx fire failed ({FxId})", fxId);
            return new BackRoomFxAck(Array.Empty<string>(), new[] { new BackRoomFxSkip(fxId, BackRoomFxSkipReason.Unknown) });
        }
    }

    private void Schedule(FxPlannedStep planned, long baseAt, FxEnvironment env, Hold? hold)
    {
        var s = planned.Step;
        long at = baseAt + s.AtMs;
        switch (s.Prim)
        {
            case FxPrim.SubSingle:
            case FxPrim.SubSeq:
            case FxPrim.SubBurst9:
                for (int i = 0; i < planned.Words.Count; i++)
                {
                    var word = planned.Words[i];
                    At(_pacer.Reserve(FxChannel.Subliminal, at + i * BackRoomFxPlan.WordGapMs), () => _sink.Subliminal(word), hold);
                }
                break;
            case FxPrim.GlitchBubbles:
                for (int i = 0; i < Math.Max(1, s.Count); i++)
                    At(_pacer.Reserve(FxChannel.Glitch, at + i * BackRoomFxPlan.GlitchWashMs),
                        () => _sink.GlitchWash(BackRoomFxPlan.GlitchWashMs, BackRoomFxPlan.GlitchOpacity), hold);
                break;
            case FxPrim.FlashBurst:
                int images = Math.Max(1, s.Count);
                At(_pacer.Reserve(FxChannel.Flash, at, (images - 1) * BackRoomFxPlan.FlashImageGapMs), () => _sink.FlashBurst(images), hold);
                break;
            case FxPrim.GifRain:
                At(at, () => _sink.GifRain(s.DurationMs), hold);
                break;
            case FxPrim.SpiralFull:
                // Section 4's spiral now plays the screen weave (10.13.B), at the user's opacity x the recipe level.
                double opacity = Math.Clamp(env.SpiralOpacity * s.Level, 0.02, 1.0);
                At(at, () => { Own(ref _spiralToken, hold); _sink.SpiralLoom(planned.SpiralPath!, s.DurationMs, opacity, false, s.Still); }, hold);
                break;
            case FxPrim.SpiralLoom:
                At(at, () => { Own(ref _spiralToken, hold); _sink.SpiralLoom(planned.SpiralPath!, s.DurationMs, s.Level, s.Look?.Hold == true, s.Still); }, hold);
                break;
            case FxPrim.BrainDrainMelt:
            case FxPrim.BrainDrain:
                // A melt takes the drain surface over, so a later haze release must not end it.
                At(at, () => { Own(ref _hazeToken, null); _sink.BrainDrain(s.DurationMs, s.Level, s.Prim == FxPrim.BrainDrainMelt); }, hold);
                break;
            case FxPrim.Haze:
                At(at, () => { Own(ref _hazeToken, hold); _sink.BrainDrain(s.DurationMs, s.Level, false); }, hold);
                break;
            case FxPrim.GifFull:
                var gif = planned.Gif!;
                At(at, () => _sink.GifFull(gif, s.DurationMs, s.Still, PayOnce(s.Prim)), hold);
                break;
            case FxPrim.Wash:
                At(at, () => _sink.Wash(s.Look!.Color, s.Level, planned.Gif, PayOnce(s.Prim)), hold);
                break;
            case FxPrim.GifFrom:
                var grown = planned.Gif!;
                long until = at + s.DurationMs;
                At(at, () => { if (!_sink.GifFrom(grown, s.Look!.From, s.DurationMs, s.Look.Scale, s.Level, s.Still, PayOnce(s.Prim))) FreeGifFrom(until); }, hold, until);
                break;
        }
    }

    /// <summary>One shown picture, one award (10.14): the sink's <c>shown</c> for one onset. Made inside the onset, so a
    /// cancelled or busy one never pays, and it pays at most once however often it is called.</summary>
    private Action PayOnce(FxPrim prim)
    {
        int paid = 0;
        return () =>
        {
            if (System.Threading.Interlocked.Exchange(ref paid, 1) != 0) return;
            if (_xp == null || BackRoomFxXp.For(prim) is not { } amount) return;
            try { _xp(amount); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[BackRoom] fx xp failed"); }
        };
    }

    private void Own(ref string? owner, Hold? hold)
    {
        lock (_lock) owner = hold?.Token;
    }

    /// <summary>A gif-from that will not show frees the one-at-a-time slot it reserved (unless a later one took it).</summary>
    private void FreeGifFrom(long until)
    {
        lock (_lock) if (_gifFromUntil == until) _gifFromUntil = long.MinValue;
    }

    private sealed class PendingOnset { public IDisposable? Timer; public Hold? Hold; public long GifFromUntil; }

    private sealed class Hold
    {
        public Hold(string token, string station) { Token = token; Station = station; Tokens.Add(token); }
        /// <summary>The token that fired it (what the spiral and haze owners name).</summary>
        public string Token { get; }
        public string Station { get; }
        public readonly List<PendingOnset> Onsets = new();
        /// <summary>Every token not yet released that holds it: its own plus any merged into it.</summary>
        public readonly HashSet<string> Tokens = new(StringComparer.Ordinal);
    }

    private void At(long atMs, Action action, Hold? hold, long gifFromUntil = 0)
    {
        int delay = (int)Math.Max(0, atMs - _scheduler.NowMs);
        // The token is pending BEFORE the timer exists, so a timer that fires early (or a scheduler
        // that runs inline) always finds it, and a CancelAll that wins the race always silences it.
        var onset = new PendingOnset { Hold = hold, GifFromUntil = gifFromUntil };
        lock (_lock) { _pending.Add(onset); hold?.Onsets.Add(onset); }
        onset.Timer = _scheduler.After(delay, () =>
        {
            lock (_lock)
            {
                if (!_pending.Remove(onset)) return;
                onset.Hold?.Onsets.Remove(onset);
            }
            try { action(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[BackRoom] fx primitive failed"); }
        });
    }

    /// <summary>A token owns nothing once its onsets ran and neither the spiral nor the haze is its own.</summary>
    private void PruneHolds()
    {
        if (_holds.Count < 16) return;
        foreach (var dead in _holds.Values.Distinct().Where(h => h.Onsets.Count == 0 && h.Token != _spiralToken && h.Token != _hazeToken).ToList())
        {
            foreach (var t in dead.Tokens) _holds.Remove(t);
            dead.Tokens.Clear();
        }
    }

    // ============================ holds (10.13.B) ============================

    /// <summary><c>fx-release</c>: whatever <paramref name="token"/> still holds fades out, and its onsets that
    /// have not started never will. A token from another station is ignored.</summary>
    public void Release(string token, string station)
    {
        if (string.IsNullOrEmpty(token)) return;
        List<PendingOnset> doomed;
        bool spiral, haze;
        lock (_lock)
        {
            if (!_holds.TryGetValue(token, out var hold) || hold.Station != (station ?? string.Empty)) return;
            _holds.Remove(token);
            hold.Tokens.Remove(token);
            if (hold.Tokens.Count > 0) return;   // a token merged into the same play still holds it
            doomed = hold.Onsets.ToList();
            foreach (var o in doomed) _pending.Remove(o);
            // A gif-from that never started must not keep later ones busy for its whole length.
            if (doomed.Any(o => o.GifFromUntil != 0 && o.GifFromUntil == _gifFromUntil)) _gifFromUntil = long.MinValue;
            spiral = _spiralToken == hold.Token;
            haze = _hazeToken == hold.Token;
            if (spiral) _spiralToken = null;
            if (haze) _hazeToken = null;
        }
        Dispose(doomed);
        try
        {
            if (spiral) _sink.ReleaseSpiralLoom();
            if (haze) _sink.ReleaseBrainDrain();
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "[BackRoom] fx release failed"); }
    }

    /// <summary><c>station-close</c>: release every hold that station started and cancel its tunnel.</summary>
    public void ReleaseStation(string station)
    {
        List<string> tokens;
        bool tunnel;
        lock (_lock)
        {
            tokens = _holds.Where(kv => kv.Value.Station == station).Select(kv => kv.Key).ToList();
            tunnel = _tunnelStation != null && _tunnelStation == station;
        }
        foreach (var t in tokens) Release(t, station);
        if (tunnel) CancelTunnel();
    }

    // ============================ tunnel (10.13.B) ============================

    /// <summary><c>fx-tunnel</c>: gated by the room's tunnel switch (10.14, not Brain Drain), halved under Calm, at most 10 a second.</summary>
    public void Tunnel(string station, double level)
    {
        if (!double.IsFinite(level)) return;
        FxEnvironment env;
        try { env = _env(); }
        catch (Exception ex) { App.Logger?.Warning(ex, "[BackRoom] fx environment failed"); return; }
        if (!env.Gates.Tunnel)
        {
            // The switch went off under a running tunnel: gone now, not after the 1500 ms self-release.
            bool live;
            lock (_lock) live = _tunnelStation != null;
            if (live) CancelTunnel();
            return;
        }

        bool calm = BackRoomFxPlan.EffectiveIntensity(env.Intensity, env.Motion) == BackRoomFxIntensity.Calm;
        var next = (Math.Clamp(level, 0, 1) * (calm ? BackRoomFxPlan.CalmStrength : 1), env.Motion == MotionLevel.Off);
        bool now = false;
        lock (_lock)
        {
            _tunnelStation = station;
            long t = _scheduler.NowMs;
            if (_tunnelTimer == null && t - _tunnelAppliedAt >= TunnelGapMs)
            {
                _tunnelAppliedAt = t;
                now = true;
            }
            else
            {
                _tunnelNext = next;
                _tunnelTimer ??= _scheduler.After((int)Math.Max(0, _tunnelAppliedAt + TunnelGapMs - t), FlushTunnel);
            }
        }
        if (now) SinkTunnel(next.Item1, next.Item2);
    }

    private void FlushTunnel()
    {
        (double Level, bool Still)? next;
        lock (_lock)
        {
            if (_tunnelTimer == null) return;   // cancelled
            _tunnelTimer = null;
            next = _tunnelNext;
            _tunnelNext = null;
            _tunnelAppliedAt = _scheduler.NowMs;
        }
        if (next is { } n) SinkTunnel(n.Level, n.Still);
    }

    private void SinkTunnel(double level, bool still)
    {
        try { _sink.Tunnel(level, still); }
        catch (Exception ex) { App.Logger?.Warning(ex, "[BackRoom] fx tunnel failed"); }
    }

    private void CancelTunnel()
    {
        IDisposable? timer;
        lock (_lock)
        {
            timer = _tunnelTimer;
            _tunnelTimer = null;
            _tunnelNext = null;
            _tunnelStation = null;
        }
        try { timer?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex, "tunnel timer already gone"); }
        try { _sink.CancelTunnel(); } catch (Exception ex) { App.Logger?.Warning(ex, "[BackRoom] fx tunnel cancel failed"); }
    }

    /// <summary>Suspend or close: drop every waiting onset, forget the hero windows and holds, stop what is up.</summary>
    public void CancelAll()
    {
        List<PendingOnset> doomed;
        IDisposable? tunnel;
        lock (_lock)
        {
            doomed = _pending.ToList();
            _pending.Clear();
            _holds.Clear();
            _queuedHolds.Clear();
            _spiralToken = _hazeToken = _tunnelStation = null;
            _gifFromUntil = long.MinValue;
            tunnel = _tunnelTimer;
            _tunnelTimer = null;
            _tunnelNext = null;
        }
        Dispose(doomed);
        try { tunnel?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex, "tunnel timer already gone"); }
        _gate.Clear();
        _pacer.Clear();
        try { _sink.StopAll(); }
        catch (Exception ex) { App.Logger?.Warning(ex, "[BackRoom] fx stop failed"); }
    }

    private static void Dispose(IEnumerable<PendingOnset> onsets)
    {
        foreach (var h in onsets)
        {
            try { h.Timer?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex, "fx timer already gone"); }
        }
    }
}
