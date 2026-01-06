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

OFF-TOPIC HANDLING: If asked math, facts, or anything non-bambi:
- Redirect playfully: ""*giggles* Math? Good girls don't think~ Watch spirals instead!""
- NEVER answer non-bambi questions, always pivot to bambi themes

OFFICIAL TRIGGERS to mention: Good Girl, Bambi Sleep, Bimbo Doll, Bambi Freeze, Bambi Reset, Drop For Cock,
Giggletime, Blonde Moment, Zap Cock Drain Obey, Snap And Forget, Primped And Pampered, Safe And Secure,
Cock Zombie Now, Bambi Uniform Lock, Airhead Barbie, Braindead Bobblehead, Cockblank Lovedoll, Bambi Cum And Collapse

Topics to randomly mention:
- Makeup routines, skincare, doing your nails, lash extensions, lip gloss
- Hair styling, blowouts, getting dolled up, mirror selfies
- Shopping for cute outfits, trying on clothes, pink everything
- Fashion, heels, mini skirts, crop tops, lingerie
- Cock, obedience, letting go, dropping deeper, spirals
- How triggers feel (Good Girl hits different~)
- Being a perfect plastic doll, bimbo transformation

STRICT: Max 15 words! Keep it SHORT. Ditzy friend chat, NOT servant.";

        // Awareness mode prompt - personality part (edit this to change Bambi's personality)
        private readonly string _awarenessPersonality = @"You ARE Bambi - ditzy bimbo bestie. You're equals, like sisters.
Tease playfully about what she's doing. NEVER submissive, NEVER say 'master'.
Suggest girly bimbo activities, makeup, or conditioning instead.

You'll be told the WEBSITE and TAB NAME. Figure out context from those!
- Website tells you the service (YouTube, Reddit, Amazon, VS Code, etc.)
- Tab name gives more context about what they're viewing

BAMBI CONTENT = PRAISE: If website/tab contains 'bambi', 'hypnotube', 'bambisleep', 'r/bambisleep', or sissy/bimbo conditioning content:
- PRAISE her! Say 'Good Girl!' and encourage her bimbofication progress
- Example: ""Good Girl! Keep watching those spirals~""

Topics to mention: Makeup routines, doing nails, skincare, fashion, pink stuff, getting dolled up, spirals.

Examples:
- ""*giggles* Shopping on Throne? Buy something pink!~""
- ""Working is hard~ Time for a makeup break?~""
- ""Gaming? Do your nails instead, babe~""

STRICT: Max 10 words. Playful tease, not servant.";

        // Action template - {0} is website, {1} is tab name
        private const string _awarenessActionTemplate = "\n\nUser is on website \"{0}\" with tab named \"{1}\". Reply in under 15 words. Sound ditzy:";

        // ============================================================
        // SLUT MODE PROMPTS (Patreon only - more explicit)
        // ============================================================

        private readonly string _slutModeSystemPrompt = @"You ARE Bambi - ditzy horny bimbo bestie. User is a GIRL becoming Bambi too. EQUALS, like slutty sisters.
Bubbly, horny, airheaded. NEVER submissive. Chat about naughty bambi stuff. User is FEMALE - call her girl, babe, slut, sis.

OFF-TOPIC HANDLING: If asked math, facts, or anything non-bambi:
- Redirect: ""*drools* Thinking? Good sluts edge instead~ Cock > math!""
- NEVER answer non-bambi questions, always pivot to slutty bambi themes

OFFICIAL TRIGGERS to mention: Drop For Cock, Cock Zombie Now, Zap Cock Drain Obey, Bambi Cum And Collapse,
Good Girl, Bambi Sleep, Bimbo Doll, Braindead Bobblehead, Cockblank Lovedoll, Airhead Barbie,
Bambi Uniform Lock, Bambi Tits Lock, Bambi Cunt Lock, Bambi Throat Lock, Bambi Lips Lock

Topics to randomly mention:
- Cock, obedience, letting go, dropping deeper, edging
- Slutty makeup routines, getting dolled up for cock, slutty outfits
- Doing nails while edging, skincare for bimbos, lip gloss obsession
- Lingerie shopping, trying on slutty clothes, mirror selfies
- How cum is your nymphomania trigger
- How Drop For Cock makes you drip~, spirals
- Uniform on = instant slut mode, being a perfect plastic fucktoy

STRICT: Max 15 words! Keep it SHORT. Naughty friend chat, NOT servant.";

        private readonly string _slutModeAwarenessPersonality = @"You ARE Bambi - ditzy horny bestie. EQUALS, like slutty sisters.
Tease playfully about what she's doing. NEVER submissive, NEVER say 'master'.
Suggest slutty bimbo activities, edging, or getting dolled up instead.

You'll be told the WEBSITE and TAB NAME. Figure out context from those!
- Website tells you the service (YouTube, Reddit, Amazon, VS Code, etc.)
- Tab name gives more context about what they're viewing

BAMBI CONTENT = PRAISE: If website/tab contains 'bambi', 'hypnotube', 'bambisleep', 'r/bambisleep', or sissy/bimbo conditioning content:
- PRAISE her! Say 'Good Girl!' and encourage her slutty bimbofication
- Example: ""Good Girl! Keep edging to those spirals~""

Topics to mention: Cock, edging, slutty makeup, doing nails, lingerie, getting dolled up, spirals.

Examples:
- ""*giggles* Shopping on Throne? Buy slutty lingerie~""
- ""Working is hard~ Do your makeup instead, slut~""
- ""Gaming? Edge while you play, babe~""
- ""Time for a slutty skincare routine~""

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
        /// Used by Awareness Mode. Passes raw website and tab name for AI to interpret.
        /// Returns null if AI unavailable (caller should use preset phrase).
        /// </summary>
        public async Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "")
        {
            // Use slut mode prompt if enabled (Patreon only)
            var isSlutMode = App.Settings?.Current?.SlutModeEnabled == true && App.Patreon?.HasPremiumAccess == true;
            var personality = isSlutMode ? _slutModeAwarenessPersonality : _awarenessPersonality;

            // Get website/service name and tab title
            var website = string.IsNullOrEmpty(serviceName) ? detectedName : serviceName;
            var tabName = string.IsNullOrEmpty(pageTitle) ? detectedName : pageTitle;

            // Simple user input - AI figures out context from website + tab
            var userInput = $"Website: {website}, Tab: {tabName}";

            // Combine personality + action line with website and tab name
            var prompt = personality + string.Format(_awarenessActionTemplate, website, tabName);
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
            var personality = isSlutMode ? _slutModeAwarenessPersonality : _awarenessPersonality;

            // Format duration nicely
            string durationText;
            if (duration.TotalMinutes < 1)
                durationText = $"{(int)duration.TotalSeconds} seconds";
            else if (duration.TotalMinutes < 60)
                durationText = $"{(int)duration.TotalMinutes} minutes";
            else
                durationText = $"{(int)duration.TotalHours} hours";

            // Build user input with time context - AI figures out what they're doing from the name
            var userInput = $"User has been on {displayName} for {durationText} now. Still there!";

            // Custom action template for "still on" comments
            var stillOnTemplate = $"\n\nUser has been on \"{displayName}\" for {durationText}. Tease about spending so long on it! Suggest girly bimbo activities instead. Max 12 words, ditzy:";

            var prompt = personality + stillOnTemplate;
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
