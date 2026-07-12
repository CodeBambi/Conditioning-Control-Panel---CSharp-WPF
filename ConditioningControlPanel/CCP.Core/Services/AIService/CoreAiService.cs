using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Core.Services.AIService;

/// <summary>
/// Cross-platform <see cref="IAiService"/> for the Avalonia head. Ports the WPF cloud companion
/// AI (<c>Services/AiService.cs</c>, archaeology 2026-07-05) onto Core seams (HttpClient,
/// <see cref="ISettingsService"/>, <see cref="IUserIdentityProvider"/>,
/// <see cref="IModerationGuard"/>). Faithful to the WPF cloud contract:
/// <list type="bullet">
/// <item>V2 proxy <c>codebambi-proxy.vercel.app/v2/ai/chat</c> with <c>X-Auth-Token</c> header +
///   <c>unified_id</c> body, <c>max_tokens=100</c>, <c>temperature=0.7</c>, exactly two messages
///   [system, user] (<c>AiService.cs:324-347</c>).</item>
/// <item>Client-side daily rate limit (free 100 / supporter 1000, midnight reset) with the
///   server-authoritative <c>requests_remaining</c> override (<c>AiService.cs:304-316,389-398</c>).</item>
/// <item><see cref="SanitizeResponse"/> stripping the context-tag echoes BEFORE output
///   moderation (<c>AiService.cs:401-403,428-454</c>).</item>
/// <item>The full input/output moderation sandwich with the escalation rule: the user-facing
///   notice (<see cref="IModerationCounter.RecordHit"/>) fires ONLY for user-typed INPUT on the
///   interactive chat path; every hit is logged (<c>AiService.cs:283-298,408-425</c>).</item>
/// <item>All five ambient reactions honor <c>returnRefusalSentinel:false</c> → a moderation hit
///   returns null (silently dropped, never a refusal bubble) and is still logged.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>v1 scope (documented follow-ups — see the task-board row):</b></para>
/// <list type="bullet">
/// <item>Cloud provider only (the default, <c>AiProvider==Cloud</c>) for chat + reactions. The
///   Local (persistent history / enrichment / command execution) and OpenAI-compatible providers
///   are follow-up commits; the OpenAI provider should ALSO close the WPF moderation-gap
///   (<c>OpenAiCompatibleService</c> runs no guard — likely an oversight).</list>
/// <item><see cref="GetRawChatCompletionAsync"/> uses local Ollama (stateless, persona-less) so the
///   quiz keeps working (mirrors <c>LocalAiService.cs:660-701</c> / the former
///   <c>AvaloniaQuizAiService</c>).</item>
/// <item>The legacy Patreon bearer fallback (<c>/ai/chat</c>) is not ported — no Patreon-token
///   seam in Core yet. V2 404 → null (logged).</item>
/// <item>The dedicated <c>ModerationLog</c> compliance file is written via the injected
///   <c>IModerationLog</c> seam — faithfully ported in the Avalonia head (<c>AvaloniaModerationLog</c>
///   writes the append-only <c>{UserDataPath}/logs/moderation.log</c>, matching WPF
///   <c>App.ModerationLog</c>); Serilog is the fallback only when the seam is absent.</item>
/// </list>
/// </remarks>
public sealed class CoreAiService : IAiService
{
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
    private const int FreeDailyLimit = 100;       // Free users (logged in, no Patreon)
    private const int PatreonDailyLimit = 1000;   // Patreon supporters
    private const int MaxTokensHardCap = 100;     // ~50 words, enough for video names (AiService.cs:34)
    private const string LoginRequiredHint = "Log in with Discord or Patreon to chat with me~ *giggles*"; // loc: ai_login_required_hint
    private static readonly string[] DefaultIdleFallback = { "Good girl~" };

    // WPF SanitizeResponse contract (AiService.cs:428-454). Strips context-tag echoes so they do
    // not trip the output-moderation regexes. Runs BEFORE CheckOutput.
    private static readonly Regex[] SanitizePatterns =
    {
        new(@"\[Category:[^\]]*\]", RegexOptions.Compiled),
        new(@"\[[A-Za-z]+/[A-Za-z]+\]", RegexOptions.Compiled),
        new(@"\[(?:Category|App|Title|Duration|Context):[^\]]*\]", RegexOptions.Compiled),
    };
    private static readonly Regex MultiSpace = new(@"\s{2,}", RegexOptions.Compiled);

    private readonly ISettingsService _settings;
    private readonly IUserIdentityProvider _identity;
    private readonly IModerationGuard _moderation;
    private readonly IModerationCounter? _counter;
    private readonly ISystemPromptBuilder _promptBuilder;
    private readonly IModService? _mods;
    private readonly ILogger<CoreAiService>? _logger;
    private readonly IModerationLog? _moderationLog;
    private readonly HttpClient _cloud;
    private readonly Random _fallbackRandom = new();

