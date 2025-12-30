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
                    _audioReader.Volume = (float)_volume;
                }
            }
        }
        
        public MindWipeService()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30) // Check every 30 seconds
            };
            _timer.Tick += Timer_Tick;
            
            LoadAudioFiles();
        }
        
        private void LoadAudioFiles()
        {
            try
            {
                var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "mindwipe");
                if (Directory.Exists(assetsPath))
                {
                    _audioFiles = Directory.GetFiles(assetsPath, "*.*")
                        .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    
                    App.Logger?.Information("MindWipe: Loaded {Count} audio files from {Path}", 
                        _audioFiles.Length, assetsPath);
                }
                else
                {
                    App.Logger?.Warning("MindWipe: Audio folder not found at {Path}", assetsPath);
                    _audioFiles = Array.Empty<string>();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "MindWipe: Failed to load audio files");
                _audioFiles = Array.Empty<string>();
            }
        }
        
        public void Start(double frequencyPerHour, double volume)
        {
            if (_isRunning) return;
            
            _frequencyPerHour = frequencyPerHour;
            _volume = volume;
            _sessionMode = false;
            _isRunning = true;
            _cts = new CancellationTokenSource();
            
            _timer.Start();
            
            App.Logger?.Information("MindWipe: Started (frequency: {Freq}/hour, volume: {Vol}%)", 
                frequencyPerHour, volume * 100);
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
            
            if (!_isRunning || _audioFiles == null || _audioFiles.Length == 0) return;
            
            // Calculate probability of triggering in this 30-second window
            double probability;
            
            if (_sessionMode)
            {
                // Escalating frequency in session mode
                // Base multiplier determines plays per 5-minute block
                // Block 0 (0-5min): base plays, Block 1 (5-10min): base+1 plays, etc.
                var elapsed = DateTime.Now - _sessionStartTime;
                var fiveMinBlocks = (int)(elapsed.TotalMinutes / 5);
                var playsThisBlock = _sessionBaseFrequency + fiveMinBlocks;
                
                // Cap at reasonable maximum (10 plays per 5 min block)
                playsThisBlock = Math.Min(playsThisBlock, 10);
                
                // 5 minutes = 10 thirty-second windows
                // Probability = plays / windows
                probability = playsThisBlock / 10.0;
                
                App.Logger?.Debug("MindWipe: Block {Block}, plays this block: {Plays}, probability: {Prob:P0}", 
                    fiveMinBlocks, playsThisBlock, probability);
            }
            else
            {
                // Normal mode: frequency per hour
                // 120 thirty-second windows per hour
                probability = _frequencyPerHour / 120.0;
            }
            
            if (_random.NextDouble() < probability)
            {
                _ = PlayRandomAudioAsync();
            }
        }
        
        private async Task PlayRandomAudioAsync()
        {
            if (_audioFiles == null || _audioFiles.Length == 0) return;
            
            try
            {
                var audioFile = _audioFiles[_random.Next(_audioFiles.Length)];
                
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    PlayAudio(audioFile);
                });
                
                App.Logger?.Debug("MindWipe: Playing {File}", Path.GetFileName(audioFile));
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
                App.Logger?.Warning("MindWipe: No audio files available");
                return;
            }
            
            _ = PlayRandomAudioAsync();
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
