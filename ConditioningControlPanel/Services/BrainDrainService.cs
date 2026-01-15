using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
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
            if (!bypassLevelCheck && App.Settings.Current.PlayerLevel < 70)
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

            var mode = App.Settings.Current.BrainDrainHighRefresh ? "High Refresh (500ms)" : "Normal (5s)";
            App.Logger?.Information("BrainDrain started at intensity {Intensity}%, mode: {Mode}", _intensity, mode);
        }
        
        public void Stop()
        {
            if (!_isRunning) return;
            
            _isRunning = false;
            _timer.Stop();
            _cts?.Cancel();
            
            StopCurrentAudio();
            
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
            try
            {
                StopCurrentAudio();
                
                _audioReader = new AudioFileReader(filePath);
                _audioReader.Volume = (float)(App.Settings.Current.MasterVolume / 100.0);
                
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioReader);
                _waveOut.PlaybackStopped += (s, e) =>
                {
                    try
                    {
                        _waveOut?.Dispose();
                        _audioReader?.Dispose();
                    }
                    catch { }
                };
                
                _waveOut.Play();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BrainDrain: Error playing audio file {Path}", filePath);
            }
        }
        
        private void StopCurrentAudio()
        {
            try
            {
                _waveOut?.Stop();
                _waveOut?.Dispose();
                _waveOut = null;

                _audioReader?.Dispose();
                _audioReader = null;
            }
            catch { }
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