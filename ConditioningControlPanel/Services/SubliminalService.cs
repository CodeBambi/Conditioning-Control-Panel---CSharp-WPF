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
        private int _subliminalCount;

        /// <summary>
        /// Fired when a subliminal is displayed
        /// </summary>
        public event EventHandler? SubliminalDisplayed;

        public SubliminalService()
        {
            _audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sub_audio");
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
                if (App.Settings.Current.AudioDuckingEnabled)
                    App.Audio.Duck(App.Settings.Current.DuckingLevel);
                PlayWhisperAudio(audioPath);
                
                // Haptic triggers 250ms before visual appears
                Task.Delay(50).ContinueWith(_ =>
                {
                    _ = App.Haptics?.TriggerSubliminalPatternAsync(text);
                    Task.Delay(250).ContinueWith(__ =>
                    {
                        Application.Current?.Dispatcher?.Invoke(() => ShowSubliminalVisuals(text));
                    });
                });

                // If "Bambi Freeze" was played, follow up with "Bambi Reset"
                if (text.Equals("Bambi Freeze", StringComparison.OrdinalIgnoreCase))
                {
                    ScheduleBambiReset();
                }
                App.Progression?.AddXP(20, XPSource.Subliminal);
            }
            else
            {
                TriggerSubliminalWithHapticPattern(text);
                App.Progression?.AddXP(10, XPSource.Subliminal);
            }
        }

        // Track if a deferred reset is pending (for when video ends)
        private bool _deferredResetPending;

        /// <summary>
        /// Trigger a Bambi Freeze subliminal with audio - used before videos and bubble count games
        /// </summary>
        /// <param name="deferReset">If true, don't schedule reset immediately (call TriggerDeferredBambiReset later)</param>
        public void TriggerBambiFreeze(bool deferReset = false)
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
                if (App.Settings.Current.AudioDuckingEnabled)
                    App.Audio?.Duck(App.Settings.Current.DuckingLevel);
                PlayWhisperAudio(audioPath);

                // Haptic triggers 250ms before visual appears
                Task.Delay(50).ContinueWith(_ =>
                {
                    _ = App.Haptics?.TriggerSubliminalPatternAsync(text);
                    Task.Delay(250).ContinueWith(__ =>
                    {
                        Application.Current?.Dispatcher?.Invoke(() => ShowSubliminalVisuals(text));
                    });
                });

                // Handle reset scheduling
                if (deferReset)
                {
                    // Mark that we should trigger reset when video ends
                    _deferredResetPending = true;
                    App.Logger?.Information("Bambi Freeze triggered with audio (reset deferred until video ends)");
                }
                else
                {
                    // Schedule Bambi Reset after freeze with delay
                    ScheduleBambiReset();
                    App.Logger?.Information("Bambi Freeze triggered with audio");
                }
            }
            else
            {
                // No audio file, just show visual with haptic
                TriggerSubliminalWithHapticPattern(text);
                App.Logger?.Information("Bambi Freeze triggered (no audio file found)");
            }
        }

        /// <summary>
        /// Trigger the deferred Bambi Reset (called when video ends)
        /// </summary>
        public void TriggerDeferredBambiReset()
        {
            if (!_deferredResetPending)
            {
                return;
            }

            _deferredResetPending = false;

            // 90% chance to trigger reset
            if (_random.NextDouble() > 0.90)
            {
                App.Logger?.Debug("Bambi Reset skipped (10% chance roll)");
                return;
            }

            // Trigger reset after a short delay (1-2 seconds after video ends)
            var delay = _random.Next(1000, 2000);
            Task.Delay(delay).ContinueWith(_ =>
            {
                Application.Current.Dispatcher.Invoke(() => PlayBambiReset());
            });
        }

        /// <summary>
        /// Schedule Bambi Reset to follow Bambi Freeze after a delay
        /// </summary>
        private void ScheduleBambiReset()
        {
            // 90% chance to trigger reset
            if (_random.NextDouble() > 0.90)
            {
                App.Logger?.Debug("Bambi Reset skipped (10% chance roll)");
                return;
            }

            // Wait 4-8 seconds then show Bambi Reset (longer delay than before)
            var delay = _random.Next(4000, 8000);
            Task.Delay(delay).ContinueWith(_ =>
            {
                Application.Current.Dispatcher.Invoke(() => PlayBambiReset());
            });
        }

        /// <summary>
        /// Play the Bambi Reset audio and visual
        /// </summary>
        private void PlayBambiReset()
        {
            var resetText = "Bambi Reset";
            string? resetAudio = FindLinkedAudio(resetText);

            if (resetAudio != null && App.Settings.Current.SubAudioEnabled)
            {
                if (App.Settings.Current.AudioDuckingEnabled)
                    App.Audio?.Duck(App.Settings.Current.DuckingLevel);
                PlayWhisperAudio(resetAudio);
                // Haptic triggers 250ms before visual appears
                Task.Delay(50).ContinueWith(_ =>
                {
                    _ = App.Haptics?.TriggerSubliminalPatternAsync(resetText);
                    Task.Delay(250).ContinueWith(__ =>
                    {
                        Application.Current?.Dispatcher?.Invoke(() => ShowSubliminalVisuals(resetText));
                    });
                });
            }
            else
            {
                TriggerSubliminalWithHapticPattern(resetText);
            }

            App.Logger?.Debug("Bambi Reset triggered after Bambi Freeze");
        }

        /// <summary>
        /// Play the audio clip for a trigger phrase (if whispers enabled and audio exists)
        /// Used by Trigger Mode to play matching audio when showing trigger bubbles
        /// </summary>
        public void PlayTriggerAudio(string trigger)
        {
            // Check if whispers are enabled
            if (App.Settings?.Current?.SubAudioEnabled != true)
            {
                return;
            }

            var audioPath = FindLinkedAudio(trigger);
            if (audioPath != null)
            {
                // Duck other audio briefly
                if (App.Settings?.Current?.AudioDuckingEnabled == true)
                    App.Audio?.Duck(App.Settings?.Current?.DuckingLevel ?? 80);
                PlayWhisperAudio(audioPath);
                App.Logger?.Debug("TriggerMode: Playing audio for '{Trigger}'", trigger);
            }
        }

        private string? FindLinkedAudio(string text)
        {
            var cleanText = text.Trim();
            var extensions = new[] { ".mp3", ".wav", ".ogg", ".MP3", ".WAV", ".OGG" };

            // Try various case combinations
            var textVariants = new[]
            {
                cleanText,                          // As-is
                cleanText.ToUpper(),                // UPPERCASE
                cleanText.ToLower(),                // lowercase
                cleanText.Replace("'", "'"),        // Normalize curly apostrophe to straight
                cleanText.Replace("'", "'"),        // Normalize straight apostrophe to curly
                cleanText.ToUpper().Replace("'", "'"),
            };

            foreach (var textVar in textVariants)
            {
                foreach (var ext in extensions)
                {
                    var path = Path.Combine(_audioPath, textVar + ext);
                    if (File.Exists(path)) return path;
                }
            }

            // Fallback: case-insensitive directory search
            try
            {
                if (Directory.Exists(_audioPath))
                {
                    var files = Directory.GetFiles(_audioPath);
                    var normalizedText = cleanText.ToUpperInvariant().Replace("'", "'");

                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file).ToUpperInvariant().Replace("'", "'");
                        if (fileName == normalizedText)
                        {
                            return file;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Error searching audio directory: {Error}", ex.Message);
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

                // Apply volume with curve, including master volume
                var masterVol = App.Settings.Current.MasterVolume / 100.0f;
                var subVol = App.Settings.Current.SubAudioVolume / 100.0f;
                var curvedVol = (float)Math.Pow(subVol * masterVol, 1.5);
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

        /// <summary>
        /// Trigger haptic pattern before showing subliminal, then show visuals
        /// Pattern depends on the trigger text (Cum/Collapse = long, Freeze = short sharp, Sleep = decay, etc.)
        /// Buttplug.io has ~1.3s latency so we trigger haptics earlier for that provider
        /// </summary>
        private async void TriggerSubliminalWithHapticPattern(string text)
        {
            // Get anticipation delay from haptic service (Buttplug needs ~1.3s, Lovense ~250ms)
            var anticipationMs = App.Haptics?.SubliminalAnticipationMs ?? 250;

            // Trigger haptic pattern first (pattern depends on text)
            _ = App.Haptics?.TriggerSubliminalPatternAsync(text);

            // Wait for anticipation delay before showing visual
            await Task.Delay(anticipationMs);

            // Now show on UI thread
            Application.Current?.Dispatcher?.Invoke(() => ShowSubliminalVisuals(text));
        }

        private void ShowSubliminalVisuals(string text)
        {
            // Increment counter and fire event
            _subliminalCount++;
            SubliminalDisplayed?.Invoke(this, EventArgs.Empty);

            // Duration in frames * ~16.6ms per frame, minimum 100ms
            var durationMs = Math.Max(100, App.Settings.Current.SubliminalDuration * 17);
            var targetOpacity = App.Settings.Current.SubliminalOpacity / 100.0;

            // Colors from settings
            var bgColor = ParseColor(App.Settings.Current.SubBackgroundColor, Colors.Black);
            var textColor = ParseColor(App.Settings.Current.SubTextColor, Color.FromRgb(255, 0, 255)); // Magenta
            var borderColor = ParseColor(App.Settings.Current.SubBorderColor, Colors.White);
            var bgTransparent = App.Settings.Current.SubBackgroundTransparent;

            // Get all monitors and create windows for all at once
            var screens = App.GetAllScreensCached();
            var windows = new List<Window>();
            var stealsFocus = App.Settings.Current.SubliminalStealsFocus;

            // Create all windows first (don't show yet)
            // SourceInitialized handler will apply click-through styles BEFORE window is rendered
            foreach (var screen in screens)
            {
                var win = CreateSubliminalWindow(screen, text, targetOpacity,
                    bgColor, textColor, borderColor, bgTransparent, stealsFocus);
                windows.Add(win);
            }

            // Show all windows simultaneously
            foreach (var win in windows)
            {
                win.Show();

                // If stealing focus is enabled, activate the window to force keyboard focus
                if (stealsFocus)
                {
                    win.Activate();
                }

                _activeWindows.Add(win);
            }

            // Animate all windows simultaneously
            foreach (var win in windows)
            {
                AnimateSubliminal(win, targetOpacity, durationMs);
            }
        }

        private const int WS_EX_LAYERED = 0x00080000;

        private Window CreateSubliminalWindow(System.Windows.Forms.Screen screen, string text,
            double targetOpacity, Color bgColor, Color textColor,
            Color borderColor, bool bgTransparent, bool stealsFocus)
        {
            // Use EXACTLY the same approach as OverlayService.GetWpfScreenBounds
            // which works correctly for multi-monitor DPI setups
            var bounds = screen.Bounds;

            // WPF uses the primary monitor's DPI as the reference for ALL coordinate calculations
            double primaryDpi = GetPrimaryMonitorDpi();
            double primaryScale = primaryDpi / 96.0;

            // BOTH position AND size divided by primaryScale (same as OverlayService)
            double left = bounds.X / primaryScale;
            double top = bounds.Y / primaryScale;
            double width = bounds.Width / primaryScale;
            double height = bounds.Height / primaryScale;

            App.Logger?.Debug("Subliminal window for {Screen}: Bounds=({BX},{BY},{BW}x{BH}), PrimaryDPI={PDPI}, PrimaryScale={PS}, WPF=({WL},{WT},{WW}x{WH})",
                screen.DeviceName, bounds.X, bounds.Y, bounds.Width, bounds.Height, primaryDpi, primaryScale, left, top, width, height);

            // Capture screen bounds for use in SourceInitialized handler
            var targetBounds = bounds;

            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent, // Always transparent for click-through
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

            // Use a Grid that stretches to fill the window (unlike Canvas with explicit size)
            var grid = new Grid
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };

            // Add colored background as child element if not transparent
            if (!bgTransparent)
            {
                var bgRect = new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(bgColor),
                    IsHitTestVisible = false,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = System.Windows.VerticalAlignment.Stretch
                };
                grid.Children.Add(bgRect);
            }

            // Create text container that centers content
            var textCanvas = new Canvas
            {
                IsHitTestVisible = false,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };

            var fontSize = 120;

            // Border/outline offsets
            var offsets = new (double x, double y)[]
            {
                (-3, -3), (3, -3), (-3, 3), (3, 3),
                (0, -4), (0, 4), (-4, 0), (4, 0)
            };

            // Draw border text (will be positioned in Loaded event when we know actual size)
            foreach (var (ox, oy) in offsets)
            {
                var borderText = CreateTextBlock(text, fontSize, borderColor);
                borderText.Tag = (ox, oy, true); // Store offset and isBorder flag
                textCanvas.Children.Add(borderText);
            }

            // Draw main text
            var mainText = CreateTextBlock(text, fontSize, textColor);
            mainText.Tag = (0.0, 0.0, false); // No offset, not border
            textCanvas.Children.Add(mainText);

            grid.Children.Add(textCanvas);
            win.Content = grid;

            // CRITICAL: Apply click-through styles in SourceInitialized (BEFORE window is rendered)
            // This is the same pattern OverlayService uses and prevents focus stealing during Show()
            win.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    if (stealsFocus)
                    {
                        // Only mouse click-through, but allow focus steal
                        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                    }
                    else
                    {
                        // Full click-through: mouse passes through AND no focus stealing
                        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                    }

                    // Position window using SetWindowPos with SWP_NOACTIVATE for robust no-focus behavior
                    // Use physical pixel coordinates to bypass WPF DPI virtualization
                    SetWindowPos(hwnd, HWND_TOPMOST,
                        targetBounds.X, targetBounds.Y, targetBounds.Width, targetBounds.Height,
                        SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }
            };

            // Position text when window is loaded (we now know actual size)
            win.Loaded += (s, e) =>
            {
                var centerX = win.ActualWidth / 2.0;
                var centerY = win.ActualHeight / 2.0;

                foreach (var child in textCanvas.Children)
                {
                    if (child is TextBlock tb && tb.Tag is (double ox, double oy, bool isBorder))
                    {
                        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        Canvas.SetLeft(tb, centerX - tb.DesiredSize.Width / 2 + ox);
                        Canvas.SetTop(tb, centerY - tb.DesiredSize.Height / 2 + oy);
                    }
                }
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

        private double GetMonitorDpi(System.Windows.Forms.Screen screen)
        {
            try
            {
                // Get a point inside this monitor
                var hMonitor = MonitorFromPoint(new POINT { X = screen.Bounds.X + 1, Y = screen.Bounds.Y + 1 }, 2);
                if (hMonitor != IntPtr.Zero)
                {
                    var result = GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
                    if (result == 0)
                    {
                        return dpiX;
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
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
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
