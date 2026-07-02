using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Sessions;

/// <summary>
/// Session lifecycle states.
/// </summary>
public enum SessionState
{
    Idle,
    Running,
    Paused,
    Completed
}

/// <summary>
/// Event args raised when the active session phase changes.
/// </summary>
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

/// <summary>
/// Event args raised each tick with current session progress.
/// </summary>
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

/// <summary>
/// Event args raised when a session stops (both early stop and natural completion).
/// Carries the final elapsed time captured before state reset, because
/// <c>ISessionService.ElapsedTime</c> returns zero once the service is idle.
/// </summary>
public class SessionStoppedEventArgs : EventArgs
{
    public TimeSpan FinalElapsed { get; }
    public bool Completed { get; }

    public SessionStoppedEventArgs(TimeSpan finalElapsed, bool completed)
    {
        FinalElapsed = finalElapsed;
        Completed = completed;
    }
}

/// <summary>
/// Event args raised when a session completes successfully.
/// </summary>
public class SessionCompletedEventArgs : EventArgs
{
    public ConditioningControlPanel.Models.Session Session { get; }
    public TimeSpan Duration { get; }
    public int XPEarned { get; }
    public int PauseCount { get; }
    public int XPPenalty => PauseCount * 100;

    public SessionCompletedEventArgs(ConditioningControlPanel.Models.Session session, TimeSpan duration, int xp, int pauseCount = 0)
    {
        Session = session;
        Duration = duration;
        XPEarned = xp;
        PauseCount = pauseCount;
    }
}
