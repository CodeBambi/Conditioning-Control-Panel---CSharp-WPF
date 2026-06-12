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

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// Bambi Companion AI provider backed by a local Ollama instance.
    /// Posts directly to <c>POST {host}/api/chat</c> with <c>stream:false</c> —
    /// no third-party client, since OllamaSharp 5.4.16 was returning 404 for
    /// model names that the same endpoint accepts via raw HTTP.
    /// </summary>
    public class LocalAiService : AiProviderBase
    {
        private const string DefaultHost = "http://localhost:11434/";
        private const string DefaultModel = "qwen3.5:latest";

        private readonly SemaphoreSlim _aiSemaphore = new(1, 1);
        private bool _isProcessing;
        private bool _isUserQueued;

        // Persistent chat history. Index 0 is system; optional enrichment block at 1
        // when effects are enabled; then alternating user/assistant turns.
        private readonly List<ChatMessage> _messages = new();

        private HttpClient _http;
        private string _activeHost;

        public override bool IsAvailable => true;
        public override int DailyRequestsRemaining => -1;

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
            _activeHost = NormalizeHost(GetConfiguredHost());
            _http = BuildHttpClient(_activeHost);

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
                    .Where(m => m.Role == "user" || m.Role == "assistant")
                    .Where(m => !string.IsNullOrEmpty(m.Content)
                                && !m.Content!.Contains("[CONTEXT BLOCK — NOT DIALOGUE]"))
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
        /// Clears in-memory and on-disk chat history.
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
        /// this as a "load" hint without generating tokens. <c>keep_alive=30m</c> asks
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
                    keep_alive = "30m"
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

        protected override string GetFallbackResponse()
        {
            // Bambi keeps its flavored fallback; all other mods get a neutral one.
            if (App.Mods?.IsBambiMode == true)
                return "Bambi's head is so empty right now~ *giggles*";
            return "...";
        }

        protected override string GetModelHint()
        {
            var model = GetConfiguredModel();
            return "local:" + (string.IsNullOrWhiteSpace(model) ? "unknown" : model);
        }

        protected override string GetProviderName() => "LocalAiService";

        public override async Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
        {
            // Local chat always treats the request as a user message for queue/drop logic.
            _ = isUserMessage;
            var result = await base.GetBambiReplyExAsync(userInput, isUserMessage: true);
            return result;
        }

        /// <summary>
        /// Adds local-AI queue/drop semantics around the shared pipeline.
        /// </summary>
        protected override async Task<string?> GetChatResponseAsync(
            string userInput,
            string systemPrompt,
            bool isUserMessage,
            bool returnRefusalSentinel)
        {
            if (isUserMessage)
            {
                if (_isUserQueued) { App.Logger?.Debug("LocalAiService: user request dropped (one already queued)"); return GetRandomThinkingPhrase(); }
                _isUserQueued = true;
            }
            else
            {
                if (_isProcessing) { App.Logger?.Debug("LocalAiService: automated request dropped (busy)"); return null; }
            }

            await _aiSemaphore.WaitAsync();
            if (isUserMessage) _isUserQueued = false;
            _isProcessing = true;

            try
            {
                return await base.GetChatResponseAsync(userInput, systemPrompt, isUserMessage, returnRefusalSentinel);
            }
            finally
            {
                _isProcessing = false;
                // Semaphore may have been disposed if the app shut down mid-request.
                try { _aiSemaphore.Release(); } catch (ObjectDisposedException) { }
            }
        }

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

        /// <summary>
        /// Core transport: maintains the persistent message list, sends to Ollama,
        /// and returns raw assistant content. Moderation and command execution are
        /// handled by the base class pipeline.
        /// </summary>
        protected override async Task<string?> GetRawCompletionAsync(
            string systemPrompt,
            string userInput,
            bool isUserMessage)
        {
            _ = isUserMessage;

            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("LocalAiService: Offline mode enabled, skipping AI request");
                return null;
            }

            EnsureHost();
            var model = GetConfiguredModel();

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
                var facts = KnowledgeService.GetKnowledge(userInput, maxResults: 20);
                var factsJson = JsonSerializer.Serialize(facts);
                var enrichment = PromptService.BuildEnrichmentMessage(factsJson, currentTime);

                if (oldEnrichment != null) oldEnrichment.Content = enrichment.Content;
                else _messages.Insert(1, new ChatMessage("user", enrichment.Content));
            }
            else if (oldEnrichment != null)
            {
                _messages.Remove(oldEnrichment);
            }

            // Append the new user turn.
            _messages.Add(new ChatMessage("user", userInput));

            App.Logger?.Information("LocalAiService: sending to Ollama (model={Model}, effects={Effects}, msgs={MsgCount})",
                model, effectsEnabled, _messages.Count);

            var (status, body) = await SendChatAsync(model, _messages);

            if (status != 200)
            {
                App.Logger?.Warning("LocalAiService: Ollama returned HTTP {Status}: {Body}", status, body);
                // Roll back the user turn so we don't poison history with an unanswered turn.
                if (_messages.Count > 0 && _messages[^1].Role == "user") _messages.RemoveAt(_messages.Count - 1);
                return DescribeOllamaError(status, body, model);
            }

            var content = ExtractContent(body);
            if (string.IsNullOrEmpty(content))
            {
                App.Logger?.Warning("LocalAiService: empty content in 200 response: {Body}", Truncate(body, 300));
                if (_messages.Count > 0 && _messages[^1].Role == "user") _messages.RemoveAt(_messages.Count - 1);
                return null;
            }

            App.Logger?.Information("LocalAiService: got reply ({Len} chars)", content.Length);

            // Append assistant turn so future requests have context.
            _messages.Add(new ChatMessage("assistant", content));

            return content;
        }

        /// <summary>
        /// Rolls back the assistant and user turns when output moderation blocks the
        /// response, and skips persistence so the rejected exchange isn't remembered.
        /// </summary>
        protected override void OnOutputModerationBlocked(string userInput, string rawResponse)
        {
            // Roll back assistant turn first (most recent), then the user turn that produced it.
            if (_messages.Count > 0 && _messages[^1].Role == "assistant") _messages.RemoveAt(_messages.Count - 1);
            if (_messages.Count > 0 && _messages[^1].Role == "user") _messages.RemoveAt(_messages.Count - 1);
        }

        /// <summary>
        /// Persists the accepted conversation and signals memory recall after the
        /// response has passed moderation.
        /// </summary>
        protected override void OnResponseAccepted(string userInput, string rawResponse)
        {
            // Persist asynchronously so chat latency isn't impacted by disk I/O.
            _ = Task.Run(PersistHistory);

            // she_remembers: a reply was produced while turns restored from a previous
            // session are in context — persistent memory surfacing across launches.
            // Signal once per session; GamificationBridge entitlement-gates the unlock.
            if (_restoredTurnCount > 0 && !_memoryRecallSignaled)
            {
                _memoryRecallSignaled = true;
                RaisePersistentMemoryRecalled();
            }
        }

        private async Task<(int status, string body)> SendChatAsync(string model, List<ChatMessage> messages)
        {
            // think:false tells reasoning models (qwen3, deepseek-r1, etc.) to skip the
            // long internal reasoning phase and respond directly. Non-reasoning models
            // ignore it. This can cut response time from ~50s to ~3s for qwen3-class models.
            var payload = new
            {
                model = model,
                messages = messages.Select(m => new { role = m.Role, content = m.Content ?? string.Empty }).ToArray(),
                stream = false,
                think = false
            };
            var json = JsonSerializer.Serialize(payload);

            using var req = new HttpRequestMessage(HttpMethod.Post, "api/chat")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            using var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
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

                var content = ExtractContent(body);
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

        public override void Dispose()
        {
            _aiSemaphore.Dispose();
            _http.Dispose();
        }

        private sealed class ChatMessage
        {
            public string Role { get; set; }
            public string? Content { get; set; }
            public ChatMessage(string role, string? content) { Role = role; Content = content; }
        }
    }
}
