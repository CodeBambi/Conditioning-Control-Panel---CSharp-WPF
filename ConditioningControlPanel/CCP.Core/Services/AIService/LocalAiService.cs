using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Core.Services.AIService;

/// <summary>
/// Cross-platform LOCAL (Ollama) <see cref="IAiService"/> for the Avalonia head. Ports the WPF
/// <c>Services/AIService/LocalAiService.cs</c> onto Core seams (HttpClient, IAppEnvironment for
/// the persisted chat-history file, IModerationGuard for the moderation sandwich,
/// ISystemPromptBuilder for the persona). Faithful to the WPF local contract (archaeology
/// 2026-07-05 §4):
/// <list type="bullet">
/// <item>Ollama <c>{host}api/chat</c>, model from settings, <c>stream=false</c>, <c>think=false</c>
///   (the perf flag that cuts reasoning-model latency ~50s→~3s). No <c>options</c>/temperature on the
///   chat path (<c>LocalAiService.cs:618-639</c>).</item>
/// <item><b>Persistent multi-turn history</b> (cloud has none): a <c>_messages</c> list with the
///   system prompt at index 0 (refreshed every call) then alternating user/assistant turns, persisted
///   to <c>UserDataPath/local_chat_history.json</c>, capped at 50 user+assistant pairs, gated by
///   <c>ChatMemoryEnabled</c> (<c>LocalAiService.cs:38,90-122</c>).</item>
/// <item>The full input/output moderation sandwich, identical to cloud: input scan before send,
///   output scan after sanitize, with the rollback rule — on an output block BOTH the user and
///   assistant turns are rolled back and nothing is persisted, so disk history stays last-known-clean
///   (<c>LocalAiService.cs:533-560</c>).</item>
/// <item>Queue/drop semantics: a second user click while busy returns a "still thinking" phrase;
///   automated ambient reactions are dropped entirely while a request is processing
///   (<c>LocalAiService.cs:412-422</c>).</item>
/// <item>All 5 ambient reactions are <b>stateless</b> (they send [system, userInput] and are never
///   appended or persisted) so the model does not fixate on old suggestions.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>v1 scope (documented follow-ups — see the task-board row):</b></para>
/// <list type="bullet">
/// <item><b>No AI-command execution</b> (<c>AllowAiToControlEffects</c>): the WPF enrichment
///   <c>[CONTEXT BLOCK]</c> + <c>AiResponseParser.Parse</c> + <c>IAICommandService</c> dispatch (3-cmd
///   cap) are NOT ported — <c>IAICommandService</c> is not registered in the Avalonia DI yet. The
///   <c>AiCommandService</c> port (with the 3-cmd cap) is a prerequisite follow-up.</item>
/// <item>The "still thinking" double-click phrase is an approximation (WPF's exact phrase not
///   extracted); the idle fallback phrases come from <c>IModService.GetPhrases("Idle")</c> as in WPF.</item>
/// </list>
/// </remarks>
public sealed class LocalAiService : IAiService, IDisposable
{
    private const int MaxPersistedPairs = 50;
    private const string HistoryFileName = "local_chat_history.json";
    private const string ThinkingPhrase = "*giggles* Hold on, I'm still thinking~";
    private static readonly string[] DefaultIdleFallback = { "Good girl~" };

    // Same SanitizeResponse contract as cloud (context-tag echoes stripped before output moderation).
    private static readonly Regex[] SanitizePatterns =
    {
        new(@"\[Category:[^\]]*\]", RegexOptions.Compiled),
        new(@"\[[A-Za-z]+/[A-Za-z]+\]", RegexOptions.Compiled),
        new(@"\[(?:Category|App|Title|Duration|Context):[^\]]*\]", RegexOptions.Compiled),
    };
    private static readonly Regex MultiSpace = new(@"\s{2,}", RegexOptions.Compiled);

