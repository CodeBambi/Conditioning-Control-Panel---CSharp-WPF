using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Services.AIService;

namespace ConditioningControlPanel.Services.Companion.Brain
{
    /// <summary>
    /// A fully assembled request: the system prompt, and the message array to put on the wire
    /// (which already begins with that same system prompt as message 0).
    /// </summary>
    public sealed record PromptRequest(string SystemPrompt, IReadOnlyList<ChatMessage> Messages);

    /// <summary>
    /// Turns "what purpose, what history, what input" into the exact bytes sent to a provider.
    /// </summary>
    public interface IPromptAssembler
    {
        /// <summary>
        /// Builds the request for <paramref name="purpose"/> from the current
        /// <paramref name="session"/>.
        ///
        /// <para><b>Contract:</b> the caller has ALREADY appended the current input to
        /// <paramref name="session"/>, so it is the last turn of the window. <paramref name="input"/>
        /// is passed for tail decisions only (e.g. picking a purpose instruction) and implementations
        /// MUST NOT append it a second time. It may be null.</para>
        /// </summary>
        PromptRequest BuildRequest(AiPurpose purpose, ChatSession session, string? input);
    }

    /// <summary>
    /// Train 1 SHELL implementation. The system prompt still comes from
    /// <see cref="BambiSprite.GetSystemPrompt"/> verbatim — which is what keeps
    /// <c>SafetyComposer.Wrap</c> (preamble + floor around every user-authored section) intact, since
    /// that wrap happens at that method's single exit point.
    ///
    /// <para>What this shell adds is only the small DYNAMIC TAIL: memory block, recommendation
    /// exclusion line, and the per-purpose instruction. The two-zone restructure proper — hoisting a
    /// byte-stable prefix and killing <c>SampleVideoTitles()</c>'s per-call shuffle so provider prompt
    /// caching can finally hit — is the prompt agent's work on its own branch. The SIGNATURE and the
    /// tail composition order are final.</para>
    ///
    /// <para>The tail is appended AFTER the persona text and therefore inside the safety sandwich's
    /// footprint; it contains no user-authored text (memory facts are moderated before storage,
    /// recommendation titles come from our own media list), so it cannot be used to smuggle
    /// instructions past the guard.</para>
    /// </summary>
    public sealed class PromptAssembler : IPromptAssembler
    {
        /// <summary>Ceiling on the memory block, per doc 01 §2.5.</summary>
        public const int MemoryTokenBudget = 500;

        /// <summary>
        /// The anti-repeat rule ambient calls carry. Together with
        /// <see cref="RecentRecommendations"/> this is what lets ambient calls hold history at all —
        /// past "watch X~" lines used to act as few-shot bait and fixate the model on one title,
        /// which is why ambient reactions were made stateless in the first place.
        /// </summary>
        public const string AntiRepeatLine =
            "Do not repeat, rephrase or re-recommend anything from the recent lines above. Say something new.";

        /// <summary>
        /// Tells the model what the "said aloud" lines are, so it builds on her voice instead of
        /// parroting it (doc 01 §3.3).
        /// </summary>
        public const string SpokenAloudRule =
            "Lines marked \"said aloud\" are things you already said out loud a moment ago - never repeat them, never contradict them, build on them if natural.";

        private readonly IMemoryStore _memory;
        private readonly RecentRecommendations _recommendations;
        private readonly Func<string> _systemPromptProvider;

        /// <param name="systemPromptProvider">
        /// Injectable so tests (and, later, the two-zone assembler) can supply a prompt without
        /// standing up <see cref="BambiSprite"/> and the whole personality/mod stack.
        /// </param>
        public PromptAssembler(IMemoryStore memory, RecentRecommendations recommendations,
            Func<string>? systemPromptProvider = null)
        {
            _memory = memory ?? new MemoryStore();
            _recommendations = recommendations ?? new RecentRecommendations();
            _systemPromptProvider = systemPromptProvider ?? DefaultSystemPrompt;
        }

        private static string DefaultSystemPrompt()
        {
            try { return new BambiSprite().GetSystemPrompt(); }
            catch (Exception ex)
            {
                // A prompt build failure must not take chat down; the providers' own legacy paths
                // would have thrown here too, so degrade to an empty system prompt and let the
                // moderation spine and the provider fallbacks handle the rest.
                App.Logger?.Warning(ex, "PromptAssembler: system prompt build failed");
                return string.Empty;
            }
        }

        public PromptRequest BuildRequest(AiPurpose purpose, ChatSession session, string? input)
        {
            _ = input; // already the last turn of the window; see the interface contract.

            var spec = purpose == AiPurpose.Chat ? ChatWindowSpec.Chat : ChatWindowSpec.Ambient;
            var window = session?.BuildWindow(spec) ?? Array.Empty<CompanionTurn>();

            var sb = new StringBuilder(_systemPromptProvider());
            AppendTail(sb, purpose, window);
            var systemPrompt = sb.ToString();

            var messages = new List<ChatMessage>(window.Count + 1) { ChatMessage.System(systemPrompt) };
            messages.AddRange(ChatSession.ToMessages(window));

            return new PromptRequest(systemPrompt, messages);
        }

        /// <summary>
        /// The dynamic tail. Order is deliberate: durable facts, then the exclusion set, then the
        /// per-call instruction last so it is the most recent thing the model read.
        /// </summary>
        private void AppendTail(StringBuilder sb, AiPurpose purpose, IReadOnlyList<CompanionTurn> window)
        {
            var lines = new List<string>();

            var memory = _memory.GetInjectionBlock(MemoryTokenBudget);
            if (!string.IsNullOrWhiteSpace(memory)) lines.Add(memory!);

            var exclusion = _recommendations.BuildExclusionLine();
            if (exclusion != null) lines.Add(exclusion);

            if (window.Any(t => t.Kind == TurnKind.BarkEcho)) lines.Add(SpokenAloudRule);

            if (purpose != AiPurpose.Chat) lines.Add(AntiRepeatLine);

            if (lines.Count == 0) return;

            sb.AppendLine().AppendLine();
            foreach (var line in lines) sb.AppendLine(line);
        }
    }
}
