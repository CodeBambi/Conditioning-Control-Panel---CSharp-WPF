using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Buttplug.Client;
using Buttplug.Core.Messages;
using ConditioningControlPanel.Services.Haptics.Core;
using Serilog;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// Buttplug / Intiface Central provider for the v2 haptics engine.
    /// </summary>
    /// <remarks>
    /// <para><b>Message spec: v4.</b> The <c>Buttplug</c> 5.0.1 NuGet package (BSD-3, pure .NET,
    /// one package containing Core + Client + WebSocket connector) speaks Buttplug message spec
    /// v4. There is no <c>ScalarCmd</c>/<c>VibrateCmd</c> and no <c>device.VibrateAsync()</c>;
    /// everything goes through <c>OutputCmd</c>, surfaced in C# as
    /// <c>ButtplugClientDeviceFeature.RunOutputAsync(DeviceOutputCommand)</c>. Capabilities are
    /// per-FEATURE: a device exposes <c>Features</c> (index -&gt; feature), each feature carries a
    /// set of <see cref="OutputType"/>s (Vibrate/Oscillate/Rotate/Position/HwPositionWithDuration/
    /// Led/Temperature/Constrict/Spray) and <see cref="InputType"/>s (Battery/RSSI/Button/Pressure/
    /// Depth/Position). Input is <c>InputCmd</c> (Read/Subscribe/Unsubscribe).</para>
    ///
    /// <para><b>Level-set semantics.</b> Buttplug outputs latch: a value holds until the next
    /// command for that feature. No keep-alive refresh is needed (unlike the Lovense LAN API with
    /// <c>timeSec</c>), so <see cref="SetOutputsAsync"/> only writes when the quantized step value
    /// actually changed.</para>
    ///
    /// <para><b>Not available via Buttplug</b> (dual-path with LovenseProviderV2 exists for this):
    /// Solace depth, thrust/finger/suction/pump as distinct output types, toy button presets, and
    /// Lovense pattern messages. Buttplug's closest equivalents are Position / Oscillate.</para>
    /// </remarks>
    public sealed class ButtplugProviderV2 : IHapticProviderV2
    {
        private const string DefaultServerUrl = "ws://127.0.0.1:12345";

        /// <summary>Timeout for the ping round-trip and for the shutdown-safe stop-all.</summary>
        private static readonly TimeSpan WireTimeout = TimeSpan.FromSeconds(3);

        /// <summary>Fallback duration for HwPositionWithDuration moves, in ms (one 10 Hz tick).</summary>
        private const uint DefaultMoveDurationMs = 100;

        /// <summary>Minimum gap between Error events raised from the 10 Hz output path.</summary>
        private static readonly TimeSpan OutputErrorThrottle = TimeSpan.FromSeconds(5);

        private readonly object _sync = new();

        private DateTime _lastOutputErrorUtc = DateTime.MinValue;
        private ButtplugClient? _client;
        private ButtplugWebsocketConnector? _connector;
        private string? _urlOverride;
        private bool _disposed;

        /// <summary>Per-device dispatch state, keyed by our stable provider-scoped device id.</summary>
        private sealed class Entry
        {
            public HapticDevice Device = null!;
            public ButtplugClientDevice Native = null!;
            /// <summary>(actuator type, per-type ordinal) -&gt; the Buttplug feature that drives it.</summary>
            public readonly Dictionary<(ActuatorType Type, int Index), Target> Targets = new();
            /// <summary>Last quantized step value written per actuator (unchanged-send suppression).</summary>
            public readonly Dictionary<(ActuatorType Type, int Index), int> LastSent = new();
        }

        private readonly struct Target
        {
            public Target(uint featureIndex, OutputType outputType, int min, int max)
            {
                FeatureIndex = featureIndex; OutputType = outputType; Min = min; Max = max;
            }
            public uint FeatureIndex { get; }
            public OutputType OutputType { get; }
            public int Min { get; }
            public int Max { get; }
        }

        private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, string> _idByNativeIndex = new();
        private IReadOnlyList<HapticDevice> _snapshot = Array.Empty<HapticDevice>();

        // ------------------------------------------------------------------ contract

        public string Key => "buttplug";
        public string DisplayName => "Intiface / Buttplug";

        public bool IsConnected => _client?.Connected == true;

        /// <summary>Snapshot list; rebuilt on every change so callers never enumerate live state.</summary>
        public IReadOnlyList<HapticDevice> Devices
        {
            get { lock (_sync) { return _snapshot; } }
        }

        public event EventHandler? DevicesChanged;
        public event EventHandler<HapticToyEvent>? ToyEvent;
        public event EventHandler<string>? Error;

        /// <summary>
        /// Optional explicit server URL. When unset the legacy <c>Haptics.ButtplugUrl</c> setting is
        /// used, falling back to <c>ws://127.0.0.1:12345</c> (Intiface Central's default).
        /// </summary>
        public void SetUrl(string? url) => _urlOverride = string.IsNullOrWhiteSpace(url) ? null : url.Trim();

        private string ResolveUrl()
        {
            if (!string.IsNullOrWhiteSpace(_urlOverride)) return _urlOverride!;
            try
            {
                var fromSettings = App.Settings?.Current?.Haptics?.ButtplugUrl;
                if (!string.IsNullOrWhiteSpace(fromSettings)) return fromSettings!.Trim();
            }
            catch { /* settings not up yet */ }
            return DefaultServerUrl;
        }

        public async Task<bool> ConnectAsync(CancellationToken ct)
        {
            if (_disposed) return false;

            await DisconnectAsync().ConfigureAwait(false);

            var url = ResolveUrl();
            ButtplugClient? client = null;
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    throw new UriFormatException($"'{url}' is not a valid WebSocket URL.");

                Log.Information("ButtplugProviderV2: connecting to {Url}", url);

                client = new ButtplugClient("Conditioning Control Panel");
                client.DeviceAdded += OnDeviceAdded;
                client.DeviceRemoved += OnDeviceRemoved;
                client.ServerDisconnect += OnServerDisconnect;
                client.ErrorReceived += OnErrorReceived;
                client.InputReadingReceived += OnInputReadingReceived;
                client.ScanningFinished += OnScanningFinished;

                var connector = new ButtplugWebsocketConnector(uri);
                await client.ConnectAsync(connector, ct).ConfigureAwait(false);

                _client = client;
                _connector = connector;

                RebuildDevices();

                // Kick off discovery; devices arrive asynchronously via DeviceAdded.
                try { await client.StartScanningAsync(ct).ConfigureAwait(false); }
                catch (Exception ex) { Log.Debug(ex, "ButtplugProviderV2: StartScanning failed (server may already be scanning)"); }

                Log.Information("ButtplugProviderV2: connected, {Count} device(s) known", Devices.Count);
                RaiseDevicesChanged();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ButtplugProviderV2: connect failed ({Url})", url);
                RaiseError($"Intiface connection failed: {ex.Message}");
                if (!ReferenceEquals(client, _client))
                {
                    UnhookAndDispose(client);
                }
                else
                {
                    await DisconnectAsync().ConfigureAwait(false);
                }
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            var client = _client;
            _client = null;
            _connector = null;

            ClearDevices();

            if (client == null) return;

            Unhook(client);
            try
            {
                if (client.Connected) await client.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ButtplugProviderV2: disconnect threw (ignored)");
            }
            try { client.Dispose(); } catch { }

            RaiseDevicesChanged();
        }

        /// <summary>
        /// Level-set write for one device. Quantizes each 0..1 intensity onto the feature's own
        /// native step range and skips any actuator whose quantized value is unchanged (Buttplug
        /// latches values, so silence == "hold what you have").
        /// </summary>
        public async Task SetOutputsAsync(string deviceId, IReadOnlyList<ActuatorOutput> outputs, CancellationToken ct)
        {
            if (_disposed || outputs == null || outputs.Count == 0) return;

            Entry? entry;
            lock (_sync) { _entries.TryGetValue(deviceId ?? "", out entry); }
            if (entry == null || _client?.Connected != true) return;

            foreach (var output in outputs)
            {
                if (ct.IsCancellationRequested) return;

                var key = (output.Type, output.Index);
                Target target;
                int steps;
                lock (_sync)
                {
                    if (!entry.Targets.TryGetValue(key, out target)) continue;
                    steps = Quantize(output.Intensity, target);
                    if (entry.LastSent.TryGetValue(key, out var last) && last == steps) continue;
                }

                try
                {
                    await WriteAsync(entry.Native, target, steps, ct).ConfigureAwait(false);
                    lock (_sync) { entry.LastSent[key] = steps; }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Device loss must never throw out of the mixer loop.
                    lock (_sync) { entry.LastSent.Remove(key); }
                    Log.Debug(ex, "ButtplugProviderV2: output write failed on {Device} {Type}#{Index}",
                        entry.Device.Name, output.Type, output.Index);

                    // The mixer calls this ~10x/sec per device; a dead toy would otherwise
                    // machine-gun the Error event into the UI.
                    var now = DateTime.UtcNow;
                    if (now - _lastOutputErrorUtc >= OutputErrorThrottle)
                    {
                        _lastOutputErrorUtc = now;
                        RaiseError($"{entry.Device.Name}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Immediate all-stop. Bypasses unchanged-send suppression (the cache is cleared so the
        /// next SetOutputsAsync re-sends), and is safe to call during shutdown - it never throws
        /// and it never waits longer than <see cref="WireTimeout"/>.
        /// </summary>
        public async Task StopAllAsync()
        {
            lock (_sync)
            {
                foreach (var e in _entries.Values) e.LastSent.Clear();
            }

            var client = _client;
            if (client?.Connected != true) return;

            try
            {
                using var cts = new CancellationTokenSource(WireTimeout);
                await client.StopAllDevicesAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ButtplugProviderV2: StopAllDevices failed");
                // Best-effort per-device fallback so a single bad device can't leave toys running.
                ButtplugClientDevice[] natives;
                lock (_sync) { natives = _entries.Values.Select(e => e.Native).ToArray(); }
                foreach (var native in natives)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(WireTimeout);
                        await native.StopAsync(cts.Token).ConfigureAwait(false);
                    }
                    catch { /* shutdown-safe */ }
                }
            }
        }

        /// <summary>
        /// Real wire round-trip: sends a <c>RequestDeviceList</c> through our own connector and
        /// waits for the server's reply. (<c>ButtplugClient</c> exposes no public ping, and
        /// <c>Connected</c> can stay true after the route dies; RequestDeviceList is the cheapest
        /// message every Buttplug server answers at any time after the handshake, with no side
        /// effects on the toys.)
        /// </summary>
        public async Task<bool> PingAsync()
        {
            var connector = _connector;
            if (_disposed || connector == null || _client?.Connected != true) return false;

            try
            {
                using var cts = new CancellationTokenSource(WireTimeout);
                var reply = await connector.SendAsync(new RequestDeviceList(), cts.Token).ConfigureAwait(false);
                return reply != null;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ButtplugProviderV2: ping round-trip failed");
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            var client = _client;
            _client = null;
            _connector = null;
            ClearDevices();

            if (client == null) return;
            Unhook(client);

            // ButtplugClient.Dispose() disconnects synchronously; keep it off the caller's thread
            // so app shutdown can never block on a dead socket.
            _ = Task.Run(() =>
            {
                try { client.Dispose(); }
                catch (Exception ex) { Log.Debug(ex, "ButtplugProviderV2: dispose threw (ignored)"); }
            });
        }

        // ------------------------------------------------------------------ output plumbing

        /// <summary>0..1 -&gt; native step value, honouring the feature's advertised [min,max].</summary>
        private static int Quantize(double intensity, in Target target)
        {
            if (double.IsNaN(intensity) || intensity <= 0) return 0;
            var clamped = Math.Clamp(intensity, 0.0, 1.0);
            var steps = (int)Math.Round(clamped * target.Max, MidpointRounding.AwayFromZero);
            if (steps <= 0) steps = 1;                      // never silently swallow a live level
            if (target.Min > 0 && steps < target.Min) steps = target.Min;
            return Math.Clamp(steps, 0, target.Max);
        }

        private static Task WriteAsync(ButtplugClientDevice device, in Target target, int steps, CancellationToken ct)
        {
            DeviceOutputCommand cmd;
            if (target.OutputType == OutputType.HwPositionWithDuration)
            {
                // Hardware-timed move: the mixer only supplies a target level, so we give the toy
                // one output-loop tick (or its own message timing gap, whichever is longer) to get there.
                var duration = Math.Max(device.MessageTimingGap, DefaultMoveDurationMs);
                cmd = new DeviceOutputCommand(target.OutputType, PercentOrSteps.FromSteps(steps), duration);
            }
            else
            {
                cmd = new DeviceOutputCommand(target.OutputType, PercentOrSteps.FromSteps(steps), null);
            }
            return device.RunOutputAsync(target.FeatureIndex, cmd, ct);
        }

        // ------------------------------------------------------------------ device registry

        private void OnDeviceAdded(object? sender, DeviceAddedEventArgs e)
        {
            try
            {
                Log.Information("ButtplugProviderV2: device added: {Name}", e.Device.Name);
                RebuildDevices();
                RaiseDevicesChanged();
                _ = RefreshBatteryAsync(e.Device);
                _ = SubscribeInputsAsync(e.Device);
            }
            catch (Exception ex) { Log.Debug(ex, "ButtplugProviderV2: OnDeviceAdded failed"); }
        }

        private void OnDeviceRemoved(object? sender, DeviceRemovedEventArgs e)
        {
            try
            {
                Log.Information("ButtplugProviderV2: device removed: {Name}", e.Device.Name);
                RebuildDevices();
                RaiseDevicesChanged();
            }
            catch (Exception ex) { Log.Debug(ex, "ButtplugProviderV2: OnDeviceRemoved failed"); }
        }

        private void OnServerDisconnect(object? sender, EventArgs e)
        {
            Log.Warning("ButtplugProviderV2: Intiface server disconnected");
            ClearDevices();
            RaiseError("Intiface server disconnected.");
            RaiseDevicesChanged();
        }

        private void OnScanningFinished(object? sender, EventArgs e)
            => Log.Debug("ButtplugProviderV2: scanning finished");

        private void OnErrorReceived(object? sender, Buttplug.Core.ButtplugExceptionEventArgs e)
        {
            var message = e?.Exception?.Message ?? "Unknown Buttplug error";
            Log.Debug("ButtplugProviderV2: server error: {Message}", message);
            RaiseError(message);
        }

        /// <summary>
        /// Rebuild the device registry from <c>client.Devices</c>. Existing <see cref="HapticDevice"/>
        /// instances are reused so per-toy config the device manager mirrored onto them survives.
        /// </summary>
        private void RebuildDevices()
        {
            var natives = _client?.Devices ?? Array.Empty<ButtplugClientDevice>();

            lock (_sync)
            {
                var kept = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                var byIndex = new Dictionary<uint, string>();
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var native in natives)
                {
                    string id;
                    try { id = MakeStableId(native, used); }
                    catch { continue; }

                    _entries.TryGetValue(id, out var existing);
                    var entry = new Entry
                    {
                        Native = native,
                        Device = existing?.Device ?? new HapticDevice { Id = id, ProviderKey = Key }
                    };

                    entry.Device.Id = id;
                    entry.Device.ProviderKey = Key;
                    entry.Device.Name = native.Name ?? "Unknown device";
                    entry.Device.Nickname = string.IsNullOrWhiteSpace(native.DisplayName) ? "" : native.DisplayName;
                    entry.Device.IsConnected = true;
                    entry.Device.Actuators = BuildActuators(native, entry.Targets);
                    if (existing != null) entry.Device.BatteryPercent = existing.Device.BatteryPercent;

                    kept[id] = entry;
                    byIndex[native.Index] = id;
                }

                _entries.Clear();
                foreach (var kv in kept) _entries[kv.Key] = kv.Value;
                _idByNativeIndex.Clear();
                foreach (var kv in byIndex) _idByNativeIndex[kv.Key] = kv.Value;

                _snapshot = _entries.Values.Select(x => x.Device).ToList();
            }
        }

        private void ClearDevices()
        {
            lock (_sync)
            {
                foreach (var entry in _entries.Values) entry.Device.IsConnected = false;
                _entries.Clear();
                _idByNativeIndex.Clear();
                _snapshot = Array.Empty<HapticDevice>();
            }
        }

        /// <summary>
        /// Provider-scoped id that survives reconnects. Buttplug's numeric device index is
        /// session-scoped, so we key off the name (Intiface display name wins when the user set
        /// one) and disambiguate duplicates with a #n suffix. ':' is stripped because
        /// <c>HapticDevice.DeviceKey</c> is "provider:id".
        /// </summary>
        private static string MakeStableId(ButtplugClientDevice native, HashSet<string> used)
        {
            var baseName = string.IsNullOrWhiteSpace(native.DisplayName) ? native.Name : native.DisplayName;
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "device";
            baseName = baseName.Replace(':', '-').Trim();

            var id = baseName;
            var n = 2;
            while (!used.Add(id))
            {
                id = baseName + "#" + n;
                n++;
            }
            return id;
        }

        /// <summary>
        /// spec v4 feature -&gt; actuator mapping. Each feature can carry several output types; each
        /// (device, actuator type) pair gets a 0-based ordinal so multi-motor toys (Edge = 2 vibes,
        /// Lapis = 3) are individually addressable. Step counts come from the feature's own
        /// advertised range, which is the native resolution the mixer quantizes to.
        /// </summary>
        private static List<HapticActuator> BuildActuators(
            ButtplugClientDevice native,
            Dictionary<(ActuatorType, int), Target> targets)
        {
            targets.Clear();
            var actuators = new List<HapticActuator>();
            var ordinals = new Dictionary<ActuatorType, int>();

            IEnumerable<ButtplugClientDeviceFeature> features;
            try { features = native.Features.OrderBy(kv => kv.Key).Select(kv => kv.Value); }
            catch { return actuators; }

            foreach (var feature in features)
            {
                foreach (var (outputType, actuatorType) in OutputMap)
                {
                    bool has;
                    try { has = feature.HasOutput(outputType); } catch { has = false; }
                    if (!has) continue;

                    int min = 0, max = 0;
                    try
                    {
                        if (!feature.TryGetOutputRange(outputType, out min, out max) || max <= 0)
                        {
                            min = 0;
                            max = DefaultSteps(actuatorType);
                        }
                    }
                    catch { min = 0; max = DefaultSteps(actuatorType); }

                    ordinals.TryGetValue(actuatorType, out var index);
                    ordinals[actuatorType] = index + 1;

                    actuators.Add(new HapticActuator { Type = actuatorType, Index = index, Steps = max });
                    targets[(actuatorType, index)] = new Target(feature.FeatureIndex, outputType, min, max);
                }
            }

            return actuators;
        }

        /// <summary>
        /// Buttplug v4 <see cref="OutputType"/> -&gt; contract <see cref="ActuatorType"/>.
        /// Led / Temperature / Spray have no haptic-contract equivalent and are ignored.
        /// Buttplug has no Thrust / Finger / Suction / Pump / Depth / Stroke output type -
        /// those are Lovense-only and live on LovenseProviderV2.
        /// </summary>
        private static readonly (OutputType Output, ActuatorType Actuator)[] OutputMap =
        {
            (OutputType.Vibrate, ActuatorType.Vibrate),
            (OutputType.Rotate, ActuatorType.Rotate),
            (OutputType.Oscillate, ActuatorType.Oscillate),
            (OutputType.Position, ActuatorType.Position),
            (OutputType.HwPositionWithDuration, ActuatorType.Position),
            (OutputType.Constrict, ActuatorType.Constrict),
        };

        private static int DefaultSteps(ActuatorType type) => type switch
        {
            ActuatorType.Position => 100,
            _ => 20
        };

        // ------------------------------------------------------------------ inputs (best-effort)

        /// <summary>Best-effort battery read; null stays null when the toy has no battery sensor.</summary>
        private async Task RefreshBatteryAsync(ButtplugClientDevice native)
        {
            try
            {
                if (!native.HasInput(InputType.Battery)) return;

                var level = await native.BatteryAsync(WireTimeout).ConfigureAwait(false);
                var percent = (int)Math.Round(Math.Clamp(level, 0.0, 1.0) * 100);

                string? deviceId;
                lock (_sync)
                {
                    if (!_idByNativeIndex.TryGetValue(native.Index, out deviceId) ||
                        !_entries.TryGetValue(deviceId, out var entry)) return;
                    entry.Device.BatteryPercent = percent;
                }

                RaiseDevicesChanged();
                RaiseToyEvent(deviceId!, ToyEventKind.BatteryChanged, percent);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ButtplugProviderV2: battery read failed for {Name}", native.Name);
            }
        }

        /// <summary>
        /// Best-effort subscription to button input so toy buttons can become app input
        /// (Phase F). Only features that advertise Subscribe are touched; every failure is
        /// swallowed - plenty of toys report a button feature they cannot stream.
        /// </summary>
        private async Task SubscribeInputsAsync(ButtplugClientDevice native)
        {
            try
            {
                foreach (var feature in native.GetFeaturesWithInput(InputType.Button))
                {
                    try
                    {
                        var def = feature.FeatureDefinition?.GetInput(InputType.Button);
                        if (def?.Command == null || !def.Command.Contains(InputCommandType.Subscribe)) continue;
                        await feature.RunInputAsync(DeviceInput.Button.Subscribe()).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "ButtplugProviderV2: button subscribe failed on {Name}", native.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ButtplugProviderV2: input subscribe pass failed for {Name}", native.Name);
            }
        }

        private void OnInputReadingReceived(object? sender, InputReadingEventArgs e)
        {
            try
            {
                string? deviceId;
                lock (_sync)
                {
                    if (!_idByNativeIndex.TryGetValue(e.DeviceIndex, out deviceId)) return;
                }

                var reading = e.Reading;
                if (reading == null) return;

                var button = reading.GetValue(InputType.Button);
                if (button.HasValue)
                {
                    RaiseToyEvent(deviceId!, button.Value > 0 ? ToyEventKind.ButtonDown : ToyEventKind.ButtonUp, button.Value);
                    return;
                }

                if (reading.BatteryLevel.HasValue)
                {
                    var percent = (int)Math.Round(Math.Clamp(reading.BatteryLevel.Value, 0.0, 1.0) * 100);
                    lock (_sync)
                    {
                        if (_entries.TryGetValue(deviceId!, out var entry)) entry.Device.BatteryPercent = percent;
                    }
                    RaiseDevicesChanged();
                    RaiseToyEvent(deviceId!, ToyEventKind.BatteryChanged, percent);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ButtplugProviderV2: input reading failed");
            }
        }

        // ------------------------------------------------------------------ plumbing

        private void Unhook(ButtplugClient client)
        {
            try
            {
                client.DeviceAdded -= OnDeviceAdded;
                client.DeviceRemoved -= OnDeviceRemoved;
                client.ServerDisconnect -= OnServerDisconnect;
                client.ErrorReceived -= OnErrorReceived;
                client.InputReadingReceived -= OnInputReadingReceived;
                client.ScanningFinished -= OnScanningFinished;
            }
            catch { }
        }

        private void UnhookAndDispose(ButtplugClient? client)
        {
            if (client == null) return;
            Unhook(client);
            try { client.Dispose(); } catch { }
        }

        private void RaiseDevicesChanged()
        {
            try { DevicesChanged?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { Log.Debug(ex, "ButtplugProviderV2: DevicesChanged handler threw"); }
        }

        private void RaiseError(string message)
        {
            try { Error?.Invoke(this, message); }
            catch (Exception ex) { Log.Debug(ex, "ButtplugProviderV2: Error handler threw"); }
        }

        private void RaiseToyEvent(string deviceId, ToyEventKind kind, double value)
        {
            try
            {
                ToyEvent?.Invoke(this, new HapticToyEvent
                {
                    DeviceKey = Key + ":" + deviceId,
                    Kind = kind,
                    Value = value
                });
            }
            catch (Exception ex) { Log.Debug(ex, "ButtplugProviderV2: ToyEvent handler threw"); }
        }
    }
}
