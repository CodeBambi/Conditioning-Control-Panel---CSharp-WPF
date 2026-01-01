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
        
        public double Intensity
        {
            get => _intensity;
            set => _intensity = Math.Clamp(value, 1, 100);
        }
        
        public BrainDrainService()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5) 
            };
            _timer.Tick += Timer_Tick;
            
            LoadAudioFiles();
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
        
        public void Start()
        {
            if (App.Settings.Current.PlayerLevel < 90)
            {
                App.Logger?.Information("BrainDrain: Level {Level} is below 90, not available", App.Settings.Current.PlayerLevel);
                return;
            }

            if (!App.Settings.Current.BrainDrainEnabled)
            {
                App.Logger?.Debug("BrainDrain: Not enabled in settings");
                return;
            }

            if (_isRunning) return;
            
            UpdateSettings();
            _isRunning = true;
            _cts = new CancellationTokenSource();
            
            _timer.Start();
            
            App.Logger?.Information("BrainDrain started at intensity {Intensity}%", _intensity);
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
        
        public void Test()
        {
            if (_audioFiles == null || _audioFiles.Length == 0)
            {
                System.Windows.MessageBox.Show(
                    "No audio files found!\n\nPlace .mp3, .wav, or .ogg files in:\nResources/sounds/braindrain/", 
                    "Brain Drain", 
                    System.Windows.MessageBoxButton.OK, 
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
            
            PlayAudioNow();
        }
        
        public void Dispose()
        {
            Stop();
            StopCurrentAudio();
        }
    }
}