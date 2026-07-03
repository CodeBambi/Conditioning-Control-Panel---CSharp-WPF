using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using NAudio.CoreAudioApi;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// Windows audio device enumeration using NAudio/CoreAudioAPI (WASAPI).
/// Provides accurate playback endpoint names and the system default device.
/// </summary>
public sealed class WindowsAudioDeviceService : IAudioDeviceService, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private string? _preferredDeviceId;
    private bool _disposed;

    public event EventHandler? PreferredDeviceChanged;

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            // Flag the true default endpoint so the picker can render the "(default)"
            // marker, matching the WPF EnumerateOutputDevices contract (WS0 lot 4 A1-14).
            string? defaultId = null;
            try
            {
                using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                defaultId = defaultDevice?.ID;
            }
            catch
            {
                // No default endpoint (e.g. no audio hardware); leave all unmarked.
            }

            var collection = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in collection)
            {
                using (device)
                {
                    devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName,
                        IsDefault: defaultId != null && device.ID == defaultId));
                }
            }
        }
        catch
        {
            // CoreAudio may be unavailable in some Windows sandboxes; fail open.
        }

        return devices;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _enumerator.Dispose(); } catch { }
    }

    public string? GetDefaultOutputDeviceId()
    {
        if (!string.IsNullOrEmpty(_preferredDeviceId))
            return _preferredDeviceId;

        try
        {
            using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return defaultDevice?.ID;
        }
        catch
        {
            return null;
        }
    }

    public void SetPreferredDevice(string? deviceId)
    {
        _preferredDeviceId = deviceId;
        PreferredDeviceChanged?.Invoke(this, EventArgs.Empty);
    }
}
