using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Sessions;

/// <summary>
/// Cross-platform seam that starts and stops the active session's feature effects
/// (flash, video, bubbles, overlays, etc.) in one coordinated operation.
/// </summary>
public interface ISessionEffectOrchestrator
{
    /// <summary>Start all effects enabled by the given session.</summary>
    void StartEffects(Session session);

    /// <summary>
    /// Start effects for a fresh session start, or restart them after a pause when
    /// <paramref name="resuming"/> is true. A resume restarts the stimuli per session
    /// settings (WPF SessionEngine.ResumeSession parity) but skips one-time session-start
    /// work such as burst scheduling and autonomy arming.
    /// Default implementation falls back to a fresh <see cref="StartEffects(Session)"/>.
    /// </summary>
    void StartEffects(Session session, bool resuming) => StartEffects(session);

    /// <summary>Stop all running session effects.</summary>
    void StopEffects();

    /// <summary>
    /// Stop effects. When <paramref name="pausing"/> is true this is a session pause
    /// (WPF SessionEngine.PauseSession parity): session-scoped stimuli stop but
    /// engine-lifetime concerns (autonomy arm state, system audio unduck) are untouched.
    /// Default implementation falls back to a full <see cref="StopEffects()"/>.
    /// </summary>
    void StopEffects(bool pausing) => StopEffects();

    /// <summary>
    /// Per-second scheduling hook driven by the session tick (delayed feature starts,
    /// intermittent bubble bursts). Called only while the session is running.
    /// </summary>
    void TickEffects(Session session, TimeSpan elapsed);
}
