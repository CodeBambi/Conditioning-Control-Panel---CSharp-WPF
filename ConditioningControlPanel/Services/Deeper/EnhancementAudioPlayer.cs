using System;
using System.IO;
using System.Windows.Threading;
using NAudio.Wave;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Long-form audio player for end-user Deeper enhancement playback.
    ///
    /// Separate from <see cref="AudioService"/> — that one is built around
    /// short cue clips (StopSound on every Play, no Pause/Seek), so it's the
    /// wrong shape for sitting through a 20-minute hypno track. This player
    /// uses MediaFoundationReader (covers MP3/AAC/WAV/M4A) + WaveOutEvent
    /// and exposes Play / Pause / Resume / Stop / Seek with a 100 ms polling
    /// timer that emits PlaybackTimeMsChanged so the engine has a tick stream
    /// to bind to.
    ///
    /// Eager construct (cheap), lazy resource open. Disposing kills the
    /// underlying reader + output device.
    /// </summary>
    public sealed class EnhancementAudioPlayer : IDisposable
    {
        private MediaFoundationReader? _reader;
        private WaveOutEvent? _output;
        private DispatcherTimer? _tickTimer;
        private string? _currentPath;
        private bool _disposed;

        public string? CurrentPath => _currentPath;
        public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
        public bool IsPaused => _output?.PlaybackState == PlaybackState.Paused;
        public long CurrentTimeMs => _reader == null ? 0 : (long)_reader.CurrentTime.TotalMilliseconds;
        public long DurationMs => _reader == null ? 0 : (long)_reader.TotalTime.TotalMilliseconds;

        /// <summary>0..100. Default 80.</summary>
        public int Volume
        {
            get => _output == null ? 80 : (int)Math.Round(_output.Volume * 100);
            set
            {
                if (_output == null) return;
                _output.Volume = Math.Clamp(value, 0, 100) / 100f;
            }
        }

        /// <summary>
        /// Fires from the UI thread on each polling tick while playing.
        /// Argument is current time in milliseconds.
        /// </summary>
        public event Action<long>? PlaybackTimeMsChanged;

        /// <summary>Fires (UI thread) when playback reaches the end naturally.</summary>
        public event Action? Ended;

        /// <summary>Fires (UI thread) when a new file is loaded successfully.</summary>
        public event Action<string>? Loaded;

        /// <summary>
        /// Open and start playing a file. Replaces any in-flight playback.
        /// Returns false on any failure (file missing, codec unsupported, etc.).
        /// </summary>
        public bool Play(string path)
        {
            if (_disposed) return false;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                App.Logger?.Debug("EnhancementAudioPlayer.Play: file missing ({Path})", path);
                return false;
            }

            try
            {
                Stop();

                _reader = new MediaFoundationReader(path);
                _output = new WaveOutEvent { DesiredLatency = 200 };
                _output.Init(_reader);
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Play();
                _currentPath = path;

                EnsureTimer();
                _tickTimer?.Start();

                try { Loaded?.Invoke(path); }
                catch (Exception ex) { App.Logger?.Debug("EnhancementAudioPlayer Loaded subscriber error: {Error}", ex.Message); }
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementAudioPlayer.Play failed");
                Stop();
                return false;
            }
        }

        public void Pause()
        {
            try { _output?.Pause(); _tickTimer?.Stop(); }
            catch (Exception ex) { App.Logger?.Debug("EnhancementAudioPlayer.Pause error: {Error}", ex.Message); }
        }

        public void Resume()
        {
            try { _output?.Play(); _tickTimer?.Start(); }
            catch (Exception ex) { App.Logger?.Debug("EnhancementAudioPlayer.Resume error: {Error}", ex.Message); }
        }

        public void Stop()
        {
            try
            {
                _tickTimer?.Stop();
                if (_output != null)
                {
                    _output.PlaybackStopped -= OnPlaybackStopped;
                    _output.Stop();
                    _output.Dispose();
                    _output = null;
                }
                _reader?.Dispose();
                _reader = null;
                _currentPath = null;
            }
            catch (Exception ex) { App.Logger?.Debug("EnhancementAudioPlayer.Stop error: {Error}", ex.Message); }
        }

        public void Seek(double seconds)
        {
            try
            {
                if (_reader == null) return;
                _reader.CurrentTime = TimeSpan.FromSeconds(Math.Max(0, seconds));
                // Push a tick immediately so the engine cursor rewinds without
                // waiting for the next 100 ms timer fire.
                try { PlaybackTimeMsChanged?.Invoke(CurrentTimeMs); } catch { }
            }
            catch (Exception ex) { App.Logger?.Debug("EnhancementAudioPlayer.Seek error: {Error}", ex.Message); }
        }

        private void EnsureTimer()
        {
            if (_tickTimer != null) return;
            _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _tickTimer.Tick += (_, _) =>
            {
                try { PlaybackTimeMsChanged?.Invoke(CurrentTimeMs); }
                catch (Exception ex) { App.Logger?.Debug("EnhancementAudioPlayer tick error: {Error}", ex.Message); }
            };
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            // PlaybackStopped fires on Stop AND on natural end. Distinguish by
            // checking if we ran past the end of the reader; if so, raise Ended.
            try
            {
                _tickTimer?.Stop();
                bool naturalEnd = _reader != null && _reader.Position >= _reader.Length - 1024;
                if (naturalEnd)
                {
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher != null && !dispatcher.HasShutdownStarted)
                        dispatcher.BeginInvoke(() => { try { Ended?.Invoke(); } catch { } });
                }
                if (e.Exception != null)
                    App.Logger?.Warning(e.Exception, "EnhancementAudioPlayer playback stopped with error");
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
