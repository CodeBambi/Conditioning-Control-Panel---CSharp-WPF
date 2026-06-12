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
    public sealed class OpenAiCompatibleService : AiProviderBase
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

        private static CompanionPromptSettings? Settings => App.Settings?.Current?.CompanionPrompt;

        public override bool IsAvailable
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

                var usage = s.Usage;
                return usage != null && usage.OpenAiCompatibleRequestCount < limit;
            }
        }

        public override int DailyRequestsRemaining
        {
            get
            {
                var s = Settings;
                if (s == null) return 0;

                ResetDailyCounterIfNeeded();

                var limit = s.DailyRequestLimit;
                if (limit <= 0) return -1; // unlimited

                var usage = s.Usage;
                var remaining = usage == null ? limit : limit - usage.OpenAiCompatibleRequestCount;
                return remaining < 0 ? 0 : remaining;
            }
        }

        public OpenAiCompatibleService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            ResetDailyCounterIfNeeded();
        }

        public override async Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
        {
            _ = isUserMessage;

            if (App.Settings?.Current?.OfflineMode == true)
                return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

            return await base.GetBambiReplyExAsync(userInput, isUserMessage);
        }

        /// <summary>
        /// Core transport: builds messages (with optional enrichment), applies sampler
        /// settings, posts to the configured endpoint, and returns the raw assistant content.
        /// </summary>
        protected override async Task<string?> GetRawCompletionAsync(
            string systemPrompt,
            string userInput,
            bool isUserMessage)
        {
            _ = isUserMessage;

            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("OpenAiCompatibleService: Offline mode enabled, skipping AI request");
                return null;
            }

            var apiKey = GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                App.Logger?.Debug("OpenAiCompatibleService: missing API key");
                return null;
            }

            ResetDailyCounterIfNeeded();

            var s = Settings;
            var usage = s?.Usage;
            var limit = s?.DailyRequestLimit ?? 0;
            if (limit > 0 && usage != null && usage.OpenAiCompatibleRequestCount >= limit)
            {
                App.Logger?.Debug("OpenAiCompatibleService: daily limit reached ({Limit})", limit);
                return null;
            }

            var model = GetConfiguredModel();
            var messages = BuildMessages(systemPrompt, userInput);

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
                        return null;
                    }

                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    {
                        App.Logger?.Warning("OpenAiCompatibleService: response has no choices");
                        return null;
                    }

                    var first = choices[0];
                    if (!first.TryGetProperty("message", out var message) ||
                        !message.TryGetProperty("content", out var contentElement))
                    {
                        App.Logger?.Warning("OpenAiCompatibleService: response missing message.content");
                        return null;
                    }

                    return CleanTokenizerArtifacts(contentElement.GetString());
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
                    return null;
                }
            }

            App.Logger?.Warning("OpenAiCompatibleService: request failed after retry");
            return null;
        }

        private List<MessageDto> BuildMessages(string systemPrompt, string userInput)
        {
            var messages = new List<MessageDto>
            {
                new("system", systemPrompt),
                new("user", userInput)
            };

            var enrichment = BuildEnrichmentMessage(userInput);
            if (enrichment != null)
            {
                messages.Insert(1, enrichment);
            }

            return messages;
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

        private static string CleanTokenizerArtifacts(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            // GPT-2/GPT-Neo/llama.cpp tokenizers sometimes emit 'Ġ' for leading spaces.
            return text.Replace("Ġ", " ");
        }

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
            var usage = Settings?.Usage;
            if (usage == null) return;

            if (DateTime.Today <= usage.LastResetDate) return;

            usage.OpenAiCompatibleRequestCount = 0;
            usage.LastResetDate = DateTime.Today;
            App.Settings?.Save();
            App.Logger?.Debug("OpenAiCompatibleService: Daily request count reset");
        }

        private void BumpDailyCounter()
        {
            ResetDailyCounterIfNeeded();
            var usage = Settings?.Usage;
            if (usage == null) return;

            usage.OpenAiCompatibleRequestCount++;
            App.Settings?.Save();
        }

        protected override string GetFallbackResponse()
        {
            if (App.Mods?.IsBambiMode == true)
                return "Bambi's head is so empty right now~ *giggles*";
            return "...";
        }

        protected override string GetModelHint()
        {
            var model = GetConfiguredModel();
            return "openai-compatible:" + (string.IsNullOrWhiteSpace(model) ? "unknown" : model);
        }

        protected override string GetProviderName() => "OpenAiCompatibleService";

        public override void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
