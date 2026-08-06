

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

        // One moderation spine for BOTH entry points (legacy one-shots and the Train 1 multi-turn
        // SendAsync). This provider shipped a whole release with NO moderation at all; a second,
        // hand-copied spine for the new path is exactly how that happens again.
        private readonly TransportModeration _moderation = CreateModeration();

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

        /// <summary>
        /// This provider's moderation spine, pre-wired with its log prefix, counter source and
        /// <c>openai_compat:{model}</c> hint. Static so a test can exercise the real,
        /// provider-configured spine without a live endpoint.
        /// </summary>
        internal static TransportModeration CreateModeration() =>
            new("OpenAiCompatibleService", "openai_compat", ModelHint);

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
        private Task<string?> SendChatAsync(string systemPrompt, string userInput, bool returnRefusalSentinel = false,
            string purpose = AiMeter.PurposeChat)
            => SendChatCoreAsync(BuildMessages(systemPrompt, userInput), userInput, returnRefusalSentinel, purpose);

        /// <summary>
        /// The one request path, shared by the legacy single-shot wrapper above and the Train 1
        /// multi-turn <see cref="SendAsync"/>. Everything that must not fork lives here: offline gate,
        /// input moderation, key/limit gates, retry-with-backoff, response parsing, output moderation
        /// and effect execution, and the single [AI-METER] line.
        ///
        /// <paramref name="newestUserInput"/> is the only text handed to <c>CheckInput</c> — earlier
        /// turns in <paramref name="messages"/> already passed the guard when they were first sent.
        /// </summary>
        private async Task<string?> SendChatCoreAsync(List<MessageDto> messages, string? newestUserInput,
            bool returnRefusalSentinel, string purpose, CancellationToken cancellationToken = default)
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
            var meterInputChars = messages.Sum(m => m.Content?.Length ?? 0);
            void Meter(string outcome, int outputChars = 0) =>
                AiMeter.Record(AiMeter.ProviderOpenAiCompatible, purpose, meterInputChars, outputChars,
                    meterStopwatch.ElapsedMilliseconds, outcome);

            // INPUT MODERATION (Layer 1 — code-side, prompt cannot bypass). Runs BEFORE the
            // HTTP request so prohibited inputs never leave the client. Same semantics as the
            // cloud and local providers; shared with SendAsync through _moderation.
            if (_moderation.CheckInput(newestUserInput, escalate: returnRefusalSentinel).HasValue)
            {
                Meter(AiMeter.OutcomeRefusedInput);
                return returnRefusalSentinel ? ModerationRefusal.InputSentinel : null;
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

                    using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var status = (int)response.StatusCode;
                        var retryableStatus = status == 429 || status >= 500;

                        if (attempt == 0 && retryableStatus)
                        {
                            await Task.Delay(1200, cancellationToken).ConfigureAwait(false);
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
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Caller-driven cancellation, not a transport failure: leave the meter alone.
                    return null;
                }
                catch (HttpRequestException) when (attempt == 0)
                {
                    await Task.Delay(1200, cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException) when (attempt == 0)
                {
                    await Task.Delay(1200, cancellationToken).ConfigureAwait(false);
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
            InsertEnrichmentIfEnabled(messages);
            return messages;
        }

        /// <summary>
        /// Splices the effects "[CONTEXT BLOCK — NOT DIALOGUE]" message in immediately after the
        /// leading system message(s) — index 1 for the legacy two-message shape, which is where it
        /// always went, and the equivalent position for a multi-turn window. Best-effort: a knowledge
        /// or prompt failure means "no effects this turn", never a dead reply.
        /// </summary>
        private void InsertEnrichmentIfEnabled(List<MessageDto> messages)
        {
            if (App.Settings?.Current?.CompanionPrompt?.AllowAiToControlEffects != true) return;

            try
            {
                var currentTime = DateTime.Now.ToString("yyyy-M-dd dddd h:mm:ss tt");
                var facts = _knowledgeService.GetKnowledge("");
                var factsJson = JsonSerializer.Serialize(facts);
                var enrichment = _promptService.BuildEnrichmentMessage(factsJson, currentTime);

                var insertAt = 0;
                while (insertAt < messages.Count && messages[insertAt].Role == "system") insertAt++;
                messages.Insert(insertAt, enrichment);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("OpenAiCompatibleService: enrichment block build failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// OUTPUT MODERATION (Layer 1). Returns true when <paramref name="text"/> is safe to
        /// show. On a block, <paramref name="refusal"/> carries the value the caller must
        /// return — the sentinel on the interactive chat path, null everywhere else.
        /// </summary>
        private bool PassesOutputModeration(string? text, bool returnRefusalSentinel, out string? refusal)
        {
            refusal = null;

            // Model OUTPUT that trips the filter is never the user's doing, so it does NOT escalate
            // the Content Policy Notice — TransportModeration.CheckOutput never touches the counter.
            // The hit is still recorded for the CCBill compliance log.
            if (_moderation.CheckOutput(text) == null) return true;

            refusal = returnRefusalSentinel ? ModerationRefusal.OutputSentinel : null;
            return false;
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

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
        {
#pragma warning disable CS0618 // legacy internals: the adapter layer is one level up, in AiServiceStrategy
            var result = await GetBambiReplyExAsync(userInput, isUserMessage).ConfigureAwait(false);
#pragma warning restore CS0618
            if (result.Refusal != null)
            {
                return result.Refusal.Source == ModerationSource.Input
                    ? ModerationRefusal.InputSentinel
                    : ModerationRefusal.OutputSentinel;
            }
            return result.Text;
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
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

        /// <summary>
        /// Train 1 transport seam. See <see cref="IAiService.SendAsync"/> for the contract.
        ///
        /// <para><paramref name="messages"/> is forwarded verbatim, in order — OpenAI-compatible
        /// endpoints are natively multi-turn, so the whole window reaches the model. The effects
        /// context block is spliced in after the system message exactly as on the legacy path.</para>
        ///
        /// <para><b>Deliberately not sent:</b> <c>max_tokens</c> and <c>temperature</c> from
        /// <paramref name="options"/>. This is a bring-your-own endpoint whose sampler is configured by
        /// the user (<c>OpenAiCompatibleUseCustomSamplerSettings</c>); the legacy path has never sent a
        /// cap, and imposing the cloud provider's 100-token ceiling here would silently truncate
        /// replies that work today.</para>
        /// </summary>
        public async Task<AiReplyResult> SendAsync(IReadOnlyList<ChatMessage> messages, AiCallOptions options,
            CancellationToken cancellationToken = default)
        {
            options ??= AiCallOptions.Chat;
            var list = messages ?? (IReadOnlyList<ChatMessage>)Array.Empty<ChatMessage>();

            if (App.Settings?.Current?.OfflineMode == true)
                return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

            var dtos = new List<MessageDto>(list.Count + 1);
            foreach (var m in list) dtos.Add(new MessageDto(m.Role, m.Content ?? string.Empty));
            InsertEnrichmentIfEnabled(dtos);

            var newestUser = NewestUserText(list);

            var reply = await SendChatCoreAsync(dtos, newestUser,
                returnRefusalSentinel: options.Interactive, purpose: options.MeterPurpose,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var refusalSource = ModerationRefusal.GetSource(reply);
            if (refusalSource.HasValue)
            {
                return new AiReplyResult(string.Empty, IsAiGenerated: false,
                    Refusal: new ModerationRefusalInfo(Category: null, Source: refusalSource.Value));
            }

            if (string.IsNullOrWhiteSpace(reply))
                return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

            return new AiReplyResult(reply, IsAiGenerated: true, Refusal: null);
        }

        /// <summary>
        /// The newest user-authored message in a transport window — the only text handed to
        /// <c>CheckInput</c>. Earlier turns already passed the guard when they were first sent, and
        /// re-checking them would double-write the compliance log.
        /// </summary>
        internal static string NewestUserText(IReadOnlyList<ChatMessage> messages)
        {
            if (messages == null) return string.Empty;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role == ChatMessage.RoleUser) return messages[i].Content ?? string.Empty;
            }
            return string.Empty;
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "", TimeSpan? duration = null)
        {
            var prompt = _bambiSprite.GetSystemPrompt();
            var userInput = FrameFormatter.AwarenessFrame(detectedName, category, serviceName, pageTitle, duration);
            return await SendChatAsync(prompt, userInput, purpose: AiMeter.PurposeAwareness).ConfigureAwait(false);
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
        {
            var prompt = _bambiSprite.GetSystemPrompt();
            var userInput = FrameFormatter.StillOnFrame(displayName, category, duration);
            return await SendChatAsync(prompt, userInput, purpose: AiMeter.PurposeStillOn).ConfigureAwait(false);
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
        {
            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = FrameFormatter.KeywordFrame(keyword, promptTemplate);
            return await SendChatAsync(systemPrompt, userInput, purpose: AiMeter.PurposeKeyword).ConfigureAwait(false);
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
        {
            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = FrameFormatter.LockScreenFrame(sentance, mistakes, amount, promptTemplate);
            return await SendChatAsync(systemPrompt, userInput, purpose: AiMeter.PurposeLockScreen).ConfigureAwait(false);
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public async Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
        {
            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = FrameFormatter.VideoDoneFrame(title, promptTemplate);
            return await SendChatAsync(systemPrompt, userInput, purpose: AiMeter.PurposeVideoDone).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
