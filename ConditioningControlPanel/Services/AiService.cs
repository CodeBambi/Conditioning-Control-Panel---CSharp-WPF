using System;
using System.Collections.Generic;
using System.Linq;
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
        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
        {
#pragma warning disable CS0618 // legacy internals: the adapter layer is one level up, in AiServiceStrategy
            var result = await GetBambiReplyExAsync(userInput, isUserMessage);
#pragma warning restore CS0618
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
        [Obsolete(AiLegacyApi.OneShotObsolete)]
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
            var result = await GetAiResponseAsync(userInput, prompt, returnRefusalSentinel: true, purpose: AiMeter.PurposeChat);

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
        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "", TimeSpan? duration = null)
        {
            // Get prompt from active personality preset
            var prompt = _bambiSprite.GetSystemPrompt();

            // Context tag: [Category: X | App: Y | Title: Z | Duration: Nm]. Shared with the other
            // two providers via FrameFormatter — this used to be three hand-copied f-strings.
            var userInput = FrameFormatter.AwarenessFrame(detectedName, category, serviceName, pageTitle, duration);

            return await GetAiResponseAsync(userInput, prompt, purpose: AiMeter.PurposeAwareness);
        }

        /// <summary>
        /// Gets an AI-generated "still on" reaction when user has been on the same activity for a while.
        /// Includes time context for the AI to reference.
        /// </summary>
        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
        {
            // Get prompt from active personality preset
            var prompt = _bambiSprite.GetSystemPrompt();
            var userInput = FrameFormatter.StillOnFrame(displayName, category, duration);

            return await GetAiResponseAsync(userInput, prompt, purpose: AiMeter.PurposeStillOn);
        }

        /// <summary>
        /// Gets an AI-generated reaction line when a configured keyword trigger fires.
        /// Used by <see cref="KeywordTriggerService"/>'s AvatarCommentAction dispatch.
        /// Returns null if AI is unavailable (caller is expected to use a canned phrase).
        /// </summary>
        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
        {
            if (!IsAvailable) return null;

            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = FrameFormatter.KeywordFrame(keyword, promptTemplate);

            return await GetAiResponseAsync(userInput, systemPrompt, purpose: AiMeter.PurposeKeyword);
        }

        /// <summary>
        /// Gets an AI-generated reaction after the user finishes a lock-screen mantra.
        /// Returns null if AI unavailable.
        /// </summary>
        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
        {
            if (!IsAvailable) return null;

            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = FrameFormatter.LockScreenFrame(sentance, mistakes, amount, promptTemplate);

            return await GetAiResponseAsync(userInput, systemPrompt, purpose: AiMeter.PurposeLockScreen);
        }

        /// <summary>
        /// Gets an AI-generated reaction after a mandatory video finishes.
        /// Returns null if AI unavailable.
        /// </summary>
        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
        {
            if (!IsAvailable) return null;

            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = FrameFormatter.VideoDoneFrame(title, promptTemplate);

            return await GetAiResponseAsync(userInput, systemPrompt, purpose: AiMeter.PurposeVideoDone);
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
        private async Task<string?> GetAiResponseAsync(string userInput, string systemPrompt, bool returnRefusalSentinel = false,
            string purpose = AiMeter.PurposeChat)
        {
            // Check offline mode first
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("AiService: Offline mode enabled, skipping AI request");
                return null;
            }

            // [AI-METER] — log-only sizing. Emitted once per request attempt (and for a
            // moderation-refused input, which is a request we deliberately didn't make).
            // The paths that never reach the wire at all — offline, no access, daily limit —
            // are not requests and stay silent.
            var meterStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var meterInputChars = (systemPrompt?.Length ?? 0) + (userInput?.Length ?? 0);
            void Meter(string outcome, int outputChars = 0) =>
                AiMeter.Record(AiMeter.ProviderCloud, purpose, meterInputChars, outputChars,
                    meterStopwatch.ElapsedMilliseconds, outcome);

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
                    Meter(AiMeter.OutcomeRefusedInput);
                    return returnRefusalSentinel ? ModerationRefusal.InputSentinel : null;
                }
                // ProfessionalAdvice is soft (Allow=true with Category set) — log only.
                if (inputCheck.Allow && inputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                {
                    App.ModerationLog?.Record(ProhibitedCategory.ProfessionalAdvice, source: "input", modelHint: "cloud");
                }
            }

            // Build messages array
            var messages = new[]
            {
                new ProxyChatMessage { Role = "system", Content = systemPrompt },
                new ProxyChatMessage { Role = "user", Content = userInput }
            };

            var post = await PostToProxyAsync(messages, MaxTokensHardCap, 0.7, purposeWire: null);
            // Skipped = never reached the wire (no access / daily limit): not a request, stays
            // silent in the meter, exactly as before the Train 1 extraction.
            if (post.Outcome == ProxyOutcome.Skipped) return null;
            if (post.Outcome != ProxyOutcome.Ok)
            {
                Meter(post.Outcome == ProxyOutcome.Empty ? AiMeter.OutcomeEmpty : AiMeter.OutcomeError);
                return null;
            }

            var raw = post.Content!;

            // Sanitize response to remove any leaked metadata tags FIRST (so context-tag
            // echoes don't accidentally trip moderation regexes).
            var sanitized = SanitizeResponse(raw);

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
                    Meter(AiMeter.OutcomeRefusedOutput, raw.Length);
                    return returnRefusalSentinel ? ModerationRefusal.OutputSentinel : null;
                }
                if (outputCheck.Allow && outputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                {
                    App.ModerationLog?.Record(ProhibitedCategory.ProfessionalAdvice, source: "output", modelHint: "cloud");
                }
            }

            Meter(AiMeter.OutcomeOk, raw.Length);
            return sanitized;
        }

        // ===================== Train 1 transport seam =====================

        /// <summary>
        /// <see cref="Skipped"/> means the request never reached the wire (no entitlement, daily
        /// limit) — those are not requests and must stay silent in the [AI-METER] stream.
        /// </summary>
        private enum ProxyOutcome { Ok, Error, Empty, Skipped }

        private sealed record ProxyPostResult(ProxyOutcome Outcome, string? Content);

        /// <summary>
        /// The whole proxy round trip: entitlement gate, daily circuit breaker, V2 auth with the
        /// legacy Patreon-Bearer fallback, error/empty classification and the server-authoritative
        /// remaining-request reconciliation. Extracted in Train 1 so the single-shot legacy path and
        /// the multi-turn <see cref="SendAsync"/> path cannot drift on any of it.
        ///
        /// <paramref name="purposeWire"/> rides along as a top-level <c>purpose</c> field. The proxy
        /// ignores unknown fields today, so sending it is safe before the server deploy that maps it
        /// to a model tier; null omits it entirely (legacy call sites).
        /// </summary>
        private async Task<ProxyPostResult> PostToProxyAsync(ProxyChatMessage[] messages, int maxTokens,
            double temperature, string? purposeWire)
        {
            // Check access (cloud identity or Patreon)
            if (!IsAvailable)
            {
                App.Logger?.Debug("AiService: No AI access - HasCloudIdentity={Cloud}, HasAiAccess={HasAi}",
                    App.HasCloudIdentity, App.Patreon?.HasAiAccess);
                return new ProxyPostResult(ProxyOutcome.Skipped, null);
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
                return new ProxyPostResult(ProxyOutcome.Skipped, null);
            }

            try
            {
                _dailyRequestCount++;

                HttpResponseMessage? response;

                // Try V2 auth first (unified_id + X-Auth-Token) — free for all cloud users
                var unifiedId = App.UnifiedUserId;
                var authToken = App.Settings?.Current?.AuthToken;
                if (!string.IsNullOrEmpty(unifiedId))
                {
                    var v2Request = new V2ChatRequest
                    {
                        UnifiedId = unifiedId,
                        Messages = messages,
                        MaxTokens = maxTokens,
                        Temperature = temperature,
                        Purpose = purposeWire
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
                        response = await SendLegacyRequestAsync(messages, maxTokens, temperature, purposeWire);
                        if (response == null) return new ProxyPostResult(ProxyOutcome.Error, null);
                    }
                }
                else
                {
                    response = await SendLegacyRequestAsync(messages, maxTokens, temperature, purposeWire);
                    if (response == null) return new ProxyPostResult(ProxyOutcome.Error, null);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    App.Logger?.Warning("AiService: Proxy returned {Status}: {Error}",
                        response.StatusCode, errorText);
                    return new ProxyPostResult(ProxyOutcome.Error, null);
                }

                var result = await response.Content.ReadFromJsonAsync<ProxyChatResponse>();

                if (result == null || !string.IsNullOrEmpty(result.Error))
                {
                    App.Logger?.Warning("AiService: Proxy error: {Error}", result?.Error);
                    return new ProxyPostResult(ProxyOutcome.Error, null);
                }

                if (string.IsNullOrEmpty(result.Content))
                {
                    App.Logger?.Warning("AiService: Empty response from proxy");
                    return new ProxyPostResult(ProxyOutcome.Empty, null);
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

                // #739: "companion spits out gibberish" could not be diagnosed from a user's log,
                // because nothing recorded what the model actually returned - only that a reply
                // arrived. One repro with this line settles whether the text was already garbage on
                // arrival (a provider/model problem) or was mangled downstream by our own cleanup.
                App.Logger?.Debug("AiService: raw reply ({Length} chars): {Raw}",
                    result.Content?.Length ?? 0, result.Content ?? "(null)");

                return new ProxyPostResult(ProxyOutcome.Ok, result.Content);
            }
            catch (TaskCanceledException)
            {
                App.Logger?.Warning("AiService: Request timed out");
                return new ProxyPostResult(ProxyOutcome.Error, null);
            }
            catch (HttpRequestException ex)
            {
                App.Logger?.Warning(ex, "AiService: Network error");
                return new ProxyPostResult(ProxyOutcome.Error, null);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "AiService: Failed to get AI reply");
                return new ProxyPostResult(ProxyOutcome.Error, null);
            }
        }

        /// <summary>
        /// Multi-turn transport. See <see cref="IAiService.SendAsync"/> for the contract.
        ///
        /// The proxy already accepts up to 50 <c>messages[]</c> and trims the history to its own
        /// ~14k-char budget server-side, forwarding the array as-is — so multi-turn cloud chat works
        /// against today's deployment with zero server changes. The client still self-caps its window
        /// (see <c>ChatSession</c>) so we never rely on that trim for correctness.
        /// </summary>
        public async Task<AiReplyResult> SendAsync(IReadOnlyList<ChatMessage> messages, AiCallOptions options,
            System.Threading.CancellationToken cancellationToken = default)
        {
            options ??= AiCallOptions.Chat;
            var wire = (messages ?? Array.Empty<ChatMessage>())
                .Select(m => new ProxyChatMessage { Role = m.Role, Content = m.Content ?? string.Empty })
                .ToArray();

            var meterStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var meterInputChars = wire.Sum(m => m.Content?.Length ?? 0);
            void Meter(string outcome, int outputChars = 0) =>
                AiMeter.Record(AiMeter.ProviderCloud, options.MeterPurpose, meterInputChars, outputChars,
                    meterStopwatch.ElapsedMilliseconds, outcome);

            AiReplyResult Canned() => new(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("AiService.SendAsync: offline mode, skipping AI request");
                return Canned();
            }

            // INPUT MODERATION (Layer 1). Only the newest user-role message is user-authored input
            // we haven't seen before; earlier turns already passed the guard when they were sent,
            // and re-checking them would double-log the compliance record.
            var newestUser = wire.LastOrDefault(m => m.Role == ChatMessage.RoleUser)?.Content ?? string.Empty;
            var guard = App.ModerationGuard;
            if (guard != null)
            {
                var inputCheck = guard.CheckInput(newestUser);
                if (!inputCheck.Allow && inputCheck.Category.HasValue)
                {
                    App.ModerationLog?.Record(inputCheck.Category.Value, source: "input", modelHint: "cloud");
                    // Escalate the user-facing Content Policy Notice only for text the user typed.
                    if (options.Interactive)
                        App.ModerationCounter?.RecordHit(inputCheck.Category.Value, "input:cloud");
                    App.Logger?.Information("AiService.SendAsync: input blocked by ModerationGuard (category={Cat})", inputCheck.Category);
                    Meter(AiMeter.OutcomeRefusedInput);
                    return new AiReplyResult(string.Empty, IsAiGenerated: false,
                        Refusal: new ModerationRefusalInfo(inputCheck.Category, ModerationSource.Input));
                }
                if (inputCheck.Allow && inputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                {
                    App.ModerationLog?.Record(ProhibitedCategory.ProfessionalAdvice, source: "input", modelHint: "cloud");
                }
            }

            if (!IsAvailable)
            {
                App.Logger?.Debug("AiService.SendAsync: AI not available — user needs to log in for AI chat");
                return new AiReplyResult(Loc.Get("ai_login_required_hint"), IsAiGenerated: false, Refusal: null);
            }

            // MaxTokensHardCap is the client's cost ceiling; the server clamps again per purpose.
            var maxTokens = Math.Clamp(options.MaxTokens, 1, MaxTokensHardCap);
            var post = await PostToProxyAsync(wire, maxTokens, options.Temperature, options.PurposeWire);
            if (post.Outcome == ProxyOutcome.Skipped) return Canned();
            if (post.Outcome != ProxyOutcome.Ok)
            {
                Meter(post.Outcome == ProxyOutcome.Empty ? AiMeter.OutcomeEmpty : AiMeter.OutcomeError);
                return Canned();
            }

            var raw = post.Content!;
            var sanitized = SanitizeResponse(raw);

            // OUTPUT MODERATION (Layer 1). Prohibited model output is discarded before display;
            // the caller (CompanionBrain) rolls the turn back so it never reaches disk (P2/H5).
            if (guard != null)
            {
                var outputCheck = guard.CheckOutput(sanitized ?? string.Empty);
                if (!outputCheck.Allow && outputCheck.Category.HasValue)
                {
                    App.ModerationLog?.Record(outputCheck.Category.Value, source: "output", modelHint: "cloud");
                    // Model output tripping the filter is not the user's doing — no counter hit.
                    App.Logger?.Information("AiService.SendAsync: output blocked by ModerationGuard (category={Cat})", outputCheck.Category);
                    Meter(AiMeter.OutcomeRefusedOutput, raw.Length);
                    return new AiReplyResult(string.Empty, IsAiGenerated: false,
                        Refusal: new ModerationRefusalInfo(outputCheck.Category, ModerationSource.Output));
                }
                if (outputCheck.Allow && outputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                {
                    App.ModerationLog?.Record(ProhibitedCategory.ProfessionalAdvice, source: "output", modelHint: "cloud");
                }
            }

            Meter(AiMeter.OutcomeOk, raw.Length);
            return new AiReplyResult(sanitized, IsAiGenerated: true, Refusal: null);
        }

        /// <summary>
        /// Sanitizes AI response by removing any leaked internal metadata tags.
        /// The AI sometimes echoes context tags that should be hidden from users.
        /// </summary>
        private static string SanitizeResponse(string? response)
        {
            if (string.IsNullOrEmpty(response))
                return response ?? string.Empty;

            // #739: reasoning blocks and tokenizer artifacts first. The cloud path had no such
            // cleanup at all, so a reasoning model's scratchpad rendered verbatim into the bubble.
            response = AiTextHygiene.Clean(response);

            // Remove context metadata tags like [Category: X | App: Y | Title: Z | Duration: Nm],
            // reaction tags like [Media/Streaming], and the truncated variants the 100-token cap
            // produces. Shared with the parser path so both stay in step.
            var sanitized = AiTextHygiene.StripMetadataTags(response);

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
        private async Task<HttpResponseMessage?> SendLegacyRequestAsync(ProxyChatMessage[] messages,
            int maxTokens, double temperature, string? purposeWire)
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
                MaxTokens = maxTokens,
                Temperature = temperature,
                Purpose = purposeWire
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
