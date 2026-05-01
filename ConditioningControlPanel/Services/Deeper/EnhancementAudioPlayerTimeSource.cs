using System;
using System.Windows;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Adapts <see cref="EnhancementAudioPlayer"/> to <see cref="IPlaybackTimeSource"/>
    /// for the engine. Audio-only — GetVideoRect always returns Rect.Empty,
    /// which the engine treats as "skip gaze evaluation" silently.
    /// </summary>
    public sealed class EnhancementAudioPlayerTimeSource : IPlaybackTimeSource, IDisposable
    {
        private readonly EnhancementAudioPlayer _player;
        private bool _attached;

        public event Action<double>? PlaybackTimeChanged;

        public EnhancementAudioPlayerTimeSource(EnhancementAudioPlayer player)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
        }

        public void Attach()
        {
            if (_attached) return;
            _player.PlaybackTimeMsChanged += OnTimeMs;
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached) return;
            _player.PlaybackTimeMsChanged -= OnTimeMs;
            _attached = false;
        }

        private void OnTimeMs(long ms)
        {
            try { PlaybackTimeChanged?.Invoke(ms / 1000.0); }
            catch (Exception ex) { App.Logger?.Debug("EnhancementAudioPlayerTimeSource handler error: {Error}", ex.Message); }
        }

        public double GetCurrentTimeSeconds() => _player.CurrentTimeMs / 1000.0;
        public double GetDurationSeconds() => _player.DurationMs / 1000.0;
        public bool IsPlaying => _player.IsPlaying;

        public void Seek(double seconds) => _player.Seek(seconds);
        public void Pause() => _player.Pause();
        public void Play() => _player.Resume();

        public Rect GetVideoRect() => Rect.Empty;

        public void Dispose() => Detach();
    }
}
