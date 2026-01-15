using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Haptics
{
    public enum HapticProviderType
    {
        None,
        Mock,
        Lovense,
        Buttplug
    }

    public interface IHapticProvider
    {
        string Name { get; }
        bool IsConnected { get; }
        List<string> ConnectedDevices { get; }

        event EventHandler<bool>? ConnectionChanged;
        event EventHandler<string>? DeviceDiscovered;
        event EventHandler<string>? Error;

        Task<bool> ConnectAsync();
        Task DisconnectAsync();
        Task VibrateAsync(double intensity, int durationMs);
        Task StopAsync();
    }
}
