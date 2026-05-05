using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;
using VlcFromType = LibVLCSharp.Shared.FromType;
using VlcMedia = LibVLCSharp.Shared.Media;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Plays translucent, click-through ambient videos above the desktop. Side videos
/// are intentionally separate from mandatory videos: they do not lock input,
/// start attention checks, or use the interaction queue.
/// </summary>
public sealed class SideVideoService : IDisposable
{
    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".avi", ".mov", ".mkv", ".wmv" };
    private static readonly string[] LocationOptions = { "top_left", "top_right", "bottom_left", "bottom_right", "center" };

    private readonly Random _random = new();
    private readonly List<Window> _windows = new();
    private readonly List<MediaElement> _mediaElements = new();
    private readonly List<VlcMediaPlayer> _audioPlayers = new();
    private readonly List<VlcMedia> _audioMedia = new();

    private DispatcherTimer? _fallbackCloseTimer;
    private bool _isPlaying;
    private bool _disposed;

    public bool IsPlaying => _isPlaying;

    public void Stop()
    {
        CloseCurrent();
        App.Logger?.Information("SideVideoService stopped");
    }

    public void TriggerSideVideo()
    {
        DispatcherHelper.RunOnUISync(() =>
        {
            try
            {
                var path = PickVideoPath();
                if (string.IsNullOrEmpty(path))
                {
                    var videosPath = GetVideosPath();
                    MessageBox.Show(
                        $"No videos found.\n\nDrop videos into:\n{videosPath}",
                        "Side Video",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                PlayVideo(path);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "SideVideoService: failed to trigger side video");
            }
        });
    }

    private string? PickVideoPath()
    {
        var files = EnumerateVideos(GetVideosPath()).ToList();
        if (files.Count == 0) return null;
        return files[_random.Next(files.Count)];
    }

    private static string GetVideosPath()
    {
        return Path.Combine(App.EffectiveAssetsPath, "videos");
    }

    private static IEnumerable<string> EnumerateVideos(string folder)
    {
        if (!Directory.Exists(folder)) yield break;

        foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
        {
            if (VideoExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                yield return file;
        }
    }

    private void PlayVideo(string path)
    {
        CloseCurrent();

        var settings = App.Settings.Current;
        var mode = settings.SideVideoMode;
        var screens = mode == "background" && settings.DualMonitorEnabled
            ? App.GetAllScreensCached()
            : new[] { Screen.PrimaryScreen! };

        foreach (var screen in screens)
        {
            var instance = CreateVideoWindow(screen, path, mode);
            if (instance.Window != null)
            {
                _windows.Add(instance.Window);
                if (instance.Media != null)
                    _mediaElements.Add(instance.Media);
            }
        }

        if (_windows.Count == 0) return;

        _isPlaying = true;
        StartAudio(path);
        _fallbackCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        _fallbackCloseTimer.Tick += (_, _) => CloseCurrent();
        _fallbackCloseTimer.Start();

        App.Logger?.Information("SideVideoService: playing overlay for {File} mode={Mode} windows={Count}",
            Path.GetFileName(path), mode, _windows.Count);
    }

    private (Window? Window, MediaElement? Media) CreateVideoWindow(Screen screen, string path, string mode)
    {
        try
        {
            var location = ResolveLocation(App.Settings.Current.SideVideoLocation);
            var bounds = mode == "background"
                ? GetFullScreenBounds(screen)
                : GetLocationBounds(screen, location);
            Window? window = null;

            var media = new MediaElement
            {
                Source = new Uri(path),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.Uniform,
                // Audio is handled by LibVLC below. MediaElement audio is unreliable
                // inside transparent layered overlay windows on some systems.
                IsMuted = true,
                Volume = 0,
                Opacity = (App.Settings.Current.SideVideoOpacity / 100.0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = mode == "background" ? Visibility.Visible : Visibility.Hidden
            };

            media.MediaOpened += (_, _) =>
            {
                if (window == null) return;

                if (mode == "background")
                {
                    media.Visibility = Visibility.Visible;
                    return;
                }

                if (media.NaturalVideoWidth <= 0 || media.NaturalVideoHeight <= 0)
                {
                    media.Visibility = Visibility.Visible;
                    return;
                }

                var resizedBounds = GetNativeLocationBounds(screen, media.NaturalVideoWidth, media.NaturalVideoHeight, location);
                window.Left = resizedBounds.Left;
                window.Top = resizedBounds.Top;
                window.Width = resizedBounds.Width;
                window.Height = resizedBounds.Height;
                media.Visibility = Visibility.Visible;

                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    SetWindowPos(hwnd, HWND_TOPMOST, (int)resizedBounds.Left, (int)resizedBounds.Top,
                        (int)resizedBounds.Width, (int)resizedBounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }
            };
            media.MediaEnded += (_, _) => CloseCurrent();
            media.MediaFailed += (_, e) =>
            {
                App.Logger?.Warning("SideVideoService: media failed: {Error}", e.ErrorException?.Message);
                CloseCurrent();
            };

            var content = new Grid
            {
                ClipToBounds = true,
                Background = Brushes.Transparent
            };

            if (mode == "background")
            {
                content.Children.Add(new Border
                {
                    Background = Brushes.Black,
                    Opacity = App.Settings.Current.SideVideoOpacity / 100.0,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                });
            }

            content.Children.Add(media);

            window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Content = content
            };

            window.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                SetWindowPos(hwnd, HWND_TOPMOST, (int)bounds.Left, (int)bounds.Top, (int)bounds.Width, (int)bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            };

            window.Show();
            media.Play();
            return (window, media);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "SideVideoService: failed to create side video window");
            return (null, null);
        }
    }

    private Rect GetFullScreenBounds(Screen screen)
    {
        return new Rect(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height);
    }

    private static double GetEffectiveVolume()
    {
        var settings = App.Settings?.Current;
        var master = settings?.MasterVolume ?? 100;
        var video = settings?.VideoVolume ?? 100;
        return Math.Clamp((master / 100.0) * (video / 100.0), 0.0, 1.0);
    }

    private void StartAudio(string path)
    {
        try
        {
            if (!VideoService.WaitForLibVLC())
            {
                App.Logger?.Warning("SideVideoService: LibVLC unavailable; side video audio skipped");
                return;
            }

            var libvlc = VideoService.SharedLibVLC;
            if (libvlc == null) return;

            var media = new VlcMedia(libvlc, path, VlcFromType.FromPath);
            // This player is only for audio. Without disabling video output, LibVLC
            // may create its own native video window for files that contain video.
            media.AddOption(":no-video");
            var player = new VlcMediaPlayer(libvlc);
            App.Audio?.ApplyPreferredDevice(player);

            player.EndReached += (_, _) =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(CloseCurrent));
            };
            player.EncounteredError += (_, _) =>
            {
                App.Logger?.Warning("SideVideoService: LibVLC audio playback error");
            };

            _audioMedia.Add(media);
            _audioPlayers.Add(player);
            var started = player.Play(media);

            // Configure audio after Play(); LibVLC can ignore pre-playback settings.
            player.Mute = false;
            player.Volume = (int)Math.Round(GetEffectiveVolume() * 100.0);

            App.Logger?.Information("SideVideoService: audio started={Started} volume={Volume} mute={Mute} file={File}",
                started, player.Volume, player.Mute, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "SideVideoService: failed to start audio");
        }
    }

    private string ResolveLocation(string location)
    {
        return location == "random"
            ? LocationOptions[_random.Next(LocationOptions.Length)]
            : location;
    }

    private Rect GetLocationBounds(Screen screen, string location)
    {
        var area = screen.WorkingArea;
        const double placeholderSizeRatio = 0.35;
        var width = Math.Max(240, area.Width * placeholderSizeRatio);
        var height = width * 9.0 / 16.0;
        if (height > area.Height * 0.9)
        {
            height = area.Height * 0.9;
            width = height * 16.0 / 9.0;
        }

        return PlaceLocationBounds(area, width, height, location);
    }

    private static Rect GetNativeLocationBounds(Screen screen, int nativeWidth, int nativeHeight, string location)
    {
        var area = screen.WorkingArea;
        return PlaceLocationBounds(area, nativeWidth, nativeHeight, location);
    }

    private static Rect PlaceLocationBounds(System.Drawing.Rectangle area, double width, double height, string location)
    {
        const double margin = 24;
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var maxWidth = Math.Max(1, area.Width - margin * 2);
        var maxHeight = Math.Max(1, area.Height - margin * 2);
        var scale = Math.Min(1.0, Math.Min(maxWidth / width, maxHeight / height));
        width *= scale;
        height *= scale;

        var left = location switch
        {
            "top_right" or "bottom_right" => area.Right - width - margin,
            "center" => area.Left + (area.Width - width) / 2.0,
            _ => area.Left + margin
        };
        var top = location switch
        {
            "bottom_left" or "bottom_right" => area.Bottom - height - margin,
            "center" => area.Top + (area.Height - height) / 2.0,
            _ => area.Top + margin
        };

        left = Math.Clamp(left, area.Left + margin, area.Right - width - margin);
        top = Math.Clamp(top, area.Top + margin, area.Bottom - height - margin);

        return new Rect(left, top, width, height);
    }

    private void CloseCurrent()
    {
        _fallbackCloseTimer?.Stop();
        _fallbackCloseTimer = null;

        foreach (var media in _mediaElements.ToList())
        {
            try { media.Stop(); media.Close(); }
            catch (Exception ex) { App.Logger?.Debug("SideVideoService: media close failed: {Error}", ex.Message); }
        }
        _mediaElements.Clear();

        foreach (var player in _audioPlayers.ToList())
        {
            try { player.Stop(); player.Dispose(); }
            catch (Exception ex) { App.Logger?.Debug("SideVideoService: audio player close failed: {Error}", ex.Message); }
        }
        _audioPlayers.Clear();

        foreach (var media in _audioMedia.ToList())
        {
            try { media.Dispose(); }
            catch (Exception ex) { App.Logger?.Debug("SideVideoService: audio media close failed: {Error}", ex.Message); }
        }
        _audioMedia.Clear();

        foreach (var window in _windows.ToList())
        {
            try { window.Close(); }
            catch (Exception ex) { App.Logger?.Debug("SideVideoService: window close failed: {Error}", ex.Message); }
        }
        _windows.Clear();
        _isPlaying = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
