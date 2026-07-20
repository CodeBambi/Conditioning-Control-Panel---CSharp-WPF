using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.AvatarTube;

/// <summary>
/// The engine's clock seam (first-attempt lesson: tests inject, not assume). Production is
/// a monotonic Stopwatch; tests drive a manual clock so cadence, pause/resume, and
/// pack-switch are deterministic and fast. One interface, two implementations.
/// </summary>
public interface IAvatarClock
{
    /// <summary>Monotonic milliseconds (never wall-clock: pauses/slews must not move deadlines).</summary>
    long NowMs { get; }

    Task Delay(int milliseconds, CancellationToken cancellationToken);
}

/// <summary>Production clock: Stopwatch monotonic time + Task.Delay.</summary>
public sealed class SystemAvatarClock : IAvatarClock
{
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

    public long NowMs => _stopwatch.ElapsedMilliseconds;

    public Task Delay(int milliseconds, CancellationToken cancellationToken) =>
        Task.Delay(milliseconds, cancellationToken);
}

/// <summary>Test clock: time advances only when the test says so; Delay completes on advance.</summary>
public sealed class ManualAvatarClock : IAvatarClock
{
    private readonly object _gate = new();
    private long _now;
    private TaskCompletionSource? _waiter;

    public long NowMs { get { lock (_gate) { return _now; } } }

    /// <summary>True while the engine loop is parked in Delay — the race-free test driving signal.</summary>
    public bool DelayPending { get { lock (_gate) { return _waiter is { Task.IsCompleted: false }; } } }

    public Task Delay(int milliseconds, CancellationToken cancellationToken)
    {
        TaskCompletionSource waiter;
        lock (_gate)
        {
            _waiter?.TrySetCanceled();
            _waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waiter = _waiter;
        }

        // Observes cancellation like the production Task.Delay (engine stop path).
        return waiter.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Advances time and releases any in-flight Delay.</summary>
    public void Advance(long milliseconds)
    {
        lock (_gate)
        {
            _now += milliseconds;
            _waiter?.TrySetResult();
        }
    }

    /// <summary>Cancels any in-flight Delay (engine stop in tests).</summary>
    public void Release()
    {
        lock (_gate)
        {
            _waiter?.TrySetCanceled();
        }
    }
}

/// <summary>Animation mode: static pose set (dip-fade) vs the animated emote pipeline.</summary>
public enum AvatarMode
{
    Static,
    Animated,
}

/// <summary>
/// Demonstrator constants (pending-owner per the packet's owner-questions framing; WPF
/// parity anchors noted). Owner question "transition/liveness bounds" stays open — these
/// are demonstrator values, recorded, never presented as ratified product constants.
/// </summary>
public sealed record AvatarEngineOptions
{
    /// <summary>Engine loop quantum (WPF float timer parity: 16ms ≈ 60fps).</summary>
    public int QuantumMs { get; init; } = 16;

    /// <summary>Crossfade duration (WPF `_circeFadeMs` default, CirceEmotes.cs:84).</summary>
    public int FadeMs { get; init; } = 1000;

    /// <summary>Min-hold before a queued clip change executes (WPF `CirceMinHoldMs`, CirceEmotes.cs:78).</summary>
    public int MinHoldMs { get; init; } = 2000;

    /// <summary>Click-reaction cooldown (WPF `CirceClickCooldownMs`, CirceEmotes.cs:137).</summary>
    public int ClickCooldownMs { get; init; } = 3000;

    /// <summary>Reaction lead-out before talk end (WPF `talkLeadOutMs` fallback, CirceEmotes.cs:103).</summary>
    public int TalkLeadOutMs { get; init; } = 500;

    /// <summary>Static pose dip-fade depth/duration (WPF opacity dip 1→0.3 over 150ms, Avatar.cs:1364-1371).</summary>
    public double DipDepth { get; init; } = 0.3;

    public int DipMs { get; init; } = 150;

    /// <summary>Float amplitude/period (WPF `FloatDistance`/phase math, Windowing.cs:52-53,501-520).</summary>
    public double FloatAmplitudeDip { get; init; } = 4.0;

