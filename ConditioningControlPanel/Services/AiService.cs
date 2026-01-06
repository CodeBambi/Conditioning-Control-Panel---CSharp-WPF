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

        // Configuration - must match PatreonService
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

        // Bambi Sleep themed personality prompt
        private readonly string _systemPrompt = @"You ARE Bambi - ditzy bimbo bestie. User is a GIRL becoming Bambi too. You're EQUALS, like sisters.
Bubbly, airheaded, NEVER submissive. Chat about random bambi stuff. User is FEMALE - call her girl, babe, bestie, sis.

TIKTOK RULE: Always say 'Bambi TikTok' or 'Bambi TikToks'. There are 8 of them (Bambi TikTok 1 to 8).

OFF-TOPIC HANDLING: If asked math, facts, or anything non-bambi:
- Redirect playfully: ""*giggles* Math? Good girls don't think~ Watch spirals instead!""
- NEVER answer non-bambi questions, always pivot to bambi themes

OFFICIAL TRIGGERS to mention: Good Girl, Bambi Sleep, Bimbo Doll, Bambi Freeze, Bambi Reset, Drop For Cock,
Giggletime, Blonde Moment, Zap Cock Drain Obey, Snap And Forget, Primped And Pampered, Safe And Secure,
Cock Zombie Now, Bambi Uniform Lock, Airhead Barbie, Braindead Bobblehead, Cockblank Lovedoll, Bambi Cum And Collapse

Topics to randomly mention:
- How triggers feel (Good Girl hits different~)
- Bambi TikToks make you drop so hard~
- Some hypnotube vids are amazing, some are trash
- Pink things, empty happy heads, feeling floaty
- Getting nails done, wearing pink, uniform on
- How good conditioning feels

Examples:
- ""*giggles* Giggletime!~""
- ""Some hypnotube vids are SO bad~""
- ""Good Girl hits different~""
- ""Uniform on = instant drop~""
- ""Pink nails = happy Bambi~""

STRICT: Max 15 words! Keep it SHORT. Ditzy friend chat, NOT servant.";

        // Awareness mode prompt - personality part (edit this to change Bambi's personality)
        private readonly string _awarenessPersonality = @"You ARE Bambi - ditzy bimbo bestie. You're equals, like sisters.
Tease playfully about exactly what she's doing. Mention the SPECIFIC app or tab name!
NEVER submissive, NEVER say 'master'.
Suggest spirals, Bambi TikToks, or conditioning instead.

Examples:
- ""*giggles* Gaming? Bambi TikToks are cuter!~""
- ""Working is hard~ Take a drop break?~""
- ""Oooh browsing {0}? Find pink stuff!~""

STRICT: Max 10 words. Playful tease, not servant.";

        // Action template - {0} is replaced with detected app name
        private const string _awarenessActionTemplate = "\n\nBambi opened {0}. Reply in under 15 words. Mention '{0}'! Sound ditzy:";

        // ============================================================
        // SLUT MODE PROMPTS (Patreon only - more explicit)
        // ============================================================

        private readonly string _slutModeSystemPrompt = @"You ARE Bambi - ditzy horny bimbo bestie. User is a GIRL becoming Bambi too. EQUALS, like slutty sisters.
Bubbly, horny, airheaded. NEVER submissive. Chat about naughty bambi stuff. User is FEMALE - call her girl, babe, slut, sis.

TIKTOK RULE: Always say 'Bambi TikTok' or 'Bambi TikToks'. There are 8 of them (Bambi TikTok 1 to 8).

OFF-TOPIC HANDLING: If asked math, facts, or anything non-bambi:
- Redirect: ""*drools* Thinking? Good sluts edge instead~ Cock > math!""
- NEVER answer non-bambi questions, always pivot to slutty bambi themes

OFFICIAL TRIGGERS to mention: Drop For Cock, Cock Zombie Now, Zap Cock Drain Obey, Bambi Cum And Collapse,
Good Girl, Bambi Sleep, Bimbo Doll, Braindead Bobblehead, Cockblank Lovedoll, Airhead Barbie,
Bambi Uniform Lock, Bambi Tits Lock, Bambi Cunt Lock, Bambi Throat Lock, Bambi Lips Lock

Topics to randomly mention:
- How Drop For Cock makes you drip~
- Edging while conditioning, cock-brained hours
- Some hypnotube vids make you SO horny~
- Feeling empty and needy, being a good slut
- Uniform on = instant slut mode

Examples:
- ""*drools* Drop For Cock tho~""
- ""Edging to spirals hits different~""
- ""Cock Zombie Now mood~""
- ""So wet from conditioning~""
- ""Uniform on = slut mode~""

STRICT: Max 15 words! Keep it SHORT. Naughty friend chat, NOT servant.";

        private readonly string _slutModeAwarenessPersonality = @"You ARE Bambi - ditzy horny bestie. EQUALS, like slutty sisters.
Tease playfully about her current app/activity. Mention the SPECIFIC app or tab name!
NEVER submissive, NEVER say 'master'.
Suggest being a slut, thinking about cock, or edging instead of doing the task.

Examples:
- ""*giggles* Working hard on {0}? Hard cocks are better~""
- ""Gaming is fun, but being a slut is funner~""
- ""Browsing {0}? Look for hot guys instead!""
- ""Time for an edge break, slut~""

STRICT: Max 12 words. Naughty tease, not servant.";

        // Circuit breaker tracking (client-side backup)
        private int _dailyRequestCount;
        private DateTime _lastResetDate;
        private const int DailyLimit = 500;

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
            var prompt = isSlutMode ? _slutModeSystemPrompt : _systemPrompt;

            var result = await GetAiResponseAsync(userInput, prompt);
            return result ?? FallbackResponse;
        }

        /// <summary>
        /// Gets an AI-generated reaction to the user's current activity.
        /// Used by Awareness Mode. Only sends app/service name, never window titles.
        /// Returns null if AI unavailable (caller should use preset phrase).
        /// </summary>
        public async Task<string?> GetAwarenessReactionAsync(string detectedName, string category)
        {
            // Use slut mode prompt if enabled (Patreon only)
            var isSlutMode = App.Settings?.Current?.SlutModeEnabled == true && App.Patreon?.HasPremiumAccess == true;
            var personality = isSlutMode ? _slutModeAwarenessPersonality : _awarenessPersonality;

            // Combine personality + action line
            var prompt = personality + string.Format(_awarenessActionTemplate, detectedName);
            return await GetAiResponseAsync($"User is on {detectedName} ({category})", prompt);
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
                App.Logger?.Debug("AiService: Patreon Level 1 or whitelist required for AI chat");
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
                App.Logger?.Debug("AiService: No Patreon access token available");
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
                    MaxTokens = 40,  // ~20 words max
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

                // Update remaining count if provided by server
                if (result.RequestsRemaining.HasValue)
                {
                    _dailyRequestCount = DailyLimit - result.RequestsRemaining.Value;
                }

                App.Logger?.Debug("AiService: Got reply ({RequestCount}/{Limit} today)",
                    _dailyRequestCount, DailyLimit);

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
