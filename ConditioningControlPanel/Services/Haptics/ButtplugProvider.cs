using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Buttplug.Client;
using Buttplug.Client.Connectors.WebsocketConnector;
using Serilog;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// Buttplug.io provider via Intiface Central
    /// </summary>
    public class ButtplugProvider : IHapticProvider
    {
        private string _serverUrl = "ws://127.0.0.1:12345";
        private ButtplugClient? _client;
        private ButtplugClientDevice? _activeDevice;
        private CancellationTokenSource? _vibrateCts;

        public string Name => "Buttplug.io (Intiface)";
        public bool IsConnected => _client?.Connected == true && _activeDevice != null;
        public List<string> ConnectedDevices { get; } = new();

        public event EventHandler<bool>? ConnectionChanged;
        public event EventHandler<string>? DeviceDiscovered;
        public event EventHandler<string>? Error;

        public void SetUrl(string url)
        {
            _serverUrl = url;
            Log.Debug("ButtplugProvider: URL set to {Url}", url);
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

                // Connect to Intiface via WebSocket
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
                if (_client.Devices.Length > 0)
                {
                    // Use first device that can vibrate
                    foreach (var device in _client.Devices)
                    {
                        if (device.VibrateAttributes.Count > 0)
                        {
                            _activeDevice = device;
                            ConnectedDevices.Clear();
                            ConnectedDevices.Add($"{device.Name} (Vibrate)");
                            Log.Information("ButtplugProvider: Using device {Name}", device.Name);
                            break;
                        }
                    }

                    if (_activeDevice == null)
                    {
                        // No vibrating device, just use first one
                        _activeDevice = _client.Devices[0];
                        ConnectedDevices.Clear();
                        ConnectedDevices.Add(_activeDevice.Name);
                        Log.Warning("ButtplugProvider: No vibrating device found, using {Name}", _activeDevice.Name);
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

            // If we don't have an active device and this one can vibrate, use it
            if (_activeDevice == null && e.Device.VibrateAttributes.Count > 0)
            {
                _activeDevice = e.Device;
                ConnectedDevices.Clear();
                ConnectedDevices.Add($"{e.Device.Name} (Vibrate)");
                ConnectionChanged?.Invoke(this, true);
            }
        }

        private void OnDeviceRemoved(object? sender, DeviceRemovedEventArgs e)
        {
            Log.Information("ButtplugProvider: Device removed: {Name}", e.Device.Name);

            if (_activeDevice?.Index == e.Device.Index)
            {
                _activeDevice = null;
                ConnectedDevices.Clear();
                ConnectionChanged?.Invoke(this, false);
            }
        }

        private void OnServerDisconnect(object? sender, EventArgs e)
        {
            Log.Warning("ButtplugProvider: Server disconnected");
            _activeDevice = null;
            ConnectedDevices.Clear();
            ConnectionChanged?.Invoke(this, false);
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

                _activeDevice = null;
                ConnectedDevices.Clear();
                ConnectionChanged?.Invoke(this, false);
                Log.Information("ButtplugProvider: Disconnected");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ButtplugProvider: Error during disconnect");
            }
        }

        public async Task VibrateAsync(double intensity, int durationMs)
        {
            if (_activeDevice == null || _client?.Connected != true)
                return;

            try
            {
                // Cancel any existing vibration stop timer
                _vibrateCts?.Cancel();
                _vibrateCts = new CancellationTokenSource();
                var token = _vibrateCts.Token;

                // Clamp intensity to 0-1 range
                var clampedIntensity = Math.Clamp(intensity, 0.0, 1.0);

                // Send vibrate command
                await _activeDevice.VibrateAsync(clampedIntensity);

                Log.Debug("ButtplugProvider: Vibrate {Intensity:F2} for {Duration}ms", clampedIntensity, durationMs);

                // Schedule stop after duration (fire-and-forget with cancellation)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(durationMs, token);
                        if (!token.IsCancellationRequested && _activeDevice != null && _client?.Connected == true)
                        {
                            await _activeDevice.Stop();
                            Log.Debug("ButtplugProvider: Auto-stopped after {Duration}ms", durationMs);
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

            if (_activeDevice == null || _client?.Connected != true)
                return;

            try
            {
                await _activeDevice.Stop();
                Log.Debug("ButtplugProvider: Stopped");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ButtplugProvider: Stop failed");
            }
        }
    }
}