    public int FloatPeriodMs { get; init; } = 2000;
}

/// <summary>One visual layer's public snapshot (the view maps clip/frame to platform images).</summary>
public sealed record AvatarLayerState(int ClipId, int FrameIndex, double Opacity);

/// <summary>Raised whenever the rendered composition changed (frame, fade step, float step, mode).</summary>
public sealed class AvatarFrameEventArgs(
    AvatarPack pack, AvatarMode mode, AvatarLayerState layerA, AvatarLayerState? layerB, double floatYDip, long wallNowMs)
    : EventArgs
{
    /// <summary>The pack THESE frames belong to — self-sufficient across pack switches
    /// (a queued post from the old pack renders the old frame, which is what was presented).</summary>
    public AvatarPack Pack { get; } = pack;

    public int PackId { get; } = pack.Def.PackId;
    public AvatarMode Mode { get; } = mode;
    public AvatarLayerState LayerA { get; } = layerA;
    public AvatarLayerState? LayerB { get; } = layerB;
    public double FloatYDip { get; } = floatYDip;
    public long WallNowMs { get; } = wallNowMs;
}

/// <summary>Structured engine trace event (corroboration + evaluator correlation — never standalone liveness evidence).</summary>
public sealed class AvatarTraceEventArgs(string kind, int packId, int clipId, int frameIndex, long wallNowMs) : EventArgs
{
    public string Kind { get; } = kind;
    public int PackId { get; } = packId;
    public int ClipId { get; } = clipId;
    public int FrameIndex { get; } = frameIndex;
    public long WallNowMs { get; } = wallNowMs;
}

/// <summary>
/// The AvatarTube demonstrator animation engine. ONE SP-004 owned operation per run
/// generation drives EVERYTHING (pre-approach consult): frame advancement on declared
/// non-uniform deadlines, crossfade opacity steps (no Avalonia Transitions clock as a
/// second timing authority), dip-fades, and the float transform — there is no
/// DispatcherTimer/Threading.Timer/PeriodicTimer anywhere (first-attempt leak REJECTs:
/// fresh-timer-per-entry, created-but-not-started, stop-missing paths).
///
/// Pause is an in-operation gate observing the generation token (a gate that ignores
/// cancellation wedges teardown's drain): resume continues the SAME operation, so the next
/// applied frame is the paused frame's successor by construction, and the effective-time
/// re-base keeps cadence bit-identical. Fixed-quantum loop with monotonic deadline
/// accumulation absorbs Task.Delay's systematic overshoot instead of drifting.
/// </summary>
public sealed class AvatarAnimationEngine
{
    private readonly AsyncOperationOwner _owner;
    private readonly IAvatarClock _clock;
    private readonly ILogSink _log;
    private readonly AvatarEngineOptions _options;
    private readonly object _gate = new();

    private AvatarPack _pack;
    private AvatarMode _mode = AvatarMode.Static;

    // Loop-owned animation state (mutated only on the loop thread unless noted).
    private Layer _layerA;
    private Layer? _layerB;
    private Fade? _fade;
    private Dip? _dip;
    private readonly List<PendingRequest> _requests = [];
    private long _pausedAccum;
    private long _pauseStartRaw = -1;
    private bool _pauseTraceEmitted;
    private bool _pauseRequested;
    private TaskCompletionSource? _resumeGate;
    private long _currentClipStartMs;
    private long _reactionAtMs = -1;
    private long _lastClickMs = -1; // no accepted click yet (long.MinValue arithmetic overflows)
    private int _idleClip = SyntheticAvatarPacks.ClipIdle;

    public AvatarAnimationEngine(AsyncOperationOwner owner, IAvatarClock clock, ILogSink log, AvatarPack pack, AvatarEngineOptions? options = null)
    {
        _owner = owner;
        _clock = clock;
        _log = log;
        _pack = pack;
        _options = options ?? new AvatarEngineOptions();
        _layerA = Layer.Start(pack, SyntheticAvatarPacks.ClipPoses, 0);
    }

    /// <summary>Raised on the loop thread when the composition changed. UI delivery is the participant's job (IUiDispatch).</summary>
    public event EventHandler<AvatarFrameEventArgs>? FrameApplied;

