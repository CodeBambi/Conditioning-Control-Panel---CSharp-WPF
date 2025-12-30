using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using NAudio.Wave;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Service for playing mind wipe audio effects at random intervals.
    /// Unlockable at level 75. Does NOT duck other audio.
    /// </summary>
    public class MindWipeService : IDisposable
    {
        private readonly Random _random = new();
        private readonly DispatcherTimer _timer;
        private CancellationTokenSource? _cts;
        
        private bool _isRunning;
        private double _frequencyPerHour = 6; // Default 6 per hour
        private double _volume = 0.5; // 50% default volume
        
        private string[]? _audioFiles;
        private WaveOutEvent? _waveOut;
        private AudioFileReader? _audioReader;
        
        // Session mode
        private bool _sessionMode;
        private int _sessionBaseFrequency;
        private DateTime _sessionStartTime;
        
        // Loop mode
        private bool _loopMode;
        private string? _loopFilePath;
        
        public bool IsRunning => _isRunning;
        public bool IsLooping => _loopMode && _waveOut?.PlaybackState == PlaybackState.Playing;
        public int AudioFileCount => _audioFiles?.Length ?? 0;
        
        public double FrequencyPerHour
        {
            get => _frequencyPerHour;
            set => _frequencyPerHour = Math.Clamp(value, 1, 180);
        }
        public double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0, 1);
                // Update live if playing
                if (_audioReader != null)
                {
                    try { _audioReader.Volume = (float)_volume; } catch { }
                }
            }
        }
        
        public MindWipeService()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10) // Check every 10 seconds for better high-frequency support
            };
            _timer.Tick += Timer_Tick;
            
            LoadAudioFiles();
        }
        
        private void LoadAudioFiles()
        {
            try
            {
                var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "mindwipe");
                
                App.Logger?.Information("MindWipe: Looking for audio files in {Path}", assetsPath);
                
                if (!Directory.Exists(assetsPath))
                {
                    // Create the directory so user knows where to put files
                    Directory.CreateDirectory(assetsPath);
                    App.Logger?.Warning("MindWipe: Created empty folder at {Path} - add audio files here!", assetsPath);
                    _audioFiles = Array.Empty<string>();
                    return;
                }
                
                _audioFiles = Directory.GetFiles(assetsPath, "*.*")
                    .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                
                if (_audioFiles.Length == 0)
                {
                    App.Logger?.Warning("MindWipe: No .mp3/.wav/.ogg files found in {Path}", assetsPath);
                }
                else
                {
                    App.Logger?.Information("MindWipe: Loaded {Count} audio files: {Files}", 
                        _audioFiles.Length, 
                        string.Join(", ", _audioFiles.Select(Path.GetFileName)));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "MindWipe: Failed to load audio files");
                _audioFiles = Array.Empty<string>();
            }
        }
        
        /// <summary>
        /// Reload audio files from disk (call after adding new files)
        /// </summary>
        public void ReloadAudioFiles()
        {
            LoadAudioFiles();
        }
        
        public void Start(double frequencyPerHour, double volume)
        {
            if (_isRunning)
            {
                App.Logger?.Debug("MindWipe: Already running, updating settings");
                UpdateSettings(frequencyPerHour, volume);
                return;
            }
            
            _frequencyPerHour = frequencyPerHour;
            _volume = volume;
            _sessionMode = false;
            _isRunning = true;
            _cts = new CancellationTokenSource();
            
            _timer.Start();
            
            App.Logger?.Information("MindWipe: Started (frequency: {Freq}/hour, volume: {Vol}%, files: {Count})", 
                frequencyPerHour, volume * 100, _audioFiles?.Length ?? 0);
        }
        
        /// <summary>
        /// Start in session mode with escalating frequency
        /// </summary>
        public void StartSession(int baseFrequencyMultiplier)
        {
            if (_isRunning) return;
            
            _sessionMode = true;
            _sessionBaseFrequency = baseFrequencyMultiplier;
            _sessionStartTime = DateTime.Now;
            _isRunning = true;
            _cts = new CancellationTokenSource();
            
            _timer.Start();
            
            App.Logger?.Information("MindWipe: Started in session mode (base multiplier: {Base})", 
                baseFrequencyMultiplier);
        }
        
        public void Stop()
        {
            if (!_isRunning) return;
            
            _isRunning = false;
            _timer.Stop();
            _cts?.Cancel();
            
            StopCurrentAudio();
            
            App.Logger?.Information("MindWipe: Stopped");
        }
        
        public void UpdateSettings(double frequencyPerHour, double volume)
        {
            _frequencyPerHour = frequencyPerHour;
            _volume = volume;
            // Update live volume if playing
            if (_audioReader != null)
            {
                _audioReader.Volume = (float)_volume;
            }
        }
        
        /// <summary>
        /// Start looping a random audio file continuously in the background
        /// </summary>
        public void StartLoop(double volume)
        {
            if (_audioFiles == null || _audioFiles.Length == 0)
            {
                App.Logger?.Warning("MindWipe: No audio files available for loop");
                return;
            }
            
            // Stop any existing playback
            StopLoop();
            
            _loopMode = true;
            _volume = volume;
            _loopFilePath = _audioFiles[_random.Next(_audioFiles.Length)];
            
            PlayLoopAudio();
            
            App.Logger?.Information("MindWipe: Loop started with {File} at {Vol}% volume", 
                Path.GetFileName(_loopFilePath), volume * 100);
        }
        
        /// <summary>
        /// Stop the looping audio
        /// </summary>
        public void StopLoop()
        {
            _loopMode = false;
            _loopFilePath = null;
            StopCurrentAudio();
            
            App.Logger?.Information("MindWipe: Loop stopped");
        }
        
        private void PlayLoopAudio()
        {
            if (!_loopMode || string.IsNullOrEmpty(_loopFilePath)) return;
            
            try
            {
                StopCurrentAudio();
                
                _audioReader = new AudioFileReader(_loopFilePath);
                _audioReader.Volume = (float)_volume;
                
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioReader);
                _waveOut.PlaybackStopped += (s, e) =>
                {
                    // Loop: restart when finished
                    if (_loopMode && _loopFilePath != null)
                    {
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            PlayLoopAudio();
                        });
                    }
                };
                
                _waveOut.Play();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "MindWipe: Error playing loop audio");
            }
        }
        
        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Don't trigger random sounds if loop mode is active
            if (_loopMode) return;
            
            if (!_isRunning)
            {
                App.Logger?.Warning("MindWipe: Timer ticked but not running");
                return;
            }
            
            if (_audioFiles == null || _audioFiles.Length == 0)
            {
                App.Logger?.Warning("MindWipe: No audio files loaded");
                return;
            }
            
            // Calculate probability of triggering in this 30-second window
            double probability;
            
            if (_sessionMode)
            {
                // Escalating frequency in session mode
                var elapsed = DateTime.Now - _sessionStartTime;
                var fiveMinBlocks = (int)(elapsed.TotalMinutes / 5);
                var playsThisBlock = _sessionBaseFrequency + fiveMinBlocks;
                
                // Cap at reasonable maximum (15 plays per 5 min block)
                playsThisBlock = Math.Min(playsThisBlock, 15);
                
                // 5 minutes = 30 ten-second windows
                probability = playsThisBlock / 30.0;
                
                App.Logger?.Debug("MindWipe: Session mode - Block {Block}, plays: {Plays}, prob: {Prob:P0}", 
                    fiveMinBlocks, playsThisBlock, probability);
            }
            else
            {
                // Normal mode: frequency per hour
                // 360 ten-second windows per hour
                // At 180/hour, probability = 0.5 = 50% chance per interval
                probability = _frequencyPerHour / 360.0;
                
                App.Logger?.Debug("MindWipe: Normal mode - Freq: {Freq}/h, prob: {Prob:P0}", 
                    _frequencyPerHour, probability);
            }
            
            // Generate random and check (probability > 1.0 means always trigger)
            var roll = _random.NextDouble();
            if (roll < probability)
            {
                App.Logger?.Information("MindWipe: Triggering audio (roll: {Roll:F2} < prob: {Prob:F2})", roll, probability);
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
                App.Logger?.Debug("MindWipe: Playing {File} at volume {Vol}%", 
                    Path.GetFileName(audioFile), _volume * 100);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "MindWipe: Failed to play audio");
            }
        }
        
        private void PlayAudio(string filePath)
        {
            try
            {
                // Stop any currently playing audio
                StopCurrentAudio();
                
                _audioReader = new AudioFileReader(filePath);
                _audioReader.Volume = (float)_volume;
                
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioReader);
                _waveOut.PlaybackStopped += (s, e) =>
                {
                    // Cleanup after playback
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
                App.Logger?.Error(ex, "MindWipe: Error playing audio file {Path}", filePath);
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
        /// Trigger a single mind wipe sound immediately (for testing)
        /// </summary>
        public void TriggerOnce()
        {
            if (_audioFiles == null || _audioFiles.Length == 0)
            {
                App.Logger?.Warning("MindWipe: No audio files available in assets/mindwipe/");
                System.Windows.MessageBox.Show(
                    "No audio files found!\n\nPlace .mp3, .wav, or .ogg files in:\nassets/mindwipe/", 
                    "Mind Wipe", 
                    System.Windows.MessageBoxButton.OK, 
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
            
            // Use settings volume for test
            _volume = App.Settings.Current.MindWipeVolume / 100.0;
            PlayAudioNow();
        }
        
        /// <summary>
        /// Get current session frequency (for UI display)
        /// </summary>
        public int GetCurrentSessionFrequency()
        {
            if (!_sessionMode) return (int)_frequencyPerHour;
            
            var elapsed = DateTime.Now - _sessionStartTime;
            var fiveMinBlocks = (int)(elapsed.TotalMinutes / 5);
            return Math.Min(_sessionBaseFrequency + fiveMinBlocks, 30);
        }
        
        public void Dispose()
        {
            Stop();
            StopCurrentAudio();
        }
    }
}
