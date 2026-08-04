using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Haptics.Core
{
    /// <summary>
    /// Owns every v2 provider and the merged device registry.
    ///
    /// Providers are connected CONCURRENTLY (the old service had exactly one "active provider",
    /// so a Lovense user could not also drive an Intiface toy). Devices from all providers are
    /// merged into one list, de-duplicated when the same physical toy shows up through two
    /// providers, and decorated with the user's persisted per-device config (role / trim /
    /// enabled / nickname), keyed by the stable <see cref="HapticDevice.DeviceKey"/>.
    /// </summary>
    public sealed class HapticDeviceManager : IDisposable
    {
        /// <summary>When the same toy is visible through two providers we keep the one with the
        /// richer API. Lovense LAN gives us depth/position/presets/button events; Buttplug does
        /// not; the mock is only ever a stand-in.</summary>
        private static readonly string[] ProviderPreference = { "lovense", "buttplug", "mock" };

        private readonly object _gate = new();
        private readonly List<IHapticProviderV2> _providers = new();
        private readonly Dictionary<string, IHapticProviderV2> _ownerByKey = new(StringComparer.Ordinal);
        private readonly HapticSettings _settings;
        private List<HapticDevice> _devices = new();
        private bool _disposed;
        private bool _lastConnected;

        public HapticDeviceManager(HapticSettings settings)
        {
            _settings = settings;
            _settings.EnsureV2Migrated();

            Register(new MockProviderV2());
            Register(new LovenseProviderV2());
            Register(new ButtplugProviderV2());
        }

        /// <summary>Snapshot — safe to enumerate without locking.</summary>
        public IReadOnlyList<HapticDevice> Devices
        {
            get { lock (_gate) return _devices; }
        }

        public IReadOnlyList<IHapticProviderV2> Providers
        {
            get { lock (_gate) return _providers.ToList(); }
        }

        public bool IsConnected
        {
            get { lock (_gate) return _providers.Any(p => p.IsConnected); }
        }

        public string ProviderNames
        {
            get
            {
                lock (_gate)
                {
                    var live = _providers.Where(p => p.IsConnected).Select(p => p.DisplayName).ToList();
                    return live.Count == 0 ? "None" : string.Join(" + ", live);
                }
            }
        }

        public event EventHandler? DevicesChanged;
        public event EventHandler<HapticToyEvent>? ToyEvent;
        public event EventHandler<string>? Error;
        public event EventHandler<bool>? ConnectionChanged;
        /// <summary>Raised once per newly-seen device (display name) — feeds the legacy
        /// HapticService.DeviceDiscovered event.</summary>
        public event EventHandler<string>? DeviceDiscovered;

        public void Register(IHapticProviderV2 provider)
        {
            lock (_gate)
            {
                if (_providers.Any(p => string.Equals(p.Key, provider.Key, StringComparison.OrdinalIgnoreCase))) return;
                _providers.Add(provider);
            }
            provider.DevicesChanged += (s, e) => Rebuild();
            provider.ToyEvent += (s, e) => { try { ToyEvent?.Invoke(this, e); } catch { } };
            provider.Error += (s, e) => { try { Error?.Invoke(this, e); } catch { } };
        }

        /// <summary>Which providers the user wants connected. Seeded from the legacy single-choice
        /// <c>Provider</c> enum on first load, then editable per-provider in the v2 config.</summary>
        private List<IHapticProviderV2> EnabledProviders()
        {
            var v2 = _settings.V2;
            lock (_gate)
            {
                return _providers.Where(p => v2.Provider(p.Key).Enabled).ToList();
            }
        }

        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            var targets = EnabledProviders();
            if (targets.Count == 0)
            {
                App.Logger?.Warning("Haptics: no provider enabled — nothing to connect");
                return false;
            }

            var results = await Task.WhenAll(targets.Select(async p =>
            {
                try
                {
                    var ok = await p.ConnectAsync(ct).ConfigureAwait(false);
                    if (!ok) App.Logger?.Warning("Haptics: provider {Provider} failed to connect", p.Key);
                    return ok;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Haptics: provider {Provider} threw while connecting", p.Key);
                    return false;
                }
            })).ConfigureAwait(false);

            Rebuild();
            return results.Any(r => r);
        }

        public async Task DisconnectAsync()
        {
            List<IHapticProviderV2> providers;
            lock (_gate) providers = _providers.ToList();
            foreach (var p in providers)
            {
                try { await p.DisconnectAsync().ConfigureAwait(false); }
                catch (Exception ex) { App.Logger?.Debug("Haptics: {Provider} disconnect error: {E}", p.Key, ex.Message); }
            }
            Rebuild();
        }

        public async Task StopAllAsync()
        {
            List<IHapticProviderV2> providers;
            lock (_gate) providers = _providers.Where(p => p.IsConnected).ToList();
            foreach (var p in providers)
            {
                try { await p.StopAllAsync().ConfigureAwait(false); }
                catch (Exception ex) { App.Logger?.Debug("Haptics: {Provider} StopAll error: {E}", p.Key, ex.Message); }
            }
        }

        /// <summary>Liveness check that actually touches the wire. True when ANY connected
        /// provider answers — one dead provider must not tear down the other.</summary>
        public async Task<bool> PingAsync()
        {
            List<IHapticProviderV2> providers;
            lock (_gate) providers = _providers.Where(p => p.IsConnected).ToList();
            if (providers.Count == 0) return false;

            foreach (var p in providers)
            {
                try { if (await p.PingAsync().ConfigureAwait(false)) return true; }
                catch { }
            }
            return false;
        }

        public Task SendAsync(HapticDevice device, IReadOnlyList<ActuatorOutput> outputs, CancellationToken ct)
        {
            IHapticProviderV2? owner;
            lock (_gate) _ownerByKey.TryGetValue(device.DeviceKey, out owner);
            if (owner == null || !owner.IsConnected) return Task.CompletedTask;
            return owner.SetOutputsAsync(device.Id, outputs, ct);
        }

        public HapticDevice? Find(string deviceKey)
        {
            lock (_gate) return _devices.FirstOrDefault(d => d.DeviceKey == deviceKey);
        }

        // ---------------------------------------------------------------- registry

        /// <summary>Re-merge every provider's device list, apply persisted config, dedupe.</summary>
        public void Rebuild()
        {
            if (_disposed) return;
            List<HapticDevice> merged = new();
            List<string> newlySeen = new();
            var v2 = _settings.V2;
            bool connected;

            lock (_gate)
            {
                var ordered = _providers
                    .OrderBy(p => Array.IndexOf(ProviderPreference, p.Key.ToLowerInvariant()) is var i && i >= 0 ? i : int.MaxValue)
                    .ToList();

                var seenNames = new HashSet<string>(StringComparer.Ordinal);
                var previousKeys = new HashSet<string>(_devices.Select(d => d.DeviceKey), StringComparer.Ordinal);
                _ownerByKey.Clear();

                foreach (var provider in ordered)
                {
                    if (!provider.IsConnected) continue;
                    IReadOnlyList<HapticDevice> devices;
                    try { devices = provider.Devices; }
                    catch { continue; }

                    foreach (var d in devices)
                    {
                        if (d == null) continue;
                        // Dedupe: the same physical toy through two providers. Name match is all we
                        // have (Buttplug ids are session-scoped), so it is deliberately fuzzy.
                        var norm = NormalizeName(d.Name);
                        if (norm.Length > 0 && !seenNames.Add(norm)) continue;

                        var copy = Clone(d);
                        var cfg = v2.Device(copy.DeviceKey);
                        cfg.LastKnownName = string.IsNullOrWhiteSpace(copy.Name) ? cfg.LastKnownName : copy.Name;
                        copy.Role = cfg.Role;
                        copy.IntensityTrim = Math.Clamp(cfg.IntensityTrim, 0, 1);
                        copy.Enabled = cfg.Enabled;
                        if (!string.IsNullOrWhiteSpace(cfg.Nickname)) copy.Nickname = cfg.Nickname;

                        merged.Add(copy);
                        _ownerByKey[copy.DeviceKey] = provider;
                        if (!previousKeys.Contains(copy.DeviceKey))
                            newlySeen.Add(string.IsNullOrWhiteSpace(copy.Nickname) ? copy.Name : copy.Nickname);
                    }
                }

                _devices = merged;
                connected = _providers.Any(p => p.IsConnected);
            }

            foreach (var name in newlySeen)
            {
                try { DeviceDiscovered?.Invoke(this, name); } catch { }
            }
            try { DevicesChanged?.Invoke(this, EventArgs.Empty); } catch { }

            if (connected != _lastConnected)
            {
                _lastConnected = connected;
                try { ConnectionChanged?.Invoke(this, connected); } catch { }
            }
        }

        private static HapticDevice Clone(HapticDevice d) => new()
        {
            Id = d.Id,
            ProviderKey = d.ProviderKey,
            Name = d.Name,
            Nickname = d.Nickname,
            BatteryPercent = d.BatteryPercent,
            IsConnected = d.IsConnected,
            Actuators = d.Actuators.Select(a => new HapticActuator { Type = a.Type, Index = a.Index, Steps = a.Steps }).ToList()
        };

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        // ---------------------------------------------------------------- per-device config

        public void SetRole(string deviceKey, ToyRole role) => UpdateConfig(deviceKey, c => c.Role = role);
        public void SetTrim(string deviceKey, double trim) => UpdateConfig(deviceKey, c => c.IntensityTrim = Math.Clamp(trim, 0, 1));
        public void SetEnabled(string deviceKey, bool enabled) => UpdateConfig(deviceKey, c => c.Enabled = enabled);
        public void SetNickname(string deviceKey, string nickname) => UpdateConfig(deviceKey, c => c.Nickname = nickname ?? "");

        private void UpdateConfig(string deviceKey, Action<HapticDeviceConfig> mutate)
        {
            if (string.IsNullOrEmpty(deviceKey)) return;
            var cfg = _settings.V2.Device(deviceKey);
            mutate(cfg);
            try { App.Settings?.Save(); } catch { }
            Rebuild();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            List<IHapticProviderV2> providers;
            lock (_gate)
            {
                providers = _providers.ToList();
                _providers.Clear();
                _ownerByKey.Clear();
                _devices = new List<HapticDevice>();
            }
            foreach (var p in providers)
            {
                try { p.Dispose(); } catch { }
            }
        }
    }
}
