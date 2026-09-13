using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>
/// The primitive calls, one per contract primitive. The real one (<c>BackRoomFxServices</c>)
/// maps these onto the app's existing overlay services; tests use a recorder. Every call arrives on
/// the scheduler's thread (the UI dispatcher in the app).
/// </summary>
public interface IBackRoomFxSink
{
    void FlashBurst(int amount);
    void GifRain(int durationMs);
    void GlitchWash(int durationMs, double opacity);
    void Subliminal(string text);
    /// <summary><paramref name="level"/> is a fraction of the user's own spiral opacity.</summary>
    void Spiral(int durationMs, double level, bool still);
    /// <summary><paramref name="level"/> is a fraction of the user's own brain drain intensity.</summary>
    void BrainDrain(int durationMs, double level, bool melt);
    void GifFull(BackRoomGif gif, int durationMs, bool still);
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
public sealed record FxEnvironment(MotionLevel Motion, BackRoomFxIntensity Intensity, FxGates Gates);

/// <summary>
/// The effect dispatcher (C3). Resolves an fx id through <see cref="BackRoomFxPlan"/>, admits it
/// through the hero gate, paces every repeated onset under the 6 Hz ceiling, and schedules the
/// primitives on the sink. The ack goes back synchronously: what will play, and every skip.
/// </summary>
public sealed class BackRoomFx : IBackRoomFx
{
    private readonly IBackRoomFxSink _sink;
    private readonly IFxScheduler _scheduler;
    private readonly Func<FxEnvironment> _env;
    private readonly Random _rng;
    private readonly FxHeroGate _gate;
    private readonly FxStrobePacer _pacer = new();
    private readonly object _lock = new();
    private readonly HashSet<PendingOnset> _pending = new();

    public BackRoomFx(IBackRoomFxSink sink, IFxScheduler scheduler, Func<FxEnvironment> env, Random? rng = null)
    {
        _sink = sink;
        _scheduler = scheduler;
        _env = env;
        _rng = rng ?? new Random();
        _gate = new FxHeroGate(() => _scheduler.NowMs);
    }

    internal FxHeroGate Gate => _gate;

    /// <summary>Primitive onsets still waiting to run (tests, diagnostics).</summary>
    public int PendingCount { get { lock (_lock) return _pending.Count; } }

    public BackRoomFxAck Fire(string fxId, string station, IReadOnlyList<string> symbolKeys, BackRoomMediaDeal deal)
        => Fire(fxId, symbolKeys, deal, _env());

    /// <summary>Fire under an explicit environment. The room never calls this; the dev rig does, so a
    /// desk run can walk every intensity without touching the user's settings.</summary>
    internal BackRoomFxAck Fire(string fxId, IReadOnlyList<string>? symbolKeys, BackRoomMediaDeal? deal, FxEnvironment env)
    {
        fxId ??= string.Empty;
        try
        {
            FxPlan plan;
            lock (_rng) plan = BackRoomFxPlan.Resolve(fxId, env.Intensity, env.Motion, env.Gates, symbolKeys, deal, _rng);
            if (plan.Steps.Count == 0) return new BackRoomFxAck(Array.Empty<string>(), plan.Skipped);

            var admission = _gate.Admit(fxId, plan.HeroMs);
            switch (admission.Kind)
            {
                case FxAdmitKind.Busy:
                    var busy = plan.Skipped.Concat(plan.Fired.Distinct()
                        .Select(p => new BackRoomFxSkip(p, BackRoomFxSkipReason.Busy))).ToList();
                    return new BackRoomFxAck(Array.Empty<string>(), busy);
                case FxAdmitKind.Merged:
                    // It plays once, as the copy already on stage or in the queue.
                    return new BackRoomFxAck(plan.Fired, plan.Skipped);
            }

            long baseAt = _scheduler.NowMs + admission.DelayMs;
            foreach (var planned in plan.Steps) Schedule(planned, baseAt);
            return new BackRoomFxAck(plan.Fired, plan.Skipped);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "[BackRoom] fx fire failed ({FxId})", fxId);
            return new BackRoomFxAck(Array.Empty<string>(), new[] { new BackRoomFxSkip(fxId, BackRoomFxSkipReason.Unknown) });
        }
    }

    private void Schedule(FxPlannedStep planned, long baseAt)
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
                    At(_pacer.Reserve(FxChannel.Subliminal, at + i * BackRoomFxPlan.WordGapMs), () => _sink.Subliminal(word));
                }
                break;
            case FxPrim.GlitchBubbles:
                for (int i = 0; i < Math.Max(1, s.Count); i++)
                    At(_pacer.Reserve(FxChannel.Glitch, at + i * BackRoomFxPlan.GlitchWashMs),
                        () => _sink.GlitchWash(BackRoomFxPlan.GlitchWashMs, BackRoomFxPlan.GlitchOpacity));
                break;
            case FxPrim.FlashBurst:
                At(_pacer.Reserve(FxChannel.Flash, at), () => _sink.FlashBurst(Math.Max(1, s.Count)));
                break;
            case FxPrim.GifRain:
                At(at, () => _sink.GifRain(s.DurationMs));
                break;
            case FxPrim.SpiralFull:
                At(at, () => _sink.Spiral(s.DurationMs, s.Level, s.Still));
                break;
            case FxPrim.BrainDrainMelt:
            case FxPrim.BrainDrain:
                At(at, () => _sink.BrainDrain(s.DurationMs, s.Level, s.Prim == FxPrim.BrainDrainMelt));
                break;
            case FxPrim.GifFull:
                var gif = planned.Gif!;
                At(at, () => _sink.GifFull(gif, s.DurationMs, s.Still));
                break;
        }
    }

    private sealed class PendingOnset { public IDisposable? Timer; }

    private void At(long atMs, Action action)
    {
        int delay = (int)Math.Max(0, atMs - _scheduler.NowMs);
        // The token is pending BEFORE the timer exists, so a timer that fires early (or a scheduler
        // that runs inline) always finds it, and a CancelAll that wins the race always silences it.
        var onset = new PendingOnset();
        lock (_lock) _pending.Add(onset);
        onset.Timer = _scheduler.After(delay, () =>
        {
            lock (_lock) { if (!_pending.Remove(onset)) return; }
            try { action(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[BackRoom] fx primitive failed"); }
        });
    }

    /// <summary>Suspend or close: drop every waiting onset, forget the hero windows, stop what is up.</summary>
    public void CancelAll()
    {
        List<PendingOnset> doomed;
        lock (_lock)
        {
            doomed = _pending.ToList();
            _pending.Clear();
        }
        foreach (var h in doomed)
        {
            try { h.Timer?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex, "fx timer already gone"); }
        }
        _gate.Clear();
        _pacer.Clear();
        try { _sink.StopAll(); }
        catch (Exception ex) { App.Logger?.Warning(ex, "[BackRoom] fx stop failed"); }
    }
}
