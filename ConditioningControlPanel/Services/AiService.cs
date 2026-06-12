using System;
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
    public class AiService : AiProviderBase
    {
        private readonly HttpClient _httpClient;

        // Configuration - must match PatreonService
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

        // Circuit breaker tracking (client-side)
        private const int FreeDailyLimit = 100;     // Free users (logged in, no Patreon)
        private const int PatreonDailyLimit = 1000;  // Patreon supporters
        private const int MaxTokensHardCap = 100; // Hard cap on response tokens to control costs (~50 words, enough for video names)

        /// <summary>
        /// Effective daily limit based on user tier
        /// </summary>
        private int DailyLimit => App.Patreon?.HasAiAccess == true ? PatreonDailyLimit : FreeDailyLimit;

        /// <summary>
        /// Whether AI is available (cloud identity or Patreon access)
        /// </summary>
        public override bool IsAvailable => App.HasCloudIdentity || App.Patreon?.HasAiAccess == true;

        /// <summary>
        /// Daily requests remaining (client-side tracking)
        /// </summary>
        public override int DailyRequestsRemaining
        {
            get
            {
                ResetDailyCounterIfNeeded();
                var usage = App.Settings?.Current?.CompanionPrompt?.Usage;
                return Math.Max(0, DailyLimit - (usage?.CloudRequestCount ?? 0));
            }
        }

        /// <summary>
        /// Cloud proxy handles effects server-side; don't execute parsed commands locally.
        /// </summary>
        protected override bool SupportsEffectCommands => false;

        public AiService()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(ProxyBaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");

            ResetDailyCounterIfNeeded();

            App.Logger?.Information("AiService initialized (proxy mode, V2 auth or Patreon)");
        }

        public override async Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
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

            return await base.GetBambiReplyExAsync(userInput, isUserMessage);
        }

        /// <summary>
        /// Core transport: posts to the cloud proxy (V2 auth or legacy Patreon Bearer),
        /// updates the persisted daily counter, and returns sanitized assistant content.
        /// </summary>
        protected override async Task<string?> GetRawCompletionAsync(
            string systemPrompt,
            string userInput,
            bool isUserMessage)
        {
            _ = isUserMessage;

            // Check offline mode first
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("AiService: Offline mode enabled, skipping AI request");
                return null;
            }

            // Check access (cloud identity or Patreon)
            if (!IsAvailable)
            {
                App.Logger?.Debug("AiService: No AI access - HasCloudIdentity={Cloud}, HasAiAccess={HasAi}",
                    App.HasCloudIdentity, App.Patreon?.HasAiAccess);
                return null;
            }

            ResetDailyCounterIfNeeded();

            // Circuit breaker check (client-side backup)
            var usage = App.Settings?.Current?.CompanionPrompt?.Usage;
            if (usage != null && usage.CloudRequestCount >= DailyLimit)
            {
                App.Logger?.Debug("AiService: Daily limit reached ({Limit} requests)", DailyLimit);
                return null;
            }

            try
            {
                BumpDailyCounter();

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
                if (result.RequestsRemaining.HasValue && result.RequestsRemaining.Value >= 0 && usage != null)
                {
                    // Server tells us how many requests remain - calculate our count from that
                    var serverLimit = Math.Max(DailyLimit, usage.CloudRequestCount + result.RequestsRemaining.Value);
                    usage.CloudRequestCount = serverLimit - result.RequestsRemaining.Value;
                    App.Settings?.Save();
                    App.Logger?.Debug("AiService: Server says {Remaining} remaining, calculated count={Count}",
                        result.RequestsRemaining.Value, usage.CloudRequestCount);
                }

                App.Logger?.Information("AiService: Got reply ({RequestCount}/{Limit} today, {Remaining} remaining)",
                    usage?.CloudRequestCount ?? 0, DailyLimit, DailyRequestsRemaining);

                // Sanitize response to remove any leaked metadata tags FIRST (so context-tag
                // echoes don't accidentally trip moderation regexes).
                return SanitizeResponse(result.Content);
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
        private string SanitizeResponse(string? response)
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

        protected override string GetFallbackResponse()
        {
            var phrases = App.Mods?.GetPhrases("Idle") ?? new[] { "Good girl~" };
            return phrases[_fallbackRandom.Next(phrases.Length)];
        }

        protected override string GetModelHint() => "cloud";

        protected override string GetProviderName() => "AiService";

        private void ResetDailyCounterIfNeeded()
        {
            var usage = App.Settings?.Current?.CompanionPrompt?.Usage;
            if (usage == null) return;

            if (DateTime.Today <= usage.LastResetDate) return;

            usage.CloudRequestCount = 0;
            usage.LastResetDate = DateTime.Today;
            App.Settings?.Save();
            App.Logger?.Debug("AiService: Daily request count reset");
        }

        private void BumpDailyCounter()
        {
            ResetDailyCounterIfNeeded();
            var usage = App.Settings?.Current?.CompanionPrompt?.Usage;
            if (usage == null) return;

            usage.CloudRequestCount++;
            App.Settings?.Save();
        }

        private static readonly Random _fallbackRandom = new();

        public override void Dispose()
        {
            _httpClient.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
