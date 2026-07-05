using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Commands;
using ConditioningControlPanel.Core.Services.AIService.Enrichment;
using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.AiEnrichment;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Core.Services.AIService;

/// <summary>
/// Cross-platform OpenAI-compatible <see cref="IAiService"/> for the Avalonia head. Ports the WPF
/// <c>Services/AIService/OpenAiCompatibleService.cs</c> onto Core seams, with two deliberate
/// IMPROVEMENTS over the WPF reference (both permitted by the goal's "freedom to improve internals"
/// doctrine, behavior-preserving or -improving):
/// <list type="bullet">
/// <item><b>Key storage via <see cref="ISecretStore"/></b> instead of the WPF Windows-only DPAPI
///   string in settings (<c>SecureStringHelper.Unprotect</c>). Same DPAPI protection on Windows
///   (<c>DesktopSecretStore</c>) plus a Linux path (<c>libsecret</c>/encrypted-file) = strict
///   improvement. The legacy <c>OpenAiCompatibleApiKey</c> settings field is NOT read here (it held
///   a Windows-only DPAPI blob the Core head cannot decrypt); the key lives in the secret store
///   under <see cref="SecretKey"/>.</item>
/// <item><b>The full input/output moderation sandwich</b> the WPF provider omits entirely (verified
///   absence — <c>grep Moderation OpenAiCompatibleService.cs</c> = 0 matches). Mirrors
///   <see cref="CoreAiService"/>: input scan before send, sanitize-then-output-scan, escalation only
///   on user-typed chat input. This closes a real compliance gap.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>Faithful to the WPF contract</b> (archaeology 2026-07-05):</para>
/// <list type="bullet">
/// <item>Stateless (no history, like cloud). In-memory client-side daily counter reset at local
///   midnight; <c>DailyRequestLimit &lt;= 0</c> = unlimited (reported as <c>-1</c>); counter bumped
///   BEFORE send (<c>OpenAiCompatibleService.cs:64-99,399</c>).</item>
/// <item>Endpoint normalization is EXACT (<c>:117-152</c>): strip a trailing <c>/chat/completions</c>
///   (case-insensitive), force exactly one trailing slash, clear query/fragment, fallback
///   <c>https://api.openai.com/v1/</c>. The base MUST end in <c>/</c> for the relative
///   <c>chat/completions</c> append — reproduce exactly or 404.</item>
/// <item>Chat payload is <c>{model, messages}</c> + optional sampler keys ONLY — <b>NO max_tokens, NO
///   default temperature</b> (<c>:393-397</c>). Default model <c>gpt-4o-mini</c>.</item>
/// <item>Sampler (<c>:188-199</c>): 7 keys gated by <c>OpenAiCompatibleUseCustomSamplerSettings</c>,
///   each omitted when null (<c>top_k</c> is int, rest double).</item>
/// <item>Retry (<c>:401-472</c>): one retry on 429 / &gt;=500 / HttpRequestException /
///   TaskCanceledException, fixed 1200ms delay, attempt-0 only.</item>
/// <item>Response (<c>:437-455</c>): <c>choices[0].message.content</c> → <c>CleanTokenizerArtifacts</c>
///   (<c>Ġ</c>→space) → <see cref="ProcessResponse"/>.</item>
/// <item>All 5 reactions are stateless [system, user], share the chat transport, and do NOT
///   short-circuit on <c>!IsAvailable</c> (the transport's internal gate is the only gate).</item>
/// </list>
/// <para><b>v1 scope (documented follow-ups — see the task-board row):</b> AI-command execution
/// (<c>AllowAiToControlEffects</c>) is injected as <see cref="IAiCommandService"/>? and dispatches
/// only when registered; the enrichment <c>[CONTEXT BLOCK]</c> is deferred with it (the two are
/// paired — enrichment tells the model to emit commands, the executor runs them). A key-entry UI
/// (settings dialog writing <see cref="ISecretStore"/>) is the other follow-up that makes the
/// provider user-reachable.</para>
/// </remarks>
public sealed class OpenAiService : IAiService, IDisposable
{
    internal const string SecretKey = "openai-api-key";
    private const string DefaultModel = "gpt-4o-mini";
    private const string DefaultEndpoint = "https://api.openai.com/v1/";
    private const int RetryDelayMs = 1200;
    private static readonly string[] DefaultIdleFallback = { "Good girl~" };

    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly IModerationGuard _moderation;
    private readonly IModerationCounter? _counter;
    private readonly ISystemPromptBuilder _promptBuilder;
    private readonly IAiResponseParser _parser;
    private readonly IAiCommandService? _commands;
    private readonly IPromptService? _promptService;
    private readonly IModService? _mods;
    private readonly ILogger<OpenAiService>? _logger;
    private readonly Random _fallbackRandom = new();

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private int _dailyRequestCount;
    private DateTime _lastResetDate = DateTime.Today;

