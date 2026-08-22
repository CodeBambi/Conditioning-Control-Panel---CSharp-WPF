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

        /// <summary>
        /// Verify the device is still reachable. IsConnected can lie when the OS routing
        /// table changes after connect (e.g. user enables a VPN), so call this before any
        /// operation that needs to confirm we can actually talk to the device.
        /// </summary>
        Task<bool> PingAsync();
    }
}