    private readonly ISettingsService _settings;
    private readonly IAppEnvironment _environment;
    private readonly IModerationGuard _moderation;
    private readonly IModerationCounter? _counter;
    private readonly ISystemPromptBuilder _promptBuilder;
    private readonly IModService? _mods;
    private readonly ILogger<LocalAiService>? _logger;
    private readonly Random _fallbackRandom = new();
    private readonly string _historyPath;

    private readonly object _gate = new();
    private bool _isProcessing;

    // _messages[0] = system (refreshed every call); then alternating user/assistant turns.
    private readonly List<LocalChatMessage> _messages = new();

    private string? _activeHost;
    private string? _activeModel;
    private HttpClient? _http;

    public LocalAiService(
        ISettingsService settings,
        IAppEnvironment environment,
        IModerationGuard moderation,
        ISystemPromptBuilder promptBuilder,
        ILogger<LocalAiService>? logger = null,
        IModerationCounter? counter = null,
        IModService? mods = null)
    {
        _settings = settings;
        _environment = environment;
        _moderation = moderation;
        _promptBuilder = promptBuilder;
        _logger = logger;
        _counter = counter;
        _mods = mods;
        _historyPath = Path.Combine(environment.UserDataPath, HistoryFileName);
        LoadHistory();
    }

    // Local is always "available" — Ollama reachability is discovered at call time (WPF :46-47).
    public bool IsAvailable => true;
    public int DailyRequestsRemaining => -1; // local = unlimited

    private string ModelHint => $"local:{_activeModel ?? _settings.Current?.CompanionPrompt?.AiModel ?? "?"}";

    // ---------- IAiService: chat (stateful) ----------

    /// <inheritdoc/>
    public async Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
    {
        var r = await GetBambiReplyExAsync(userInput, isUserMessage).ConfigureAwait(false);
        if (r.Refusal != null)
            return r.Refusal.Source == ModerationSource.Input
                ? ModerationRefusal.InputSentinel
                : ModerationRefusal.OutputSentinel;
        return r.Text;
    }

