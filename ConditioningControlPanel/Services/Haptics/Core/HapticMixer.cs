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
        /// <summary>Re-send an UNCHANGED non-zero target at most this often, so providers that
        /// use short-timeSec repeats get a heartbeat. Zero targets are never re-sent.</summary>
        private const int RefreshMs = 1000;
        /// <summary>A wedged provider must not wedge the loop.</summary>
        private const int ProviderCallTimeoutMs = 500;
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

        // transients
        private readonly List<ActivePulse> _active = new();
        private readonly List<PendingStep> _pending = new();
        private readonly Dictionary<long, SeqState> _sequences = new();
        private long _nextSeqId = 1;

        // Per-device output state. Concurrent because the loop thread ADDS entries outside
        // _gate (in BuildOutputs) while panic/stop paths enumerate them under _gate.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DeviceState> _deviceState =
            new(StringComparer.Ordinal);

        private long _panicMutedUntil;
        private long _testGateUntil;
        private bool _gateWasOpen = true;
        private CancellationTokenSource? _loopCts;
        private Task? _loopTask;
        private bool _disposed;

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

            bool anythingLive;
            List<PulseSample>? pulseSamples;
            double[] layerSnapshot;

            lock (_gate)
            {
                PromotePending(now);
                ExpireActive(now);
                ExpireSequencesLocked(now);
                ApplyLayerEnvelopes(now);

                layerSnapshot = new double[LayerCount];
                for (int i = 0; i < LayerCount; i++) layerSnapshot[i] = _layerValues[i];

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

            var devices = _devices.Devices;
            if (devices.Count == 0) return anythingLive;

            List<Task>? sends = null;
            foreach (var device in devices)
            {
                if (!device.Enabled || !device.IsConnected) continue;
                var outputs = BuildOutputs(device, layerSnapshot, pulseSamples, now, out var changed);
                if (outputs == null || !changed) continue;
                sends ??= new List<Task>(devices.Count);
                sends.Add(SafeSendAsync(device, outputs, ct));
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
        /// mixer's WhenAll (which we may abandon on timeout -> UnobservedTaskException).</summary>
        private async Task SafeSendAsync(HapticDevice device, IReadOnlyList<ActuatorOutput> outputs, CancellationToken ct)
        {
            try { await _devices.SendAsync(device, outputs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { App.Logger?.Debug("Haptic send to {Device} failed: {E}", device.DeviceKey, ex.Message); }
        }

        /// <summary>Combine floor + transients for one device and turn them into actuator outputs.
        /// Returns null when there is nothing to send.</summary>
        private IReadOnlyList<ActuatorOutput>? BuildOutputs(
            HapticDevice device, double[] layers, List<PulseSample>? pulses, long now, out bool changed)
        {
            changed = false;
            var v2 = _settings.V2;

            // --- continuous floor (role-filtered, per-layer scaled) ---
            double floorTarget = 0;
            for (int i = 0; i < LayerCount; i++)
            {
                var value = layers[i];
                if (value <= 0) continue;
                var rule = v2.Rule((HapticLayer)i);
                if (!rule.Enabled) continue;
                if (!RoleMatches(rule.Target, device.Role)) continue;
                var scaled = value * Math.Clamp(rule.Intensity, 0, 1);
                if (scaled > floorTarget) floorTarget = scaled;
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
                        groupSum += s.Value;
                    }
                    if (groupSum > 1) groupSum = 1;
                    if (groupSum > transient) transient = groupSum;
                }
            }

            var raw = Math.Max(state.Floor, transient);

            // --- master multiplier, hard cap, per-device trim ---
            var value2 = Math.Min(raw * MasterIntensity, MasterCap);
            if (raw > 0) value2 = Math.Max(value2, MinPerceptibleIntensity);
            value2 = Math.Clamp(value2 * Math.Clamp(device.IntensityTrim, 0, 1), 0, 1);

            // --- quantize + suppress unchanged ---
            var actuators = device.Actuators;
            if (actuators.Count == 0) return null;

            if (state.Quantized == null || state.Quantized.Length != actuators.Count)
            {
                state.Quantized = new int[actuators.Count];
                for (int i = 0; i < state.Quantized.Length; i++) state.Quantized[i] = -1;
            }

            List<ActuatorOutput>? outputs = null;
            bool allZero = true;
            for (int i = 0; i < actuators.Count; i++)
            {
                var a = actuators[i];
                // Position is absolute placement, not a level — FunScript drives it through
                // SetPositionAsync. Stroke is a range pair, likewise not a generic level.
                if (a.Type == ActuatorType.Position || a.Type == ActuatorType.Stroke) continue;

                var steps = Math.Max(1, a.Steps);
                var q = (int)Math.Round(value2 * steps);
                if (q != 0) allZero = false;
                if (state.Quantized[i] != q) { changed = true; state.Quantized[i] = q; }

                outputs ??= new List<ActuatorOutput>(actuators.Count);
                outputs.Add(new ActuatorOutput(a.Type, a.Index, q / (double)steps));
            }

            if (outputs == null) return null;

            if (!changed)
            {
                // Unchanged: silence stays silent (zero traffic), a held level gets a slow
                // heartbeat so short-timeSec providers can refresh.
                if (allZero) return null;
                if (now - state.LastSendAt < RefreshMs) return null;
                changed = true;
            }

            state.LastSendAt = now;
            return outputs;
        }

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
            }
            if (value > 0) Start();
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
            var max = Math.Max(1, _settings.V2.MaxConcurrentPulses);
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
                    AttackMs = Math.Max(0, p.Pulse.AttackMs),
                    HoldMs = Math.Max(0, p.Pulse.HoldMs),
                    DecayMs = Math.Max(0, p.Pulse.DecayMs),
                    Priority = p.Pulse.Priority,
                    Target = p.Pulse.Target
                });
            }
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
            _ = SafeStopAllAsync();
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
            }
            foreach (var s in _deviceState.Values) s.Floor = 0;
            if (!completeSequences) return;
            foreach (var s in _sequences.Values) s.Handle.Complete();
            _sequences.Clear();
        }

        private async Task SafeStopAllAsync()
        {
            try
            {
                await _devices.StopAllAsync().ConfigureAwait(false);
                lock (_gate)
                {
                    foreach (var s in _deviceState.Values)
                    {
                        s.Floor = 0;
                        s.LastSendAt = 0;
                        if (s.Quantized != null)
                            for (int i = 0; i < s.Quantized.Length; i++) s.Quantized[i] = 0;
                    }
                }
            }
            catch (Exception ex) { App.Logger?.Debug("Haptics StopAll failed (non-fatal): {E}", ex.Message); }
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
            // NEVER block here — Dispose runs on the UI thread during app shutdown. The stop is a
            // background best-effort with a hard cap; ProcessExit is the belt-and-braces path.
            _ = Task.Run(async () =>
            {
                try { await FlushStopAsync(ShutdownFlushCap).ConfigureAwait(false); }
                catch { }
                finally { try { cts?.Dispose(); } catch { } }
            });
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

        private sealed class DeviceState
        {
            public double Floor;
            public int[]? Quantized;
            public long LastSendAt;
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
