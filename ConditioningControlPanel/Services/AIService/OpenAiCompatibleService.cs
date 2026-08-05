

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.AiEnrichment;
using ConditioningControlPanel.Services.AIService.Enrichment;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// IAiService implementation that talks to an OpenAI-compatible chat completions endpoint.
    /// Uses the same BambiSprite system prompt and awareness formatting as the cloud provider,
    /// but sends requests directly to a user-configured HTTP endpoint with a bearer API key.
    /// Daily limits are controlled via CompanionPromptSettings.DailyRequestLimit (0 = unlimited).
    ///
    /// When AI Companion Effects are enabled, the request includes an enrichment message that
    /// instructs the model to emit effect commands in a JSON wrapper. Replies are parsed by the
    /// same <see cref="AiResponseParser"/> used by the local provider, and any valid commands are
    /// executed through <see cref="App.Commands"/>.
    /// </summary>
    public sealed class OpenAiCompatibleService : IAiService
    {
        public enum DiagnosticCategory
        {
            Success,
            MissingConfiguration,
            Endpoint,
            Authentication,
            Model,
            Timeout,
            Connection,
            Http,
            Unknown
        }

        public sealed record ConnectionDiagnosticResult(
            bool Success,
            DiagnosticCategory Category,
            string Message,
            int? HttpStatusCode = null,
            long? ElapsedMs = null);

        private readonly HttpClient _httpClient;
        private readonly BambiSprite _bambiSprite;
        private readonly IAiResponseParser _parser;
        private readonly KnowledgeService _knowledgeService;
        private readonly IPromptService _promptService;

        private int _dailyRequestCount;
        private DateTime _lastResetDate;

        private static CompanionPromptSettings? Settings => App.Settings?.Current?.CompanionPrompt;

        public bool IsAvailable
        {
            get
            {
                if (App.Settings?.Current?.OfflineMode == true) return false;

                var s = Settings;
                if (s == null) return false;

                if (string.IsNullOrWhiteSpace(s.OpenAiCompatibleEndpoint)) return false;
                if (string.IsNullOrWhiteSpace(s.OpenAiCompatibleApiKey)) return false;

                ResetDailyCounterIfNeeded();

                var limit = s.DailyRequestLimit;
                if (limit <= 0) return true; // unlimited

                return _dailyRequestCount < limit;
            }
        }

        public int DailyRequestsRemaining
        {
            get
            {
                var s = Settings;
                if (s == null) return 0;

                ResetDailyCounterIfNeeded();

                var limit = s.DailyRequestLimit;
                if (limit <= 0) return -1; // unlimited

                var remaining = limit - _dailyRequestCount;
                return remaining < 0 ? 0 : remaining;
            }
        }

        public OpenAiCompatibleService()
        {
            _bambiSprite = new BambiSprite();
            _parser = new AiResponseParser(GetFallbackResponse);
            _knowledgeService = new KnowledgeService();
            _promptService = new PromptService();
            _lastResetDate = DateTime.Today;
            _dailyRequestCount = 0;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        private static Uri GetConfiguredEndpointBaseUri()
        {
            var raw = Settings?.OpenAiCompatibleEndpoint?.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                // Fallback: standard OpenAI base, though without a key it will not be available.
                return new Uri("https://api.openai.com/v1/");
            }

            if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed))
            {
                App.Logger?.Warning("OpenAiCompatibleService: invalid endpoint '{Endpoint}', falling back to OpenAI base", raw);
                return new Uri("https://api.openai.com/v1/");
            }

            // Some users paste the full chat-completions URL. Normalize to its base so
            // both ".../api/v1" and ".../api/v1/" (and full endpoint forms) work.
            var path = parsed.AbsolutePath.TrimEnd('/');
            if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - "/chat/completions".Length);
            }

            if (string.IsNullOrEmpty(path))
            {
                path = "/";
            }

            if (!path.EndsWith("/", StringComparison.Ordinal))
            {
                path += "/";
            }

            var builder = new UriBuilder(parsed)
            {
                Path = path,
                Query = string.Empty,
                Fragment = string.Empty
            };

            return builder.Uri;
        }

        private static string GetConfiguredModel()
        {
            var model = Settings?.OpenAiCompatibleModel;
            if (string.IsNullOrWhiteSpace(model))
            {
                // Reasonable default; kept here only as a suggestion.
                return "gpt-4o-mini";
            }

            return model;
        }

        /// <summary>
        /// Model identifier recorded in moderation.log. Mirrors the "local:{model}" shape the
        /// Ollama provider uses so the compliance record says which endpoint produced the hit.
        /// </summary>
        private static string ModelHint()
        {
            var model = GetConfiguredModel();
            return "openai_compat:" + (string.IsNullOrWhiteSpace(model) ? "unknown" : model);
        }

        private static string? GetApiKey()
        {
            var raw = Settings?.OpenAiCompatibleApiKey;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                // Value is stored encrypted at rest; decrypt on use.
                return SecureStringHelper.Unprotect(raw);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "OpenAiCompatibleService: failed to decrypt API key");
                return null;
            }
        }

        private static void ApplySamplerSettings(Dictionary<string, object> payload)
        {
            var s = Settings;
            if (s == null || !s.OpenAiCompatibleUseCustomSamplerSettings) return;

            if (s.OpenAiCompatibleTemperature.HasValue) payload["temperature"] = s.OpenAiCompatibleTemperature.Value;
            if (s.OpenAiCompatibleTopP.HasValue) payload["top_p"] = s.OpenAiCompatibleTopP.Value;
            if (s.OpenAiCompatibleTopK.HasValue) payload["top_k"] = s.OpenAiCompatibleTopK.Value;
            if (s.OpenAiCompatibleFrequencyPenalty.HasValue) payload["frequency_penalty"] = s.OpenAiCompatibleFrequencyPenalty.Value;
            if (s.OpenAiCompatiblePresencePenalty.HasValue) payload["presence_penalty"] = s.OpenAiCompatiblePresencePenalty.Value;
            if (s.OpenAiCompatibleRepetitionPenalty.HasValue) payload["repetition_penalty"] = s.OpenAiCompatibleRepetitionPenalty.Value;
            if (s.OpenAiCompatibleMinP.HasValue) payload["min_p"] = s.OpenAiCompatibleMinP.Value;
        }

        /// <summary>#739: now a thin alias over the shared helper. This cleanup used to live only
        /// here, so the cloud and local providers never got it despite hitting the same models.</summary>
        private static string CleanTokenizerArtifacts(string? text) => AiTextHygiene.Clean(text);

        public async Task<ConnectionDiagnosticResult> TestEndpointAsync(CancellationToken cancellationToken = default)
        {
            var endpointRaw = Settings?.OpenAiCompatibleEndpoint?.Trim();
            if (string.IsNullOrWhiteSpace(endpointRaw))
            {
                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.MissingConfiguration,
                    Message: "Endpoint is missing");
            }

            var model = GetConfiguredModel();
            if (string.IsNullOrWhiteSpace(model))
            {
                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.MissingConfiguration,
                    Message: "Model is missing");
            }

            var apiKey = GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.Authentication,
                    Message: "API key is missing or could not be decrypted");
            }

            if (!Uri.TryCreate(endpointRaw, UriKind.Absolute, out _))
            {
                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.Endpoint,
                    Message: "Endpoint URL is invalid");
            }

            var baseUri = GetConfiguredEndpointBaseUri();
            var endpointUri = new Uri(baseUri, "chat/completions");

            var payload = new Dictionary<string, object>
            {
                ["model"] = model,
                ["max_tokens"] = 1,
                ["temperature"] = 0,
                ["messages"] = new[]
                {
                    new { role = "user", content = "ping" }
                }
            };
            ApplySamplerSettings(payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                sw.Stop();

                if (response.IsSuccessStatusCode)
                {
                    return new ConnectionDiagnosticResult(
                        Success: true,
                        Category: DiagnosticCategory.Success,
                        Message: "Connected",
                        HttpStatusCode: (int)response.StatusCode,
                        ElapsedMs: sw.ElapsedMilliseconds);
                }

                var status = (int)response.StatusCode;
                var bodyLower = body?.ToLowerInvariant() ?? string.Empty;

                if (status == 401 || status == 403)
                {
                    return new ConnectionDiagnosticResult(
                        Success: false,
                        Category: DiagnosticCategory.Authentication,
                        Message: "Authentication failed (invalid API key or unauthorized endpoint)",
                        HttpStatusCode: status,
                        ElapsedMs: sw.ElapsedMilliseconds);
                }

                if (status == 404)
                {
                    return new ConnectionDiagnosticResult(
                        Success: false,
                        Category: DiagnosticCategory.Endpoint,
                        Message: "Endpoint not found (check base URL path, e.g. /api/v1)",
                        HttpStatusCode: status,
                        ElapsedMs: sw.ElapsedMilliseconds);
                }

                if (status == 400 && (bodyLower.Contains("model") || bodyLower.Contains("unknown_model") || bodyLower.Contains("not found")))
                {
                    return new ConnectionDiagnosticResult(
                        Success: false,
                        Category: DiagnosticCategory.Model,
                        Message: "Model is invalid or unavailable on this endpoint",
                        HttpStatusCode: status,
                        ElapsedMs: sw.ElapsedMilliseconds);
                }

                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.Http,
                    Message: $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}",
                    HttpStatusCode: status,
                    ElapsedMs: sw.ElapsedMilliseconds);
            }
            catch (TaskCanceledException)
            {
                sw.Stop();
                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.Timeout,
                    Message: "Request timed out",
                    ElapsedMs: sw.ElapsedMilliseconds);
            }
            catch (HttpRequestException ex)
            {
                sw.Stop();
                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.Connection,
                    Message: $"Connection failed: {ex.Message}",
                    ElapsedMs: sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.Unknown,
                    Message: $"Unexpected error: {ex.GetType().Name}",
                    ElapsedMs: sw.ElapsedMilliseconds);
            }
        }

        private void ResetDailyCounterIfNeeded()
        {
            if (DateTime.Today <= _lastResetDate) return;

            _dailyRequestCount = 0;
            _lastResetDate = DateTime.Today;
            App.Logger?.Debug("OpenAiCompatibleService: Daily request count reset");
        }

        private void BumpDailyCounter()
        {
            ResetDailyCounterIfNeeded();
            _dailyRequestCount++;
        }

        /// <summary>
        /// Core request path.
        ///
        /// Moderation: if <paramref name="returnRefusalSentinel"/> is true and the input or
        /// output trips <see cref="App.ModerationGuard"/>, returns the appropriate
        /// <see cref="ModerationRefusal"/> sentinel string so the chat UI can render the
        /// refusal bubble + POLICY badge. When false (awareness, keyword, lockscreen, video
        /// paths) a moderation hit returns null and the caller silently drops the reaction —
        /// surfacing a refusal there would be jarring (user didn't actively prompt).
        /// </summary>
        private async Task<string?> SendChatAsync(string systemPrompt, string userInput, bool returnRefusalSentinel = false,
            string purpose = AiMeter.PurposeChat)
        {
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("OpenAiCompatibleService: Offline mode enabled, skipping AI request");
                return null;
            }

            // [AI-METER] — log-only sizing, one line per request attempt (plus refused input,
            // a request we deliberately didn't make). Missing-key and daily-limit bails never
            // reach the wire and stay silent. Refined to the real message list below.
            var meterStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var meterInputChars = (systemPrompt?.Length ?? 0) + (userInput?.Length ?? 0);
            void Meter(string outcome, int outputChars = 0) =>
                AiMeter.Record(AiMeter.ProviderOpenAiCompatible, purpose, meterInputChars, outputChars,
                    meterStopwatch.ElapsedMilliseconds, outcome);

            // INPUT MODERATION (Layer 1 — code-side, prompt cannot bypass). Runs BEFORE the
            // HTTP request so prohibited inputs never leave the client. Same semantics as the
            // cloud and local providers.
            var guard = App.ModerationGuard;
            if (guard != null)
            {
                var inputCheck = guard.CheckInput(userInput ?? string.Empty);
                if (!inputCheck.Allow && inputCheck.Category.HasValue)
                {
                    App.ModerationLog?.Record(inputCheck.Category.Value, source: "input", modelHint: ModelHint());
                    // Only escalate the user-facing Content Policy Notice for content the user
                    // actually typed (interactive chat path). Background/auto reactions leave
                    // returnRefusalSentinel false and must not pop the warning — that filtering
                    // is "on us, not on them". Logged above for compliance either way.
                    if (returnRefusalSentinel)
                        App.ModerationCounter?.RecordHit(inputCheck.Category.Value, "input:openai_compat");
                    App.Logger?.Information("OpenAiCompatibleService: input blocked by ModerationGuard (category={Cat})", inputCheck.Category);
                    Meter(AiMeter.OutcomeRefusedInput);
                    return returnRefusalSentinel ? ModerationRefusal.InputSentinel : null;
                }
                // ProfessionalAdvice is soft (Allow=true with Category set) — log only.
                if (inputCheck.Allow && inputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                {
                    App.ModerationLog?.Record(ProhibitedCategory.ProfessionalAdvice, source: "input", modelHint: ModelHint());
                }
            }

            var apiKey = GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                App.Logger?.Debug("OpenAiCompatibleService: missing API key");
                return null;
            }

            ResetDailyCounterIfNeeded();
            var limit = Settings?.DailyRequestLimit ?? 0;
            if (limit > 0 && _dailyRequestCount >= limit)
            {
                App.Logger?.Debug("OpenAiCompatibleService: daily limit reached ({Limit})", limit);
                return null;
            }

            var model = GetConfiguredModel();
            var messages = BuildMessages(systemPrompt, userInput);
            meterInputChars = messages.Sum(m => m.Content?.Length ?? 0);

            var payload = new Dictionary<string, object>
            {
                ["model"] = model,
                ["messages"] = messages
            };
            ApplySamplerSettings(payload);

            var baseUri = GetConfiguredEndpointBaseUri();
            var endpointUri = new Uri(baseUri, "chat/completions");

            BumpDailyCounter();

            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                    };
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                    App.Logger?.Debug("OpenAiCompatibleService: request to {Url} (attempt {Attempt})", request.RequestUri, attempt + 1);

                    using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var status = (int)response.StatusCode;
                        var retryableStatus = status == 429 || status >= 500;

                        if (attempt == 0 && retryableStatus)
                        {
                            await Task.Delay(1200).ConfigureAwait(false);
                            continue;
                        }

                        App.Logger?.Warning("OpenAiCompatibleService: HTTP {Status} from {Endpoint}: {Body}",
                            status,
                            endpointUri,
                            json);
                        Meter(AiMeter.OutcomeError);
                        return null;
                    }

                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    {
                        App.Logger?.Warning("OpenAiCompatibleService: response has no choices");
                        Meter(AiMeter.OutcomeError);
                        return null;
                    }

                    var first = choices[0];
                    if (!first.TryGetProperty("message", out var message) ||
                        !message.TryGetProperty("content", out var contentElement))
                    {
                        App.Logger?.Warning("OpenAiCompatibleService: response missing message.content");
                        Meter(AiMeter.OutcomeError);
                        return null;
                    }

                    var content = CleanTokenizerArtifacts(contentElement.GetString());
                    var processed = ProcessResponse(content, returnRefusalSentinel, out var outputBlocked);
                    Meter(outputBlocked ? AiMeter.OutcomeRefusedOutput
                            : string.IsNullOrWhiteSpace(processed) ? AiMeter.OutcomeEmpty : AiMeter.OutcomeOk,
                        content?.Length ?? 0);
                    return processed;
                }
                catch (HttpRequestException) when (attempt == 0)
                {
                    await Task.Delay(1200).ConfigureAwait(false);
                }
                catch (TaskCanceledException) when (attempt == 0)
                {
                    await Task.Delay(1200).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "OpenAiCompatibleService: request failed");
                    Meter(AiMeter.OutcomeError);
                    return null;
                }
            }

            App.Logger?.Warning("OpenAiCompatibleService: request failed after retry");
            Meter(AiMeter.OutcomeError);
            return null;
        }

        private List<MessageDto> BuildMessages(string systemPrompt, string userInput)
        {
            var messages = new List<MessageDto>
            {
                new("system", systemPrompt),
                new("user", userInput)
            };

            var effectsEnabled = App.Settings?.Current?.CompanionPrompt?.AllowAiToControlEffects == true;
            if (effectsEnabled)
            {
                var currentTime = DateTime.Now.ToString("yyyy-M-dd dddd h:mm:ss tt");
                var facts = _knowledgeService.GetKnowledge("");
                var factsJson = JsonSerializer.Serialize(facts);
                var enrichment = _promptService.BuildEnrichmentMessage(factsJson, currentTime);
                messages.Insert(1, enrichment);
            }

            return messages;
        }

        /// <summary>
        /// OUTPUT MODERATION (Layer 1). Returns true when <paramref name="text"/> is safe to
        /// show. On a block, <paramref name="refusal"/> carries the value the caller must
        /// return — the sentinel on the interactive chat path, null everywhere else.
        /// </summary>
        private static bool PassesOutputModeration(string? text, bool returnRefusalSentinel, out string? refusal)
        {
            refusal = null;

            var guard = App.ModerationGuard;
            if (guard == null) return true;

            var outputCheck = guard.CheckOutput(text ?? string.Empty);
            if (!outputCheck.Allow && outputCheck.Category.HasValue)
            {
                App.ModerationLog?.Record(outputCheck.Category.Value, source: "output", modelHint: ModelHint());
                // Model OUTPUT that trips the filter is never the user's doing, so it does NOT
                // escalate the Content Policy Notice (logged above for compliance only). The
                // warning is reserved for user-typed input.
                App.Logger?.Information("OpenAiCompatibleService: output blocked by ModerationGuard (category={Cat})", outputCheck.Category);
                refusal = returnRefusalSentinel ? ModerationRefusal.OutputSentinel : null;
                return false;
            }
            if (outputCheck.Allow && outputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
            {
                App.ModerationLog?.Record(ProhibitedCategory.ProfessionalAdvice, source: "output", modelHint: ModelHint());
            }

            return true;
        }

        private string? ProcessResponse(string? content, bool returnRefusalSentinel, out bool outputBlocked)
        {
            outputBlocked = false;

            if (string.IsNullOrWhiteSpace(content))
                return null;

            var effectsEnabled = App.Settings?.Current?.CompanionPrompt?.AllowAiToControlEffects == true;
            if (!effectsEnabled)
            {
                // Strip context-tag echoes BEFORE moderation, matching the cloud path's
                // ordering (AiService.SanitizeResponse): an echoed awareness tag carries the
                // user's raw tab title, which must not be able to trip the output guard and
                // write a false model-output hit into the compliance log. The effects branch
                // below gets this for free — Parse() sanitizes CleanText.
                var visible = _parser.SanitizeVisibleText(content);
                if (PassesOutputModeration(visible, returnRefusalSentinel, out var plainRefusal)) return visible;
                outputBlocked = true;
                return plainRefusal;
            }

            var parsed = _parser.Parse(content);
            var commands = parsed.Commands;

            // Moderate the user-visible text (JSON effects wrapper already stripped) BEFORE
            // executing anything, as the local provider does: a blocked turn fires no effects
            // and shows no text.
            if (!PassesOutputModeration(parsed.CleanText, returnRefusalSentinel, out var blockedRefusal))
            {
                outputBlocked = true;
                return blockedRefusal;
            }

            if (commands.Count > 0)
            {
                App.Logger?.Information("OpenAiCompatibleService: parsed {Count} command(s) from response", commands.Count);
                if (App.Commands != null)
                {
                    App.Commands.BeginBatch();
                    foreach (var cmd in commands)
                        App.Commands.ExecuteCommand(cmd);
                }
            }

            return string.IsNullOrWhiteSpace(parsed.CleanText) ? null : parsed.CleanText;
        }

        private static string GetFallbackResponse()
        {
            if (App.Mods?.IsBambiMode == true)
                return "Bambi's head is so empty right now~ *giggles*";
            return "...";
        }

        public async Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
        {
            var result = await GetBambiReplyExAsync(userInput, isUserMessage).ConfigureAwait(false);
            if (result.Refusal != null)
            {
                return result.Refusal.Source == ModerationSource.Input
                    ? ModerationRefusal.InputSentinel
                    : ModerationRefusal.OutputSentinel;
            }
            return result.Text;
        }

        public async Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
        {
            _ = isUserMessage; // queueing semantics are local-only

            if (App.Settings?.Current?.OfflineMode == true)
                return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

            var prompt = _bambiSprite.GetSystemPrompt();
            // Interactive path: a moderation block must surface as a POLICY bubble, not a
            // silent drop, so ask for the refusal sentinel here (and only here).
            var reply = await SendChatAsync(prompt, userInput, returnRefusalSentinel: true, purpose: AiMeter.PurposeChat).ConfigureAwait(false);

            var refusalSource = ModerationRefusal.GetSource(reply);
            if (refusalSource.HasValue)
            {
                // Category was already logged inside SendChatAsync; the sentinel string can't
                // carry it, so we surface only the source here.
                return new AiReplyResult(
                    string.Empty,
                    IsAiGenerated: false,
                    Refusal: new ModerationRefusalInfo(Category: null, Source: refusalSource.Value));
            }

            if (string.IsNullOrWhiteSpace(reply))
                return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

            return new AiReplyResult(reply, IsAiGenerated: true, Refusal: null);
        }

        public async Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "", TimeSpan? duration = null)
        {
            var prompt = _bambiSprite.GetSystemPrompt();

            var website = string.IsNullOrEmpty(serviceName) ? detectedName : serviceName;
            var tabName = string.IsNullOrEmpty(pageTitle) ? detectedName : pageTitle;

            // Same bucketing as the still-on path.
            var elapsed = duration ?? TimeSpan.Zero;
            string durationText;
            if (elapsed.TotalMinutes < 1)
                durationText = $"{(int)elapsed.TotalSeconds}s";
            else if (elapsed.TotalMinutes < 60)
                durationText = $"{(int)elapsed.TotalMinutes}m";
            else
                durationText = $"{(int)elapsed.TotalHours}h";

            var userInput = $"[Category: {category} | App: {website} | Title: {tabName} | Duration: {durationText}]";

            return await SendChatAsync(prompt, userInput, purpose: AiMeter.PurposeAwareness).ConfigureAwait(false);
        }

        public async Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
        {
            var prompt = _bambiSprite.GetSystemPrompt();

            string durationText;
            if (duration.TotalMinutes < 1)
                durationText = $"{(int)duration.TotalSeconds}s";
            else if (duration.TotalMinutes < 60)
                durationText = $"{(int)duration.TotalMinutes}m";
            else
                durationText = $"{(int)duration.TotalHours}h";

            var userInput = $"[Category: {category} | App: {displayName} | Title: {displayName} | Duration: {durationText}]";

            return await SendChatAsync(prompt, userInput, purpose: AiMeter.PurposeStillOn).ConfigureAwait(false);
        }

        public async Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
        {
            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = string.IsNullOrEmpty(promptTemplate)
                ? $"You just caught the user on the word '{keyword}'. React in character, one short line."
                : promptTemplate.Replace("{keyword}", keyword);

            return await SendChatAsync(systemPrompt, userInput, purpose: AiMeter.PurposeKeyword).ConfigureAwait(false);
        }

        public async Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
        {
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

            return await SendChatAsync(systemPrompt, userInput, purpose: AiMeter.PurposeLockScreen).ConfigureAwait(false);
        }

        public async Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
        {
            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = string.IsNullOrEmpty(promptTemplate)
                ? $"The user has just finished the mandatory video {title}. React in character, one short line."
                : promptTemplate.Replace("{title}", title);

            return await SendChatAsync(systemPrompt, userInput, purpose: AiMeter.PurposeVideoDone).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