    private int _dailyRequestCount;
    private DateTime _lastResetDate = DateTime.Today;

    public CoreAiService(
        ISettingsService settings,
        IUserIdentityProvider identity,
        IModerationGuard moderation,
        ISystemPromptBuilder promptBuilder,
        ILogger<CoreAiService>? logger = null,
        IModerationCounter? counter = null,
        IModService? mods = null,
        IModerationLog? moderationLog = null)
    {
        _settings = settings;
        _identity = identity;
        _moderation = moderation;
        _promptBuilder = promptBuilder;
        _logger = logger;
        _counter = counter;
        _mods = mods;
        _moderationLog = moderationLog;
        _cloud = new HttpClient { BaseAddress = new Uri(ProxyBaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        _cloud.DefaultRequestHeaders.TryAddWithoutValidation("X-Client-Version", "avalonia");
        _cloud.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ConditioningControlPanel/Avalonia");
    }

    // ---------- Availability + rate limit (AiService.cs:39,52,57) ----------

    private bool HasCloudIdentity => !string.IsNullOrWhiteSpace(_identity.UnifiedUserId);
    private bool IsSupporter => _settings.Current?.HasCachedPremiumAccess == true;

    /// <inheritdoc/>
    /// <remarks>WPF: <c>App.HasCloudIdentity || App.Patreon?.HasAiAccess</c>. Core has no
    /// Patreon-HasAiAccess seam, so supporter status proxies via the cached premium flag.</remarks>
    public bool IsAvailable => HasCloudIdentity || IsSupporter;

    private int DailyLimit => IsSupporter ? PatreonDailyLimit : FreeDailyLimit;

    /// <inheritdoc/>
    public int DailyRequestsRemaining
    {
        get { ResetDailyCounterIfNeeded(); return Math.Max(0, DailyLimit - _dailyRequestCount); }
    }

    private void ResetDailyCounterIfNeeded()
    {
        if (DateTime.Today > _lastResetDate)
        {
            _dailyRequestCount = 0;
            _lastResetDate = DateTime.Today;
        }
    }

    // ---------- IAiService: chat ----------

    /// <inheritdoc/>
    /// <remarks>Thin wrapper over <see cref="GetBambiReplyExAsync"/>: translates a typed refusal
    /// into the sentinel string for legacy string callers (WPF AiService.cs:86-94).</remarks>
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
        // isUserMessage is ignored on cloud — the cloud path has its own circuit breaker (WPF :109-110).
        if (_settings.Current?.OfflineMode == true)
            return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

        if (!IsAvailable)
        {
            _logger?.LogDebug("CoreAiService: AI not available — user needs to log in.");
            return new AiReplyResult(LoginRequiredHint, IsAiGenerated: false, Refusal: null);
        }

        var systemPrompt = _promptBuilder.GetSystemPrompt();
        var result = await CloudChatAsync(userInput, systemPrompt, returnRefusalSentinel: true).ConfigureAwait(false);

        var refusalSource = ModerationRefusal.GetSource(result);
        if (refusalSource != null)
            return new AiReplyResult(string.Empty, IsAiGenerated: false,
                Refusal: new ModerationRefusalInfo(Category: null, Source: refusalSource.Value));
        if (string.IsNullOrEmpty(result))
            return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);
        return new AiReplyResult(result, IsAiGenerated: true, Refusal: null);
    }

    // ---------- IAiService: ambient reactions (returnRefusalSentinel:false) ----------
    // A moderation hit returns null (silently dropped — the user did not actively prompt) and is
    // still logged. Keyword/lock/video short-circuit !IsAvailable → null (WPF :198,214,237).

    public async Task<string?> GetAwarenessReactionAsync(string detectedName, string category,
        string serviceName = "", string pageTitle = "")
    {
        var website = string.IsNullOrWhiteSpace(serviceName) ? detectedName : serviceName;
        var tabName = string.IsNullOrWhiteSpace(pageTitle) ? detectedName : pageTitle;
        var userInput = $"[Category: {category} | App: {website} | Title: {tabName} | Duration: 0m]";
        return await CloudChatAsync(userInput, _promptBuilder.GetSystemPrompt(), returnRefusalSentinel: false).ConfigureAwait(false);
    }

    public async Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
    {
        var userInput = $"[Category: {category} | App: {displayName} | Title: {displayName} | Duration: {FormatDuration(duration)}]";
        return await CloudChatAsync(userInput, _promptBuilder.GetSystemPrompt(), returnRefusalSentinel: false).ConfigureAwait(false);
    }

