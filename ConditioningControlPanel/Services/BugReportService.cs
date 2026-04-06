using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Client service for submitting bug reports. Collects metadata (with a hard
    /// allowlist), runs the scrubber on logs, HMAC-signs the payload with a
    /// separate embedded secret, and POSTs to the proxy /bug/upload endpoint.
    /// </summary>
    public class BugReportService
    {
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
        private const string BugUploadPath = "/bug/upload";

        // Separate embedded secret — NOT the anti-cheat HMAC key, NOT tied to UnifiedId.
        // Rotated only on compromise via a CCP version bump (old clients get 401 after rotation).
        // Must match BUG_REPORT_CLIENT_SECRET env var on the proxy.
        private const string EmbeddedClientSecret = "067dd64f975e275fd4ba5dd06febaa63f95dcdb93f667ae44716046a30d14b95";

        // Hard caps (client-side mirror of server-side caps).
        private const int MaxDescriptionChars = 4000;
        private const int MaxStepsChars = 4000;
        private const int MaxCrashLogChars = 120_000;
        private const int MaxAppLogLines = 100;

        private readonly HttpClient _httpClient;

        public BugReportService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            _httpClient.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");
        }

        /// <summary>
        /// Result of collecting a bug report ready for preview + submission.
        /// </summary>
        public class BugReportDraft
        {
            public BugMetadata Metadata { get; set; } = new();
            public string Description { get; set; } = string.Empty;
            public string Steps { get; set; } = string.Empty;
            public string ScrubbedCrashLog { get; set; } = string.Empty;
            public string ScrubbedAppLog { get; set; } = string.Empty;
            public bool IncludeAppLog { get; set; }
            public ScrubberCounts Counts { get; set; } = ScrubberCounts.Empty;
        }

        public class BugMetadata
        {
            [JsonProperty("app_version")] public string AppVersion { get; set; } = string.Empty;
            [JsonProperty("os")]          public string Os { get; set; } = string.Empty;
            [JsonProperty("dotnet")]      public string Dotnet { get; set; } = string.Empty;
            [JsonProperty("language")]    public string Language { get; set; } = string.Empty;
            [JsonProperty("active_mod_id")] public string ActiveModId { get; set; } = string.Empty;
        }

        public enum SubmitOutcome { Success, SavedPending, ValidationFailed, NetworkError }

        public class SubmitResult
        {
            public SubmitOutcome Outcome { get; set; }
            public string? Token { get; set; }
            public string? ErrorMessage { get; set; }
        }

        /// <summary>
        /// Build a draft using the hard-allowlisted metadata fields. Nothing is
        /// collected via reflection — every field is set here explicitly.
        /// Forbidden fields (machine name, user name, hostname, Discord/Patreon
        /// identity, IP, timezone, full locale) are never populated.
        /// </summary>
        public BugReportDraft CreateDraft(string description, string steps, bool includeAppLog)
        {
            var metadata = new BugMetadata
            {
                AppVersion = UpdateService.AppVersion ?? "unknown",
                Os = SafeToString(() => Environment.OSVersion.ToString()),
                Dotnet = SafeToString(() => Environment.Version.ToString()),
                Language = App.Settings?.Current?.Language ?? "en",
                ActiveModId = ResolveActiveModId(),
            };

            // Pull crash log if present.
            var crashLogRaw = TryReadCrashLog();
            var (scrubbedCrash, crashCounts) = LogScrubber.Scrub(crashLogRaw);
            if (scrubbedCrash.Length > MaxCrashLogChars)
            {
                scrubbedCrash = scrubbedCrash.Substring(scrubbedCrash.Length - MaxCrashLogChars);
            }

            // Pull recent app log (last N lines) only if the user opted in.
            var scrubbedApp = string.Empty;
            var appCounts = ScrubberCounts.Empty;
            if (includeAppLog)
            {
                var appLogRaw = TryReadRecentAppLog(MaxAppLogLines);
                (scrubbedApp, appCounts) = LogScrubber.Scrub(appLogRaw);
            }

            var totalCounts = crashCounts.Add(appCounts);

            return new BugReportDraft
            {
                Metadata = metadata,
                Description = Truncate(description ?? string.Empty, MaxDescriptionChars),
                Steps = Truncate(steps ?? string.Empty, MaxStepsChars),
                ScrubbedCrashLog = scrubbedCrash,
                ScrubbedAppLog = scrubbedApp,
                IncludeAppLog = includeAppLog,
                Counts = totalCounts,
            };
        }

        /// <summary>
        /// Render the draft as the exact JSON that would be sent to the server.
        /// Shown in the preview so the user sees every field — no hidden data.
        /// </summary>
        public string RenderPreview(BugReportDraft draft)
        {
            var payload = BuildPayload(draft);
            return JsonConvert.SerializeObject(payload, Formatting.Indented);
        }

        /// <summary>
        /// Submit the draft to the proxy /bug/upload endpoint. Returns a
        /// SubmitResult with one of four outcomes; the caller shows a
        /// localized toast based on the outcome.
        /// </summary>
        public async Task<SubmitResult> SubmitAsync(BugReportDraft draft)
        {
            try
            {
                var payload = BuildPayload(draft);
                var body = JsonConvert.SerializeObject(payload);

                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                var signature = ComputeSignature(timestamp, body);

                using var req = new HttpRequestMessage(HttpMethod.Post, ProxyBaseUrl + BugUploadPath)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                req.Headers.Add("X-Bug-Timestamp", timestamp);
                req.Headers.Add("X-Bug-Signature", signature);

                using var res = await _httpClient.SendAsync(req).ConfigureAwait(false);
                var responseText = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

                // 202 = accepted but bot unreachable (payload saved pending retry).
                if ((int)res.StatusCode == 202)
                {
                    var token202 = TryExtractToken(responseText);
                    return new SubmitResult
                    {
                        Outcome = SubmitOutcome.SavedPending,
                        Token = token202,
                        ErrorMessage = "bot_unreachable",
                    };
                }

                if (!res.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("[BugReport] Upload failed: {Status} {Body}", res.StatusCode, responseText);
                    return new SubmitResult
                    {
                        Outcome = SubmitOutcome.ValidationFailed,
                        ErrorMessage = $"HTTP {(int)res.StatusCode}",
                    };
                }

                var token = TryExtractToken(responseText);
                if (string.IsNullOrEmpty(token))
                {
                    return new SubmitResult
                    {
                        Outcome = SubmitOutcome.ValidationFailed,
                        ErrorMessage = "missing_token_in_response",
                    };
                }

                App.Logger?.Information("[BugReport] Submitted successfully: {Token}", token);
                return new SubmitResult
                {
                    Outcome = SubmitOutcome.Success,
                    Token = token,
                };
            }
            catch (TaskCanceledException ex)
            {
                App.Logger?.Warning(ex, "[BugReport] Upload timed out");
                return new SubmitResult
                {
                    Outcome = SubmitOutcome.NetworkError,
                    ErrorMessage = "timeout",
                };
            }
            catch (HttpRequestException ex)
            {
                App.Logger?.Warning(ex, "[BugReport] Network error");
                return new SubmitResult
                {
                    Outcome = SubmitOutcome.NetworkError,
                    ErrorMessage = ex.Message,
                };
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[BugReport] Unexpected error");
                return new SubmitResult
                {
                    Outcome = SubmitOutcome.ValidationFailed,
                    ErrorMessage = ex.Message,
                };
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private Dictionary<string, object> BuildPayload(BugReportDraft draft)
        {
            // Explicit field-by-field construction — forbidden fields (machine name,
            // user name, Discord/Patreon identity, IP, timezone) are NEVER collected.
            return new Dictionary<string, object>
            {
                ["metadata"] = new Dictionary<string, string>
                {
                    ["app_version"] = draft.Metadata.AppVersion,
                    ["os"] = draft.Metadata.Os,
                    ["dotnet"] = draft.Metadata.Dotnet,
                    ["language"] = draft.Metadata.Language,
                    ["active_mod_id"] = draft.Metadata.ActiveModId,
                },
                ["description"] = draft.Description,
                ["steps"] = draft.Steps,
                ["crash_log"] = draft.ScrubbedCrashLog,
                ["app_log"] = draft.IncludeAppLog ? draft.ScrubbedAppLog : string.Empty,
                ["scrubber_counts"] = new Dictionary<string, int>
                {
                    ["paths"] = draft.Counts.Paths,
                    ["emails"] = draft.Counts.Emails,
                    ["tokens"] = draft.Counts.Tokens,
                    ["appdata"] = draft.Counts.AppData,
                },
            };
        }

        private static string ComputeSignature(string timestamp, string body)
        {
            var keyBytes = Encoding.UTF8.GetBytes(EmbeddedClientSecret);
            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string? TryExtractToken(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText)) return null;
            try
            {
                var obj = JObject.Parse(responseText);
                return obj["token"]?.Value<string>();
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveActiveModId()
        {
            try
            {
                var mod = App.Mods?.ActiveMod;
                if (mod == null) return "unknown";
                // If the mod is not a built-in (i.e. locally authored / unpublished),
                // report a generic "custom-mod" label instead of the real ID. This
                // avoids narrow identification in a small community.
                if (!mod.IsBuiltIn) return "custom-mod";
                return string.IsNullOrEmpty(mod.Id) ? "unknown" : mod.Id;
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeToString(Func<string> fn)
        {
            try { return fn() ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);

        private static string TryReadCrashLog()
        {
            try
            {
                var path = Path.Combine(App.UserDataPath, "logs", "crash.log");
                if (!File.Exists(path)) return string.Empty;
                // Crash log is usually small but can accumulate across sessions.
                // Read the last MaxCrashLogChars bytes via a streaming approach.
                var info = new FileInfo(path);
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long tail = Math.Min(info.Length, MaxCrashLogChars);
                fs.Seek(info.Length - tail, SeekOrigin.Begin);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                return sr.ReadToEnd();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[BugReport] crash log read failed: {Msg}", ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Read the last N lines of today's rolling Serilog file.
        /// Serilog rolls daily with name `app-YYYYMMDD.log` (RollingInterval.Day).
        /// </summary>
        private static string TryReadRecentAppLog(int maxLines)
        {
            try
            {
                var logDir = Path.Combine(App.UserDataPath, "logs");
                if (!Directory.Exists(logDir)) return string.Empty;
                var files = Directory.GetFiles(logDir, "app-*.log");
                if (files.Length == 0) return string.Empty;
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                var latest = files[^1];

                // Tail-read: read the whole file then take the last maxLines lines.
                // Serilog logs are bounded to 7 days × ~1 log/event, typically small.
                using var fs = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                var allLines = new List<string>();
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    allLines.Add(line);
                    if (allLines.Count > maxLines * 4)
                    {
                        // Trim to avoid unbounded growth on huge files.
                        allLines.RemoveRange(0, allLines.Count - maxLines * 2);
                    }
                }
                int start = Math.Max(0, allLines.Count - maxLines);
                return string.Join(Environment.NewLine, allLines.GetRange(start, allLines.Count - start));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[BugReport] app log read failed: {Msg}", ex.Message);
                return string.Empty;
            }
        }
    }
}
