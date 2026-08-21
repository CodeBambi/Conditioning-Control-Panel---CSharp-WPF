using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Haptics;

/// <summary>
/// Which ported moment a live envelope belongs to. Upstream's <c>HapticEventKind</c>, narrowed to
/// the kinds this build can actually produce — it is the key upstream's own
/// <c>TrackKind</c>/<c>CancelKind</c> pair uses (<c>Services/Haptics/HapticService.cs:408-422</c>),
/// and the flash ladder's replace rule is the only thing that needs it.
/// </summary>
public enum HapticMoment
{
    /// <summary>A flash image was placed (<c>HapticEventKind.FlashDecay</c>).</summary>
    FlashDecay,

    /// <summary>A subliminal card was shown (<c>HapticEventKind.SubliminalTrigger</c>).</summary>
    SubliminalTrigger,

    /// <summary>A bouncing word hit an edge (<c>HapticEventKind.BouncingTextBounce</c>).</summary>
    BouncingTextBounce,
}

/// <summary>
/// <b>The limb: upstream's mixer arithmetic over SP-119's sink, with no output loop.</b>
///
/// <para>SP-126, option C+ (<c>client/docs/haptic-limb-census.md</c> §9). It is a command
/// vocabulary — <see cref="IHapticLimb"/>'s five moments plus <see cref="SetLayer"/> — over a
/// SHARED-INSTANT evaluator: transients sum within a priority group, groups arbitrate by MAX,
/// continuous layers form a soft-ramped floor underneath, and the concurrency cap keeps a burst from
/// becoming one flat wall of buzz. Every rule and every constant is
/// <c>Services/Haptics/Core/HapticMixer.cs</c>'s, cited at the line.</para>
///
/// <para><b>WHY THERE IS NO 10 Hz LOOP, and why that is not a shortcut.</b> Upstream's loop exists
/// because it is the SENDER: it is <i>"the self-imposed rate limit for the Lovense LAN API"</i>
/// (<c>HapticMixer.cs:68-70</c>), it re-asserts unchanged targets every second so a provider using
/// short <c>timeSec</c> repeats gets a heartbeat (<c>:86-92</c>), and it re-stamps zeros for two
/// seconds after a stop (<c>:93-97</c>). <b>All three are DELIVERY properties</b>, and SP-119's seam
/// already assigns delivery to the provider in as many words — <i>"Lovense: timeSec 0 + keep-alive
/// refresh, or short-timeSec repeats — provider's choice"</i>
/// (<c>Services/Haptics/Core/HapticContracts.cs:70-73</c>, quoted on <see cref="IHapticSink"/>).
/// What remains after delivery is removed is arithmetic, and arithmetic needs an INSTANT rather than
/// a poll. So each posted envelope schedules its own one-shot wake-ups on the injected
/// <see cref="ISessionClock"/> — its start, its crest, its decay edge, its end, and upstream's own
/// <see cref="HapticMix.TickMs"/> grid in between — and at each wake EVERY live envelope is evaluated
/// at that same instant. <b>Nothing wakes while nothing is playing</b>, and a still scene costs
/// exactly zero.</para>
///
/// <para><b>Every wake carries the instant it stands for.</b> It never asks the clock what time it
/// is on arrival. A late timer therefore renders the envelope's true value at the moment it was meant
/// to, instead of smearing the shape by however long the thread pool took — and a fact can advance a
/// manual clock in one jump and still read the exact sequence of levels a real run would produce.</para>
///
/// <para><b>The verbs never block the caller and never touch a wire on the caller's thread.</b> A
/// flash surface calls <see cref="IHapticLimb.FlashPlaced"/> from the surface thread; all it does is
/// queue steps and schedule wakes. That is upstream's shape too — <c>Play</c> adds to <c>_pending</c>
/// and returns, and the loop promotes (<c>HapticMixer.cs:840-866</c>).</para>
///
/// <para><b>The gate is evaluated HERE and never at a call site</b>, which is D204 preserved:
/// upstream refuses centrally inside the mixer (<c>HapticMixer.cs:191-204</c> is the gate,
/// <c>:843</c> is <c>Play</c>'s refusal) and its activity readout still fires, so <b>an unentitled
/// user with no toy still sees the app report what it asked for</b>. A limb that added an
/// entitlement or connected check at a module would change user-observable behaviour.
/// <see cref="Moments"/> therefore counts what was asked; <see cref="Sends"/> counts what was
/// attempted.</para>
///
/// <para><b>NOTHING MOVES.</b> No provider client is admitted, so the sink refuses and there is no
/// device key to address: on this build <see cref="Sends"/> stays at zero for every session and
/// <see cref="EvaluationsWithNoDevice"/> is where the truth of that is visible.</para>
/// </summary>
public sealed class HapticLimb : IHapticLimb, IDisposable
{
    private readonly IHapticSink _sink;
    private readonly ISessionClock _clock;
    private readonly Func<bool> _outputAllowed;
    private readonly Func<IReadOnlyList<string>> _devices;
    private readonly Action<string>? _log;

