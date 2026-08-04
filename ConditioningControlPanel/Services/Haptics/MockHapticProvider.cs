using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Services.Haptics.Core;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// Legacy (v1) mock provider. Superseded by <see cref="Core.MockProviderV2"/>, which models
    /// three virtual toys with real actuator capabilities; kept so the v1 IHapticProvider path
    /// still compiles until the v2 providers land. The toast window it used to own now lives in
    /// <see cref="Core.MockToast"/> so BOTH mocks share ONE window (HWND-leak history — see that file).
    /// </summary>
    public class MockHapticProvider : IHapticProvider
    {
        public string Name => "Mock (Testing)";
        public bool IsConnected { get; private set; }
        public List<string> ConnectedDevices { get; } = new();

        public event EventHandler<bool>? ConnectionChanged;
        public event EventHandler<string>? DeviceDiscovered;
#pragma warning disable CS0067 // Required by IHapticProvider interface
        public event EventHandler<string>? Error;
#pragma warning restore CS0067

        public Task<bool> ConnectAsync()
        {
            IsConnected = true;
            ConnectedDevices.Clear();
            ConnectedDevices.Add("Mock Vibrator 1");
            ConnectedDevices.Add("Mock Vibrator 2");

            DeviceDiscovered?.Invoke(this, "Mock Vibrator 1");
            DeviceDiscovered?.Invoke(this, "Mock Vibrator 2");
            ConnectionChanged?.Invoke(this, true);

            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            ConnectedDevices.Clear();
            ConnectionChanged?.Invoke(this, false);
            return Task.CompletedTask;
        }

        public Task VibrateAsync(double intensity, int durationMs)
        {
            if (!IsConnected) return Task.CompletedTask;

            var percentage = (int)(intensity * 100);
            MockToast.Post($"Haptic: {percentage}% for {durationMs}ms");
            return Task.CompletedTask;
        }

        public Task<bool> PingAsync() => Task.FromResult(IsConnected);

        public Task StopAsync()
        {
            if (!IsConnected) return Task.CompletedTask;
            MockToast.Post("Haptic: Stopped");
            return Task.CompletedTask;
        }

    }
}
