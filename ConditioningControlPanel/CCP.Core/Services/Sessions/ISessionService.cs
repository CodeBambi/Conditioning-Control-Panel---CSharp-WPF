using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Sessions;

/// <summary>
/// Cross-platform session state-machine.
/// Tracks elapsed time, phase transitions, pause count, and XP calculation.
/// UI heads subscribe to events to drive overlays, audio, and other effects.
/// </summary>
public interface ISessionService
{
    SessionState State { get; }
    ConditioningControlPanel.Models.Session? CurrentSession { get; }
    TimeSpan ElapsedTime { get; }
    TimeSpan RemainingTime { get; }
    double ProgressPercent { get; }
    int CurrentPhaseIndex { get; }
    int PauseCount { get; }
    int XPPenalty { get; }

    /// <summary>
    /// Snapshot of settings captured at session start (for achievements / strict-lock checks).
    /// </summary>
    bool SessionStartStrictLock { get; }

    /// <summary>
    /// Snapshot of panic-key setting captured at session start.
    /// </summary>
    bool SessionStartPanicKey { get; }

    event EventHandler? SessionStarted;
    event EventHandler<SessionStoppedEventArgs>? SessionStopped;
    event EventHandler<SessionCompletedEventArgs>? SessionCompleted;
    event EventHandler? SessionPaused;
    event EventHandler? SessionResumed;
    event EventHandler<SessionPhaseChangedEventArgs>? PhaseChanged;
    event EventHandler<SessionProgressEventArgs>? ProgressUpdated;

    /// <summary>
    /// Start a session. Throws if a session is already running.
    /// </summary>
    Task StartSessionAsync(ConditioningControlPanel.Models.Session session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the current session. Completes it if <paramref name="completed"/> is true.
    /// </summary>
    void StopSession(bool completed = false);

    /// <summary>
    /// Pause the current running session.
    /// </summary>
    void PauseSession();

    /// <summary>
    /// Resume a paused session.
    /// </summary>
    void ResumeSession();
}