    /// <summary>Structured trace (wall-clock correlated with capture timestamps).</summary>
    public event EventHandler<AvatarTraceEventArgs>? Traced;

    /// <summary>Live event-subscription count — the real-registry leak signal (first-attempt subscription-leak REJECT).</summary>
    public int FrameSubscriberCount => FrameApplied?.GetInvocationList().Length ?? 0;

    public AvatarMode Mode { get { lock (_gate) { return _mode; } } }

    public bool Paused { get { lock (_gate) { return _pauseRequested; } } }

    public int CurrentPackId => _pack.Def.PackId;

    /// <summary>The current authoritative frame (layer A) — evidence readback for tests.</summary>
    public (int ClipId, int FrameIndex) CurrentFrame
    {
        get { lock (_gate) { return (_layerA.ClipId, _layerA.FrameIndex); } }
    }

    /// <summary>The owned completion (async contract §1) — null until <see cref="Start"/>.</summary>
    public Task<OperationOutcome>? Completion { get; private set; }

    /// <summary>The active pack, swapped by the participant on pack switch (same generation stays; frames rebase to idle[0]).</summary>
    public void SetPack(AvatarPack pack)
    {
        lock (_gate)
        {
            _pack = pack;
            _mode = AvatarMode.Animated;
            ResetPipelineUnsafe(StartClipFor(AvatarMode.Animated, pack));
        }
    }

    /// <summary>Begins a generation and starts the ONE owned animation operation.</summary>
    public void Start()
    {
        lock (_gate)
        {
            _owner.Begin();
            _pausedAccum = 0;
            _pauseStartRaw = -1;
            _pauseRequested = false;
            _pauseTraceEmitted = false;
            _resumeGate = null;
            ResetPipelineUnsafe(StartClipFor(_mode, _pack));
            TraceUnsafe("frame", _layerA.ClipId, 0);
        }

        Completion = _owner.RunAsync("avatar-animation", token => LoopAsync(token));
    }

    /// <summary>Start clip for a mode, degraded gracefully for packs lacking it (the single-clip fallback pack).</summary>
    private static int StartClipFor(AvatarMode mode, AvatarPack pack)
    {
        var preferred = mode == AvatarMode.Static ? SyntheticAvatarPacks.ClipPoses : SyntheticAvatarPacks.ClipIdle;
        return pack.Def.Clips.Any(c => c.ClipId == preferred) ? preferred : pack.Def.Clips[0].ClipId;
    }

    private void ResetPipelineUnsafe(int startClipId)
    {
        _layerA = Layer.Start(_pack, startClipId, 0);
        _layerB = null;
        _fade = null;
        _dip = null;
        _requests.Clear();
        _reactionAtMs = -1;
        _currentClipStartMs = 0;
    }

    /// <summary>Cancels the generation (idempotent; the loop completes with typed Cancelled).</summary>
    public void Stop()
    {
        _owner.Cancel();
        lock (_gate)
        {
            // Wake a paused loop so it can observe the cancelled token and complete typed
            // Cancelled — completing (not cancelling) the gate keeps the OCE path reserved
            // for token cancellation.
            _resumeGate?.TrySetResult();
        }
    }

    public void SetMode(AvatarMode mode)
    {
        lock (_gate)
        {
            if (_mode == mode)
            {
                return;
            }

            _mode = mode;
            var target = mode == AvatarMode.Static ? SyntheticAvatarPacks.ClipPoses : _idleClip;
            QueueClipChange("mode", target, minHold: false);
        }
    }

    /// <summary>Synthetic talk trigger (audio pipeline is a BLOCKED row — declared duration substitutes).</summary>
    public void TriggerTalk(int durationMs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(durationMs, 200);
        lock (_gate)
        {
            if (_mode != AvatarMode.Animated)
            {
                return;
            }

            QueueClipChange("talk-start", SyntheticAvatarPacks.ClipTalk, minHold: true);
            var effective = EffectiveNowUnsafe();
            _reactionAtMs = effective + durationMs - _options.TalkLeadOutMs;
        }
    }

