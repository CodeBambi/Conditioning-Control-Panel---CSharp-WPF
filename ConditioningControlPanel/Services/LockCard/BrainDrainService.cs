using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;
using NAudio.Wave;
using Serilog;

namespace ConditioningControlPanel.Services
{
    public class BrainDrainService : IDisposable
    {
        private readonly Random _random = new();
        private readonly DispatcherTimer _timer;
        private CancellationTokenSource? _cts;
        
        private bool _isRunning;
        private double _intensity = 50; // 50% default intensity
        
        private string[]? _audioFiles;
        private WaveOutEvent? _waveOut;
        private AudioFileReader? _audioReader;
        /// <summary>Bumped by every trigger and by StopCurrentAudio. A build that finishes after a
        /// newer one started (or after a stop) is thrown away instead of published over the live
        /// player.</summary>
        private long _playGeneration;

        /// <summary>
        /// Guards the player fields and the generation. Held ONLY for cheap work: the arbitration
        /// check, the field swap and <c>WaveOutEvent.Play</c> (which just queues the playback
        /// thread). Never held across a build, a Stop or a Dispose, so a background build can never
        /// block a stop and a stop can never block the UI thread.
        /// </summary>
        private readonly object _audioLock = new();

        public bool IsRunning => _isRunning;
        public int AudioFileCount => _audioFiles?.Length ?? 0;

        /// <summary>
        /// Fires when a brain drain audio effect is triggered
        /// </summary>
        public event EventHandler? BrainDrainTriggered;
        
        public double Intensity
        {
            get => _intensity;
            set => _intensity = Math.Clamp(value, 1, 100);
        }
        
        public BrainDrainService()
        {
            _timer = new DispatcherTimer();
            _timer.Tick += Timer_Tick;

            LoadAudioFiles();
        }

        private void UpdateTimerInterval()
        {
            // High refresh mode: 500ms interval for smoother effect
            // Normal mode: 5s interval for lower CPU usage
            var interval = App.Settings.Current.BrainDrainHighRefresh
                ? TimeSpan.FromMilliseconds(500)
                : TimeSpan.FromSeconds(5);

            _timer.Interval = interval;
        }
        
        private void LoadAudioFiles()
        {
            try
            {
                var audioFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "braindrain");
                
                App.Logger?.Information("BrainDrain: Looking for audio files in {Path}", audioFolderPath);
                
                if (!Directory.Exists(audioFolderPath))
                {
                    Directory.CreateDirectory(audioFolderPath);
                    App.Logger?.Warning("BrainDrain: Created empty folder at {Path} - add audio files here!", audioFolderPath);
                    _audioFiles = Array.Empty<string>();
                    return;
                }
                
                _audioFiles = Directory.GetFiles(audioFolderPath, "*.*")
                    .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                
                if (_audioFiles.Length == 0)
                {
                    App.Logger?.Warning("BrainDrain: No .mp3/.wav/.ogg files found in {Path}", audioFolderPath);
                }
                else
                {
                    App.Logger?.Information("BrainDrain: Loaded {Count} audio files", _audioFiles.Length);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BrainDrain: Failed to load audio files");
                _audioFiles = Array.Empty<string>();
            }
        }
        
        public void ReloadAudioFiles()
        {
            LoadAudioFiles();
        }
        
        public void Start(bool bypassLevelCheck = false)
        {
            if (!bypassLevelCheck && !App.Settings.Current.IsLevelUnlocked(70))
            {
                App.Logger?.Information("BrainDrain: Level {Level} is below 70, not available", App.Settings.Current.PlayerLevel);
                return;
            }

            if (!App.Settings.Current.BrainDrainEnabled)
            {
                App.Logger?.Debug("BrainDrain: Not enabled in settings");
                return;
            }

            if (_isRunning) return;

            UpdateSettings();
            UpdateTimerInterval();
            _isRunning = true;
            _cts = new CancellationTokenSource();

            _timer.Start();

            // Update Discord presence
            App.DiscordRpc?.SetBrainDrainActivity();

            var mode = App.Settings.Current.BrainDrainHighRefresh ? "High Refresh (500ms)" : "Normal (5s)";
            App.Logger?.Information("BrainDrain started at intensity {Intensity}%, mode: {Mode}", _intensity, mode);
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cts?.Cancel();

            // Audio teardown FIRST and inline. App.KillAllAudio can reach us from the panic
            // fallback thread, and the DispatcherTimer below has UI-thread affinity - stopping it
            // first would throw there and leave the audio playing. NAudio has no thread affinity.
            StopCurrentAudio();

            DispatcherHelper.RunOnUI(() => { try { _timer.Stop(); } catch { } });

            // Update Discord presence back to idle
            App.DiscordRpc?.SetIdleActivity();

            App.Logger?.Information("BrainDrain stopped");
        }
        
        public void UpdateSettings()
        {
            Intensity = App.Settings.Current.BrainDrainIntensity;
        }
        
        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_isRunning) return;
            if (_audioFiles == null || _audioFiles.Length == 0) return;

            var probability = _intensity / 100.0 / (60.0 / _timer.Interval.TotalSeconds);
            