    public async Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
    {
        if (!IsAvailable) return null;
        var userInput = string.IsNullOrWhiteSpace(promptTemplate)
            ? $"You just caught the user on the word '{keyword}'. React in character, one short line."
            : promptTemplate!.Replace("{keyword}", keyword);
        return await CloudChatAsync(userInput, _promptBuilder.GetSystemPrompt(), returnRefusalSentinel: false).ConfigureAwait(false);
    }

    public async Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
    {
        if (!IsAvailable) return null;
        var userInput = string.IsNullOrWhiteSpace(promptTemplate)
            ? $"The user made {mistakes} mistakes in '{sentance}' for the lock screen. They had to type it {amount} of time. React in character, one short line."
            : promptTemplate!.Replace("{sentance}", sentance).Replace("{mistakes}", mistakes.ToString()).Replace("{amount}", amount.ToString());
        return await CloudChatAsync(userInput, _promptBuilder.GetSystemPrompt(), returnRefusalSentinel: false).ConfigureAwait(false);
    }

    public async Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
    {
        if (!IsAvailable) return null;
        var userInput = string.IsNullOrWhiteSpace(promptTemplate)
            ? $"The user has just finished the mandatory video {title}. React in character, one short line."
            : promptTemplate!.Replace("{title}", title);
        return await CloudChatAsync(userInput, _promptBuilder.GetSystemPrompt(), returnRefusalSentinel: false).ConfigureAwait(false);
    }

    // ---------- IAiService: stateless raw completion (quiz) ----------

