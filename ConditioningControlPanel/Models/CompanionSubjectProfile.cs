using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Persisted, high-level behavioral profile for the AI companion. Updated at the
    /// end of sessions and injected into the system prompt once when a new session
    /// starts. Stores only patterns and preferences — never raw chat transcripts or
    /// explicit user messages.
    /// </summary>
    public class CompanionSubjectProfile
    {
        public DateTime UpdatedAt { get; set; }

        /// <summary>Total number of conditioning sessions included in the profile.</summary>
        public int TotalSessions { get; set; }

        /// <summary>Total number of successful effect actions across all sessions.</summary>
        public int TotalActions { get; set; }

        /// <summary>Actions seen most often, e.g. ["flash", "bubbles", "spiral"].</summary>
        public List<string> FavoriteActions { get; set; } = new();

        /// <summary>Actions that were frequently declined/blocked, e.g. ["haptic", "video"].</summary>
        public List<string> DeclinedActions { get; set; } = new();

        /// <summary>Most common session start hour (0-23) if a pattern exists.</summary>
        public int? CommonSessionHour { get; set; }

        /// <summary>Average session length in minutes, if known.</summary>
        public double? AverageSessionMinutes { get; set; }

        /// <summary>Preferred intensity trend: "gentle", "moderate", "intense", or null.</summary>
        public string? IntensityPreference { get; set; }

        /// <summary>Free-form, very short notes for the AI (e.g. "responds well to denial").</summary>
        public string? Notes { get; set; }

        /// <summary>Compact narrative summary of the most recent session's actions, persisted from action history.</summary>
        public string? RecentSessionSummary { get; set; }
    }
}
