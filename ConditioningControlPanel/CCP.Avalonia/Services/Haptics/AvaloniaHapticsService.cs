using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Buttplug.Client;
using Buttplug.Client.Connectors.WebsocketConnector;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Services.Haptics;

/// <summary>
/// Avalonia head implementation of <see cref="IHapticsService"/> using Buttplug.io (Intiface).
/// Supports connecting over WebSocket, enumerating vibrate-capable devices, and sending
/// vibration commands. Falls back gracefully when no server/device is available.
/// </summary>
public sealed class AvaloniaHapticsService : IHapticsService, IAsyncDisposable
{
    private readonly ILogger<AvaloniaHapticsService>? _logger;
    private readonly object _sync = new();
    private readonly List<ButtplugClientDevice> _activeDevices = new();

    private ButtplugClient? _client;
    private CancellationTokenSource? _vibrateCts;
    private bool _isConnected;
    private bool _isConnecting;

    public AvaloniaHapticsService(ILogger<AvaloniaHapticsService>? logger = null)
    {
        _logger = logger;
    }

    public bool IsConnected
    {
        get { lock (_sync) return _isConnected; }
        private set { lock (_sync) _isConnected = value; }
    }

    public bool IsConnecting
    {
        get { lock (_sync) return _isConnecting; }
        private set { lock (_sync) _isConnecting = value; }
    }

    public IReadOnlyList<string> ConnectedDevices
    {
        get
        {
            lock (_sync)
            {
                return _activeDevices.Select(d => $"{d.Name} (Vibrate)").ToArray();
            }
        }
    }

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<string>? DeviceAdded;
    public event EventHandler<string>? DeviceRemoved;

    public async Task<bool> ConnectAsync(string providerUrl)
    {
        if (IsConnected)
        {
            _logger?.LogInformation("Haptics connect requested but already connected");
            return true;
        }

        IsConnecting = true;
        try
        {
            await DisconnectAsync();

            var url = string.IsNullOrWhiteSpace(providerUrl) ? "ws://127.0.0.1:12345" : providerUrl;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                uri = new Uri("ws://127.0.0.1:12345");

            _logger?.LogInformation("Haptics connecting to {Url}", uri);

            _client = new ButtplugClient("Conditioning Control Panel");
            _client.DeviceAdded += OnDeviceAdded;
            _client.DeviceRemoved += OnDeviceRemoved;
            _client.ServerDisconnect += OnServerDisconnect;

            var connector = new ButtplugWebsocketConnector(uri);
            await _client.ConnectAsync(connector);

            await _client.StartScanningAsync();
            await Task.Delay(2000);
            try { await _client.StopScanningAsync(); } catch { /* scanning may already be stopped */ }

            lock (_sync)
            {
                _activeDevices.Clear();
                foreach (var device in _client.Devices)
                {
                    if (device.VibrateAttributes.Count > 0)
                        _activeDevices.Add(device);
                }

                if (_activeDevices.Count == 0 && _client.Devices.Length > 0)
                    _activeDevices.Add(_client.Devices[0]);

                _isConnected = _client.Connected && _activeDevices.Count > 0;
            }

            IsConnecting = false;

            if (_isConnected)
            {
                foreach (var device in _activeDevices)
                    DeviceAdded?.Invoke(this, device.Name);
                ConnectionStateChanged?.Invoke(this, true);
                _logger?.LogInformation("Haptics connected; {Count} device(s) ready", _activeDevices.Count);
            }
            else
            {
                _logger?.LogWarning("Haptics connected to server but no devices found");
                ConnectionStateChanged?.Invoke(this, false);
            }

            return _isConnected;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Haptics connect failed");
            IsConnecting = false;
            IsConnected = false;
            ConnectionStateChanged?.Invoke(this, false);
            return false;
        }
    }

    public void Disconnect()
    {
        _ = DisconnectAsync();
    }

    public async Task<bool> TestAsync(int intensityPercent, int durationMs)
    {
        if (!IsConnected)
        {
            _logger?.LogInformation("Haptics test requested but not connected");
            return false;
        }

        var intensity = Math.Clamp(intensityPercent / 100.0, 0.0, 1.0);
        _logger?.LogInformation("Haptics test triggered (intensity {Intensity:P0}, duration {Duration}ms)", intensity, durationMs);

        await VibrateAsync(intensity, durationMs);
        return true;
    }

