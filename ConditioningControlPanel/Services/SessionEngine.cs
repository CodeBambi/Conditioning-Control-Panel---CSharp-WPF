using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Central engine for running timed conditioning sessions.
    /// Coordinates all services (Flash, Subliminal, Audio, Overlays, etc.) based on session configuration.
    /// </summary>
    public class SessionEngine : IDisposable
    {
        // Events
        public event EventHandler<SessionPhaseChangedEventArgs>? PhaseChanged;
        public event EventHandler<SessionProgressEventArgs>? ProgressUpdated;
        public event EventHandler<SessionCompletedEventArgs>? SessionCompleted;
        public event EventHandler? SessionStarted;
        public event EventHandler? SessionStopped;
        
        // State
        private Session? _currentSession;
        private bool _isRunning;
        private DateTime _startTime;
        private DispatcherTimer? _mainTimer;
        private DispatcherTimer? _phaseTimer;
        private int _currentPhaseIndex;
        private CancellationTokenSource? _cancellationToken;
        
        // Saved settings (to restore after session)
        private AppSettings? _savedSettings;
        
        // Random for bubble bursts etc.
        private readonly Random _random = new();
        
        // Bubble burst tracking
        private List<double> _scheduledBubbleBursts = new();
        private int _bubbleBurstIndex;
        private bool _bubblesCurrentlyActive;
        private DateTime _bubbleBurstEndTime;
        
        // Ramp tracking
        private double _currentFlashOpacity;
        private double _currentPinkOpacity;
        private double _currentSpiralOpacity;
        
        // Randomized start times (±3 min from session defaults)
        private double _randomizedPinkStartMinute;
        private double _randomizedSpiralStartMinute;
        
        // Corner GIF window (for Gamer Girl session)
        private Window? _cornerGifWindow;
        
        // Reference to main window for service access
        private readonly MainWindow _mainWindow;
        
        public bool IsRunning => _isRunning;
        public Session? CurrentSession => _currentSession;
        public TimeSpan ElapsedTime => _isRunning ? DateTime.Now - _startTime : TimeSpan.Zero;
        public TimeSpan RemainingTime => _currentSession != null 
            ? TimeSpan.FromMinutes(_currentSession.DurationMinutes) - ElapsedTime 
            : TimeSpan.Zero;
        public double ProgressPercent => _currentSession != null 
            ? Math.Min(100, (ElapsedTime.TotalMinutes / _currentSession.DurationMinutes) * 100) 
            : 0;
        
        public SessionEngine(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }
        
        /// <summary>
        /// Starts a session with the given configuration
        /// </summary>
        public async Task StartSessionAsync(Session session)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("A session is already running. Stop it first.");
            }
            
            _currentSession = session;
            _isRunning = true;
            _startTime = DateTime.Now;
            _currentPhaseIndex = 0;
            _cancellationToken = new CancellationTokenSource();
            
            // Save current settings to restore later
            SaveCurrentSettings();
            
            // Randomize delayed start times (±3 minutes from session defaults)
            RandomizeStartTimes(session);
            
            // Apply session settings
            ApplySessionSettings(session.Settings);
            
            // Schedule bubble bursts if enabled
            if (session.Settings.BubblesEnabled && session.Settings.BubblesIntermittent)
            {
                ScheduleBubbleBursts(session);
            }
            
            // Initialize ramp values
            _currentFlashOpacity = session.Settings.FlashOpacity;
            _currentPinkOpacity = session.Settings.PinkFilterStartOpacity;
            _currentSpiralOpacity = session.Settings.SpiralOpacity;
            
            // Setup corner GIF if enabled
            if (session.Settings.CornerGifEnabled)
            {
                ShowCornerGif(session.Settings);
            }
            
            // Start Mind Wipe if enabled (escalating frequency)
            if (session.Settings.MindWipeEnabled)
            {
                App.MindWipe.Volume = session.Settings.MindWipeVolume / 100.0;
                App.MindWipe.StartSession(session.Settings.MindWipeBaseMultiplier);
            }
            
            // Start main timer (updates every second)
            _mainTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _mainTimer.Tick += MainTimer_Tick;
            _mainTimer.Start();
            
            // Fire started event
            SessionStarted?.Invoke(this, EventArgs.Empty);
            
            // Track session start for achievements (e.g., Relapse)
            App.Achievements?.TrackSessionStart();
            
            // Announce first phase
            if (session.Phases.Count > 0)
            {
                PhaseChanged?.Invoke(this, new SessionPhaseChangedEventArgs(session.Phases[0], 0));
            }
            
            App.Logger?.Information("Session started: {Name}", session.Name);
        }
        
        /// <summary>
        /// Stops the current session
        /// </summary>
        public void StopSession(bool completed = false)
        {
            if (!_isRunning) return;
            
            _isRunning = false;
            _cancellationToken?.Cancel();
            
            // Stop timers
            _mainTimer?.Stop();
            _mainTimer = null;
            _phaseTimer?.Stop();
            _phaseTimer = null;
            
            // Close corner GIF
            CloseCornerGif();
            
            // Stop Mind Wipe
            App.MindWipe?.Stop();
            
            // Stop Bubbles
            App.Bubbles?.Stop();
            
            // Restore original settings
            RestoreSettings();
            
            // Fire events
            SessionStopped?.Invoke(this, EventArgs.Empty);
            
            if (completed && _currentSession != null)
            {
                SessionCompleted?.Invoke(this, new SessionCompletedEventArgs(
                    _currentSession,
                    ElapsedTime,
                    _currentSession.BonusXP
                ));
                
                App.Logger?.Information("Session completed: {Name}, XP: {XP}", 
                    _currentSession.Name, _currentSession.BonusXP);
                
                // Track achievement for session completion
                // Use App.Settings.Current for panic/strict lock as these are app-level settings
                var appSettings = App.Settings.Current;
                App.Achievements?.TrackSessionComplete(
                    _currentSession.Name,
                    ElapsedTime.TotalMinutes,
                    !appSettings.PanicKeyEnabled, // No panic = disabled
                    appSettings.StrictLockEnabled
                );
            }
            else
            {
                App.Logger?.Information("Session stopped early");
                
                // Track panic button press for Relapse achievement
                App.Achievements?.TrackPanicPressed();
            }
            
            _currentSession = null;
        }
        
        private void MainTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isRunning || _currentSession == null) return;
            
            var elapsed = ElapsedTime;
            var elapsedMinutes = elapsed.TotalMinutes;
            var totalMinutes = _currentSession.DurationMinutes;
            var progress = elapsedMinutes / totalMinutes;
            
            // Check if session is complete
            if (elapsedMinutes >= totalMinutes)
            {
                StopSession(completed: true);
                return;
            }
            
            // Update progress
            ProgressUpdated?.Invoke(this, new SessionProgressEventArgs(
                elapsed,
                RemainingTime,
                ProgressPercent
            ));
            
            // Check for phase changes
            CheckPhaseTransition(elapsedMinutes);
            
            // Update ramping values
            UpdateRampingValues(elapsedMinutes, totalMinutes);
            
            // Check for delayed feature starts
            CheckDelayedFeatures(elapsedMinutes);
            
            // Handle intermittent bubbles
            HandleIntermittentBubbles(elapsedMinutes);
        }
        
        private void CheckPhaseTransition(double elapsedMinutes)
        {
            if (_currentSession?.Phases == null) return;
            
            // Find which phase we should be in
            int newPhaseIndex = 0;
            for (int i = _currentSession.Phases.Count - 1; i >= 0; i--)
            {
                if (elapsedMinutes >= _currentSession.Phases[i].StartMinute)
                {
                    newPhaseIndex = i;
                    break;
                }
            }
            
            if (newPhaseIndex != _currentPhaseIndex)
            {
                _currentPhaseIndex = newPhaseIndex;
                var phase = _currentSession.Phases[newPhaseIndex];
                PhaseChanged?.Invoke(this, new SessionPhaseChangedEventArgs(phase, newPhaseIndex));
                App.Logger?.Information("Phase changed: {Phase}", phase.Name);
            }
        }
        
        private void UpdateRampingValues(double elapsedMinutes, double totalMinutes)
        {
            if (_currentSession == null) return;
            var settings = _currentSession.Settings;
            var progress = elapsedMinutes / totalMinutes;
            
            // Flash opacity ramp
            if (settings.FlashEnabled && settings.FlashOpacity != settings.FlashOpacityEnd)
            {
                _currentFlashOpacity = Lerp(settings.FlashOpacity, settings.FlashOpacityEnd, progress);
                App.Settings.Current.FlashOpacity = (int)_currentFlashOpacity;
            }
            
            // Flash frequency ramp (for sessions like Good Girls Don't Cum)
            if (settings.FlashEnabled && settings.FlashPerHour != settings.FlashPerHourEnd)
            {
                var currentFreq = Lerp(settings.FlashPerHour, settings.FlashPerHourEnd, progress);
                App.Settings.Current.FlashFrequency = (int)currentFreq;
            }
            
            // Flash scale (apply once at start if set)
            if (settings.FlashEnabled && settings.FlashScale != 100)
            {
                App.Settings.Current.ImageScale = settings.FlashScale;
            }
            
            // Pink filter ramp (only after randomized start minute)
            if (settings.PinkFilterEnabled && elapsedMinutes >= _randomizedPinkStartMinute)
            {
                var pinkDuration = totalMinutes - _randomizedPinkStartMinute;
                var pinkProgress = (elapsedMinutes - _randomizedPinkStartMinute) / pinkDuration;
                pinkProgress = Math.Clamp(pinkProgress, 0, 1);
                _currentPinkOpacity = Lerp(settings.PinkFilterStartOpacity, settings.PinkFilterEndOpacity, pinkProgress);
                // Apply to overlay service
                App.Settings.Current.PinkFilterOpacity = (int)_currentPinkOpacity;
                _mainWindow.UpdatePinkFilterOpacity((int)_currentPinkOpacity);
            }
            
            // Spiral ramp (only after randomized start minute)
            if (settings.SpiralEnabled && elapsedMinutes >= _randomizedSpiralStartMinute)
            {
                var spiralDuration = totalMinutes - _randomizedSpiralStartMinute;
                var spiralProgress = (elapsedMinutes - _randomizedSpiralStartMinute) / spiralDuration;
                spiralProgress = Math.Clamp(spiralProgress, 0, 1);
                _currentSpiralOpacity = Lerp(settings.SpiralOpacity, settings.SpiralOpacityEnd, spiralProgress);
                // Apply to overlay service
                App.Settings.Current.SpiralOpacity = (int)_currentSpiralOpacity;
                _mainWindow.UpdateSpiralOpacity((int)_currentSpiralOpacity);
            }

            // Bubble frequency ramp
            if (settings.BubblesEnabled && !settings.BubblesIntermittent && settings.BubblesStartMinute > 0)
            {
                if (elapsedMinutes >= settings.BubblesStartMinute)
                {
                    var timeSinceBubbleStart = elapsedMinutes - settings.BubblesStartMinute;
                    var rampSteps = (int)(timeSinceBubbleStart / 5);
                    var currentBubbleFreq = settings.BubblesFrequency + rampSteps;

                    if (App.Settings.Current.BubblesFrequency != currentBubbleFreq)
                    {
                        App.Settings.Current.BubblesFrequency = currentBubbleFreq;
                        App.Bubbles.RefreshFrequency();
                    }
                }
            }
        }
        
        private void CheckDelayedFeatures(double elapsedMinutes)
        {
            if (_currentSession == null) return;
            var settings = _currentSession.Settings;
            
            // Pink filter delayed start (use randomized time)
            if (settings.PinkFilterEnabled && !App.Settings.Current.PinkFilterEnabled)
            {
                if (elapsedMinutes >= _randomizedPinkStartMinute)
                {
                    App.Settings.Current.PinkFilterEnabled = true;
                    _mainWindow.EnablePinkFilter(true);
                    App.Logger?.Information("Pink filter activated at {Minutes:F1} minutes (target was {Target:F1})", 
                        elapsedMinutes, _randomizedPinkStartMinute);
                }
            }
            
            // Spiral delayed start (use randomized time)
            if (settings.SpiralEnabled && !App.Settings.Current.SpiralEnabled)
            {
                // Check if spiral path exists OR if there are spirals in Spirals folder
                var spiralsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Spirals");
                var hasUserSpiral = !string.IsNullOrEmpty(App.Settings.Current.SpiralPath) && 
                                   File.Exists(App.Settings.Current.SpiralPath);
                var hasRandomSpirals = Directory.Exists(spiralsFolder) && 
                                       Directory.GetFiles(spiralsFolder, "*.gif").Length > 0;
                
                if (!hasUserSpiral && !hasRandomSpirals)
                {
                    App.Logger?.Warning("Spiral enabled in session but no spiral files found - skipping");
                    // Disable in session to prevent repeated warnings
                    settings.SpiralEnabled = false;
                    return;
                }
                
                if (elapsedMinutes >= _randomizedSpiralStartMinute)
                {
                    App.Settings.Current.SpiralEnabled = true;
                    _mainWindow.EnableSpiral(true);
                    App.Logger?.Information("Spiral activated at {Minutes:F1} minutes (target was {Target:F1})", 
                        elapsedMinutes, _randomizedSpiralStartMinute);
                }
            }

            // Bubbles delayed start
            if (settings.BubblesEnabled && !App.Settings.Current.BubblesEnabled && settings.BubblesStartMinute > 0 && !settings.BubblesIntermittent)
            {
                if (elapsedMinutes >= settings.BubblesStartMinute)
                {
                    App.Settings.Current.BubblesEnabled = true;
                    if(App.Settings.Current.PlayerLevel >= 20)
                        App.Bubbles.Start();
                }
            }
        }
        
        /// <summary>
        /// Randomizes delayed start times by ±3 minutes (clamped to valid range)
        /// </summary>
        private void RandomizeStartTimes(Session session)
        {
            var settings = session.Settings;
            
            // Randomize pink filter start (±3 min, min 0)
            if (settings.PinkFilterEnabled && settings.PinkFilterStartMinute > 0)
            {
                var offset = (_random.NextDouble() * 6) - 3; // -3 to +3
                _randomizedPinkStartMinute = Math.Max(0, settings.PinkFilterStartMinute + offset);
            }
            else
            {
                _randomizedPinkStartMinute = settings.PinkFilterStartMinute;
            }
            
            // Randomize spiral start (±3 min, min 0)
            if (settings.SpiralEnabled && settings.SpiralStartMinute > 0)
            {
                var offset = (_random.NextDouble() * 6) - 3; // -3 to +3
                _randomizedSpiralStartMinute = Math.Max(0, settings.SpiralStartMinute + offset);
            }
            else
            {
                _randomizedSpiralStartMinute = settings.SpiralStartMinute;
            }
            
            App.Logger?.Debug("Randomized start times - Pink: {Pink:F1}min, Spiral: {Spiral:F1}min",
                _randomizedPinkStartMinute, _randomizedSpiralStartMinute);
        }
        
        private void ScheduleBubbleBursts(Session session)
        {
            _scheduledBubbleBursts.Clear();
            _bubbleBurstIndex = 0;
            
            var settings = session.Settings;
            var totalMinutes = session.DurationMinutes;
            var burstCount = settings.BubblesBurstCount;
            
            // Distribute bursts randomly but with minimum gaps
            var minGap = settings.BubblesGapMin;
            var maxGap = settings.BubblesGapMax;
            
            double currentTime = _random.Next(2, 5); // Start after 2-5 minutes
            
            for (int i = 0; i < burstCount && currentTime < totalMinutes - 2; i++)
            {
                _scheduledBubbleBursts.Add(currentTime);
                currentTime += _random.Next(minGap, maxGap + 1);
            }
            
            App.Logger?.Information("Scheduled {Count} bubble bursts: {Times}", 
                _scheduledBubbleBursts.Count, 
                string.Join(", ", _scheduledBubbleBursts.Select(t => $"{t:F1}min")));
        }
        
        private void HandleIntermittentBubbles(double elapsedMinutes)
        {
            if (_currentSession == null || !_currentSession.Settings.BubblesEnabled) return;
            if (!_currentSession.Settings.BubblesIntermittent) return;
            
            // Check if we need to end current burst
            if (_bubblesCurrentlyActive && DateTime.Now >= _bubbleBurstEndTime)
            {
                _bubblesCurrentlyActive = false;
                _mainWindow.SetBubblesActive(false);
                App.Logger?.Information("Bubble burst ended");
            }
            
            // Check if we should start a new burst
            if (!_bubblesCurrentlyActive && _bubbleBurstIndex < _scheduledBubbleBursts.Count)
            {
                var nextBurstTime = _scheduledBubbleBursts[_bubbleBurstIndex];
                if (elapsedMinutes >= nextBurstTime)
                {
                    // Start burst
                    _bubblesCurrentlyActive = true;
                    var burstDuration = _random.Next(1, 3); // 1-2 minutes
                    _bubbleBurstEndTime = DateTime.Now.AddMinutes(burstDuration);
                    _bubbleBurstIndex++;
                    
                    _mainWindow.SetBubblesActive(true, _currentSession.Settings.BubblesPerBurst);
                    App.Logger?.Information("Bubble burst started, duration: {Duration}min", burstDuration);
                }
            }
        }
        
        private void SaveCurrentSettings()
        {
            // Clone current settings
            _savedSettings = new AppSettings();
            var current = App.Settings.Current;
            
            // Save all relevant settings
            _savedSettings.FlashEnabled = current.FlashEnabled;
            _savedSettings.FlashFrequency = current.FlashFrequency;
            _savedSettings.FlashOpacity = current.FlashOpacity;
            _savedSettings.FlashClickable = current.FlashClickable;
            _savedSettings.FlashAudioEnabled = current.FlashAudioEnabled;
            _savedSettings.ImageScale = current.ImageScale;
            
            _savedSettings.SubliminalEnabled = current.SubliminalEnabled;
            _savedSettings.SubliminalFrequency = current.SubliminalFrequency;
            _savedSettings.SubliminalOpacity = current.SubliminalOpacity;
            
            _savedSettings.SubAudioEnabled = current.SubAudioEnabled;
            _savedSettings.SubAudioVolume = current.SubAudioVolume;
            
            _savedSettings.AudioDuckingEnabled = current.AudioDuckingEnabled;
            _savedSettings.DuckingLevel = current.DuckingLevel;
            
            _savedSettings.PinkFilterEnabled = current.PinkFilterEnabled;
            _savedSettings.PinkFilterOpacity = current.PinkFilterOpacity;
            
            _savedSettings.SpiralEnabled = current.SpiralEnabled;
            _savedSettings.SpiralOpacity = current.SpiralOpacity;
            
            _savedSettings.BubblesEnabled = current.BubblesEnabled;
            _savedSettings.BubblesFrequency = current.BubblesFrequency;
            
            _savedSettings.BouncingTextEnabled = current.BouncingTextEnabled;
            _savedSettings.BouncingTextSpeed = current.BouncingTextSpeed;
            
            // Save bouncing text pool (deep copy)
            _savedBouncingTextPool = new Dictionary<string, bool>(current.BouncingTextPool);
            
            _savedSettings.MandatoryVideosEnabled = current.MandatoryVideosEnabled;
            _savedSettings.VideosPerHour = current.VideosPerHour;
            _savedSettings.LockCardEnabled = current.LockCardEnabled;
            _savedSettings.LockCardFrequency = current.LockCardFrequency;
            _savedSettings.BubbleCountEnabled = current.BubbleCountEnabled;
            _savedSettings.BubbleCountFrequency = current.BubbleCountFrequency;
        }
        
        private Dictionary<string, bool>? _savedBouncingTextPool;
        
        private void ApplySessionSettings(SessionSettings settings)
        {
            var current = App.Settings.Current;
            
            // Flash Images
            current.FlashEnabled = settings.FlashEnabled;
            if (settings.FlashEnabled)
            {
                current.FlashFrequency = settings.FlashPerHour;
                current.FlashOpacity = settings.FlashOpacity;
                current.SimultaneousImages = settings.FlashImages;
                current.FlashClickable = settings.FlashClickable;
                current.FlashAudioEnabled = settings.FlashAudioEnabled;
            }
            
            // Subliminals
            current.SubliminalEnabled = settings.SubliminalEnabled;
            if (settings.SubliminalEnabled)
            {
                current.SubliminalFrequency = settings.SubliminalPerMin;
                current.SubliminalOpacity = settings.SubliminalOpacity;
                current.SubliminalDuration = settings.SubliminalFrames;
            }
            
            // Audio Whispers (Sub Audio)
            current.SubAudioEnabled = settings.AudioWhispersEnabled;
            if (settings.AudioWhispersEnabled)
            {
                current.SubAudioVolume = settings.WhisperVolume;
            }
            
            // Audio Ducking - apply session-specific duck level
            if (settings.AudioDuckLevel > 0)
            {
                current.AudioDuckingEnabled = true;
                current.DuckingLevel = settings.AudioDuckLevel;
            }
            
            // Bouncing Text - override phrases with session-specific ones
            current.BouncingTextEnabled = settings.BouncingTextEnabled;
            if (settings.BouncingTextEnabled)
            {
                current.BouncingTextSpeed = settings.BouncingTextSpeed;
                
                // Override the bouncing text pool with session phrases
                if (settings.BouncingTextPhrases.Count > 0)
                {
                    // Disable all existing phrases
                    var keys = current.BouncingTextPool.Keys.ToList();
                    foreach (var key in keys)
                    {
                        current.BouncingTextPool[key] = false;
                    }
                    
                    // Add/enable session phrases
                    foreach (var phrase in settings.BouncingTextPhrases)
                    {
                        current.BouncingTextPool[phrase] = true;
                    }
                }
                
                // Start bouncing text if level requirement met
                if (current.PlayerLevel >= 60)
                {
                    App.BouncingText.Stop(); // Stop first to reset state
                    App.BouncingText.Start();
                    App.Logger?.Information("Session: Started bouncing text with phrases: {Phrases}", 
                        string.Join(", ", settings.BouncingTextPhrases));
                }
            }
            else
            {
                // Stop bouncing text if session disables it
                App.BouncingText.Stop();
            }
            
            // Pink Filter (delayed start - don't enable yet if delayed)
            if (settings.PinkFilterEnabled && settings.PinkFilterStartMinute == 0)
            {
                current.PinkFilterEnabled = true;
                current.PinkFilterOpacity = settings.PinkFilterStartOpacity;
            }
            else
            {
                current.PinkFilterEnabled = false;
            }
            
            // Spiral (delayed start - don't enable yet if delayed)
            if (settings.SpiralEnabled && settings.SpiralStartMinute == 0)
            {
                current.SpiralEnabled = true;
                current.SpiralOpacity = settings.SpiralOpacity;
            }
            else
            {
                current.SpiralEnabled = false;
            }
            
            // Bubbles
            if (settings.BubblesEnabled)
            {
                current.BubblesFrequency = settings.BubblesFrequency;
                // Start immediately if no start minute is set. Otherwise, CheckDelayedFeatures will handle it.
                current.BubblesEnabled = settings.BubblesStartMinute == 0 && !settings.BubblesIntermittent;
            }
            else
            {
                current.BubblesEnabled = false;
            }
            
            // Interactive Features
            current.MandatoryVideosEnabled = settings.MandatoryVideosEnabled;
            if (settings.VideosPerHour.HasValue)
            {
                current.VideosPerHour = settings.VideosPerHour.Value;
            }

            current.LockCardEnabled = settings.LockCardEnabled;
            if (settings.LockCardFrequency.HasValue)
            {
                current.LockCardFrequency = settings.LockCardFrequency.Value;
            }

            current.BubbleCountEnabled = settings.BubbleCountEnabled;
            if (settings.BubbleCountFrequency.HasValue)
            {
                current.BubbleCountFrequency = settings.BubbleCountFrequency.Value;
            }
            
            // Apply settings to UI
            _mainWindow.ApplySessionSettings();
        }
        
        private void RestoreSettings()
        {
            if (_savedSettings == null) return;
            
            var current = App.Settings.Current;
            
            current.FlashEnabled = _savedSettings.FlashEnabled;
            current.FlashFrequency = _savedSettings.FlashFrequency;
            current.FlashOpacity = _savedSettings.FlashOpacity;
            current.FlashClickable = _savedSettings.FlashClickable;
            current.FlashAudioEnabled = _savedSettings.FlashAudioEnabled;
            current.ImageScale = _savedSettings.ImageScale;
            
            current.SubliminalEnabled = _savedSettings.SubliminalEnabled;
            current.SubliminalFrequency = _savedSettings.SubliminalFrequency;
            current.SubliminalOpacity = _savedSettings.SubliminalOpacity;
            
            current.SubAudioEnabled = _savedSettings.SubAudioEnabled;
            current.SubAudioVolume = _savedSettings.SubAudioVolume;
            
            current.AudioDuckingEnabled = _savedSettings.AudioDuckingEnabled;
            current.DuckingLevel = _savedSettings.DuckingLevel;
            
            current.PinkFilterEnabled = _savedSettings.PinkFilterEnabled;
            current.PinkFilterOpacity = _savedSettings.PinkFilterOpacity;
            
            current.SpiralEnabled = _savedSettings.SpiralEnabled;
            current.SpiralOpacity = _savedSettings.SpiralOpacity;
            
            current.BubblesEnabled = _savedSettings.BubblesEnabled;
            current.BubblesFrequency = _savedSettings.BubblesFrequency;
            
            current.BouncingTextEnabled = _savedSettings.BouncingTextEnabled;
            current.BouncingTextSpeed = _savedSettings.BouncingTextSpeed;
            
            // Restore bouncing text pool
            if (_savedBouncingTextPool != null)
            {
                current.BouncingTextPool.Clear();
                foreach (var kvp in _savedBouncingTextPool)
                {
                    current.BouncingTextPool[kvp.Key] = kvp.Value;
                }
                _savedBouncingTextPool = null;
            }
            
            current.MandatoryVideosEnabled = _savedSettings.MandatoryVideosEnabled;
            current.VideosPerHour = _savedSettings.VideosPerHour;
            current.LockCardEnabled = _savedSettings.LockCardEnabled;
            current.LockCardFrequency = _savedSettings.LockCardFrequency;
            current.BubbleCountEnabled = _savedSettings.BubbleCountEnabled;
            current.BubbleCountFrequency = _savedSettings.BubbleCountFrequency;
            
            // Apply restored settings to UI
            _mainWindow.ApplySessionSettings();
            
            _savedSettings = null;
        }
        
        private void ShowCornerGif(SessionSettings settings)
        {
            try
            {
                var gifPath = settings.CornerGifPath;
                if (string.IsNullOrEmpty(gifPath) || !System.IO.File.Exists(gifPath))
                {
                    // Fallback to spiral.gif
                    gifPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "spiral.gif");
                    App.Logger?.Information("Corner GIF not set, defaulting to spiral.gif");
                    
                    if (!System.IO.File.Exists(gifPath))
                    {
                        App.Logger?.Warning("Default spiral.gif not found at {Path}", gifPath);
                        return; // Exit if default is also not found
                    }
                }
                
                var gifSize = 500; // User requested size
                var margin = 0; // Margin from screen edge, set to 0 to be as close as possible
                
                // Use WPF's SystemParameters for accurate screen dimensions
                var screenWidth = SystemParameters.PrimaryScreenWidth;
                var screenHeight = SystemParameters.PrimaryScreenHeight;
                var workAreaHeight = SystemParameters.WorkArea.Height;
                
                double left = 0, top = 0;
                switch (settings.CornerGifPosition)
                {
                    case CornerPosition.TopLeft:
                        left = margin;
                        top = margin;
                        break;
                    case CornerPosition.TopRight:
                        left = screenWidth - gifSize - margin;
                        top = margin;
                        break;
                    case CornerPosition.BottomLeft:
                        left = margin;
                        top = workAreaHeight - gifSize - margin;
                        break;
                    case CornerPosition.BottomRight:
                        left = screenWidth - gifSize - margin;
                        top = workAreaHeight - gifSize - margin;
                        break;
                }
                
                // Increase opacity by 20%
                var opacity = Math.Min(100, settings.CornerGifOpacity + 20) / 100.0;

                _cornerGifWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Width = gifSize,
                    Height = gifSize,
                    Left = left,
                    Top = top,
                    Opacity = opacity
                };
                
                // Use MediaElement for GIF animation (better than WebBrowser for transparency)
                var mediaElement = new System.Windows.Controls.MediaElement
                {
                    Source = new Uri(gifPath),
                    LoadedBehavior = System.Windows.Controls.MediaState.Play,
                    UnloadedBehavior = System.Windows.Controls.MediaState.Manual,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Width = gifSize,
                    Height = gifSize
                };
                
                // Loop the GIF
                mediaElement.MediaEnded += (s, e) =>
                {
                    mediaElement.Stop();
                    mediaElement.Position = TimeSpan.Zero;
                    mediaElement.Play();
                };
                
                _cornerGifWindow.Content = mediaElement;
                _cornerGifWindow.Show();
                
                // Make click-through
                MakeWindowClickThrough(_cornerGifWindow);
                
                App.Logger?.Information("Corner GIF shown at {Position}: {Path} (pos: {Left},{Top}, size: {Size}px, opacity: {Opacity}%)", 
                    settings.CornerGifPosition, gifPath, left, top, gifSize, settings.CornerGifOpacity);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to show corner GIF");
            }
        }
        
        private void CloseCornerGif()
        {
            if (_cornerGifWindow != null)
            {
                _cornerGifWindow.Close();
                _cornerGifWindow = null;
            }
        }
        
        private void MakeWindowClickThrough(Window window)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }
        
        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * Math.Clamp(t, 0, 1);
        }
        
        // P/Invoke for click-through
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
        
        public void Dispose()
        {
            StopSession(false);
            _cancellationToken?.Dispose();
        }
    }
    
    #region Event Args
    
    public class SessionPhaseChangedEventArgs : EventArgs
    {
        public SessionPhase Phase { get; }
        public int PhaseIndex { get; }
        
        public SessionPhaseChangedEventArgs(SessionPhase phase, int index)
        {
            Phase = phase;
            PhaseIndex = index;
        }
    }
    
    public class SessionProgressEventArgs : EventArgs
    {
        public TimeSpan Elapsed { get; }
        public TimeSpan Remaining { get; }
        public double ProgressPercent { get; }
        
        public SessionProgressEventArgs(TimeSpan elapsed, TimeSpan remaining, double percent)
        {
            Elapsed = elapsed;
            Remaining = remaining;
            ProgressPercent = percent;
        }
    }
    
    public class SessionCompletedEventArgs : EventArgs
    {
        public Session Session { get; }
        public TimeSpan Duration { get; }
        public int XPEarned { get; }
        
        public SessionCompletedEventArgs(Session session, TimeSpan duration, int xp)
        {
            Session = session;
            Duration = duration;
            XPEarned = xp;
        }
    }
    
    #endregion
}
