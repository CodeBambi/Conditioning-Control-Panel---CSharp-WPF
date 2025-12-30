using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NAudio.Wave;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Service for displaying subliminal text flashes across all monitors.
    /// Ported from Python engine.py _flash_subliminal / _show_subliminal_visuals
    /// </summary>
    public class SubliminalService : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly Random _random = new();
        private readonly List<Window> _activeWindows = new();
        private readonly string _audioPath;
        
        private WaveOutEvent? _audioPlayer;
        private AudioFileReader? _audioFile;
        
        private bool _isRunning;
        private bool _disposed;

        public SubliminalService()
        {
            _audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "sub_audio");
            Directory.CreateDirectory(_audioPath);
            
            _timer = new DispatcherTimer();
            _timer.Tick += Timer_Tick;
        }

        /// <summary>
        /// Start the subliminal service
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;
            
            _isRunning = true;
            ScheduleNext();
            
            App.Logger?.Information("SubliminalService started");
        }

        /// <summary>
        /// Stop the subliminal service
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            _timer.Stop();
            
            // Close any active windows
            foreach (var win in _activeWindows.ToList())
            {
                try { win.Close(); } catch { }
            }
            _activeWindows.Clear();
            
            StopAudio();
            
            App.Logger?.Information("SubliminalService stopped");
        }

        private void ScheduleNext()
        {
            if (!_isRunning || !App.Settings.Current.SubliminalEnabled) return;
            
            // Calculate interval based on frequency (messages per minute)
            var freq = Math.Max(1, App.Settings.Current.SubliminalFrequency);
            var baseInterval = 60.0 / freq; // seconds between messages
            
            // Add some randomness (±30%)
            var variance = baseInterval * 0.3;
            var interval = baseInterval + (_random.NextDouble() * variance * 2 - variance);
            interval = Math.Max(1, interval); // At least 1 second
            
            _timer.Interval = TimeSpan.FromSeconds(interval);
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _timer.Stop();
            
            if (!_isRunning || !App.Settings.Current.SubliminalEnabled)
                return;
            
            FlashSubliminal();
            ScheduleNext();
        }

        /// <summary>
        /// Display a subliminal flash
        /// </summary>
        public void FlashSubliminal()
        {
            var pool = App.Settings.Current.SubliminalPool;
            var activeTexts = pool.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
            
            if (activeTexts.Count == 0)
            {
                App.Logger?.Debug("No active subliminal texts");
                return;
            }
            
            var text = activeTexts[_random.Next(activeTexts.Count)];
            
            // Check for linked audio
            string? audioPath = FindLinkedAudio(text);
            
            if (audioPath != null && App.Settings.Current.SubAudioEnabled)
            {
                // Duck other audio, play whisper, then show visual
                App.Audio.Duck(App.Settings.Current.DuckingLevel);
                PlayWhisperAudio(audioPath);
                
                // Slight delay before showing visual
                Task.Delay(300).ContinueWith(_ => 
                {
                    Application.Current.Dispatcher.Invoke(() => ShowSubliminalVisuals(text));
                });
                
                // If "Bambi Freeze" was played, follow up with "Bambi Reset"
                if (text.Equals("Bambi Freeze", StringComparison.OrdinalIgnoreCase))
                {
                    ScheduleBambiReset();
                }
            }
            else
            {
                ShowSubliminalVisuals(text);
            }
            
            // Add XP
            App.Progression?.AddXP(1);
        }

        /// <summary>
        /// Trigger a Bambi Freeze subliminal with audio - used before videos and bubble count games
        /// </summary>
        public void TriggerBambiFreeze()
        {
            if (!_isRunning && !App.Settings.Current.SubliminalEnabled)
            {
                // Still allow Bambi Freeze even if subliminals are disabled - it's a special trigger
                App.Logger?.Debug("Triggering Bambi Freeze (subliminals disabled but special trigger allowed)");
            }
            
            var text = "Bambi Freeze";
            string? audioPath = FindLinkedAudio(text);
            
            if (audioPath != null)
            {
                // Duck other audio, play whisper, then show visual
                App.Audio?.Duck(App.Settings.Current.DuckingLevel);
                PlayWhisperAudio(audioPath);
                
                // Slight delay before showing visual
                Task.Delay(300).ContinueWith(_ => 
                {
                    Application.Current.Dispatcher.Invoke(() => ShowSubliminalVisuals(text));
                });
                
                // Schedule Bambi Reset after freeze
                ScheduleBambiReset();
                
                App.Logger?.Information("Bambi Freeze triggered with audio");
            }
            else
            {
                // No audio file, just show visual
                ShowSubliminalVisuals(text);
                App.Logger?.Information("Bambi Freeze triggered (no audio file found)");
            }
        }

        /// <summary>
        /// Schedule Bambi Reset to follow Bambi Freeze after a delay
        /// </summary>
        private void ScheduleBambiReset()
        {
            // Wait 2-4 seconds then show Bambi Reset
            var delay = _random.Next(2000, 4000);
            Task.Delay(delay).ContinueWith(_ =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var resetText = "Bambi Reset";
                    string? resetAudio = FindLinkedAudio(resetText);
                    
                    if (resetAudio != null && App.Settings.Current.SubAudioEnabled)
                    {
                        App.Audio.Duck(App.Settings.Current.DuckingLevel);
                        PlayWhisperAudio(resetAudio);
                        Task.Delay(300).ContinueWith(_ =>
                        {
                            Application.Current.Dispatcher.Invoke(() => ShowSubliminalVisuals(resetText));
                        });
                    }
                    else
                    {
                        ShowSubliminalVisuals(resetText);
                    }
                    
                    App.Logger?.Debug("Bambi Reset triggered after Bambi Freeze");
                });
            });
        }

        private string? FindLinkedAudio(string text)
        {
            var cleanText = text.Trim();
            var extensions = new[] { ".mp3", ".wav", ".ogg" };
            
            foreach (var ext in extensions)
            {
                var path = Path.Combine(_audioPath, cleanText + ext);
                if (File.Exists(path)) return path;
                
                var pathLower = Path.Combine(_audioPath, cleanText.ToLower() + ext);
                if (File.Exists(pathLower)) return pathLower;
            }
            
            return null;
        }

        private void PlayWhisperAudio(string path)
        {
            try
            {
                StopAudio();
                
                _audioFile = new AudioFileReader(path);
                _audioPlayer = new WaveOutEvent();
                
                // Apply volume with curve
                var vol = App.Settings.Current.SubAudioVolume / 100.0f;
                var curvedVol = Math.Max(0.05f, (float)Math.Pow(vol, 1.5));
                _audioFile.Volume = curvedVol;
                
                _audioPlayer.Init(_audioFile);
                _audioPlayer.PlaybackStopped += (s, e) =>
                {
                    // Unduck after playback + small delay
                    Task.Delay(500).ContinueWith(_ => App.Audio.Unduck());
                };
                _audioPlayer.Play();
                
                App.Logger?.Debug("Playing subliminal audio: {Path}", Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Could not play subliminal audio: {Error}", ex.Message);
                App.Audio.Unduck();
            }
        }

        private void StopAudio()
        {
            try
            {
                _audioPlayer?.Stop();
                _audioPlayer?.Dispose();
                _audioFile?.Dispose();
            }
            catch { }
            
            _audioPlayer = null;
            _audioFile = null;
        }

        private void ShowSubliminalVisuals(string text)
        {
            // Duration in frames * ~16.6ms per frame, minimum 100ms
            var durationMs = Math.Max(100, App.Settings.Current.SubliminalDuration * 17);
            var targetOpacity = App.Settings.Current.SubliminalOpacity / 100.0;
            
            // Colors from settings
            var bgColor = ParseColor(App.Settings.Current.SubBackgroundColor, Colors.Black);
            var textColor = ParseColor(App.Settings.Current.SubTextColor, Color.FromRgb(255, 0, 255)); // Magenta
            var borderColor = ParseColor(App.Settings.Current.SubBorderColor, Colors.White);
            var bgTransparent = App.Settings.Current.SubBackgroundTransparent;
            
            // Get all monitors and create windows for all at once
            var screens = System.Windows.Forms.Screen.AllScreens;
            var windows = new List<Window>();
            
            // Create all windows first (don't show yet)
            foreach (var screen in screens)
            {
                var win = CreateSubliminalWindow(screen, text, targetOpacity, 
                    bgColor, textColor, borderColor, bgTransparent);
                windows.Add(win);
            }
            
            // Show all windows simultaneously
            foreach (var win in windows)
            {
                win.Show();
                _activeWindows.Add(win);
                ForceTopmost(win);
            }
            
            // Animate all windows simultaneously
            foreach (var win in windows)
            {
                AnimateSubliminal(win, targetOpacity, durationMs);
            }
        }

        /// <summary>
        /// Force window to stay on top even over fullscreen apps
        /// </summary>
        private void ForceTopmost(Window window)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch { }
        }

        private Window CreateSubliminalWindow(System.Windows.Forms.Screen screen, string text, 
            double targetOpacity, Color bgColor, Color textColor, 
            Color borderColor, bool bgTransparent)
        {
            // Get primary monitor DPI - WPF uses this for coordinate system
            double primaryDpi = GetPrimaryMonitorDpi();
            double scale = primaryDpi / 96.0;
            
            // Convert physical pixels to WPF units
            double left = screen.Bounds.X / scale;
            double top = screen.Bounds.Y / scale;
            double width = screen.Bounds.Width / scale;
            double height = screen.Bounds.Height / scale;

            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = bgTransparent ? Brushes.Transparent : new SolidColorBrush(bgColor),
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                Opacity = 0
            };

            var canvas = new Canvas
            {
                Background = Brushes.Transparent,
                Width = width,
                Height = height
            };

            // Create text with border effect (multiple offset copies for outline)
            var centerX = width / 2.0;
            var centerY = height / 2.0;
            var fontSize = 120;
            
            // Border/outline offsets
            var offsets = new (double x, double y)[]
            {
                (-3, -3), (3, -3), (-3, 3), (3, 3),
                (0, -4), (0, 4), (-4, 0), (4, 0)
            };

            // Draw border text
            foreach (var (ox, oy) in offsets)
            {
                var borderText = CreateTextBlock(text, fontSize, borderColor);
                borderText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(borderText, centerX - borderText.DesiredSize.Width / 2 + ox);
                Canvas.SetTop(borderText, centerY - borderText.DesiredSize.Height / 2 + oy);
                canvas.Children.Add(borderText);
            }

            // Draw main text
            var mainText = CreateTextBlock(text, fontSize, textColor);
            mainText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(mainText, centerX - mainText.DesiredSize.Width / 2);
            Canvas.SetTop(mainText, centerY - mainText.DesiredSize.Height / 2);
            canvas.Children.Add(mainText);

            win.Content = canvas;
            
            // Set window styles to hide from Alt+Tab and prevent focus
            win.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
            };
            
            return win;
        }

        private double GetPrimaryMonitorDpi()
        {
            try
            {
                var primary = System.Windows.Forms.Screen.PrimaryScreen;
                if (primary != null)
                {
                    var hMonitor = MonitorFromPoint(new POINT { X = primary.Bounds.X + 1, Y = primary.Bounds.Y + 1 }, 2);
                    if (hMonitor != IntPtr.Zero)
                    {
                        var result = GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
                        if (result == 0)
                        {
                            return dpiX;
                        }
                    }
                }
            }
            catch { }
            return 96.0;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        private TextBlock CreateTextBlock(string text, double fontSize, Color color)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Arial"),
                Foreground = new SolidColorBrush(color),
                TextAlignment = TextAlignment.Center
            };
        }

        private void AnimateSubliminal(Window win, double targetOpacity, int holdMs)
        {
            var fadeInDuration = TimeSpan.FromMilliseconds(50);
            var holdDuration = TimeSpan.FromMilliseconds(holdMs);
            var fadeOutDuration = TimeSpan.FromMilliseconds(50);

            var storyboard = new Storyboard();

            // Fade in
            var fadeIn = new DoubleAnimation(0, targetOpacity, fadeInDuration);
            Storyboard.SetTarget(fadeIn, win);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(Window.OpacityProperty));
            storyboard.Children.Add(fadeIn);

            // Hold (stay at target opacity)
            var hold = new DoubleAnimation(targetOpacity, targetOpacity, holdDuration)
            {
                BeginTime = fadeInDuration
            };
            Storyboard.SetTarget(hold, win);
            Storyboard.SetTargetProperty(hold, new PropertyPath(Window.OpacityProperty));
            storyboard.Children.Add(hold);

            // Fade out
            var fadeOut = new DoubleAnimation(targetOpacity, 0, fadeOutDuration)
            {
                BeginTime = fadeInDuration + holdDuration
            };
            Storyboard.SetTarget(fadeOut, win);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(Window.OpacityProperty));
            storyboard.Children.Add(fadeOut);

            storyboard.Completed += (s, e) =>
            {
                _activeWindows.Remove(win);
                win.Close();
            };

            storyboard.Begin();
        }

        private Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return fallback;
                if (!hex.StartsWith("#")) hex = "#" + hex;
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return fallback;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();
            StopAudio();
        }

        #region Win32

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        #endregion
    }
}
