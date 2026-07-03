using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Models;
using LibVLCSharp.Shared;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace ConditioningControlPanel.Avalonia.Services.Video;

/// <summary>
/// Avalonia port of the legacy WPF <c>DualMonitorVideoService</c>.
/// Plays a single decoded video stream across all connected monitors using LibVLC
/// memory rendering (RV32) into per-window Avalonia <see cref="WriteableBitmap"/> instances.
/// </summary>
public sealed class AvaloniaMultiMonitorVideoService : IMultiMonitorVideoService, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly IScreenProvider _screenProvider;
    private readonly ISettingsService? _settings;
    private readonly ILogger<AvaloniaMultiMonitorVideoService> _logger;

    private VlcMediaPlayer? _mediaPlayer;
    private IntPtr _frameBuffer = IntPtr.Zero;
    private uint _videoWidth;
    private uint _videoHeight;
    private readonly List<(Window Window, WriteableBitmap Bitmap, Image ImageControl)> _windowData = new();
    private readonly object _bufferLock = new();
    private volatile bool _frameReady;
    private volatile bool _bufferValid;
    private bool _isPlaying;
    private string? _outputDeviceId;
    private bool _disposed;
    // Key contract for the current playback (WPF SetupStrictHandlers parity); reset on Stop().
    private bool _strictMode;
    // Last known playback position for watch credit (read at teardown, after Stop()).
    private long _lastPlaybackTimeMs = -1;
    // Cached copy of _windowData for the render loop — refreshed on window-list changes so
    // OnRenderTick never allocates per tick (WS0 lot 4 fix R1-12).
    private (Window Window, WriteableBitmap Bitmap, Image ImageControl)[] _windowSnapshot =
        Array.Empty<(Window, WriteableBitmap, Image)>();
    private readonly DispatcherTimer _renderTimer;

    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? PlaybackError;

    /// <summary>
    /// Raised when the user presses ESC on a NON-strict video: dismiss with the normal
    /// cleanup path so the session keeps running (WPF SetupStrictHandlers contract,
    /// VideoService.cs:1822-1835).
    /// </summary>
    public event EventHandler? DismissRequested;

    /// <summary>
    /// Raised when the user presses the configured panic key on a NON-strict video:
    /// force-end playback (WPF ForceCleanup contract).
    /// </summary>
    public event EventHandler? PanicRequested;

    /// <summary>Raised when the user clicks any video surface (raises attention targets).</summary>
    public event EventHandler? SurfaceClicked;

    public bool IsPlaying => _isPlaying;

    /// <summary>
    /// Best-effort playback position in ms for watch credit. Consuming resets the captured
    /// value so a later teardown pass can't double-count the same playback.
    /// </summary>
    internal long ConsumePlaybackTimeMs()
    {
        long live = -1;
        try { live = _mediaPlayer?.Time ?? -1; }
        catch { /* player may already be disposed */ }
        var result = Math.Max(live, _lastPlaybackTimeMs);
        _lastPlaybackTimeMs = -1;
        return result;
    }

    /// <summary>
    /// Sets the key contract for the CURRENT playback: strict videos block the panic key,
    /// system keys and Alt+F4; non-strict videos surface ESC/panic via
    /// <see cref="DismissRequested"/>/<see cref="PanicRequested"/>. Reset on <see cref="Stop"/>.
    /// </summary>
    internal void SetStrictMode(bool strict) => _strictMode = strict;

    public AvaloniaMultiMonitorVideoService(
        LibVLC libVlc,
        IScreenProvider screenProvider,
        ILogger<AvaloniaMultiMonitorVideoService> logger,
        ISettingsService? settings = null)
    {
        _libVlc = libVlc;
        _screenProvider = screenProvider;
        _logger = logger;
        _settings = settings;

        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += OnRenderTick;
    }

    /// <summary>
    /// Play a video URL on all monitors simultaneously.
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

            var bufferSize = _videoWidth * _videoHeight * 4; // BGRA = 4 bytes per pixel
            lock (_bufferLock)
            {
                _frameBuffer = Marshal.AllocHGlobal((int)bufferSize);
                _bufferValid = true;
            }

            _lastPlaybackTimeMs = -1;
            _mediaPlayer = new VlcMediaPlayer(_libVlc);
            _mediaPlayer.Mute = false;
            _mediaPlayer.Volume = 100;
            // WPF parity (VideoService.cs:1337): the user setting is an escape hatch for the
            // rare systems with broken hardware decoders; default stays hardware-on.
            _mediaPlayer.EnableHardwareDecoding = _settings?.Current?.VideoHardwareDecoding ?? true;
            _mediaPlayer.SetVideoCallbacks(LockCallback, null, DisplayCallback);
            _mediaPlayer.SetVideoFormat("RV32", _videoWidth, _videoHeight, _videoWidth * 4);

            _mediaPlayer.Playing += OnPlaying;
            _mediaPlayer.EndReached += OnEndReached;
            _mediaPlayer.EncounteredError += OnError;
            _mediaPlayer.TimeChanged += OnTimeChanged;

            Dispatcher.UIThread.Invoke(CreateWindows);
            Dispatcher.UIThread.Invoke(() => _renderTimer.Start());

            using var media = new Media(_libVlc, videoUrl, FromType.FromLocation);
            _mediaPlayer.Play(media);
            _isPlaying = true;

            _logger.LogInformation("AvaloniaMultiMonitorVideo: Started playback of {Url} on {Count} monitors",
                videoUrl, _windowData.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AvaloniaMultiMonitorVideo: Failed to start playback");
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
    /// Explicit interface implementation for <see cref="IMultiMonitorVideoService.PlayUrl(string)"/>.
    /// </summary>
    void IMultiMonitorVideoService.PlayUrl(string url) => Play(url);

    /// <summary>
    /// Explicit interface implementation for <see cref="IMultiMonitorVideoService.PlayFile(string)"/>.
    /// </summary>
    void IMultiMonitorVideoService.PlayFile(string filePath) => PlayFile(filePath);

    /// <summary>
    /// Stop playback and clean up resources.
    /// </summary>
    public void Stop()
    {
        // Idempotent: every teardown path (dismiss, panic, natural end, service cleanup)
        // may call Stop; do nothing when there is nothing to stop (WS0 lot 4 fix R1-4).
        if (!_isPlaying && _mediaPlayer == null && _windowData.Count == 0)
            return;

        _strictMode = false;

        // CRITICAL: Invalidate buffer FIRST to stop render loop from using it
        _bufferValid = false;
        _isPlaying = false;
        _frameReady = false;

        Dispatcher.UIThread.Invoke(() => _renderTimer.Stop());

        var playerToDispose = _mediaPlayer;
        _mediaPlayer = null;

        if (playerToDispose != null)
        {
            // Capture the final position for watch credit before the player clock dies.
            try
            {
                var time = playerToDispose.Time;
                if (time > _lastPlaybackTimeMs) _lastPlaybackTimeMs = time;
            }
            catch { /* best effort */ }

            playerToDispose.Playing -= OnPlaying;
            playerToDispose.EndReached -= OnEndReached;
            playerToDispose.EncounteredError -= OnError;
            playerToDispose.TimeChanged -= OnTimeChanged;

            try
            {
                playerToDispose.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogInformation("AvaloniaMultiMonitorVideo: Error stopping media player: {Error}", ex.Message);
            }
        }

        Dispatcher.UIThread.Invoke(() =>
        {
            foreach (var (window, _, _) in _windowData.ToArray())
            {
                try
                {
                    window.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("AvaloniaMultiMonitorVideo: Error closing window: {Error}", ex.Message);
                }
            }
            _windowData.Clear();
            _windowSnapshot = Array.Empty<(Window, WriteableBitmap, Image)>();
        });

        if (playerToDispose != null)
        {
            // Deferred background dispose: LibVLC teardown can block while its internal
            // threads unwind, so never dispose synchronously on the UI thread. This replaces
            // the old reentrant WaitWithMessagePump (WS0 lot 4 fix R1-4).
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                try
                {
                    playerToDispose.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("AvaloniaMultiMonitorVideo: Error disposing media player: {Error}", ex.Message);
                }
            });
        }

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
                await Task.Delay(500);
                try
                {
                    Marshal.FreeHGlobal(bufferToFree);
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("AvaloniaMultiMonitorVideo: Error freeing frame buffer: {Error}", ex.Message);
                }
            });
        }

        _logger.LogInformation("AvaloniaMultiMonitorVideo: Playback stopped");
    }

    /// <summary>
    /// Set the volume (0-100).
    /// </summary>
    public void SetVolume(int volume)
    {
        var player = _mediaPlayer;
        if (player != null)
        {
            try { player.Volume = Math.Clamp(volume, 0, 100); }
            catch (ObjectDisposedException) { }
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
            var player = _mediaPlayer;
            if (player != null)
            {
                try { player.Mute = value; }
                catch (ObjectDisposedException) { }
            }
        }
    }

    /// <summary>
    /// Set the audio output device.
    /// </summary>
    public void SetAudioOutputDevice(string? deviceId)
    {
        _outputDeviceId = deviceId;

        var player = _mediaPlayer;
        if (player == null || string.IsNullOrEmpty(deviceId))
            return;

        ApplyAudioOutputDevice(player, deviceId);
    }

    private void ApplyAudioOutputDevice(VlcMediaPlayer player, string deviceId)
    {
        try
        {
            bool found = false;
            try
            {
                var available = player.AudioOutputDeviceEnum;
                if (available != null)
                {
                    foreach (var d in available)
                    {
                        if (string.Equals(d.DeviceIdentifier, deviceId, StringComparison.Ordinal))
                        {
                            found = true;
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Enumeration may fail while no stream is active; allow the attempt anyway.
                found = true;
            }

            if (found)
            {
                player.SetOutputDevice(deviceId);
                _logger.LogInformation("Set multi-monitor video audio output device to {DeviceId}", deviceId);
            }
            else
            {
                _logger.LogWarning("Saved audio output device {DeviceId} is not present in LibVLC outputs; using system default", deviceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SetAudioOutputDevice failed");
        }
    }

    private void CreateWindows()
    {
        var screens = _screenProvider.GetAllScreens();

        if (screens.Count == 0)
        {
            _logger.LogWarning("AvaloniaMultiMonitorVideo: No screens found");
            return;
        }

        var primary = _screenProvider.GetPrimaryScreen() ?? screens[0];
        var fillSecondaries = ShouldFillSecondaryMonitors(_settings?.Current, screens.Count);

        _logger.LogInformation("AvaloniaMultiMonitorVideo: Creating windows for {Count} screens (fillSecondaries={Fill}): {Names}",
            screens.Count, fillSecondaries, string.Join(", ", screens.Select(s => s.Name)));

        foreach (var screen in screens)
        {
            var isPrimary = ReferenceEquals(screen, primary) ||
                string.Equals(screen.Name, primary.Name, StringComparison.Ordinal);
            if (!isPrimary && !fillSecondaries) continue;
            try
            {
                var bitmap = new WriteableBitmap(
                    new PixelSize((int)_videoWidth, (int)_videoHeight),
                    new Vector(96, 96),
                    global::Avalonia.Platform.PixelFormat.Bgra8888,
                    global::Avalonia.Platform.AlphaFormat.Unpremul);

                var (window, imageControl) = CreateFullscreenWindow(screen, bitmap, isPrimary);
                window.Show();
                _windowData.Add((window, bitmap, imageControl));

                _logger.LogInformation("AvaloniaMultiMonitorVideo: Created window on {Screen} at {Bounds}",
                    screen.Name, screen.Bounds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AvaloniaMultiMonitorVideo: Failed to create window on {Screen}", screen.Name);
            }
        }

        _windowSnapshot = _windowData.ToArray();
        _logger.LogInformation("AvaloniaMultiMonitorVideo: Successfully created {Count} windows", _windowData.Count);
    }

    /// <summary>
    /// WPF ShouldFillSecondaryMonitors parity (VideoService.cs:933-938): the primary always
    /// fills; secondaries only when DualMonitorEnabled; and on 3+ monitors only when the user
    /// opts in via FillAllMonitorsWithVideo (avoids per-monitor render targets lagging, #389).
    /// Without injected settings the legacy fill-all behavior is preserved.
    /// </summary>
    private static bool ShouldFillSecondaryMonitors(AppSettings? settings, int screenCount)
    {
        if (settings == null) return true;
        if (!settings.DualMonitorEnabled) return false;
        if (screenCount <= 2) return true;
        return settings.FillAllMonitorsWithVideo;
    }

    private (Window Window, Image ImageControl) CreateFullscreenWindow(ScreenInfo screen, WriteableBitmap bitmap, bool isPrimary)
    {
        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var grid = new Grid
        {
            Background = Brushes.Black,
            Children = { image }
        };

        var window = new Window
        {
            Title = "MultiMonitorVideo",
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            Topmost = true,
            ShowInTaskbar = false,
            // WPF parity (VideoService.cs:1317-1320): only the audio-bearing primary window
            // activates so it receives ESC/panic keys; secondaries never steal focus.
            ShowActivated = isPrimary,
            Background = Brushes.Black,
            Content = grid
        };
        window.ConstrainToScreen(screen);

        if (isPrimary)
        {
            window.KeyDown += OnPrimaryKeyDown;
        }

        // A click on any video surface raises the attention targets back above the video
        // windows instead of burying them (WPF click-overlay contract, VideoService.cs:1490-1512).
        window.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            SurfaceClicked?.Invoke(this, EventArgs.Empty);
        };

        return (window, image);
    }

    /// <summary>
    /// WPF SetupStrictHandlers parity (VideoService.cs:1789-1835). Non-strict: ESC dismisses
    /// via <see cref="DismissRequested"/> (normal cleanup; the session keeps running) and the
    /// user's panic key force-stops via <see cref="PanicRequested"/>. Strict: the panic key,
    /// system keys and Alt+F4 are blocked.
    /// </summary>
    private void OnPrimaryKeyDown(object? sender, KeyEventArgs e)
    {
        var settings = _settings?.Current;
        var isPanicKey = settings?.PanicKeyEnabled == true &&
            string.Equals(e.Key.ToString(), settings.PanicKey, StringComparison.OrdinalIgnoreCase);

        if (_strictMode)
        {
            if (e.Key == Key.Escape || e.Key == Key.System || isPanicKey ||
                (e.Key == Key.F4 && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) ||
                (e.Key == Key.Tab && (e.KeyModifiers == KeyModifiers.Alt || e.KeyModifiers == KeyModifiers.Control)) ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (DismissRequested != null) DismissRequested.Invoke(this, EventArgs.Empty);
            else Stop();
            return;
        }

        if (isPanicKey)
        {
            e.Handled = true;
            if (PanicRequested != null) PanicRequested.Invoke(this, EventArgs.Empty);
            else Stop();
        }
    }

    #region LibVLC Callbacks

    /// <summary>
    /// LibVLC lock callback - called when LibVLC wants to write a frame.
    /// Returns pointer to our frame buffer.
    /// </summary>
    private IntPtr LockCallback(IntPtr opaque, IntPtr planes)
    {
        lock (_bufferLock)
        {
            if (!_bufferValid || _frameBuffer == IntPtr.Zero)
            {
                Marshal.WriteIntPtr(planes, IntPtr.Zero);
                return IntPtr.Zero;
            }

            Marshal.WriteIntPtr(planes, _frameBuffer);
            return IntPtr.Zero;
        }
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
    /// Avalonia dispatcher timer callback.
    /// Copies the frame from LibVLC buffer to each window's WriteableBitmap.
    /// </summary>
    private unsafe void OnRenderTick(object? sender, EventArgs e)
    {
        if (!_bufferValid || !_frameReady)
            return;

        // Cached snapshot (refreshed on window-list changes) — no per-tick allocation.
        var windows = _windowSnapshot;
        if (windows.Length == 0)
            return;

        _frameReady = false;

        bool lockAcquired = false;
        try
        {
            lockAcquired = Monitor.TryEnter(_bufferLock, 16); // ~1 frame at 60fps
            if (!lockAcquired)
            {
                return;
            }

            if (!_bufferValid || _frameBuffer == IntPtr.Zero)
                return;

            foreach (var (_, bitmap, imageControl) in windows)
            {
                try
                {
                    using var framebuffer = bitmap.Lock();
                    var src = _frameBuffer.ToPointer();
                    var dst = framebuffer.Address.ToPointer();
                    int rowBytes = framebuffer.RowBytes;
                    int copyBytes = (int)Math.Min(_videoWidth * 4, rowBytes) * (int)_videoHeight;
                    Buffer.MemoryCopy(src, dst, copyBytes, copyBytes);
                    imageControl.InvalidateVisual();
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("AvaloniaMultiMonitorVideo: Frame copy error for one window: {Error}", ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation("AvaloniaMultiMonitorVideo: Frame copy error: {Error}", ex.Message);
        }
        finally
        {
            if (lockAcquired)
            {
                Monitor.Exit(_bufferLock);
            }
        }
    }

    #endregion

    #region Media Player Events

    private void OnPlaying(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Re-apply the requested audio output device once playback is active;
            // LibVLC output-device enumeration is only reliable after the audio stream starts.
            if (_mediaPlayer != null && !string.IsNullOrEmpty(_outputDeviceId))
            {
                ApplyAudioOutputDevice(_mediaPlayer, _outputDeviceId);
            }

            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        // Track the last known position for watch credit; read at teardown, after Stop()
        // has already reset the player clock.
        _lastPlaybackTimeMs = e.Time;
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
            Stop();
        });
    }

    private void OnError(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
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

        // Player disposal is deferred to a background task by Stop(); never block the UI
        // thread waiting on LibVLC teardown (WS0 lot 4 fix R1-6).
        _renderTimer.Tick -= OnRenderTick;

        _logger.LogInformation("AvaloniaMultiMonitorVideoService disposed");
    }
}