    /// <inheritdoc/>
    public async Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = true)
    {
        if (_settings.Current?.OfflineMode == true)
            return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

        // Queue/drop: a second user click while busy → "still thinking" (WPF :412-416).
        lock (_gate)
        {
            if (_isProcessing) return new AiReplyResult(ThinkingPhrase, IsAiGenerated: false, Refusal: null);
            _isProcessing = true;
        }

        LocalChatMessage? userMsg = null;
        try
        {
            var systemPrompt = _promptBuilder.GetSystemPrompt();
            EnsureSystemMessage(systemPrompt);

            // --- INPUT moderation (LocalAiService.cs:390-410) ---
            var inputCheck = _moderation.CheckInput(userInput);
            if (!inputCheck.Allow && inputCheck.Category.HasValue)
            {
                LogModeration(inputCheck.Category.Value, source: "input");
                _counter?.RecordHit(inputCheck.Category.Value, "input:local");
                return new AiReplyResult(string.Empty, IsAiGenerated: false,
                    Refusal: new ModerationRefusalInfo(Category: null, Source: ModerationSource.Input));
            }
            if (inputCheck.Allow && inputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                LogModeration(ProhibitedCategory.ProfessionalAdvice, source: "input");

            // Append the user turn, send, then rollback on any failure so history stays clean.
            userMsg = new LocalChatMessage("user", userInput);
            _messages.Add(userMsg);

            var raw = await SendChatAsync(_messages).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
            {
                _messages.Remove(userMsg); // rollback failed user turn (WPF :491-498)
                return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);
            }

            var sanitized = SanitizeResponse(raw);
            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = GetFallbackResponse();

            // --- OUTPUT moderation (LocalAiService.cs:533-560) ---
            var outputCheck = _moderation.CheckOutput(sanitized);
            if (!outputCheck.Allow && outputCheck.Category.HasValue)
            {
                LogModeration(outputCheck.Category.Value, source: "output");
                // Roll back BOTH turns and skip persistence (WPF :548-558): history stays last-known-clean.
                _messages.Remove(userMsg);
                return new AiReplyResult(string.Empty, IsAiGenerated: false,
                    Refusal: new ModerationRefusalInfo(Category: null, Source: ModerationSource.Output));
            }
            if (outputCheck.Allow && outputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                LogModeration(ProhibitedCategory.ProfessionalAdvice, source: "output");

            _messages.Add(new LocalChatMessage("assistant", sanitized));

            if (_settings.Current?.CompanionPrompt?.ChatMemoryEnabled != false)
                _ = PersistHistoryAsync(); // fire-and-forget; trimmed to MaxPersistedPairs

            return new AiReplyResult(sanitized, IsAiGenerated: true, Refusal: null);
        }
        catch (Exception ex)
        {
            // Any unexpected throw (e.g. a malformed Ollama host slipping past EnsureHost): roll back
            // the pending user turn so history stays clean, and return a canned fallback. (WPF wraps
            // the whole body in catch + returns DescribeChatException — no diagnostics ported in v1.)
            _logger?.LogWarning(ex, "LocalAiService: chat path threw; rolling back any pending user turn.");
            if (userMsg != null) _messages.Remove(userMsg);
            return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);
        }
        finally
        {
            lock (_gate) _isProcessing = false;
        }
    }

    // ---------- IAiService: ambient reactions (stateless, dropped while busy) ----------

    public async Task<string?> GetAwarenessReactionAsync(string detectedName, string category,
        string serviceName = "", string pageTitle = "")
    {
        var website = string.IsNullOrWhiteSpace(serviceName) ? detectedName : serviceName;
        var tabName = string.IsNullOrWhiteSpace(pageTitle) ? detectedName : pageTitle;
        return await StatelessReactionAsync(
            $"[Category: {category} | App: {website} | Title: {tabName} | Duration: 0m]").ConfigureAwait(false);
    }

    public async Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
        => await StatelessReactionAsync(
            $"[Category: {category} | App: {displayName} | Title: {displayName} | Duration: {FormatDuration(duration)}]").ConfigureAwait(false);

    public async Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
        => await StatelessReactionAsync(string.IsNullOrWhiteSpace(promptTemplate)
            ? $"You just caught the user on the word '{keyword}'. React in character, one short line."
            : promptTemplate!.Replace("{keyword}", keyword)).ConfigureAwait(false);

    public async Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
        => await StatelessReactionAsync(string.IsNullOrWhiteSpace(promptTemplate)
            ? $"The user made {mistakes} mistakes in '{sentance}' for the lock screen. They had to type it {amount} of time. React in character, one short line."
            : promptTemplate!.Replace("{sentance}", sentance).Replace("{mistakes}", mistakes.ToString()).Replace("{amount}", amount.ToString())).ConfigureAwait(false);

    public async Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
        => await StatelessReactionAsync(string.IsNullOrWhiteSpace(promptTemplate)
            ? $"The user has just finished the mandatory video {title}. React in character, one short line."
            : promptTemplate!.Replace("{title}", title)).ConfigureAwait(false);

    /// <summary>
    /// Stateless reaction path: [system, userInput] only — never appended/persisted (stops the model
    /// fixating on old suggestions). Dropped (returns null) if a request is already processing.
    /// returnRefusalSentinel is always false → a moderation hit returns null (logged), never a bubble.
    /// </summary>
    private async Task<string?> StatelessReactionAsync(string userInput)
    {
        lock (_gate) { if (_isProcessing) return null; _isProcessing = true; }
        try
        {
            var systemPrompt = _promptBuilder.GetSystemPrompt();

            var inputCheck = _moderation.CheckInput(userInput);
            if (!inputCheck.Allow && inputCheck.Category.HasValue)
            {
                LogModeration(inputCheck.Category.Value, source: "input");
                return null;
            }
            if (inputCheck.Allow && inputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                LogModeration(ProhibitedCategory.ProfessionalAdvice, source: "input");

            var messages = new[]
            {
                new LocalChatMessage("system", systemPrompt),
                new LocalChatMessage("user", userInput)
            };
            var raw = await SendChatAsync(messages).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var sanitized = SanitizeResponse(raw);
            if (string.IsNullOrWhiteSpace(sanitized)) return null;

            var outputCheck = _moderation.CheckOutput(sanitized);
            if (!outputCheck.Allow && outputCheck.Category.HasValue)
            {
                LogModeration(outputCheck.Category.Value, source: "output");
                return null;
            }
            if (outputCheck.Allow && outputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                LogModeration(ProhibitedCategory.ProfessionalAdvice, source: "output");
            return sanitized;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LocalAiService: reaction path threw.");
            return null;
        }
        finally
        {
            lock (_gate) _isProcessing = false;
        }
    }

    // ---------- IAiService: stateless raw completion (quiz) ----------

    /// <inheritdoc/>
    /// <remarks>Stateless, persona-less Ollama; only path that sends options.temperature
    /// (LocalAiService.cs:660-701). Shared with CoreAiService's raw completion — both serve the quiz.</remarks>
    public async Task<string?> GetRawChatCompletionAsync(IEnumerable<(string role, string content)> messages, double temperature = 0.8)
    {
        var cp = _settings.Current?.CompanionPrompt;
        if (cp == null) return null;
        var host = NormalizeHost(cp.AiOllamaHost);
        var model = cp.AiModel;
        if (string.IsNullOrWhiteSpace(model)) { _logger?.LogDebug("LocalAiService: no local model configured."); return null; }

        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(host), Timeout = TimeSpan.FromMinutes(5) };
            var payload = new
            {
                model,
                messages = messages.Select(m => new { role = m.role, content = m.content ?? string.Empty }).ToArray(),
                stream = false,
                think = false,
                options = new { temperature }
            };
            using var resp = await http.PostAsJsonAsync("api/chat", payload).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger?.LogWarning("LocalAiService: Ollama HTTP {Status}: {Body}", (int)resp.StatusCode, Truncate(body, 200));
                return null;
            }
            return ExtractContent(body);
        }
        catch (TaskCanceledException) { _logger?.LogWarning("LocalAiService: raw completion timed out."); return null; }
        catch (Exception ex) { _logger?.LogWarning(ex, "LocalAiService: raw chat completion failed."); return null; }
    }

    // ---------- Ollama chat transport (stateful: full _messages thread) ----------

    /// <summary>Sends the full message thread to Ollama and returns the assistant content (WPF SendChatAsync).</summary>
    private async Task<string?> SendChatAsync(IReadOnlyList<LocalChatMessage> messages)
    {
        var cp = _settings.Current?.CompanionPrompt;
        if (cp == null) return null;
        EnsureHost(cp);
        if (_http == null) return null; // invalid host (EnsureHost logged + kept previous, which may be null)

        var payload = new
        {
            model = _activeModel,
            messages = messages.Select(m => new { role = m.Role, content = m.Content ?? string.Empty }).ToArray(),
            stream = false,
            think = false
        };
        try
        {
            using var resp = await _http!.PostAsJsonAsync("api/chat", payload).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger?.LogWarning("LocalAiService: Ollama HTTP {Status} ({Model}): {Body}",
                    (int)resp.StatusCode, _activeModel, Truncate(body, 200));
                return null;
            }
            return ExtractContent(body);
        }
        catch (TaskCanceledException) { _logger?.LogWarning("LocalAiService: Ollama request timed out ({Model}).", _activeModel); return null; }
        catch (HttpRequestException ex) { _logger?.LogWarning(ex, "LocalAiService: cannot reach Ollama at {Host}.", _activeHost); return null; }
        catch (Exception ex) { _logger?.LogWarning(ex, "LocalAiService: Ollama chat failed."); return null; }
    }

    /// <summary>(Re)creates the HttpClient when the host changes (WPF EnsureHost :256-268). Validates
    /// the URI and constructs the new client BEFORE disposing the old one, so a malformed host never
    /// leaves <c>_http</c> disposed-or-null mid-call (which would throw on the next request).</summary>
    private void EnsureHost(CompanionPromptSettings cp)
    {
        var host = NormalizeHost(cp.AiOllamaHost);
        var model = string.IsNullOrWhiteSpace(cp.AiModel) ? "qwen3.5:latest" : cp.AiModel;
        if (_http != null && _activeHost == host && _activeModel == model) return;

        if (!Uri.TryCreate(host, UriKind.Absolute, out var uri))
        {
            _logger?.LogWarning("LocalAiService: invalid Ollama host '{Host}' — keeping previous client.", host);
            return;
        }
        var next = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromMinutes(5) };
        _http?.Dispose();
        _http = next;
        _activeHost = host;
        _activeModel = model;
    }

    private void EnsureSystemMessage(string systemPrompt)
    {
        if (_messages.Count > 0 && _messages[0].Role == "system")
            _messages[0] = new LocalChatMessage("system", systemPrompt); // refresh (persona may have changed)
        else
            _messages.Insert(0, new LocalChatMessage("system", systemPrompt));
    }

    // ---------- History persistence (local_chat_history.json) ----------

    private void LoadHistory()
    {
        try
        {
            // WPF returns early when memory is disabled, so a stale history file is not re-seeded.
            if (_settings.Current?.CompanionPrompt?.ChatMemoryEnabled == false) return;
            if (!File.Exists(_historyPath)) return;
            var json = File.ReadAllText(_historyPath);
            var turns = JsonSerializer.Deserialize<List<LocalChatMessage>>(json);
            if (turns == null) return;
            // Restored turns only (system/enrichment are rebuilt at runtime).
            _messages.AddRange(turns.Where(t => t.Role is "user" or "assistant"));
            _logger?.LogDebug("LocalAiService: restored {Count} history turns.", _messages.Count);
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "LocalAiService: failed to load chat history; starting fresh."); _messages.Clear(); }
    }

    private async Task PersistHistoryAsync()
    {
        try
        {
            // Persist only user+assistant turns, trimmed to the most recent MaxPersistedPairs pairs.
            var turns = _messages.Where(m => m.Role is "user" or "assistant").ToList();
            var pairCount = turns.Count / 2;
            if (pairCount > MaxPersistedPairs)
                turns = turns.Skip((pairCount - MaxPersistedPairs) * 2).ToList();

            var json = JsonSerializer.Serialize(turns);
            await File.WriteAllTextAsync(_historyPath, json).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "LocalAiService: failed to persist chat history."); }
    }

    // ---------- Helpers ----------

    private void LogModeration(ProhibitedCategory category, string source)
        => _logger?.LogWarning("Moderation hit | category={Category} | source={Source} | model={Model}",
            category, source, ModelHint);

    private static string SanitizeResponse(string text)
    {
        foreach (var rx in SanitizePatterns)
            text = rx.Replace(text, string.Empty);
        text = MultiSpace.Replace(text, " ");
        return text.Trim();
    }

    private string GetFallbackResponse()
    {
        var phrases = _mods?.GetPhrases("Idle") ?? DefaultIdleFallback;
        return phrases[_fallbackRandom.Next(phrases.Length)];
    }

    private static string FormatDuration(TimeSpan d)
    {
        if (d.TotalMinutes < 1) return $"{(int)d.TotalSeconds}s";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m";
        return $"{(int)d.TotalHours}h";
    }

    private static string NormalizeHost(string host) =>
        string.IsNullOrWhiteSpace(host) ? "http://localhost:11434/" :
        host.EndsWith("/", StringComparison.Ordinal) ? host : host + "/";

    private static string? ExtractContent(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c))
                return c.GetString();
        }
        catch { /* malformed JSON — treat as empty */ }
        return null;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "...";

    public void Dispose() => _http?.Dispose();

    /// <summary>A persisted chat-history turn (role + content).</summary>
    private sealed class LocalChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
        public LocalChatMessage(string role, string content) { Role = role; Content = content; }
    }
}
