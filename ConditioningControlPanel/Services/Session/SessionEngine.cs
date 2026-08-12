using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using XamlAnimatedGif;

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
        private bool _isPaused;
        private int _pauseCount;
        private TimeSpan _pausedElapsedTime; // Time accumulated before pause
        private DateTime _startTime;
        private DateTime _pauseStartTime;
        private DispatcherTimer? _mainTimer;
        private DispatcherTimer? _phaseTimer;
        private int _currentPhaseIndex;
        private CancellationTokenSource? _cancellationToken;
        
        // Saved settings (to restore after session)
        private AppSettings? _savedSettings;

        // Capture achievement-relevant settings at session start (immune to mid-session changes)
        private bool _sessionStartStrictLock;
        private bool _sessionStartPanicKey;
        
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
        private bool _brainDrainActive;

        // Randomized start times (±3 min from session defaults)
        private double _randomizedPinkStartMinute;
        private double _randomizedSpiralStartMinute;
        
        // Corner GIF window (for Gamer Girl session)
        private Window? _cornerGifWindow;
        private Image? _cornerGifImage;
        private double _cornerGifWidth;  // Cached original GIF dimensions
        private double _cornerGifHeight;

        // Anti-cheat: Stopwatch for wall-clock cross-check (immune to system clock manipulation)
        private readonly Stopwatch _wallClockStopwatch = new();

        // Reference to main window for service access
        private readonly MainWindow _mainWindow;

        public bool IsRunning => _isRunning;

        /// <summary>
        /// Safely check if main window is still valid and available
        /// </summary>
        private bool IsMainWindowValid => _mainWindow != null && _mainWindow.IsLoaded;
        public bool IsPaused => _isPaused;
        public int CurrentPhaseIndex => _currentPhaseIndex;
        public int PauseCount => _pauseCount;
        public int XPPenalty => _pauseCount * 100; // 100 XP per pause
        public Session? CurrentSession => _currentSession;
        public TimeSpan ElapsedTime
        {
            get
            {
                if (!_isRunning) return TimeSpan.Zero;
                if (_isPaused) return _pausedElapsedTime;

                var dateTimeElapsed = _pausedElapsedTime + (DateTime.Now - _startTime);
                var stopwatchElapsed = _wallClockStopwatch.Elapsed;

                // Anti-cheat / clock-jump guard: if DateTime-based elapsed diverges from the
                // monotonic Stopwatch by more than 30s in EITHER direction, trust the Stopwatch.
                // A positive divergence guards against speed-hacking; a negative divergence guards
                // against a backward wall-clock jump (DST/NTP/sleep-resume), which otherwise makes
                // RemainingTime balloon (e.g. "149 minutes left" on a 30-minute session). See #369.
                var divergence = dateTimeElapsed - stopwatchElapsed;
                if (Math.Abs(divergence.TotalSeconds) > 30)
                {
                    App.Logger?.Warning("Timer integrity: DateTime elapsed {DateTimeElapsed} vs Stopwatch {StopwatchElapsed} — divergence {Divergence}s, using Stopwatch",
                        dateTimeElapsed, stopwatchElapsed, divergence.TotalSeconds);
                    return stopwatchElapsed;
                }

                // Never report negative elapsed time.
                return dateTimeElapsed < TimeSpan.Zero ? TimeSpan.Zero : dateTimeElapsed;
            }
        }
        public TimeSpan RemainingTime
        {
            get
            {
                if (_currentSession == null) return TimeSpan.Zero;
                var remaining = TimeSpan.FromMinutes(_currentSession.DurationMinutes) - ElapsedTime;
                return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
            }
        }
        public double ProgressPercent => _currentSession != null
            ? Math.Min(100, (ElapsedTime.TotalMinutes / _currentSession.DurationMinutes) * 100)
            : 0;

        /// <summary>
        /// #736: minutes genuinely left in the running session, for features that must fit their
        /// schedule inside it (see <see cref="LockCardService.Start"/>). Null when no session is
        /// running, which callers read as "open-ended".
        /// </summary>
        private double? SessionWindowMinutes =>
            _currentSession == null ? null : RemainingTime.TotalMinutes;
        
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
            // Published so ModService can ask who owns the phrase pools. Safe to set before the
            // pools are actually overridden: IsOverridingPhrasePools stays false until
            // RememberPrescribedPools runs below.
            Active = this;
            _isPaused = false;
            _pauseCount = 0;
            _pausedElapsedTime = TimeSpan.Zero;
            _startTime = DateTime.Now;
            _wallClockStopwatch.Restart();
            _currentPhaseIndex = 0;
            _cancellationToken = new CancellationTokenSource();
            
            // Save current settings to restore later
            SaveCurrentSettings();

            // Capture achievement-relevant settings NOW (before anything can modify them mid-session)
            _sessionStartStrictLock = App.Settings.Current.StrictLockEnabled;
            _sessionStartPanicKey = App.Settings.Current.PanicKeyEnabled;
            
            // Randomize delayed start times (±3 minutes from session defaults)
            RandomizeStartTimes(session);
            
            // Apply session settings
            ApplySessionSettings(session.Settings);
            // Record what the override just wrote, so a mid-session mod switch can neither
            // persist these phrases as the user's own nor strip them from the running session.
            RememberPrescribedPools();
            try { _modIdAtStart = App.Mods?.ActiveMod?.Id; } catch { _modIdAtStart = null; }
            
            // Schedule bubble bursts if enabled
            if (session.Settings.BubblesEnabled && session.Settings.BubblesIntermittent)
            {
                ScheduleBubbleBursts(session);
            }
            
            // Initialize ramp values
            _currentFlashOpacity = session.Settings.FlashOpacity;
            _currentPinkOpacity = session.Settings.PinkFilterStartOpacity;
            _currentSpiralOpacity = session.Settings.SpiralOpacity;
            
            // Setup corner GIF if enabled (respects start/end minute timers)
            if (session.Settings.CornerGifEnabled && session.Settings.CornerGifStartMinute == 0)
            {
                ShowCornerGif(session.Settings);
            }
            
            // Start Mind Wipe if enabled (escalating frequency)
            if (session.Settings.MindWipeEnabled)
            {
                if (session.Settings.MindWipeStartMinute == 0)
                {
                    App.MindWipe.Volume = session.Settings.MindWipeVolume / 100.0;
                    App.MindWipe.StartSession(session.Settings.MindWipeBaseMultiplier);
                }
                else
                {
                    var mw = session.Settings;
                    DeferFeatureStart("mind wipe", mw.MindWipeStartMinute, () =>
                    {
                        App.MindWipe.Volume = mw.MindWipeVolume / 100.0;
                        App.MindWipe.StartSession(mw.MindWipeBaseMultiplier);
                    });
                }
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

            // Update Discord presence with session name
            App.DiscordRpc?.SetSessionActivity(session.Name);

            // Track session start for achievements (e.g., Relapse)
            App.Achievements?.TrackSessionStart();

            // Season Recap (local-only): count this season's session, and record which
            // features were engaged — once per session, from the enabled flags, so pause/
            // resume can't double-count. Drives the card's session count + badge row.
            try
            {
                SeasonRecapService.IncrementSessionStarted();
                var ss = session.Settings;
                if (ss.FlashEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.Flash);
                if (ss.MandatoryVideosEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.Video);
                if (ss.SubliminalEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.Subliminal);
                if (ss.SpiralEnabled || ss.PinkFilterEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.Overlay);
                if (ss.BubblesEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.Bubbles);
                if (ss.BubbleCountEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.BubbleCount);
                if (ss.BouncingTextEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.BouncingText);
                if (ss.LockCardEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.LockCard);
                if (ss.MindWipeEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.MindWipe);
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SeasonRecap: failed to record session feature use");
            }

            // Begin capturing the post-session media log (videos played + flash images shown)
            App.SessionLog?.BeginSession(session);

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

            // Capture elapsed time BEFORE setting _isRunning to false
            // (ElapsedTime property returns Zero when not running)
            var finalElapsedTime = ElapsedTime;

            _isRunning = false;
            // Hand phrase-pool custody back to the user. Cleared here rather than at the end of
            // StopSession so nothing can read stale prescribed pools while teardown is in flight;
            // the reference check means a newer engine that has already published itself is not
            // clobbered by a late stop from an older one.
            _prescribedSubliminalPool = null;
            _prescribedBouncingTextPool = null;
            _prescribedLockCardPool = null;
            if (ReferenceEquals(Active, this)) Active = null;

            _wallClockStopwatch.Stop();
            _cancellationToken?.Cancel();

            // Stop timers
            _mainTimer?.Stop();
            _mainTimer = null;
            _phaseTimer?.Stop();
            _phaseTimer = null;

            // Drop any not-yet-fired deferred feature starts (#483)
            _pendingFeatureStarts.Clear();

            // Close corner GIF
            CloseCornerGif();
            
            // Stop Mind Wipe
            App.MindWipe?.Stop();

            // Stop Pop Quiz
            App.PopQuiz?.Stop();

            // Stop Bubbles
            App.Bubbles?.Stop();

            // Stop Brain Drain if it was active during session
            if (_brainDrainActive)
            {
                App.BrainDrain?.Stop();
                _brainDrainActive = false;
            }

            // Force-unduck audio before restoring settings (subliminals/flashes may have left it ducked)
            App.Audio?.ForceUnduck();

            // Restore original settings
            RestoreSettings();
            
            // Fire events
            SessionStopped?.Invoke(this, EventArgs.Empty);

            // Update Discord presence back to idle
            App.DiscordRpc?.SetIdleActivity();

            // Track abandoned session if not completed
            if (!completed)
            {
                App.Achievements?.TrackSessionAbandoned();
            }

            int xpForLog = 0;

            if (completed && _currentSession != null)
            {
                // Calculate XP with pause penalty (100 XP per pause)
                App.Logger?.Debug("Session XP calculation: Session={Name}, Source={Source}, BonusXP={BonusXP}, Penalty={Penalty}, Duration={Duration:F1}min",
                    _currentSession.Name, _currentSession.Source, _currentSession.BonusXP, XPPenalty, finalElapsedTime.TotalMinutes);

                int baseXP = Math.Max(0, Math.Min(2500, _currentSession.BonusXP) - XPPenalty);

                // Apply level-based XP multiplier
                int level = App.Settings?.Current?.PlayerLevel ?? 1;
                double multiplier = App.Progression?.GetSessionXPMultiplier(level) ?? 1.0;

                // Duration bonus: reward time investment (sessions under 2 min don't count)
                double durationMinutes = Math.Max(0, finalElapsedTime.TotalMinutes - 2);
                int durationBonus = (int)Math.Round(durationMinutes * (8 + level * 0.15));

                int finalXP = Math.Max(0, (int)Math.Round(baseXP * multiplier) + durationBonus);
                xpForLog = finalXP;

                // Track achievement using settings captured at session START (not current settings).
                // RestoreSettings()+SessionStopped fire before this point, so reading App.Settings.Current
                // here would give the wrong value. Use the snapshot taken in StartSessionAsync() instead.
                // (TriggerVideoSafely used to also mutate StrictLockEnabled mid-session; it now passes a
                // per-call strictOverride instead, but the snapshot is still the correct source here.)
                App.Logger?.Information("Session achievement check: Session={Name}, NoPanic={NoPanic}, StrictLock={Strict}",
                    _currentSession.Name, !_sessionStartPanicKey, _sessionStartStrictLock);
                App.Achievements?.TrackSessionComplete(
                    _currentSession.Name,
                    finalElapsedTime.TotalMinutes,
                    !_sessionStartPanicKey, // No panic = panic key was disabled at session start
                    _sessionStartStrictLock
                );

                try
                {
                    SessionCompleted?.Invoke(this, new SessionCompletedEventArgs(
                        _currentSession,
                        finalElapsedTime,
                        finalXP,
                        _pauseCount
                    ));
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Error in SessionCompleted event handler");
                }

                App.Logger?.Information("Session completed: {Name}, XP: {XP} (base: {Base}, multiplier: {Mult:F2}x, paused {PauseCount}x, penalty: -{Penalty})",
                    _currentSession.Name, finalXP, baseXP, multiplier, _pauseCount, XPPenalty);
            }
            else
            {
                App.Logger?.Information("Session stopped early");

                // Track panic button press for Relapse achievement
                App.Achievements?.TrackPanicPressed();
            }

            // Finalize media log AFTER XP is settled so the persisted log records the actual award.
            // Aborted sessions still get a log (xpForLog == 0) so the post-session dialog shows
            // what played even when the user cut things short.
            try { App.SessionLog?.EndSession(completed, finalElapsedTime, xpForLog); }
            catch (Exception ex) { App.Logger?.Error(ex, "SessionLog.EndSession failed"); }

            _currentSession = null;
        }

        /// <summary>
        /// Pause the current session. Each pause costs 100 XP.
        /// </summary>
        public void PauseSession()
        {
            if (!_isRunning || _isPaused || _currentSession == null) return;

            // IMPORTANT: Save elapsed time BEFORE setting _isPaused to true
            // (ElapsedTime returns _pausedElapsedTime when paused, which would be stale)
            _pausedElapsedTime = ElapsedTime;
            _isPaused = true;
            _pauseCount++;
            _pauseStartTime = DateTime.Now;
            _wallClockStopwatch.Stop();

            // Stop timers but keep session state
            _mainTimer?.Stop();
            _phaseTimer?.Stop();

            // Pause services by stopping them
            App.Flash?.Stop();
            App.Subliminal?.Stop();
            App.Bubbles?.Stop();
            // Scheduler only. A pause must NOT dismiss a card the user is mid-way through typing —
            // that would walk through strict mode and forfeit the card's XP (#875).
            App.LockCard?.Stop();
            App.PopQuiz?.Stop();
            App.BubbleCount?.Stop();
            App.BouncingText?.Stop();
            App.MindWipe?.Stop();
            App.BrainDrain?.Stop();
            App.Overlay?.Stop(); // Stops all overlays
            App.Video?.Stop();
            App.Audio?.StopSound();

            App.Logger?.Information("Session paused (pause #{Count}, -100 XP penalty)", _pauseCount);
        }

        /// <summary>
        /// Resume a paused session
        /// </summary>
        public void ResumeSession()
        {
            if (!_isRunning || !_isPaused || _currentSession == null) return;

            _isPaused = false;
            _startTime = DateTime.Now; // Reset start time, elapsed time is tracked in _pausedElapsedTime
            _wallClockStopwatch.Start();

            // Restart timers
            _mainTimer?.Start();
            _phaseTimer?.Start();

            // Re-apply current session settings to restart services. Skip features whose
            // deferred timeline start (#483) hasn't arrived yet — CheckDelayedFeatures
            // fires them when due; elapsed time survives the pause.
            var settings = _currentSession.Settings;
            if (settings.FlashEnabled && !IsFeaturePending("flash")) App.Flash?.Start();
            if (settings.SubliminalEnabled && !IsFeaturePending("subliminal")) App.Subliminal?.Start();
            if (settings.BubblesEnabled) App.Bubbles?.Start();
            if (settings.LockCardEnabled && !IsFeaturePending("lock cards")) App.LockCard?.Start(SessionWindowMinutes);
            if (App.Settings.Current.PopQuizEnabled) App.PopQuiz?.Start();
            if (settings.BubbleCountEnabled && !IsFeaturePending("bubble count")) App.BubbleCount?.Start();
            if (settings.BouncingTextEnabled && !IsFeaturePending("bouncing text")) App.BouncingText?.Start();
            if (settings.MindWipeEnabled && !IsFeaturePending("mind wipe"))
                App.MindWipe?.Start(settings.MindWipeBaseMultiplier, settings.MindWipeVolume / 100.0);
            // DISABLED: Brain Drain is up for rework due to performance issues
            // if (_brainDrainActive && App.Settings.Current.IsLevelUnlocked(70)) App.BrainDrain?.Start();
            if (settings.MandatoryVideosEnabled && !IsFeaturePending("mandatory videos")) App.Video?.Start();
            // Re-enable overlays via the overlay service
            App.Overlay?.Start();

            App.Logger?.Information("Session resumed");
        }

        private void MainTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isRunning || _isPaused || _currentSession == null) return;
            
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
            // Easing curve (#660): per-session override if the preset sets one, else the global setting.
            var curve = settings.RampCurve ?? App.Settings.Current.RampCurve;
            var progress = RampCurves.ApplyCurve(elapsedMinutes / totalMinutes, curve);

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
            
            // Pink filter ramp (only after randomized start minute).
            // Drive the overlay DIRECTLY (same path as Deeper enhancement ramps) instead of
            // writing the ramped value into App.Settings.Current.PinkFilterOpacity: that value
            // is the user's persisted setting and auto-saves to disk, so an app kill/crash
            // mid-session froze the ramp maximum into settings.json permanently — the
            // "screen keeps getting more pink and stays that way" reports (#471, #476).
            // The snapshot in RestoreSettings only heals a CLEAN stop.
            if (settings.PinkFilterEnabled && elapsedMinutes >= _randomizedPinkStartMinute)
            {
                var pinkDuration = totalMinutes - _randomizedPinkStartMinute;
                var pinkProgress = (elapsedMinutes - _randomizedPinkStartMinute) / pinkDuration;
                pinkProgress = RampCurves.ApplyCurve(pinkProgress, curve);
                _currentPinkOpacity = Lerp(settings.PinkFilterStartOpacity, settings.PinkFilterEndOpacity, pinkProgress);
                App.Overlay?.SetSustainedOverlayOpacity("pink_filter", _currentPinkOpacity / 100.0);
            }

            // Spiral ramp (only after randomized start minute) — ramp-only guard matches the
            // flash ramp's, not pink's (pink direct-drives constants too).
            // Only when the session actually ramps: driving the overlay with a constant parks a
            // ramp hold in OverlayService, which makes the settings-sync skip the spiral for the
            // whole session and freezes the user's own opacity slider at the session value (#897).
            if (settings.SpiralEnabled && settings.SpiralOpacity != settings.SpiralOpacityEnd
                && elapsedMinutes >= _randomizedSpiralStartMinute)
            {
                var spiralDuration = totalMinutes - _randomizedSpiralStartMinute;
                var spiralProgress = (elapsedMinutes - _randomizedSpiralStartMinute) / spiralDuration;
                spiralProgress = RampCurves.ApplyCurve(spiralProgress, curve);
                _currentSpiralOpacity = Lerp(settings.SpiralOpacity, settings.SpiralOpacityEnd, spiralProgress);
                App.Overlay?.SetSustainedOverlayOpacity("spiral", _currentSpiralOpacity / 100.0);
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
                        App.Bubbles?.RefreshFrequency();
                    }
                }
            }

            // DISABLED: Brain Drain is up for rework due to performance issues
            // if (settings.BrainDrainEnabled && _brainDrainActive && elapsedMinutes >= settings.BrainDrainStartMinute)
            // {
            //     var brainDrainDuration = totalMinutes - settings.BrainDrainStartMinute;
            //     var brainDrainProgress = (elapsedMinutes - settings.BrainDrainStartMinute) / brainDrainDuration;
            //     brainDrainProgress = Math.Clamp(brainDrainProgress, 0, 1);
            //     _currentBrainDrainIntensity = Lerp(settings.BrainDrainStartIntensity, settings.BrainDrainEndIntensity, brainDrainProgress);
            //     if (IsMainWindowValid) _mainWindow.UpdateBrainDrainIntensity((int)_currentBrainDrainIntensity);
            // }
        }
        
        private void CheckDelayedFeatures(double elapsedMinutes)
        {
            if (_currentSession == null) return;
            var settings = _currentSession.Settings;

            // Deferred feature starts queued by ApplySessionSettings (timeline start
            // events, #483). Reverse-iterate so firing removes in place.
            for (int i = _pendingFeatureStarts.Count - 1; i >= 0; i--)
            {
                var pending = _pendingFeatureStarts[i];
                if (elapsedMinutes < pending.StartMinute) continue;
                _pendingFeatureStarts.RemoveAt(i);
                try
                {
                    pending.Start();
                    App.Logger?.Information("Session: {Feature} started at {Minutes:F1} minutes (target was {Target})",
                        pending.Name, elapsedMinutes, pending.StartMinute);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Session: deferred start of {Feature} failed", pending.Name);
                }
            }

            // Pink filter delayed start (use randomized time)
            if (settings.PinkFilterEnabled && !App.Settings.Current.PinkFilterEnabled)
            {
                if (elapsedMinutes >= _randomizedPinkStartMinute)
                {
                    App.Settings.Current.PinkFilterEnabled = true;
                    if (IsMainWindowValid) _mainWindow.EnablePinkFilter(true);
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
                    // A non-ramping session never drives the overlay directly, so its prescribed
                    // opacity has to land in settings here the way an immediate start does.
                    if (settings.SpiralOpacity == settings.SpiralOpacityEnd)
                        App.Settings.Current.SpiralOpacity = settings.SpiralOpacity;
                    App.Settings.Current.SpiralEnabled = true;
                    if (IsMainWindowValid) _mainWindow.EnableSpiral(true);
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
                    App.Bubbles?.Start(bypassLevelCheck: true); // Bypass level check during sessions
                }
            }

            // Corner GIF delayed start
            if (settings.CornerGifEnabled && _cornerGifWindow == null && settings.CornerGifStartMinute > 0)
            {
                if (elapsedMinutes >= settings.CornerGifStartMinute)
                {
                    ShowCornerGif(settings);
                    App.Logger?.Information("Corner GIF activated at {Minutes:F1} minutes (target was {Target})",
                        elapsedMinutes, settings.CornerGifStartMinute);
                }
            }

            // Corner GIF delayed end
            if (settings.CornerGifEnabled && _cornerGifWindow != null && settings.CornerGifEndMinute > 0)
            {
                if (elapsedMinutes >= settings.CornerGifEndMinute)
                {
                    CloseCornerGif();
                    App.Logger?.Information("Corner GIF deactivated at {Minutes:F1} minutes (target was {Target})",
                        elapsedMinutes, settings.CornerGifEndMinute);
                }
            }

            // DISABLED: Brain Drain is up for rework due to performance issues
            // if (settings.BrainDrainEnabled && !_brainDrainActive && settings.BrainDrainStartMinute > 0)
            // {
            //     if (elapsedMinutes >= settings.BrainDrainStartMinute)
            //     {
            //         _brainDrainActive = true;
            //         if (IsMainWindowValid) _mainWindow.EnableBrainDrain(true, settings.BrainDrainStartIntensity);
            //         App.Logger?.Information("Brain Drain activated at {Minutes:F1} minutes", elapsedMinutes);
            //     }
            // }
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
                if (IsMainWindowValid) _mainWindow.SetBubblesActive(false);
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

                    if (IsMainWindowValid) _mainWindow.SetBubblesActive(true, _currentSession.Settings.BubblesPerBurst);
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
            _savedSettings.CorruptionMode = current.CorruptionMode;
            _savedSettings.FlashAudioEnabled = current.FlashAudioEnabled;
            _savedSettings.ImageScale = current.ImageScale;
            _savedSettings.SimultaneousImages = current.SimultaneousImages;

            _savedSettings.SubliminalEnabled = current.SubliminalEnabled;
            _savedSettings.SubliminalFrequency = current.SubliminalFrequency;
            _savedSettings.SubliminalOpacity = current.SubliminalOpacity;

            // Save subliminal pool (deep copy)
            _savedSubliminalPool = new Dictionary<string, bool>(current.SubliminalPool);

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
            _savedSettings.BubblesClickable = current.BubblesClickable;

            _savedSettings.BouncingTextEnabled = current.BouncingTextEnabled;
            _savedSettings.BouncingTextSpeed = current.BouncingTextSpeed;
            _savedSettings.BouncingTextSize = current.BouncingTextSize;
            _savedSettings.BouncingTextOpacity = current.BouncingTextOpacity;

            // Save bouncing text pool (deep copy)
            _savedBouncingTextPool = new Dictionary<string, bool>(current.BouncingTextPool);
            
            _savedSettings.MandatoryVideosEnabled = current.MandatoryVideosEnabled;
            _savedSettings.VideosPerHour = current.VideosPerHour;
            _savedSettings.LockCardEnabled = current.LockCardEnabled;
            _savedSettings.LockCardFrequency = current.LockCardFrequency;

            // Save lock card pool (deep copy)
            _savedLockCardPool = new Dictionary<string, bool>(current.LockCardPhrases);

            _savedSettings.PopQuizEnabled = current.PopQuizEnabled;
            _savedSettings.PopQuizFrequency = current.PopQuizFrequency;
            _savedSettings.BubbleCountEnabled = current.BubbleCountEnabled;
            _savedSettings.BubbleCountFrequency = current.BubbleCountFrequency;
        }
        
        private Dictionary<string, bool>? _savedBouncingTextPool;
        private Dictionary<string, bool>? _savedSubliminalPool;
        private Dictionary<string, bool>? _savedLockCardPool;

        // ---------------------------------------------------------------------------------
        // Phrase-pool custody, for ModService (see IsOverridingPhrasePools below).
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// The pools this session PRESCRIBED, captured straight after ApplySessionSettings wrote
        /// them. Stored as finished dictionaries rather than re-deriving them, so re-applying is a
        /// plain assignment and cannot drift from what the override originally computed.
        /// Null for a pool the session left alone.
        /// </summary>
        private Dictionary<string, bool>? _prescribedSubliminalPool;
        private Dictionary<string, bool>? _prescribedBouncingTextPool;
        private Dictionary<string, bool>? _prescribedLockCardPool;

        /// <summary>
        /// The active mod when this session started, so RestoreSettings can tell whether the user
        /// switched mods mid-session and the pool snapshot it is about to restore is stale.
        /// </summary>
        private string? _modIdAtStart;

        /// <summary>
        /// The running engine, so <see cref="ModService"/> can ask who owns the phrase pools right
        /// now without MainWindow having to plumb a reference through it.
        ///
        /// Every consumer must gate on <see cref="IsOverridingPhrasePools"/>, which re-checks
        /// <see cref="IsRunning"/> - so even if this reference were ever left dangling by an
        /// abnormal teardown, it reports "no session" and callers fall back to normal behaviour.
        /// A stale non-null here can therefore never make ModService do the wrong thing.
        /// </summary>
        internal static SessionEngine? Active { get; private set; }

        /// <summary>
        /// True while a live session has replaced one or more phrase pools with its own.
        ///
        /// Why ModService needs to know: during a session the live SubliminalPool /
        /// BouncingTextPool / LockCardPhrases are the SESSION's phrases, not the user's. Anything
        /// that persists the live pool as the user's saved pool is writing the wrong data - and
        /// because RestoreSettings only restores the flat pool and never the per-mod backup, that
        /// bad backup outlives the session and gets copied over the good pool on next launch.
        /// </summary>
        internal bool IsOverridingPhrasePools =>
            IsRunning && (_prescribedSubliminalPool != null
                       || _prescribedBouncingTextPool != null
                       || _prescribedLockCardPool != null);

        /// <summary>The user's own pools, as they were before this session overrode them.</summary>
        internal Dictionary<string, bool>? UserSubliminalPool =>
            _savedSubliminalPool == null ? null : new Dictionary<string, bool>(_savedSubliminalPool);

        internal Dictionary<string, bool>? UserBouncingTextPool =>
            _savedBouncingTextPool == null ? null : new Dictionary<string, bool>(_savedBouncingTextPool);

        internal Dictionary<string, bool>? UserLockCardPool =>
            _savedLockCardPool == null ? null : new Dictionary<string, bool>(_savedLockCardPool);

        /// <summary>
        /// Folds an edit the user just made in a phrase editor into this session's restore snapshot,
        /// so RestoreSettings still undoes the session's own temporary pool changes but no longer
        /// reverts the user's edit with them (#906: phrases added/removed/renamed mid-session came
        /// back exactly as they were before the session, silently losing the work).
        ///
        /// Every call is a genuine user edit: only ASSIGNING a whole pool raises INPC (the session
        /// mutates the live pools in place), and the mod-driven assignments are made with
        /// ModService's pool mirror suppressed, so they never reach here.
        /// </summary>
        internal void NoteUserPhrasePoolEdit(string? propertyName)
        {
            if (!IsRunning) return;
            var current = App.Settings?.Current;
            if (current == null) return;

            try
            {
                switch (propertyName)
                {
                    case nameof(AppSettings.SubliminalPool):
                        FoldUserPoolEdit(current.SubliminalPool, ref _prescribedSubliminalPool, ref _savedSubliminalPool);
                        break;
                    case nameof(AppSettings.BouncingTextPool):
                        FoldUserPoolEdit(current.BouncingTextPool, ref _prescribedBouncingTextPool, ref _savedBouncingTextPool);
                        break;
                    case nameof(AppSettings.LockCardPhrases):
                        FoldUserPoolEdit(current.LockCardPhrases, ref _prescribedLockCardPool, ref _savedLockCardPool);
                        break;
                }
                // Every fold assumes a GENUINE user edit (session writes are in-place/no-INPC,
                // mod writes run suppressed). A non-user caller reaching here corrupts the
                // restore snapshot — this line is the tell.
                App.Logger?.Debug("[Session] Folded mid-session {Pool} edit into restore snapshot", propertyName);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Session] Failed to fold a mid-session phrase edit into the restore snapshot");
            }
        }

        /// <summary>
        /// Applies the user's delta (live pool vs what the session prescribed) to the pre-session
        /// snapshot, then re-bases the prescribed copy onto the live pool so a later edit diffs
        /// against what is actually on screen — and so a mod switch re-asserts the edited pool.
        /// Phrases the session owns are never folded into the user's pool: only keys the user
        /// added, deleted, or re-toggled on their OWN phrases move across.
        /// </summary>
        private static void FoldUserPoolEdit(
            Dictionary<string, bool>? live,
            ref Dictionary<string, bool>? prescribed,
            ref Dictionary<string, bool>? saved)
        {
            if (live == null || saved == null) return;

            if (prescribed == null)
            {
                // The session left this pool alone, so the live pool IS the user's own.
                saved = new Dictionary<string, bool>(live);
                return;
            }

            foreach (var kvp in live)
            {
                if (!prescribed.TryGetValue(kvp.Key, out var wasEnabled))
                    saved[kvp.Key] = kvp.Value;                     // added while the session ran
                else if (wasEnabled != kvp.Value && saved.ContainsKey(kvp.Key))
                    saved[kvp.Key] = kvp.Value;                     // re-toggled one of their own
            }

            foreach (var key in prescribed.Keys)
            {
                if (!live.ContainsKey(key)) saved.Remove(key);       // deleted while the session ran
            }

            prescribed = new Dictionary<string, bool>(live);
        }

        /// <summary>
        /// Re-asserts this session's prescribed phrase pools over the live settings.
        ///
        /// Called after a mod switch: ModService.ActivateMod restores the incoming mod's saved
        /// pools, which would otherwise leave the running session speaking the user's phrases
        /// instead of the ones it prescribed. Only pools the session actually overrode are
        /// touched, so a mod switch still fully applies to every pool the session left alone.
        /// </summary>
        internal void ReapplyPhrasePoolOverrides()
        {
            if (!IsRunning) return;
            var current = App.Settings?.Current;
            if (current == null) return;

            try
            {
                if (_prescribedSubliminalPool != null)
                    current.SubliminalPool = new Dictionary<string, bool>(_prescribedSubliminalPool);
                if (_prescribedBouncingTextPool != null)
                    current.BouncingTextPool = new Dictionary<string, bool>(_prescribedBouncingTextPool);
                if (_prescribedLockCardPool != null)
                    current.LockCardPhrases = new Dictionary<string, bool>(_prescribedLockCardPool);

                App.Logger?.Information("[Session] Re-applied prescribed phrase pools after a mod switch");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Session] Failed to re-apply prescribed phrase pools");
            }
        }

        /// <summary>
        /// Snapshots whatever ApplySessionSettings just wrote into the pools, for the two uses
        /// above. Call once per pool, immediately after the override.
        /// </summary>
        private void RememberPrescribedPools()
        {
            var current = App.Settings?.Current;
            if (current == null) return;

            if (_currentSession?.Settings.SubliminalPhrases.Count > 0)
                _prescribedSubliminalPool = new Dictionary<string, bool>(current.SubliminalPool);
            if (_currentSession?.Settings.BouncingTextPhrases.Count > 0)
                _prescribedBouncingTextPool = new Dictionary<string, bool>(current.BouncingTextPool);
            if (_currentSession?.Settings.LockCardPhrases.Count > 0)
                _prescribedLockCardPool = new Dictionary<string, bool>(current.LockCardPhrases);
        }

        // Deferred feature starts (the timeline editor's "start at minute X" events).
        // The editor serializes a StartMinute for 13 features, but the engine only ever
        // read four of them (pink filter, spiral, bubbles, corner GIF) — flash, subliminal,
        // whispers, bouncing text, videos, lock cards, bubble count and mind wipe all
        // started at t=0 regardless (#483). ApplySessionSettings queues one entry per
        // delayed feature; CheckDelayedFeatures fires each when its minute arrives. The
        // four original features keep their bespoke delay paths (randomized starts, ramps).
        private readonly List<(string Name, int StartMinute, Action Start)> _pendingFeatureStarts = new();

        private void DeferFeatureStart(string name, int startMinute, Action start)
        {
            _pendingFeatureStarts.Add((name, startMinute, start));
            App.Logger?.Information("Session: {Feature} start deferred to minute {Minute}", name, startMinute);
        }

        private bool IsFeaturePending(string name) =>
            _pendingFeatureStarts.Any(p => p.Name == name);

        private void ApplySessionSettings(SessionSettings settings)
        {
            var current = App.Settings.Current;
            _pendingFeatureStarts.Clear();
            
            // Flash Images
            current.FlashEnabled = settings.FlashEnabled;
            if (settings.FlashEnabled)
            {
                current.FlashFrequency = settings.FlashPerHour;
                current.FlashOpacity = settings.FlashOpacity;
                current.SimultaneousImages = settings.FlashImages;
                current.FlashClickable = settings.FlashClickable;
                current.CorruptionMode = settings.FlashHydra;
                current.FlashAudioEnabled = settings.FlashAudioEnabled;

                if (settings.FlashStartMinute == 0)
                {
                    // Start flash service for session
                    App.Flash?.Start();
                    App.Logger?.Information("Session: Started flash images at {Freq}/hour", settings.FlashPerHour);
                }
                else
                {
                    App.Flash?.Stop();   // engine may already have it running
                    DeferFeatureStart("flash", settings.FlashStartMinute, () => App.Flash?.Start());
                }
            }
            else
            {
                // Stop flash if session disables it
                App.Flash?.Stop();
            }

            // Subliminals - override phrases with session-specific ones
            current.SubliminalEnabled = settings.SubliminalEnabled;
            if (settings.SubliminalEnabled)
            {
                current.SubliminalFrequency = settings.SubliminalPerMin;
                current.SubliminalOpacity = settings.SubliminalOpacity;
                current.SubliminalDuration = settings.SubliminalFrames;

                // Override the subliminal pool with session phrases
                if (settings.SubliminalPhrases.Count > 0)
                {
                    // Disable all existing phrases
                    var keys = current.SubliminalPool.Keys.ToList();
                    foreach (var key in keys)
                    {
                        current.SubliminalPool[key] = false;
                    }

                    // Add/enable session phrases (mod-aware: transform triggers for active mod)
                    foreach (var phrase in settings.SubliminalPhrases)
                    {
                        var modePhrase = App.Mods?.MakeModAware(phrase) ?? phrase;
                        current.SubliminalPool[modePhrase] = true;
                    }

                    App.Logger?.Information("Session: Using subliminal phrases: {Phrases}",
                        string.Join(", ", settings.SubliminalPhrases));
                }

                if (settings.SubliminalStartMinute == 0)
                {
                    // Start subliminal service for session
                    App.Subliminal?.Start();
                }
                else
                {
                    App.Subliminal?.Stop();
                    DeferFeatureStart("subliminal", settings.SubliminalStartMinute, () => App.Subliminal?.Start());
                }
            }
            else
            {
                App.Subliminal?.Stop();
            }

            // Audio Whispers (Sub Audio) — flag-driven (no service Start), so a delayed
            // start just holds the flag off until the minute arrives.
            current.SubAudioEnabled = settings.AudioWhispersEnabled && settings.AudioWhispersStartMinute == 0;
            if (settings.AudioWhispersEnabled)
            {
                current.SubAudioVolume = settings.WhisperVolume;
                if (settings.AudioWhispersStartMinute > 0)
                    DeferFeatureStart("audio whispers", settings.AudioWhispersStartMinute,
                        () => App.Settings.Current.SubAudioEnabled = true);
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
                current.BouncingTextSize = settings.BouncingTextSize;
                current.BouncingTextOpacity = settings.BouncingTextOpacity;

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
                
                App.BouncingText?.Stop(); // Stop first to reset state
                if (settings.BouncingTextStartMinute == 0)
                {
                    // Start bouncing text (bypass level requirement during sessions)
                    App.BouncingText?.Start(bypassLevelCheck: true);
                    App.Logger?.Information("Session: Started bouncing text with phrases: {Phrases}",
                        string.Join(", ", settings.BouncingTextPhrases));
                }
                else
                {
                    DeferFeatureStart("bouncing text", settings.BouncingTextStartMinute,
                        () => App.BouncingText?.Start(bypassLevelCheck: true));
                }
            }
            else
            {
                // Stop bouncing text if session disables it
                App.BouncingText?.Stop();
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
                current.BubblesClickable = settings.BubblesClickable;
                // Start immediately if no start minute is set. Otherwise, CheckDelayedFeatures will handle it.
                current.BubblesEnabled = settings.BubblesStartMinute == 0 && !settings.BubblesIntermittent;

                // Start bubbles if immediate start (bypass level check during sessions)
                if (current.BubblesEnabled)
                {
                    App.Bubbles?.Start(bypassLevelCheck: true);
                    // Start() no-ops when bubbles were already running from the dashboard,
                    // which would keep spawning at the user's old rate for the whole session
                    App.Bubbles?.RefreshFrequency();
                }
                else
                {
                    // Delayed or intermittent start: the session owns bubbles from t=0, so a
                    // dashboard-started spawner must not keep running until the schedule kicks in
                    App.Bubbles?.Stop();
                }
            }
            else
            {
                current.BubblesEnabled = false;
                App.Bubbles?.Stop();
            }

            // Interactive Features
            current.MandatoryVideosEnabled = settings.MandatoryVideosEnabled;
            if (settings.MandatoryVideosEnabled)
            {
                if (settings.VideosPerHour.HasValue)
                {
                    current.VideosPerHour = settings.VideosPerHour.Value;
                }
                if (settings.MandatoryVideosStartMinute == 0)
                {
                    App.Video?.Start();
                }
                else
                {
                    App.Video?.Stop();
                    DeferFeatureStart("mandatory videos", settings.MandatoryVideosStartMinute, () => App.Video?.Start());
                }
            }
            else
            {
                App.Video?.Stop();
            }

            current.LockCardEnabled = settings.LockCardEnabled;
            if (settings.LockCardEnabled)
            {
                if (settings.LockCardFrequency.HasValue)
                {
                    current.LockCardFrequency = settings.LockCardFrequency.Value;
                }

                // Override lock card pool with session-specific phrases
                if (settings.LockCardPhrases.Count > 0)
                {
                    var keys = current.LockCardPhrases.Keys.ToList();
                    foreach (var key in keys)
                    {
                        current.LockCardPhrases[key] = false;
                    }

                    foreach (var phrase in settings.LockCardPhrases)
                    {
                        var modePhrase = App.Mods?.MakeModAware(phrase) ?? phrase;
                        current.LockCardPhrases[modePhrase] = true;
                    }

                    App.Logger?.Information("Session: Using lock card phrases: {Phrases}",
                        string.Join(", ", settings.LockCardPhrases));
                }

                if (settings.LockCardStartMinute == 0)
                {
                    App.LockCard?.Start(SessionWindowMinutes);
                }
                else
                {
                    App.LockCard?.Stop();
                    // #736: the window is evaluated when the deferred start actually fires, so the
                    // first card is scheduled against the time genuinely left in the session.
                    DeferFeatureStart("lock cards", settings.LockCardStartMinute,
                        () => App.LockCard?.Start(SessionWindowMinutes));
                }
            }
            else
            {
                App.LockCard?.Stop();
            }

            // Pop quiz is a user-level toggle (AppSettings), not per-session
            if (App.Settings.Current.PopQuizEnabled)
            {
                App.PopQuiz?.Start();
            }
            else
            {
                App.PopQuiz?.Stop();
            }

            current.BubbleCountEnabled = settings.BubbleCountEnabled;
            if (settings.BubbleCountEnabled)
            {
                if (settings.BubbleCountFrequency.HasValue)
                {
                    current.BubbleCountFrequency = settings.BubbleCountFrequency.Value;
                }
                if (settings.BubbleCountStartMinute == 0)
                {
                    App.BubbleCount?.Start();
                }
                else
                {
                    App.BubbleCount?.Stop();
                    DeferFeatureStart("bubble count", settings.BubbleCountStartMinute, () => App.BubbleCount?.Start());
                }
            }
            else
            {
                App.BubbleCount?.Stop();
            }

            // Start overlay service (handles spiral and pink filter)
            App.Overlay?.Start();

            // Apply settings to UI
            if (IsMainWindowValid) _mainWindow.ApplySessionSettings();
        }

        private void RestoreSettings()
        {
            if (_savedSettings == null) return;
            
            var current = App.Settings.Current;
            
            current.FlashEnabled = _savedSettings.FlashEnabled;
            current.FlashFrequency = _savedSettings.FlashFrequency;
            current.FlashOpacity = _savedSettings.FlashOpacity;
            current.FlashClickable = _savedSettings.FlashClickable;
            current.CorruptionMode = _savedSettings.CorruptionMode;
            current.FlashAudioEnabled = _savedSettings.FlashAudioEnabled;
            current.ImageScale = _savedSettings.ImageScale;
            current.SimultaneousImages = _savedSettings.SimultaneousImages;

            current.SubliminalEnabled = _savedSettings.SubliminalEnabled;
            current.SubliminalFrequency = _savedSettings.SubliminalFrequency;
            current.SubliminalOpacity = _savedSettings.SubliminalOpacity;

            // Restore subliminal pool
            if (_savedSubliminalPool != null)
            {
                current.SubliminalPool.Clear();
                foreach (var kvp in _savedSubliminalPool)
                {
                    current.SubliminalPool[kvp.Key] = kvp.Value;
                }
                _savedSubliminalPool = null;
            }

            // If the user switched MODS while this session was running, the snapshot restored above
            // belongs to the OUTGOING mod - restoring it verbatim would leave the wrong mod's
            // phrases live until the next relaunch. The per-mod backups are all correct by this
            // point (ModService no longer persists session phrases into them), so the fix is simply
            // to re-read the active mod's own pools. Only runs when the mod actually changed.
            try
            {
                var modNow = App.Mods?.ActiveMod?.Id;
                if (_modIdAtStart != null && modNow != null && modNow != _modIdAtStart)
                {
                    App.Logger?.Information(
                        "[Session] Mod changed mid-session ({From} -> {To}); re-applying the active mod's pools "
                        + "instead of the start-time snapshot", _modIdAtStart, modNow);
                    App.Mods?.ReapplyActiveModPools();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Session] Post-session mod pool reconciliation failed");
            }

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
            current.BubblesClickable = _savedSettings.BubblesClickable;

            current.BouncingTextEnabled = _savedSettings.BouncingTextEnabled;
            current.BouncingTextSpeed = _savedSettings.BouncingTextSpeed;
            current.BouncingTextSize = _savedSettings.BouncingTextSize;
            current.BouncingTextOpacity = _savedSettings.BouncingTextOpacity;

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

            // Restore lock card pool
            if (_savedLockCardPool != null)
            {
                current.LockCardPhrases.Clear();
                foreach (var kvp in _savedLockCardPool)
                {
                    current.LockCardPhrases[kvp.Key] = kvp.Value;
                }
                _savedLockCardPool = null;
            }

            current.PopQuizEnabled = _savedSettings.PopQuizEnabled;
            current.PopQuizFrequency = _savedSettings.PopQuizFrequency;
            current.BubbleCountEnabled = _savedSettings.BubbleCountEnabled;
            current.BubbleCountFrequency = _savedSettings.BubbleCountFrequency;

            // The session's pink/spiral ramps drove the overlays directly and set ramp
            // holds so the settings-sync wouldn't stomp them. Release the holds so the
            // sync re-applies the user's restored opacities on its next tick.
            App.Overlay?.ReleaseOpacityRampHolds();

            // Apply restored settings to UI
            if (IsMainWindowValid) _mainWindow.ApplySessionSettings();

            _savedSettings = null;
        }
        
        private void ShowCornerGif(SessionSettings settings)
        {
            try
            {
                var gifPath = settings.CornerGifPath;
                Uri? gifUri = null;
                System.Drawing.Image? img = null;

                if (!string.IsNullOrEmpty(gifPath) && System.IO.File.Exists(gifPath))
                {
                    try
                    {
                        gifUri = new Uri(gifPath);
                        img = System.Drawing.Image.FromFile(gifPath);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("Failed to load corner GIF from file: {Error}", ex.Message);
                        gifUri = null;
                        img = null;
                    }
                }

                // Fallback to embedded resource if file loading failed
                if (img == null)
                {
                    try
                    {
                        gifUri = new Uri(ModResourceResolver.ResolveSpiralUri(), UriKind.Absolute);
                        if (gifUri.IsFile)
                        {
                            // Active-mod override: GetResourceStream only accepts pack:// URIs.
                            img = System.Drawing.Image.FromFile(gifUri.LocalPath);
                            App.Logger?.Information("Corner GIF not set or found, defaulting to spiral.gif resource");
                        }
                        else
                        {
                            var resourceInfo = Application.GetResourceStream(gifUri);
                            if (resourceInfo?.Stream != null)
                            {
                                using (resourceInfo.Stream)
                                {
                                    img = System.Drawing.Image.FromStream(resourceInfo.Stream);
                                }
                                App.Logger?.Information("Corner GIF not set or found, defaulting to spiral.gif resource");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("Failed to load default corner GIF resource: {Error}", ex.Message);
                    }
                }

                if (img == null || gifUri == null)
                {
                    App.Logger?.Warning("Could not load corner GIF image - skipping corner GIF display");
                    return;
                }

                // Get GIF dimensions to maintain aspect ratio and cache them
                _cornerGifWidth = img.Width;
                _cornerGifHeight = img.Height;
                img.Dispose();

                double gifWidth = _cornerGifWidth;
                double gifHeight = _cornerGifHeight;

                // Scale based on user's size setting (default 300)
                var targetSize = settings.CornerGifSize > 0 ? settings.CornerGifSize : 300;
                double scale = targetSize / Math.Max(gifWidth, gifHeight);
                double windowWidth = gifWidth * scale;
                double windowHeight = gifHeight * scale;

                // Get actual screen bounds using Forms.Screen (more reliable for DPI)
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) return;

                // Calculate DPI scale
                double dpiScale;
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    dpiScale = g.DpiX / 96.0;
                }

                // Convert physical pixels to WPF logical units
                double screenWidth = screen.Bounds.Width / dpiScale;
                double screenHeight = screen.Bounds.Height / dpiScale;

                // Position at the exact screen edges (0 offset)
                double left = 0, top = 0;
                switch (settings.CornerGifPosition)
                {
                    case CornerPosition.TopLeft:
                        left = 0;
                        top = 0;
                        break;
                    case CornerPosition.TopRight:
                        left = screenWidth - windowWidth;
                        top = 0;
                        break;
                    case CornerPosition.BottomLeft:
                        left = 0;
                        top = screenHeight - windowHeight;
                        break;
                    case CornerPosition.BottomRight:
                        left = screenWidth - windowWidth;
                        top = screenHeight - windowHeight;
                        break;
                }

                // Apply user's opacity setting directly (no 90% reduction for corner GIF)
                var opacity = settings.CornerGifOpacity / 100.0;

                _cornerGifWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Width = windowWidth,
                    Height = windowHeight,
                    Left = left,
                    Top = top,
                    Opacity = opacity,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };

                // Use Image with XamlAnimatedGif for proper GIF animation
                var imageElement = new Image
                {
                    Stretch = System.Windows.Media.Stretch.Uniform
                };

                // Catch GIF rendering errors gracefully instead of letting them crash the app.
                // Must be attached BEFORE SetSourceUri: that starts an async load, so a fault
                // raised in the gap would have no subscriber.
                AnimationBehavior.AddErrorHandler(imageElement, (s, e) =>
                {
                    App.Logger?.Warning("Corner GIF animation error ({Kind}): {Error}",
                        e.Kind, e.Exception?.Message);
                });

                // Set the animated GIF source using XamlAnimatedGif
                AnimationBehavior.SetRepeatBehavior(imageElement, System.Windows.Media.Animation.RepeatBehavior.Forever);
                AnimationBehavior.SetSourceUri(imageElement, gifUri);

                _cornerGifImage = imageElement;
                _cornerGifWindow.Content = imageElement;

                // Hook SourceInitialized BEFORE Show() to safely get the hwnd for click-through
                _cornerGifWindow.SourceInitialized += (s, e) =>
                {
                    MakeWindowClickThrough(_cornerGifWindow);
                };
                _cornerGifWindow.Show();

                App.Logger?.Information("Corner GIF shown at {Position}: {Path} (pos: {Left},{Top}, size: {Width}x{Height}px, opacity: {Opacity}%)",
                    settings.CornerGifPosition, gifUri.ToString(), left, top, (int)windowWidth, (int)windowHeight, settings.CornerGifOpacity);
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
            _cornerGifImage = null;
            _cornerGifWidth = 0;
            _cornerGifHeight = 0;
        }

        /// <summary>
        /// Updates the corner GIF size during an active session (#474). The window is
        /// RECREATED, not resized in place: resizing a realized AllowsTransparency window
        /// while its GIF is animating runs a synchronous CompleteRender that can deadlock
        /// the render thread — the crash that originally got the size slider's live
        /// update disabled. Close+Show is the same path the CornerGifEndMinute timer
        /// already exercises mid-session. Callers should debounce (the UI slider does).
        /// </summary>
        public void UpdateCornerGifSize(int newSize)
        {
            if (_currentSession == null) return;

            try
            {
                _currentSession.Settings.CornerGifSize = newSize;
                if (_cornerGifWindow == null) return; // not shown (yet) — start timer will pick the value up
                CloseCornerGif();
                ShowCornerGif(_currentSession.Settings);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to update corner GIF size");
            }
        }

        /// <summary>
        /// Swaps the corner GIF file during an active session (#474). Recreates the
        /// window (see UpdateCornerGifSize for why in-place changes are avoided; a new
        /// file changes the window dimensions anyway).
        /// </summary>
        public void UpdateCornerGifPath(string path)
        {
            if (_currentSession == null) return;

            try
            {
                _currentSession.Settings.CornerGifPath = path;
                if (_cornerGifWindow == null) return;
                CloseCornerGif();
                ShowCornerGif(_currentSession.Settings);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to update corner GIF path");
            }
        }

        /// <summary>
        /// Moves the corner GIF to another corner during an active session (#474).
        /// Position-only change (Left/Top) — moving a layered window doesn't touch its
        /// render surface, so no recreate is needed.
        /// </summary>
        public void UpdateCornerGifPosition(CornerPosition position)
        {
            if (_currentSession == null) return;

            try
            {
                _currentSession.Settings.CornerGifPosition = position;
                if (_cornerGifWindow == null) return;

                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) return;

                double dpiScale;
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    dpiScale = g.DpiX / 96.0;
                }

                double screenWidth = screen.Bounds.Width / dpiScale;
                double screenHeight = screen.Bounds.Height / dpiScale;
                double windowWidth = _cornerGifWindow.Width;
                double windowHeight = _cornerGifWindow.Height;

                double left = 0, top = 0;
                switch (position)
                {
                    case CornerPosition.TopLeft:
                        left = 0; top = 0;
                        break;
                    case CornerPosition.TopRight:
                        left = screenWidth - windowWidth; top = 0;
                        break;
                    case CornerPosition.BottomLeft:
                        left = 0; top = screenHeight - windowHeight;
                        break;
                    case CornerPosition.BottomRight:
                        left = screenWidth - windowWidth; top = screenHeight - windowHeight;
                        break;
                }

                _cornerGifWindow.Left = left;
                _cornerGifWindow.Top = top;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to update corner GIF position");
            }
        }

        /// <summary>
        /// Updates the corner GIF opacity during an active session
        /// </summary>
        public void UpdateCornerGifOpacity(int newOpacity)
        {
            if (_cornerGifWindow == null || _currentSession == null) return;

            try
            {
                // Update the session settings
                _currentSession.Settings.CornerGifOpacity = newOpacity;

                // Apply opacity directly (no reduction)
                _cornerGifWindow.Opacity = newOpacity / 100.0;

                App.Logger?.Debug("Corner GIF opacity updated to {Opacity}%", newOpacity);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to update corner GIF opacity");
            }
        }

        private void MakeWindowClickThrough(Window window)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                App.Logger?.Warning("MakeWindowClickThrough: hwnd is zero, window not yet initialized");
                return;
            }
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            // Add TOOLWINDOW to hide from alt-tab, TRANSPARENT and LAYERED for click-through
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * Math.Clamp(t, 0, 1);
        }

        // P/Invoke for click-through and hiding from alt-tab
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        
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
        public int PauseCount { get; }
        public int XPPenalty => PauseCount * 100;

        public SessionCompletedEventArgs(Session session, TimeSpan duration, int xp, int pauseCount = 0)
        {
            Session = session;
            Duration = duration;
            XPEarned = xp;
            PauseCount = pauseCount;
        }
    }
    
    #endregion
}
