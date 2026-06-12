using System;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// A single conditioning/effect event recorded for the AI's short-term memory.
    /// Kept in memory only and pruned after a configurable age window.
    /// </summary>
    public class CompanionActionEvent
    {
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Short action identifier, e.g. "flash", "spiral", "bubbles", "video", "lock_card".
        /// </summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Who triggered the action: "ai", "autonomy", or "user".
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// Whether the action was actually carried out (false if blocked, failed, or no-op).
        /// </summary>
        public bool Succeeded { get; set; }

        /// <summary>
        /// Compact human-readable detail, e.g. "intensity=25%" or "random pick".
        /// </summary>
        public string? Summary { get; set; }
    }
}
