using System;
using System.Windows;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Adapts <see cref="VideoService"/>'s primary-monitor playback to the
    /// generic <see cref="IPlaybackTimeSource"/> the EnhancementEngine consumes.
    ///
    /// Subscribes lazily — call <see cref="Attach"/> when starting an engine,
    /// <see cref="Detach"/> when stopping. Multiple sources can be attached to
    /// one VideoService at once (each gets the same time stream).
    /// </summary>
    public sealed class VideoServiceTimeSource : IPlaybackTimeSource, IDisposable
    {
        private readonly VideoService _video;
        private bool _attached;

        public event Action<double>? PlaybackTimeChanged;

        public VideoServiceTimeSource(VideoService video)
        {
            _video = video ?? throw new ArgumentNullException(nameof(video));
        }

        public void Attach()
        {
            if (_attached) return;
            _video.PrimaryPlaybackTimeMsChanged += OnTimeMs;
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached) return;
            _video.PrimaryPlaybackTimeMsChanged -= OnTimeMs;
            _attached = false;
        }

        private void OnTimeMs(long ms)
        {
            try { PlaybackTimeChanged?.Invoke(ms / 1000.0); }
            catch (Exception ex) { App.Logger?.Debug("VideoServiceTimeSource handler error: {Error}", ex.Message); }
        }

        public double GetCurrentTimeSeconds()
        {
            var ms = _video.GetCurrentPlaybackTimeMs();
            return ms < 0 ? 0 : ms / 1000.0;
        }

        public double GetDurationSeconds()
        {
            try
            {
                var len = _video.PrimaryMediaPlayer?.Length ?? 0;
                return len > 0 ? len / 1000.0 : 0;
            }
            catch { return 0; }
        }

        public bool IsPlaying
        {
            get
            {
                try { return _video.PrimaryMediaPlayer?.IsPlaying ?? false; }
                catch { return false; }
            }
        }

        public void Seek(double seconds) => _video.SeekPrimary((long)Math.Max(0, seconds * 1000));
        public void Pause() => _video.PausePrimary();
        public void Play() => _video.PlayPrimary();

        public Rect GetVideoRect()
        {
            try
            {
                var w = _video.PrimaryVideoWindow;
                if (w == null) return Rect.Empty;
                return new Rect(w.Left, w.Top, w.Width, w.Height);
            }
            catch { return Rect.Empty; }
        }

        public void Dispose() => Detach();
    }
}
