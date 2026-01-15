using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles AI-powered chat responses for the Bambi Companion widget.
    /// Uses hosted proxy that forwards to OpenRouter for roleplay.
    /// Requires Patreon Level 1 or higher for AI chat features.
    /// </summary>
    public class AiService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly BambiSprite _bambiSprite;

        // Configuration - must match PatreonService
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

        // Circuit breaker tracking (client-side)
        private int _dailyRequestCount;
        private DateTime _lastResetDate;
        private const int DailyLimit = 1000;
        private const int MaxTokensHardCap = 60; // Hard cap on response tokens to control costs (~30 words)

        // Fallback response when API unavailable or limit reached
        private const string FallbackResponse = "Bambi's head is so empty right now~ *giggles*";

        /// <summary>
        /// Whether AI is available (requires Patreon Level 1+ or whitelist)
        /// </summary>
        public bool IsAvailable => App.Patreon?.HasAiAccess == true;

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

            _bambiSprite = new BambiSprite();
            _lastResetDate = DateTime.Today;
            _dailyRequestCount = 0;

            App.Logger?.Information("AiService initialized (proxy mode, requires Patreon Level 1+)");
        }

        /// <summary>
        /// Gets an AI-generated reply in the Bambi personality.
        /// Returns fallback response if API unavailable or daily limit reached.
        /// </summary>
        public async Task<string> GetBambiReplyAsync(string userInput)
        {
            // Use slut mode prompt if enabled (Patreon only)
            var isSlutMode = App.Settings?.Current?.SlutModeEnabled == true && App.Patreon?.HasPremiumAccess == true;
            var prompt = isSlutMode ? _bambiSprite.GetSlutModePersonality() : _bambiSprite.GetSystemPrompt();

            var result = await GetAiResponseAsync(userInput, prompt);
            return result ?? FallbackResponse;
        }

        /// <summary>
        /// Gets an AI-generated reaction to the user's current activity.
        /// Used by Awareness Mode. Passes raw website and tab name for AI to interpret.
        /// Returns null if AI unavailable (caller should use preset phrase).
        /// </summary>
        public async Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "")
        {
            // Use slut mode prompt if enabled (Patreon only)
            var isSlutMode = App.Settings?.Current?.SlutModeEnabled == true && App.Patreon?.HasPremiumAccess == true;
            var prompt = isSlutMode ? _bambiSprite.GetSlutModePersonality() : _bambiSprite.GetSystemPrompt();

            // Get website/service name and tab title
            var website = string.IsNullOrEmpty(serviceName) ? detectedName : serviceName;
            var tabName = string.IsNullOrEmpty(pageTitle) ? detectedName : pageTitle;

            // Format context as expected by BambiSprite: [App: X | Title: Y | Duration: Z]
            var userInput = $"[App: {website} | Title: {tabName} | Duration: 0m]";

            return await GetAiResponseAsync(userInput, prompt);
        }

        /// <summary>
        /// Gets an AI-generated "still on" reaction when user has been on the same activity for a while.
        /// Includes time context for the AI to reference.
        /// </summary>
        public async Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
        {
            // Use slut mode prompt if enabled (Patreon only)
            var isSlutMode = App.Settings?.Current?.SlutModeEnabled == true && App.Patreon?.HasPremiumAccess == true;
            var prompt = isSlutMode ? _bambiSprite.GetSlutModePersonality() : _bambiSprite.GetSystemPrompt();

            // Format duration nicely
            string durationText;
            if (duration.TotalMinutes < 1)
                durationText = $"{(int)duration.TotalSeconds}s";
            else if (duration.TotalMinutes < 60)
                durationText = $"{(int)duration.TotalMinutes}m";
            else
                durationText = $"{(int)duration.TotalHours}h";

            // Format context as expected by BambiSprite: [App: X | Title: Y | Duration: Z]
            var userInput = $"[App: {displayName} | Title: {displayName} | Duration: {durationText}]";

            return await GetAiResponseAsync(userInput, prompt);
        }

        /// <summary>
        /// Core method to get an AI response with custom system prompt.
        /// Returns null if unavailable.
        /// </summary>
        private async Task<string?> GetAiResponseAsync(string userInput, string systemPrompt)
        {
            // Check Patreon access (tier 1+ or whitelisted)
            if (App.Patreon?.HasAiAccess != true)
            {
                App.Logger?.Warning("AiService: No AI access - Tier={Tier}, HasAiAccess={HasAi}, HasPremium={HasPremium}",
                    App.Patreon?.CurrentTier, App.Patreon?.HasAiAccess, App.Patreon?.HasPremiumAccess);
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

            // Get Patreon access token
            var accessToken = App.Patreon?.GetAccessToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                App.Logger?.Warning("AiService: No Patreon access token available - IsAuthenticated={IsAuth}",
                    App.Patreon?.IsAuthenticated);
                return null;
            }

            try
            {
                _dailyRequestCount++;

                // Build request for proxy
                var request = new ProxyChatRequest
                {
                    Messages = new[]
                    {
                        new ProxyChatMessage { Role = "system", Content = systemPrompt },
                        new ProxyChatMessage { Role = "user", Content = userInput }
                    },
                    MaxTokens = MaxTokensHardCap,  // Hard capped to control costs (~25 words max)
                    Temperature = 0.7
                };

                // Add Patreon bearer token for authorization
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);

                var response = await _httpClient.PostAsJsonAsync("/ai/chat", request);

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

                return result.Content;
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

        public void Dispose()
        {
            _httpClient.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
