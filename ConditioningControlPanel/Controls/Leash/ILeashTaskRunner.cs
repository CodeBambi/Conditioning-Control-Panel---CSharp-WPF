using System;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// What the punishment gate needs from whoever runs the task behind it (N lock cards, a pink
/// session, a bubble quota, a detention session, a video watch). The CORE lane implements it
/// (<c>LeashTaskRunner</c>) and hands it over through <see cref="LeashLocator.Runner"/>; the gate
/// only presses Start and listens.
///
/// <para>Contract: events are raised on the UI thread. The runner never posts <c>complete</c>
/// itself: the gate host calls <see cref="ILeashService.CompleteAsync"/> when
/// <see cref="Completed"/> fires. <see cref="Start"/> is never called for a Chaster punishment
/// (those book on the tab and complete on arrival).</para>
/// </summary>
public interface ILeashTaskRunner
{
    /// <summary>True while a task is under way (the gate steps aside while it runs).</summary>
    bool IsRunning { get; }

    /// <summary>The punishment being worked on, or null.</summary>
    string? RunningPid { get; }

    /// <summary>Starts the task. False when it cannot start now (the gate stays up and says so).</summary>
    bool Start(Punishment punishment);

    /// <summary>Drops a running task without completing it and forgets its progress (a cut,
    /// the leash ended, the punishment went away).</summary>
    void Cancel();

    /// <summary>Stops a running task but keeps its progress for a later <see cref="Start"/> of the
    /// same punishment (a panic press). Nothing it drives comes back by itself.</summary>
    void Park() => Cancel();

    /// <summary>The open video assignment being watched, or null.</summary>
    string? RunningAid => null;

    /// <summary>Opens today's video assignment and watches it for real. False when it cannot now.</summary>
    bool StartAssignmentWatch(Assignment assignment) => false;

    /// <summary>The runner went idle by itself (the activity stopped, the next card would not
    /// open, the video will not play). The task stays pending.</summary>
    event Action<LeashTaskStopped>? Stopped { add { } remove { } }

    /// <summary>pid, done, total. Lines count cards, sessions count minutes, bubbles count pops.</summary>
    event Action<string, int, int>? Progress;

    /// <summary>pid. The task is done; the gate host posts <c>complete</c>.</summary>
    event Action<string>? Completed;

    /// <summary>True when the last <see cref="Completed"/> was a video stopped by its time cap.</summary>
    bool LastCompletionCapped => false;
}
