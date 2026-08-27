using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
        /// Extra headroom the memory block gets ON TOP of <see cref="MemoryTokenBudget"/>, for
        /// boundary lines only.
        ///
        /// <para><see cref="MemoryStore.GetInjectionBlock"/> deliberately renders boundaries outside
        /// its own budget — a remembered "stop teasing me about X" is consent hygiene and must not
        /// lose a budget race to a joke about the user's cat — so the block it returns can legally
        /// exceed the budget it was handed. Clamping it flat at 500 here would quietly undo that.
        /// The exemption is bounded on both sides: the store caps boundaries at
        /// <see cref="MemoryStore.MaxBoundaryLines"/> (+1 overflow line), and this allowance caps
        /// what they may cost, so a pathological file still cannot inflate every request.</para>
        /// </summary>
        public const int BoundaryOvershootTokens = 300;

        /// <summary>
        /// Ceiling on the WHOLE dynamic tail. The tail is what the user pays full price for on every
        /// single call, so it is bounded here as well as inside the memory store: a third-party
        /// <see cref="IMemoryStore"/> that ignores its budget must not be able to inflate every
        /// request forever.
        ///
        /// <para>The one documented exception is <see cref="BoundaryOvershootTokens"/>, so the true
        /// worst case is <c>TailTokenBudget + BoundaryOvershootTokens</c> and only a user with 20
        /// recorded boundaries ever reaches it.</para>
        /// </summary>
        public const int TailTokenBudget = 700;

        /// <summary>Marks the boundary between the cached zone and the per-call zone.</summary>
        public const string TailHeader = "--- RIGHT NOW ---";

        /// <summary>
        /// Hard ceiling, in CHARACTERS, on the assembled system message.
        ///
        /// <para>The cloud proxy rejects any single message over 10,000 chars with
        /// <c>400 {error:"input_too_large"}</c>, and the client packs the whole stable prefix AND this
        /// tail into one system message. A user with a long knowledge base or a mod shipping ~100
        /// video titles already sits near that line; the tail adds up to
        /// <see cref="TailTokenBudget"/> + <see cref="BoundaryOvershootTokens"/> ≈ 4,000 chars more.
        /// Going over turns EVERY cloud call — chat and ambient — into an unbadged canned phrase with
        /// no user-visible reason.</para>
        ///
        /// <para>Only the TAIL is ever trimmed to fit. The prefix ends with
        /// <c>SafetyComposer.Floor</c>, so cutting it from the end would silently drop the safety
        /// floor: an oversized prefix is logged loudly and sent whole instead.</para>
        /// </summary>
        public const int SystemMessageCharCeiling = 9000;

        /// <summary>The proxy's actual reject threshold (input_too_large above this). The 9000
        /// ceiling is our soft budget; the gap between them is what lets a pinned anti-repeat
        /// line survive on prompts whose prefix alone busts the soft cap.</summary>
        public const int ProxyHardRejectCap = 9900;

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
            "Lines marked \"said aloud\" are things you already said out loud a moment ago - never repeat them, never contradict them, build on them if natural. " +
            "Your own replies are always plain speech: never wrap them in «» marks, never write \"said aloud\" yourself, and never call the user by your own name.";

        /// <summary>~40 tokens each, and always the LAST thing the model reads.</summary>
        public const string ChatInstruction =
            "The last line is them talking to you directly. Answer that line in one short bubble, in character.";

        public const string ReactionInstruction =
            "The last \"event\" line is something that just happened on their screen. React to it unprompted in one short beat - do not greet them, do not ask what they need.";

        public const string MemoryInstruction =
            "Return ONLY the JSON object that was asked for. No prose, no markdown fences, no commentary.";

        public const string SummaryInstruction =
            "Summarise what happened above in at most three sentences. Plain prose, no quotes, no lists.";

        /// <summary>
        /// Ceiling, in conservatively-estimated REAL tokens (<see cref="ConservativeRealTokens"/>),
        /// on the whole request this assembler will emit.
        ///
        /// <para>The cloud model behind the proxy is a 4,096-token-context model
        /// (gryphe/mythomax-l2-13b), and OpenRouter enables its "middle-out" context compression BY
        /// DEFAULT on every endpoint with ≤8k context. When a request lands near or over the
        /// window, that plugin silently guts the MIDDLE of the prompt — production receipts from
        /// 2026-08-07 show calls that shipped ~2,900-3,400 real tokens being billed for only
        /// ~1,545-1,690 prompt tokens, i.e. roughly half of every system prompt (persona rules,
        /// media rules, OUTPUT RULES, safety floor) never reached the model, while the old-voice
        /// chat history at the end survived — which is why switching personality presets changed
        /// nothing the user could hear. The client-side defense is to never build a request big
        /// enough for the compressor to fire: 4,096 context − 350 worst-case completion
        /// (ChatWithEffects) − ~350 margin for chat-template overhead and estimator error.</para>
        ///
        /// <para>Only HISTORY is shed to meet this (oldest first, newest turn always kept); the
        /// system prompt is never touched here, exactly like the char-cap path in
        /// <see cref="Compose"/>. Local models are covered too: Ollama's default context is the
        /// same 4k or smaller and it truncates overflow just as silently.</para>
        /// </summary>
        public const int ContextFitTokenBudget = 3400;

        /// <summary>
        /// Deliberately pessimistic real-token estimate (chars/3). The brain's budgeting estimator
        /// (<see cref="ChatSession.ApproxTokens"/>, chars/4) tracks provider billing, but Llama-2
        /// tokenizes this prompt's prose+URL mix at ~3.0-3.7 chars/token — measured 3.75 with
        /// cl100k on the exact wire payload, and Llama's 32k vocab is strictly less efficient.
        /// A fit check against a 4k window must overestimate, never underestimate.
        /// </summary>
        internal static int ConservativeRealTokens(string? text) =>
            string.IsNullOrEmpty(text) ? 0 : text!.Length / 3;

        private readonly IMemoryStore _memory;
        private readonly RecentRecommendations _recommendations;
        private readonly Func<string> _systemPromptProvider;
        private readonly Func<DateTime> _localClock;
        private readonly Func<IReadOnlyList<(string Title, string Url)>> _linkPool;
        private readonly Func<DateTime?> _personaFence;
        private readonly Func<string?> _lockdownContext;

        /// <param name="systemPromptProvider">
        /// Injectable so tests (and any future prefix source) can supply a prefix without standing up
        /// <see cref="BambiSprite"/> and the whole personality/mod stack. Defaults to the cached
        /// two-zone prefix.
        /// </param>
        /// <param name="localClock">Local wall clock, injectable for the time-of-day line's tests.</param>
        /// <param name="linkPool">The sanctioned link catalogue the wire-history hygiene pass
        /// rewrites against; injectable for tests, defaults to
        /// <see cref="Companion.CompanionLinkIndex.CurrentEntries"/>.</param>
        /// <param name="personaFence">The persona-switch fence moment
        /// (<see cref="Models.AppSettings.PersonaVoiceFenceUtc"/>); injectable for tests. Null
        /// (no value) means no fence.</param>
        public PromptAssembler(IMemoryStore memory, RecentRecommendations recommendations,
            Func<string>? systemPromptProvider = null, Func<DateTime>? localClock = null,
            Func<IReadOnlyList<(string Title, string Url)>>? linkPool = null,
            Func<DateTime?>? personaFence = null,
            Func<string?>? lockdownContext = null)
        {
            // Deliberately NOT `?? new MemoryStore()`. The production MemoryStore constructor is not
            // inert — it loads memory.json, starts a MemorySignalWriter and registers a shutdown
            // save — so a silent fallback here would give the process a SECOND store racing the
            // brain's on the same file. Every real caller passes CompanionBrain.Memory; null is a
            // wiring bug and should say so.
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
            _recommendations = recommendations ?? new RecentRecommendations();
            _systemPromptProvider = systemPromptProvider ?? DefaultSystemPrompt;
            _localClock = localClock ?? (() => DateTime.Now);
            _linkPool = linkPool ?? DefaultLinkPool;
            _personaFence = personaFence ?? DefaultPersonaFence;
            _lockdownContext = lockdownContext ?? DefaultLockdownContext;
        }

        private static DateTime? DefaultPersonaFence()
        {
            try { return App.Settings?.Current?.PersonaVoiceFenceUtc; }
            catch { return null; }
        }

        private static IReadOnlyList<(string Title, string Url)> DefaultLinkPool()
        {
            try { return Companion.CompanionLinkIndex.CurrentEntries(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("PromptAssembler: link pool unavailable: {Error}", ex.Message);
                return Array.Empty<(string, string)>();
            }
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
            window = FenceHistoryToPersona(window);

            var prefix = _systemPromptProvider() ?? string.Empty;
            var tail = BuildTail(purpose, window);
            // Pin BOTH anti-fixation lines through the hard-cap floor branch: with a fat preset
            // the prefix alone busts the soft cap on every call, and losing vary+exclusion is
            // how one invented title became the whole evening's suggestion (0807).
            var pinnedLines = _recommendations.BuildExclusionLine();
            if (purpose == AiPurpose.Chat || purpose == AiPurpose.Reaction)
                pinnedLines = pinnedLines == null ? VaryPicksRule : pinnedLines + "\n" + VaryPicksRule;
            var systemPrompt = Compose(prefix, tail, PurposeInstruction(purpose), pinned: pinnedLines);

            var messages = new List<ChatMessage>(window.Count + 1) { ChatMessage.System(systemPrompt) };
            messages.AddRange(ChatSession.ToMessages(SanitizeAssistantHistory(window)));
            ShedHistoryToContextFit(messages);

            App.Logger?.Debug(
                "[AI-PROMPT] purpose={Purpose} prefix_tok~{PrefixTokens} tail_tok~{TailTokens} window={Window}",
                purpose, ChatSession.ApproxTokens(prefix), ChatSession.ApproxTokens(tail), window.Count);

            return new PromptRequest(systemPrompt, messages);
        }

        /// <summary>
        /// Drops assistant-authored turns from BEFORE the most recent personality-preset selection
        /// (<see cref="Models.AppSettings.PersonaVoiceFenceUtc"/>) — wire-only, like
        /// <see cref="SanitizeAssistantHistory"/>; the stored session and the visible bubbles keep
        /// everything.
        ///
        /// <para>Root cause 2026-08-07 (part 2): a restored ~100-turn session in the previous
        /// voice out-few-shots any changed persona paragraph — the model imitates its own recent
        /// replies far harder than it obeys a ~135-token description, so a persona switch changed
        /// the prompt but not what she said. User turns are kept (they are the user's own words
        /// and the model needs the conversational thread), and <see cref="TurnKind.BarkEcho"/>
        /// lines are kept (they are her scripted recorded voice, true regardless of preset — and
        /// dropping them would break the "never contradict what you said aloud" rule).</para>
        /// </summary>
        internal IReadOnlyList<CompanionTurn> FenceHistoryToPersona(IReadOnlyList<CompanionTurn> window)
        {
            if (window == null || window.Count == 0) return window ?? Array.Empty<CompanionTurn>();

            DateTime? fence;
            try { fence = _personaFence(); }
            catch { return window; }
            if (fence == null) return window;

            var kept = new List<CompanionTurn>(window.Count);
            int dropped = 0;
            foreach (var turn in window)
            {
                if (turn.Kind is (TurnKind.AssistantChat or TurnKind.AmbientReply) && turn.Utc < fence.Value)
                {
                    dropped++;
                    continue;
                }
                kept.Add(turn);
            }

            if (dropped == 0) return window;
            App.Logger?.Information(
                "[AI-PROMPT] persona fence dropped {Dropped} pre-switch assistant turn(s) from the wire window",
                dropped);
            return kept;
        }

        /// <summary>
        /// Final belt: sheds the OLDEST history messages until the whole request fits
        /// <see cref="ContextFitTokenBudget"/> under the pessimistic
        /// <see cref="ConservativeRealTokens"/> estimate. The system message (index 0) and the
        /// newest message are never shed. See <see cref="ContextFitTokenBudget"/> for why this
        /// exists — a request that brushes the model's real 4k window gets HALF its prompt
        /// silently cut in transit, which is strictly worse than arriving with less history.
        /// </summary>
        internal static void ShedHistoryToContextFit(List<ChatMessage> messages)
        {
            if (messages == null || messages.Count == 0) return;

            int Estimate() => messages.Sum(m => ConservativeRealTokens(m.Content));

            int before = messages.Count;
            // Index 1 is the oldest history message; keep at least the system message + newest.
            while (messages.Count > 2 && Estimate() > ContextFitTokenBudget)
                messages.RemoveAt(1);

            if (messages.Count != before)
            {
                App.Logger?.Information(
                    "[AI-PROMPT] context-fit shed {Shed} oldest history message(s) ({Before}→{After}) to stay under ~{Budget} real tokens",
                    before - messages.Count, before, messages.Count, ContextFitTokenBudget);
            }

            if (Estimate() > ContextFitTokenBudget)
            {
                // Nothing left to shed: the system prompt alone brushes the model's window. The
                // provider-side compressor may fire and gut it from the middle — this line is the
                // only client-side witness. Shrink the preset / knowledge base / media list.
                App.Logger?.Warning(
                    "[AI-PROMPT] request still ~{Tokens} est. real tokens after shedding all history " +
                    "(budget {Budget}) — the system prompt alone risks provider-side middle-out truncation",
                    Estimate(), ContextFitTokenBudget);
            }
        }

        /// <summary>
        /// Few-shot hygiene for the WIRE copy of the history window: every assistant-authored
        /// dialogue line has off-pool video titles rewritten to real pool entries
        /// (<see cref="Services.Companion.CompanionTitleMatcher.RewriteOffPoolTitlesForPrompt"/>)
        /// before it is put on the request.
        ///
        /// <para>Root cause 0807: her own past lines are the strongest signal a small model has —
        /// a session.json carrying ~97 turns of invented titles out-competed the verbatim pool
        /// list in the prefix for a whole day, and the store-level rewrite cannot touch inventions
        /// below the fuzzy floor, so the poison was self-renewing. This pass is wire-only: the
        /// stored history and the bubbles the user actually saw are untouched, it simply stops the
        /// model from ever SEEING itself get away with an off-pool title.</para>
        ///
        /// <para>Bark echoes are scripted (on-pool by construction) and user turns are the user's
        /// own words; neither is touched. Any failure degrades to the unsanitized window — a
        /// hygiene pass must never take chat down.</para>
        /// </summary>
        internal IReadOnlyList<CompanionTurn> SanitizeAssistantHistory(IReadOnlyList<CompanionTurn> window)
        {
            if (window == null || window.Count == 0) return window ?? Array.Empty<CompanionTurn>();

            try
            {
                IReadOnlyList<(string Title, string Url)>? pool = null;
                List<CompanionTurn>? sanitized = null;
                int changedTitles = 0, changedTurns = 0;

                for (int i = 0; i < window.Count; i++)
                {
                    var turn = window[i];
                    if (turn.Kind is not (TurnKind.AssistantChat or TurnKind.AmbientReply)) continue;

                    pool ??= _linkPool() ?? Array.Empty<(string, string)>();
                    if (pool.Count == 0) return window;

                    var text = Services.Companion.CompanionTitleMatcher
                        .RewriteOffPoolTitlesForPrompt(turn.Text, pool, out var changed);
                    if (changed == 0) continue;

                    sanitized ??= new List<CompanionTurn>(window);
                    sanitized[i] = turn with { Text = text };
                    changedTitles += changed;
                    changedTurns++;
                }

                if (sanitized == null) return window;
                App.Logger?.Information(
                    "[AI-LINK] wire history hygiene rewrote {Count} off-pool title(s) across {Turns} turn(s)",
                    changedTitles, changedTurns);
                return sanitized;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("PromptAssembler: history hygiene failed: {Error}", ex.Message);
                return window;
            }
        }

        /// <summary>
        /// Joins the cached prefix and the per-call tail into one system message, keeping the result
        /// under <see cref="SystemMessageCharCeiling"/>.
        ///
        /// <para>Over the ceiling, the tail is shed from its LOW-priority end (the last lines
        /// <see cref="BuildTail"/> added) and <paramref name="instruction"/> is always kept — a call
        /// with no purpose instruction is worse than a call with no time-of-day line. If the prefix
        /// alone is already over, it is sent untouched and logged: the prefix ends with the safety
        /// floor, and trimming it to satisfy a length cap would be trading a compliance control for a
        /// cost control.</para>
        /// </summary>
        internal static string Compose(string prefix, string tail, string instruction, string? pinned = null)
        {
            prefix ??= string.Empty;
            tail ??= string.Empty;
            if (tail.Length == 0) return WarnIfOversize(prefix);

            var joined = prefix + "\n\n" + tail;
            if (joined.Length <= SystemMessageCharCeiling) return joined;

            // Room the tail may occupy, after the prefix and the two joining newlines.
            int room = SystemMessageCharCeiling - prefix.Length - 2;
            var floor = TailHeader + "\n" + instruction;
            if (room < floor.Length)
            {
                // Even the minimum tail doesn't fit. Send the prefix plus just the instruction —
                // dropping the instruction entirely would leave the call with no purpose at all.
                // The pinned line (the anti-repeat exclusion set) rides along when the hard proxy
                // reject cap still has room for it: a fat preset used to strip the exclusion on
                // EVERY call, which turned the anti-fixation system off exactly for the users
                // with the biggest prompts.
                if (!string.IsNullOrEmpty(pinned) &&
                    prefix.Length + floor.Length + pinned!.Length + 3 <= ProxyHardRejectCap)
                {
                    floor = TailHeader + "\n" + pinned + "\n" + instruction;
                }
                App.Logger?.Warning(
                    "[AI-PROMPT] system prompt over the {Ceiling}-char proxy cap (prefix={Prefix} chars) — tail reduced to the purpose instruction",
                    SystemMessageCharCeiling, prefix.Length);
                return WarnIfOversize(prefix + "\n\n" + floor);
            }

            var lines = tail.Replace("\r\n", "\n").Split('\n').ToList();
            // Drop from the end, but never the instruction (the last line) or the header (the first).
            while (lines.Count > 2 && string.Join("\n", lines).Length > room)
                lines.RemoveAt(lines.Count - 2);

            var trimmed = string.Join("\n", lines);
            App.Logger?.Warning(
                "[AI-PROMPT] system prompt trimmed to fit the {Ceiling}-char proxy cap (tail {Before}→{After} chars)",
                SystemMessageCharCeiling, tail.Length, trimmed.Length);
            return WarnIfOversize(prefix + "\n\n" + trimmed);
        }

        /// <summary>
        /// Loc key for the user-visible "your prompt is too long" notice. Kept next to the code that
        /// raises it so the two cannot drift.
        /// </summary>
        internal const string OversizeNoticeLocKey = "companion_prompt_too_long_notice";

        /// <summary>0 until the oversize notice has been raised; one per app session, via Interlocked
        /// because prompts are composed from whichever thread the call came in on.</summary>
        private static int _oversizeNoticeRaised;

        /// <summary>Resets the once-per-session latch. Tests only.</summary>
        internal static void ResetOversizeNoticeForTests() => Interlocked.Exchange(ref _oversizeNoticeRaised, 0);

        /// <summary>True once the notice has been raised this session. Tests + diagnostics.</summary>
        internal static bool OversizeNoticeRaised => Volatile.Read(ref _oversizeNoticeRaised) != 0;

        private static string WarnIfOversize(string systemPrompt)
        {
            if (systemPrompt.Length > SystemMessageCharCeiling)
            {
                App.Logger?.Warning(
                    "[AI-PROMPT] system prompt is {Chars} chars — the cloud proxy rejects anything over 10000 with input_too_large; " +
                    "trim the knowledge base or the mod's video list",
                    systemPrompt.Length);
            }

            // Past the HARD cap the request will actually be rejected, and the companion falls back
            // to a middle-cut prompt (AiService.SalvageOversizeSystemMessage) or, failing that, to
            // canned phrases. That is a visible change in her behaviour with no visible cause, so it
            // gets a visible reason: once per session, on the surface the app already uses for
            // this kind of thing, never a dialog and never per call.
            if (systemPrompt.Length > ProxyHardRejectCap) RaiseOversizeNoticeOnce();

            return systemPrompt;
        }

        /// <summary>
        /// Raises the user-visible notice exactly once per app session, on the UI thread, through
        /// the existing toast surface. Never throws: a diagnostic must not be able to take chat
        /// down, and prompts are composed from background threads where a WPF failure would
        /// otherwise surface as an unobserved task exception.
        /// </summary>
        private static void RaiseOversizeNoticeOnce()
        {
            if (Interlocked.Exchange(ref _oversizeNoticeRaised, 1) != 0) return;

            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        App.Notifications?.Show(
                            Localization.Loc.Get(OversizeNoticeLocKey),
                            NotificationType.Warning,
                            TimeSpan.FromSeconds(12));
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("PromptAssembler: oversize notice failed to show: {Error}", ex.Message);
                    }
                }));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("PromptAssembler: oversize notice dispatch failed: {Error}", ex.Message);
            }
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

            // Anti-fixation lines FIRST: Compose sheds tail lines from the end, and the budget
            // loop below drops whatever no longer fits — with these after the time-of-day line
            // they were the first casualties of a fat prompt, which is exactly when the model
            // needs them most (observed 2026-08-07: four chat calls ran with no exclusion set
            // and no vary rule, and the same invented titles came back all day).
            var exclusion = _recommendations.BuildExclusionLine();
            if (exclusion != null) lines.Add(exclusion);

            // The warden briefing, when a lockdown is running. Chat and Reaction only: those are the
            // two purposes where SHE speaks. Memory extracts JSON facts and Summary writes prose about
            // the conversation, and a "you are the warden" instruction in either would end up recorded
            // as something the user said or believed, which would outlive the lockdown by design.
            // Placed right after the exclusion set - ahead of everything else, but never ahead of the
            // 0807 anti-fixation lines, which earned their spot at the front of the budget.
            if (purpose == AiPurpose.Chat || purpose == AiPurpose.Reaction)
            {
                string? lockdown = null;
                try { lockdown = _lockdownContext(); }
                catch (Exception ex) { App.Logger?.Debug("PromptAssembler: lockdown context threw: {Error}", ex.Message); }
                if (!string.IsNullOrWhiteSpace(lockdown)) lines.Add(lockdown!);
            }

            if (purpose == AiPurpose.Chat || purpose == AiPurpose.Reaction) lines.Add(VaryPicksRule);

            lines.Add(TimeOfDayLine(_localClock()));

            if (window != null && window.Any(t => t.Kind == TurnKind.BarkEcho)) lines.Add(SpokenAloudRule);

            if (purpose == AiPurpose.Reaction) lines.Add(AntiRepeatLine);

            // Budget: the instruction is never the thing we drop — a call with no instruction is a
            // call with no purpose. Everything else yields to it, lowest-priority (last) first.
            int budget = TailTokenBudget
                         - ChatSession.ApproxTokens(TailHeader) - 1
                         - ChatSession.ApproxTokens(instruction) - 1;
            int spent = 0;
            var kept = new List<string>(lines.Count + 1);

            // The memory block is charged against the budget but is never itself dropped: it is
            // already independently bounded by the clamp above, and it is the one part of the tail
            // that can carry the user's boundaries. Letting a fat-but-legal memory block fall off
            // the end of a generic budget loop would delete boundaries to make room for a
            // "vary your picks" reminder.
            var memory = ClampToTokens(
                _memory.GetInjectionBlock(MemoryTokenBudget), MemoryTokenBudget, BoundaryOvershootTokens);
            if (!string.IsNullOrWhiteSpace(memory))
            {
                kept.Add(memory!);
                spent += ChatSession.ApproxTokens(memory) + 1;
            }

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

        /// <summary>
        /// The warden briefing (Possession wave 2, item B12). While a lockdown is running the companion
        /// is not a companion, she is the thing holding the door, and until this existed she had no idea:
        /// the user would type "let me out" and get a cheerful video recommendation, which is the single
        /// fastest way to break a mode built entirely out of atmosphere.
        ///
        /// <para>It lives in the DYNAMIC TAIL, never in the stable prefix, for two reasons. The obvious
        /// one is caching: the prefix is byte-identical across calls by design and a countdown in it
        /// would invalidate it every single turn. The load-bearing one is that it must not be able to
        /// persist - a "you are the warden" paragraph cached into a prefix that only rebuilds on a mod
        /// switch would still be there an hour after the lockdown ended.</para>
        ///
        /// <para>The three prohibitions are the whole safety surface. The secret exit phrase is the
        /// user's own key and the model has no business hinting at it (it does not know it either, and a
        /// GUESS presented confidently is worse than silence); promising to end the lockdown is a promise
        /// nothing in this process can keep, because only the timer and the phrase can; and "short" keeps
        /// her from monologuing at someone who is, at that moment, locked in with her.</para>
        /// </summary>
        internal static string BuildLockdownContext(int minutesLeft, string? rung, string? intensity, int escapes)
        {
            var sb = new StringBuilder();
            sb.Append("A lockdown is running (").Append(Math.Max(0, minutesLeft)).Append(" minutes left");
            if (!string.IsNullOrWhiteSpace(rung)) sb.Append(", Possession rung ").Append(rung);
            if (!string.IsNullOrWhiteSpace(intensity)) sb.Append(", intensity ").Append(intensity);
            sb.Append(", the user tried to escape ").Append(Math.Max(0, escapes)).Append(" times). ");
            sb.Append("You are the warden: tease them about wanting out, stay in your mod voice, ");
            sb.Append("never reveal or hint at the secret exit phrase, never promise to end it, ");
            sb.Append("keep it playful and short.");
            return sb.ToString();
        }

        /// <summary>
        /// Reads the live lockdown state, or null when none is running. Every read is guarded: this runs
        /// on the chat path, and a prompt that throws is a chat that does not answer.
        /// </summary>
        private static string? DefaultLockdownContext()
        {
            try
            {
                var lockdown = App.Lockdown;
                if (lockdown == null || !lockdown.IsActive) return null;

                int minutes = (int)Math.Round(lockdown.Remaining.TotalMinutes, MidpointRounding.AwayFromZero);

                // Rung and intensity are only true while the haunt is actually running. With
                // LockdownPossessionEnabled off there is a timer and no ladder, and telling the model
                // about a rung nothing is climbing invites her to narrate effects that never happen.
                string? rung = null, intensity = null;
                var director = App.Possession;
                if (director != null && director.IsHaunting)
                {
                    rung = RungName(director.CurrentRung);
                    intensity = IntensityName(App.Settings?.Current?.LockdownPossessionIntensity ?? 1);
                }

                return BuildLockdownContext(minutes, rung, intensity,
                    Services.Possession.PossessionRemember.EscapeAttempts);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("PromptAssembler: lockdown context unavailable: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>The ladder rung, in the words POSSESSION.md uses for it.</summary>
        internal static string RungName(Services.Possession.PossessionRung rung) => rung switch
        {
            Services.Possession.PossessionRung.Settle => "Settle",
            Services.Possession.PossessionRung.Drift => "Drift",
            Services.Possession.PossessionRung.Melt => "Melt",
            Services.Possession.PossessionRung.Collapse => "Collapse",
            Services.Possession.PossessionRung.ItKnows => "It knows",
            _ => "Settle"
        };

        /// <summary>The intensity preset, in the words the Lockdown card uses for it.</summary>
        internal static string IntensityName(int intensity) => intensity switch
        {
            0 => "Gentle",
            2 => "Full Doki",
            _ => "Eerie"
        };

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
        /// <param name="local">Local wall clock.</param>
        /// <remarks>
        /// The clock is rendered to the HOUR, not the minute. Providers discount the longest common
        /// prefix of a request, and this line sits inside message 0 ahead of the whole history window:
        /// at <c>HH:mm</c> resolution two chat turns a minute apart share no cacheable prefix at all,
        /// so the ~1,600 tokens of history Train 1 added get re-billed at full price on every single
        /// turn. The hour band plus the hour is all the flavour she has ever used.
        /// </remarks>
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
                "Right now it is {0}, around {1:00}:00 their time ({2}).",
                band, local.Hour, local.DayOfWeek);
        }

        /// <summary>
        /// Trims a block to a token ceiling at line boundaries, so a memory store that ignores its
        /// budget (or a future one that grows a new section) can never inflate every request.
        /// </summary>
        internal static string? ClampToTokens(string? block, int tokenBudget)
            => ClampToTokens(block, tokenBudget, exemptTokenBudget: 0);

        /// <summary>
        /// As above, but lines the memory store rendered outside its own budget
        /// (<see cref="MemoryStore.IsUnbudgetedInjectionLine"/> — boundaries) are charged to a
        /// SEPARATE <paramref name="exemptTokenBudget"/> instead of the main one, so an ordinary
        /// fact can never crowd out a boundary and vice versa. Both buckets are hard ceilings, so
        /// the result is still bounded at <c>tokenBudget + exemptTokenBudget</c>.
        ///
        /// <para>An over-budget line is skipped rather than ending the scan: a later, shorter
        /// boundary must still get its chance even if an earlier long fact did not fit.</para>
        /// </summary>
        internal static string? ClampToTokens(string? block, int tokenBudget, int exemptTokenBudget)
        {
            if (string.IsNullOrWhiteSpace(block)) return null;
            if (ChatSession.ApproxTokens(block) <= tokenBudget) return block;

            var sb = new StringBuilder();
            int spent = 0;
            int exemptSpent = 0;
            foreach (var line in block!.Replace("\r\n", "\n").Split('\n'))
            {
                int cost = ChatSession.ApproxTokens(line) + 1;
                bool exempt = exemptTokenBudget > 0 && MemoryStore.IsUnbudgetedInjectionLine(line);

                if (exempt)
                {
                    if (exemptSpent + cost > exemptTokenBudget) continue;
                    exemptSpent += cost;
                }
                else
                {
                    if (spent + cost > tokenBudget) continue;
                    spent += cost;
                }

                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
            }

            var clamped = sb.ToString().TrimEnd();
            return clamped.Length == 0 ? null : clamped;
        }
    }
}
