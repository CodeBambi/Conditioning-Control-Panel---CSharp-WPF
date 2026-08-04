using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Audio
{
    /// <summary>
    /// Suggestion #659 — "Audio Layers". Plays several user-supplied audio tracks at once,
    /// each looping seamlessly, mixed through a SINGLE output device.
    ///
    /// The app has a documented handle-exhaustion history (see CLAUDE.md), so this service
    /// deliberately opens ONE <see cref="WaveOutEvent"/> and feeds it a
    /// <see cref="MixingSampleProvider"/> — never one device per track. Each track becomes a
    /// looping <see cref="ISampleProvider"/> (rewinds its <see cref="AudioFileReader"/> at EOF),
    /// is normalised into the mixer's format (stereo + resampled), then wrapped in a
    /// <see cref="VolumeSampleProvider"/> for independent gain. A master
    /// <see cref="VolumeSampleProvider"/> sits on the mixer output for the overall level and
    /// for cooperative ducking (<see cref="ApplyDuck"/> / <see cref="ReleaseDuck"/>).
    /// </summary>
    public class LayeredAudioService : IDisposable
    {
        // Uniform mixer format — MixingSampleProvider requires every input to share this.
        private static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        private readonly object _lock = new();

        private WaveOutEvent? _output;
        private MixingSampleProvider? _mixer;
        private VolumeSampleProvider? _master;

        // Per-track disposables + the live VolumeSampleProvider so the config window can
        // adjust a single track's gain without rebuilding the whole graph (no audio gap).
        private readonly List<AudioFileReader> _readers = new();
        private readonly Dictionary<AudioLayerTrack, VolumeSampleProvider> _trackVolumes = new();

        private bool _isPlaying;
        private bool _disposed;

        // Cooperative duck factor (1 = full volume, <1 while other audio ducks us).
        private float _duckFactor = 1f;

        /// <summary>True while the layered player is actively mixing tracks.</summary>
        public bool IsPlaying { get { lock (_lock) return _isPlaying; } }

        /// <summary>
        /// (Re)build the mixing graph from <see cref="AppSettings.AudioLayers"/> and start playback.
        /// Idempotent: a second call rebuilds from current settings. Unreadable/missing files are
        /// skipped with a log — a bad file never stops the other tracks or crashes.
        /// </summary>
        /// <param name="ignoreMasterToggle">
        /// When true, play even if <see cref="AppSettings.AudioLayersEnabled"/> is off. Used by
        /// #668 audio-only sessions, which want the bed regardless of the standalone master toggle.
        /// </param>
        public void Start(bool ignoreMasterToggle = false)
        {
            lock (_lock)
            {
                if (_disposed) return;
                StopInternal();

                var settings = App.Settings?.Current;
                if (settings == null) return;
                if (!settings.AudioLayersEnabled && !ignoreMasterToggle) return;

                var tracks = settings.AudioLayers;
                if (tracks == null || tracks.Count == 0) return;

                var mixer = new MixingSampleProvider(MixFormat) { ReadFully = true };
                int added = 0;

                foreach (var track in tracks)
                {
                    if (track == null || !track.Enabled) continue;
                    if (string.IsNullOrWhiteSpace(track.Path) || !File.Exists(track.Path))
                    {
                        App.Logger?.Debug("LayeredAudio: skipping missing/unset track '{Path}'", track?.Path);
                        continue;
                    }

                    try
                    {
                        var reader = new AudioFileReader(track.Path);
                        var looped = new LoopingSampleProvider(reader);
                        ISampleProvider normalised = ConvertToMixFormat(looped);

                        var vol = new VolumeSampleProvider(normalised)
                        {
                            Volume = Math.Clamp(track.Volume / 100f, 0f, 1f)
                        };

                        mixer.AddMixerInput(vol);
                        _readers.Add(reader);
                        _trackVolumes[track] = vol;
                        added++;
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("LayeredAudio: could not load '{Path}': {Error}", track.Path, ex.Message);
                    }
                }

                if (added == 0)
                {
                    // Nothing usable — tear down the disposables we may have opened and bail.
                    StopInternal();
                    App.Logger?.Information("LayeredAudio: no playable tracks — player not started");
                    return;
                }

                _mixer = mixer;
                _master = new VolumeSampleProvider(mixer) { Volume = ComputeMasterGain(settings) };

                try
                {
                    _output = App.Audio?.CreateWaveOut() ?? new WaveOutEvent();
                    App.Audio?.ApplyPreferredDevice(_output);
                    _output.Init(_master);
                    _output.Play();
                    _isPlaying = true;
                    App.Logger?.Information("LayeredAudio: started with {Count} track(s)", added);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("LayeredAudio: output init failed: {Error}", ex.Message);
                    StopInternal();
                }
            }
        }

        /// <summary>Stop playback and release the device + all readers.</summary>
        public void Stop()
        {
            lock (_lock) StopInternal();
        }

        /// <summary>Rebuild + restart if the track list / enable flags changed structurally.</summary>
        public void Restart()
        {
            lock (_lock)
            {
                if (_disposed) return;
                bool wasPlaying = _isPlaying;
                StopInternal();
                if (wasPlaying || (App.Settings?.Current?.AudioLayersEnabled == true))
                    Start();
            }
        }

        /// <summary>
        /// Live-apply a single track's volume without rebuilding (smooth slider drags).
        /// No-op if that track isn't currently in the running graph.
        /// </summary>
        public void SetTrackVolumeLive(AudioLayerTrack track, int volumePercent)
        {
            lock (_lock)
            {
                if (track != null && _trackVolumes.TryGetValue(track, out var vp))
                    vp.Volume = Math.Clamp(volumePercent / 100f, 0f, 1f);
            }
        }

        /// <summary>Live-apply the master (overall) volume without rebuilding.</summary>
        public void SetMasterVolumeLive()
        {
            lock (_lock)
            {
                var s = App.Settings?.Current;
                if (_master != null && s != null) _master.Volume = ComputeMasterGain(s);
            }
        }

        /// <summary>
        /// Cooperative duck: lower our master gain while other CCP audio (barks, whispers)
        /// plays. Called from <see cref="AudioService.Duck"/>. Our mixer output is in-process
        /// so the session-based ducker can't reach it — this is the in-process equivalent.
        /// </summary>
        public void ApplyDuck(float amount)
        {
            lock (_lock)
            {
                _duckFactor = Math.Clamp(1f - amount, 0f, 1f);
                SetMasterVolumeLive();
            }
        }

        /// <summary>Release a cooperative duck (restore master gain).</summary>
        public void ReleaseDuck()
        {
            lock (_lock)
            {
                _duckFactor = 1f;
                SetMasterVolumeLive();
            }
        }

        private float ComputeMasterGain(AppSettings settings)
        {
            var layers = Math.Clamp(settings.AudioLayersMasterVolume / 100f, 0f, 1f);
            var appMaster = Math.Clamp(settings.MasterVolume / 100f, 0f, 1f);
            return Math.Clamp(layers * appMaster * _duckFactor, 0f, 1f);
        }

        /// <summary>Normalise any source to the mixer's channel count + sample rate.</summary>
        private static ISampleProvider ConvertToMixFormat(ISampleProvider src)
        {
            // Channels first: MonoToStereo needs a mono input; a >2ch source is left as-is
            // (rare for user audio) and would surface as a mixer format mismatch we log below.
            if (src.WaveFormat.Channels == 1)
                src = new MonoToStereoSampleProvider(src);

            if (src.WaveFormat.SampleRate != MixFormat.SampleRate)
                src = new WdlResamplingSampleProvider(src, MixFormat.SampleRate);

            return src;
        }

        private void StopInternal()
        {
            try { _output?.Stop(); } catch { }
            try { _output?.Dispose(); } catch { }
            _output = null;

            foreach (var r in _readers)
            {
                try { r.Dispose(); } catch { }
            }
            _readers.Clear();
            _trackVolumes.Clear();

            _mixer = null;
            _master = null;
            _isPlaying = false;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                StopInternal();
            }
        }

        /// <summary>
        /// Loops an <see cref="AudioFileReader"/> forever by rewinding to the start on EOF.
        /// NAudio has no built-in looping sample provider; this is the sample-domain analogue
        /// of the LoopStream idiom used elsewhere (ChaosTunnelService/QuizWindow).
        /// </summary>
        private sealed class LoopingSampleProvider : ISampleProvider
        {
            private readonly AudioFileReader _reader;
            public LoopingSampleProvider(AudioFileReader reader) => _reader = reader;
            public WaveFormat WaveFormat => _reader.WaveFormat;

            public int Read(float[] buffer, int offset, int count)
            {
                int total = 0;
                while (total < count)
                {
                    int n = _reader.Read(buffer, offset + total, count - total);
                    if (n == 0)
                    {
                        if (_reader.Position == 0) break; // empty/failed source — don't spin
                        _reader.Position = 0;             // rewind and keep filling the buffer
                        continue;
                    }
                    total += n;
                }
                return total;
            }
        }
    }
}