    /// <summary>
    /// Apply a named vibration <paramref name="mode"/> (pattern) at <paramref name="intensity"/>
    /// for <paramref name="durationMs"/>. Faithful port of WPF
    /// <c>HapticService.ApplyVibrationModeAsync</c> (Services/Haptics/HapticService.cs:230):
    /// the per-mode step structure (counts, intensities, durations, gap placement) comes from
    /// the shared <see cref="VibrationModePlanner"/> so every head plays the identical pattern.
    /// The device command is fire-and-forget (Buttplug sets a speed, the wrapper schedules an
    /// auto-stop), so each step is awaited for its full duration to keep adjacent pulses/gaps
    /// from collapsing into the last command. A 0-intensity step is a silence gap
    /// (<c>VibrateAsync(0, G)</c> stops the motor for G ms).
    /// </summary>
    public async Task ApplyVibrationModeAsync(double intensity, int durationMs, VibrationMode mode, CancellationToken? token = null)
    {
        // WPF HapticService.cs:232 — no-op when not connected.
        if (!IsConnected) return;

        var ct = token ?? CancellationToken.None;
        _logger?.LogInformation("Haptics vibration mode {Mode} (intensity {Intensity:P0}, duration {Duration}ms)",
            mode, intensity, durationMs);

        var steps = VibrationModePlanner.Plan(intensity, durationMs, mode);
        foreach (var step in steps)
        {
            // WPF HapticService.cs:237/252/266/284/300/312 — cancel between steps.
            if (ct.IsCancellationRequested) break;

            // Send the step then actually play it for its duration. Intensity 0 stops the
            // motor (silence gap). Awaiting the duration is required because VibrateAsync
            // returns as soon as the command is posted, not after the device plays it.
            await VibrateAsync(step.Intensity, step.DurationMs);
            try { await Task.Delay(step.DurationMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public Task SetSyncPatternAsync(float[] samples, int durationMs)
    {
        if (!IsConnected || samples == null || samples.Length == 0)
            return Task.CompletedTask;

        _logger?.LogDebug("Haptics sync pattern: {Duration}ms over {Samples} samples", durationMs, samples.Length);

        // Fire-and-forget: play the sample envelope as a series of short vibrations.
        _ = Task.Run(async () =>
        {
            try
            {
                var chunkMs = Math.Max(50, durationMs / samples.Length);
                for (int i = 0; i < samples.Length; i++)
                {
                    var intensity = Math.Clamp(samples[i], 0f, 1f);
                    if (intensity > 0)
                        await VibrateAsync(intensity, chunkMs);
                    else
                        await Task.Delay(chunkMs);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Haptics sync pattern playback failed");
            }
        });

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _vibrateCts?.Cancel();
        _vibrateCts = null;

        List<ButtplugClientDevice> devices;
        lock (_sync)
        {
            devices = _activeDevices.ToList();
        }

        if (devices.Count == 0 || _client?.Connected != true)
            return;

        var stops = devices.Select(d =>
        {
            try { return d.Stop(); }
            catch { return Task.CompletedTask; }
        });
        await Task.WhenAll(stops);
        _logger?.LogDebug("Haptics stopped {Count} device(s)", devices.Count);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }

    private async Task DisconnectAsync()
    {
        _vibrateCts?.Cancel();
        _vibrateCts = null;

        var client = _client;
        _client = null;

        if (client != null)
        {
            try
            {
                client.DeviceAdded -= OnDeviceAdded;
                client.DeviceRemoved -= OnDeviceRemoved;
                client.ServerDisconnect -= OnServerDisconnect;

                if (client.Connected)
                    await client.DisconnectAsync();
                client.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Haptics disconnect cleanup failed");
            }
        }

        List<string> removedNames;
        lock (_sync)
        {
            removedNames = _activeDevices.Select(d => d.Name).ToList();
            _activeDevices.Clear();
            _isConnected = false;
            _isConnecting = false;
        }

        foreach (var name in removedNames)
            DeviceRemoved?.Invoke(this, name);

        ConnectionStateChanged?.Invoke(this, false);
    }

    private void OnDeviceAdded(object? sender, DeviceAddedEventArgs e)
    {
        _logger?.LogInformation("Haptics device added: {Name}", e.Device.Name);
        lock (_sync)
        {
            if (_activeDevices.Any(d => d.Index == e.Device.Index))
                return;
            if (e.Device.VibrateAttributes.Count > 0)
                _activeDevices.Add(e.Device);
        }
        DeviceAdded?.Invoke(this, e.Device.Name);
        ConnectionStateChanged?.Invoke(this, true);
    }

    private void OnDeviceRemoved(object? sender, DeviceRemovedEventArgs e)
    {
        _logger?.LogInformation("Haptics device removed: {Name}", e.Device.Name);
        bool anyLeft;
        lock (_sync)
        {
            var existing = _activeDevices.FirstOrDefault(d => d.Index == e.Device.Index);
            if (existing != null)
                _activeDevices.Remove(existing);
            anyLeft = _activeDevices.Count > 0;
        }
        DeviceRemoved?.Invoke(this, e.Device.Name);
        ConnectionStateChanged?.Invoke(this, anyLeft);
    }

    private void OnServerDisconnect(object? sender, EventArgs e)
    {
        _logger?.LogWarning("Haptics server disconnected");
        List<string> removedNames;
        lock (_sync)
        {
            removedNames = _activeDevices.Select(d => d.Name).ToList();
            _activeDevices.Clear();
            _isConnected = false;
        }
        foreach (var name in removedNames)
            DeviceRemoved?.Invoke(this, name);
        ConnectionStateChanged?.Invoke(this, false);
    }

    private async Task VibrateAsync(double intensity, int durationMs)
    {
        List<ButtplugClientDevice> devices;
        lock (_sync)
        {
            devices = _activeDevices.ToList();
        }

        if (devices.Count == 0 || _client?.Connected != true)
            return;

        try
        {
            _vibrateCts?.Cancel();
            _vibrateCts = new CancellationTokenSource();
            var token = _vibrateCts.Token;
            var clamped = Math.Clamp(intensity, 0.0, 1.0);

            var tasks = devices.Select(d =>
            {
                try { return d.VibrateAsync(clamped); }
                catch { return Task.CompletedTask; }
            });
            await Task.WhenAll(tasks);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(durationMs, token);
                    if (!token.IsCancellationRequested)
                        await StopAsync();
                }
                catch (OperationCanceledException)
                {
                    // Expected when a new vibration starts.
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Haptics auto-stop failed");
                }
            }, token);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Haptics vibrate failed");
        }
    }
}