            if (_random.NextDouble() < probability)
            {
                PlayAudioNow();
            }
        }
        
        private void PlayAudioNow()
        {
            if (_audioFiles == null || _audioFiles.Length == 0) return;

            try
            {
                var audioFile = _audioFiles[_random.Next(_audioFiles.Length)];
                PlayAudio(audioFile);

                // Fire event for avatar/UI notification
                BrainDrainTriggered?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BrainDrain: Failed to play audio");
            }
        }
        
        private void PlayAudio(string filePath)
        {
            if (App.Audio?.IsOutputSuppressed == true) return; // endpoint down — stay quiet, don't spin

            var volume = CurrentMasterVolume();
            long generation;
            lock (_audioLock)
            {
                generation = ++_playGeneration;
            }

            // Building an AudioFileReader (mp3 index) and opening a WaveOutEvent are half-second
            // blocking calls. Run at this feature's cadence on the UI thread they read as the app
            // hitching every few seconds (#890), so the whole build happens off-thread; the pair is
            // only started once it has won the arbitration below.
            Task.Run(() =>
            {
                AudioFileReader? reader = null;
                WaveOutEvent? waveOut = null;
                try
                {
                    reader = new AudioFileReader(filePath) { Volume = volume };
                    waveOut = new WaveOutEvent();
                    App.Audio?.ApplyPreferredDevice(waveOut);
                    waveOut.Init(reader);

                    // Capture THIS pair rather than reading the fields: by the time the clip ends
                    // they can already point at a newer player, and disposing that one killed
                    // live audio.
                    var endedReader = reader;
                    var endedWaveOut = waveOut;
                    waveOut.PlaybackStopped += (_, _) =>
                    {
                        lock (_audioLock)
                        {
                            if (ReferenceEquals(_waveOut, endedWaveOut))
                            {
                                _waveOut = null;
                                _audioReader = null;
                            }
                        }
                        DisposePair(endedWaveOut, endedReader);
                    };
                }
                catch (Exception ex)
                {
                    DisposePair(waveOut, reader);
                    App.Logger?.Error(ex, "BrainDrain: Error playing audio file {Path}", filePath);
                    App.Audio?.NoteOutputFailure("braindrain", ex.Message);
                    return;
                }

                WaveOutEvent? displacedOut = null;
                AudioFileReader? displacedReader = null;
                var started = false;

                lock (_audioLock)
                {
                    // The stop decision is arbitrated HERE, on this thread. NAudio has no UI
                    // affinity, and deferring it to the dispatcher meant a wedged UI thread (the
                    // panic case) could never stop an in-flight pair, while a shutdown dropped it
                    // still playing and undisposed.
                    if (_isRunning && generation == _playGeneration)
                    {
                        try
                        {
                            waveOut!.Play();   // only queues the playback thread; cheap under the lock
                            displacedOut = _waveOut;
                            displacedReader = _audioReader;
                            _waveOut = waveOut;
                            _audioReader = reader;
                            // Master volume may have been dragged during the build, and nothing
                            // else re-reads it for this clip's whole duration.
                            try { reader!.Volume = CurrentMasterVolume(); } catch { }
                            started = true;
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Error(ex, "BrainDrain: Error starting audio file {Path}", filePath);
                        }
                    }
                }

                DisposePair(displacedOut, displacedReader);
                if (started) App.Audio?.NoteOutputSuccess();
                else DisposePair(waveOut, reader);
            });
        }

        private static float CurrentMasterVolume()
        {
            try { return Math.Clamp(App.Settings.Current.MasterVolume, 0, 100) / 100f; }
            catch { return 1f; }
        }

        /// <summary>
        /// Stops and disposes a pair. Never called with <see cref="_audioLock"/> held: waveOutReset
        /// and waveOutClose take NAudio's own locks and can be raced by the playback thread, so this
        /// service's lock deliberately stays out of them.
        /// </summary>
        private static void DisposePair(WaveOutEvent? waveOut, AudioFileReader? reader)
        {
            try { waveOut?.Stop(); } catch { }
            try { waveOut?.Dispose(); } catch { }
            try { reader?.Dispose(); } catch { }
        }

        private void StopCurrentAudio()
        {
            WaveOutEvent? waveOut;
            AudioFileReader? reader;
            lock (_audioLock)
            {
                // Bumping the generation is what makes a stop stick: Stop() alone relied on
                // !_isRunning, so a Stop->Start while a build was in flight let the stopped
                // session's clip publish over the new one.
                _playGeneration++;
                // Fields cleared FIRST: PlaybackStopped may already have disposed this pair, and a
                // throw out of Stop() used to leave the stale references in place.
                waveOut = _waveOut;
                reader = _audioReader;
                _waveOut = null;
                _audioReader = null;
            }
            DisposePair(waveOut, reader);
        }

        /// <summary>
        /// Update volume on currently playing audio (for live master volume changes).
        /// </summary>
        public void UpdateMasterVolume(int volume)
        {
            try
            {
                if (_audioReader != null)
                {
                    _audioReader.Volume = Math.Clamp(volume, 0, 100) / 100.0f;
                }
            }
            catch { }
        }

        public void Dispose()
        {
            Stop();
            StopCurrentAudio();
        }
    }
}