using System;
using System.Windows;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// IPlaybackTimeSource that wraps the Deeper editor's local LibVLC
    /// MediaPlayer (used for the in-editor video preview). Emits
    /// PlaybackTimeChanged on the UI thread.
    ///
    /// For audio enhancements the editor uses NAudio rather than LibVLC; the
    /// editor passes a <paramref name="audioTickFn"/> callback that returns
    /// the current NAudio playback time. When the MediaPlayer is null we fall
    /// back to that callback. GetVideoRect comes from a UI element resolver
    /// (the editor's VideoPreview FrameworkElement) so the engine can map
    /// gaze rects to screen DIPs.
    /// </summary>
    public sealed class EditorPreviewTimeSource : IPlaybackTimeSource, IDisposable
    {
        private readonly MediaPlayer? _player;
        private readonly Func<double>? _audioTimeFn;
        private readonly Func<double>? _audioDurationFn;
        private readonly Func<bool>? _audioIsPlayingFn;
        private readonly Action<double>? _audioSeekFn;
        private readonly Action? _audioPauseFn;
        private readonly Action? _audioPlayFn;
        private readonly Func<Rect>? _videoRectFn;

        private readonly System.Windows.Threading.Dispatcher _dispatcher;
        private bool _attached;

        public event Action<double>? PlaybackTimeChanged;

        public EditorPreviewTimeSource(
            MediaPlayer? videoPlayer,
            Func<double>? audioTimeFn,
            Func<double>? audioDurationFn,
            Func<bool>? audioIsPlayingFn,
            Action<double>? audioSeekFn,
            Action? audioPauseFn,
            Action? audioPlayFn,
            Func<Rect>? videoRectFn,
            System.Windows.Threading.Dispatcher dispatcher)
        {
            _player = videoPlayer;
            _audioTimeFn = audioTimeFn;
            _audioDurationFn = audioDurationFn;
            _audioIsPlayingFn = audioIsPlayingFn;
            _audioSeekFn = audioSeekFn;
            _audioPauseFn = audioPauseFn;
            _audioPlayFn = audioPlayFn;
            _videoRectFn = videoRectFn;
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public void Attach()
        {
            if (_attached) return;
            if (_player != null) _player.TimeChanged += OnVlcTime;
            _attached = true;

            // Audio path doesn't have a TimeChanged event; the editor pumps a
            // tick on its own DispatcherTimer when in audio mode. The engine's
            // first tick from Compile() seeds the cursor either way, so audio
            // preview still works as long as the editor calls EmitTick.
        }

        public void Detach()
        {
            if (!_attached) return;
            if (_player != null) _player.TimeChanged -= OnVlcTime;
            _attached = false;
        }

        /// <summary>
        /// Manually push a tick. Used by the editor's audio playback path,
        /// which doesn't fire its own TimeChanged events.
        /// </summary>
        public void EmitTick(double seconds)
        {
            if (!_attached) return;
            try { PlaybackTimeChanged?.Invoke(seconds); }
            catch (Exception ex) { App.Logger?.Debug("EditorPreviewTimeSource.EmitTick error: {Error}", ex.Message); }
        }

        private void OnVlcTime(object? sender, MediaPlayerTimeChangedEventArgs e)
        {
            if (!_attached) return;
            var t = e.Time / 1000.0;
            try
            {
                if (_dispatcher.CheckAccess()) PlaybackTimeChanged?.Invoke(t);
                else _dispatcher.BeginInvoke(() => PlaybackTimeChanged?.Invoke(t));
            }
            catch (Exception ex) { App.Logger?.Debug("EditorPreviewTimeSource VLC tick error: {Error}", ex.Message); }
        }

        public double GetCurrentTimeSeconds()
        {
            try
            {
                if (_player != null) return Math.Max(0, _player.Time) / 1000.0;
                return _audioTimeFn?.Invoke() ?? 0;
            }
            catch { return 0; }
        }

        public double GetDurationSeconds()
        {
            try
            {
                if (_player != null) return Math.Max(0, _player.Length) / 1000.0;
                return _audioDurationFn?.Invoke() ?? 0;
            }
            catch { return 0; }
        }

        public bool IsPlaying
        {
            get
            {
                try
                {
                    if (_player != null) return _player.IsPlaying;
                    return _audioIsPlayingFn?.Invoke() ?? false;
                }
                catch { return false; }
            }
        }

        public void Seek(double seconds)
        {
            try
            {
                if (_player != null && _player.IsSeekable)
                    _player.Time = (long)Math.Max(0, seconds * 1000);
                else
                    _audioSeekFn?.Invoke(seconds);
            }
            catch (Exception ex) { App.Logger?.Debug("EditorPreviewTimeSource.Seek error: {Error}", ex.Message); }
        }

        public void Pause()
        {
            try
            {
                if (_player != null) _player.Pause();
                else _audioPauseFn?.Invoke();
            }
            catch (Exception ex) { App.Logger?.Debug("EditorPreviewTimeSource.Pause error: {Error}", ex.Message); }
        }

        public void Play()
        {
            try
            {
                if (_player != null) _player.Play();
                else _audioPlayFn?.Invoke();
            }
            catch (Exception ex) { App.Logger?.Debug("EditorPreviewTimeSource.Play error: {Error}", ex.Message); }
        }

        public Rect GetVideoRect()
        {
            try { return _videoRectFn?.Invoke() ?? Rect.Empty; }
            catch { return Rect.Empty; }
        }

        public void Dispose() => Detach();
    }
}
