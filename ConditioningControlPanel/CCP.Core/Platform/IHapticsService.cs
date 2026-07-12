using System.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform abstraction for a haptic device provider.
/// Implementations may be real device bridges (Lovense, Buttplug.io, etc.)
/// or stubs used for UI testing in the Avalonia head.
/// </summary>
public interface IHapticsService
{
    bool IsConnected { get; }
    bool IsConnecting { get; }
    IReadOnlyList<string> ConnectedDevices { get; }

    event EventHandler<bool>? ConnectionStateChanged;
    event EventHandler<string>? DeviceAdded;
    event EventHandler<string>? DeviceRemoved;

    Task<bool> ConnectAsync(string providerUrl);
    void Disconnect();
    Task<bool> TestAsync(int intensityPercent, int durationMs);

    /// <summary>
    /// Apply a named vibration <paramref name="mode"/> (pattern) at <paramref name="intensity"/>
    /// (0..1 fraction) for <paramref name="durationMs"/> milliseconds. Faithful port of WPF
    /// <c>HapticService.ApplyVibrationModeAsync</c> (Services/Haptics/HapticService.cs:230) — the
    /// AI haptic command (<c>HapticCommand.cs:24</c>) calls this with <c>VibrationMode.Pulse</c>.
    /// Default implementation is a safe no-op so fakes/stubs compile unchanged; concrete heads
    /// override with device playback built from <see cref="VibrationModePlanner"/>.
    /// </summary>
    Task ApplyVibrationModeAsync(double intensity, int durationMs, VibrationMode mode, CancellationToken? token = null)
        => Task.CompletedTask;

    /// <summary>Play a synchronous haptic pattern over the given duration in milliseconds.</summary>
    Task SetSyncPatternAsync(float[] samples, int durationMs);

    /// <summary>Stop any active haptic pattern immediately.</summary>
    Task StopAsync();
}
