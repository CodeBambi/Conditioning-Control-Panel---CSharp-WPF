using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService.Enrichment;
using ConditioningControlPanel.Services.Moderation;
// This file's unqualified `ChatMessage` is the mutable NESTED buffer type (nested types win over
// namespace types inside the class body). The Train 1 transport currency is the namespace-level
// record, aliased here so IAiService.SendAsync can be implemented without touching the ~15 existing
// nested-type references below.
using TransportMessage = ConditioningControlPanel.Services.AIService.ChatMessage;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// Bambi Companion AI provider backed by a local Ollama instance.
    /// Posts directly to <c>POST {host}/api/chat</c> with <c>stream:false</c> —
    /// no third-party client, since OllamaSharp 5.4.16 was returning 404 for
    /// model names that the same endpoint accepts via raw HTTP.
    /// </summary>
    public class LocalAiService : IAiService
    {
        private const string DefaultHost = "http://localhost:11434/";
        private const string DefaultModel = "qwen3.5:latest";

        private readonly BambiSprite _bambiSprite;
        private readonly IAiResponseParser _parser;
        private readonly KnowledgeService _knowledgeService;
        private readonly IPromptService _promptService;

        private readonly SemaphoreSlim _aiSemaphore = new(1, 1);
        private volatile bool _isProcessing;
        private bool _isUserQueued;

        // One moderation spine for BOTH entry points (legacy one-shots and the Train 1
        // multi-turn SendAsync). See TransportModeration for why this is an object and not
        // two copies of the same twenty lines.
        private readonly TransportModeration _moderation = CreateModeration();

        // Persistent chat history. Index 0 is system; optional enrichment block at 1
        // when effects are enabled; then alternating user/assistant turns.
        private readonly List<ChatMessage> _messages = new();

        private HttpClient _http;
        private string _activeHost;

        // Currently parsed commands from the most recent AI response.
        private List<AiCommandData> _currentCommands = new();

        public bool IsAvailable => true;
        public int DailyRequestsRemaining => -1;

        // Number of user/assistant turns seeded from disk at construction (a prior
        // session's conversation). Non-zero means persistent memory is in play.
        private int _restoredTurnCount;
        // Signal she_remembers at most once per session.
        private bool _memoryRecallSignaled;

        /// <summary>
        /// Raised once per session when the local companion produces a reply while the
        /// chat history contains turns restored from a previous session — i.e. persistent
        /// memory actually surfacing across launches. Static because the provider instance
        /// is owned by the AI strategy; GamificationBridge subscribes for the app lifetime.
        /// (Cloud AiService has no cross-session memory, so this is local-AI only.)
        /// </summary>
        public static event EventHandler? PersistentMemoryRecalled;

        private static void RaisePersistentMemoryRecalled()
        {
            try { PersistentMemoryRecalled?.Invoke(null, EventArgs.Empty); }
            catch (Exception ex) { App.Logger?.Debug("PersistentMemoryRecalled subscriber error: {Error}", ex.Message); }
        }

        public LocalAiService()
        {
            _bambiSprite = new BambiSprite();
            _activeHost = NormalizeHost(GetConfiguredHost());
            _http = BuildHttpClient(_activeHost);
            _parser = new AiResponseParser(GetFallbackResponse);
            _knowledgeService = new KnowledgeService();
            _promptService = new PromptService();

            // Load any persisted chat history from the previous app session. Local
            // models can hold long-running context; persistence makes Bambi remember
            // between launches. (Cloud provider doesn't have or use this.)
            LoadPersistedHistory();

            App.Logger?.Information("LocalAiService initialized (host={Host}, model={Model}, restored={Count} turns)",
                _activeHost, GetConfiguredModel(), _messages.Count);
        }

        // -------- Persistent chat memory (local only) --------

        // Cap on persisted user+assistant pairs. Picked so the file stays small (<200KB
        // typical) while preserving enough context that Bambi remembers a long conversation.
        private const int MaxPersistedPairs = 50;
        private static string HistoryFilePath =>
            Path.Combine(App.UserDataPath, "local_chat_history.json");

        private sealed class PersistedTurn
        {
            public string Role { get; set; } = "";
            public string Content { get; set; } = "";
        }

        /// <summary>
        /// Reads the persisted user/assistant history from disk and seeds <c>_messages</c>.
        /// Skips system and enrichment messages — those are rebuilt fresh per request.
        /// Best-effort: any parse failure or missing file results in an empty history.
        /// </summary>
        private void LoadPersistedHistory()
        {
            try
            {
                if (App.Settings?.Current?.CompanionPrompt?.ChatMemoryEnabled == false) return;
                if (!File.Exists(HistoryFilePath)) return;
                var json = File.ReadAllText(HistoryFilePath);
                var turns = JsonSerializer.Deserialize<List<PersistedTurn>>(json);
                if (turns == null) return;

                foreach (var t in turns)
                {
                    if (string.IsNullOrEmpty(t.Role) || string.IsNullOrEmpty(t.Content)) continue;
                    if (t.Role != "user" && t.Role != "assistant") continue;
                    _messages.Add(new ChatMessage(t.Role, t.Content));
                }

                // At construction _messages holds only restored turns (system/enrichment
                // are inserted later, per request), so this is the prior-session turn count.
                _restoredTurnCount = _messages.Count;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "LocalAiService: failed to load persisted chat history");
            }
        }

        /// <summary>
        /// Writes the user/assistant turns of the current conversation to disk.
        /// Drops the system prompt and any enrichment block so they're regenerated
        /// freshly on next load. Trimmed to <see cref="MaxPersistedPairs"/> recent
        /// turns (counted in pairs) to bound file size.
        /// </summary>
        private void PersistHistory()
        {
            try
            {
                if (App.Settings?.Current?.CompanionPrompt?.ChatMemoryEnabled == false) return;
                var dialogue = _messages
                    .Where(IsDialogueTurn)
                    .Select(m => new PersistedTurn { Role = m.Role, Content = m.Content ?? string.Empty })
                    .ToList();

                // Cap to last N pairs (one pair = user + assistant). Trim from the front.
                int maxMessages = MaxPersistedPairs * 2;
                if (dialogue.Count > maxMessages)
                {
                    dialogue = dialogue.Skip(dialogue.Count - maxMessages).ToList();
                }

                Directory.CreateDirectory(Path.GetDirectoryName(HistoryFilePath)!);
                var json = JsonSerializer.Serialize(dialogue, new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(HistoryFilePath, json);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "LocalAiService: failed to persist chat history");
            }
        }

        /// <summary>
        /// Identifies a genuine user/assistant dialogue turn — the exact set of messages
        /// <see cref="PersistHistory"/> writes to disk. Excludes the system prompt and the
        /// enrichment "[CONTEXT BLOCK — NOT DIALOGUE]" preamble (a user-role message that is
        /// context, not conversation). Shared so in-memory trimming and disk persistence
        /// count pairs identically.
        /// </summary>
        internal static bool IsDialogueTurn(ChatMessage m) =>
            (m.Role == "user" || m.Role == "assistant")
            && !string.IsNullOrEmpty(m.Content)
            && !m.Content!.Contains("[CONTEXT BLOCK — NOT DIALOGUE]");

        /// <summary>
        /// Bounds the in-memory <c>_messages</c> list to the same window
        /// <see cref="PersistHistory"/> writes to disk: the system message and any
        /// non-dialogue preamble (the enrichment context block) are always kept, plus only
        /// the most recent <see cref="MaxPersistedPairs"/> user/assistant pairs. Without this
        /// the list grows every turn and the whole list is posted to Ollama each request
        /// (see BuildResponse's <c>outgoing = _messages</c>), so the prompt/KV-cache balloons
        /// until Ollama exhausts system RAM (#631). Dialogue is identified via
        /// <see cref="IsDialogueTurn"/> so memory and disk stay consistent.
        /// </summary>
        private void TrimDialogueHistory() => TrimDialogueHistory(_messages, MaxPersistedPairs);

        /// <summary>
        /// Pure, testable core of the in-memory history cap (#631). Operates on an arbitrary
        /// message list so it can be exercised headlessly. Keeps the system message and any
        /// non-dialogue preamble (the enrichment context block) in place regardless of
        /// position, and retains only the most recent <paramref name="maxPairs"/>
        /// user/assistant pairs. Behavior is identical to the instance call
        /// <c>TrimDialogueHistory()</c> which passes <c>_messages</c> and
        /// <see cref="MaxPersistedPairs"/>.
        /// </summary>
        internal static void TrimDialogueHistory(List<ChatMessage> messages, int maxPairs)
        {
            int maxMessages = maxPairs * 2;

            // Collect indices of dialogue turns in order. Non-dialogue entries (system +
            // enrichment) are skipped so they're always preserved regardless of position.
            var dialogueIndices = new List<int>();
            for (int i = 0; i < messages.Count; i++)
            {
                if (IsDialogueTurn(messages[i])) dialogueIndices.Add(i);
            }
            if (dialogueIndices.Count <= maxMessages) return;

            // Drop the oldest dialogue turns from the front, keeping the most recent
            // maxMessages. Remove by descending index so earlier indices stay valid, and so
            // the tail (the just-appended turn the error paths RemoveAt) is never touched.
            int dropCount = dialogueIndices.Count - maxMessages;
            for (int k = dropCount - 1; k >= 0; k--)
            {
                messages.RemoveAt(dialogueIndices[k]);
            }
        }

        /// <summary>
        /// Clears in-memory and on-disk chat history. Useful for a "reset memory"
        /// button (not yet exposed in the UI).
        /// </summary>
        public void ClearHistory()
        {
            _messages.Clear();
            try { if (File.Exists(HistoryFilePath)) File.Delete(HistoryFilePath); }
            catch (Exception ex) { App.Logger?.Warning(ex, "LocalAiService: failed to delete chat history file"); }
            App.Logger?.Information("LocalAiService: chat history cleared");
        }

        /// <summary>
        /// Tells Ollama to load the configured model into memory so the first real
        /// chat doesn't pay the cold-start cost (~30-60s for 8B-class models on CPU,
        /// less on GPU). Sends an empty <c>/api/generate</c> request — Ollama treats
        /// this as a "load" hint without generating tokens. <c>keep_alive=10m</c> asks
        /// the model to stay resident longer than the default 5 minutes.
        /// Best-effort and silent on failure (Ollama may not be running yet).
        /// </summary>
        public async Task WarmUpAsync()
        {
            EnsureHost();
            var model = GetConfiguredModel();
            if (string.IsNullOrWhiteSpace(model)) return;

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                App.Logger?.Information("LocalAiService: warming up model={Model} on host={Host}", model, _activeHost);

                var payload = JsonSerializer.Serialize(new
                {
                    model = model,
                    keep_alive = "10m"
                });

                using var req = new HttpRequestMessage(HttpMethod.Post, "api/generate")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                using var resp = await _http.SendAsync(req);

                sw.Stop();
                if (resp.IsSuccessStatusCode)
                {
                    App.Logger?.Information("LocalAiService: warm-up succeeded in {Ms}ms", sw.ElapsedMilliseconds);
                }
                else
                {
                    App.Logger?.Information("LocalAiService: warm-up returned HTTP {Status} (model may not be pulled yet)",
                        (int)resp.StatusCode);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Information("LocalAiService: warm-up failed (Ollama not reachable?): {Error}", ex.Message);
            }
        }

        private static string NormalizeHost(string host)
        {
            return host.EndsWith("/", StringComparison.Ordinal) ? host : host + "/";
        }

        private static HttpClient BuildHttpClient(string host)
        {
            return new HttpClient
            {
                BaseAddress = new Uri(host),
                // Local model inference can be slow on first load (model warm-up).
                Timeout = TimeSpan.FromMinutes(5)
            };
        }

        private static string GetConfiguredHost()
        {
            var host = App.Settings?.Current?.CompanionPrompt?.AiOllamaHost;
            return string.IsNullOrWhiteSpace(host) ? DefaultHost : host;
        }

        private static string GetConfiguredModel()
        {
            var model = App.Settings?.Current?.CompanionPrompt?.AiModel;
            return string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        }

        /// <summary>Rebuilds HttpClient if the configured host has changed.</summary>
        private void EnsureHost()
        {
            var configured = NormalizeHost(GetConfiguredHost());
            if (string.Equals(configured, _activeHost, StringComparison.OrdinalIgnoreCase)) return;

            App.Logger?.Information("LocalAiService: host changed {Old} -> {New}, rebuilding HTTP client",
                _activeHost, configured);
            _http.Dispose();
            _activeHost = configured;
            _http = BuildHttpClient(_activeHost);
        }

        private static string GetFallbackResponse()
        {
            // Bambi keeps its flavored fallback; all other mods get a neutral one.
            if (App.Mods?.IsBambiMode == true)
                return "Bambi's head is so empty right now~ *giggles*";
            return "...";
        }

        /// <summary>
        /// This provider's moderation spine, pre-wired with its log prefix, counter source and
        /// <c>local:{model}</c> hint. Static so a test can exercise the real, provider-configured
        /// spine without standing up an Ollama client.
        /// </summary>
        internal static TransportModeration CreateModeration() =>
            new("LocalAiService", "local", () =>
            {
                var model = GetConfiguredModel();
                return "local:" + (string.IsNullOrWhiteSpace(model) ? "unknown" : model);
            });

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
        {
#pragma warning disable CS0618 // legacy internals: the adapter layer is one level up, in AiServiceStrategy
            var result = await GetBambiReplyExAsync(userInput, isUserMessage);
#pragma warning restore CS0618
            if (result.Refusal != null)
            {
                return result.Refusal.Source == ModerationSource.Input
                    ? ModerationRefusal.InputSentinel
                    : ModerationRefusal.OutputSentinel;
            }
            return result.Text;
        }

        /// <summary>
        /// Typed variant. See <see cref="IAiService.GetBambiReplyExAsync"/>.
        /// </summary>
        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
        {
            var prompt = _bambiSprite.GetSystemPrompt();
            // Mark this as a user request so a second click while busy gets a "still thinking"
            // phrase back instead of the bare fallback.
            var result = await GetAiResponseAsync(userInput, prompt, isUser: true, returnRefusalSentinel: true, purpose: AiMeter.PurposeChat);

            var refusalSource = ModerationRefusal.GetSource(result);
            if (refusalSource.HasValue)
            {
                return new AiReplyResult(
                    string.Empty,
                    IsAiGenerated: false,
                    Refusal: new ModerationRefusalInfo(Category: null, Source: refusalSource.Value));
            }

            // GetAiResponseAsync returns null/empty in a handful of paths that all represent
            // "we didn't get a usable LLM reply" — Ollama not reachable, empty content,
            // queue drop ("still thinking" phrase), or descriptive error strings produced by
            // DescribeOllamaError / DescribeChatException. Treat ALL of those as canned
            // (badge OFF) so the user doesn't see the pink AI badge over an error string.
            //
            // Heuristic: a real Ollama reply never starts with the literal "(" we use for
            // parenthetical diagnostic messages, and "still thinking" phrases are short and
            // come from a fixed pool. We don't try to filter those by content here — instead
            // we treat any null return as canned and let real content flow as AI.
            if (string.IsNullOrEmpty(result))
                return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

            // Best-effort: descriptive error strings produced by DescribeChatException /
            // DescribeOllamaError are parenthetical diagnostics, NOT model output. Keep them
            // out of the AI-badge path.
            if (result.StartsWith("(", StringComparison.Ordinal) && result.EndsWith(")", StringComparison.Ordinal))
                return new AiReplyResult(result, IsAiGenerated: false, Refusal: null);

            return new AiReplyResult(result, IsAiGenerated: true, Refusal: null);
        }

        /// <summary>
        /// Train 1 transport seam. See <see cref="IAiService.SendAsync"/> for the contract.
        ///
        /// <para>Ollama's <c>/api/chat</c> is natively multi-turn, so <paramref name="messages"/> is
        /// posted verbatim, in order, including the caller's system message. This method deliberately
        /// does NOT touch <c>_messages</c>: <c>CompanionBrain</c> owns conversation state now, and a
        /// provider keeping a parallel history is what made switching providers lose the thread (and
        /// what fixated ambient reactions on one video title). The legacy one-shot path below still
        /// uses <c>_messages</c> for as long as <c>UseCompanionBrain=false</c> is a supported
        /// configuration, and <c>local_chat_history.json</c> is still READ at construction so the
        /// brain can import it.</para>
        ///
        /// <para>Single-flight is preserved: an interactive send waits its turn, an ambient one is
        /// dropped when a call is already in flight (an ambient line is worth less than the chat reply
        /// it would queue behind). The brain enforces the same rule one level up; this is the floor
        /// that also covers a mixed build where a legacy call site is still live.</para>
        /// </summary>
        public async Task<AiReplyResult> SendAsync(IReadOnlyList<TransportMessage> messages, AiCallOptions options,
            CancellationToken cancellationToken = default)
        {
            options ??= AiCallOptions.Chat;
            var incoming = messages ?? (IReadOnlyList<TransportMessage>)Array.Empty<TransportMessage>();

            // [AI-METER] — log-only sizing, stamped with the caller's purpose. Refined to the real
            // outgoing list once the enrichment block (if any) has been folded in.
            var meterStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var meterInputChars = TotalChars(incoming);
            void Meter(string outcome, int outputChars = 0) =>
                AiMeter.Record(AiMeter.ProviderLocal, options.MeterPurpose, meterInputChars, outputChars,
                    meterStopwatch.ElapsedMilliseconds, outcome);

            // INPUT MODERATION (Layer 1 — code-side, prompt cannot bypass). Only the newest user-role
            // message is text we have not already checked: earlier turns passed the guard when they
            // were first sent, and re-checking them would double-write the compliance log.
            var newestUser = NewestUserText(incoming);
            var blockedInput = _moderation.CheckInput(newestUser, escalate: options.Interactive);
            if (blockedInput.HasValue)
            {
                Meter(AiMeter.OutcomeRefusedInput);
                return new AiReplyResult(string.Empty, IsAiGenerated: false,
                    Refusal: new ModerationRefusalInfo(blockedInput, ModerationSource.Input));
            }

            if (!options.Interactive && _isProcessing)
            {
                App.Logger?.Debug("LocalAiService.SendAsync: ambient request dropped (busy)");
                return new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null);
            }

            try
            {
                await _aiSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null);
            }
            catch (ObjectDisposedException)
            {
                return new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null);
            }

            _isProcessing = true;
            EnsureHost();
            var model = GetConfiguredModel();

            try
            {
                var outgoing = BuildOutgoing(incoming, BuildEnrichmentContent());
                meterInputChars = outgoing.Sum(m => m.Content?.Length ?? 0);

                App.Logger?.Information(
                    "LocalAiService.SendAsync: sending to Ollama (model={Model}, msgs={MsgCount}, purpose={Purpose})",
                    model, outgoing.Count, options.MeterPurpose);

                var (status, body) = await SendChatAsync(model,
                    outgoing.Select(m => (m.Role, (string?)m.Content)), cancellationToken).ConfigureAwait(false);

                if (status != 200)
                {
                    App.Logger?.Warning("LocalAiService.SendAsync: Ollama returned HTTP {Status}: {Body}", status, body);
                    Meter(AiMeter.OutcomeError);
                    // Diagnostics are not model output and must never wear the AI badge, but they are
                    // the single most useful thing we can show ("Ollama isn't running — start it").
                    return new AiReplyResult(DescribeOllamaError(status, body, model), IsAiGenerated: false, Refusal: null);
                }

                var content = ExtractContent(body);
                if (string.IsNullOrEmpty(content))
                {
                    App.Logger?.Warning("LocalAiService.SendAsync: empty content in 200 response: {Body}", Truncate(body, 300));
                    Meter(AiMeter.OutcomeEmpty);
                    return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);
                }

                var parsed = _parser.Parse(content);

                // OUTPUT MODERATION (Layer 1) on the user-visible text — the JSON effects wrapper is
                // already stripped by the parser. A blocked turn fires NO effects and shows no text;
                // the caller (CompanionBrain) rolls its turns back so nothing reaches disk (P2/H5).
                var blockedOutput = _moderation.CheckOutput(parsed.CleanText);
                if (blockedOutput.HasValue)
                {
                    _currentCommands = new List<AiCommandData>();
                    Meter(AiMeter.OutcomeRefusedOutput, content.Length);
                    return new AiReplyResult(string.Empty, IsAiGenerated: false,
                        Refusal: new ModerationRefusalInfo(blockedOutput, ModerationSource.Output));
                }

                _currentCommands = parsed.Commands;
                if (_currentCommands.Count > 0 && App.Commands != null)
                {
                    App.Logger?.Information("LocalAiService.SendAsync: parsed {Count} command(s) from response",
                        _currentCommands.Count);
                    App.Commands.BeginBatch();
                    foreach (var cmd in _currentCommands) App.Commands.ExecuteCommand(cmd);
                }

                if (string.IsNullOrWhiteSpace(parsed.CleanText))
                {
                    Meter(AiMeter.OutcomeEmpty, content.Length);
                    return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);
                }

                Meter(AiMeter.OutcomeOk, content.Length);
                return new AiReplyResult(parsed.CleanText, IsAiGenerated: true, Refusal: null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Meter(AiMeter.OutcomeError);
                return new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "LocalAiService.SendAsync: chat call threw (host={Host}, model={Model})",
                    _activeHost, model);
                Meter(AiMeter.OutcomeError);
                return new AiReplyResult(DescribeChatException(ex, model), IsAiGenerated: false, Refusal: null);
            }
            finally
            {
                _isProcessing = false;
                try { _aiSemaphore.Release(); } catch (ObjectDisposedException) { }
            }
        }

        /// <summary>
        /// The newest user-authored message in a transport window — the only text
        /// <see cref="SendAsync"/> hands to <c>CheckInput</c>. Everything before it already passed the
        /// guard on the turn it was sent, and re-checking it would double the compliance log and halve
        /// the hits needed to trip the escalating counter.
        /// </summary>
        internal static string NewestUserText(IReadOnlyList<TransportMessage> messages)
        {
            if (messages == null) return string.Empty;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role == TransportMessage.RoleUser) return messages[i].Content ?? string.Empty;
            }
            return string.Empty;
        }

        private static int TotalChars(IReadOnlyList<TransportMessage> messages)
        {
            var total = 0;
            for (int i = 0; i < messages.Count; i++) total += messages[i].Content?.Length ?? 0;
            return total;
        }

        /// <summary>
        /// The outgoing list: the caller's window, with the effects <c>[CONTEXT BLOCK]</c> spliced in
        /// immediately after the leading system message(s) — the same position the legacy path used
        /// (index 1). Pure, so the placement is pinned by a test rather than by reading the wire.
        /// </summary>
        internal static List<TransportMessage> BuildOutgoing(IReadOnlyList<TransportMessage> messages, string? enrichment)
        {
            var list = new List<TransportMessage>((messages?.Count ?? 0) + 1);
            if (messages != null) list.AddRange(messages);
            if (string.IsNullOrWhiteSpace(enrichment)) return list;

            var insertAt = 0;
            while (insertAt < list.Count && list[insertAt].Role == TransportMessage.RoleSystem) insertAt++;
            list.Insert(insertAt, TransportMessage.User(enrichment!));
            return list;
        }

        /// <summary>
        /// The enrichment "[CONTEXT BLOCK — NOT DIALOGUE]" message, or null when AI Companion Effects
        /// are off. Best-effort: a knowledge/prompt failure degrades to "no effects this turn" rather
        /// than taking the reply down.
        /// </summary>
        private string? BuildEnrichmentContent()
        {
            if (App.Settings?.Current?.CompanionPrompt?.AllowAiToControlEffects != true) return null;
            try
            {
                var currentTime = DateTime.Now.ToString("yyyy-M-dd dddd h:mm:ss tt");
                var facts = _knowledgeService.GetKnowledge("");
                var factsJson = JsonSerializer.Serialize(facts);
                return _promptService.BuildEnrichmentMessage(factsJson, currentTime).Content;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("LocalAiService: enrichment block build failed: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Raises <see cref="PersistentMemoryRecalled"/> from outside this class. Exists so
        /// <c>CompanionBrain</c> — which took over cross-session dialogue persistence in Train 1 —
        /// can fire the same signal that drives the <c>she_remembers</c> achievement and the
        /// matching bark trigger, now for cloud users too. The event stays declared here so its two
        /// existing subscribers (GamificationBridge, BarkService) need no change.
        /// </summary>
        internal static void SignalPersistentMemoryRecalled() => RaisePersistentMemoryRecalled();

        private static readonly string[] StillThinkingPhrases =
        {
            "Hmm... still thinking.",
            "One sec, processing...",
            "Almost there.",
            "Thinking...",
            "Hold on, working on it.",
            "Just a moment."
        };

        private static string GetRandomThinkingPhrase()
        {
            var modPhrases = App.Mods?.GetPhrases("Thinking");
            var pool = modPhrases != null && modPhrases.Length > 0 ? modPhrases : StillThinkingPhrases;
            return pool[_random.Next(pool.Length)];
        }

        private static readonly Random _random = new();

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "", TimeSpan? duration = null)
        {
            var prompt = _bambiSprite.GetSystemPrompt();
            var userInput = FrameFormatter.AwarenessFrame(detectedName, category, serviceName, pageTitle, duration);
            return await GetAiResponseAsync(userInput, prompt, purpose: AiMeter.PurposeAwareness);
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
        {
            var prompt = _bambiSprite.GetSystemPrompt();
            var userInput = FrameFormatter.StillOnFrame(displayName, category, duration);
            return await GetAiResponseAsync(userInput, prompt, purpose: AiMeter.PurposeStillOn);
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
        {
            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = FrameFormatter.KeywordFrame(keyword, promptTemplate);
            return await GetAiResponseAsync(userInput, systemPrompt, purpose: AiMeter.PurposeKeyword);
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
        {
            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = FrameFormatter.LockScreenFrame(sentance, mistakes, amount, promptTemplate);
            return await GetAiResponseAsync(userInput, systemPrompt, purpose: AiMeter.PurposeLockScreen);
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
        {
            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = FrameFormatter.VideoDoneFrame(title, promptTemplate);
            return await GetAiResponseAsync(userInput, systemPrompt, purpose: AiMeter.PurposeVideoDone);
        }

        private async Task<string?> GetAiResponseAsync(string userInput, string systemPrompt, bool isUser = false, bool returnRefusalSentinel = false,
            string purpose = AiMeter.PurposeChat)
        {
            // [AI-METER] — log-only sizing, one line per request attempt (plus refused input,
            // a request we deliberately didn't make). Queue-drop paths never reach Ollama and
            // stay silent. meterInputChars is refined to the real outgoing message list below.
            var meterStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var meterInputChars = (systemPrompt?.Length ?? 0) + (userInput?.Length ?? 0);
            void Meter(string outcome, int outputChars = 0) =>
                AiMeter.Record(AiMeter.ProviderLocal, purpose, meterInputChars, outputChars,
                    meterStopwatch.ElapsedMilliseconds, outcome);

            // INPUT MODERATION (Layer 1 — code-side, prompt cannot bypass). Same semantics
            // as AiService cloud path: hard categories block before the HTTP call; refusal
            // sentinel surfaces only when the caller is the chat UI. Shared with SendAsync
            // through _moderation so the two entry points cannot fork the spine.
            if (_moderation.CheckInput(userInput, escalate: returnRefusalSentinel).HasValue)
            {
                Meter(AiMeter.OutcomeRefusedInput);
                return returnRefusalSentinel ? ModerationRefusal.InputSentinel : null;
            }

            if (isUser)
            {
                if (_isUserQueued) { App.Logger?.Debug("LocalAiService: user request dropped (one already queued)"); return GetRandomThinkingPhrase(); }
                _isUserQueued = true;
            }
            else
            {
                if (_isProcessing) { App.Logger?.Debug("LocalAiService: automated request dropped (busy)"); return null; }
            }

            await _aiSemaphore.WaitAsync();
            if (isUser) _isUserQueued = false;
            _isProcessing = true;

            EnsureHost();
            var model = GetConfiguredModel();

            try
            {
                // System prompt at index 0, refreshed each call so prompt edits take effect immediately.
                var sys = _messages.FirstOrDefault(m => m.Role == "system");
                if (sys == null) _messages.Insert(0, new ChatMessage("system", systemPrompt));
                else sys.Content = systemPrompt;

                // Optional enrichment block right after the system message (only when effects on).
                var effectsEnabled = App.Settings?.Current?.CompanionPrompt?.AllowAiToControlEffects == true;
                var oldEnrichment = _messages.FirstOrDefault(m => m.Content?.Contains("[CONTEXT BLOCK — NOT DIALOGUE]") == true);

                if (effectsEnabled)
                {
                    var currentTime = DateTime.Now.ToString("yyyy-M-dd dddd h:mm:ss tt");
                    var facts = _knowledgeService.GetKnowledge("");
                    var factsJson = JsonSerializer.Serialize(facts);
                    var enrichment = _promptService.BuildEnrichmentMessage(factsJson, currentTime);

                    if (oldEnrichment != null) oldEnrichment.Content = enrichment.Content;
                    else _messages.Insert(1, new ChatMessage("user", enrichment.Content));
                }
                else if (oldEnrichment != null)
                {
                    _messages.Remove(oldEnrichment);
                }

                // Build the outgoing message list. Genuine USER chat carries the full persistent
                // history for conversational continuity. AUTOMATED ambient reactions (screen
                // awareness, "still on", keyword, idle, video-done) do NOT: they used to share this
                // same chat thread, so every past "...watch Naughty Bambi~" line became a few-shot
                // example the local model parroted — fixating it on one title regardless of the
                // (randomized) system prompt and bleeding suggestions across mods. Ambient reactions
                // now send a STATELESS [system, (enrichment), userInput] and are never appended or
                // persisted, so the system prompt's rotating video pool actually drives variety.
                List<ChatMessage> outgoing;
                if (isUser)
                {
                    _messages.Add(new ChatMessage("user", userInput));
                    // Bound memory (and thus the prompt sent to Ollama) to the same window we
                    // persist to disk. Trims from the front, so the just-appended user turn the
                    // error paths below RemoveAt stays the last element. (#631)
                    TrimDialogueHistory();
                    outgoing = _messages;
                }
                else
                {
                    outgoing = new List<ChatMessage>();
                    var sysMsg = _messages.FirstOrDefault(m => m.Role == "system");
                    if (sysMsg != null) outgoing.Add(sysMsg);
                    if (effectsEnabled)
                    {
                        var enr = _messages.FirstOrDefault(m => m.Content?.Contains("[CONTEXT BLOCK — NOT DIALOGUE]") == true);
                        if (enr != null) outgoing.Add(enr);
                    }
                    outgoing.Add(new ChatMessage("user", userInput));
                }

                App.Logger?.Information("LocalAiService: sending to Ollama (model={Model}, effects={Effects}, isUser={IsUser}, msgs={MsgCount})",
                    model, effectsEnabled, isUser, outgoing.Count);

                meterInputChars = outgoing.Sum(m => m.Content?.Length ?? 0);

                var (status, body) = await SendChatAsync(model, outgoing.Select(m => (m.Role, m.Content)));

                if (status != 200)
                {
                    App.Logger?.Warning("LocalAiService: Ollama returned HTTP {Status}: {Body}", status, body);
                    // Roll back the user turn so we don't poison history with an unanswered turn.
                    // (Automated reactions never appended to _messages, so there's nothing to undo.)
                    if (isUser && _messages.Count > 0 && _messages[^1].Role == "user") _messages.RemoveAt(_messages.Count - 1);
                    Meter(AiMeter.OutcomeError);
                    return DescribeOllamaError(status, body, model);
                }

                var content = ExtractContent(body);
                if (string.IsNullOrEmpty(content))
                {
                    App.Logger?.Warning("LocalAiService: empty content in 200 response: {Body}", Truncate(body, 300));
                    if (isUser && _messages.Count > 0 && _messages[^1].Role == "user") _messages.RemoveAt(_messages.Count - 1);
                    Meter(AiMeter.OutcomeEmpty);
                    return GetFallbackResponse();
                }

                App.Logger?.Information("LocalAiService: got reply ({Len} chars)", content.Length);

                // Append assistant turn so future requests have context — USER chat only.
                // Automated ambient reactions stay stateless (see outgoing-list note above).
                if (isUser)
                    _messages.Add(new ChatMessage("assistant", content));

                var parsed = _parser.Parse(content);
                _currentCommands = parsed.Commands;

                if (effectsEnabled)
                {
                    App.Logger?.Information("LocalAiService: parsed {Count} command(s) from response", _currentCommands.Count);
                }

                // OUTPUT MODERATION (Layer 1). Scan the user-visible text — the JSON
                // effects wrapper is already stripped by the parser. If the model produced
                // prohibited content we discard the WHOLE turn (no commands executed, no
                // text displayed, persistence skipped) and return the refusal sentinel or
                // null per caller.
                //
                // P2/H5: persistence is deferred until AFTER output moderation passes.
                // Previously PersistHistory() ran before the output check, so a prohibited
                // assistant turn (and its preceding user turn) would land on disk and be
                // reloaded next launch. The user/assistant turns are also rolled back from
                // the in-memory _messages list so future requests don't carry the rejected
                // context.
                if (_moderation.CheckOutput(parsed.CleanText).HasValue)
                {
                    // Don't fire effects from a blocked response.
                    _currentCommands = new List<AiCommandData>();
                    // Roll back assistant turn first (most recent), then the user turn
                    // that produced it. PersistHistory is NOT called — the file on disk
                    // remains at the prior known-clean state. (Automated reactions never
                    // appended to _messages, so there's nothing to roll back for them.)
                    if (isUser)
                    {
                        if (_messages.Count > 0 && _messages[^1].Role == "assistant") _messages.RemoveAt(_messages.Count - 1);
                        if (_messages.Count > 0 && _messages[^1].Role == "user") _messages.RemoveAt(_messages.Count - 1);
                    }
                    Meter(AiMeter.OutcomeRefusedOutput, content.Length);
                    return returnRefusalSentinel ? ModerationRefusal.OutputSentinel : null;
                }

                // Persist asynchronously so chat latency isn't impacted by disk I/O.
                // Runs only after output moderation passed (P2/H5), and only for genuine USER
                // chat — automated ambient reactions are stateless and must not be saved/replayed.
                if (isUser)
                    _ = Task.Run(PersistHistory);

                // she_remembers: a reply was produced while turns restored from a previous
                // session are in context — persistent memory surfacing across launches.
                // Signal once per session; GamificationBridge entitlement-gates the unlock.
                if (_restoredTurnCount > 0 && !_memoryRecallSignaled)
                {
                    _memoryRecallSignaled = true;
                    RaisePersistentMemoryRecalled();
                }

                if (_currentCommands.Count > 0 && App.Commands != null)
                {
                    App.Commands.BeginBatch();
                    foreach (var cmd in _currentCommands) App.Commands.ExecuteCommand(cmd);
                }

                Meter(string.IsNullOrWhiteSpace(parsed.CleanText) ? AiMeter.OutcomeEmpty : AiMeter.OutcomeOk, content.Length);

                return parsed.CleanText;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "LocalAiService: chat call threw (host={Host}, model={Model})", _activeHost, model);
                if (_messages.Count > 0 && _messages[^1].Role == "user") _messages.RemoveAt(_messages.Count - 1);
                Meter(AiMeter.OutcomeError);
                return DescribeChatException(ex, model);
            }
            finally
            {
                _isProcessing = false;
                // Semaphore may have been disposed if the app shut down mid-request.
                try { _aiSemaphore.Release(); } catch (ObjectDisposedException) { }
            }
        }

        /// <summary>
        /// One POST to <c>/api/chat</c>. Takes role/content tuples so the legacy path's mutable
        /// <see cref="ChatMessage"/> buffer and <see cref="SendAsync"/>'s immutable
        /// <see cref="TransportMessage"/> window share a single wire encoder.
        /// </summary>
        private async Task<(int status, string body)> SendChatAsync(string model,
            IEnumerable<(string role, string? content)> messages, CancellationToken cancellationToken = default)
        {
            // think:false tells reasoning models (qwen3, deepseek-r1, etc.) to skip the
            // long internal reasoning phase and respond directly. Non-reasoning models
            // ignore it. This can cut response time from ~50s to ~3s for qwen3-class models.
            var payload = new
            {
                model = model,
                messages = messages.Select(m => new { role = m.role, content = m.content ?? string.Empty }).ToArray(),
                stream = false,
                think = false
            };
            var json = JsonSerializer.Serialize(payload);

            using var req = new HttpRequestMessage(HttpMethod.Post, "api/chat")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            using var resp = await _http.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            return ((int)resp.StatusCode, body);
        }

        private static string ExtractContent(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var c))
                {
                    return c.GetString() ?? string.Empty;
                }
            }
            catch { }
            return string.Empty;
        }

        /// <summary>
        /// One-shot, stateless multi-turn chat completion against the configured Ollama
        /// host/model. For non-companion features (e.g. the Lab Quiz) that maintain their
        /// own conversation and just need raw assistant text — no persona wrapper, no
        /// persistent history, no command parsing. Returns the assistant content, or
        /// <c>null</c> on any transport / HTTP / parse failure so the caller can fall back.
        /// </summary>
        public static async Task<string?> GetRawChatCompletionAsync(
            IEnumerable<(string role, string content)> messages,
            double temperature = 0.8)
        {
            var host = NormalizeHost(GetConfiguredHost());
            var model = GetConfiguredModel();
            if (string.IsNullOrWhiteSpace(model)) return null;

            try
            {
                using var http = BuildHttpClient(host);
                var payload = new
                {
                    model,
                    messages = messages.Select(m => new { role = m.role, content = m.content ?? string.Empty }).ToArray(),
                    stream = false,
                    think = false,
                    options = new { temperature }
                };
                var json = JsonSerializer.Serialize(payload);

                using var req = new HttpRequestMessage(HttpMethod.Post, "api/chat")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                using var resp = await http.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("LocalAiService.GetRawChatCompletionAsync: HTTP {Status} from {Host} (model={Model})",
                        (int)resp.StatusCode, host, model);
                    return null;
                }

                // #739: think:false is requested above, but a model that ignores it (or a non-Ollama
                // OpenAI-compatible host behind the same setting) still returns its scratchpad.
                var content = AiTextHygiene.Clean(ExtractContent(body));
                return string.IsNullOrWhiteSpace(content) ? null : content;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "LocalAiService.GetRawChatCompletionAsync failed (host={Host}, model={Model})", host, model);
                return null;
            }
        }

        /// <summary>
        /// Turn a chat-call exception into a user-facing line that points at the most likely
        /// cause. Generic "(Ollama call failed: ...)" leaves users guessing — connection
        /// refused almost always means Ollama isn't running, and a clear hint to start it
        /// (or install it) cuts most "AI doesn't work" reports (#151).
        /// </summary>
        private string DescribeChatException(Exception ex, string model)
        {
            // Walk to the innermost exception — HttpRequestException usually wraps a
            // SocketException whose message names the actual failure.
            var inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            var msg = inner.Message ?? string.Empty;

            bool refused = msg.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase)
                || ex is HttpRequestException && msg.Contains("connection", StringComparison.OrdinalIgnoreCase);
            bool dnsFail = msg.Contains("No such host", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("name resolution", StringComparison.OrdinalIgnoreCase);
            bool timeout = ex is TaskCanceledException || msg.Contains("timeout", StringComparison.OrdinalIgnoreCase);

            if (refused)
                return $"(Can't reach Ollama at {_activeHost} — looks like it isn't running. Start Ollama, or install it from https://ollama.com)";
            if (dnsFail)
                return $"(Can't reach Ollama host {_activeHost} — check the host setting in Companion → AI)";
            if (timeout)
                return $"(Ollama took too long to respond. The first request after launch can take ~30-60s as the model loads — try once more.)";

            return $"(Ollama call failed: {ex.Message})";
        }

        private static string DescribeOllamaError(int status, string body, string model)
        {
            // Try to surface Ollama's structured "error" field.
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    var msg = err.GetString() ?? string.Empty;
                    if (status == 404 && msg.Contains("not found", StringComparison.OrdinalIgnoreCase))
                        return $"(Ollama: model '{model}' not found — check 'ollama list' or pull it)";
                    return $"(Ollama HTTP {status}: {msg})";
                }
            }
            catch { }
            return $"(Ollama HTTP {status}: {Truncate(body, 200)})";
        }

        private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "...";

        /// <summary>
        /// Queries Ollama at the given host for the installed model tags. Returns an
        /// empty list if Ollama isn't reachable or the response can't be parsed.
        /// </summary>
        public static async Task<List<string>> ListInstalledModelsAsync(string? host = null)
        {
            var configured = NormalizeHost(string.IsNullOrWhiteSpace(host) ? GetConfiguredHost() : host);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                using var resp = await http.GetAsync(configured + "api/tags");
                if (!resp.IsSuccessStatusCode)
                {
                    App.Logger?.Information("LocalAiService.ListInstalledModelsAsync: HTTP {Status} from {Host}",
                        (int)resp.StatusCode, configured);
                    return new List<string>();
                }

                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("models", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return new List<string>();

                var names = new List<string>();
                foreach (var m in arr.EnumerateArray())
                {
                    if (m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                    {
                        var name = n.GetString();
                        if (!string.IsNullOrEmpty(name)) names.Add(name);
                    }
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return names;
            }
            catch (Exception ex)
            {
                App.Logger?.Information(ex, "LocalAiService.ListInstalledModelsAsync: failed to reach {Host}", configured);
                return new List<string>();
            }
        }

        public void Dispose()
        {
            // Best-effort synchronous unload so the model doesn't sit in VRAM/RAM for the
            // full keep_alive window after the app exits (#629). Ollama frees the model when
            // it receives keep_alive=0. Bounded to ~2s so shutdown never hangs when Ollama
            // isn't running / reachable. Sent BEFORE _http is disposed.
            var model = GetConfiguredModel();
            if (!string.IsNullOrWhiteSpace(model))
            {
                try
                {
                    var payload = BuildUnloadPayload(model);
                    using var req = new HttpRequestMessage(HttpMethod.Post, "api/generate")
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    };
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    using var resp = _http.Send(req, cts.Token);
                    App.Logger?.Information("LocalAiService: sent model unload (keep_alive=0) for {Model} on dispose", model);
                }
                catch (Exception ex)
                {
                    App.Logger?.Information("LocalAiService: model unload on dispose skipped ({Error})", ex.Message);
                }
            }

            _aiSemaphore.Dispose();
            _http.Dispose();
        }

        /// <summary>
        /// Builds the JSON body of the best-effort model-unload request sent on
        /// <see cref="Dispose"/> (#629): <c>{"model": ..., "keep_alive": 0}</c>. Ollama
        /// evicts the model from VRAM/RAM immediately when it receives keep_alive=0.
        /// Extracted so the payload shape can be asserted headlessly.
        /// </summary>
        internal static string BuildUnloadPayload(string model) =>
            JsonSerializer.Serialize(new
            {
                model = model,
                keep_alive = 0
            });

        internal sealed class ChatMessage
        {
            public string Role { get; set; }
            public string? Content { get; set; }
            public ChatMessage(string role, string? content) { Role = role; Content = content; }
        }
    }
}
