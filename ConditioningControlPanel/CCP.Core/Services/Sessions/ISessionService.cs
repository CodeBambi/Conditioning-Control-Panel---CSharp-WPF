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

    /// <summary>
    /// Queue a deferred feature start for the timeline editor's "start at minute X"
    /// events (#483). <paramref name="fire"/> runs on the session tick once
    /// <paramref name="startMinute"/> minutes of session time have elapsed; stopping the
    /// session drops unfired entries. Mirrors WPF SessionEngine.DeferFeatureStart
    /// (SessionEngine.cs:868-872). Default implementation is a no-op so fakes keep compiling.
    /// </summary>
    void DeferFeatureStart(string name, int startMinute, Action fire) { }

    /// <summary>
    /// True while a start queued via <see cref="DeferFeatureStart"/> has not fired yet.
    /// Resume paths use this to avoid prematurely starting deferred features.
    /// Mirrors WPF SessionEngine.IsFeaturePending (SessionEngine.cs:874-875).
    /// </summary>
    bool IsFeaturePending(string name) => false;
}
