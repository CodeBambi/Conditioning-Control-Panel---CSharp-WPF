using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Application = System.Windows.Application;
using Screen = System.Windows.Forms.Screen;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Service for playing videos across multiple monitors using a single decoder.
    /// Uses LibVLC memory rendering to a shared WriteableBitmap that both monitors display.
    /// This approach uses minimal resources: 1 decoder, 1 audio track, 1 memory buffer.
    /// </summary>
    public class DualMonitorVideoService : IDisposable
    {
        private LibVLC? _libVLC;
        private VlcMediaPlayer? _mediaPlayer;
        private WriteableBitmap? _sharedFrame;
        private IntPtr _frameBuffer = IntPtr.Zero;
        private uint _videoWidth;
        private uint _videoHeight;
        private readonly List<Window> _windows = new();
        private readonly object _bufferLock = new();
        private volatile bool _frameReady;
        private volatile bool _bufferValid;  // Guards buffer access across threads
        private bool _isPlaying;
        private bool _disposed;

        // Events
        public event EventHandler? PlaybackStarted;
        public event EventHandler? PlaybackEnded;
        public event EventHandler<string>? PlaybackError;

        // Win32 for window positioning
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        // Win32 for memory copy (safe alternative to unsafe code)
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        public bool IsPlaying => _isPlaying;

        /// <summary>
        /// Play a video URL on all monitors simultaneously.
        /// Uses a single decoder with shared frame buffer for efficiency.
        /// </summary>
        /// <param name="videoUrl">Direct video URL (mp4, m3u8, etc.)</param>
        /// <param name="width">Video width for buffer allocation (default 1920)</param>
        /// <param name="height">Video height for buffer allocation (default 1080)</param>
        public void Play(string videoUrl, uint width = 1920, uint height = 1080)
        {
            if (_isPlaying)
            {
                Stop();
            }

            try
            {
                _videoWidth = width;
                _videoHeight = height;

                // Initialize LibVLC
                InitializeLibVLC();

                if (_libVLC == null)
                {
                    PlaybackError?.Invoke(this, "Failed to initialize LibVLC");
                    return;
                }

                // Allocate frame buffer for LibVLC to write to
                var bufferSize = _videoWidth * _videoHeight * 4; // BGRA = 4 bytes per pixel
                lock (_bufferLock)
                {
                    _frameBuffer = Marshal.AllocHGlobal((int)bufferSize);
                    _bufferValid = true;
                }

                // Create shared bitmap on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _sharedFrame = new WriteableBitmap(
                        (int)_videoWidth,
                        (int)_videoHeight,
                        96, 96,
                        PixelFormats.Bgr32,
                        null);
                });

                // Create media player with memory rendering
                _mediaPlayer = new VlcMediaPlayer(_libVLC);
                _mediaPlayer.SetVideoCallbacks(LockCallback, null, DisplayCallback);
                _mediaPlayer.SetVideoFormat("RV32", _videoWidth, _videoHeight, _videoWidth * 4);

                // Set up event handlers
                _mediaPlayer.Playing += OnPlaying;
                _mediaPlayer.EndReached += OnEndReached;
                _mediaPlayer.EncounteredError += OnError;

                // Create fullscreen windows on all monitors
                Application.Current.Dispatcher.Invoke(CreateWindows);

                // Start render loop
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CompositionTarget.Rendering += OnCompositionTargetRendering;
                });

                // Play the video
                using var media = new Media(_libVLC, videoUrl, FromType.FromLocation);
                _mediaPlayer.Play(media);
                _isPlaying = true;

                App.Logger?.Information("DualMonitorVideo: Started playback of {Url} on {Count} monitors",
                    videoUrl, _windows.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "DualMonitorVideo: Failed to start playback");
                PlaybackError?.Invoke(this, ex.Message);
                Stop();
            }
        }

        /// <summary>
        /// Play a local video file on all monitors.
        /// </summary>
        public void PlayFile(string filePath, uint width = 1920, uint height = 1080)
        {
            if (!File.Exists(filePath))
            {
                PlaybackError?.Invoke(this, $"File not found: {filePath}");
                return;
            }

            Play(new Uri(filePath).AbsoluteUri, width, height);
        }

        /// <summary>
        /// Stop playback and clean up resources.
        /// </summary>
        public void Stop()
        {
            // CRITICAL: Invalidate buffer FIRST to stop render loop from using it
            _bufferValid = false;
            _isPlaying = false;
            _frameReady = false;

            // Unsubscribe from rendering event first to stop frame updates
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                CompositionTarget.Rendering -= OnCompositionTargetRendering;
            });

            // Stop the media player
            try
            {
                _mediaPlayer?.Stop();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DualMonitorVideo: Error stopping media player: {Error}", ex.Message);
            }

            // Wait a bit for LibVLC to fully stop rendering
            // Use message-pump-aware wait to prevent deadlock when called from UI thread
            WaitWithMessagePump(150);

            // Close all windows
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                foreach (var window in _windows.ToArray())
                {
                    try
                    {
                        window.Close();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("DualMonitorVideo: Error closing window: {Error}", ex.Message);
                    }
                }
                _windows.Clear();
            });

            // Clear shared frame reference
            _sharedFrame = null;

            // Clean up media player with delay to let any pending events complete
            var playerToDispose = _mediaPlayer;
            _mediaPlayer = null;

            if (playerToDispose != null)
            {
                playerToDispose.Playing -= OnPlaying;
                playerToDispose.EndReached -= OnEndReached;
                playerToDispose.EncounteredError -= OnError;

                // Dispose asynchronously after delay to avoid crashes from pending events
                Task.Run(async () =>
                {
                    await Task.Delay(500);
                    try
                    {
                        playerToDispose.Dispose();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("DualMonitorVideo: Error disposing media player: {Error}", ex.Message);
                    }
                });
            }

            // Free frame buffer with lock to prevent race with render callback
            // Use longer delay to ensure all pending render frames have completed
            IntPtr bufferToFree;
            lock (_bufferLock)
            {
                bufferToFree = _frameBuffer;
                _frameBuffer = IntPtr.Zero;
            }

            if (bufferToFree != IntPtr.Zero)
            {
                Task.Run(async () =>
                {
                    // Wait longer than render loop interval (16ms) plus safety margin
                    await Task.Delay(500);
                    try
                    {
                        Marshal.FreeHGlobal(bufferToFree);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("DualMonitorVideo: Error freeing frame buffer: {Error}", ex.Message);
                    }
                });
            }

            App.Logger?.Information("DualMonitorVideo: Playback stopped");
        }

        /// <summary>
        /// Waits for a specified number of milliseconds while continuing to pump WPF messages.
        /// This prevents deadlocks when LibVLC threads need to dispatch to the UI thread during cleanup.
        /// </summary>
        private static void WaitWithMessagePump(int milliseconds)
        {
            var endTime = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < endTime)
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(
                        DispatcherPriority.Background,
                        new Action(() => { }));
                }
                catch
                {
                    return;
                }
                Thread.Sleep(10);
            }
        }

        /// <summary>
        /// Set the volume (0-100).
        /// </summary>
        public void SetVolume(int volume)
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Volume = Math.Clamp(volume, 0, 100);
            }
        }

        /// <summary>
        /// Get or set mute state.
        /// </summary>
        public bool Mute
        {
            get => _mediaPlayer?.Mute ?? false;
            set
            {
                if (_mediaPlayer != null)
                    _mediaPlayer.Mute = value;
            }
        }

        private void InitializeLibVLC()
        {
            if (_libVLC != null) return;

            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var libvlcPath = Path.Combine(appDir, "libvlc");

                if (!Directory.Exists(libvlcPath))
                {
                    libvlcPath = Path.Combine(appDir, "libvlc", "win-x64");
                }

                if (Directory.Exists(libvlcPath))
                {
                    Core.Initialize(libvlcPath);
                }
                else
                {
                    Core.Initialize();
                }

                _libVLC = new LibVLC(
                    "--no-video-title-show",
                    "--no-osd",
                    "--aout=directsound",
                    "--verbose=-1"
                );

                App.Logger?.Information("DualMonitorVideo: LibVLC initialized (version {Version})", _libVLC.Version);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "DualMonitorVideo: Failed to initialize LibVLC");
                _libVLC = null;
            }
        }

        private void CreateWindows()
        {
            var screens = App.GetAllScreensCached();

            if (screens.Length == 0)
            {
                App.Logger?.Warning("DualMonitorVideo: No screens found");
                return;
            }

            App.Logger?.Information("DualMonitorVideo: Creating windows for {Count} screens: {Names}",
                screens.Length, string.Join(", ", screens.Select(s => s.DeviceName)));

            foreach (var screen in screens)
            {
                try
                {
                    var window = CreateFullscreenWindow(screen);
                    window.Show();
                    _windows.Add(window);

                    App.Logger?.Debug("DualMonitorVideo: Created window on {Screen} at {Bounds}",
                        screen.DeviceName, screen.Bounds);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "DualMonitorVideo: Failed to create window on {Screen}", screen.DeviceName);
                }
            }

            App.Logger?.Information("DualMonitorVideo: Successfully created {Count} windows", _windows.Count);
        }

        private Window CreateFullscreenWindow(Screen screen)
        {
            // Image control that displays the shared bitmap
            var image = new Image
            {
                Source = _sharedFrame,  // All windows share the SAME bitmap!
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Container grid with black background
            var grid = new Grid
            {
                Background = Brushes.Black,
                Children = { image }
            };

            var window = new Window
            {
                Title = "DualMonitorVideo",
                WindowStyle = WindowStyle.None,
                WindowState = WindowState.Normal,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                ShowInTaskbar = false,
                Background = Brushes.Black,
                Content = grid,
                Left = screen.Bounds.Left,
                Top = screen.Bounds.Top,
                Width = screen.Bounds.Width,
                Height = screen.Bounds.Height
            };

            // Handle keyboard input for escape to close
            window.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Stop();
                }
            };

            // Position window using Win32 after it's loaded
            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                SetWindowPos(hwnd, HWND_TOPMOST,
                    screen.Bounds.Left, screen.Bounds.Top,
                    screen.Bounds.Width, screen.Bounds.Height,
                    SWP_NOACTIVATE);
            };

            return window;
        }

        #region LibVLC Callbacks

        /// <summary>
        /// LibVLC lock callback - called when LibVLC wants to write a frame.
        /// Returns pointer to our frame buffer.
        /// </summary>
        private IntPtr LockCallback(IntPtr opaque, IntPtr planes)
        {
            // Tell LibVLC where to write the frame data
            Marshal.WriteIntPtr(planes, _frameBuffer);
            return IntPtr.Zero;
        }

        /// <summary>
        /// LibVLC display callback - called when a frame is ready to display.
        /// Sets flag for the render loop to pick up.
        /// </summary>
        private void DisplayCallback(IntPtr opaque, IntPtr picture)
        {
            _frameReady = true;
        }

        #endregion

        #region Render Loop

        /// <summary>
        /// WPF composition target rendering callback.
        /// Copies the frame from LibVLC buffer to the shared WriteableBitmap.
        /// Both windows automatically update since they share the same bitmap source.
        /// </summary>
        private void OnCompositionTargetRendering(object? sender, EventArgs e)
        {
            // Quick check without lock - if buffer is invalid, skip immediately
            if (!_bufferValid || !_frameReady)
                return;

            // Capture shared frame reference (immutable once set, cleared to null on stop)
            var frame = _sharedFrame;
            if (frame == null)
                return;

            _frameReady = false;

            try
            {
                // Lock to safely access buffer pointer
                lock (_bufferLock)
                {
                    // Double-check validity inside lock
                    if (!_bufferValid || _frameBuffer == IntPtr.Zero)
                        return;

                    frame.Lock();

                    // Copy frame data from LibVLC buffer to WriteableBitmap
                    var bufferSize = _videoWidth * _videoHeight * 4;
                    CopyMemory(frame.BackBuffer, _frameBuffer, bufferSize);

                    // Mark entire bitmap as dirty so WPF redraws it
                    frame.AddDirtyRect(new Int32Rect(0, 0, (int)_videoWidth, (int)_videoHeight));
                    frame.Unlock();
                }

                // Both windows automatically update because they reference the same bitmap!
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DualMonitorVideo: Frame copy error: {Error}", ex.Message);
            }
        }

        #endregion

        #region Media Player Events

        private void OnPlaying(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                PlaybackStarted?.Invoke(this, EventArgs.Empty);
            });
        }

        private void OnEndReached(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
                Stop();
            });
        }

        private void OnError(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                PlaybackError?.Invoke(this, "LibVLC encountered an error during playback");
                Stop();
            });
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();

            // CRITICAL: Wait for async player disposal (500ms in Stop) to complete
            // before disposing LibVLC, as the player needs LibVLC to be alive during disposal
            Thread.Sleep(600);

            try
            {
                _libVLC?.Dispose();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DualMonitorVideo: Error disposing LibVLC: {Error}", ex.Message);
            }
            _libVLC = null;

            App.Logger?.Information("DualMonitorVideoService disposed");
        }
    }
}
