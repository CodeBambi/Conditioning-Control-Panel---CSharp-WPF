using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Views;

public partial class VideoSpikeWindow : Window
{
    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private Media? _currentMedia;

    public VideoSpikeWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            _libVlc = new LibVLC();
            _player = new MediaPlayer(_libVlc);
            VideoView.MediaPlayer = _player;
            StatusText.Text = "LibVLC initialized. Press Play.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Init failed: {ex.Message}";
        }
    }

    private void PlayButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_player == null || _libVlc == null) return;

        var videoPath = FindSampleVideo();
        if (string.IsNullOrEmpty(videoPath))
        {
            StatusText.Text = "No sample video found.";
            return;
        }

        StopInternal();
        _currentMedia = new Media(_libVlc, videoPath);
        _player.Play(_currentMedia);
        StatusText.Text = $"Playing: {Path.GetFileName(videoPath)}";
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        StopInternal();
        StatusText.Text = "Stopped.";
    }

    private void StopInternal()
    {
        _player?.Stop();
        _currentMedia?.Dispose();
        _currentMedia = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        // Debug-only spike harness, but still release the private LibVLC instance so
        // repeated opens don't leak native resources (WS0 lot 4 R1-11). Deferred
        // background dispose per the port's LibVLC teardown convention.
        var player = _player;
        var media = _currentMedia;
        var libVlc = _libVlc;
        _player = null;
        _currentMedia = null;
        _libVlc = null;
        if (VideoView != null)
        {
            VideoView.MediaPlayer = null;
        }
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try { player?.Stop(); } catch { }
            await System.Threading.Tasks.Task.Delay(400).ConfigureAwait(false);
            try { player?.Dispose(); } catch { }
            try { media?.Dispose(); } catch { }
            try { libVlc?.Dispose(); } catch { }
        });
        base.OnClosed(e);
    }

    private static string? FindSampleVideo()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "_test_loop.mp4"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Resources", "tutorial_videos", "_test_loop.mp4"),
            Path.Combine(AppContext.BaseDirectory, "Resources", "tutorial_videos", "_test_loop.mp4"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
