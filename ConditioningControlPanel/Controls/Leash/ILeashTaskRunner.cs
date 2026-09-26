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

    /// <summary>Drops a running task without completing it (a cut, a panic).</summary>
    void Cancel();

    /// <summary>pid, done, total. Lines count cards, sessions count minutes, bubbles count pops.</summary>
    event Action<string, int, int>? Progress;

    /// <summary>pid. The task is done; the gate host posts <c>complete</c>.</summary>
    event Action<string>? Completed;
}
