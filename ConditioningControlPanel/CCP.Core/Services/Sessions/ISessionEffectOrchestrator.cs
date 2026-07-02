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

    /// <summary>Stop all running session effects.</summary>
    void StopEffects();

    /// <summary>
    /// Per-second scheduling hook driven by the session tick (delayed feature starts,
    /// intermittent bubble bursts). Called only while the session is running.
    /// </summary>
    void TickEffects(Session session, TimeSpan elapsed);
}