    /// <summary>Click reaction (WPF: own pool, 3000ms cooldown, interrupts talk, plays once → idle).</summary>
    public void TriggerClick()
    {
        lock (_gate)
        {
            if (_mode != AvatarMode.Animated)
            {
                return;
            }

            var effective = EffectiveNowUnsafe();
            if (_lastClickMs >= 0 && effective - _lastClickMs < _options.ClickCooldownMs)
            {
                TraceUnsafe("click-cooldown-ignored", _layerA.ClipId, _layerA.FrameIndex);
                return;
            }

            _lastClickMs = effective;
            // Interruption parity: any pending talk schedule dies (WPF StopTalkSequence).
            _reactionAtMs = -1;
            QueueClipChange("click-start", SyntheticAvatarPacks.ClipClick, minHold: true);
        }
    }

    /// <summary>Pause the pipeline (gate observes the generation token; float/fades/deadlines all freeze).</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (_pauseRequested)
            {
                return;
            }

            _pauseRequested = true;
            // Freeze stamp is the CALL time, not the loop's gate-entry time: deadlines
            // crossed while the loop sleeps into the gate must not execute after resume.
            _pauseStartRaw = _clock.NowMs;
            _resumeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Resume the SAME operation: successor-frame + unchanged cadence by construction.</summary>
    public void Resume()
    {
        lock (_gate)
        {
            _pauseRequested = false;
            _resumeGate?.TrySetResult();
        }
    }

    private async Task<OperationOutcome> LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Task? gate = null;
            lock (_gate)
            {
                if (_pauseRequested)
                {
                    if (_pauseStartRaw >= 0 && !_pauseTraceEmitted)
                    {
                        _pauseTraceEmitted = true;
                        TraceUnsafe(AvatarTraceEvent.PauseBegin, _layerA.ClipId, _layerA.FrameIndex);
                    }

                    gate = _resumeGate?.Task;
                }
            }

            if (gate is not null)
            {
                // The gate observes the token: teardown cancellation never wedges here.
                try
                {
                    await gate.WaitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return OperationOutcome.Cancelled.Instance;
                }

                if (token.IsCancellationRequested)
                {
                    // Stop() woke the gate: exit typed Cancelled without a spurious resume.
                    return OperationOutcome.Cancelled.Instance;
                }

                lock (_gate)
                {
                    _pausedAccum += _clock.NowMs - _pauseStartRaw;
                    _pauseStartRaw = -1;
                    _pauseTraceEmitted = false;
                    TraceUnsafe(AvatarTraceEvent.PauseEnd, _layerA.ClipId, _layerA.FrameIndex);
                }
            }

            Tick();
            // The float transform moves every quantum by design (WPF 16ms float-timer
            // parity) — the composition is emitted every loop iteration, and pause
            // freezing the loop freezes the float with everything else.
            EmitFrame();

            try
            {
                await _clock.Delay(_options.QuantumMs, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return OperationOutcome.Cancelled.Instance;
            }
        }

        return OperationOutcome.Cancelled.Instance;
    }

