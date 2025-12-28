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
            }
            else
            {
                ShowSubliminalVisuals(text);
            }
            
            // Add XP
            App.Progression?.AddXP(1);
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
            }
            
            // Animate all windows simultaneously
            foreach (var win in windows)
            {
                AnimateSubliminal(win, targetOpacity, durationMs);
            }
        }

        private Window CreateSubliminalWindow(System.Windows.Forms.Screen screen, string text, 
            double targetOpacity, Color bgColor, Color textColor, 
            Color borderColor, bool bgTransparent)
        {
            // Get DPI scaling for this screen
            double dpiScale = GetDpiScale(screen);
            
            // Calculate actual pixel positions (accounting for DPI)
            double left = screen.Bounds.X / dpiScale;
            double top = screen.Bounds.Y / dpiScale;
            double width = screen.Bounds.Width / dpiScale;
            double height = screen.Bounds.Height / dpiScale;
            
            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = bgTransparent ? Brushes.Transparent : new SolidColorBrush(bgColor),
                Topmost = true,
                ShowInTaskbar = false,
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
            
            return win;
        }

        private double GetDpiScale(System.Windows.Forms.Screen screen)
        {
            try
            {
                // Try to get DPI for the specific screen
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    return g.DpiX / 96.0;
                }
            }
            catch
            {
                return 1.0;
            }
        }

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
    }
}
