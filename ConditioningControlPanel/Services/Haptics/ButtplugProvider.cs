using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Buttplug.Client;
using Buttplug.Core.Messages;
using Serilog;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// Buttplug.io provider via Intiface Central
    /// Supports multiple devices - all connected vibrating devices receive commands
    /// </summary>
    /// <remarks>
    /// LEGACY provider (IHapticProvider). Ported to the Buttplug 5.0.1 NuGet package, which
    /// speaks message spec **v4**: there is no VibrateCmd/ScalarCmd and no
    /// <c>device.VibrateAsync()</c> convenience any more. Output goes per *feature* via
    /// <c>ButtplugClientDeviceFeature.RunOutputAsync(DeviceOutput.Vibrate.Percent(x))</c>
    /// (see <see cref="ButtplugProviderV2"/> for the full v4 mapping). Behaviour is unchanged:
    /// one intensity broadcast to every vibrating device, with a fire-and-forget auto-stop timer.
    /// This file is superseded by <see cref="ButtplugProviderV2"/> and gets deleted at integration.
    /// </remarks>
    public class ButtplugProvider : IHapticProvider
    {
        private string _serverUrl = "ws://127.0.0.1:12345";
        private ButtplugClient? _client;
        private readonly object _devicesLock = new();
        private readonly List<ButtplugClientDevice> _activeDevices = new();
        private CancellationTokenSource? _vibrateCts;

        public string Name => "Buttplug.io (Intiface)";

        public bool IsConnected
        {
            get
            {
                lock (_devicesLock) { return _client?.Connected == true && _activeDevices.Count > 0; }
            }
        }

        public List<string> ConnectedDevices { get; } = new();

        public event EventHandler<bool>? ConnectionChanged;
        public event EventHandler<string>? DeviceDiscovered;
        public event EventHandler<string>? Error;

        public void SetUrl(string url)
        {
            _serverUrl = url;
            Log.Debug("ButtplugProvider: URL set to {Url}", url);
        }

        /// <summary>Snapshot of the devices we currently drive (never enumerate the field directly).</summary>
        private ButtplugClientDevice[] DeviceSnapshot()
        {
            lock (_devicesLock) { return _activeDevices.ToArray(); }
        }

        /// <summary>spec v4: a device "can vibrate" when any of its features exposes a Vibrate output.</summary>
        private static bool CanVibrate(ButtplugClientDevice device)
        {
            try { return device.HasOutput(OutputType.Vibrate); }
            catch { return false; }
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                Log.Information("ButtplugProvider: Connecting to {Url}", _serverUrl);

                // Create client
                _client = new ButtplugClient("Conditioning Control Panel");

                // Subscribe to events
                _client.DeviceAdded += OnDeviceAdded;
                _client.DeviceRemoved += OnDeviceRemoved;
                _client.ServerDisconnect += OnServerDisconnect;

                // Connect to Intiface via WebSocket (5.x ships the connector in the core package)
                var connector = new ButtplugWebsocketConnector(new Uri(_serverUrl));
                await _client.ConnectAsync(connector);

                Log.Information("ButtplugProvider: Connected to Intiface server");

                // Start scanning for devices
                await _client.StartScanningAsync();

                // Wait a moment for devices to be discovered
                await Task.Delay(2000);

                // Stop scanning
                try { await _client.StopScanningAsync(); } catch { }

                // Check if we have any devices
                var devices = _client.Devices;
                if (devices.Length > 0)
                {
                    // Add ALL devices that can vibrate
                    lock (_devicesLock)
                    {
                        _activeDevices.Clear();
                        ConnectedDevices.Clear();

                        foreach (var device in devices)
                        {
                            if (CanVibrate(device))
                            {
                                _activeDevices.Add(device);
                                ConnectedDevices.Add($"{device.Name} (Vibrate)");
                                Log.Information("ButtplugProvider: Added device {Name}", device.Name);
                            }
                        }

                        if (_activeDevices.Count == 0)
                        {
                            // No vibrating devices found, use first one anyway
                            var firstDevice = devices[0];
                            _activeDevices.Add(firstDevice);
                            ConnectedDevices.Add(firstDevice.Name);
                            Log.Warning("ButtplugProvider: No vibrating device found, using {Name}", firstDevice.Name);
                        }

                        Log.Information("ButtplugProvider: {Count} device(s) ready", _activeDevices.Count);
                    }

                    ConnectionChanged?.Invoke(this, true);
                    return true;
                }
                else
                {
                    Log.Warning("ButtplugProvider: No devices found. Make sure your device is connected in Intiface.");
                    Error?.Invoke(this, "No devices found. Connect your device in Intiface first.");
                    await DisconnectAsync();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ButtplugProvider: Failed to connect");
                Error?.Invoke(this, $"Connection failed: {ex.Message}");
                return false;
            }
        }

        private void OnDeviceAdded(object? sender, DeviceAddedEventArgs e)
        {
            Log.Information("ButtplugProvider: Device added: {Name}", e.Device.Name);
            DeviceDiscovered?.Invoke(this, e.Device.Name);

            var added = false;
            var count = 0;

            // Add this device if it can vibrate and isn't already in our list
            if (CanVibrate(e.Device))
            {
                lock (_devicesLock)
                {
                    if (!_activeDevices.Any(d => d.Index == e.Device.Index))
                    {
                        _activeDevices.Add(e.Device);
                        ConnectedDevices.Add($"{e.Device.Name} (Vibrate)");
                        added = true;
                    }
                    count = _activeDevices.Count;
                }
            }

            if (added)
            {
                Log.Information("ButtplugProvider: Now have {Count} active device(s)", count);
                ConnectionChanged?.Invoke(this, true);
            }
        }

        private void OnDeviceRemoved(object? sender, DeviceRemovedEventArgs e)
        {
            Log.Information("ButtplugProvider: Device removed: {Name}", e.Device.Name);

            var removed = false;
            var count = 0;

            lock (_devicesLock)
            {
                var deviceToRemove = _activeDevices.FirstOrDefault(d => d.Index == e.Device.Index);
                if (deviceToRemove != null)
                {
                    _activeDevices.Remove(deviceToRemove);
                    ConnectedDevices.Remove($"{e.Device.Name} (Vibrate)");
                    removed = true;
                }
                count = _activeDevices.Count;
            }

            if (removed)
            {
                Log.Information("ButtplugProvider: Now have {Count} active device(s)", count);
                ConnectionChanged?.Invoke(this, count > 0);
            }
        }

        private void OnServerDisconnect(object? sender, EventArgs e)
        {
            Log.Warning("ButtplugProvider: Server disconnected");
            lock (_devicesLock)
            {
                _activeDevices.Clear();
                ConnectedDevices.Clear();
            }
            ConnectionChanged?.Invoke(this, false);
        }

        public Task<bool> PingAsync()
        {
            // Buttplug fires ServerDisconnect when the WS drops, so the cached state is reliable.
            // VPN tunnels rarely break localhost routing anyway.
            // (ButtplugProviderV2.PingAsync does a real wire round-trip; this legacy shim does not.)
            return Task.FromResult(IsConnected);
        }

        public async Task DisconnectAsync()
        {
            try
            {
                // Cancel any pending vibration timer
                _vibrateCts?.Cancel();
                _vibrateCts = null;

                if (_client != null)
                {
                    _client.DeviceAdded -= OnDeviceAdded;
                    _client.DeviceRemoved -= OnDeviceRemoved;
                    _client.ServerDisconnect -= OnServerDisconnect;

                    if (_client.Connected)
                    {
                        await _client.DisconnectAsync();
                    }
                    _client.Dispose();
                    _client = null;
                }

                lock (_devicesLock)
                {
                    _activeDevices.Clear();
                    ConnectedDevices.Clear();
                }
                ConnectionChanged?.Invoke(this, false);
                Log.Information("ButtplugProvider: Disconnected");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ButtplugProvider: Error during disconnect");
            }
        }

        /// <summary>
        /// spec v4 vibrate: no device-wide VibrateCmd exists, so fan the level out over every
        /// feature that advertises a Vibrate output. Percent() lets the client map 0..1 onto the
        /// feature's own step range.
        /// </summary>
        private static async Task VibrateDeviceAsync(ButtplugClientDevice device, double intensity)
        {
            try
            {
                var cmd = DeviceOutput.Vibrate.Percent(intensity);
                foreach (var feature in device.GetFeaturesWithOutput(OutputType.Vibrate))
                {
                    await feature.RunOutputAsync(cmd).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ButtplugProvider: Vibrate failed on {Name}", device.Name);
            }
        }

        private static async Task StopDeviceAsync(ButtplugClientDevice device)
        {
            try { await device.StopAsync().ConfigureAwait(false); }
            catch (Exception ex) { Log.Debug(ex, "ButtplugProvider: Stop failed on {Name}", device.Name); }
        }

        public async Task VibrateAsync(double intensity, int durationMs)
        {
            var devices = DeviceSnapshot();
            if (devices.Length == 0 || _client?.Connected != true)
                return;

            try
            {
                // Cancel any existing vibration stop timer
                _vibrateCts?.Cancel();
                _vibrateCts = new CancellationTokenSource();
                var token = _vibrateCts.Token;

                // Clamp intensity to 0-1 range
                var clampedIntensity = Math.Clamp(intensity, 0.0, 1.0);

                // Send vibrate command to ALL connected devices
                await Task.WhenAll(devices.Select(d => VibrateDeviceAsync(d, clampedIntensity)));

                Log.Debug("ButtplugProvider: Vibrate {Intensity:F2} for {Duration}ms on {Count} device(s)",
                    clampedIntensity, durationMs, devices.Length);

                // Schedule stop after duration (fire-and-forget with cancellation)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(durationMs, token);
                        var current = DeviceSnapshot();
                        if (!token.IsCancellationRequested && current.Length > 0 && _client?.Connected == true)
                        {
                            // Stop ALL devices
                            await Task.WhenAll(current.Select(StopDeviceAsync));
                            Log.Debug("ButtplugProvider: Auto-stopped {Count} device(s) after {Duration}ms",
                                current.Length, durationMs);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when a new vibration starts before this one ends
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "ButtplugProvider: Auto-stop failed");
                    }
                }, token);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ButtplugProvider: Vibrate failed");
            }
        }

        public async Task StopAsync()
        {
            // Cancel any pending auto-stop timer
            _vibrateCts?.Cancel();
            _vibrateCts = null;

            var devices = DeviceSnapshot();
            if (devices.Length == 0 || _client?.Connected != true)
                return;

            try
            {
                // Stop ALL devices
                await Task.WhenAll(devices.Select(StopDeviceAsync));
                Log.Debug("ButtplugProvider: Stopped {Count} device(s)", devices.Length);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ButtplugProvider: Stop failed");
            }
        }
    }
}
