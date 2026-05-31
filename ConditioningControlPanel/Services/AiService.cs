using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles AI-powered chat responses for the Bambi Companion widget.
    /// Uses hosted proxy that forwards to OpenRouter for roleplay.
    /// Free for all users with a cloud identity; falls back to Patreon auth.
    /// </summary>
    public class AiService : IDisposable, IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly BambiSprite _bambiSprite;

        // Configuration - must match PatreonService
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

        // Circuit breaker tracking (client-side)
        private int _dailyRequestCount;
        private DateTime _lastResetDate;
        private const int FreeDailyLimit = 100;     // Free users (logged in, no Patreon)
        private const int PatreonDailyLimit = 1000;  // Patreon supporters
        private const int MaxTokensHardCap = 100; // Hard cap on response tokens to control costs (~50 words, enough for video names)

        /// <summary>
        /// Effective daily limit based on user tier
        /// </summary>
        private int DailyLimit => App.Patreon?.HasAiAccess == true ? PatreonDailyLimit : FreeDailyLimit;

        // Fallback response when API unavailable or limit reached — pick from idle phrases for variety
        private static readonly Random _fallbackRandom = new();
        private static string GetFallbackResponse()
        {
            var phrases = App.Mods?.GetPhrases("Idle") ?? new[] { "Good girl~" };
            return phrases[_fallbackRandom.Next(phrases.Length)];
        }

        /// <summary>
        /// Whether AI is available (cloud identity or Patreon access)
        /// </summary>
        public bool IsAvailable => App.HasCloudIdentity || App.Patreon?.HasAiAccess == true;

        /// <summary>
        /// Daily requests remaining (client-side tracking)
        /// </summary>
        public int DailyRequestsRemaining => Math.Max(0, DailyLimit - _dailyRequestCount);

        public AiService()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(ProxyBaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");

            _bambiSprite = new BambiSprite();
            _lastResetDate = DateTime.Today;
            _dailyRequestCount = 0;

            App.Logger?.Information("AiService initialized (proxy mode, V2 auth or Patreon)");
        }

        /// <summary>
        /// Gets an AI-generated reply in the Bambi personality.
        /// Returns fallback response if API unavailable or daily limit reached.
        ///
        /// Legacy string-returning API kept for non-UI callers (Autonomy, command
        /// scripts) that only need text. New UI surfaces (chat box) should call
        /// <see cref="GetBambiReplyExAsync"/> instead so they can distinguish real
        /// AI replies from canned fallbacks (P2/C4 — pink "AI" badge must not
        /// appear over fallback strings).
        /// </summary>
        public async Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
        {
            var result = await GetBambiReplyExAsync(userInput, isUserMessage);
            // Preserve the legacy sentinel-string contract so any caller still
            // routing through the string API can detect refusals.
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
        public async Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
        {
            // isUserMessage is honored by the local provider for queue/drop logic;
            // cloud path has its own circuit breaker and rate limiting, so we ignore it here.
            _ = isUserMessage;

            // Offline mode → canned phrase, never an AI reply.
            if (App.Settings?.Current?.OfflineMode == true)
                return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

            if (!IsAvailable)
            {
                App.Logger?.Debug("AiService: AI not available — user needs to log in for AI chat");
                return new AiReplyResult(Loc.Get("ai_login_required_hint"), IsAiGenerated: false, Refusal: null);
            }

            // Get prompt from active personality preset (handles all personalities including slut mode)
            var prompt = _bambiSprite.GetSystemPrompt();

            // GetAiResponseAsync returns:
            //   • a refusal sentinel string → typed refusal result
            //   • null on any failure (HTTP error, empty content, daily-limit, etc.) → canned fallback
            //   • model text → genuine AI reply
            var result = await GetAiResponseAsync(userInput, prompt, returnRefusalSentinel: true);

            var refusalSource = ModerationRefusal.GetSource(result);
            if (refusalSource.HasValue)
            {
                // Category was already logged inside GetAiResponseAsync; the sentinel
                // string can't carry it, so we surface only the source here.
                return new AiReplyResult(
                    string.Empty,
                    IsAiGenerated: false,
                    Refusal: new ModerationRefusalInfo(Category: null, Source: refusalSource.Value));
            }

            if (result == null)
                return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

            return new AiReplyResult(result, IsAiGenerated: true, Refusal: null);
        }

        /// <summary>
        /// Gets an AI-generated reaction to the user's current activity.
        /// Used by Awareness Mode. Passes raw website and tab name for AI to interpret.
        /// Returns null if AI unavailable (caller should use preset phrase).
        /// </summary>
        public async Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "")
        {
            // Get prompt from active personality preset
            var prompt = _bambiSprite.GetSystemPrompt();

            // Get website/service name and tab title
            var website = string.IsNullOrEmpty(serviceName) ? detectedName : serviceName;
            var tabName = string.IsNullOrEmpty(pageTitle) ? detectedName : pageTitle;

            // Format context with category for accurate reactions
            // Format: [Category: X | App: Y | Title: Z | Duration: 0m]
            var userInput = $"[Category: {category} | App: {website} | Title: {tabName} | Duration: 0m]";

            return await GetAiResponseAsync(userInput, prompt);
        }

        /// <summary>
        /// Gets an AI-generated "still on" reaction when user has been on the same activity for a while.
        /// Includes time context for the AI to reference.
        /// </summary>
        public async Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
        {
            // Get prompt from active personality preset
            var prompt = _bambiSprite.GetSystemPrompt();

            // Format duration nicely
            string durationText;
            if (duration.TotalMinutes < 1)
                durationText = $"{(int)duration.TotalSeconds}s";
            else if (duration.TotalMinutes < 60)
                durationText = $"{(int)duration.TotalMinutes}m";
            else
                durationText = $"{(int)duration.TotalHours}h";

            // Format context with category for accurate reactions
            // Format: [Category: X | App: Y | Title: Z | Duration: Nm]
            var userInput = $"[Category: {category} | App: {displayName} | Title: {displayName} | Duration: {durationText}]";

            return await GetAiResponseAsync(userInput, prompt);
        }

        /// <summary>
        /// Gets an AI-generated reaction line when a configured keyword trigger fires.
        /// Used by <see cref="KeywordTriggerService"/>'s AvatarCommentAction dispatch.
        /// Returns null if AI is unavailable (caller is expected to use a canned phrase).
        /// </summary>
        public async Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
        {
            if (!IsAvailable) return null;

            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = string.IsNullOrEmpty(promptTemplate)
                ? $"You just caught the user on the word '{keyword}'. React in character, one short line."
                : promptTemplate.Replace("{keyword}", keyword);

            return await GetAiResponseAsync(userInput, systemPrompt);
        }

        /// <summary>
        /// Gets an AI-generated reaction after the user finishes a lock-screen mantra.
        /// Returns null if AI unavailable.
        /// </summary>
        public async Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
        {
            if (!IsAvailable) return null;

            var systemPrompt = _bambiSprite.GetSystemPrompt();
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

            return await GetAiResponseAsync(userInput, systemPrompt);
        }

        /// <summary>
        /// Gets an AI-generated reaction after a mandatory video finishes.
        /// Returns null if AI unavailable.
        /// </summary>
        public async Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
        {
            if (!IsAvailable) return null;

            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = string.IsNullOrEmpty(promptTemplate)
                ? $"The user has just finished the mandatory video {title}. React in character, one short line."
                : promptTemplate.Replace("{title}", title);

            return await GetAiResponseAsync(userInput, systemPrompt);
        }

        /// <summary>
        /// Core method to get an AI response with custom system prompt.
        /// Returns null if unavailable.
        ///
        /// Moderation: if <paramref name="returnRefusalSentinel"/> is true and the input
        /// or output trips <see cref="App.ModerationGuard"/>, returns the appropriate
        /// <see cref="ModerationRefusal"/> sentinel string so the chat UI can render the
        /// refusal bubble + POLICY badge. When false (awareness, keyword, lockscreen,
        /// video paths) a moderation hit returns null and the caller silently drops the
        /// reaction — surfacing a refusal there would be jarring (user didn't actively
        /// prompt).
        /// </summary>
        private async Task<string?> GetAiResponseAsync(string userInput, string systemPrompt, bool returnRefusalSentinel = false)
        {
            // Check offline mode first
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("AiService: Offline mode enabled, skipping AI request");
                return null;
            }

            // INPUT MODERATION (Layer 1 — code-side, prompt cannot bypass).
            // Runs BEFORE the HTTP request so prohibited inputs never leave the client.
            var guard = App.ModerationGuard;
            if (guard != null)
            {
                var inputCheck = guard.CheckInput(userInput ?? string.Empty);
                if (!inputCheck.Allow && inputCheck.Category.HasValue)
                {
                    App.ModerationLog?.Record(inputCheck.Category.Value, source: "input", modelHint: "cloud");
                    // Only escalate the user-facing Content Policy Notice for content the
                    // user actually typed. returnRefusalSentinel is true only on the
                    // interactive chat path; every background/auto reaction (awareness,
                    // keyword, lockscreen, video-done) leaves it false and must not pop
                    // the warning — that filtering is "on us, not on them". The hit is
                    // still logged above for the CCBill compliance record either way.
                    if (returnRefusalSentinel)
                        App.ModerationCounter?.RecordHit(inputCheck.Category.Value, "input:cloud");
                    App.Logger?.Information("AiService: input blocked by ModerationGuard (category={Cat})", inputCheck.Category);
                    return returnRefusalSentinel ? ModerationRefusal.InputSentinel : null;
                }
                // ProfessionalAdvice is soft (Allow=true with Category set) — log only.
                if (inputCheck.Allow && inputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                {
                    App.ModerationLog?.Record(ProhibitedCategory.ProfessionalAdvice, source: "input", modelHint: "cloud");
                }
            }

            // Check access (cloud identity or Patreon)
            if (!IsAvailable)
            {
                App.Logger?.Debug("AiService: No AI access - HasCloudIdentity={Cloud}, HasAiAccess={HasAi}",
                    App.HasCloudIdentity, App.Patreon?.HasAiAccess);
                return null;
            }

            // Reset daily count at midnight
            if (DateTime.Today > _lastResetDate)
            {
                _dailyRequestCount = 0;
                _lastResetDate = DateTime.Today;
                App.Logger?.Debug("AiService: Daily request count reset");
            }

            // Circuit breaker check (client-side backup)
            if (_dailyRequestCount >= DailyLimit)
            {
                App.Logger?.Debug("AiService: Daily limit reached ({Limit} requests)", DailyLimit);
                return null;
            }

            try
            {
                _dailyRequestCount++;

                // Build messages array
                var messages = new[]
                {
                    new ProxyChatMessage { Role = "system", Content = systemPrompt },
                    new ProxyChatMessage { Role = "user", Content = userInput }
                };

                HttpResponseMessage response;

                // Try V2 auth first (unified_id + X-Auth-Token) — free for all cloud users
                var unifiedId = App.UnifiedUserId;
                var authToken = App.Settings?.Current?.AuthToken;
                if (!string.IsNullOrEmpty(unifiedId))
                {
                    var v2Request = new V2ChatRequest
                    {
                        UnifiedId = unifiedId,
                        Messages = messages,
                        MaxTokens = MaxTokensHardCap,
                        Temperature = 0.7
                    };

                    using var v2Msg = new HttpRequestMessage(HttpMethod.Post, "/v2/ai/chat");
                    if (!string.IsNullOrEmpty(authToken))
                        v2Msg.Headers.TryAddWithoutValidation("X-Auth-Token", authToken);
                    v2Msg.Content = JsonContent.Create(v2Request);

                    response = await _httpClient.SendAsync(v2Msg);

                    // If V2 endpoint not deployed yet (404), fall back to legacy Patreon auth
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        App.Logger?.Debug("AiService: V2 endpoint not available, trying legacy auth");
                        response.Dispose();
                        response = await SendLegacyRequestAsync(messages);
                        if (response == null) return null;
                    }
                }
                else
                {
                    response = await SendLegacyRequestAsync(messages);
                    if (response == null) return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    App.Logger?.Warning("AiService: Proxy returned {Status}: {Error}",
                        response.StatusCode, errorText);
                    return null;
                }

                var result = await response.Content.ReadFromJsonAsync<ProxyChatResponse>();

                if (result == null || !string.IsNullOrEmpty(result.Error))
                {
                    App.Logger?.Warning("AiService: Proxy error: {Error}", result?.Error);
                    return null;
                }

                if (string.IsNullOrEmpty(result.Content))
                {
                    App.Logger?.Warning("AiService: Empty response from proxy");
                    return null;
                }

                // Update remaining count if provided by server (server is authoritative)
                if (result.RequestsRemaining.HasValue && result.RequestsRemaining.Value >= 0)
                {
                    // Server tells us how many requests remain - calculate our count from that
                    var serverLimit = Math.Max(DailyLimit, _dailyRequestCount + result.RequestsRemaining.Value);
                    _dailyRequestCount = serverLimit - result.RequestsRemaining.Value;
                    App.Logger?.Debug("AiService: Server says {Remaining} remaining, calculated count={Count}",
                        result.RequestsRemaining.Value, _dailyRequestCount);
                }

                App.Logger?.Information("AiService: Got reply ({RequestCount}/{Limit} today, {Remaining} remaining)",
                    _dailyRequestCount, DailyLimit, DailyRequestsRemaining);

                // Sanitize response to remove any leaked metadata tags FIRST (so context-tag
                // echoes don't accidentally trip moderation regexes).
                var sanitized = SanitizeResponse(result.Content);

                // OUTPUT MODERATION (Layer 1). Discard prohibited model output before display.
                if (guard != null)
                {
                    var outputCheck = guard.CheckOutput(sanitized ?? string.Empty);
                    if (!outputCheck.Allow && outputCheck.Category.HasValue)
                    {
                        App.ModerationLog?.Record(outputCheck.Category.Value, source: "output", modelHint: "cloud");
                        // Model OUTPUT that trips the filter is never the user's doing, so
                        // it does NOT escalate the Content Policy Notice (logged above for
                        // compliance only). The warning is reserved for user-typed input.
                        App.Logger?.Information("AiService: output blocked by ModerationGuard (category={Cat})", outputCheck.Category);
                        return returnRefusalSentinel ? ModerationRefusal.OutputSentinel : null;
                    }
                    if (outputCheck.Allow && outputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                    {
                        App.ModerationLog?.Record(ProhibitedCategory.ProfessionalAdvice, source: "output", modelHint: "cloud");
                    }
                }

                return sanitized;
            }
            catch (TaskCanceledException)
            {
                App.Logger?.Warning("AiService: Request timed out");
                return null;
            }
            catch (HttpRequestException ex)
            {
                App.Logger?.Warning(ex, "AiService: Network error");
                return null;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "AiService: Failed to get AI reply");
                return null;
            }
        }

        /// <summary>
        /// Sanitizes AI response by removing any leaked internal metadata tags.
        /// The AI sometimes echoes context tags that should be hidden from users.
        /// </summary>
        private static string SanitizeResponse(string? response)
        {
            if (string.IsNullOrEmpty(response))
                return response ?? string.Empty;

            // Remove context metadata tags like [Category: X | App: Y | Title: Z | Duration: Nm]
            var sanitized = Regex.Replace(response, @"\[Category:[^\]]*\]", "", RegexOptions.IgnoreCase);

            // Remove reaction category tags like [Media/Streaming] or [Gaming/Casual]
            sanitized = Regex.Replace(sanitized, @"\[[A-Za-z]+/[A-Za-z]+\]", "", RegexOptions.IgnoreCase);

            // Remove any standalone square bracket tags that look like metadata
            sanitized = Regex.Replace(sanitized, @"\[(?:Category|App|Title|Duration|Context):[^\]]*\]", "", RegexOptions.IgnoreCase);

            // Clean up any resulting double spaces or leading/trailing whitespace
            sanitized = Regex.Replace(sanitized, @"\s{2,}", " ");
            sanitized = sanitized.Trim();

            // If sanitization removed everything meaningful, return a fallback
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                App.Logger?.Warning("AiService: Response was entirely metadata, returning fallback");
                return GetFallbackResponse();
            }

            return sanitized;
        }

        /// <summary>
        /// Sends AI request via legacy Patreon Bearer auth. Returns null if no Patreon token available.
        /// </summary>
        private async Task<HttpResponseMessage?> SendLegacyRequestAsync(ProxyChatMessage[] messages)
        {
            var accessToken = App.Patreon?.GetAccessToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                App.Logger?.Warning("AiService: No auth method available (no Patreon token)");
                return null;
            }

            var legacyRequest = new ProxyChatRequest
            {
                Messages = messages,
                MaxTokens = MaxTokensHardCap,
                Temperature = 0.7
            };

            using var legacyMsg = new HttpRequestMessage(HttpMethod.Post, "/ai/chat");
            legacyMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            legacyMsg.Content = JsonContent.Create(legacyRequest);

            return await _httpClient.SendAsync(legacyMsg);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
