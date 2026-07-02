using System.Diagnostics;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.SessionLog;
using ConditioningControlPanel.Core.Services.Settings;
using Serilog;

namespace ConditioningControlPanel.Core.Services.Sessions;

/// <summary>
/// Portable session state-machine implementation.
/// Owns the full WPF SessionEngine lifecycle contract: timing, phase transitions,
/// pause count, XP calculation, settings apply/restore, per-session ramps,
/// session-log begin/end, and achievement lifecycle hooks.
/// UI heads subscribe to events and drive effects accordingly.
/// </summary>
public sealed class SessionService : ISessionService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IProgressionService _progression;
    private readonly ISessionLogService? _sessionLog;
    private readonly IAchievementService? _achievements;
    private readonly ISessionEffectOrchestrator? _effectOrchestrator;
    private readonly SessionSettingsScope _settingsScope = new();
    private readonly Random _random = new();
    private readonly Stopwatch _wallClockStopwatch = new();

    private ConditioningControlPanel.Models.Session? _currentSession;
    private SessionState _state = SessionState.Idle;
    private DispatcherTimer? _tickSubscription;

    private DateTime _startTime;
    private TimeSpan _pausedElapsedTime;
    private int _currentPhaseIndex;
    private int _pauseCount;
    private bool _divergenceWarned;

    private bool _sessionStartStrictLock;
    private bool _sessionStartPanicKey;

    // Randomized delayed-start times (WPF SessionEngine.RandomizeStartTimes parity).
    private double _randomizedPinkStartMinute;
    private double _randomizedSpiralStartMinute;

    public SessionState State => _state;
    public ConditioningControlPanel.Models.Session? CurrentSession => _currentSession;

    /// <summary>
    /// Randomized pink-filter/spiral delayed-start minutes for the current session.
    /// Exposed so the effect orchestrator's delayed enables use the same jittered minute
    /// as the ramp gating above (WPF SessionEngine shares one field between
    /// UpdateRampingValues and CheckDelayedFeatures).
    /// </summary>
    public double RandomizedPinkStartMinute => _randomizedPinkStartMinute;
    /// <inheritdoc cref="RandomizedPinkStartMinute"/>
    public double RandomizedSpiralStartMinute => _randomizedSpiralStartMinute;
    public int CurrentPhaseIndex => _currentPhaseIndex;
    public int PauseCount => _pauseCount;
    public int XPPenalty => _pauseCount * 100;
    public bool SessionStartStrictLock => _sessionStartStrictLock;
    public bool SessionStartPanicKey => _sessionStartPanicKey;

    public TimeSpan ElapsedTime
    {
        get
        {
            if (_state == SessionState.Idle) return TimeSpan.Zero;
            if (_state == SessionState.Paused) return _pausedElapsedTime;

            var dateTimeElapsed = _pausedElapsedTime + (DateTime.Now - _startTime);
            var stopwatchElapsed = _wallClockStopwatch.Elapsed;

            var divergence = dateTimeElapsed - stopwatchElapsed;
            if (Math.Abs(divergence.TotalSeconds) > 30)
            {
                // Warn once per session: this getter runs several times per second and the
                // condition is persistent once a clock jump happens, so an unthrottled warning
                // floods the log (~21k lines/hour).
                if (!_divergenceWarned)
                {
                    _divergenceWarned = true;
                    Log.Warning("Timer integrity: DateTime elapsed {DateTimeElapsed} vs Stopwatch {StopwatchElapsed} — divergence {Divergence}s, using Stopwatch",
                        dateTimeElapsed, stopwatchElapsed, divergence.TotalSeconds);
                }
                return stopwatchElapsed;
            }

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

    public event EventHandler? SessionStarted;
    public event EventHandler<SessionStoppedEventArgs>? SessionStopped;
    public event EventHandler<SessionCompletedEventArgs>? SessionCompleted;
    public event EventHandler? SessionPaused;
    public event EventHandler? SessionResumed;
    public event EventHandler<SessionPhaseChangedEventArgs>? PhaseChanged;
    public event EventHandler<SessionProgressEventArgs>? ProgressUpdated;

    public SessionService(
        ISettingsService settings,
        IProgressionService progression,
        ISessionLogService? sessionLog = null,
        IAchievementService? achievements = null,
        ISessionEffectOrchestrator? effectOrchestrator = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _sessionLog = sessionLog;
        _achievements = achievements;
        _effectOrchestrator = effectOrchestrator;
    }

    public Task StartSessionAsync(ConditioningControlPanel.Models.Session session, CancellationToken cancellationToken = default)
    {
        if (_state != SessionState.Idle)
            throw new InvalidOperationException("A session is already running. Stop it first.");

        // The tick timer binds to the current thread's dispatcher; off the UI thread it would
        // silently never fire, so fail loudly instead.
        Dispatcher.UIThread.VerifyAccess();

        _currentSession = session;
        _state = SessionState.Running;
        _pauseCount = 0;
        _pausedElapsedTime = TimeSpan.Zero;
        _startTime = DateTime.Now;
        _wallClockStopwatch.Restart();
        _currentPhaseIndex = 0;
        _divergenceWarned = false;

        // Capture achievement-relevant settings NOW (immune to mid-session changes).
        _sessionStartStrictLock = _settings.Current.StrictLockEnabled;
        _sessionStartPanicKey = _settings.Current.PanicKeyEnabled;

        // Randomize delayed start times, then overwrite live settings with the session preset
        // (snapshot is restored in StopSession before SessionStopped fires).
        RandomizeStartTimes(session);
        _settingsScope.Apply(_settings.Current, session.Settings);

        _settings.Current.TotalSessions++;
        RecordSeasonFeatureUse(session);
        _settings.Save();

        _tickSubscription = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickSubscription.Tick += (_, _) => OnTick();
        _tickSubscription.Start();

        _achievements?.TrackSessionStart();

        // Begin capturing the post-session media log (videos played + flash images shown).
        _sessionLog?.BeginSession(session);

        if (session.Phases.Count > 0)
        {
            PhaseChanged?.Invoke(this, new SessionPhaseChangedEventArgs(session.Phases[0], 0));
        }

        SessionStarted?.Invoke(this, EventArgs.Empty);
        Log.Information("Session started: {Name}", session.Name);

        return Task.CompletedTask;
    }

    public void StopSession(bool completed = false)
    {
        if (_state == SessionState.Idle) return;

        // Capture elapsed BEFORE resetting state (ElapsedTime returns Zero when idle).
        var finalElapsedTime = ElapsedTime;
        _state = SessionState.Idle;
        _wallClockStopwatch.Stop();
        _tickSubscription?.Stop();
        _tickSubscription = null;

        // Restore the user's pre-session settings BEFORE SessionStopped fires (WPF parity).
        _settingsScope.Restore(_settings.Current);

        SessionStopped?.Invoke(this, new SessionStoppedEventArgs(finalElapsedTime, completed));

        if (!completed)
        {
            _achievements?.TrackSessionAbandoned();
        }

        int xpForLog = 0;
        if (completed && _currentSession != null)
        {
            int baseXP = Math.Max(0, Math.Min(2500, _currentSession.BonusXP) - XPPenalty);
            int level = _settings.Current.PlayerLevel;
            double multiplier = _progression.GetSessionXPMultiplier(level);
            double durationMinutes = Math.Max(0, finalElapsedTime.TotalMinutes - 2);
            int durationBonus = (int)Math.Round(durationMinutes * (8 + level * 0.15));
            int finalXP = Math.Max(0, (int)Math.Round(baseXP * multiplier) + durationBonus);
            xpForLog = finalXP;

            Log.Information("Session completed: {Name}, XP: {XP} (base: {Base}, multiplier: {Mult:F2}x, paused {PauseCount}x, penalty: -{Penalty})",
                _currentSession.Name, finalXP, baseXP, multiplier, _pauseCount, XPPenalty);

            // Use the settings snapshot captured at session start, not the live (already
            // restored) settings, so mid-session toggles can't skew achievement checks.
            _achievements?.TrackSessionComplete(
                _currentSession.Name,
                finalElapsedTime.TotalMinutes,
                !_sessionStartPanicKey,
                _sessionStartStrictLock);

            try
            {
                SessionCompleted?.Invoke(this, new SessionCompletedEventArgs(
                    _currentSession, finalElapsedTime, finalXP, _pauseCount));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in SessionCompleted event handler");
            }
        }
        else
        {
            Log.Information("Session stopped early");
            _achievements?.TrackPanicPressed();
        }

        // Finalize the media log AFTER XP is settled so the persisted log records the actual
        // award. Aborted sessions still get a log (xpForLog == 0), mirroring WPF.
        try { _sessionLog?.EndSession(completed, finalElapsedTime, xpForLog); }
        catch (Exception ex) { Log.Error(ex, "SessionLog.EndSession failed"); }

        _currentSession = null;
    }

    public void PauseSession()
    {
        if (_state != SessionState.Running || _currentSession == null) return;

        _pausedElapsedTime = ElapsedTime;
        _state = SessionState.Paused;
        _pauseCount++;
        _wallClockStopwatch.Stop();
        _tickSubscription?.Stop();
        _tickSubscription = null;

        Log.Information("Session paused (pause #{Count}, -100 XP penalty)", _pauseCount);
        SessionPaused?.Invoke(this, EventArgs.Empty);
    }

    public void ResumeSession()
    {
        if (_state != SessionState.Paused || _currentSession == null) return;

        Dispatcher.UIThread.VerifyAccess();

        _state = SessionState.Running;
        _startTime = DateTime.Now;
        _wallClockStopwatch.Start();
        _tickSubscription = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickSubscription.Tick += (_, _) => OnTick();
        _tickSubscription.Start();

        Log.Information("Session resumed");
        SessionResumed?.Invoke(this, EventArgs.Empty);
    }

    private void OnTick()
    {
        if (_state != SessionState.Running || _currentSession == null) return;

        var elapsed = ElapsedTime;
        var elapsedMinutes = elapsed.TotalMinutes;
        var totalMinutes = _currentSession.DurationMinutes;

        if (elapsedMinutes >= totalMinutes)
        {
            StopSession(completed: true);
            return;
        }

        ProgressUpdated?.Invoke(this, new SessionProgressEventArgs(
            elapsed, RemainingTime, ProgressPercent));

        CheckPhaseTransition(elapsedMinutes);
        UpdateRampingValues(elapsedMinutes, totalMinutes);

        try
        {
            _effectOrchestrator?.TickEffects(_currentSession, elapsed);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Session effect tick failed");
        }
    }

    private void CheckPhaseTransition(double elapsedMinutes)
    {
        if (_currentSession?.Phases == null) return;

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
            Log.Information("Phase changed: {Phase}", phase.Name);
        }
    }

    /// <summary>
    /// Randomizes delayed start times by ±3 minutes (clamped to min 0).
    /// Mirrors WPF SessionEngine.RandomizeStartTimes.
    /// </summary>
    private void RandomizeStartTimes(ConditioningControlPanel.Models.Session session)
    {
        var settings = session.Settings;

        if (settings.PinkFilterEnabled && settings.PinkFilterStartMinute > 0)
        {
            var offset = (_random.NextDouble() * 6) - 3; // -3 to +3
            _randomizedPinkStartMinute = Math.Max(0, settings.PinkFilterStartMinute + offset);
        }
        else
        {
            _randomizedPinkStartMinute = settings.PinkFilterStartMinute;
        }

        if (settings.SpiralEnabled && settings.SpiralStartMinute > 0)
        {
            var offset = (_random.NextDouble() * 6) - 3; // -3 to +3
            _randomizedSpiralStartMinute = Math.Max(0, settings.SpiralStartMinute + offset);
        }
        else
        {
            _randomizedSpiralStartMinute = settings.SpiralStartMinute;
        }

        Log.Debug("Randomized start times - Pink: {Pink:F1}min, Spiral: {Spiral:F1}min",
            _randomizedPinkStartMinute, _randomizedSpiralStartMinute);
    }

    /// <summary>
    /// Interpolates session start->end values into the live settings each tick so running
    /// effect services pick them up. Mirrors WPF SessionEngine.UpdateRampingValues minus
    /// the WPF-only UI calls.
    /// </summary>
    private void UpdateRampingValues(double elapsedMinutes, double totalMinutes)
    {
        if (_currentSession == null) return;
        var settings = _currentSession.Settings;
        var current = _settings.Current;
        var progress = elapsedMinutes / totalMinutes;

        // Flash opacity ramp
        if (settings.FlashEnabled && settings.FlashOpacity != settings.FlashOpacityEnd)
        {
            current.FlashOpacity = (int)Lerp(settings.FlashOpacity, settings.FlashOpacityEnd, progress);
        }

        // Flash frequency ramp
        if (settings.FlashEnabled && settings.FlashPerHour != settings.FlashPerHourEnd)
        {
            current.FlashFrequency = (int)Lerp(settings.FlashPerHour, settings.FlashPerHourEnd, progress);
        }

        // Flash scale (apply once at start if set)
        if (settings.FlashEnabled && settings.FlashScale != 100)
        {
            current.ImageScale = settings.FlashScale;
        }

        // Pink filter ramp (only after randomized start minute)
        if (settings.PinkFilterEnabled && elapsedMinutes >= _randomizedPinkStartMinute)
        {
            var pinkDuration = totalMinutes - _randomizedPinkStartMinute;
            var pinkProgress = Math.Clamp((elapsedMinutes - _randomizedPinkStartMinute) / pinkDuration, 0, 1);
            current.PinkFilterOpacity = (int)Lerp(settings.PinkFilterStartOpacity, settings.PinkFilterEndOpacity, pinkProgress);
        }

        // Spiral ramp (only after randomized start minute)
        if (settings.SpiralEnabled && elapsedMinutes >= _randomizedSpiralStartMinute)
        {
            var spiralDuration = totalMinutes - _randomizedSpiralStartMinute;
            var spiralProgress = Math.Clamp((elapsedMinutes - _randomizedSpiralStartMinute) / spiralDuration, 0, 1);
            current.SpiralOpacity = (int)Lerp(settings.SpiralOpacity, settings.SpiralOpacityEnd, spiralProgress);
        }

        // Bubble frequency ramp (stepped: +1 every 5 minutes past the start minute)
        if (settings.BubblesEnabled && !settings.BubblesIntermittent && settings.BubblesStartMinute > 0
            && elapsedMinutes >= settings.BubblesStartMinute)
        {
            var timeSinceBubbleStart = elapsedMinutes - settings.BubblesStartMinute;
            var rampSteps = (int)(timeSinceBubbleStart / 5);
            var currentBubbleFreq = settings.BubblesFrequency + rampSteps;

            if (current.BubblesFrequency != currentBubbleFreq)
            {
                current.BubblesFrequency = currentBubbleFreq;
                CoreApp.Bubbles?.RefreshFrequency();
            }
        }
    }

    private static double Lerp(double start, double end, double progress) => start + (end - start) * progress;

    private static void RecordSeasonFeatureUse(ConditioningControlPanel.Models.Session session)
    {
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
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SeasonRecap: failed to record session feature use");
        }
    }

    public void Dispose()
    {
        StopSession(false);
        _tickSubscription?.Stop();
        _tickSubscription = null;
    }
}
