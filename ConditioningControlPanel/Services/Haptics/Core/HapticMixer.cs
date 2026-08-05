using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Haptics.Core
{
    /// <summary>One step of a rendered pattern: fire <see cref="Pulse"/> <see cref="DelayMs"/> after the sequence starts.</summary>
    public readonly struct HapticPulseStep
    {
        public HapticPulseStep(int delayMs, HapticPulse pulse)
        {
            DelayMs = delayMs; Pulse = pulse;
        }
        public int DelayMs { get; }
        public HapticPulse Pulse { get; }
    }

    /// <summary>
    /// Handle to a scheduled pattern. <see cref="Completion"/> finishes when the last step's
    /// envelope has decayed (or immediately on <see cref="Cancel"/>), which is what lets the
    /// legacy await-based API (ApplyVibrationModeAsync and friends) keep its timing without a
    /// single Task.Delay chain or CancellationTokenSource of its own.
    /// </summary>
    public sealed class HapticSequence
    {
        private readonly TaskCompletionSource<bool> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HapticMixer? _mixer;

        internal HapticSequence(HapticMixer? mixer, long id) { _mixer = mixer; Id = id; }

        internal long Id { get; }
        public Task Completion => _tcs.Task;
        internal void Complete() => _tcs.TrySetResult(true);
        public void Cancel() => _mixer?.CancelSequence(this);

        internal static HapticSequence Completed()
        {
            var s = new HapticSequence(null, 0);
            s.Complete();
            return s;
        }
    }

    /// <summary>
    /// The single choke point for every haptic output in the app.
    ///
    /// LAYERS   — continuous 0..1 sources (<see cref="HapticLayer"/>), combined per
    ///            actuator-channel by MAX. Level-set semantics: a layer holds its value until
    ///            somebody changes it, so there is no "keep it alive" command traffic.
    /// PULSES   — transient <see cref="HapticPulse"/> envelopes (attack/hold/decay) layered
    ///            OVER the continuous floor. Equal priorities sum then clamp; different
    ///            priorities take the max; a concurrency cap keeps a burst from turning into
    ///            one flat 100% wall.
    /// OUTPUT   — ONE loop at 10 Hz. Per tick it coalesces to the latest target per actuator,
    ///            quantizes to the actuator's native steps and suppresses unchanged sends, so
    ///            a still scene produces literally zero wire traffic.
    /// SAFETY   — master cap, soft-ramp on any rise from silence, panic stop, shutdown
    ///            watchdog, and the premium + master-enable gate, all evaluated HERE exactly
    ///            once instead of being re-checked in twenty consumer call sites.
    ///
    /// Thread-safety: every mutator is lock-free from the caller's point of view (they take
    /// <c>_gate</c> briefly); the output loop runs on a background thread and never touches WPF.
    /// </summary>
    public sealed class HapticMixer : IDisposable
    {
        // ---- tunables (defaults; the ones users can move live in HapticSettingsV2) ----
        /// <summary>Output loop cadence when anything is live. 10 Hz — the self-imposed rate
        /// limit for the Lovense LAN API, which documents none.</summary>
        public const int DefaultTickMs = 100;
        /// <summary>Idle cadence: nothing is playing, so we only need to notice new work.</summary>
        public const int IdleTickMs = 250;
        /// <summary>Time for the continuous floor to climb from silence to full. Safety, not taste:
        /// a session that resumes at 100% with no ramp is how people get hurt.</summary>
        public const int DefaultSoftRampMs = 800;
        /// <summary>Hard ceiling on any single actuator, applied after the master multiplier.</summary>
        public const double DefaultMasterCap = 0.70;
        /// <summary>Max simultaneously-active transient envelopes.</summary>
        public const int DefaultMaxConcurrentPulses = 4;
        /// <summary>Lowest intensity that still maps to a real vibration level. LovenseProvider
        /// treats intensity &lt;= 0.05 as "off", so any floor of exactly 0.05 silences the device
        /// instead of keeping a faint buzz (#516).</summary>
        public const double MinPerceptibleIntensity = 0.06;
        /// <summary>After a panic stop, refuse to output anything at all for this long.</summary>
        private const int PanicMuteMs = 400;
        /// <summary>Re-send an UNCHANGED target at most this often, so providers that use
        /// short-timeSec repeats get a heartbeat. ZEROS ARE INCLUDED: a Lovense level has no
        /// server-side watchdog, so a zero that never lands leaves the toy running forever and the
        /// provider's 25 s keep-alive re-arms the stale level. A periodic zero costs nothing on the
        /// wire (every provider suppresses unchanged sends itself) and is the only thing that
        /// self-heals a dropped, reordered or IO-failed zero.</summary>
        private const int RefreshMs = 1000;
        /// <summary>Immediately after a device's target falls to zero (or a stop/panic invalidates
        /// its cache) the zero is re-asserted EVERY tick for this long, not just at
        /// <see cref="RefreshMs"/>. Covers the window where the Lovense per-device gate drops
        /// commands because an earlier POST is still in flight.</summary>
        private const int ZeroReassertMs = 2000;
        /// <summary>A wedged provider must not wedge the loop.</summary>
        private const int ProviderCallTimeoutMs = 500;
        /// <summary>Per-provider budget for an all-stop (panic / gate close / shutdown).</summary>
        private static readonly TimeSpan ProviderStopTimeout = TimeSpan.FromMilliseconds(1500);
        /// <summary>How many times a panic all-stop is fired. ONE best-effort POST is not a safety
        /// net: the provider swallows its own failures, and a stale in-flight level command can land
        /// AFTER the stop and re-arm the toy (they are concurrent).</summary>
        private const int PanicStopAttempts = 3;
        /// <summary>Gap between panic all-stop attempts.</summary>
        private const int PanicStopRetryMs = 150;
        /// <summary>Hard cap on the best-effort all-stop during shutdown.</summary>
        public static readonly TimeSpan ShutdownFlushCap = TimeSpan.FromSeconds(2);

        private static readonly int LayerCount = Enum.GetValues(typeof(HapticLayer)).Length;

        private readonly object _gate = new();
        private readonly HapticDeviceManager _devices;
        private readonly HapticSettings _settings;

        // continuous layers
        private readonly double[] _layerValues = new double[LayerCount];
        private readonly long[] _layerAutoZeroAt = new long[LayerCount];
        private readonly LayerEnvelope?[] _layerEnvelopes = new LayerEnvelope?[LayerCount];
        /// <summary>Phase F: optional PER-VIBRATION-MOTOR breakdown of a layer (audio band-split).
        /// Null for every layer in the normal case, and then costs exactly one bool test per tick.
        /// Arrays are replaced wholesale, never mutated, so the loop may read them unlocked.</summary>
        private readonly double[]?[] _layerSplits = new double[]?[LayerCount];
        private volatile bool _anySplit;
        /// <summary>Phase F: continuous layers are muted until this tick (user turned the toy up or
        /// down themselves — see <see cref="SuppressLayersUntil"/>). Transients still fire.</summary>
        private long _layersSuppressedUntil;

        // transients
        private readonly List<ActivePulse> _active = new();
        private readonly List<PendingStep> _pending = new();
        private readonly Dictionary<long, SeqState> _sequences = new();
        private long _nextSeqId = 1;

        // Per-device output state. SINGLE-WRITER DISCIPLINE (M3): every field of DeviceState is
        // written by the OUTPUT LOOP THREAD ONLY (the sole exception being SafeSendAsync's
        // completion, which publishes ONE reference — the quantized array it was handed — and
        // nothing else), and entries are only ever added there. Other threads (panic, gate close,
        // shutdown, the UI) never touch it: they bump _resyncGeneration instead and the loop
        // consumes that at the top of the next tick. The dictionary stays concurrent because those
        // threads may still read it.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DeviceState> _deviceState =
            new(StringComparer.Ordinal);
        /// <summary>Bumped by any path that invalidates what we believe the toys are doing (panic,
        /// all-stop, gate close, ClearAll). The loop compares it against <see cref="_resyncSeen"/>
        /// and then re-stamps every device from scratch.</summary>
        private long _resyncGeneration;
        /// <summary>Loop-thread only.</summary>
        private long _resyncSeen;

        private long _panicMutedUntil;
        private long _testGateUntil;
        private bool _gateWasOpen = true;
        /// <summary>Phase F: the temperament preset in force for the CURRENT tick. Resolved once at
        /// the top of <see cref="TickAsync"/> (a string switch per tick, not per device) and read by
        /// <see cref="PromotePending"/> and <see cref="BuildOutputs"/>, both of which run on the
        /// same loop thread.</summary>
        private HapticTemperament _temperament = HapticTemperament.Default;
        private CancellationTokenSource? _loopCts;
        private Task? _loopTask;
        private bool _disposed;
        /// <summary>One-shot latch for <see cref="ShutdownStopBlocking"/> so the deterministic
        /// shutdown stop cannot burn its budget twice (App.OnExit calls it, then Dispose does).</summary>
        private int _shutdownStopped;

        /// <summary>Fires on the loop thread with a short human string ("FlashClick 55%") for the
        /// UI's activity readout. Subscribers MUST marshal to the dispatcher themselves.</summary>
        public event EventHandler<string>? Activity;

        public HapticMixer(HapticDeviceManager devices, HapticSettings settings)
        {
            _devices = devices;
            _settings = settings;
            _settings.EnsureV2Migrated();
            try { AppDomain.CurrentDomain.ProcessExit += OnProcessExit; } catch { }
        }

        // ===================================================================== gate

        /// <summary>Premium + master toggle, evaluated ONCE here rather than in every consumer.</summary>
        public bool IsGateOpen
        {
            get
            {
                if (!_settings.Enabled && Environment.TickCount64 > Interlocked.Read(ref _testGateUntil)) return false;
                try { return App.Patreon?.HasPremiumAccess ?? false; }
                catch { return false; }
            }
        }

        /// <summary>Let the "Test" button prove the hardware works even when the master toggle is
        /// off (the old code drove the provider directly and so was never gated). Premium is still
        /// required — this only waives <c>Settings.Enabled</c>, and only for a few seconds.</summary>
        public void AllowTestWindow(int ms)
            => Interlocked.Exchange(ref _testGateUntil, Environment.TickCount64 + Math.Clamp(ms, 0, 10_000));

        public double MasterCap => Math.Clamp(_settings.V2.MasterCap, 0.05, 1.0);
        /// <summary>Master intensity multiplier (the repurposed legacy <c>GlobalIntensity</c>).</summary>
        public double MasterIntensity => Math.Clamp(_settings.GlobalIntensity, 0.0, 1.0);

        /// <summary>PHASE F: the configured temperament preset. Every multiplier it carries is
        /// applied AFTER the routing row's own intensity and BEFORE the master multiplier and the
        /// master cap, so the cap always wins. Balanced (the default) is all 1.0.</summary>
        public HapticTemperament Temperament => HapticTemperament.FromKey(_settings.V2.Temperament);

        // ===================================================================== loop

        public void Start()
        {
            lock (_gate)
            {
                if (_disposed || _loopTask != null) return;
                _loopCts = new CancellationTokenSource();
                var ct = _loopCts.Token;
                _loopTask = Task.Run(() => LoopAsync(ct), ct);
            }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                bool busy = false;
                try { busy = await TickAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { App.Logger?.Debug("HapticMixer tick error (non-fatal): {E}", ex.Message); }

                try { await Task.Delay(busy ? DefaultTickMs : IdleTickMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>One 10 Hz tick. Returns true when something is live (keep the fast cadence).</summary>
        private async Task<bool> TickAsync(CancellationToken ct)
        {
            var now = Environment.TickCount64;
            var gateOpen = IsGateOpen;
            // Resolve the temperament ONCE per tick: PromotePending (envelope shaping + the
            // concurrency window) and BuildOutputs (level scaling) must agree within a tick.
            _temperament = HapticTemperament.FromKey(_settings.V2.Temperament);

            if (!gateOpen)
            {
                // Transition open -> closed: drop everything and stop the toys once.
                if (_gateWasOpen)
                {
                    _gateWasOpen = false;
                    lock (_gate) ClearAllInternal(completeSequences: true);
                    _ = SafeStopAllAsync();
                }
                // Still expire sequences so awaiting callers are released.
                ExpireSequences(now);
                return false;
            }
            _gateWasOpen = true;

            // H2: the gate can be open for TWO reasons. When it is open only because a Test window
            // is running (the master toggle is still off), the test's own transients may drive the
            // toy but the accumulated CONTINUOUS layers may not — Video/AudioSync/Dtrh/Pattern hold
            // their last value while the master is off, and letting a 4-10 s test re-open the gate
            // for all of them started the toy on a stale video floor.
            var testWindowOnly = !_settings.Enabled;

            bool anythingLive;
            List<PulseSample>? pulseSamples;
            double[] layerSnapshot;
            double[]?[]? splitSnapshot = null;

            lock (_gate)
            {
                PromotePending(now);
                ExpireActive(now);
                ExpireSequencesLocked(now);
                ApplyLayerEnvelopes(now);

                layerSnapshot = new double[LayerCount];
                for (int i = 0; i < LayerCount; i++) layerSnapshot[i] = _layerValues[i];

                // User-override back-off: the toy's own controls win for a while, so the floor
                // goes away entirely. Transients are deliberately still allowed — an achievement
                // buzz is an event, not the app quietly overriding the user's choice again.
                if (now < _layersSuppressedUntil || testWindowOnly)
                {
                    for (int i = 0; i < LayerCount; i++) layerSnapshot[i] = 0;
                }
                else if (_anySplit)
                {
                    splitSnapshot = (double[]?[])_layerSplits.Clone();
                }

                pulseSamples = _active.Count == 0 ? null : new List<PulseSample>(_active.Count);
                if (pulseSamples != null)
                {
                    foreach (var p in _active)
                    {
                        var v = p.Envelope(now);
                        if (v > 0) pulseSamples.Add(new PulseSample(v, p.Priority, p.Target));
                    }
                }

                anythingLive = _active.Count > 0 || _pending.Count > 0;
                for (int i = 0; i < LayerCount && !anythingLive; i++)
                    if (_layerValues[i] > 0) anythingLive = true;

                if (now < _panicMutedUntil) { layerSnapshot = new double[LayerCount]; pulseSamples = null; anythingLive = true; }
            }

            // Consume any pending invalidation (panic / all-stop / gate close) BEFORE building this
            // tick's outputs, so the first thing we do after a stop is re-stamp the real target.
            ConsumeResync(now);

            var devices = _devices.Devices;
            if (devices.Count == 0) return anythingLive;

            List<Task>? sends = null;
            foreach (var device in devices)
            {
                IReadOnlyList<ActuatorOutput>? outputs;
                bool changed;
                int[]? pending;

                if (!device.Enabled || !device.IsConnected)
                {
                    // H1: a toy that leaves the drive set (the user disabled it, or it fell off the
                    // air) must not keep running its last level — Lovense's keep-alive would refresh
                    // it forever. Drive it to zero exactly like any other target; if it is already
                    // unreachable the send is a swallowed no-op and the zero re-assert keeps trying
                    // for as long as the device stays registered.
                    var st = GetDeviceState(device.DeviceKey);
                    if (!device.IsConnected) st.Offline = true;
                    outputs = BuildOutputs(device, ZeroLayers, null, null, now, out changed, out pending);
                }
                else
                {
                    var st = GetDeviceState(device.DeviceKey);
                    // Back from the dead: whatever this toy is doing now, we did not put it there
                    // (our zeros went nowhere while it was off the air), so forget the cache.
                    if (st.Offline) { st.Offline = false; Invalidate(st, now); }
                    outputs = BuildOutputs(device, layerSnapshot, splitSnapshot, pulseSamples, now, out changed, out pending);
                }

                if (outputs == null || !changed) continue;
                sends ??= new List<Task>(devices.Count);
                sends.Add(SafeSendAsync(device, outputs, pending, ct));
            }

            if (sends != null && sends.Count > 0)
            {
                var all = Task.WhenAll(sends);
                // A provider that hangs must not wedge the mixer; it gets one tick's grace.
                await Task.WhenAny(all, Task.Delay(ProviderCallTimeoutMs, ct)).ConfigureAwait(false);
            }

            return anythingLive;
        }

        /// <summary>Providers must not throw per the contract, but a broken one must never fault the
        /// mixer's WhenAll (which we may abandon on timeout -> UnobservedTaskException).
        ///
        /// The quantized cache is committed HERE, only after the send came back clean: writing it
        /// at build time meant a failed send was never retried, and for a zero that is the
        /// difference between "the toy stops" and "the toy buzzes until it is unplugged".</summary>
        private async Task SafeSendAsync(HapticDevice device, IReadOnlyList<ActuatorOutput> outputs,
                                         int[]? pending, CancellationToken ct)
        {
            try
            {
                await _devices.SendAsync(device, outputs, ct).ConfigureAwait(false);
                // A late commit (this send was overtaken by a newer tick) can only ever cause a
                // redundant re-send next tick, never a missed one: we only ever record levels the
                // provider actually accepted.
                if (pending != null) GetDeviceState(device.DeviceKey).Quantized = pending;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { App.Logger?.Debug("Haptic send to {Device} failed: {E}", device.DeviceKey, ex.Message); }
        }

        /// <summary>Combine floor + transients for one device and turn them into actuator outputs.
        /// Returns null when there is nothing to send. <paramref name="pending"/> is the quantized
        /// cache the caller must commit once (and only once) the send succeeds.</summary>
        private IReadOnlyList<ActuatorOutput>? BuildOutputs(
            HapticDevice device, double[] layers, double[]?[]? splits,
            List<PulseSample>? pulses, long now, out bool changed, out int[]? pending)
        {
            changed = false;
            pending = null;
            var v2 = _settings.V2;
            // Phase F temperament: applied AFTER the routing row's intensity and BEFORE Finish()
            // (master multiplier + master cap + per-device trim), so the cap always wins.
            var temperament = _temperament;

            // --- per-motor split (Phase F audio band-split), only on multi-vibe toys ---
            double[]? motorFloors = null;
            if (splits != null)
            {
                int motors = 0;
                foreach (var a in device.Actuators) if (a.Type == ActuatorType.Vibrate) motors++;
                if (motors >= 2) motorFloors = new double[motors];
            }

            // --- continuous floor (role-filtered, per-layer scaled) ---
            double floorTarget = 0;
            for (int i = 0; i < LayerCount; i++)
            {
                var value = layers[i];
                if (value <= 0) continue;
                var rule = v2.Rule((HapticLayer)i);
                if (!rule.Enabled) continue;
                if (!RoleMatches(rule.Target, device.Role)) continue;
                var layerScale = Math.Clamp(rule.Intensity, 0, 1) * temperament.ContinuousScale;
                var scaled = value * layerScale;
                if (scaled > floorTarget) floorTarget = scaled;

                if (motorFloors == null) continue;
                // A split layer contributes its OWN value per motor; an unsplit layer contributes
                // the same value to all of them. SetLayerPerActuator keeps the scalar layer value
                // at max(perMotor), so floorTarget above always covers every motor.
                var split = splits![i];
                for (int m = 0; m < motorFloors.Length; m++)
                {
                    var v = split == null ? value : split[Math.Min(m, split.Length - 1)];
                    var s = v * layerScale;
                    if (s > motorFloors[m]) motorFloors[m] = s;
                }
            }

            var state = GetDeviceState(device.DeviceKey);

            // --- soft ramp: rises are slew-limited, falls are instant ---
            var rampMs = Math.Max(1, v2.SoftRampMs);
            var maxRise = DefaultTickMs / (double)rampMs;
            if (floorTarget > state.Floor) state.Floor = Math.Min(floorTarget, state.Floor + maxRise);
            else state.Floor = floorTarget;

            // --- transients: sum within a priority group, max across groups ---
            double transient = 0;
            if (pulses != null && pulses.Count > 0)
            {
                // Priorities are few; a straight scan grouped by value is cheaper than a dictionary.
                foreach (var priority in DistinctPriorities(pulses))
                {
                    double groupSum = 0;
                    foreach (var s in pulses)
                    {
                        if (s.Priority != priority) continue;
                        if (!RoleMatches(s.Target, device.Role)) continue;
                        groupSum += s.Value * temperament.TransientScale;
                    }
                    if (groupSum > 1) groupSum = 1;
                    if (groupSum > transient) transient = groupSum;
                }
            }

            var raw = Math.Max(state.Floor, transient);

            // --- master multiplier, hard cap, per-device trim ---
            double Finish(double rawLevel)
            {
                // The perceptible floor is applied BEFORE the cap: the cap is a SAFETY ceiling and
                // must be inviolable (a 0.05 MasterCap used to emit 0.06). #516 asked for a faint
                // buzz to survive quantization, not for the ceiling to leak.
                var v = rawLevel * MasterIntensity;
                if (rawLevel > 0) v = Math.Max(v, MinPerceptibleIntensity);
                v = Math.Min(v, MasterCap);
                return Math.Clamp(v * Math.Clamp(device.IntensityTrim, 0, 1), 0, 1);
            }

            var value2 = Finish(raw);

            // Band-split: the ramped floor is redistributed across the motors by the ratio the
            // layer asked for, so soft-ramp / cap / trim / master all still apply exactly once.
            double[]? motorRatios = null;
            if (motorFloors != null && floorTarget > 0)
            {
                motorRatios = new double[motorFloors.Length];
                bool differs = false;
                for (int m = 0; m < motorFloors.Length; m++)
                {
                    motorRatios[m] = Math.Clamp(motorFloors[m] / floorTarget, 0, 1);
                    if (motorRatios[m] < 0.999) differs = true;
                }
                if (!differs) motorRatios = null;   // all motors equal => normal path
            }

            // --- quantize + suppress unchanged ---
            var actuators = device.Actuators;
            if (actuators.Count == 0) return null;

            var previous = state.Quantized;
            if (previous == null || previous.Length != actuators.Count)
            {
                previous = new int[actuators.Count];
                for (int i = 0; i < previous.Length; i++) previous[i] = -1;   // -1 = unknown
                state.Quantized = previous;
            }
            // Cloned, never mutated in place: this array is published to the cache only once the
            // provider has accepted it (SafeSendAsync). Skipped actuators keep their old entry so
            // they can never look "changed".
            var next = (int[])previous.Clone();

            List<ActuatorOutput>? outputs = null;
            bool allZero = true;
            for (int i = 0; i < actuators.Count; i++)
            {
                var a = actuators[i];
                // Position is absolute placement, not a level — FunScript drives it through
                // SetPositionAsync. Stroke is a range pair, likewise not a generic level.
                if (a.Type == ActuatorType.Position || a.Type == ActuatorType.Stroke) continue;

                var level = value2;
                if (motorRatios != null && a.Type == ActuatorType.Vibrate)
                {
                    var m = Math.Clamp(a.Index, 0, motorRatios.Length - 1);
                    level = Finish(Math.Max(state.Floor * motorRatios[m], transient));
                }

                var steps = Math.Max(1, a.Steps);
                var q = (int)Math.Round(level * steps);
                if (q != 0) allZero = false;
                if (previous[i] != q) changed = true;
                next[i] = q;

                outputs ??= new List<ActuatorOutput>(actuators.Count);
                outputs.Add(new ActuatorOutput(a.Type, a.Index, q / (double)steps));

            }

            if (outputs == null) return null;

            // A target that has just FALLEN to zero opens the re-assert window: for the next couple
            // of seconds the zero is stamped on every tick, because the Lovense per-device gate
            // silently drops a command while an earlier POST is in flight (routine at 10 Hz vs a 4 s
            // command timeout) and an undelivered zero means a toy that never stops.
            if (allZero && changed) state.ZeroHoldUntil = now + ZeroReassertMs;

            if (!changed)
            {
                // Unchanged. A zero is re-asserted every tick inside the window and then rides the
                // same slow heartbeat as a held level: the heartbeat is free when it lands (every
                // provider suppresses unchanged sends itself) and is what heals a dropped zero, a
                // reordered send, or a provider cache invalidated by an IO failure.
                if (allZero && now < state.ZeroHoldUntil) changed = true;
                else if (now - state.LastSendAt < RefreshMs) return null;
                else changed = true;
            }

            state.LastSendAt = now;
            pending = next;
            return outputs;
        }

        /// <summary>All-zero layer snapshot for devices that are being driven OUT of the mix
        /// (disabled / disconnected). Never mutated.</summary>
        private static readonly double[] ZeroLayers = new double[LayerCount];

        /// <summary>Forget what we believe a device is doing, and re-assert its target hard for the
        /// next couple of seconds. Loop thread only.</summary>
        private static void Invalidate(DeviceState state, long now)
        {
            state.Floor = 0;
            state.LastSendAt = 0;
            state.ZeroHoldUntil = now + ZeroReassertMs;
            var q = state.Quantized;
            if (q != null)
            {
                var fresh = new int[q.Length];
                for (int i = 0; i < fresh.Length; i++) fresh[i] = -1;
                state.Quantized = fresh;
            }
        }

        /// <summary>Loop thread: apply any invalidation requested by a panic / stop / gate close.</summary>
        private void ConsumeResync(long now)
        {
            var gen = Interlocked.Read(ref _resyncGeneration);
            if (gen == _resyncSeen) return;
            _resyncSeen = gen;
            foreach (var s in _deviceState.Values) Invalidate(s, now);
        }

        /// <summary>Tell the loop that what we believe about the toys is no longer trustworthy.
        /// Safe from ANY thread — it never touches DeviceState itself (M3).</summary>
        private void RequestResync() => Interlocked.Increment(ref _resyncGeneration);

        /// <summary>Forget the quantized cache for every device: the next tick re-transmits each
        /// device's real target instead of suppressing it as unchanged. Anything that drives a
        /// provider AROUND the mixer (the per-toy Test button) must call this on the way out, or the
        /// mixer will keep believing the toy is at the level IT last sent. Safe from any thread.</summary>
        public void InvalidateDeviceCache() => RequestResync();

        private static IEnumerable<int> DistinctPriorities(List<PulseSample> pulses)
        {
            // pulses is tiny (cap 4) — an allocation-free O(n^2) distinct beats a HashSet.
            for (int i = 0; i < pulses.Count; i++)
            {
                bool seen = false;
                for (int j = 0; j < i; j++) if (pulses[j].Priority == pulses[i].Priority) { seen = true; break; }
                if (!seen) yield return pulses[i].Priority;
            }
        }

        private static bool RoleMatches(ToyRole target, ToyRole deviceRole)
            => target == ToyRole.All || deviceRole == ToyRole.All || deviceRole == target;

        private DeviceState GetDeviceState(string key) => _deviceState.GetOrAdd(key, _ => new DeviceState());

        // ===================================================================== layers

        /// <summary>Set a continuous layer's level. <paramref name="autoZeroMs"/> &gt; 0 makes it
        /// self-clear after that long (used by the live-intensity slider so a drag can't leave the
        /// toy running forever).</summary>
        public void SetLayer(HapticLayer layer, double value, int autoZeroMs = 0)
        {
            if (_disposed) return;
            value = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
            lock (_gate)
            {
                var i = (int)layer;
                _layerEnvelopes[i] = null;                       // an explicit set wins over a running envelope
                _layerValues[i] = value;
                _layerAutoZeroAt[i] = (value > 0 && autoZeroMs > 0) ? Environment.TickCount64 + autoZeroMs : 0;
                if (_layerSplits[i] != null) { _layerSplits[i] = null; RecomputeAnySplitLocked(); }
            }
            if (value > 0) Start();
        }

        /// <summary>
        /// PHASE F (audio band-split). Set a continuous layer as a PER-VIBRATION-MOTOR breakdown:
        /// <paramref name="perMotor"/>[0] drives Vibrate actuator index 0, [1] index 1, and so on
        /// (a shorter array repeats its last entry). Toys with fewer than two Vibrate actuators
        /// ignore the breakdown entirely and keep the scalar behaviour, which is <c>max(perMotor)</c>
        /// — so a single-motor toy feels exactly what it feels today.
        ///
        /// Passing null (or an empty array) clears the breakdown and leaves the layer scalar.
        /// The split rides through soft-ramp, master multiplier, master cap and per-device trim
        /// unchanged: only the RATIO between motors comes from here.
        /// </summary>
        public void SetLayerPerActuator(HapticLayer layer, double[]? perMotor)
        {
            if (_disposed) return;

            double[]? copy = null;
            double scalar = 0;
            if (perMotor != null && perMotor.Length > 0)
            {
                copy = new double[perMotor.Length];
                for (int i = 0; i < copy.Length; i++)
                {
                    var v = double.IsNaN(perMotor[i]) ? 0 : Math.Clamp(perMotor[i], 0, 1);
                    copy[i] = v;
                    if (v > scalar) scalar = v;
                }
            }

            lock (_gate)
            {
                var i = (int)layer;
                _layerEnvelopes[i] = null;
                _layerSplits[i] = copy;
                _layerValues[i] = scalar;
                _layerAutoZeroAt[i] = 0;
                RecomputeAnySplitLocked();
            }
            if (scalar > 0) Start();
        }

        private void RecomputeAnySplitLocked()
        {
            for (int i = 0; i < LayerCount; i++)
            {
                if (_layerSplits[i] != null) { _anySplit = true; return; }
            }
            _anySplit = false;
        }

        /// <summary>
        /// PHASE F (user-override back-off). Mute every CONTINUOUS layer until <paramref name="untilUtc"/>.
        /// Called when a toy reports that the USER changed its strength themselves: the app stops
        /// pushing a floor for a while instead of fighting them for the dial. Transient events are
        /// deliberately unaffected. A time in the past clears the suppression.
        /// </summary>
        public void SuppressLayersUntil(DateTime untilUtc)
        {
            if (_disposed) return;
            var ms = (untilUtc.ToUniversalTime() - DateTime.UtcNow).TotalMilliseconds;
            var until = ms <= 0 ? 0 : Environment.TickCount64 + (long)Math.Min(ms, 30 * 60_000);
            lock (_gate) _layersSuppressedUntil = until;
            if (until > 0) Start();
        }

        /// <summary>True while <see cref="SuppressLayersUntil"/> is holding the floor at zero.</summary>
        public bool AreLayersSuppressed
        {
            get { lock (_gate) return Environment.TickCount64 < _layersSuppressedUntil; }
        }

        public double GetLayer(HapticLayer layer)
        {
            lock (_gate) return _layerValues[(int)layer];
        }

        /// <summary>Play a pre-computed envelope on a continuous layer (audio-sync / Deeper
        /// keyframe patterns). Replaces any running envelope on the same layer; the layer returns
        /// to 0 when it ends.</summary>
        public void PlayLayerEnvelope(HapticLayer layer, IReadOnlyList<double> values, int totalMs)
        {
            if (_disposed || values == null || values.Count == 0 || totalMs <= 0) return;
            var copy = new double[values.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = Math.Clamp(values[i], 0, 1);
            lock (_gate)
            {
                var i = (int)layer;
                _layerEnvelopes[i] = new LayerEnvelope
                {
                    Values = copy,
                    StartAt = Environment.TickCount64,
                    TotalMs = totalMs
                };
                _layerAutoZeroAt[i] = 0;
                if (_layerSplits[i] != null) { _layerSplits[i] = null; RecomputeAnySplitLocked(); }
            }
            Start();
        }

        private void ApplyLayerEnvelopes(long now)
        {
            for (int i = 0; i < LayerCount; i++)
            {
                var env = _layerEnvelopes[i];
                if (env != null)
                {
                    var elapsed = now - env.StartAt;
                    if (elapsed >= env.TotalMs)
                    {
                        _layerEnvelopes[i] = null;
                        _layerValues[i] = 0;
                    }
                    else
                    {
                        var pos = elapsed / (double)env.TotalMs * env.Values.Length;
                        var idx = Math.Clamp((int)pos, 0, env.Values.Length - 1);
                        _layerValues[i] = env.Values[idx];
                    }
                    continue;
                }

                var zeroAt = _layerAutoZeroAt[i];
                if (zeroAt != 0 && now >= zeroAt)
                {
                    _layerAutoZeroAt[i] = 0;
                    _layerValues[i] = 0;
                }
            }
        }

        // ===================================================================== transients

        /// <summary>Fire a single transient envelope right now.</summary>
        public HapticSequence Post(HapticPulse pulse) => Play(new[] { new HapticPulseStep(0, pulse) });

        /// <summary>Schedule a rendered pattern. Steps are promoted to active pulses by the output
        /// loop — no Task.Delay chains, no CancellationTokenSource per event, so none of the
        /// dispose races the old service had.</summary>
        public HapticSequence Play(IReadOnlyList<HapticPulseStep> steps)
        {
            if (_disposed || steps == null || steps.Count == 0) return HapticSequence.Completed();
            if (!IsGateOpen) return HapticSequence.Completed();

            var now = Environment.TickCount64;
            HapticSequence seq;
            lock (_gate)
            {
                var id = _nextSeqId++;
                seq = new HapticSequence(this, id);
                long end = now;
                foreach (var step in steps)
                {
                    var p = step.Pulse;
                    if (p.Intensity <= 0) continue;
                    var due = now + Math.Max(0, step.DelayMs);
                    _pending.Add(new PendingStep { SeqId = id, DueAt = due, Pulse = p });
                    var stepEnd = due + Math.Max(0, p.AttackMs) + Math.Max(0, p.HoldMs) + Math.Max(0, p.DecayMs);
                    if (stepEnd > end) end = stepEnd;
                }
                if (_pending.Count == 0 && end == now) { seq.Complete(); return seq; }
                _sequences[id] = new SeqState { Handle = seq, EndAt = end };
            }
            Start();
            return seq;
        }

        public void CancelSequence(HapticSequence? seq)
        {
            if (seq == null) return;
            lock (_gate)
            {
                _pending.RemoveAll(p => p.SeqId == seq.Id);
                _active.RemoveAll(p => p.SeqId == seq.Id);
                _sequences.Remove(seq.Id);
            }
            seq.Complete();
        }

        private void PromotePending(long now)
        {
            if (_pending.Count == 0) return;
            var temperament = _temperament;
            // Phase F: the temperament's pulse-priority bias widens (Intense/Cruel) or narrows
            // (Gentle) the concurrency window. Priority only ever decides anything when that window
            // is full, so this is what "bias" means in practice.
            var max = Math.Clamp(Math.Max(1, _settings.V2.MaxConcurrentPulses) + temperament.PulsePriorityBias, 1, 12);
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (p.DueAt > now) continue;
                _pending.RemoveAt(i);

                if (_active.Count >= max)
                {
                    // Concurrency cap: the new pulse only gets in by out-ranking the weakest
                    // thing playing, otherwise a burst just becomes one flat wall of buzz.
                    int weakest = 0;
                    for (int j = 1; j < _active.Count; j++)
                        if (_active[j].Priority < _active[weakest].Priority) weakest = j;
                    if (_active[weakest].Priority >= p.Pulse.Priority) continue;
                    _active.RemoveAt(weakest);
                }

                _active.Add(new ActivePulse
                {
                    SeqId = p.SeqId,
                    StartAt = now,
                    Intensity = Math.Clamp(p.Pulse.Intensity, 0, 1),
                    // Attack/decay are stretched or compressed by the temperament; HOLD is left
                    // alone so a pattern keeps its rhythm and only changes its edge.
                    AttackMs = ScaleMs(p.Pulse.AttackMs, temperament.AttackScale),
                    HoldMs = Math.Max(0, p.Pulse.HoldMs),
                    DecayMs = ScaleMs(p.Pulse.DecayMs, temperament.DecayScale),
                    Priority = p.Pulse.Priority,
                    Target = p.Pulse.Target
                });
            }
        }

        /// <summary>Scale an envelope segment length, never below 0 and never past a tick's worth of
        /// silliness. A zero segment stays zero (an instant edge is a deliberate shape).</summary>
        private static int ScaleMs(int ms, double scale)
        {
            if (ms <= 0) return 0;
            var v = (int)Math.Round(ms * scale);
            return Math.Clamp(v, 1, 60_000);
        }

        private void ExpireActive(long now)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
                if (now >= _active[i].StartAt + _active[i].TotalMs) _active.RemoveAt(i);
        }

        private void ExpireSequences(long now)
        {
            lock (_gate) ExpireSequencesLocked(now);
        }

        private void ExpireSequencesLocked(long now)
        {
            if (_sequences.Count == 0) return;
            List<long>? done = null;
            foreach (var kv in _sequences)
            {
                if (now < kv.Value.EndAt) continue;
                (done ??= new List<long>()).Add(kv.Key);
            }
            if (done == null) return;
            foreach (var id in done)
            {
                if (_sequences.TryGetValue(id, out var s)) { _sequences.Remove(id); s.Handle.Complete(); }
            }
        }

        // ===================================================================== safety

        /// <summary>Everything off, immediately, bypassing throttles and unchanged-send
        /// suppression. Safe to call from any thread at any time, including during shutdown.</summary>
        public void PanicStop()
        {
            lock (_gate)
            {
                ClearAllInternal(completeSequences: true);
                _panicMutedUntil = Environment.TickCount64 + PanicMuteMs;
            }
            App.Logger?.Warning("Haptics: PANIC STOP — all layers zeroed, all providers stopped");
            _ = PanicStopAsync();
        }

        /// <summary>
        /// The panic all-stop, RETRIED. One best-effort POST is not a safety-of-last-resort: the
        /// providers swallow their own stop failures, and a level command that was already in flight
        /// can land AFTER the stop and re-arm the toy (they are concurrent — StopAllAsync takes no
        /// per-device gate). Every attempt also invalidates our quantized cache, so the output loop
        /// keeps stamping real zeros on top for the whole re-assert window instead of believing the
        /// devices are already silent.
        /// </summary>
        private async Task PanicStopAsync()
        {
            for (int attempt = 1; attempt <= PanicStopAttempts; attempt++)
            {
                if (attempt > 1)
                {
                    try { await Task.Delay(PanicStopRetryMs).ConfigureAwait(false); } catch { }
                }
                var ok = await SafeStopAllAsync().ConfigureAwait(false);
                if (!ok)
                    App.Logger?.Warning("Haptics: panic all-stop attempt {Attempt}/{Total} did not complete cleanly",
                                        attempt, PanicStopAttempts);
            }
        }

        /// <summary>Drop every transient and zero every layer WITHOUT touching the providers
        /// (the loop will send the zeros). Used by the legacy StopAsync shim.</summary>
        public void ClearAll()
        {
            lock (_gate) ClearAllInternal(completeSequences: true);
        }

        private void ClearAllInternal(bool completeSequences)
        {
            _pending.Clear();
            _active.Clear();
            for (int i = 0; i < LayerCount; i++)
            {
                _layerValues[i] = 0;
                _layerAutoZeroAt[i] = 0;
                _layerEnvelopes[i] = null;
                _layerSplits[i] = null;
            }
            _anySplit = false;
            // DeviceState belongs to the loop thread (M3): ask for a resync instead of zeroing it
            // here. That also re-opens the zero re-assert window, so the loop stamps real zeros.
            RequestResync();
            if (!completeSequences) return;
            foreach (var s in _sequences.Values) s.Handle.Complete();
            _sequences.Clear();
        }

        /// <summary>All-stop across every provider, in parallel and bounded. Returns true when every
        /// connected provider reported a clean stop.</summary>
        private async Task<bool> SafeStopAllAsync()
        {
            try
            {
                var ok = await _devices.StopAllAsync(ProviderStopTimeout).ConfigureAwait(false);
                // Deliberately does NOT record "the devices are now at zero". If this stop was
                // dropped — or a stale level POST lands after it — believing it would suppress every
                // future zero and the toy would run forever. Mark the cache UNKNOWN instead: the
                // next tick re-transmits the real target, and keeps doing so for the re-assert
                // window. (Providers clear their own suppression cache on StopAll, so those zeros
                // genuinely reach the wire.)
                RequestResync();
                return ok;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Haptics StopAll failed (non-fatal): {E}", ex.Message);
                RequestResync();
                return false;
            }
        }

        /// <summary>Best-effort all-stop with a hard cap. Never blocks a caller longer than
        /// <paramref name="cap"/>, and is safe to fire-and-forget.</summary>
        public async Task FlushStopAsync(TimeSpan cap)
        {
            try
            {
                var stop = SafeStopAllAsync();
                await Task.WhenAny(stop, Task.Delay(cap)).ConfigureAwait(false);
            }
            catch { }
        }

        /// <summary>
        /// SYNCHRONOUS, bounded all-stop for the deterministic shutdown path. This MUST run before
        /// the device manager (and with it the providers' HttpClients) is disposed: <c>Dispose</c>
        /// used to only SCHEDULE the flush on a background task and the very next line tore the
        /// providers down, so the toy was essentially never told to stop. The
        /// <c>AppDomain.ProcessExit</c> watchdog cannot cover it either — App.OnExit ends in
        /// <c>TerminateProcess</c>, which skips ProcessExit handlers by design.
        ///
        /// One-shot: the second caller (App.OnExit then Dispose) is a no-op rather than a second
        /// wait. Never throws, and never waits longer than <paramref name="cap"/> (+ a small slack).
        /// </summary>
        public void ShutdownStopBlocking(TimeSpan? cap = null)
        {
            if (Interlocked.Exchange(ref _shutdownStopped, 1) != 0) return;
            var budget = cap ?? ShutdownFlushCap;
            try
            {
                lock (_gate) ClearAllInternal(completeSequences: true);
                // Task.Run so no continuation ever needs the (already shutting down) UI dispatcher.
                Task.Run(() => FlushStopAsync(budget)).Wait(budget + TimeSpan.FromMilliseconds(250));
            }
            catch (Exception ex) { App.Logger?.Debug("Haptics shutdown stop failed (non-fatal): {E}", ex.Message); }
        }

        /// <summary>Shutdown watchdog. ProcessExit runs on a background thread with its own budget,
        /// so a bounded blocking wait HERE is correct — never on the UI thread.</summary>
        private void OnProcessExit(object? sender, EventArgs e)
        {
            try
            {
                lock (_gate) ClearAllInternal(completeSequences: true);
                FlushStopAsync(ShutdownFlushCap).Wait(ShutdownFlushCap + TimeSpan.FromMilliseconds(200));
            }
            catch { }
        }

        /// <summary>Targeted absolute-position write (Solace Pro). Deliberately NOT driven by the
        /// generic mixer — position is placement, not intensity. Phase F (FunScript) owns this.</summary>
        public Task SetPositionAsync(string deviceKey, double position01, CancellationToken ct = default)
        {
            if (_disposed || !IsGateOpen) return Task.CompletedTask;
            var device = _devices.Find(deviceKey);
            if (device == null || !device.Enabled) return Task.CompletedTask;
            var actuator = device.Actuators.FirstOrDefault(a => a.Type == ActuatorType.Position);
            if (actuator == null) return Task.CompletedTask;
            var outputs = new[] { new ActuatorOutput(ActuatorType.Position, actuator.Index, Math.Clamp(position01, 0, 1)) };
            return _devices.SendAsync(device, outputs, ct);
        }

        internal void RaiseActivity(string message)
        {
            try { Activity?.Invoke(this, message); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { AppDomain.CurrentDomain.ProcessExit -= OnProcessExit; } catch { }

            CancellationTokenSource? cts;
            lock (_gate)
            {
                ClearAllInternal(completeSequences: true);
                cts = _loopCts;
                _loopCts = null;
                _loopTask = null;
            }
            try { cts?.Cancel(); } catch { }

            // C2: the all-stop has to REACH the toy before the providers (and their HttpClients)
            // are disposed on the next line of HapticService.Dispose, so it is a bounded BLOCKING
            // flush, not the old fire-and-forget task that disposal always outran. One-shot: when
            // App.OnExit already flushed on the deterministic path this costs nothing.
            ShutdownStopBlocking();

            // Let the loop notice the cancel before the CTS goes away — disposing it while a
            // Task.Delay(ct) is still pending throws ObjectDisposedException on a pool thread.
            var doomed = cts;
            if (doomed != null)
            {
                _ = Task.Run(async () =>
                {
                    try { await Task.Delay(1000).ConfigureAwait(false); } catch { }
                    try { doomed.Dispose(); } catch { }
                });
            }
        }

        // ===================================================================== state types

        private sealed class ActivePulse
        {
            public long SeqId;
            public long StartAt;
            public double Intensity;
            public int AttackMs, HoldMs, DecayMs, Priority;
            public ToyRole Target;
            public int TotalMs => AttackMs + HoldMs + DecayMs;

            public double Envelope(long now)
            {
                var t = now - StartAt;
                if (t < 0) return 0;
                if (t < AttackMs) return AttackMs <= 0 ? Intensity : Intensity * (t / (double)AttackMs);
                t -= AttackMs;
                if (t < HoldMs) return Intensity;
                t -= HoldMs;
                if (t < DecayMs) return DecayMs <= 0 ? 0 : Intensity * (1.0 - t / (double)DecayMs);
                return 0;
            }
        }

        private sealed class PendingStep
        {
            public long SeqId;
            public long DueAt;
            public HapticPulse Pulse;
        }

        private sealed class SeqState
        {
            public HapticSequence Handle = null!;
            public long EndAt;
        }

        private sealed class LayerEnvelope
        {
            public double[] Values = Array.Empty<double>();
            public long StartAt;
            public int TotalMs;
        }

        /// <summary>Loop-thread-owned output state for one device (see the single-writer note on
        /// <c>_deviceState</c>).</summary>
        private sealed class DeviceState
        {
            public double Floor;
            /// <summary>Last quantized levels the provider ACCEPTED (-1 = unknown). Replaced
            /// wholesale, never mutated, and only once a send came back clean.</summary>
            public int[]? Quantized;
            public long LastSendAt;
            /// <summary>While <c>now</c> is below this, an all-zero target is re-sent every tick
            /// even when it looks unchanged.</summary>
            public long ZeroHoldUntil;
            /// <summary>True while the device is off the air, so that its return invalidates the
            /// cache (whatever it is doing now, we did not put it there).</summary>
            public bool Offline;
        }

        private readonly struct PulseSample
        {
            public PulseSample(double value, int priority, ToyRole target)
            {
                Value = value; Priority = priority; Target = target;
            }
            public double Value { get; }
            public int Priority { get; }
            public ToyRole Target { get; }
        }
    }
}
