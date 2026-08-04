using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Haptics.Core
{
    /// <summary>
    /// The single on-screen toast used by BOTH mock providers (legacy
    /// <see cref="MockHapticProvider"/> and <see cref="MockProviderV2"/>).
    ///
    /// HWND-LEAK LOAD-BEARING: this used to live inside MockHapticProvider as an instance
    /// field. AudioSync drives haptics at video frame rate (~30 Hz), and spawning a new
    /// <see cref="Window"/> per call crashed the WPF render thread with
    /// UCEERR_RENDERTHREADFAILURE after ~60 s of leaked HWNDs. The singleton is the fix —
    /// hoisting it to a static shared instance keeps it a singleton even when both mock
    /// providers are alive at once. Do not "simplify" this into per-call windows.
    /// </summary>
    internal static class MockToast
    {
        private static Window? _window;
        private static System.Windows.Controls.TextBlock? _text;
        private static DispatcherTimer? _timer;

        /// <summary>Marshal to the UI thread and show/refresh the toast. Safe from any thread.</summary>
        public static void Post(string message)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;                      // shutting down / no WPF app
            if (dispatcher.HasShutdownStarted) return;
            try
            {
                // DispatcherPriority.Normal on purpose: Loaded-priority work is starved in
                // this app and would never run.
                dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    try { Show(message); } catch { }
                }));
            }
            catch { }
        }

        /// <summary>UI-thread only.</summary>
        private static void Show(string message)
        {
            if (_window == null)
            {
                _text = new System.Windows.Controls.TextBlock
                {
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 12,
                    Margin = new Thickness(8, 4, 8, 4)
                };

                _window = new Window
                {
                    Width = 240,
                    Height = 92,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(230, 255, 105, 180)),
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    ResizeMode = ResizeMode.NoResize,
                    Content = _text
                };

                _window.Closed += (s, e) =>
                {
                    _window = null;
                    _text = null;
                    _timer?.Stop();
                    _timer = null;
                };

                var screen = SystemParameters.WorkArea;
                _window.Left = screen.Right - _window.Width - 20;
                _window.Top = screen.Bottom - _window.Height - 20;

                _window.Show();
            }

            if (_text != null) _text.Text = message;

            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                _timer.Tick += (s, e) =>
                {
                    _timer?.Stop();
                    _window?.Close();
                };
            }
            _timer.Stop();
            _timer.Start();
        }
    }
}
