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
        
        // Loop mode with crossfade
        private bool _loopMode;
        private string? _loopFilePath;
        private DateTime _loopStartTime;
        private bool _cleanSlateAchieved;
        
        // Crossfade support - two players for seamless looping
        private const double CROSSFADE_OVERLAP_SECONDS = 0.12;
        private WaveOutEvent? _loopWaveOutA;
        private WaveOutEvent? _loopWaveOutB;
        private AudioFileReader? _loopReaderA;
        private AudioFileReader? _loopReaderB;
        private bool _usePlayerA = true; // Alternate between A and B
        private DispatcherTimer? _crossfadeTimer;
        private TimeSpan _loopDuration;
        
        public bool IsRunning => _isRunning;
        public bool IsLooping => _loopMode && (_loopWaveOutA?.PlaybackState == PlaybackState.Playing || 
                                                _loopWaveOutB?.PlaybackState == PlaybackState.Playing);
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
                // Update loop players
                if (_loopReaderA != null)
                {
                    try { _loopReaderA.Volume = (float)_volume; } catch { }
                }
                if (_loopReaderB != null)
                {
                    try { _loopReaderB.Volume = (float)_volume; } catch { }
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
            StopLoop();
            
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
            // Update loop players
            if (_loopReaderA != null)
            {
                try { _loopReaderA.Volume = (float)_volume; } catch { }
            }
            if (_loopReaderB != null)
            {
                try { _loopReaderB.Volume = (float)_volume; } catch { }
            }
        }
        
        /// <summary>
        /// Start looping a random audio file continuously in the background with crossfade
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
            _loopStartTime = DateTime.Now;
            _cleanSlateAchieved = false;
            _usePlayerA = true;
            
            // Get audio duration for crossfade timing
            try
            {
                using var tempReader = new AudioFileReader(_loopFilePath);
                _loopDuration = tempReader.TotalTime;
                App.Logger?.Information("MindWipe: Loop file duration: {Duration:F2}s", _loopDuration.TotalSeconds);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "MindWipe: Failed to get audio duration, using fallback");
                _loopDuration = TimeSpan.FromSeconds(30); // Fallback
            }
            
            // Start first player
            StartNextLoopPlayer();
            
            // Setup crossfade timer - triggers slightly before track ends to start next player
            var crossfadeInterval = _loopDuration - TimeSpan.FromSeconds(CROSSFADE_OVERLAP_SECONDS);
            if (crossfadeInterval.TotalMilliseconds < 100)
            {
                crossfadeInterval = TimeSpan.FromMilliseconds(100);
            }
            
            _crossfadeTimer = new DispatcherTimer
            {
                Interval = crossfadeInterval
            };
            _crossfadeTimer.Tick += CrossfadeTimer_Tick;
            _crossfadeTimer.Start();
            
            App.Logger?.Information("MindWipe: Loop started with {File} at {Vol}% volume (crossfade: {Overlap}s)", 
                Path.GetFileName(_loopFilePath), volume * 100, CROSSFADE_OVERLAP_SECONDS);
        }
        
        private void CrossfadeTimer_Tick(object? sender, EventArgs e)
        {
            if (!_loopMode || string.IsNullOrEmpty(_loopFilePath)) return;
            
            // Check for Clean Slate achievement (60 seconds of continuous loop)
            if (!_cleanSlateAchieved)
            {
                var elapsed = (DateTime.Now - _loopStartTime).TotalSeconds;
                if (elapsed >= 60)
                {
                    _cleanSlateAchieved = true;
                    App.Achievements?.TrackMindWipeDuration(elapsed);
                    App.Logger?.Information("MindWipe: Clean Slate achievement triggered at {Elapsed:F0}s", elapsed);
                }
            }
            
            // Start the next player (creates overlap)
            StartNextLoopPlayer();
        }
        
        private void StartNextLoopPlayer()
        {
            if (!_loopMode || string.IsNullOrEmpty(_loopFilePath)) return;
            
            try
            {
                if (_usePlayerA)
                {
                    // Clean up old player A if exists
                    DisposePlayerA();
                    
                    // Create new player A
                    _loopReaderA = new AudioFileReader(_loopFilePath);
                    _loopReaderA.Volume = (float)_volume;
                    
                    _loopWaveOutA = new WaveOutEvent();
                    _loopWaveOutA.Init(_loopReaderA);
                    _loopWaveOutA.Play();
                    
                    App.Logger?.Debug("MindWipe: Started player A");
                    
                    // Schedule cleanup of player B after overlap period
                    SchedulePlayerCleanup(false);
                }
                else
                {
                    // Clean up old player B if exists
                    DisposePlayerB();
                    
                    // Create new player B
                    _loopReaderB = new AudioFileReader(_loopFilePath);
                    _loopReaderB.Volume = (float)_volume;
                    
                    _loopWaveOutB = new WaveOutEvent();
                    _loopWaveOutB.Init(_loopReaderB);
                    _loopWaveOutB.Play();
                    
                    App.Logger?.Debug("MindWipe: Started player B");
                    
                    // Schedule cleanup of player A after overlap period
                    SchedulePlayerCleanup(true);
                }
                
                // Alternate for next iteration
                _usePlayerA = !_usePlayerA;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "MindWipe: Error starting loop player");
            }
        }
        
        private void SchedulePlayerCleanup(bool cleanupA)
        {
            // Wait a bit longer than the overlap to ensure smooth transition, then cleanup old player
            Task.Delay(TimeSpan.FromSeconds(CROSSFADE_OVERLAP_SECONDS + 0.1)).ContinueWith(_ =>
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (cleanupA)
                    {
                        DisposePlayerA();
                    }
                    else
                    {
                        DisposePlayerB();
                    }
                });
            });
        }
        
        private void DisposePlayerA()
        {
            try
            {
                _loopWaveOutA?.Stop();
                _loopWaveOutA?.Dispose();
                _loopWaveOutA = null;
                
                _loopReaderA?.Dispose();
                _loopReaderA = null;
            }
            catch { }
        }
        
        private void DisposePlayerB()
        {
            try
            {
                _loopWaveOutB?.Stop();
                _loopWaveOutB?.Dispose();
                _loopWaveOutB = null;
                
                _loopReaderB?.Dispose();
                _loopReaderB = null;
            }
            catch { }
        }
        
        /// <summary>
        /// Stop the looping audio
        /// </summary>
        public void StopLoop()
        {
            _loopMode = false;
            _loopFilePath = null;
            
            _crossfadeTimer?.Stop();
            _crossfadeTimer = null;
            
            DisposePlayerA();
            DisposePlayerB();
            
            App.Logger?.Information("MindWipe: Loop stopped");
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
            StopLoop();
        }
    }
}
