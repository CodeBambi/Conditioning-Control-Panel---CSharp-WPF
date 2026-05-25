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

        private Window? _toastWindow;
        private System.Windows.Controls.TextBlock? _toastText;
        private System.Windows.Threading.DispatcherTimer? _toastTimer;

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

        public Task<bool> PingAsync() => Task.FromResult(IsConnected);

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
            // Reuse a single toast window — AudioSync fires this at video frame rate
            // (~30Hz), so spawning a new Window per call crashed the WPF render thread
            // with UCEERR_RENDERTHREADFAILURE after ~60s of leaked HWNDs.
            if (_toastWindow == null)
            {
                _toastText = new System.Windows.Controls.TextBlock
                {
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 14
                };

                _toastWindow = new Window
                {
                    Width = 200,
                    Height = 50,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(230, 255, 105, 180)),
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    ResizeMode = ResizeMode.NoResize,
                    Content = _toastText
                };

                _toastWindow.Closed += (s, e) =>
                {
                    _toastWindow = null;
                    _toastText = null;
                    _toastTimer?.Stop();
                    _toastTimer = null;
                };

                var screen = SystemParameters.WorkArea;
                _toastWindow.Left = screen.Right - _toastWindow.Width - 20;
                _toastWindow.Top = screen.Bottom - _toastWindow.Height - 20;

                _toastWindow.Show();
            }

            if (_toastText != null) _toastText.Text = message;

            if (_toastTimer == null)
            {
                _toastTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1000)
                };
                _toastTimer.Tick += (s, e) =>
                {
                    _toastTimer?.Stop();
                    _toastWindow?.Close();
                };
            }
            _toastTimer.Stop();
            _toastTimer.Start();
        }
    }
}
