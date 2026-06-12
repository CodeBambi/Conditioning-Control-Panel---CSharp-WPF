using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.AiEnrichment;
using ConditioningControlPanel.Models.CommandData;
using ConditioningControlPanel.Services.AIService.Enrichment;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// Shared foundation for all <see cref="IAiService"/> implementations.
    /// Providers only need to implement transport (<see cref="GetRawCompletionAsync"/>);
    /// this base handles prompt formatting, moderation, response parsing, command
    /// execution, result typing, chat memory, and command-outcome feedback.
    /// </summary>
    public abstract class AiProviderBase : IAiService
    {
        protected readonly BambiSprite BambiSprite;
        protected readonly IAiResponseParser Parser;
        protected readonly KnowledgeService KnowledgeService;
        protected readonly IPromptService PromptService;
        protected readonly AiChatMemory ChatMemory;

        private readonly List<CommandOutcome> _commandOutcomes = new();
        private bool _memoryRecallSignaled;
        private bool _subjectProfileInjected;

        protected AiProviderBase()
        {
            BambiSprite = new BambiSprite();
            Parser = new AiResponseParser(GetFallbackResponse);
            KnowledgeService = new KnowledgeService();
            PromptService = new PromptService();
            ChatMemory = new AiChatMemory(GetMemoryStorageKey(), () => IsChatMemoryEnabled, MaxPersistedPairs);
        }

        public abstract bool IsAvailable { get; }
        public abstract int DailyRequestsRemaining { get; }

        /// <summary>
        /// Sends a chat completion request built by the base class (system + enrichment +
        /// sliding-window memory + current user turn) and returns the raw assistant
        /// content (including any JSON effects wrapper). Transport only — moderation,
        /// parsing, command execution, and memory updates happen in the base class.
        /// </summary>
        protected abstract Task<string?> GetRawCompletionAsync(
            List<AiMessage> messages,
            string userInput,
            bool isUserMessage);

        /// <summary>
        /// Returns a short provider/model identifier used in moderation logs.
        /// </summary>
        protected abstract string GetModelHint();

        /// <summary>
        /// Returns the fallback phrase used when the provider is unavailable or
        /// returns empty content.
        /// </summary>
        protected abstract string GetFallbackResponse();

        /// <summary>
        /// When false, parsed effect commands are not executed locally. Cloud sets this
        /// to false because the proxy handles effects server-side.
        /// </summary>
        protected virtual bool SupportsEffectCommands => true;

        /// <summary>
        /// Unique key used for the provider's persistent chat-history file.
        /// </summary>
        protected virtual string GetMemoryStorageKey() => GetType().Name;

        /// <summary>
        /// Whether this provider should load/persist chat memory. Cloud is stateless.
        /// </summary>
        protected virtual bool IsChatMemoryEnabled => App.Settings?.Current?.CompanionPrompt?.ChatMemoryEnabled == true;

        /// <summary>
        /// Maximum user+assistant pairs persisted to disk.
        /// </summary>
        protected virtual int MaxPersistedPairs => 50;

        /// <summary>
        /// Maximum user+assistant pairs sent to the model per request.
        /// </summary>
        protected virtual int MaxSendPairs => App.Settings?.Current?.CompanionPrompt?.ChatMemorySendPairs ?? 10;

        /// <summary>
        /// Hook called once per session when the first accepted response is produced
        /// while restored memory turns are in context. Local overrides this to signal
        /// the GamificationBridge achievement.
        /// </summary>
        protected virtual void OnMemoryRecalled() { }

        /// <summary>
        /// Builds the optional effects-enrichment message. Cloud returns null because
        /// the proxy constructs the prompt server-side.
        /// </summary>
        protected virtual AiMessage? BuildEnrichmentMessage(string userInput)
        {
            var effectsEnabled = App.Settings?.Current?.CompanionPrompt?.AllowAiToControlEffects == true;
            if (!effectsEnabled)
                return null;

            var currentTime = DateTime.Now.ToString("yyyy-M-dd dddd h:mm:ss tt");
            var facts = KnowledgeService.GetKnowledge(userInput, maxResults: 20);
            var factsJson = JsonSerializer.Serialize(facts);
            var snapshot = AiContextSnapshot.Build();
            return PromptService.BuildEnrichmentMessage(factsJson, currentTime, snapshot, _commandOutcomes);
        }

        public virtual async Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
        {
            var result = await GetBambiReplyExAsync(userInput, isUserMessage);
            if (result.Refusal != null)
            {
                return result.Refusal.Source == ModerationSource.Input
                    ? ModerationRefusal.InputSentinel
                    : ModerationRefusal.OutputSentinel;
            }
            return result.Text;
        }

        public virtual async Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
        {
            var result = await GetChatResponseAsync(
                userInput,
                BambiSprite.GetSystemPrompt(),
                isUserMessage,
                returnRefusalSentinel: true);

            return ClassifyChatResult(result);
        }

        public virtual async Task<string?> GetAwarenessReactionAsync(
            string detectedName,
            string category,
            string serviceName = "",
            string pageTitle = "")
        {
            if (!IsAvailable)
                return null;

            var prompt = BambiSprite.GetSystemPrompt();
            var website = string.IsNullOrEmpty(serviceName) ? detectedName : serviceName;
            var tabName = string.IsNullOrEmpty(pageTitle) ? detectedName : pageTitle;
            var userInput = $"[Category: {category} | App: {website} | Title: {tabName} | Duration: 0m]";

            return await GetChatResponseAsync(userInput, prompt, isUserMessage: false, returnRefusalSentinel: false);
        }

        public virtual async Task<string?> GetStillOnReactionAsync(
            string displayName,
            string category,
            TimeSpan duration)
        {
            if (!IsAvailable)
                return null;

            var prompt = BambiSprite.GetSystemPrompt();

            string durationText;
            if (duration.TotalMinutes < 1)
                durationText = $"{(int)duration.TotalSeconds}s";
            else if (duration.TotalMinutes < 60)
                durationText = $"{(int)duration.TotalMinutes}m";
            else
                durationText = $"{(int)duration.TotalHours}h";

            var userInput = $"[Category: {category} | App: {displayName} | Title: {displayName} | Duration: {durationText}]";
            return await GetChatResponseAsync(userInput, prompt, isUserMessage: false, returnRefusalSentinel: false);
        }

        public virtual async Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
        {
            if (!IsAvailable)
                return null;

            var systemPrompt = BambiSprite.GetSystemPrompt();
            var userInput = string.IsNullOrEmpty(promptTemplate)
                ? $"You just caught the user on the word '{keyword}'. React in character, one short line."
                : promptTemplate.Replace("{keyword}", keyword);

            return await GetChatResponseAsync(userInput, systemPrompt, isUserMessage: false, returnRefusalSentinel: false);
        }

        public virtual async Task<string?> GetLockScreenReaction(
            string sentance,
            int mistakes,
            int amount,
            string? promptTemplate = null)
        {
            if (!IsAvailable)
                return null;

            var systemPrompt = BambiSprite.GetSystemPrompt();
            string userInput;
            if (string.IsNullOrEmpty(promptTemplate))
            {
                userInput = $"The user made {mistakes} mistakes in '{sentance}' for the lock screen. They had to type it {amount} of time. React in character, one short line.";
            }
            else
            {
                userInput = promptTemplate.Replace("{sentance}", sentance);
                userInput = userInput.Replace("{mistakes}", mistakes.ToString());
                userInput = userInput.Replace("{amount}", amount.ToString());
            }

            return await GetChatResponseAsync(userInput, systemPrompt, isUserMessage: false, returnRefusalSentinel: false);
        }

        public virtual async Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
        {
            if (!IsAvailable)
                return null;

            var systemPrompt = BambiSprite.GetSystemPrompt();
            var userInput = string.IsNullOrEmpty(promptTemplate)
                ? $"The user has just finished the mandatory video {title}. React in character, one short line."
                : promptTemplate.Replace("{title}", title);

            return await GetChatResponseAsync(userInput, systemPrompt, isUserMessage: false, returnRefusalSentinel: false);
        }

        /// <summary>
        /// Core pipeline shared by chat and all reaction paths:
        /// input moderation → build messages → raw completion → parse → output moderation
        /// → execute commands → update memory.
        /// </summary>
        protected virtual async Task<string?> GetChatResponseAsync(
            string userInput,
            string systemPrompt,
            bool isUserMessage,
            bool returnRefusalSentinel)
        {
            var guard = App.ModerationGuard;
            var modelHint = GetModelHint();

            // INPUT MODERATION (Layer 1 — code-side, prompt cannot bypass).
            if (guard != null)
            {
                var inputCheck = guard.CheckInput(userInput ?? string.Empty);
                if (!inputCheck.Allow && inputCheck.Category.HasValue)
                {
                    App.ModerationLog?.Record(inputCheck.Category.Value, source: "input", modelHint: modelHint);
                    // Only escalate the user-facing Content Policy Notice for content the
                    // user actually typed (interactive chat path). Background/auto reactions
                    // leave returnRefusalSentinel false and must not pop the warning.
                    if (returnRefusalSentinel)
                        App.ModerationCounter?.RecordHit(inputCheck.Category.Value, $"input:{GetProviderName()}");
                    App.Logger?.Information("{Provider}: input blocked by ModerationGuard (category={Cat})", GetProviderName(), inputCheck.Category);
                    return returnRefusalSentinel ? ModerationRefusal.InputSentinel : null;
                }
                if (inputCheck.Allow && inputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                {
                    App.ModerationLog?.Record(ProhibitedCategory.ProfessionalAdvice, source: "input", modelHint: modelHint);
                }
            }

            // Ensure memory is loaded on the first chat/reaction call. Providers are
            // constructed before App.Settings is fully ready in some paths; lazy loading
            // avoids reading stale state.
            ChatMemory.Load();

            // Inject the persisted subject profile into the system prompt once per session,
            // when a session is running. Reset when no session is active so the next session
            // gets a fresh injection.
            if (App.IsSessionRunning && !_subjectProfileInjected)
            {
                var profileAddendum = App.SubjectProfile?.BuildSystemPromptAddendum();
                if (!string.IsNullOrWhiteSpace(profileAddendum))
                {
                    systemPrompt = $"[SUBJECT PROFILE — long-term patterns across sessions]\n{profileAddendum}\n\n{systemPrompt}";
                }
                _subjectProfileInjected = true;
            }
            else if (!App.IsSessionRunning)
            {
                _subjectProfileInjected = false;
            }

            var enrichment = BuildEnrichmentMessage(userInput);
            var messages = ChatMemory.BuildSendMessages(
                new AiMessage("system", systemPrompt),
                enrichment,
                MaxSendPairs);
            messages.Add(new AiMessage("user", userInput));

            var raw = await GetRawCompletionAsync(messages, userInput, isUserMessage);
            if (string.IsNullOrWhiteSpace(raw))
            {
                // The user's turn never reached the model (or produced no usable
                // response), so do not remember it.
                ChatMemory.RemoveLastUserTurn();
                return null;
            }

            var parsed = Parser.Parse(raw);

            // OUTPUT MODERATION (Layer 1). Discard prohibited model output before display.
            if (guard != null)
            {
                var outputCheck = guard.CheckOutput(parsed.CleanText ?? string.Empty);
                if (!outputCheck.Allow && outputCheck.Category.HasValue)
                {
                    App.ModerationLog?.Record(outputCheck.Category.Value, source: "output", modelHint: modelHint);
                    // Model OUTPUT tripping the filter is not the user's doing — log for
                    // compliance but do NOT escalate the Content Policy Notice.
                    App.Logger?.Information("{Provider}: output blocked by ModerationGuard (category={Cat})", GetProviderName(), outputCheck.Category);
                    ChatMemory.RemoveLastAssistantTurn();
                    ChatMemory.RemoveLastUserTurn();
                    return returnRefusalSentinel ? ModerationRefusal.OutputSentinel : null;
                }
                if (outputCheck.Allow && outputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                {
                    App.ModerationLog?.Record(ProhibitedCategory.ProfessionalAdvice, source: "output", modelHint: modelHint);
                }
            }

            // Execute any parsed effect commands and collect outcomes for the next turn.
            _commandOutcomes.Clear();
            if (SupportsEffectCommands && parsed.Commands.Count > 0 && App.Commands != null)
            {
                App.Logger?.Information("{Provider}: parsed {Count} command(s) from response", GetProviderName(), parsed.Commands.Count);
                App.Commands.BeginBatch();
                foreach (var cmd in parsed.Commands)
                {
                    var outcome = App.Commands.ExecuteCommand(cmd);
                    if (outcome != null)
                        _commandOutcomes.Add(outcome);
                }
            }
            // Keep only the immediate turn's outcomes; cross-turn history is handled
            // by App.ActionHistory so the prompt doesn't duplicate information.
            while (_commandOutcomes.Count > 3) _commandOutcomes.RemoveAt(0);

            // Remember the completed exchange.
            ChatMemory.AddUserTurn(userInput);
            ChatMemory.AddAssistantTurn(raw);
            _ = Task.Run(ChatMemory.Save);

            if (ChatMemory.RestoredTurnCount > 0 && !_memoryRecallSignaled)
            {
                _memoryRecallSignaled = true;
                OnMemoryRecalled();
            }

            return string.IsNullOrWhiteSpace(parsed.CleanText) ? null : parsed.CleanText;
        }

        /// <summary>
        /// Turns the internal string result into the typed <see cref="AiReplyResult"/>
        /// used by the chat UI.
        /// </summary>
        protected AiReplyResult ClassifyChatResult(string? result)
        {
            var refusalSource = ModerationRefusal.GetSource(result);
            if (refusalSource.HasValue)
            {
                return new AiReplyResult(
                    string.Empty,
                    IsAiGenerated: false,
                    Refusal: new ModerationRefusalInfo(Category: null, Source: refusalSource.Value));
            }

            if (string.IsNullOrEmpty(result))
                return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

            // Best-effort: descriptive error strings produced by providers are parenthetical
            // diagnostics, NOT model output. Keep them out of the AI-badge path.
            if (result.StartsWith("(", StringComparison.Ordinal) && result.EndsWith(")", StringComparison.Ordinal))
                return new AiReplyResult(result, IsAiGenerated: false, Refusal: null);

            return new AiReplyResult(result, IsAiGenerated: true, Refusal: null);
        }

        /// <summary>
        /// Human-readable provider name for logs.
        /// </summary>
        protected virtual string GetProviderName() => GetType().Name;

        /// <summary>
        /// Clears this provider's in-memory and persisted chat history.
        /// </summary>
        public void ClearChatHistory() => ChatMemory.Clear();

        public abstract void Dispose();
    }
}