    /// <inheritdoc/>
    /// <remarks>Stateless, persona-less local Ollama (mirrors LocalAiService.cs:660-701 and the
    /// former AvaloniaQuizAiService). Only Ollama path that sends options.temperature. Fresh
    /// HttpClient per call (quiz cadence is low; matches the WPF static helper).</remarks>
    public async Task<string?> GetRawChatCompletionAsync(IEnumerable<(string role, string content)> messages, double temperature = 0.8)
    {
        var cp = _settings.Current?.CompanionPrompt;
        if (cp == null) return null;
        var host = NormalizeHost(cp.AiOllamaHost);
        var model = cp.AiModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            _logger?.LogDebug("CoreAiService: no local model configured; raw completion skipped.");
            return null;
        }

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
                _logger?.LogWarning("CoreAiService: Ollama HTTP {Status}: {Body}", (int)resp.StatusCode, Truncate(body, 200));
                return null;
            }
            return ExtractContent(body);
        }
        catch (TaskCanceledException) { _logger?.LogWarning("CoreAiService: raw completion timed out."); return null; }
        catch (Exception ex) { _logger?.LogWarning(ex, "CoreAiService: raw chat completion failed."); return null; }
    }

    // ---------- Cloud transport (AiService.cs:260-454) ----------

    /// <summary>
    /// Core cloud chat: input moderation → rate limit → V2 proxy POST → server-side limit sync →
    /// sanitize → output moderation. Returns the assistant text, a moderation sentinel (only when
    /// <paramref name="returnRefusalSentinel"/> and a hit occurs), or null on any non-refusal
    /// failure.
    /// </summary>
    private async Task<string?> CloudChatAsync(string userInput, string systemPrompt, bool returnRefusalSentinel)
    {
        // --- Layer 1: INPUT moderation (AiService.cs:283-298) ---
        var inputCheck = _moderation.CheckInput(userInput);
        if (!inputCheck.Allow && inputCheck.Category.HasValue)
        {
            LogModeration(inputCheck.Category.Value, source: "input", modelHint: "cloud");
            // Escalation (user-facing notice) ONLY on the interactive chat path (returnRefusalSentinel).
            if (returnRefusalSentinel)
                _counter?.RecordHit(inputCheck.Category.Value, "input:cloud");
            return returnRefusalSentinel ? ModerationRefusal.InputSentinel : null;
        }
        // ProfessionalAdvice is a soft hit (Allow==true, Category set) — WPF logs soft hits too.
        if (inputCheck.Allow && inputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
            LogModeration(ProhibitedCategory.ProfessionalAdvice, source: "input", modelHint: "cloud");

        // --- Availability (AiService.cs:297) — AFTER input moderation (so a blocked ambient hit
        //     is still logged) and BEFORE the rate-limit bump (an unavailable call consumes no
        //     daily slot). GetAwareness/GetStillOn rely on this order; keyword/lock/video keep
        //     their own earlier short-circuit (WPF :198/:214/:237). ---
        if (!IsAvailable) return null;

        // --- Rate limit (AiService.cs:304-316) ---
        ResetDailyCounterIfNeeded();
        if (_dailyRequestCount >= DailyLimit)
        {
            _logger?.LogDebug("CoreAiService: daily limit reached ({Count}/{Limit}).", _dailyRequestCount, DailyLimit);
            return null;
        }
        _dailyRequestCount++;

        // --- Build + send V2 request (AiService.cs:324-329,336-347) ---
        var authToken = _settings.Current?.AuthToken;
        var requestMessages = new[]
        {
            new ProxyChatMessage { Role = "system", Content = systemPrompt },
            new ProxyChatMessage { Role = "user", Content = userInput }
        };
        var v2 = new V2ChatRequest
        {
            UnifiedId = _identity.UnifiedUserId ?? string.Empty,
            Messages = requestMessages,
            MaxTokens = MaxTokensHardCap,
            Temperature = 0.7
        };

        ProxyChatResponse? result;
        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, "/v2/ai/chat");
            if (!string.IsNullOrEmpty(authToken))
                msg.Headers.TryAddWithoutValidation("X-Auth-Token", authToken);
            msg.Content = JsonContent.Create(v2);
            using var response = await _cloud.SendAsync(msg).ConfigureAwait(false);

            // V2 404 → WPF falls back to legacy Patreon bearer. No Patreon-token seam in Core yet
            // → return null (follow-up). Other non-success → null (WPF :367-373).
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger?.LogDebug("CoreAiService: V2 endpoint 404; legacy Patreon fallback not ported yet.");
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("CoreAiService: proxy HTTP {Status}.", (int)response.StatusCode);
                return null;
            }

            result = await response.Content.ReadFromJsonAsync<ProxyChatResponse>().ConfigureAwait(false);
        }
        catch (TaskCanceledException) { _logger?.LogWarning("CoreAiService: cloud request timed out."); return null; }
        catch (HttpRequestException ex) { _logger?.LogWarning(ex, "CoreAiService: cloud HTTP failure."); return null; }
        catch (Exception ex) { _logger?.LogWarning(ex, "CoreAiService: cloud request failed."); return null; }

        if (result == null) return null;
        if (!string.IsNullOrEmpty(result.Error))
        {
            _logger?.LogWarning("CoreAiService: proxy error: {Error}", result.Error);
            return null;
        }
        if (string.IsNullOrWhiteSpace(result.Content)) return null;

        // --- Server-authoritative rate-limit override (AiService.cs:389-398) ---
        if (result.RequestsRemaining.HasValue && result.RequestsRemaining.Value >= 0)
        {
            var serverLimit = Math.Max(DailyLimit, _dailyRequestCount + result.RequestsRemaining.Value);
            _dailyRequestCount = serverLimit - result.RequestsRemaining.Value;
        }

        // --- Sanitize BEFORE output moderation (AiService.cs:401-403,428-454) ---
        var sanitized = SanitizeResponse(result.Content!);
        if (string.IsNullOrWhiteSpace(sanitized))
            return GetFallbackResponse();

        // --- Layer 1: OUTPUT moderation (AiService.cs:408-425) ---
        var outputCheck = _moderation.CheckOutput(sanitized);
        if (!outputCheck.Allow && outputCheck.Category.HasValue)
        {
            LogModeration(outputCheck.Category.Value, source: "output", modelHint: "cloud");
            // Escalation NEVER fires for output (not the user's doing).
            return returnRefusalSentinel ? ModerationRefusal.OutputSentinel : null;
        }
        if (outputCheck.Allow && outputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
            LogModeration(ProhibitedCategory.ProfessionalAdvice, source: "output", modelHint: "cloud");

        return sanitized;
    }

    /// <summary>
    /// Compliance record. Routes through the injected <c>IModerationLog</c> (the dedicated
    /// moderation.log file, faithful to WPF <c>App.ModerationLog</c>); Serilog is the fallback
    /// only when the seam is absent. No message bodies are logged — category + source + model only.
    /// </summary>
    private void LogModeration(ProhibitedCategory category, string source, string modelHint)
    {
        // Prefer the dedicated compliance file (IModerationLog, faithful to WPF App.ModerationLog);
        // Serilog is the fallback when the seam isn't registered.
        if (_moderationLog != null) { _moderationLog.Record(category, source, modelHint); return; }
        _logger?.LogWarning("Moderation hit | category={Category} | source={Source} | model={Model}",
            category, source, modelHint);
    }

    // ---------- Helpers ----------

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
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c))
                return c.GetString();
        }
        catch { /* malformed JSON — treat as empty */ }
        return null;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "...";

    public void Dispose() => _cloud.Dispose();
}
