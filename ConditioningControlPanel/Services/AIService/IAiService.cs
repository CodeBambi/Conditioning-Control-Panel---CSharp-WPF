using System;
using System.Collections.Generic;
using System.Threading;
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

        /// <summary>
        /// Train 1 transport seam. Sends an already-assembled multi-turn conversation and returns
        /// the typed result. This is the method <see cref="ConditioningControlPanel.Services.Companion.Brain.CompanionBrain"/>
        /// uses; the six one-shot methods below are the legacy call sites and stay as-is until
        /// their adapters land.
        ///
        /// <para><b>Contract.</b> <paramref name="messages"/> is sent verbatim, in order, including
        /// its leading system message — implementations must not inject a system prompt of their
        /// own or re-order turns. Implementations DO run the Layer-1 moderation spine
        /// (<c>CheckInput</c> on the newest user-role message, <c>CheckOutput</c> on the reply,
        /// <c>ModerationLog</c>, and <c>ModerationCounter</c> only when
        /// <see cref="AiCallOptions.Interactive"/>), and DO emit one <c>[AI-METER]</c> line stamped
        /// with <see cref="AiCallOptions.MeterPurpose"/>.</para>
        ///
        /// <para><b>Result.</b> A genuine model reply returns <c>IsAiGenerated=true</c>. Any path that
        /// produced no usable model text (offline, no entitlement, transport failure, empty content,
        /// daily limit) returns <c>IsAiGenerated=false</c> with a canned or empty
        /// <c>Text</c> — never a badge-worthy reply. A moderation block returns a non-null
        /// <c>Refusal</c> and empty <c>Text</c>.</para>
        /// </summary>
        Task<AiReplyResult> SendAsync(IReadOnlyList<ChatMessage> messages, AiCallOptions options,
            CancellationToken cancellationToken = default);

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

        /// <summary>
        /// <paramref name="duration"/> is how long the user has been on this activity. It is
        /// formatted into the context tag with the same bucketing as
        /// <see cref="GetStillOnReactionAsync"/>; null means "not known" and reads as zero.
        /// The value used to be hardcoded to "0m" on all three providers, which lied to the
        /// model on the double-click path where the user may have dwelled for half an hour.
        /// </summary>
        Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "",
            string pageTitle = "", TimeSpan? duration = null);

        Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration);

        Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null);

        Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null);

        Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null);
    }
}