    public OpenAiService(
        ISettingsService settings,
        ISecretStore secrets,
        IModerationGuard moderation,
        ISystemPromptBuilder promptBuilder,
        IAiResponseParser parser,
        ILogger<OpenAiService>? logger = null,
        IModerationCounter? counter = null,
        IModService? mods = null,
        IAiCommandService? commands = null,
        IPromptService? promptService = null)
    {
        _settings = settings;
        _secrets = secrets;
        _moderation = moderation;
        _promptBuilder = promptBuilder;
        _parser = parser;
        _logger = logger;
        _counter = counter;
        _mods = mods;
        _commands = commands;
        _promptService = promptService;
    }

    /// <summary>Gate: offline / no endpoint / no key / over the client daily limit (0 = unlimited).</summary>
    public bool IsAvailable
    {
        get
        {
            if (_settings.Current?.OfflineMode == true) return false;
            var s = _settings.Current?.CompanionPrompt;
            if (s == null) return false;
            if (string.IsNullOrWhiteSpace(s.OpenAiCompatibleEndpoint)) return false;
            if (!HasApiKey) return false;
            ResetDailyCounterIfNeeded();
            return s.DailyRequestLimit <= 0 || _dailyRequestCount < s.DailyRequestLimit;
        }
    }

    public int DailyRequestsRemaining
    {
        get
        {
            var s = _settings.Current?.CompanionPrompt;
            if (s == null) return 0;
            ResetDailyCounterIfNeeded();
            if (s.DailyRequestLimit <= 0) return -1; // unlimited
            var remaining = s.DailyRequestLimit - _dailyRequestCount;
            return remaining < 0 ? 0 : remaining;
        }
    }

    private bool HasApiKey => !string.IsNullOrWhiteSpace(GetApiKey());
    private string ModelHint => "openai";