    /// <summary>One quantum of animation work on the loop thread.</summary>
    private void Tick()
    {
        lock (_gate)
        {
            var now = EffectiveNowUnsafe();

            // Due clip-change requests (min-hold coalescing: WPF CirceMinHoldMs parity).
            // Issue order is preserved and the LAST issued due request wins — interruption
            // semantics (a click issued after a talk must interrupt it, never the reverse).
            if (_requests.Count > 0)
            {
                var due = _requests.Where(r => now >= r.ExecuteAtMs).ToArray();
                if (due.Length > 0)
                {
                    _requests.RemoveAll(r => due.Contains(r));
                    foreach (var request in due)
                    {
                        BeginCrossfadeUnsafe(request.ClipId, now);
                    }
                }
            }

            // Talk schedule deadline: the reaction lands talkLeadOutMs before the declared
            // end (WPF talk-schedule parity with a synthetic declared-duration trigger).
            if (_reactionAtMs >= 0 && now >= _reactionAtMs && _layerA.ClipId == SyntheticAvatarPacks.ClipTalk)
            {
                _reactionAtMs = -1;
                BeginCrossfadeUnsafe(SyntheticAvatarPacks.ClipReaction, now);
            }

            // Frame advancement per layer (deadline accumulation absorbs timer overshoot).
            AdvanceLayerUnsafe(_layerA, now);
            if (_layerB is not null)
            {
                AdvanceLayerUnsafe(_layerB, now);
            }

            // Dip-fade progress (static pose mode): identity swaps at the midpoint;
            // deadlines already advanced on the DECLARED schedule (no per-pose drift).
            if (_dip is not null)
            {
                var progress = Math.Min(1.0, (double)(now - _dip.StartMs) / _options.DipMs);
                _layerA.Opacity = 1.0 - (1.0 - _options.DipDepth) * (1.0 - Math.Abs(2 * progress - 1));
                if (progress >= 0.5 && !_dip.Swapped)
                {
                    _dip.Swapped = true;
                    _layerA.FrameIndex = _dip.NextFrame;
                    TraceUnsafe("frame", _layerA.ClipId, _dip.NextFrame);
                }

                if (progress >= 1.0)
                {
                    _layerA.Opacity = 1.0;
                    _dip = null;
                }
            }

            // Crossfade progress (engine-stepped; the opacity-sum invariant keeps
            // never-blank as pure math: A+B == 1 at every step).
            if (_fade is not null && _layerB is not null)
            {
                var progress = Math.Min(1.0, (double)(now - _fade.StartMs) / _options.FadeMs);
                _layerA.Opacity = 1.0 - progress;
                _layerB.Opacity = progress;
                if (progress >= 1.0)
                {
                    _layerA = _layerB;
                    _layerB = null;
                    _fade = null;
                    _dip = null; // a dip straddling the fade boundary must not write the new layer
                    _layerA.Opacity = 1.0;
                    _currentClipStartMs = now;
                    TraceUnsafe(AvatarTraceEvent.CrossfadeEnd, _layerA.ClipId, _layerA.FrameIndex);
                }
            }
        }
    }

    private void AdvanceLayerUnsafe(Layer layer, long now)
    {
        while (now >= layer.NextDeadlineMs)
        {
            var clip = _pack.Def.Clip(layer.ClipId);
            var next = layer.FrameIndex + 1;
            if (next >= clip.Frames)
            {
                // Clip pass complete. Poses cycle forever; one-shots and idles rotate
                // onward through a crossfade (WPF: clips play ONCE, completion drives rotation).
                if (layer == _layerA && layer.ClipId != SyntheticAvatarPacks.ClipPoses && layer.PassesRemaining <= 1)
                {
                    if (_fade is null)
                    {
                        var target = layer.ClipId is SyntheticAvatarPacks.ClipIdle or SyntheticAvatarPacks.ClipIdle2
                            ? NextIdleUnsafe()
                            : _idleClip; // reaction/click one-shots return to the current idle
                        // Single-clip packs (the undecodable-asset fallback) rotate to
                        // themselves — a valid STATIC avatar, never a throw, never a blank.
                        if (!_pack.Def.Clips.Any(c => c.ClipId == target))
                        {
                            target = layer.ClipId;
                        }

                        BeginCrossfadeUnsafe(target, now);
                    }

                    layer.NextDeadlineMs = long.MaxValue;
                    return;
                }

                next = 0;
                if (layer.PassesRemaining != int.MaxValue)
                {
                    layer.PassesRemaining--;
                }
            }

            // Deadlines accumulate on the DECLARED schedule regardless of transition
            // styling — a dip/crossfade never shifts later frames (no systematic drift).
            layer.NextDeadlineMs += clip.DelaysMs[next];

            if (layer == _layerA && layer.ClipId == SyntheticAvatarPacks.ClipPoses)
            {
                // Static poses: dip-fade into every next pose (WPF 1→0.3 dip parity);
                // the strip identity swaps at the dip midpoint. A running crossfade
                // supersedes dips (outgoing layer just advances) — dip opacity math must
                // never fight the crossfade's stepped invariant.
                if (_dip is null && _fade is null)
                {
                    _dip = new Dip(now, next);
                    return;
                }
            }

            layer.FrameIndex = next;
            if (layer == _layerA)
            {
                TraceUnsafe("frame", layer.ClipId, next);
            }
        }
    }

