using System;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// Abstraction over a Bambi Companion AI provider. Implemented by both the
    /// hosted-proxy <see cref="ConditioningControlPanel.Services.AiService"/> and
    /// the local Ollama provider.
    /// </summary>
    public interface IAiService : IDisposable
    {
        bool IsAvailable { get; }

        int DailyRequestsRemaining { get; }

        Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false);

        Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "",
            string pageTitle = "");

        Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration);

        Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null);

        Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null);

        Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null);
    }
}