    private readonly object _gate = new();
    private readonly DateTimeOffset _origin;
    private readonly List<PendingStep> _pending = [];
    private readonly List<ActivePulse> _active = [];
    private readonly List<PrioritisedLevel> _samples = [];
    private readonly Dictionary<long, IDisposable?> _wakes = [];

    private double _layerTarget;
    private long _layerAutoZeroAt;
    private double _floor;
    private long _floorAt = long.MinValue;
    private bool _disposed;

    /// <param name="sink">Where levels go. <b>Never a sink that claims Available on a product
    /// path</b> — the same rule <see cref="HapticParticipant"/>'s own sink parameter carries.</param>
    /// <param name="clock">The limb's own timing seam. It is the limb's rather than a session's for
    /// upstream's reason: the mixer's loop is owned by the app-scoped haptic service and is
    /// independent of any module's timer (<c>HapticMixer.cs:224-247</c>).</param>
    /// <param name="outputAllowed">The gate, asked once per post and once per send, exactly where
    /// upstream asks it. A predicate that throws is a CLOSED gate, as upstream's own
    /// <c>try { … } catch { return false; }</c> decides (<c>HapticMixer.cs:198-203</c>).</param>
    /// <param name="devices">The device keys a level may be addressed to, re-read per send. Empty on
    /// every run of this build.</param>
    /// <param name="log">Where a send fault is reported. Null means contained but unreported.</param>
    public HapticLimb(
        IHapticSink sink,
        ISessionClock clock,
        Func<bool> outputAllowed,
        Func<IReadOnlyList<string>> devices,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(outputAllowed);
        ArgumentNullException.ThrowIfNull(devices);
        _sink = sink;
        _clock = clock;
        _outputAllowed = outputAllowed;
        _devices = devices;
        _log = log;
        _origin = clock.UtcNow;
    }

    /// <summary>How many moments were reported to this limb, whatever came of them. Counted even
    /// when the gate refuses, which is D204: upstream's readout fires after <c>Play</c>
    /// unconditionally (<c>HapticService.cs:390</c>, <c>:791</c>, <c>:849</c>).</summary>
    public int Moments { get; private set; }

    /// <summary>Moments the gate refused outright (<c>HapticMixer.cs:843</c>).</summary>
    public int Refusals { get; private set; }

    /// <summary>How many instants were evaluated. Diagnostics; never a claim.</summary>
    public int Evaluations { get; private set; }

    /// <summary>
    /// Evaluations that produced a level with <b>no device key to address it to</b>.
    ///
    /// <para>This is where "nothing moves" is legible rather than asserted: on this build it equals
    /// <see cref="Evaluations"/> whenever the gate is open, because
    /// <see cref="HapticSinkFactory.AdmittedRoutes"/> is empty, nothing was ever asked of a server,
    /// and an observation that was never made names no device.</para>
    /// </summary>
    public int EvaluationsWithNoDevice { get; private set; }

