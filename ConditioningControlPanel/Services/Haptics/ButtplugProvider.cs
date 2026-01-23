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
    /// Supports multiple devices - all connected vibrating devices receive commands
    /// </summary>
    public class ButtplugProvider : IHapticProvider
    {
        private string _serverUrl = "ws://127.0.0.1:12345";
        private ButtplugClient? _client;
        private readonly List<ButtplugClientDevice> _activeDevices = new();
        private CancellationTokenSource? _vibrateCts;

        public string Name => "Buttplug.io (Intiface)";
        public bool IsConnected => _client?.Connected == true && _activeDevices.Count > 0;
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
                    // Add ALL devices that can vibrate
                    _activeDevices.Clear();
                    ConnectedDevices.Clear();

                    foreach (var device in _client.Devices)
                    {
                        if (device.VibrateAttributes.Count > 0)
                        {
                            _activeDevices.Add(device);
                            ConnectedDevices.Add($"{device.Name} (Vibrate)");
                            Log.Information("ButtplugProvider: Added device {Name}", device.Name);
                        }
                    }

                    if (_activeDevices.Count == 0)
                    {
                        // No vibrating devices found, use first one anyway
                        var firstDevice = _client.Devices[0];
                        _activeDevices.Add(firstDevice);
                        ConnectedDevices.Add(firstDevice.Name);
                        Log.Warning("ButtplugProvider: No vibrating device found, using {Name}", firstDevice.Name);
                    }

                    Log.Information("ButtplugProvider: {Count} device(s) ready", _activeDevices.Count);
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

            // Add this device if it can vibrate and isn't already in our list
            if (e.Device.VibrateAttributes.Count > 0 && !_activeDevices.Any(d => d.Index == e.Device.Index))
            {
                _activeDevices.Add(e.Device);
                ConnectedDevices.Add($"{e.Device.Name} (Vibrate)");
                Log.Information("ButtplugProvider: Now have {Count} active device(s)", _activeDevices.Count);
                ConnectionChanged?.Invoke(this, true);
            }
        }

        private void OnDeviceRemoved(object? sender, DeviceRemovedEventArgs e)
        {
            Log.Information("ButtplugProvider: Device removed: {Name}", e.Device.Name);

            var deviceToRemove = _activeDevices.FirstOrDefault(d => d.Index == e.Device.Index);
            if (deviceToRemove != null)
            {
                _activeDevices.Remove(deviceToRemove);
                ConnectedDevices.Remove($"{e.Device.Name} (Vibrate)");
                Log.Information("ButtplugProvider: Now have {Count} active device(s)", _activeDevices.Count);
                ConnectionChanged?.Invoke(this, _activeDevices.Count > 0);
            }
        }

        private void OnServerDisconnect(object? sender, EventArgs e)
        {
            Log.Warning("ButtplugProvider: Server disconnected");
            _activeDevices.Clear();
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

                _activeDevices.Clear();
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
            if (_activeDevices.Count == 0 || _client?.Connected != true)
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
                var tasks = _activeDevices.Select(device =>
                {
                    try
                    {
                        return device.VibrateAsync(clampedIntensity);
                    }
                    catch
                    {
                        return Task.CompletedTask;
                    }
                });
                await Task.WhenAll(tasks);

                Log.Debug("ButtplugProvider: Vibrate {Intensity:F2} for {Duration}ms on {Count} device(s)",
                    clampedIntensity, durationMs, _activeDevices.Count);

                // Schedule stop after duration (fire-and-forget with cancellation)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(durationMs, token);
                        if (!token.IsCancellationRequested && _activeDevices.Count > 0 && _client?.Connected == true)
                        {
                            // Stop ALL devices
                            var stopTasks = _activeDevices.Select(device =>
                            {
                                try { return device.Stop(); }
                                catch { return Task.CompletedTask; }
                            });
                            await Task.WhenAll(stopTasks);
                            Log.Debug("ButtplugProvider: Auto-stopped {Count} device(s) after {Duration}ms",
                                _activeDevices.Count, durationMs);
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

            if (_activeDevices.Count == 0 || _client?.Connected != true)
                return;

            try
            {
                // Stop ALL devices
                var stopTasks = _activeDevices.Select(device =>
                {
                    try { return device.Stop(); }
                    catch { return Task.CompletedTask; }
                });
                await Task.WhenAll(stopTasks);
                Log.Debug("ButtplugProvider: Stopped {Count} device(s)", _activeDevices.Count);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ButtplugProvider: Stop failed");
            }
        }
    }
}
