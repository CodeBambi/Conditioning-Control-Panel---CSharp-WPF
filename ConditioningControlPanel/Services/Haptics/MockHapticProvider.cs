using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// Mock provider for testing haptic feedback without hardware
    /// </summary>
    public class MockHapticProvider : IHapticProvider
    {
        public string Name => "Mock (Testing)";
        public bool IsConnected { get; private set; }
        public List<string> ConnectedDevices { get; } = new();

        public event EventHandler<bool>? ConnectionChanged;
        public event EventHandler<string>? DeviceDiscovered;
        public event EventHandler<string>? Error;

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

            // Show a toast notification for testing
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                try
                {
                    var percentage = (int)(intensity * 100);
                    ShowHapticToast($"Haptic: {percentage}% for {durationMs}ms");
                }
                catch { }
            });

            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            if (!IsConnected) return Task.CompletedTask;

            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                try
                {
                    ShowHapticToast("Haptic: Stopped");
                }
                catch { }
            });

            return Task.CompletedTask;
        }

        private void ShowHapticToast(string message)
        {
            // Create a simple toast window
            var toast = new Window
            {
                Width = 200,
                Height = 50,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(230, 255, 105, 180)),
                Topmost = true,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize
            };

            var text = new System.Windows.Controls.TextBlock
            {
                Text = message,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14
            };

            toast.Content = text;

            // Position bottom-right
            var screen = SystemParameters.WorkArea;
            toast.Left = screen.Right - toast.Width - 20;
            toast.Top = screen.Bottom - toast.Height - 20;

            toast.Show();

            // Auto-close after 1 second
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                toast.Close();
            };
            timer.Start();
        }
    }
}