    /// <summary>How many <see cref="IHapticSink.SetOutputsAsync"/> calls were attempted. Zero on
    /// every run of this build.</summary>
    public int Sends { get; private set; }

    /// <summary>Envelopes dropped at promotion for failing to out-rank the weakest thing playing
    /// (<c>HapticMixer.cs:907-912</c>).</summary>
    public int PulsesDropped { get; private set; }

    /// <summary>Envelopes evicted at promotion by a higher-priority arrival (<c>:909-914</c>).</summary>
    public int PulsesEvicted { get; private set; }

    /// <summary>The level the last evaluation produced, after the master multiplier, the perceptible
    /// floor and the safety cap. Diagnostics and facts; never a claim that anything received it.</summary>
    public double LastLevel { get; private set; }

    /// <summary>The sink's verbatim answer to the last send, or null when none was attempted.</summary>
    public CapabilityState? LastOutcome { get; private set; }

    /// <summary>Envelopes live right now. Bounded by <see cref="HapticMix.MaxConcurrentPulses"/>.</summary>
    public int ActivePulses
    {
        get
        {
            lock (_gate)
            {
                return _active.Count;
            }
        }
    }

    /// <summary>The continuous layer as it stands, before the soft ramp and the master arithmetic.
    /// Upstream's <c>GetLayer</c> (<c>HapticMixer.cs:760-763</c>).</summary>
    public double Layer
    {
        get
        {
            lock (_gate)
            {
                return _layerTarget;
            }
        }
    }

    /// <inheritdoc/>
    public void FlashPlaced()
    {
        Moments++;
        // Upstream replaces whatever decay is running before posting a new one, so a ladder can
        // never stack on itself and flash-over-flash does not sum even where flash-over-subliminal
        // does (HapticService.cs:774-776). Both flash kinds share the visual moment upstream; this
        // port has only the display one, site 5 being absent-by-decision.
        CancelMoment(HapticMoment.FlashDecay);
        Post(HapticMoment.FlashDecay, HapticEnvelopes.FlashDecayLadder());
    }

    /// <inheritdoc/>
    public void VideoStarted()
    {
        Moments++;
        // No auto-zero, exactly as upstream passes none (HapticService.cs:848). The latch is
        // countermanded by VideoStopped on every path that takes the clip down, which is the
        // divergence from D203 rather than a copy of it.
        SetLayer(HapticEnvelopes.VideoBackgroundLevel());
    }

    /// <inheritdoc/>
    public void VideoStopped()
    {
        Moments++;
        SetLayer(0);
    }

    /// <inheritdoc/>
    public void SubliminalShown(string phrase)
    {
        Moments++;
        Post(HapticMoment.SubliminalTrigger, HapticEnvelopes.SubliminalPulse(phrase));
    }

    /// <inheritdoc/>
    public void BounceHit()
    {
        Moments++;
        Post(HapticMoment.BouncingTextBounce, HapticEnvelopes.BouncePulse());
    }

