namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// Handles AI-powered chat responses for the Bambi Companion widget.
    /// Uses hosted proxy that forwards to OpenRouter for roleplay.
    /// Free for all users with a cloud identity; falls back to Patreon auth.
    /// </summary>
    public interface IAiService : IDisposable
    {
        /// <summary>
        /// Whether AI is available (cloud identity or Patreon access)
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Daily requests remaining (client-side tracking)
        /// </summary>
        int DailyRequestsRemaining { get; }

        /// <summary>
        /// Gets an AI-generated reply in the Bambi personality.
        /// Returns fallback response if API unavailable or daily limit reached.
        /// </summary>
        Task<string> GetBambiReplyAsync(string userInput);

        /// <summary>
        /// Gets an AI-generated reaction to the user's current activity.
        /// Used by Awareness Mode. Passes raw website and tab name for AI to interpret.
        /// Returns null if AI unavailable (caller should use preset phrase).
        /// </summary>
        Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "",
            string pageTitle = "");

        /// <summary>
        /// Gets an AI-generated "still on" reaction when user has been on the same activity for a while.
        /// Includes time context for the AI to reference.
        /// </summary>
        Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration);
        
    }
}
