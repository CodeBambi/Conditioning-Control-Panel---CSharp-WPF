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

        // EMI Desk (MOMENTS 4.B). Her session beats are fired here rather than mirrored off the
        // bark triggers because the bark contexts are lossy: SessionCompleted drops the XP, the
        // elapsed time and the pause count, and SessionProgress carries only the elapsed seconds.
        // These three latches make the once-per-session beats once-per-session.
        private int _emiRampStep;
        private bool _emiSaidHalfway;
        private bool _emiSaidLastMinute;
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
            // Breadcrumb for the hang report: names the session (and, for a program day, which day
            // via HangContext's passive program probe) that was live when the UI thread died.
            HangContext.Enter("session:" + (session?.Name ?? "?"));
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
            
            // Setup corner GIF if enabled (respects start/end minute timers).
            // The template's own CornerGifEnabled used to be the ONLY gate here - see
            // CanRaiseCornerGif for the user master + the standalone-overlay dedupe.
            if (CanRaiseCornerGif(session.Settings))
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

            // EMI Desk: reset the per-session latches, then announce.
            _emiRampStep = 0;
            _emiSaidHalfway = false;
            _emiSaidLastMinute = false;
            try
            {
                App.EmiDesk?.Fire("sessionStarted", new
                {
                    target = session.Name?.ToLowerInvariant(),
                    minutes = (int)session.DurationMinutes,
                });
            }
            catch { }

            // Update Discord presence with session name
            App.DiscordRpc?.SetSessionActivity(session.Name);

            // Track session start for achievements (e.g., Relapse)
            App.Achievements?.TrackSessionStart();

            // EMI Desk (MOMENTS `firstSessionEver`). Read AFTER the tracker's increment, so "== 1"
            // is this account's first session ever - and fired behind `sessionStarted` above on
            // purpose: the engine's floor picks one of the two, and on a first session the
            // once-ever line is the one worth having. The moment's own limit ever/1 is what keeps
            // accounts whose counter was already past 1 out of it.
            try
            {
                if (App.Achievements?.Progress?.TotalSessionsStarted == 1)
                    App.EmiDesk?.Fire("firstSessionEver", null);
            }
            catch { }

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
        /// Stops the current session.
        /// </summary>
        /// <param name="completed">The session reached its end. Awards XP and never counts as abandoned.</param>
        /// <param name="suppressAbandonTracking">
        /// End the session without charging it to the abandoned-session counters. For the one case
        /// where the app, not the user, ended it: withdrawing from a training program tears the
        /// program's own session down as part of leaving, and leaving is a supported, free exit.
        /// Every other caller leaves this alone - a session the user walked out of is still abandoned.
        /// </param>
        public void StopSession(bool completed = false, bool suppressAbandonTracking = false)
        {
            if (!_isRunning) return;

            // Capture elapsed time BEFORE setting _isRunning to false
            // (ElapsedTime property returns Zero when not running)
            var finalElapsedTime = ElapsedTime;

            HangContext.Leave("session:" + (_currentSession?.Name ?? "?"));
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
            // CloseCornerGif also hands the corner back to any standalone slot that stood down
            // while this session's overlay owned it.
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

            // Hand the flash trio back before the restore. Unconditional and ahead of
            // RestoreSettings, which returns early when there is no snapshot - a session overlay
            // left parked would keep overriding the user's own values for the rest of the run.
            App.Settings?.Current?.ClearSessionFlashRamp();

            // Restore original settings
            RestoreSettings();
            
            // Fire events
            SessionStopped?.Invoke(this, EventArgs.Empty);

            // Update Discord presence back to idle
            App.DiscordRpc?.SetIdleActivity();

            // Track abandoned session if not completed
            if (!completed && !suppressAbandonTracking)
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

                // EMI Desk (MOMENTS 4.B). THE branch that tells a finish from a walk-out: no
                // elapsed-vs-planned heuristic is needed, because StopSession is already told which
                // one this is and only this arm raises SessionCompleted.
                try
                {
                    App.EmiDesk?.Fire("sessionCompleted", new
                    {
                        target = _currentSession.Name?.ToLowerInvariant(),
                        minutes = (int)finalElapsedTime.TotalMinutes,
                        n = finalXP,
                    });
                }
                catch { }
            }
            else
            {
                App.Logger?.Information("Session stopped early");

                // EMI Desk (MOMENTS 4.B): the other arm of the same branch. common.encourage, so it
                // is warm about it - never a scold for stopping.
                try { App.EmiDesk?.Fire("sessionAbandoned", new { minutes = (int)finalElapsedTime.TotalMinutes }); }
                catch { }

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

            // EMI Desk (MOMENTS 4.B): SessionEngine raises no pause or resume event, so this is the
            // only seam. common.encourage - never a chaperone about stopping.
            try { App.EmiDesk?.Fire("sessionPaused", new { n = _pauseCount }); } catch { }
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
            // The session-scoped corner overlay is a window this engine owns, and a pause means
            // "get it off my screen" - it used to keep spinning through every pause, including the
            // one the panic key triggers (ticket 1539282547484139682).
            //
            // HIDE ONLY (handBackCorner: false). A pause is not the end of the session's claim on
            // the corner, and pause/resume has to be symmetric: handing the corner back here let a
            // standalone slot realize during the pause, and ResumeSession's re-raise then refused
            // (StandaloneCornerGifActive) - one pause and a stock minute-0 program-day corner GIF
            // was gone for the rest of the day, because the per-second tick only ever re-raises
            // overlays with CornerGifStartMinute > 0. The handback debt is remembered instead
            // (_cornerHandbackOwed) and settled by whichever terminal close comes first.
            CloseCornerGif(handBackCorner: false);

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

            // Corner GIF: the pause closed it, so put it back. The per-second tick only re-raises
            // corner GIFs whose start minute is greater than zero, so a minute-0 one would
            // otherwise never come back after a pause. Admission is re-asked from scratch, so a
            // master unticked (or a standalone slot enabled) during the pause is honoured.
            if (_cornerGifWindow == null && CanRaiseCornerGif(settings))
            {
                double cornerMinutes = ElapsedTime.TotalMinutes;
                if (cornerMinutes >= settings.CornerGifStartMinute
                    && (settings.CornerGifEndMinute <= 0 || cornerMinutes < settings.CornerGifEndMinute))
                    ShowCornerGif(settings);
            }

            App.Logger?.Information("Session resumed");

            // EMI Desk (MOMENTS 4.B).
            try { App.EmiDesk?.Fire("sessionResumed", new { minutes = (int)Math.Round(RemainingTime.TotalMinutes) }); }
            catch { }
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

            // EMI Desk (MOMENTS 4.B): the two beats inside a run, each once. The bark's
            // SessionProgress context never carries the remaining time, which is the only number
            // either line wants, so they are fired here off the engine's own clock.
            try
            {
                double leftMinutes = totalMinutes - elapsedMinutes;
                if (!_emiSaidHalfway && elapsedMinutes >= totalMinutes / 2.0)
                {
                    _emiSaidHalfway = true;
                    App.EmiDesk?.Fire("sessionHalfway", new { minutes = (int)Math.Round(leftMinutes) });
                }
                if (!_emiSaidLastMinute && leftMinutes <= 1.0)
                {
                    _emiSaidLastMinute = true;
                    App.EmiDesk?.Fire("sessionLastMinute", null);
                }
            }
            catch { }
            
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

                // EMI Desk (MOMENTS 4.B).
                try
                {
                    App.EmiDesk?.Fire("sessionPhaseChanged", new
                    {
                        target = phase.Name?.ToLowerInvariant(),
                        n = newPhaseIndex + 1,
                    });
                }
                catch { }
            }
        }
        
        private void UpdateRampingValues(double elapsedMinutes, double totalMinutes)
        {
            if (_currentSession == null) return;
            var settings = _currentSession.Settings;
            // Easing curve (#660): per-session override if the preset sets one, else the global setting.
            var curve = settings.RampCurve ?? App.Settings.Current.RampCurve;
            var progress = RampCurves.ApplyCurve(elapsedMinutes / totalMinutes, curve);

            // Flash trio: opacity ramp, frequency ramp, and the session's fixed scale.
            //
            // Parked on the settings object as a session overlay rather than written into it, for
            // the reason spelled out under the pink filter below: these are the USER's persisted
            // values, and an app kill mid-session used to freeze a ramp maximum into settings.json
            // for good. FlashService still reads the ramped numbers - the getters prefer the
            // overlay - while the file, the sliders and RestoreSettings keep the user's own.
            int? rampedOpacity = null;
            int? rampedFrequency = null;
            int? rampedScale = null;

            if (settings.FlashEnabled && settings.FlashOpacity != settings.FlashOpacityEnd)
            {
                _currentFlashOpacity = Lerp(settings.FlashOpacity, settings.FlashOpacityEnd, progress);
                rampedOpacity = (int)_currentFlashOpacity;
            }

            // Flash frequency ramp (for sessions like Good Girls Don't Cum)
            if (settings.FlashEnabled && settings.FlashPerHour != settings.FlashPerHourEnd)
            {
                rampedFrequency = (int)Lerp(settings.FlashPerHour, settings.FlashPerHourEnd, progress);
            }

            // Flash scale (fixed for the whole session when the preset sets one)
            if (settings.FlashEnabled && settings.FlashScale != 100)
            {
                rampedScale = settings.FlashScale;
            }

            App.Settings?.Current?.SetSessionFlashRamp(rampedOpacity, rampedFrequency, rampedScale);


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

                    // EMI Desk (MOMENTS 4.B): the ONE genuine integer step in the whole ramp
                    // machinery. Everything else that ramps is a per-second lerp with no step to
                    // count, and MOMENTS 3 forbids her claiming a number that is not real.
                    if (rampSteps > _emiRampStep)
                    {
                        _emiRampStep = rampSteps;
                        try { App.EmiDesk?.Fire("rampStepUp", new { n = rampSteps }); } catch { }
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

                    // EMI Desk (MOMENTS 4.B): pending.Name is already a human feature name
                    // ("lock cards", "bubble count"), so it needs no key mapping.
                    try
                    {
                        App.EmiDesk?.Fire("sessionFeatureArrived", new
                        {
                            target = pending.Name?.ToLowerInvariant(),
                            n = (int)pending.StartMinute,
                        });
                    }
                    catch { }
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

            // Corner GIF: the user master is honoured LIVE, so unticking "Allow session corner
            // GIFs" mid-session takes the overlay off the screen instead of waiting for the next
            // session. Same for a standalone corner overlay the user switches on mid-session: the
            // session yields rather than stacking a second spiral behind it.
            if (_cornerGifWindow != null && !CornerGifMedia.AllowSessionCornerGif(
                    settings.CornerGifEnabled, SessionCornerGifAllowedByUser, StandaloneCornerGifActive))
            {
                CloseCornerGif();
                App.Logger?.Information("Corner GIF hidden mid-session (user master or a standalone corner overlay now owns the corner)");
            }

            // Corner GIF delayed start
            if (_cornerGifWindow == null && settings.CornerGifStartMinute > 0 && CanRaiseCornerGif(settings))
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
                {
                    saved[kvp.Key] = kvp.Value;                     // added while the session ran
                }
                else if (saved.ContainsKey(kvp.Key))
                {
                    // Differs from what the session prescribed: an unambiguous re-toggle of one of
                    // their own phrases.
                    //
                    // EQUAL to what the session prescribed is ambiguous, and stays ambiguous: the
                    // phrase editor is seeded from the LIVE pool, so the user may have ticked the
                    // box onto the session's value on purpose, or may simply have saved an
                    // unrelated edit with the session's own value standing. Adopt only the ENABLED
                    // side of that ambiguity - ApplySessionSettings disables every pre-existing
                    // phrase and enables only its own, so adopting a `false` here would silence
                    // the user's whole pool the moment they save any mid-session edit, while
                    // adopting a `true` at worst leaves one phrase they already own switched on,
                    // and it is the half the fold actually loses today (ticking a phrase the
                    // session happens to run too, which then reverted at session end).
                    if (wasEnabled != kvp.Value || kvp.Value) saved[kvp.Key] = kvp.Value;
                }
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
        
        /// <summary>How many times a corner-GIF spawn may be pushed back because a display change is
        /// in flight before it is abandoned. 8 x <see cref="CornerGifSpawnRetryMs"/> covers a monitor
        /// drag - the same budget CornerGifService.ScheduleRealize uses.</summary>
        private const int CornerGifSpawnDeferMaxAttempts = 8;
        private const int CornerGifSpawnRetryMs = 250;

        /// <summary>
        /// The user's master for session-scoped corner GIFs. Defaults to TRUE when settings are
        /// unavailable, so a missing settings object cannot silently disable a program day's art.
        /// </summary>
        private static bool SessionCornerGifAllowedByUser
            => App.Settings?.Current?.SessionCornerGifAllowed != false;

        /// <summary>A standalone corner overlay (Spiral card - "Corner GIFs") is already up or queued.</summary>
        private static bool StandaloneCornerGifActive
        {
            get
            {
                try { return App.CornerGif?.HasActiveOverlays == true; }
                catch { return false; }
            }
        }

        /// <summary>
        /// Full admission check for the session-scoped corner GIF: the template asked for it, the
        /// USER still allows it, and the corner is not already occupied by a standalone overlay.
        /// The template flag alone used to be the whole gate - ticket 1539282547484139682, where
        /// the documented "turn the Corner GIF off" workaround did nothing for program days and
        /// two spirals could end up on screen at once.
        /// </summary>
        private static bool CanRaiseCornerGif(SessionSettings settings)
            => settings != null && CornerGifMedia.AllowSessionCornerGif(
                   settings.CornerGifEnabled, SessionCornerGifAllowedByUser, StandaloneCornerGifActive);

        /// <summary>
        /// TRUE while a session-scoped corner GIF is on screen. Read by CornerGifService so a
        /// standalone slot never realizes behind the session's overlay. Plain volatile bool: it is
        /// read from RefreshOverlays on the UI thread and must stay cheap and lock-free.
        /// </summary>
        internal static bool IsSessionCornerGifActive => _sessionCornerGifActive;
        private static volatile bool _sessionCornerGifActive;

        /// <summary>
        /// TRUE while this session still owes the user's standalone corner slots a handback: it
        /// took the corner, they stood down for it, and nothing has re-queued them yet. Set when
        /// the overlay is shown, cleared only when the handback actually runs, and deliberately
        /// NOT cleared by a hide-only close - a pause or a panic press hides the overlay without
        /// ending the session's claim, and the eventual terminal close must still pay the debt.
        /// </summary>
        private bool _cornerHandbackOwed;

        /// <summary>
        /// Re-applies the corner-GIF admission rules right now, for the settings toggle: a user who
        /// unticks "Allow session corner GIFs" should see the overlay go, not wait for the next
        /// tick (and the tick does not run at all while a session is paused).
        /// </summary>
        public void RefreshCornerGifPolicy()
        {
            // Re-entrancy guard. CornerGifService.RefreshOverlays now ends by calling this (so the
            // dedupe re-resolves when the STANDALONE side changes, not only the session side), and
            // the close below can hand the corner back, which calls RefreshOverlays again. One
            // bounce is all this ever needs; the flag stops the pair from ping-ponging.
            if (_refreshingCornerGifPolicy) return;
            _refreshingCornerGifPolicy = true;
            try
            {
                var settings = _currentSession?.Settings;
                if (settings == null) return;
                if (_cornerGifWindow != null && !CanRaiseCornerGif(settings)) { CloseCornerGif(); return; }
                // NOT while paused. A pause (the pause button, or the one the panic key triggers)
                // means "nothing on my screen", and this is now reached from the STANDALONE side
                // too - so without the guard, switching a corner slot off after a panic press would
                // put the session's spiral straight back up on a session the user had just stopped.
                // ResumeSession owns the re-raise; the end minute is honoured there and here alike.
                if (_cornerGifWindow == null && IsRunning && !IsPaused && CanRaiseCornerGif(settings)
                    && settings.CornerGifStartMinute == 0
                    && (settings.CornerGifEndMinute <= 0 || ElapsedTime.TotalMinutes < settings.CornerGifEndMinute))
                    ShowCornerGif(settings);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RefreshCornerGifPolicy failed");
            }
            finally
            {
                _refreshingCornerGifPolicy = false;
            }
        }

        /// <summary>Guards <see cref="RefreshCornerGifPolicy"/> against the CornerGifService
        /// handback calling straight back into it. UI thread only, like every other corner-GIF
        /// field on this engine.</summary>
        private bool _refreshingCornerGifPolicy;

        private void ShowCornerGif(SessionSettings settings) => ShowCornerGif(settings, 0);

        private void ShowCornerGif(SessionSettings settings, int deferAttempts)
        {
            try
            {
                // Last line of defence: every caller is expected to have asked CanRaiseCornerGif
                // first, but the retry timer, the size/path live-updaters and any future caller all
                // land here, and a corner overlay that ignores the user's master is the ticket.
                if (!CanRaiseCornerGif(settings))
                {
                    App.Logger?.Debug("Corner GIF not shown: the user master is off or a standalone corner overlay owns the corner");
                    return;
                }

                // Show() on a WS_EX_LAYERED (AllowsTransparency) window runs a synchronous
                // HwndTarget.OnResize -> MediaContext.CompleteRender on first realization, and doing
                // that while a monitor/DPI change is still settling is precisely the hazard
                // DisplayChangeCoordinator exists for. Every other layered-spawn path in the app
                // honours it (CornerGifService.ScheduleRealize, FlashService, BubbleService,
                // LockCardWindow) - the session-scoped corner GIF was the one that did not, and the
                // program days raise it at minute 0, i.e. while the session's other windows are all
                // being realized at once.
                if (Services.UI.DisplayChangeCoordinator.SpawnsSuppressed)
                {
                    if (deferAttempts < CornerGifSpawnDeferMaxAttempts)
                    {
                        ScheduleCornerGifRetry(settings, deferAttempts + 1);
                        return;
                    }
                    App.Logger?.Warning("Corner GIF: display change still in flight after {Attempts} deferrals - showing anyway", deferAttempts);
                }

                var gifPath = settings.CornerGifPath;
                Uri? gifUri = null;

                if (!string.IsNullOrEmpty(gifPath) && System.IO.File.Exists(gifPath))
                {
                    try { gifUri = new Uri(gifPath); }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("Failed to load corner GIF from file: {Error}", ex.Message);
                        gifUri = null;
                    }
                }

                // Fall back to the built-in corner art. NOTE this is deliberately no longer the
                // fullscreen spiral: no program template sets CornerGifPath, so every program day
                // that raises a corner GIF landed here, and ResolveSpiralUri hands back a 2400x1600
                // 32-frame GIF for a 70-300px overlay. CornerGifMedia keeps an active mod's own
                // spiral (branding) and otherwise resolves the pre-scaled corner asset.
                if (gifUri == null)
                {
                    try
                    {
                        gifUri = new Uri(CornerGifMedia.ResolveDefaultUriString(), UriKind.Absolute);
                        App.Logger?.Information("Corner GIF not set or found, defaulting to the built-in corner spiral");
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("Failed to resolve default corner GIF resource: {Error}", ex.Message);
                    }
                }

                if (gifUri == null)
                {
                    App.Logger?.Warning("Could not load corner GIF image - skipping corner GIF display");
                    return;
                }

                // Header-only dimension read (see CornerGifMedia.TryGetPixelSize). This used to be a
                // full GDI+ decode whose only product was Width/Height - the file was then decoded a
                // SECOND time by the animator.
                //
                // Bug #625: a degenerate (0x0) image makes the scale below divide by zero, and
                // assigning the resulting NaN/Infinity to Window.Width/Height throws deep inside WPF
                // layout. Bail out loudly instead of handing WPF non-finite geometry.
                if (!CornerGifMedia.TryGetPixelSize(gifUri, out var gifWidth, out var gifHeight)
                    || gifWidth <= 0 || gifHeight <= 0)
                {
                    App.Logger?.Warning("Corner GIF has unreadable or degenerate size {W}x{H} ({Path}) - skipping overlay",
                        gifWidth, gifHeight, gifUri);
                    return;
                }

                _cornerGifWidth = gifWidth;
                _cornerGifHeight = gifHeight;

                // Scale based on user's size setting (default 300)
                var targetSize = settings.CornerGifSize > 0 ? settings.CornerGifSize : 300;
                double scale = targetSize / Math.Max(gifWidth, gifHeight);
                double windowWidth = gifWidth * scale;
                double windowHeight = gifHeight * scale;

                if (!double.IsFinite(scale) || !double.IsFinite(windowWidth) || !double.IsFinite(windowHeight)
                    || windowWidth <= 0 || windowHeight <= 0)
                {
                    App.Logger?.Warning("Corner GIF computed non-finite overlay geometry (scale={Scale}, {W}x{H}) - skipping overlay",
                        scale, windowWidth, windowHeight);
                    return;
                }

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

                // Animated by CornerGifMedia (pre-scaled frozen frames), not by XamlAnimatedGif.
                var imageElement = new Image
                {
                    Stretch = System.Windows.Media.Stretch.Uniform
                };

                // Per-frame downscale quality (#954/#958/#984). Still set, but it is now a belt to
                // the braces: the frames CornerGifMedia hands over are already the overlay's size,
                // so there is no per-frame downscale left to filter.
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                    imageElement, System.Windows.Media.BitmapScalingMode.LowQuality);

                CornerGifMedia.WarnIfOversize("Corner GIF", gifUri, gifWidth, gifHeight, windowWidth, windowHeight);

                // THE fix for the program-day freeze. XamlAnimatedGif hands the render thread a
                // WriteableBitmap at the GIF's NATIVE size and WPF resamples it to the overlay's
                // size on EVERY frame, forever - on a layered window, whose composition is a full
                // UpdateLayeredWindow blit rather than a GPU surface flip, that saturates the render
                // thread, and a saturated render thread blocks the UI thread inside
                // WriteableBitmap.Lock (the CWGXBitmapLockState::LockRead wedge in UiHangWatchdog's
                // dump notes). #221/#227 only made the filter cheaper (Fant -> bilinear); with a
                // 3.84MP source resampled to 70-300px that was never going to be enough.
                //
                // CornerGifMedia decodes ONCE, OFF the UI thread, downscaled to the pixels this
                // overlay actually occupies and capped for frames and bytes - the same discipline
                // #572 gave the fullscreen spiral (OverlayService.DecodeGifFrames).
                CornerGifMedia.Attach(imageElement, gifUri, windowWidth, windowHeight, dpiScale);

                _cornerGifImage = imageElement;
                _cornerGifWindow.Content = imageElement;

                // Hook SourceInitialized BEFORE Show() to safely get the hwnd for click-through
                _cornerGifWindow.SourceInitialized += (s, e) =>
                {
                    MakeWindowClickThrough(_cornerGifWindow);
                };
                // Both of these are stamped BEFORE Show(). Show() on a layered window realizes its
                // render target synchronously, so a wedge INSIDE it must find the hang report
                // already describing this overlay - entered afterwards, the context only ever
                // named the corner GIF when the corner GIF had not frozen anything.
                // CloseCornerGif clears both on every exit path.
                _cornerGifDiag = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "1 window src={0}x{1} draw={2}x{3} at {4} ({5})",
                    (int)gifWidth, (int)gifHeight, (int)windowWidth, (int)windowHeight,
                    settings.CornerGifPosition,
                    gifUri.IsFile ? System.IO.Path.GetFileName(gifUri.LocalPath) : "built-in");

                HangContext.Enter("session.cornerGif");
                try
                {
                    using (VideoDiag.UiScope("SessionEngine.ShowCornerGif(layered Show)"))
                    {
                        _cornerGifWindow.Show();
                    }
                }
                catch
                {
                    // A throwing Show() would otherwise leave the context entered (every later hang
                    // report blames a corner GIF that is not on screen) and a topmost, click-through
                    // window the user cannot close.
                    CloseCornerGif();
                    throw;
                }

                _sessionCornerGifActive = true;
                // From here on this session OWES the user's own slots a handback: they stand
                // down (CornerGifMedia.AllowStandaloneCornerGif) for as long as we hold the
                // corner, and nothing but a handback re-queues them. The debt survives a
                // HIDE-only close (a pause, a panic press, a live size/path edit) and is
                // settled by the first close that means the session is done with the corner -
                // which is what stops a pause that is never resumed from stranding the slot.
                _cornerHandbackOwed = true;

                App.Logger?.Information("Corner GIF shown at {Position}: {Path} (pos: {Left},{Top}, size: {Width}x{Height}px, opacity: {Opacity}%)",
                    settings.CornerGifPosition, gifUri.ToString(), left, top, (int)windowWidth, (int)windowHeight, settings.CornerGifOpacity);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to show corner GIF");
            }
        }

        /// <summary>
        /// Re-attempts a corner-GIF spawn that was pushed back by a display change. The per-second
        /// tick only re-tries corner GIFs whose StartMinute is greater than zero, and every program
        /// day that raises one at minute 0 (Presentation day 14, Takeover day 28) would otherwise
        /// lose it for the whole session.
        /// </summary>
        private void ScheduleCornerGifRetry(SessionSettings settings, int deferAttempts)
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;

            var timer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background, disp)
            {
                Interval = TimeSpan.FromMilliseconds(CornerGifSpawnRetryMs)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                // The session may have ended, or the end-minute may have closed the overlay, while
                // this sat in the queue.
                if (!IsRunning || _cornerGifWindow != null || !CanRaiseCornerGif(settings)) return;
                ShowCornerGif(settings, deferAttempts);
            };
            timer.Start();
        }

        /// <summary>
        /// The session-scoped corner GIF's hwnd, for OverlayService's z-order sweep. UI thread only
        /// (Window/WindowInteropHelper are thread-affine); IntPtr.Zero when there is none.
        /// </summary>
        internal IntPtr GetCornerGifHandle()
        {
            try
            {
                var window = _cornerGifWindow;
                if (window == null) return IntPtr.Zero;
                if (Application.Current?.Dispatcher?.CheckAccess() != true) return IntPtr.Zero;
                return new System.Windows.Interop.WindowInteropHelper(window).Handle;
            }
            catch { return IntPtr.Zero; }
        }

        /// <summary>
        /// One line about the SESSION's corner GIF for the hang report.
        ///
        /// <para>HangContext already printed <c>cornerGifWindows=</c> off CornerGifService - but the
        /// program days raise this window instead, and CornerGifService knows nothing about it, so
        /// every program-day hang report said "0+0pending" while a corner GIF was on screen driving
        /// the render thread. That blind spot is why the freeze survived two fix rounds.</para>
        ///
        /// <para>Read on the WATCHDOG thread while the UI thread may be wedged: it must therefore be
        /// a plain volatile field read and nothing else - no Window property access (thread-affine),
        /// no dispatcher marshalling, no locks.</para>
        /// </summary>
        internal static string DescribeSessionCornerGif()
        {
            var engine = Active;
            if (engine == null) return "(no session)";
            return engine._cornerGifDiag ?? "none";
        }

        /// <summary>Lock-free snapshot behind <see cref="DescribeSessionCornerGif"/>.</summary>
        private volatile string? _cornerGifDiag;

        /// <summary>
        /// The panic key's door to the session-scoped corner overlay (v6.8.5, ticket
        /// 1539282547484139682 / suggestion thread 1541736938703167550). The stop-everything pass
        /// calls <c>App.CornerGif.StopAll()</c>, but CornerGifService owns only the STANDALONE
        /// Spiral-card slots - the session's overlay is this engine's own window, and nothing on
        /// the panic path could reach it, so on a program day with Corner GIF at minute 0 one
        /// panic press stopped everything else and left the session spiral spinning.
        ///
        /// <para>Close only, never a re-show - and that includes the handback
        /// (<c>handBackCorner: false</c>). Handing the corner back here made the panic key
        /// re-realize the user's OWN standalone corner spiral one dispatcher pass after the
        /// stop-all sweep had just closed it, so the headline promise ("one press takes every
        /// surface down") failed on exactly the spiral the ticket is about. The debt is remembered
        /// (<c>_cornerHandbackOwed</c>) and settled when the session actually ends. If the session
        /// survives the press, the normal admission checks (the tick,
        /// <see cref="RefreshCornerGifPolicy"/>, ResumeSession) decide whether it comes back.
        /// Never throws.</para>
        /// </summary>
        public void PanicCloseCornerGif()
        {
            try { CloseCornerGif(handBackCorner: false); }
            catch (Exception ex) { try { App.Logger?.Warning(ex, "PanicCloseCornerGif failed"); } catch { } }
        }

        private void CloseCornerGif() => CloseCornerGif(handBackCorner: true);

        /// <summary>
        /// Tears the session-scoped corner overlay down.
        /// </summary>
        /// <param name="handBackCorner">TRUE (every TERMINAL close - the session is done with the
        /// corner) also re-realizes the user's own standalone corner slots, which stood down while
        /// this overlay owned the corner (CornerGifMedia.AllowStandaloneCornerGif). Without the
        /// handback, a slot enabled mid-session stayed invisible for the rest of the session
        /// however often the user toggled it - only session END gave the corner back.
        ///
        /// <para>FALSE for every HIDE-only close, where the session still means to own the corner
        /// or must not repaint anything right now: the two live editors (UpdateCornerGifSize /
        /// UpdateCornerGifPath, which close and immediately re-Show the same overlay - a queued
        /// standalone slot counts as StandaloneCornerGifActive, so the re-Show would refuse),
        /// <see cref="PauseSession"/> (resume has to be able to take the corner back) and
        /// <see cref="PanicCloseCornerGif"/> (a panic must not re-show a spiral).</para>
        ///
        /// <para>A hide-only close does not cancel the debt: <c>_cornerHandbackOwed</c> stays set,
        /// so the first terminal close afterwards still gives the user's slots the corner back even
        /// if the window is already gone. That is why the gate below is the debt and not
        /// <c>_cornerGifWindow != null</c> - a session paused by a panic and then stopped must
        /// still hand back.</para></param>
        private void CloseCornerGif(bool handBackCorner)
        {
            // Release the animator BEFORE closing the window. RepeatBehavior.Forever installs a
            // clock that keeps pushing frames at the render thread and pins the Image (and its
            // native-size WriteableBitmap) for as long as the source is set - Close() alone does
            // not tear it down. Every other XamlAnimatedGif site in the app clears the source on
            // teardown for exactly this reason (BlinkTrainerService, ChaosGifCascadeOverlay,
            // AvatarTubeWindow, MiniPlayerWindow); the corner GIF was the one that didn't, and it
            // is rebuilt on every size/path change mid-session (UpdateCornerGifSize/Path), so the
            // orphaned animators accumulate across a long program day.
            if (_cornerGifImage != null)
            {
                try { CornerGifMedia.Detach(_cornerGifImage); } catch { }
                try { _cornerGifImage.Source = null; } catch { }
            }
            if (_cornerGifWindow != null)
            {
                try { _cornerGifWindow.Content = null; } catch { }
                using (VideoDiag.UiScope("SessionEngine.CloseCornerGif(layered Close)"))
                {
                    _cornerGifWindow.Close();
                }
                _cornerGifWindow = null;
            }
            HangContext.Leave("session.cornerGif");
            _sessionCornerGifActive = false;
            _cornerGifDiag = null;
            _cornerGifImage = null;
            _cornerGifWidth = 0;
            _cornerGifHeight = 0;

            // The corner is free again: hand it back to the user's own slots - but ONLY if this
            // session ever took it (otherwise a close that closed nothing would fire a full
            // StopAll + re-Show burst across every standalone slot, which is the Close,Close,
            // Show,Show shape on AllowsTransparency windows that CornerGifService.QueueShow's own
            // doc comment names as #494/#709/#958). Best effort - a failed handback must never
            // propagate out of a teardown that also runs on the panic path.
            if (handBackCorner && _cornerHandbackOwed)
            {
                _cornerHandbackOwed = false;
                try { App.CornerGif?.RefreshOverlays(); } catch { /* corner handback is best-effort */ }
            }
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
                CloseCornerGif(handBackCorner: false); // a recreate, not a teardown - keep the corner
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
                CloseCornerGif(handBackCorner: false); // a recreate, not a teardown - keep the corner
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