    /// <summary>
    /// Set the continuous layer. Upstream's <c>SetLayer</c> (<c>HapticMixer.cs:661-678</c>), with
    /// upstream's clamp, its NaN rule and its auto-zero latch.
    ///
    /// <para><b>Deliberately NOT gated</b>, because upstream's is not: <c>SetLayer</c> stores and the
    /// TICK refuses (<c>:253-258</c>), so a level set while the gate is closed is remembered and
    /// simply never delivered. The refusal is on the send path below, in the same place.</para>
    ///
    /// <para><b>Public, with one product caller today.</b> The video background layer passes the
    /// shipped 0.06 and no auto-zero. The other upstream producer of a continuous layer is the flash
    /// luminance sync (census site 4), which this port does not wire because upstream's own feature
    /// switch for it ships OFF (<c>Models/HapticSettings.cs:373</c>, <i>"Off by default until the
    /// Phase E UI exposes it"</i>) — see the divergence ledger.</para>
    /// </summary>
    /// <param name="level">0..1. NaN is silence rather than an exception (<c>:667</c>).</param>
    /// <param name="autoZeroMs">Self-clear after this long, so a layer whose owner dies in an
    /// unusual way cannot leave a device running. Zero means latch until countermanded.</param>
    public void SetLayer(double level, int autoZeroMs = 0)
    {
        var value = double.IsNaN(level) ? 0 : Math.Clamp(level, 0, 1);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var now = NowLocked();
            _layerTarget = value;
            _layerAutoZeroAt = value > 0 && autoZeroMs > 0 ? now + autoZeroMs : 0;
            ScheduleWakeLocked(now);
            if (_layerAutoZeroAt > 0)
            {
                ScheduleWakeLocked(_layerAutoZeroAt);
            }
        }
    }

    /// <summary>
    /// Drop every transient, zero every layer and cancel every scheduled wake — <b>without</b>
    /// touching the sink.
    ///
    /// <para>Upstream's <c>ClearAll</c> (<c>HapticMixer.cs:1044-1049</c>). It is separate from the
    /// all-stop on purpose: <see cref="HapticParticipant"/> owns the one all-stop and its counters,
    /// and a limb that called <see cref="IHapticSink.StopAllAsync"/> itself would spend the stop
    /// budget twice on the teardown path — the defect upstream fixed with a one-shot latch
    /// (<c>:172-174</c>).</para>
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            ClearLocked();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ClearLocked();
        }
    }

    private void ClearLocked()
    {
        _pending.Clear();
        _active.Clear();
        _layerTarget = 0;
        _layerAutoZeroAt = 0;
        _floor = 0;
        _floorAt = long.MinValue;
        foreach (var wake in _wakes.Values)
        {
            wake?.Dispose();
        }

        _wakes.Clear();
    }

    /// <summary>Queue one rendered pattern. Upstream's <c>Play</c> (<c>HapticMixer.cs:840-866</c>):
    /// the gate refuses here, zero-intensity steps are dropped here, and promotion happens later.</summary>
    private void Post(HapticMoment moment, IReadOnlyList<HapticStep> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        if (!Allowed())
        {
            Refusals++;
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var now = NowLocked();
            foreach (var step in steps)
            {
                if (step.Pulse.Intensity <= 0)
                {
                    // A zero-intensity step is not a quiet step; it is nothing (HapticMixer.cs:855).
                    continue;
                }

                var due = now + Math.Max(0, step.DelayMs);
                _pending.Add(new PendingStep(moment, due, step.Pulse));
                ScheduleWakeLocked(due);
            }
        }
    }

    /// <summary>Upstream's <c>CancelSequence</c> for one kind (<c>HapticMixer.cs:868-878</c> through
    /// <c>HapticService.CancelKind</c> at <c>:413-422</c>).</summary>
    private void CancelMoment(HapticMoment moment)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var dropped = _pending.RemoveAll(p => p.Moment == moment)
                + _active.RemoveAll(a => a.Moment == moment);
            if (dropped > 0)
            {
                // Something audible just stopped, so the level has to be re-evaluated rather than
                // left where the cancelled envelope had it.
                ScheduleWakeLocked(NowLocked());
            }
        }
    }

    private void Wake(long instant)
    {
        double level;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_wakes.Remove(instant, out var handle))
            {
                handle?.Dispose();
            }

            // Upstream's own tick order: promote, then build (HapticMixer.cs, TickAsync).
            PromotePendingLocked(instant);
            level = EvaluateLocked(instant);
        }

        // Outside the lock: the sink is somebody else's process at the far end of a socket, and no
        // lock this class owns may be held across it.
        Send(level);
    }

    /// <summary>
    /// Upstream's <c>PromotePending</c> (<c>HapticMixer.cs:880-931</c>), including the concurrency
    /// cap and its ranking rule.
    ///
    /// <para><b>Upstream's corpse clause is folded into the expiry above and the two are
    /// equivalent.</b> Upstream keeps a finished pulse one extra tick so its final partial window
    /// still renders, then retires such corpses BEFORE evicting anything live (<c>:896-903</c>).
    /// This evaluator samples a pulse AT its own end instant rather than inside a 100 ms window, so
    /// the grace buys nothing and a finished pulse is retired immediately — which leaves exactly the
    /// state upstream reaches after its corpse pass, because a corpse contributes 0 to the level and
    /// is always the first thing evicted.</para>
    /// </summary>
    private void PromotePendingLocked(long now)
    {
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var pending = _pending[i];
            if (pending.DueAt > now)
            {
                continue;
            }

            _pending.RemoveAt(i);
            RetireFinishedLocked(now);

            if (_active.Count >= HapticMix.MaxConcurrentPulses)
            {
                var victim = 0;
                for (var j = 1; j < _active.Count; j++)
                {
                    if (_active[j].Pulse.Priority < _active[victim].Pulse.Priority)
                    {
                        victim = j;
                    }
                }

                if (_active[victim].Pulse.Priority >= pending.Pulse.Priority)
                {
                    // "the new pulse only gets in by out-ranking the weakest thing playing,
                    // otherwise a burst just becomes one flat wall of buzz" (HapticMixer.cs:907-912).
                    PulsesDropped++;
                    continue;
                }

                _active.RemoveAt(victim);
                PulsesEvicted++;
            }

            _active.Add(new ActivePulse(pending.Moment, now, pending.Pulse));
            ScheduleSamplesLocked(now, pending.Pulse);
        }
    }

    private void RetireFinishedLocked(long now)
    {
        for (var i = _active.Count - 1; i >= 0; i--)
        {
            if (now >= _active[i].StartAt + _active[i].Pulse.TotalMs)
            {
                _active.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// The level at one instant, by upstream's <c>BuildOutputs</c> (<c>HapticMixer.cs:441-520</c>):
    /// continuous layers by MAX into a soft-ramped floor, transients summed per priority group and
    /// arbitrated across groups by MAX, the two combined by MAX, then the master arithmetic.
    /// </summary>
    private double EvaluateLocked(long instant)
    {
        RetireFinishedLocked(instant);

        if (_layerAutoZeroAt > 0 && instant >= _layerAutoZeroAt)
        {
            _layerTarget = 0;
            _layerAutoZeroAt = 0;
        }

        // Soft ramp: rises are slew-limited, falls are instant (HapticMixer.cs:470-474). Upstream
        // grants one tick's worth per tick; this grants at least one tick's worth per evaluation and
        // proportionally more across a longer gap, which is the SAME slope without the loop's
        // quantisation. A rise that has not finished schedules its own continuation, so the climb
        // cannot stall for want of a poll.
        var target = _layerTarget;
        if (target > _floor)
        {
            var elapsed = _floorAt == long.MinValue
                ? HapticMix.TickMs
                : Math.Max(HapticMix.TickMs, instant - _floorAt);
            _floor = Math.Min(target, _floor + (elapsed / (double)HapticMix.SoftRampMs));
            if (_floor < target)
            {
                ScheduleWakeLocked(instant + HapticMix.TickMs);
            }
        }
        else
        {
            _floor = target;
        }

        _floorAt = instant;

        _samples.Clear();
        foreach (var active in _active)
        {
            var level = active.Pulse.LevelAt(instant - active.StartAt);
            if (level > 0)
            {
                _samples.Add(new PrioritisedLevel(level, active.Pulse.Priority));
            }
        }

        Evaluations++;
        LastLevel = HapticMix.Combine(_floor, HapticMix.Transient(_samples));
        return LastLevel;
    }

    /// <summary>
    /// The instants this envelope must be looked at: its crest, its decay edge, its end, and
    /// upstream's own 100 ms grid in between.
    ///
    /// <para>The crest and the end are what upstream's <c>PeakInstant</c> and its extra tick of
    /// expiry grace exist to guarantee (<c>HapticMixer.cs:942-949</c>, <c>:1226-1247</c>): a 25 ms
    /// tap can start and finish strictly between two grid points, and an envelope is 0 at its own
    /// t=0. Naming them directly is the same guarantee without the window.</para>
    /// </summary>
    private void ScheduleSamplesLocked(long start, HapticPulse pulse)
    {
        var total = pulse.TotalMs;
        ScheduleWakeLocked(start + pulse.AttackMs);
        ScheduleWakeLocked(start + pulse.AttackMs + pulse.HoldMs);
        ScheduleWakeLocked(start + total);
        for (var offset = HapticMix.TickMs; offset < total; offset += HapticMix.TickMs)
        {
            ScheduleWakeLocked(start + offset);
        }
    }

    private void ScheduleWakeLocked(long instant)
    {
        if (_disposed || _wakes.ContainsKey(instant))
        {
            return;
        }

        // Due is measured from the clock's CURRENT reading, never from the instant that scheduled
        // this one: a wake that is already overdue must fire at once and still render the value its
        // own instant calls for.
        var due = TimeSpan.FromMilliseconds(Math.Max(0, instant - NowLocked()));
        _wakes[instant] = _clock.Schedule(due, () => Wake(instant));
    }

    private long NowLocked() => (long)(_clock.UtcNow - _origin).TotalMilliseconds;

    private void Send(double level)
    {
        if (!Allowed())
        {
            // The tick's own refusal (HapticMixer.cs:253-258): the level was computed, and nothing
            // may be delivered while the gate is shut.
            return;
        }

        var devices = _devices() ?? [];
        if (devices.Count == 0)
        {
            EvaluationsWithNoDevice++;
            return;
        }

        for (var i = 0; i < devices.Count; i++)
        {
            var key = devices[i];
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            // One list per device rather than one shared: two records that alias the same array are
            // a trap for anything that later reads them back, and the allocation is per send.
            var outputs = new[] { new HapticOutput(0, HapticLevel.Of(level)) };
            Sends++;
            Observe(_sink.SetOutputsAsync(key, outputs, CancellationToken.None));
        }
    }

    private bool Allowed()
    {
        try
        {
            return _outputAllowed();
        }
        catch (Exception ex)
        {
            // A gate that cannot answer is CLOSED, which is upstream's own decision
            // (HapticMixer.cs:198-203). Type name only: this string is destined for a log.
            _log?.Invoke("haptics: the output gate threw and was read as closed — " + ex.GetType().Name);
            return false;
        }
    }

    private void Observe(Task<CapabilityState> pending)
    {
        if (pending.IsCompleted)
        {
            Record(pending);
            return;
        }

        _ = pending.ContinueWith(
            Record, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void Record(Task<CapabilityState> finished)
    {
        if (finished.IsCompletedSuccessfully)
        {
            LastOutcome = finished.Result;
            return;
        }

        var ex = finished.Exception?.GetBaseException();
        LastOutcome = new CapabilityState.Faulted(new CapabilityReason(
            HapticReasonCodes.HapticServerUnreachable,
            "the haptic sink faulted while a level was being set: " + (ex?.GetType().Name ?? "unknown")));
        _log?.Invoke("haptics: a level-set faulted and was contained — " + (ex?.GetType().Name ?? "unknown"));
    }

    private sealed record PendingStep(HapticMoment Moment, long DueAt, HapticPulse Pulse);

    private sealed record ActivePulse(HapticMoment Moment, long StartAt, HapticPulse Pulse);
}
