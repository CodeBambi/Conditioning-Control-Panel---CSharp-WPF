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
    /// The two-zone prompt layout (doc 01 §5.2) — the single biggest cost lever in the rework.
    ///
    /// <para><b>Zone 1, the STABLE PREFIX</b> (<see cref="BambiSprite.GetStablePrompt"/>): safety
    /// preamble, persona, knowledge base, the FULL media list in fixed alphabetical order, awareness
    /// protocols whose examples are resolved once per app-session, output rules, safety floor. It is
    /// byte-identical across calls AND across purposes, and is rebuilt only when one of its inputs
    /// changes (mod switch, personality edit, slut mode, links, quiz, video pool). Providers discount
    /// the longest common prefix of a prompt; until this existed, <c>SampleVideoTitles()</c> shuffled
    /// titles into the middle of every build, so that common prefix ended after a few hundred tokens
    /// and the cache hit rate was structurally zero.</para>
    ///
    /// <para><b>Zone 2, the DYNAMIC TAIL</b> (this class): memory block (≤500 tok), time-of-day line,
    /// the recommendation exclusion set, and the ~40-token purpose instruction. Small, per-call, and
    /// appended AFTER the prefix so it never invalidates it. Anti-fixation now comes from the
    /// exclusion line plus an explicit "vary your picks" rule rather than from reshuffling the prompt
    /// — which is both cheaper and a stronger fix, because the model is told what NOT to pick instead
    /// of being shown a different subset and hoped at.</para>
    ///
    /// <para>The tail sits after <c>SafetyComposer.Floor</c> (that is the layout doc 01 §5.2
    /// specifies). It carries no user-authored text — memory facts are moderated before storage and
    /// recommendation titles come from our own media list — so it cannot be used to smuggle
    /// instructions past the guard, and the guard itself is unchanged either way.</para>
    /// </summary>
    public sealed class PromptAssembler : IPromptAssembler
    {
        /// <summary>Ceiling on the memory block, per doc 01 §2.5.</summary>
        public const int MemoryTokenBudget = 500;

        /// <summary>
        /// Ceiling on the WHOLE dynamic tail. The tail is what the user pays full price for on every
        /// single call, so it is bounded here as well as inside the memory store: a third-party
        /// <see cref="IMemoryStore"/> that ignores its budget must not be able to inflate every
        /// request forever.
        /// </summary>
        public const int TailTokenBudget = 700;

        /// <summary>Marks the boundary between the cached zone and the per-call zone.</summary>
        public const string TailHeader = "--- RIGHT NOW ---";

        /// <summary>
        /// The anti-repeat rule ambient calls carry. Together with
        /// <see cref="RecentRecommendations"/> this is what lets ambient calls hold history at all —
        /// past "watch X~" lines used to act as few-shot bait and fixate the model on one title,
        /// which is why ambient reactions were made stateless in the first place.
        /// </summary>
        public const string AntiRepeatLine =
            "Do not repeat, rephrase or re-recommend anything from the recent lines above. Say something new.";

        /// <summary>
        /// The replacement for shuffling the media list. Stated once, in the tail, so it sits close to
        /// the exclusion set it works with.
        /// </summary>
        public const string VaryPicksRule =
            "Vary your picks: when you name a video or playlist, choose a different one than last time.";

        /// <summary>
        /// Tells the model what the "said aloud" lines are, so it builds on her voice instead of
        /// parroting it (doc 01 §3.3).
        /// </summary>
        public const string SpokenAloudRule =
            "Lines marked \"said aloud\" are things you already said out loud a moment ago - never repeat them, never contradict them, build on them if natural.";

        /// <summary>~40 tokens each, and always the LAST thing the model reads.</summary>
        public const string ChatInstruction =
            "The last line is them talking to you directly. Answer that line in one short bubble, in character.";

        public const string ReactionInstruction =
            "The last \"event\" line is something that just happened on their screen. React to it unprompted in one short beat - do not greet them, do not ask what they need.";

        public const string MemoryInstruction =
            "Return ONLY the JSON object that was asked for. No prose, no markdown fences, no commentary.";

        public const string SummaryInstruction =
            "Summarise what happened above in at most three sentences. Plain prose, no quotes, no lists.";

        private readonly IMemoryStore _memory;
        private readonly RecentRecommendations _recommendations;
        private readonly Func<string> _systemPromptProvider;
        private readonly Func<DateTime> _localClock;

        /// <param name="systemPromptProvider">
        /// Injectable so tests (and any future prefix source) can supply a prefix without standing up
        /// <see cref="BambiSprite"/> and the whole personality/mod stack. Defaults to the cached
        /// two-zone prefix.
        /// </param>
        /// <param name="localClock">Local wall clock, injectable for the time-of-day line's tests.</param>
        public PromptAssembler(IMemoryStore memory, RecentRecommendations recommendations,
            Func<string>? systemPromptProvider = null, Func<DateTime>? localClock = null)
        {
            _memory = memory ?? new MemoryStore();
            _recommendations = recommendations ?? new RecentRecommendations();
            _systemPromptProvider = systemPromptProvider ?? DefaultSystemPrompt;
            _localClock = localClock ?? (() => DateTime.Now);
        }

        private static string DefaultSystemPrompt()
        {
            try { return BambiSprite.GetStablePrompt(); }
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

            var prefix = _systemPromptProvider() ?? string.Empty;
            var tail = BuildTail(purpose, window);
            var systemPrompt = tail.Length == 0 ? prefix : prefix + "\n\n" + tail;

            var messages = new List<ChatMessage>(window.Count + 1) { ChatMessage.System(systemPrompt) };
            messages.AddRange(ChatSession.ToMessages(window));

            App.Logger?.Debug(
                "[AI-PROMPT] purpose={Purpose} prefix_tok~{PrefixTokens} tail_tok~{TailTokens} window={Window}",
                purpose, ChatSession.ApproxTokens(prefix), ChatSession.ApproxTokens(tail), window.Count);

            return new PromptRequest(systemPrompt, messages);
        }

        /// <summary>
        /// The dynamic tail, in a deliberate order: durable facts, then where we are in the day, then
        /// the exclusion set and its rule, then the per-call instruction LAST so it is the most recent
        /// thing the model read. Returns "" when there is nothing to say, so a stock build's prompt is
        /// exactly the cached prefix and nothing else.
        /// </summary>
        internal string BuildTail(AiPurpose purpose, IReadOnlyList<CompanionTurn> window)
        {
            var instruction = PurposeInstruction(purpose);
            var lines = new List<string>();

            var memory = ClampToTokens(_memory.GetInjectionBlock(MemoryTokenBudget), MemoryTokenBudget);
            if (!string.IsNullOrWhiteSpace(memory)) lines.Add(memory!);

            lines.Add(TimeOfDayLine(_localClock()));

            var exclusion = _recommendations.BuildExclusionLine();
            if (exclusion != null) lines.Add(exclusion);

            if (purpose == AiPurpose.Chat || purpose == AiPurpose.Reaction) lines.Add(VaryPicksRule);

            if (window != null && window.Any(t => t.Kind == TurnKind.BarkEcho)) lines.Add(SpokenAloudRule);

            if (purpose == AiPurpose.Reaction) lines.Add(AntiRepeatLine);

            // Budget: the instruction is never the thing we drop — a call with no instruction is a
            // call with no purpose. Everything else yields to it, lowest-priority (last) first.
            int budget = TailTokenBudget
                         - ChatSession.ApproxTokens(TailHeader) - 1
                         - ChatSession.ApproxTokens(instruction) - 1;
            int spent = 0;
            var kept = new List<string>(lines.Count);
            foreach (var line in lines)
            {
                int cost = ChatSession.ApproxTokens(line) + 1;
                if (spent + cost > budget) continue;
                kept.Add(line);
                spent += cost;
            }

            var sb = new StringBuilder();
            sb.AppendLine(TailHeader);
            foreach (var line in kept) sb.AppendLine(line);
            sb.Append(instruction);
            return sb.ToString();
        }

        private static string PurposeInstruction(AiPurpose purpose) => purpose switch
        {
            AiPurpose.Chat => ChatInstruction,
            AiPurpose.Reaction => ReactionInstruction,
            AiPurpose.Memory => MemoryInstruction,
            AiPurpose.Summary => SummaryInstruction,
            _ => ChatInstruction
        };

        /// <summary>
        /// One line, ~15 tokens: the day, the clock, and the band of the day. She has always had
        /// time-of-day flavour in the bark system (<c>local_hour</c> conditions); this gives the LLM
        /// the same footing without pushing a clock into the cached prefix, where it would invalidate
        /// the cache once a minute.
        /// </summary>
        internal static string TimeOfDayLine(DateTime local)
        {
            var band = local.Hour switch
            {
                < 5 => "the middle of the night",
                < 9 => "early morning",
                < 12 => "morning",
                < 14 => "midday",
                < 18 => "afternoon",
                < 22 => "evening",
                _ => "late night"
            };
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Right now it is {0}, {1:HH:mm} their time ({2}).",
                band, local, local.DayOfWeek);
        }

        /// <summary>
        /// Trims a block to a token ceiling at line boundaries, so a memory store that ignores its
        /// budget (or a future one that grows a new section) can never inflate every request.
        /// </summary>
        internal static string? ClampToTokens(string? block, int tokenBudget)
        {
            if (string.IsNullOrWhiteSpace(block)) return null;
            if (ChatSession.ApproxTokens(block) <= tokenBudget) return block;

            var sb = new StringBuilder();
            int spent = 0;
            foreach (var line in block!.Replace("\r\n", "\n").Split('\n'))
            {
                int cost = ChatSession.ApproxTokens(line) + 1;
                if (spent + cost > tokenBudget) break;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
                spent += cost;
            }

            var clamped = sb.ToString().TrimEnd();
            return clamped.Length == 0 ? null : clamped;
        }
    }
}
