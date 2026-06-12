using System;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Persisted per-provider AI request counters. Stored inside
    /// <see cref="CompanionPromptSettings"/> so usage survives app restarts.
    /// </summary>
    public class AiUsageState
    {
        /// <summary>
        /// UTC date when the counters were last reset (used to detect midnight rollover).
        /// </summary>
        public DateTime LastResetDate { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Requests counted today against the cloud proxy limit.
        /// </summary>
        public int CloudRequestCount { get; set; }

        /// <summary>
        /// Requests counted today against the OpenAI-compatible user-defined limit.
        /// </summary>
        public int OpenAiCompatibleRequestCount { get; set; }

        /// <summary>
        /// Creates a deep copy of this usage state.
        /// </summary>
        public AiUsageState Clone()
        {
            return new AiUsageState
            {
                LastResetDate = LastResetDate,
                CloudRequestCount = CloudRequestCount,
                OpenAiCompatibleRequestCount = OpenAiCompatibleRequestCount
            };
        }
    }
}
