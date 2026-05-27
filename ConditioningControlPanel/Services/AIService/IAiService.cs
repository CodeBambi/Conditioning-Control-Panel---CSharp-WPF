using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Moderation;

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

        /// <summary>
        /// P2/C4 typed variant. Returns an <see cref="AiReplyResult"/> so the chat UI
        /// can distinguish a real LLM reply (badge ON) from a canned fallback
        /// (badge OFF) and from a moderation refusal (POLICY bubble). Implementations
        /// MUST set <c>IsAiGenerated=false</c> for any fallback / login-required /
        /// circuit-broken path, and MUST populate <c>Refusal</c> with the
        /// input-or-output source when <see cref="App.ModerationGuard"/> blocks.
        /// Existing <see cref="GetBambiReplyAsync"/> continues to work and is a
        /// thin wrapper over this method for non-UI callers (autonomy / commands)
        /// that only need the text.
        /// </summary>
        Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false);

        Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "",
            string pageTitle = "");

        Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration);

        Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null);

        Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null);

        Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null);
    }
}
