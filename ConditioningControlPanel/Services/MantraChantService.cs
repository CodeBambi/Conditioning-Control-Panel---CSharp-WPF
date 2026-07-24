using System;
using System.Windows.Threading;
using NAudio.Wave;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// "Mantra Chant" — loops the active mod's VOICED mantra clips back-to-back as ambient audio.
    ///
    /// Where <see cref="MantraVoiceService"/> serves a single spoken mantra for the Takeover mic
    /// mechanic, this plays their <c>promptAudio</c> deliveries on repeat with a configurable gap so
    /// the user can bathe in the mantras hands-free. It reuses <see cref="MantraVoiceService"/> for
    /// clip selection + path resolution and copies the chained WaveOutEvent + reader loop from
    /// <see cref="Deeper.EnhancementAudioPlayer"/> (the next clip is queued on PlaybackStopped).
    ///
    /// Scoped to voiced entries only: a mantra whose promptAudio doesn't resolve is skipped, and a
    /// mod that ships no voiced mantras at all leaves the loop dark (see <see cref="CanChant"/>), so
    /// the feature never loops silence.
    /// </summary>
    public sealed class MantraChantService : IDisposable
    {
        private readonly object _lock = new();
        private WaveOutEvent? _output;
        private AudioFileReader? _reader;
        private DispatcherTimer? _gapTimer;
        private bool _running;
        private bool _disposed;

        /// <summary>True while the chant loop is active.</summary>
        public bool IsRunning { get { lock (_lock) return _running; } }

        /// <summary>True when the active mod has at least one voiced mantra we can actually chant.</summary>
        public bool CanChant() => App.MantraVoice?.HasVoicedMantras() == true;

        /// <summary>Start the loop. No-ops if already running or the active mod has nothing voiced.</summary>
        public void Start()
        {
            if (_disposed) return;
            lock (_lock)
            {
                if (_running) return;
                if (App.MantraVoice?.HasVoicedMantras() != true)
                {
                    App.Logger?.Information("MantraChantService: no voiced mantras for the active mod — chant stays off.");
                    return;
                }
                _running = true;
            }
            App.Logger?.Information("MantraChantService: chant started.");
            PlayNext();
        }

        /// <summary>Stop the loop and release the output device. Safe to call when already stopped.</summary>
        public void Stop()
        {
            bool wasActive;
            lock (_lock)
            {
                wasActive = _running || _output != null || _gapTimer != null;
                _running = false;
            }
            if (!wasActive) return;
            try { _gapTimer?.Stop(); _gapTimer = null; } catch { }
            DisposeOutput();
            App.Logger?.Debug("MantraChantService: chant stopped.");
        }

        /// <summary>Push the current MantraChantVolume onto a clip that's already playing (live slider drag).</summary>
        public void ApplyVolume()
        {
            try { if (_reader != null) _reader.Volume = EffectiveVolume(); }
            catch (Exception ex) { App.Logger?.Debug("MantraChantService.ApplyVolume error: {Error}", ex.Message); }
        }

        // Chant volume folded with the app master volume so a global mute silences the chant too.
        private static float EffectiveVolume()
        {
            var s = App.Settings?.Current;
            var chant = (s?.MantraChantVolume ?? 50) / 100.0;
            var master = (s?.MasterVolume ?? 100) / 100.0;
            return (float)Math.Clamp(chant * master, 0.0, 1.0);
        }

        // Pick a voiced clip (skipping text-only entries) and start it. If nothing resolves after a
        // bounded number of picks the set is effectively text-only, so the loop self-heals to off.
        private void PlayNext()
        {
            if (_disposed) return;
            lock (_lock) { if (!_running) return; }

            string? path = null;
            for (int i = 0; i < 12 && string.IsNullOrEmpty(path); i++)
            {
                var entry = App.MantraVoice?.NextMantra();
                if (entry == null) break;
                path = App.MantraVoice?.ResolveAudio(entry.PromptAudio);
            }

            if (string.IsNullOrEmpty(path))
            {
                App.Logger?.Information("MantraChantService: no voiced clip resolved — stopping.");
                Stop();
                return;
            }

            try
            {
                DisposeOutput(); // release the previous clip before opening the next

                _reader = new AudioFileReader(path) { Volume = EffectiveVolume() };
                _output = new WaveOutEvent { DesiredLatency = 200 };
                App.Audio?.ApplyPreferredDevice(_output);
                _output.Init(_reader);
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Play();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MantraChantService.PlayNext failed for {Path} — retrying after the gap.", path);
                DisposeOutput();
                ScheduleNext(); // don't wedge the loop on one bad clip
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            // NAudio raises this on its WaveOut callback thread; marshal to the UI thread before we
            // touch the DispatcherTimer (Stop off-thread silently corrupts it) — same as EnhancementAudioPlayer.
            var ex = e.Exception;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            void Handle()
            {
                if (ex != null)
                    App.Logger?.Warning(ex, "MantraChantService: clip stopped with error");
                bool run;
                lock (_lock) run = _running;
                if (run) ScheduleNext();
            }

            if (dispatcher.CheckAccess()) Handle();
            else { try { dispatcher.BeginInvoke((Action)Handle); } catch { } }
        }

        // Wait MantraChantGapSeconds, then play the next clip. Single-shot timer on the UI thread.
        private void ScheduleNext()
        {
            if (_disposed) return;
            lock (_lock) { if (!_running) return; }

            try { _gapTimer?.Stop(); } catch { }
            var gap = Math.Clamp(App.Settings?.Current?.MantraChantGapSeconds ?? 5, 0, 60);
            _gapTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(gap) };
            _gapTimer.Tick += (_, _) =>
            {
                try { _gapTimer?.Stop(); } catch { }
                _gapTimer = null;
                PlayNext();
            };
            _gapTimer.Start();
        }

        // Tear down the current output + reader. Unsubscribe FIRST so our own Stop() doesn't re-enter
        // OnPlaybackStopped and re-queue a clip.
        private void DisposeOutput()
        {
            try
            {
                if (_output != null)
                {
                    _output.PlaybackStopped -= OnPlaybackStopped;
                    _output.Stop();
                    _output.Dispose();
                    _output = null;
                }
                _reader?.Dispose();
                _reader = null;
            }
            catch (Exception ex) { App.Logger?.Debug("MantraChantService.DisposeOutput error: {Error}", ex.Message); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
