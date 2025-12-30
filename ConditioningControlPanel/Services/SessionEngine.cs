using System;
using System.Collections.Generic;
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
            if (session.Settings.CornerGifEnabled && !string.IsNullOrEmpty(session.Settings.CornerGifPath))
            {
                ShowCornerGif(session.Settings);
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
            }
            else
            {
                App.Logger?.Information("Session stopped early");
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
            
            // Flash opacity ramp
            if (settings.FlashEnabled && settings.FlashOpacity != settings.FlashOpacityEnd)
            {
                var progress = elapsedMinutes / totalMinutes;
                _currentFlashOpacity = Lerp(settings.FlashOpacity, settings.FlashOpacityEnd, progress);
                // Apply to flash service
                App.Settings.Current.FlashOpacity = (int)_currentFlashOpacity;
            }
            
            // Pink filter ramp (only after start minute)
            if (settings.PinkFilterEnabled && elapsedMinutes >= settings.PinkFilterStartMinute)
            {
                var pinkDuration = totalMinutes - settings.PinkFilterStartMinute;
                var pinkProgress = (elapsedMinutes - settings.PinkFilterStartMinute) / pinkDuration;
                pinkProgress = Math.Clamp(pinkProgress, 0, 1);
                _currentPinkOpacity = Lerp(settings.PinkFilterStartOpacity, settings.PinkFilterEndOpacity, pinkProgress);
                // Apply to overlay service
                App.Settings.Current.PinkFilterOpacity = (int)_currentPinkOpacity;
                _mainWindow.UpdatePinkFilterOpacity((int)_currentPinkOpacity);
            }
            
            // Spiral ramp (only after start minute)
            if (settings.SpiralEnabled && elapsedMinutes >= settings.SpiralStartMinute)
            {
                var spiralDuration = totalMinutes - settings.SpiralStartMinute;
                var spiralProgress = (elapsedMinutes - settings.SpiralStartMinute) / spiralDuration;
                spiralProgress = Math.Clamp(spiralProgress, 0, 1);
                _currentSpiralOpacity = Lerp(settings.SpiralOpacity, settings.SpiralOpacityEnd, spiralProgress);
                // Apply to overlay service
                App.Settings.Current.SpiralOpacity = (int)_currentSpiralOpacity;
                _mainWindow.UpdateSpiralOpacity((int)_currentSpiralOpacity);
            }
        }
        
        private void CheckDelayedFeatures(double elapsedMinutes)
        {
            if (_currentSession == null) return;
            var settings = _currentSession.Settings;
            
            // Pink filter delayed start
            if (settings.PinkFilterEnabled && !App.Settings.Current.PinkFilterEnabled)
            {
                if (elapsedMinutes >= settings.PinkFilterStartMinute)
                {
                    App.Settings.Current.PinkFilterEnabled = true;
                    _mainWindow.EnablePinkFilter(true);
                    App.Logger?.Information("Pink filter activated at {Minutes} minutes", elapsedMinutes);
                }
            }
            
            // Spiral delayed start
            if (settings.SpiralEnabled && !App.Settings.Current.SpiralEnabled)
            {
                if (elapsedMinutes >= settings.SpiralStartMinute)
                {
                    App.Settings.Current.SpiralEnabled = true;
                    _mainWindow.EnableSpiral(true);
                    App.Logger?.Information("Spiral activated at {Minutes} minutes", elapsedMinutes);
                }
            }
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
            
            _savedSettings.SubliminalEnabled = current.SubliminalEnabled;
            _savedSettings.SubliminalFrequency = current.SubliminalFrequency;
            _savedSettings.SubliminalOpacity = current.SubliminalOpacity;
            
            _savedSettings.SubAudioEnabled = current.SubAudioEnabled;
            _savedSettings.SubAudioVolume = current.SubAudioVolume;
            
            _savedSettings.PinkFilterEnabled = current.PinkFilterEnabled;
            _savedSettings.PinkFilterOpacity = current.PinkFilterOpacity;
            
            _savedSettings.SpiralEnabled = current.SpiralEnabled;
            _savedSettings.SpiralOpacity = current.SpiralOpacity;
            
            _savedSettings.BubblesEnabled = current.BubblesEnabled;
            _savedSettings.BubblesFrequency = current.BubblesFrequency;
            
            _savedSettings.BouncingTextEnabled = current.BouncingTextEnabled;
            _savedSettings.BouncingTextSpeed = current.BouncingTextSpeed;
            
            _savedSettings.MandatoryVideosEnabled = current.MandatoryVideosEnabled;
            _savedSettings.LockCardEnabled = current.LockCardEnabled;
            _savedSettings.BubbleCountEnabled = current.BubbleCountEnabled;
        }
        
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
            
            // Bouncing Text
            current.BouncingTextEnabled = settings.BouncingTextEnabled;
            if (settings.BouncingTextEnabled)
            {
                current.BouncingTextSpeed = settings.BouncingTextSpeed;
                // Note: Phrases are stored in BouncingTextPool dictionary, not a simple list
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
            
            // Bubbles (handled separately for intermittent mode)
            current.BubblesEnabled = settings.BubblesEnabled && !settings.BubblesIntermittent;
            
            // Disabled features for this session
            current.MandatoryVideosEnabled = settings.MandatoryVideosEnabled;
            current.LockCardEnabled = settings.LockCardEnabled;
            current.BubbleCountEnabled = settings.BubbleCountEnabled;
            
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
            
            current.SubliminalEnabled = _savedSettings.SubliminalEnabled;
            current.SubliminalFrequency = _savedSettings.SubliminalFrequency;
            current.SubliminalOpacity = _savedSettings.SubliminalOpacity;
            
            current.SubAudioEnabled = _savedSettings.SubAudioEnabled;
            current.SubAudioVolume = _savedSettings.SubAudioVolume;
            
            current.PinkFilterEnabled = _savedSettings.PinkFilterEnabled;
            current.PinkFilterOpacity = _savedSettings.PinkFilterOpacity;
            
            current.SpiralEnabled = _savedSettings.SpiralEnabled;
            current.SpiralOpacity = _savedSettings.SpiralOpacity;
            
            current.BubblesEnabled = _savedSettings.BubblesEnabled;
            current.BubblesFrequency = _savedSettings.BubblesFrequency;
            
            current.BouncingTextEnabled = _savedSettings.BouncingTextEnabled;
            current.BouncingTextSpeed = _savedSettings.BouncingTextSpeed;
            
            current.MandatoryVideosEnabled = _savedSettings.MandatoryVideosEnabled;
            current.LockCardEnabled = _savedSettings.LockCardEnabled;
            current.BubbleCountEnabled = _savedSettings.BubbleCountEnabled;
            
            // Apply restored settings to UI
            _mainWindow.ApplySessionSettings();
            
            _savedSettings = null;
        }
        
        private void ShowCornerGif(SessionSettings settings)
        {
            try
            {
                if (string.IsNullOrEmpty(settings.CornerGifPath) || !System.IO.File.Exists(settings.CornerGifPath))
                {
                    App.Logger?.Warning("Corner GIF path is empty or file doesn't exist: {Path}", settings.CornerGifPath);
                    return;
                }
                
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) return;
                
                var gifSize = 120; // Size of corner GIF
                
                double left = 0, top = 0;
                switch (settings.CornerGifPosition)
                {
                    case CornerPosition.TopLeft:
                        left = 10;
                        top = 10;
                        break;
                    case CornerPosition.TopRight:
                        left = screen.WorkingArea.Width - gifSize - 10;
                        top = 10;
                        break;
                    case CornerPosition.BottomLeft:
                        left = 10;
                        top = screen.WorkingArea.Height - gifSize - 10;
                        break;
                    case CornerPosition.BottomRight:
                        left = screen.WorkingArea.Width - gifSize - 10;
                        top = screen.WorkingArea.Height - gifSize - 10;
                        break;
                }
                
                _cornerGifWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Width = gifSize,
                    Height = gifSize,
                    Left = left,
                    Top = top,
                    Opacity = settings.CornerGifOpacity / 100.0
                };
                
                // Use WebBrowser control for GIF animation (works better than Image)
                var browser = new System.Windows.Controls.WebBrowser();
                var html = $@"
                    <html>
                    <head><style>
                        body {{ margin: 0; padding: 0; overflow: hidden; background: transparent; }}
                        img {{ width: 100%; height: 100%; object-fit: contain; }}
                    </style></head>
                    <body>
                        <img src='file:///{settings.CornerGifPath.Replace("\\", "/")}' />
                    </body>
                    </html>";
                
                _cornerGifWindow.Content = browser;
                _cornerGifWindow.Loaded += (s, e) =>
                {
                    browser.NavigateToString(html);
                };
                
                _cornerGifWindow.Show();
                
                // Make click-through
                MakeWindowClickThrough(_cornerGifWindow);
                
                App.Logger?.Information("Corner GIF shown at {Position}: {Path}", settings.CornerGifPosition, settings.CornerGifPath);
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