    /// <summary>Reads the API key from the secret store (UTF-8). Empty if unset.</summary>
    private string GetApiKey()
    {
        var bytes = _secrets.Retrieve(SecretKey);
        return bytes is null || bytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes);
    }

    // ---------- IAiService: chat (stateful only via daily counter; no turn history) ----------

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
    public async Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
    {
        if (_settings.Current?.OfflineMode == true)
            return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

        // --- INPUT moderation (BEFORE IsAvailable/rate-limit so a blocked hit is logged + consumes no slot) ---
        var inputCheck = _moderation.CheckInput(userInput);
        if (!inputCheck.Allow && inputCheck.Category.HasValue)
        {
            LogModeration(inputCheck.Category.Value, source: "input");
            _counter?.RecordHit(inputCheck.Category.Value, "input:openai");
            return new AiReplyResult(string.Empty, IsAiGenerated: false,
                Refusal: new ModerationRefusalInfo(Category: null, Source: ModerationSource.Input));
        }
        if (inputCheck.Allow && inputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
            LogModeration(ProhibitedCategory.ProfessionalAdvice, source: "input");

        // --- Availability + client daily limit (0 = unlimited). Bump BEFORE send (WPF :399). ---
        if (!IsAvailable) return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);
        ResetDailyCounterIfNeeded();
        _dailyRequestCount++;

        var messages = BuildMessages(userInput);
        var content = await SendChatAsync(messages).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
            return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

        var reply = SanitizeResponse(ProcessResponse(content));
        if (string.IsNullOrWhiteSpace(reply))
            return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

        // --- OUTPUT moderation (after sanitize; escalation NEVER fires for output) ---
        var outputCheck = _moderation.CheckOutput(reply);
        if (!outputCheck.Allow && outputCheck.Category.HasValue)
        {
            LogModeration(outputCheck.Category.Value, source: "output");
            return new AiReplyResult(string.Empty, IsAiGenerated: false,
                Refusal: new ModerationRefusalInfo(Category: null, Source: ModerationSource.Output));
        }
        if (outputCheck.Allow && outputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
            LogModeration(ProhibitedCategory.ProfessionalAdvice, source: "output");

        return new AiReplyResult(reply, IsAiGenerated: true, Refusal: null);
    }

    // ---------- IAiService: ambient reactions (stateless [system, user]; no short-circuit) ----------

    public async Task<string?> GetAwarenessReactionAsync(string detectedName, string category,
        string serviceName = "", string pageTitle = "")
    {
        var website = string.IsNullOrWhiteSpace(serviceName) ? detectedName : serviceName;
        var tabName = string.IsNullOrWhiteSpace(pageTitle) ? detectedName : pageTitle;
        return await ReactionAsync(
            $"[Category: {category} | App: {website} | Title: {tabName} | Duration: 0m]").ConfigureAwait(false);
    }

    public async Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
        => await ReactionAsync(
            $"[Category: {category} | App: {displayName} | Title: {displayName} | Duration: {FormatDuration(duration)}]").ConfigureAwait(false);

    public async Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
        => await ReactionAsync(string.IsNullOrWhiteSpace(promptTemplate)
            ? $"You just caught the user on the word '{keyword}'. React in character, one short line."
            : promptTemplate!.Replace("{keyword}", keyword)).ConfigureAwait(false);

    public async Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
        => await ReactionAsync(string.IsNullOrWhiteSpace(promptTemplate)
            ? $"The user made {mistakes} mistakes in '{sentance}' for the lock screen. They had to type it {amount} of time. React in character, one short line."
            : promptTemplate!.Replace("{sentance}", sentance).Replace("{mistakes}", mistakes.ToString()).Replace("{amount}", amount.ToString())).ConfigureAwait(false);

    public async Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
        => await ReactionAsync(string.IsNullOrWhiteSpace(promptTemplate)
            ? $"The user has just finished the mandatory video {title}. React in character, one short line."
            : promptTemplate!.Replace("{title}", title)).ConfigureAwait(false);

    /// <summary>
    /// Stateless reaction path: [system, userInput] only, full moderation sandwich with
    /// returnRefusalSentinel=false (a hit returns null, logged — never a bubble). Does NOT
    /// short-circuit on IsAvailable (the transport's internal gate is the only gate — WPF parity).
    /// </summary>
    private async Task<string?> ReactionAsync(string userInput)
    {
        var inputCheck = _moderation.CheckInput(userInput);
        if (!inputCheck.Allow && inputCheck.Category.HasValue)
        {
            LogModeration(inputCheck.Category.Value, source: "input");
            return null;
        }
        if (inputCheck.Allow && inputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
            LogModeration(ProhibitedCategory.ProfessionalAdvice, source: "input");

        if (!IsAvailable) return null;
        ResetDailyCounterIfNeeded();
        _dailyRequestCount++;

        var messages = BuildMessages(userInput);
        var content = await SendChatAsync(messages).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content)) return null;

        var reply = SanitizeResponse(ProcessResponse(content));
        if (string.IsNullOrWhiteSpace(reply)) return null;

        var outputCheck = _moderation.CheckOutput(reply);
        if (!outputCheck.Allow && outputCheck.Category.HasValue)
        {
            LogModeration(outputCheck.Category.Value, source: "output");
            return null;
        }
        return reply;
    }

    // ---------- IAiService: raw completion (quiz) ----------

    /// <inheritdoc/>
    /// <remarks>Stateless persona-less completion reusing the OpenAI transport (model + messages +
    /// temperature). The quiz contract; mirrors CoreAiService/LocalAiService raw completion.</remarks>
    public async Task<string?> GetRawChatCompletionAsync(IEnumerable<(string role, string content)> messages, double temperature = 0.8)
    {
        var s = _settings.Current?.CompanionPrompt;
        if (s == null) return null;
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(s.OpenAiCompatibleEndpoint)) return null;

        var payload = new Dictionary<string, object>
        {
            ["model"] = GetConfiguredModel(s),
            ["messages"] = messages.Select(m => new MessageDto(m.role, m.content)).ToList(),
            ["temperature"] = temperature
        };
        try
        {
            using var req = BuildRequest(s, payload, apiKey);
            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger?.LogWarning("OpenAiService: raw completion HTTP {Status}: {Body}", (int)resp.StatusCode, Truncate(body, 200));
                return null;
            }
            return ExtractContent(body);
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "OpenAiService: raw completion failed."); return null; }
    }

    // ---------- OpenAI transport ----------

    /// <summary>Builds [system, (enrichment?), user]. The <c>[CONTEXT BLOCK]</c> enrichment is emitted
    /// only when <c>AllowAiToControlEffects</c> AND a dispatcher is registered — it tells the model
    /// the command-output schema and is paired with dispatch in <see cref="ProcessResponse"/>.
    /// System prompt via the builder.</summary>
    private List<MessageDto> BuildMessages(string userInput)
    {
        var list = new List<MessageDto>
        {
            new("system", _promptBuilder.GetSystemPrompt()),
            new("user", userInput)
        };
        var cp = _settings.Current?.CompanionPrompt;
        if (cp != null && cp.AllowAiToControlEffects && _commands != null && _promptService != null)
        {
            // WPF OpenAiCompatibleService:485-496 inserts the enrichment at index 1 (after system,
            // before the real user turn). factsJson is "" (no KnowledgeService in Core yet — filed gap).
            list.Insert(1, _promptService.BuildEnrichmentMessage(factsJson: "", timeStamp: DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
        }
        return list;
    }

    /// <summary>POST {base}chat/completions with one retry on 429/&gt;=500/transport errors (WPF :401-472).</summary>
    private async Task<string?> SendChatAsync(List<MessageDto> messages)
    {
        var s = _settings.Current?.CompanionPrompt;
        if (s == null) return null;
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var payload = new Dictionary<string, object>
        {
            ["model"] = GetConfiguredModel(s),
            ["messages"] = messages
        };
        ApplySamplerSettings(payload, s); // {model, messages} + optional sampler keys ONLY

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var req = BuildRequest(s, payload, apiKey);
                using var resp = await _http.SendAsync(req).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    var status = (int)resp.StatusCode;
                    var retryable = status == 429 || status >= 500;
                    if (attempt == 0 && retryable) { await Task.Delay(RetryDelayMs).ConfigureAwait(false); continue; }
                    _logger?.LogWarning("OpenAiService: HTTP {Status}: {Body}", status, Truncate(body, 200));
                    return null;
                }
                var content = ExtractContent(body);
                return content is null ? null : CleanTokenizerArtifacts(content);
            }
            catch (HttpRequestException ex) when (attempt == 0)
            {
                _logger?.LogWarning(ex, "OpenAiService: transport error (retrying).");
                await Task.Delay(RetryDelayMs).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (attempt == 0)
            {
                _logger?.LogWarning("OpenAiService: request timed out (retrying).");
                await Task.Delay(RetryDelayMs).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "OpenAiService: chat failed.");
                return null;
            }
        }
        _logger?.LogWarning("OpenAiService: chat failed after retry.");
        return null;
    }

    /// <summary>If effects are enabled: parse commands + dispatch (when an executor is registered),
    /// return CleanText. Otherwise return the raw content (WPF :494-520).</summary>
    private string? ProcessResponse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var effectsEnabled = _settings.Current?.CompanionPrompt?.AllowAiToControlEffects == true;
        if (!effectsEnabled) return content;

        var parsed = _parser.Parse(content!);
        var commands = parsed.Commands;
        if (commands.Count > 0)
        {
            _logger?.LogInformation("OpenAiService: parsed {Count} command(s).", commands.Count);
            if (_commands != null)
            {
                _commands.BeginBatch(); // resets the 3-cmd-per-response cap (enforced in ExecuteCommand)
                foreach (var cmd in commands) _commands.ExecuteCommand(cmd);
            }
            else
            {
                _logger?.LogDebug("OpenAiService: {Count} command(s) parsed but no IAiCommandService registered (dispatch skipped).", commands.Count);
            }
        }
        return string.IsNullOrWhiteSpace(parsed.CleanText) ? null : parsed.CleanText;
    }

    // ---------- "Test connection" (for a settings dialog; not on IAiService) ----------

    /// <summary>Minimal real chat completion ({model, max_tokens:1, temperature:0, [{user,"ping"}]})
    /// to validate endpoint/key/model. No retries. (WPF TestEndpointAsync :210-351.)</summary>
    public async Task<ConnectionDiagnosticResult> TestEndpointAsync(CancellationToken ct = default)
    {
        var s = _settings.Current?.CompanionPrompt;
        if (s == null || string.IsNullOrWhiteSpace(s.OpenAiCompatibleEndpoint))
            return new ConnectionDiagnosticResult(false, DiagnosticCategory.MissingConfiguration, "Endpoint is missing");
        if (!Uri.TryCreate(s.OpenAiCompatibleEndpoint, UriKind.Absolute, out _))
            return new ConnectionDiagnosticResult(false, DiagnosticCategory.Endpoint, "Endpoint URL is invalid");
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return new ConnectionDiagnosticResult(false, DiagnosticCategory.Authentication, "API key is missing");

        var payload = new Dictionary<string, object>
        {
            ["model"] = GetConfiguredModel(s),
            ["max_tokens"] = 1,
            ["temperature"] = 0,
            ["messages"] = new[] { new MessageDto("user", "ping") }
        };
        ApplySamplerSettings(payload, s);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var req = BuildRequest(s, payload, apiKey);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            sw.Stop();
            var status = (int)resp.StatusCode;
            if (resp.IsSuccessStatusCode)
                return new ConnectionDiagnosticResult(true, DiagnosticCategory.Success, "Connected", status, sw.ElapsedMilliseconds);

            var body = (await resp.Content.ReadAsStringAsync().ConfigureAwait(false)).ToLowerInvariant();
            return status switch
            {
                401 or 403 => new ConnectionDiagnosticResult(false, DiagnosticCategory.Authentication, "Authentication failed (invalid API key or unauthorized endpoint)", status, sw.ElapsedMilliseconds),
                404 => new ConnectionDiagnosticResult(false, DiagnosticCategory.Endpoint, "Endpoint not found (check base URL path, e.g. /api/v1)", status, sw.ElapsedMilliseconds),
                400 when body.Contains("model") || body.Contains("unknown_model") || body.Contains("not found")
                    => new ConnectionDiagnosticResult(false, DiagnosticCategory.Model, "Model is invalid or unavailable on this endpoint", status, sw.ElapsedMilliseconds),
                _ => new ConnectionDiagnosticResult(false, DiagnosticCategory.Http, $"HTTP {status}: {resp.ReasonPhrase}", status, sw.ElapsedMilliseconds)
            };
        }
        catch (TaskCanceledException) { return new ConnectionDiagnosticResult(false, DiagnosticCategory.Timeout, "Request timed out"); }
        catch (HttpRequestException ex) { return new ConnectionDiagnosticResult(false, DiagnosticCategory.Connection, $"Connection failed: {ex.Message}"); }
        catch (Exception ex) { return new ConnectionDiagnosticResult(false, DiagnosticCategory.Unknown, $"Unexpected error: {ex.GetType().Name}"); }
    }

    // ---------- Helpers (endpoint/model/sampler/request/extract/clean — faithful to WPF) ----------

    internal static Uri GetConfiguredEndpointBaseUri(string? rawEndpoint)
    {
        var raw = rawEndpoint?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return new Uri(DefaultEndpoint);
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed)) return new Uri(DefaultEndpoint);

        var path = parsed.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            path = path.Substring(0, path.Length - "/chat/completions".Length);
        if (string.IsNullOrEmpty(path)) path = "/";
        if (!path.EndsWith("/", StringComparison.Ordinal)) path += "/";
        return new UriBuilder(parsed) { Path = path, Query = string.Empty, Fragment = string.Empty }.Uri;
    }

    private static string GetConfiguredModel(CompanionPromptSettings s)
        => string.IsNullOrWhiteSpace(s.OpenAiCompatibleModel) ? DefaultModel : s.OpenAiCompatibleModel;

    private static void ApplySamplerSettings(Dictionary<string, object> payload, CompanionPromptSettings s)
    {
        if (!s.OpenAiCompatibleUseCustomSamplerSettings) return;
        if (s.OpenAiCompatibleTemperature.HasValue) payload["temperature"] = s.OpenAiCompatibleTemperature.Value;
        if (s.OpenAiCompatibleTopP.HasValue) payload["top_p"] = s.OpenAiCompatibleTopP.Value;
        if (s.OpenAiCompatibleTopK.HasValue) payload["top_k"] = s.OpenAiCompatibleTopK.Value;
        if (s.OpenAiCompatibleFrequencyPenalty.HasValue) payload["frequency_penalty"] = s.OpenAiCompatibleFrequencyPenalty.Value;
        if (s.OpenAiCompatiblePresencePenalty.HasValue) payload["presence_penalty"] = s.OpenAiCompatiblePresencePenalty.Value;
        if (s.OpenAiCompatibleRepetitionPenalty.HasValue) payload["repetition_penalty"] = s.OpenAiCompatibleRepetitionPenalty.Value;
        if (s.OpenAiCompatibleMinP.HasValue) payload["min_p"] = s.OpenAiCompatibleMinP.Value;
    }

    private HttpRequestMessage BuildRequest(CompanionPromptSettings s, Dictionary<string, object> payload, string apiKey)
    {
        var endpoint = new Uri(GetConfiguredEndpointBaseUri(s.OpenAiCompatibleEndpoint), "chat/completions");
        var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return req;
    }

    private static string? ExtractContent(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return null;
            var first = choices[0];
            if (!first.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var c)) return null;
            return c.GetString();
        }
        catch { return null; }
    }

    private static string CleanTokenizerArtifacts(string? text) => text is null ? string.Empty : text.Replace("Ġ", " ");

    // Same SanitizeResponse contract as the siblings (CoreAiService/LocalAiService): strip
    // context-tag echoes BEFORE output moderation so a reaction echoing [Category:…] / [App/Title]
    // does not false-positive the guard. Idempotent on already-sanitized (effects-ON) text.
    private static readonly Regex[] SanitizePatterns =
    {
        new(@"\[Category:[^\]]*\]", RegexOptions.Compiled),
        new(@"\[[A-Za-z]+/[A-Za-z]+\]", RegexOptions.Compiled),
        new(@"\[(?:Category|App|Title|Duration|Context):[^\]]*\]", RegexOptions.Compiled),
    };
    private static readonly Regex MultiSpace = new(@"\s{2,}", RegexOptions.Compiled);
    private static string SanitizeResponse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        foreach (var rx in SanitizePatterns) text = rx.Replace(text, string.Empty);
        text = MultiSpace.Replace(text, " ");
        return text.Trim();
    }

    private void ResetDailyCounterIfNeeded()
    {
        if (_lastResetDate != DateTime.Today) { _dailyRequestCount = 0; _lastResetDate = DateTime.Today; }
    }

    private void LogModeration(ProhibitedCategory category, string source)
        => _logger?.LogWarning("Moderation hit | category={Category} | source={Source} | model={Model}", category, source, ModelHint);

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

    private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "...";

    public void Dispose() => _http.Dispose();
}

/// <summary>Result of <see cref="OpenAiService.TestEndpointAsync"/> (WPF ConnectionDiagnosticResult).</summary>
public sealed record ConnectionDiagnosticResult(
    bool Success, DiagnosticCategory Category, string Message,
    int? HttpStatusCode = null, long? ElapsedMs = null);

/// <summary>Diagnostic categories for the connection test (WPF DiagnosticCategory).</summary>
public enum DiagnosticCategory
{
    Success, MissingConfiguration, Endpoint, Authentication, Model, Timeout, Connection, Http, Unknown
}