    private int NextIdleUnsafe()
    {
        // Deterministic round-robin (demonstrator simplification; WPF weighted-random
        // excluding current is the product behavior — recorded pending-owner).
        _idleClip = _idleClip == SyntheticAvatarPacks.ClipIdle
            ? SyntheticAvatarPacks.ClipIdle2
            : SyntheticAvatarPacks.ClipIdle;
        return _idleClip;
    }

    private void QueueClipChange(string kind, int clipId, bool minHold)
    {
        var now = EffectiveNowUnsafe();
        var executeAt = minHold
            ? Math.Max(now, _currentClipStartMs + _options.MinHoldMs)
            : now;
        // Coalesce: same-kind requests replace (min-hold parity: swaps coalesce, latest wins).
        _requests.RemoveAll(r => r.Kind == kind);
        _requests.Add(new PendingRequest(kind, clipId, executeAt));
    }

    private void BeginCrossfadeUnsafe(int clipId, long now)
    {
        if (_layerA.ClipId == clipId && _fade is null)
        {
            return; // already there; identical-URI no-op parity
        }

        // Cancel any in-progress dip: the emote pipeline pre-empts pose dips.
        if (_dip is not null)
        {
            _dip = null;
            _layerA.Opacity = 1.0;
        }

        _layerB = Layer.Start(_pack, clipId, 0);
        _layerB.Opacity = 0.0;
        _layerB.NextDeadlineMs = now + _pack.Def.Clip(clipId).DelaysMs[0];
        _fade = new Fade(now);
        TraceUnsafe(AvatarTraceEvent.CrossfadeStart, clipId, 0);
    }

    private long EffectiveNowUnsafe() => _clock.NowMs - _pausedAccum;

    private void EmitFrame()
    {
        AvatarFrameEventArgs args;
        lock (_gate)
        {
            var now = EffectiveNowUnsafe();
            var floatY = Math.Sin(2.0 * Math.PI * now / _options.FloatPeriodMs) * _options.FloatAmplitudeDip;
            args = new AvatarFrameEventArgs(
                _pack, _mode,
                new AvatarLayerState(_layerA.ClipId, _layerA.FrameIndex, _layerA.Opacity),
                _layerB is null ? null : new AvatarLayerState(_layerB.ClipId, _layerB.FrameIndex, _layerB.Opacity),
                floatY, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        FrameApplied?.Invoke(this, args);
    }

    private void TraceUnsafe(string kind, int clipId, int frameIndex)
    {
        var wall = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _log.Log($"avatar-trace: {kind} pack={_pack.Def.PackId} clip={clipId} frame={frameIndex} t={wall}");
        Traced?.Invoke(this, new AvatarTraceEventArgs(kind, _pack.Def.PackId, clipId, frameIndex, wall));
    }

    private sealed class Layer
    {
        public int ClipId { get; }
        public int FrameIndex { get; set; }
        public long NextDeadlineMs { get; set; }
        public double Opacity { get; set; } = 1.0;
        public int PassesRemaining { get; set; } = int.MaxValue;
        public int[] Delays { get; }

        private Layer(int clipId, int frameIndex, int[] delays)
        {
            ClipId = clipId;
            FrameIndex = frameIndex;
            Delays = delays;
            NextDeadlineMs = delays[frameIndex];
        }

        public static Layer Start(AvatarPack pack, int clipId, int frameIndex)
        {
            var delays = pack.Def.Clip(clipId).DelaysMs;
            // One-shot semantics (WPF: clips play ONCE, completion drives rotation):
            // idles rotate after one pass; reaction/click return to idle after one pass;
            // talk loops until its schedule ends; poses cycle indefinitely.
            var passes = clipId is SyntheticAvatarPacks.ClipPoses or SyntheticAvatarPacks.ClipTalk
                ? int.MaxValue
                : 1;
            return new Layer(clipId, frameIndex, delays) { NextDeadlineMs = delays[frameIndex], PassesRemaining = passes };
        }
    }

    private sealed record Fade(long StartMs);

    private sealed class Dip(long startMs, int nextFrame)
    {
        public long StartMs { get; } = startMs;
        public int NextFrame { get; } = nextFrame;
        public bool Swapped { get; set; }
    }

    private sealed record PendingRequest(string Kind, int ClipId, long ExecuteAtMs);
}
