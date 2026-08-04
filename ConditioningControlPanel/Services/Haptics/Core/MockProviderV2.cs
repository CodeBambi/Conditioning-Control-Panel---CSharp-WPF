using System.Collections.Generic;
using System.Threading;

namespace ConditioningControlPanel.Services.Haptics.Core
{
    /// <summary>
    /// Virtual toys for development and for users who want to see what the routing matrix does
    /// before buying hardware. Three presets chosen to exercise every awkward shape in the
    /// engine:
    ///   Mock Lush   — one vibe motor (the common case)
    ///   Mock Edge   — TWO vibe motors (index disambiguation; band-split target in Phase F)
    ///   Mock Solace — Thrust (20 steps) + Depth (3 steps) and NO vibrate at all, which is what
    ///                 catches code that assumes "haptics == vibration"
    ///
    /// Visualization reuses the shared singleton toast (see <see cref="MockToast"/> — HWND-leak
    /// history) and is throttled, because the mixer can legitimately call this at 10 Hz per toy.
    /// </summary>
    public sealed class MockProviderV2 : IHapticProviderV2
    {
        private const int ToastThrottleMs = 200;

        private readonly object _gate = new();
        private readonly List<HapticDevice> _devices = new();
        private readonly Dictionary<string, double[]> _levels = new(StringComparer.Ordinal);
        private long _lastToastAt;
        private bool _disposed;

        public string Key => "mock";
        public string DisplayName => "Mock (Testing)";
        public bool IsConnected { get; private set; }

        public IReadOnlyList<HapticDevice> Devices
        {
            get { lock (_gate) return _devices.ToList(); }
        }

        public event EventHandler? DevicesChanged;
#pragma warning disable CS0067 // virtual toys have no buttons and never fail
        public event EventHandler<HapticToyEvent>? ToyEvent;
        public event EventHandler<string>? Error;
#pragma warning restore CS0067

        public Task<bool> ConnectAsync(CancellationToken ct)
        {
            lock (_gate)
            {
                _devices.Clear();
                _levels.Clear();
                _devices.Add(Make("mock-lush", "Mock Lush",
                    new HapticActuator { Type = ActuatorType.Vibrate, Index = 0, Steps = 20 }));
                _devices.Add(Make("mock-edge", "Mock Edge",
                    new HapticActuator { Type = ActuatorType.Vibrate, Index = 0, Steps = 20 },
                    new HapticActuator { Type = ActuatorType.Vibrate, Index = 1, Steps = 20 }));
                _devices.Add(Make("mock-solace", "Mock Solace",
                    new HapticActuator { Type = ActuatorType.Thrust, Index = 0, Steps = 20 },
                    new HapticActuator { Type = ActuatorType.Depth, Index = 0, Steps = 3 }));
                foreach (var d in _devices) _levels[d.Id] = new double[d.Actuators.Count];
                IsConnected = true;
            }
            App.Logger?.Information("MockProviderV2: 3 virtual toys online");
            Raise();
            MockToast.Post("Mock haptics connected\nLush / Edge / Solace");
            return Task.FromResult(true);
        }

        private HapticDevice Make(string id, string name, params HapticActuator[] actuators) => new()
        {
            Id = id,
            ProviderKey = Key,
            Name = name,
            Nickname = "",
            BatteryPercent = 100,
            IsConnected = true,
            Actuators = actuators.ToList()
        };

        public Task DisconnectAsync()
        {
            lock (_gate)
            {
                IsConnected = false;
                _devices.Clear();
                _levels.Clear();
            }
            Raise();
            return Task.CompletedTask;
        }

        public Task SetOutputsAsync(string deviceId, IReadOnlyList<ActuatorOutput> outputs, CancellationToken ct)
        {
            if (!IsConnected || outputs == null || outputs.Count == 0) return Task.CompletedTask;

            bool changed = false;
            lock (_gate)
            {
                var device = _devices.FirstOrDefault(d => d.Id == deviceId);
                if (device == null) return Task.CompletedTask;
                if (!_levels.TryGetValue(deviceId, out var levels) || levels.Length != device.Actuators.Count)
                {
                    levels = new double[device.Actuators.Count];
                    _levels[deviceId] = levels;
                }

                foreach (var o in outputs)
                {
                    var idx = device.Actuators.FindIndex(a => a.Type == o.Type && a.Index == o.Index);
                    if (idx < 0) continue;
                    // Quantize to the actuator's native resolution and suppress unchanged writes,
                    // exactly like a real provider would (this is what the mixer is tested against).
                    var steps = Math.Max(1, device.Actuators[idx].Steps);
                    var q = Math.Round(Math.Clamp(o.Intensity, 0, 1) * steps) / steps;
                    if (Math.Abs(levels[idx] - q) < 1e-6) continue;
                    levels[idx] = q;
                    changed = true;
                }
            }

            if (changed) ShowLevels();
            return Task.CompletedTask;
        }

        public Task StopAllAsync()
        {
            lock (_gate)
            {
                foreach (var levels in _levels.Values)
                    for (int i = 0; i < levels.Length; i++) levels[i] = 0;
                _lastToastAt = 0;
            }
            if (IsConnected) MockToast.Post("Mock haptics: ALL STOP");
            return Task.CompletedTask;
        }

        public Task<bool> PingAsync() => Task.FromResult(IsConnected);

        private void ShowLevels()
        {
            var now = Environment.TickCount64;
            string text;
            lock (_gate)
            {
                if (now - _lastToastAt < ToastThrottleMs) return;
                _lastToastAt = now;

                var lines = new List<string>(_devices.Count);
                foreach (var d in _devices)
                {
                    if (!_levels.TryGetValue(d.Id, out var levels)) continue;
                    var parts = new List<string>(levels.Length);
                    for (int i = 0; i < levels.Length && i < d.Actuators.Count; i++)
                        parts.Add($"{Short(d.Actuators[i].Type)}{(levels[i] > 0 ? (int)Math.Round(levels[i] * 100) + "%" : "--")}");
                    lines.Add($"{d.Name.Replace("Mock ", "")}  {string.Join(" ", parts)}");
                }
                text = string.Join("\n", lines);
            }
            if (!string.IsNullOrEmpty(text)) MockToast.Post(text);
        }

        private static string Short(ActuatorType t) => t switch
        {
            ActuatorType.Vibrate => "V",
            ActuatorType.Rotate => "R",
            ActuatorType.Thrust => "T",
            ActuatorType.Finger => "F",
            ActuatorType.Suction => "S",
            ActuatorType.Oscillate => "O",
            ActuatorType.Pump => "P",
            ActuatorType.Depth => "D",
            ActuatorType.Position => "X",
            ActuatorType.Stroke => "K",
            _ => "C"
        };

        private void Raise()
        {
            try { DevicesChanged?.Invoke(this, EventArgs.Empty); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_gate)
            {
                IsConnected = false;
                _devices.Clear();
                _levels.Clear();
            }
        }
    }
}
