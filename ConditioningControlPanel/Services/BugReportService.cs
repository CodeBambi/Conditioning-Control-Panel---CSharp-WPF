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
        // Lines of the dedicated video/panic trace appended to the app-log field. The trace is
        // terse (one short line per transition), so 200 lines covers several videos plus a whole
        // freeze window at a few KB — well inside the crash-log-sized fields the server accepts.
        private const int MaxVideoDiagLines = 200;
        // Diagnostic-line rescue (#634 + freeze reports). The last-N tail scrolls the [RES]/
        // [WATCHDOG] history out of every report because a relaunch writes far more startup
        // chatter than MaxAppLogLines. Scan a much wider tail and keep only the marker lines so
        // the resource/hang timeline survives even when the plain tail is all startup noise.
        internal const int MaxDiagScanLines = 2000;
        internal const int MaxDiagMatches = 40;      // most-recent matches kept
        internal const int MaxDiagSectionChars = 16_000; // GitHub issue-body budget guard
        // Grep-friendly markers written to the rolling app log. [RES]/[WATCHDOG] come from
        // UiHangWatchdog and are the ones that actually appear today; the video markers are
        // kept defensively (VideoDiag writes its own file, already appended in full above).
        internal static readonly string[] DiagMarkers =
            { "[RES]", "[WATCHDOG]", "[BLUR]", "[VIDEO]", "[VideoDiag]" };

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

        /// <summary>
        /// What the user is filing. Both kinds ride the exact same transport,
        /// endpoint, and signing — a Suggestion is just tagged in the description
        /// text (see the marker below) and skips crash/app-log collection.
        /// </summary>
        public enum ReportKind { Bug, Suggestion }

        // Prepended to the description for suggestions so they're obvious in the
        // shared tracker (no server change — they still arrive as "bug" reports).
        private const string SuggestionMarker = "[SUGGESTION] ";

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
        public BugReportDraft CreateDraft(string description, string steps, bool includeAppLog, ReportKind kind = ReportKind.Bug)
        {
            var metadata = new BugMetadata
            {
                AppVersion = UpdateService.AppVersion ?? "unknown",
                Os = SafeToString(() => Environment.OSVersion.ToString()),
                Dotnet = SafeToString(() => Environment.Version.ToString()),
                Language = App.Settings?.Current?.Language ?? "en",
                ActiveModId = ResolveActiveModId(),
            };

            bool isSuggestion = kind == ReportKind.Suggestion;

            // Pull crash log if present. Suggestions are feature ideas, not defects —
            // crash/app logs are noise, so skip collecting them entirely.
            var (scrubbedCrash, crashCounts) = isSuggestion
                ? (string.Empty, ScrubberCounts.Empty)
                : LogScrubber.Scrub(TryReadCrashLog());
            if (scrubbedCrash.Length > MaxCrashLogChars)
            {
                scrubbedCrash = scrubbedCrash.Substring(scrubbedCrash.Length - MaxCrashLogChars);
            }

            // Pull recent app log (last N lines) only if the user opted in (bug reports only).
            var scrubbedApp = string.Empty;
            var appCounts = ScrubberCounts.Empty;
            if (includeAppLog && !isSuggestion)
            {
                var appLogRaw = TryReadRecentAppLog(MaxAppLogLines);

                // #616/#617/#621/#622/#623: the app-log tail alone was useless for the v6.5.0 freeze
                // reports. A user whose PC had to be hard-reset must relaunch the app to file the
                // report, and the relaunch writes far more than MaxAppLogLines of startup chatter —
                // so the 100-line tail contained nothing but startup, every time. The video/panic
                // trace lives in its own small flush-on-write file that a relaunch cannot scroll,
                // so append its tail here. Same field (no server schema change), clearly delimited,
                // and it goes through the same scrubber as everything else.
                var diagRaw = VideoDiag.Tail(MaxVideoDiagLines);
                var combined = string.IsNullOrWhiteSpace(diagRaw)
                    ? appLogRaw
                    : appLogRaw + Environment.NewLine + Environment.NewLine +
                      "===== video/panic diagnostic trace (video-diag.log) =====" + Environment.NewLine +
                      diagRaw;

                // #634 + freeze reports: rescue the [RES]/[WATCHDOG] resource+hang timeline from a
                // much wider window of the rolling app log so it survives even when the 100-line
                // tail above is all startup chatter. Appended to the same field before scrubbing,
                // so it goes through the same PII scrubber as everything else below.
                var diagSampled = CollectSampledDiagnostics();
                if (!string.IsNullOrWhiteSpace(diagSampled))
                    combined = combined + Environment.NewLine + Environment.NewLine +
                        "## Diagnostics (sampled)" + Environment.NewLine + diagSampled;

                (scrubbedApp, appCounts) = LogScrubber.Scrub(combined);
            }

            var totalCounts = crashCounts.Add(appCounts);

            // Tag suggestions in the description text itself so they stand out in the
            // shared tracker. Marker is added before truncation so the cap still holds.
            var desc = description ?? string.Empty;
            if (isSuggestion)
                desc = SuggestionMarker + desc;

            return new BugReportDraft
            {
                Metadata = metadata,
                Description = Truncate(desc, MaxDescriptionChars),
                Steps = isSuggestion ? string.Empty : Truncate(steps ?? string.Empty, MaxStepsChars),
                ScrubbedCrashLog = scrubbedCrash,
                ScrubbedAppLog = scrubbedApp,
                IncludeAppLog = includeAppLog && !isSuggestion,
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
                return FilterToCurrentVersion(FilterBenignCrashes(sr.ReadToEnd()));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[BugReport] crash log read failed: {Msg}", ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Drop crash entries that are known to be harmless C++ runtime cleanup at app
        /// exit (DllNotFoundException with stack frames in __std_type_info_destroy_list /
        /// _app_exit_callback). These auto-fire on a clean shutdown and otherwise generate
        /// empty bug reports we can't act on (#147).
        /// </summary>
        private static string FilterBenignCrashes(string log)
        {
            if (string.IsNullOrEmpty(log)) return log;
            const string sep = "================================================================================";
            // Each entry starts at a separator line, has another separator after the
            // header, and a final separator closing it. Split on a sentinel and reassemble.
            var marker = "CRASH REPORT - ";
            var entries = log.Split(new[] { marker }, StringSplitOptions.None);
            if (entries.Length <= 1) return log;

            var kept = new System.Text.StringBuilder(entries[0]);
            int dropped = 0;
            for (int i = 1; i < entries.Length; i++)
            {
                var entry = entries[i];
                bool benign = entry.Contains("System.DllNotFoundException", StringComparison.Ordinal)
                    && (entry.Contains("__std_type_info_destroy_list", StringComparison.Ordinal)
                        || entry.Contains("_app_exit_callback", StringComparison.Ordinal));
                if (benign)
                {
                    dropped++;
                    continue;
                }
                kept.Append(marker).Append(entry);
            }

            if (dropped > 0)
                App.Logger?.Debug("[BugReport] filtered {Count} benign exit-cleanup crash entries", dropped);
            return kept.ToString();
        }

        /// <summary>
        /// Keep only crash entries emitted by the CURRENTLY-running build. crash.log is append-only,
        /// so even after startup rotation a report could catch a crash logged just before an in-place
        /// update; more importantly this guards against a rotation that failed (locked/denied file).
        /// Each entry is tagged with "App Version: X.Y.Z" (see App.LogCrashDetails). Entries from other
        /// versions — and legacy untagged entries from before this change — are dropped so triage sees
        /// only crashes relevant to the reported build. If the log has no delimited entries, it's
        /// returned untouched.
        /// </summary>
        private static string FilterToCurrentVersion(string log)
        {
            if (string.IsNullOrEmpty(log)) return log;
            const string marker = "CRASH REPORT - ";
            var entries = log.Split(new[] { marker }, StringSplitOptions.None);
            if (entries.Length <= 1) return log; // no delimited entries — leave as-is

            var versionTag = "App Version: " + UpdateService.AppVersion;
            var kept = new System.Text.StringBuilder(entries[0]);
            int dropped = 0;
            for (int i = 1; i < entries.Length; i++)
            {
                if (entries[i].Contains(versionTag, StringComparison.Ordinal))
                    kept.Append(marker).Append(entries[i]);
                else
                    dropped++;
            }

            if (dropped > 0)
                App.Logger?.Debug("[BugReport] dropped {Count} crash entries from other app versions", dropped);
            return kept.ToString();
        }

        /// <summary>
        /// Scan a wide tail of the rolling app log (MaxDiagScanLines) and keep only the
        /// diagnostic-critical marker lines (see <see cref="DiagMarkers"/>), returning up to
        /// MaxDiagMatches most-recent matches. This survives the startup chatter that scrolls the
        /// plain last-N tail (#634 RAM-growth lines, [WATCHDOG] hang timeline). Not scrubbed here —
        /// the caller appends the result to the app-log field so it runs through the shared
        /// LogScrubber with everything else. Never throws.
        /// </summary>
        private static string CollectSampledDiagnostics()
        {
            try
            {
                var raw = TryReadRecentAppLog(MaxDiagScanLines);
                if (string.IsNullOrEmpty(raw)) return string.Empty;
                return BuildSampledDiagnostics(raw.Split('\n'));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[BugReport] sampled diagnostics read failed: {Msg}", ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Pure core of <see cref="CollectSampledDiagnostics"/>: given the app-log lines (most recent
        /// last), scan the last <see cref="MaxDiagScanLines"/> of them, keep only lines containing a
        /// <see cref="DiagMarkers"/> marker, retain the <see cref="MaxDiagMatches"/> most-recent
        /// matches in chronological order, and cap the joined section at <see cref="MaxDiagSectionChars"/>
        /// (keeping the newest bytes). Returns an empty string when nothing matches. Never throws.
        /// Extracted so the sampling contract can be unit-tested headlessly; the production reader
        /// (<see cref="CollectSampledDiagnostics"/>) delegates here after loading the wide tail.
        /// </summary>
        internal static string BuildSampledDiagnostics(IEnumerable<string> lines)
        {
            if (lines == null) return string.Empty;
            var all = lines as IReadOnlyList<string> ?? new List<string>(lines);
            if (all.Count == 0) return string.Empty;

            // Scan only the last MaxDiagScanLines lines (mirrors the wide-tail read, which already
            // bounds the input in production — a no-op there, load-bearing for callers that pass more).
            int scanStart = Math.Max(0, all.Count - MaxDiagScanLines);

            var matches = new List<string>();
            for (int idx = scanStart; idx < all.Count; idx++)
            {
                var line = (all[idx] ?? string.Empty).TrimEnd('\r');
                for (int i = 0; i < DiagMarkers.Length; i++)
                {
                    if (line.Contains(DiagMarkers[i], StringComparison.Ordinal))
                    {
                        matches.Add(line);
                        break;
                    }
                }
            }
            if (matches.Count == 0) return string.Empty;

            int start = Math.Max(0, matches.Count - MaxDiagMatches);
            var section = string.Join(Environment.NewLine, matches.GetRange(start, matches.Count - start));
            // Cap the section so it can't blow the GitHub issue-body budget; keep the newest.
            if (section.Length > MaxDiagSectionChars)
                section = section.Substring(section.Length - MaxDiagSectionChars);
            return section;
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
