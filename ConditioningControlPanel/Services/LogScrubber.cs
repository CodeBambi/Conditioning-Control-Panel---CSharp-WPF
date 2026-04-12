using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Per-category counts from a scrubber pass. Shown in the bug-report preview
    /// so the user can see at a glance how many things were redacted.
    /// </summary>
    public record ScrubberCounts(int Paths, int Emails, int Tokens, int AppData)
    {
        public static ScrubberCounts Empty => new(0, 0, 0, 0);

        public ScrubberCounts Add(ScrubberCounts other) =>
            new(Paths + other.Paths, Emails + other.Emails, Tokens + other.Tokens, AppData + other.AppData);
    }

    /// <summary>
    /// Pure static scrubber for bug-report payloads. Unit-testable in isolation.
    /// Applies a fixed set of regex rules to remove PII and normalize timestamps.
    /// </summary>
    public static class LogScrubber
    {
        // C:\Users\<name>\...  or  C:/Users/<name>/...
        // Matches both backslash and forward-slash forms. The username is whatever
        // appears between Users\ and the next path separator.
        private static readonly Regex UserPathRegex = new(
            @"(?i)([A-Z]:[\\/])Users[\\/]([^\\/\r\n""']+)",
            RegexOptions.Compiled);

        // Email addresses (RFC-ish — good enough for log scrubbing).
        private static readonly Regex EmailRegex = new(
            @"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // OAuth-style tokens in JSON or key=value form: "access_token": "...", refresh_token=..., etc.
        private static readonly Regex JsonTokenRegex = new(
            @"(?i)(""?(?:access_token|refresh_token|auth_token|api_key|apikey|authorization|bearer_token|x-auth-token|x-admin-token|client_secret|patreon_token|discord_token)""?\s*[:=]\s*)""?[A-Z0-9._~+/=\-]{8,}""?",
            RegexOptions.Compiled);

        // Bearer <token>
        private static readonly Regex BearerRegex = new(
            @"(?i)\bBearer\s+[A-Z0-9._~+/=\-]{16,}\b",
            RegexOptions.Compiled);

        // Discord bot tokens (standard format: three dot-separated base64 segments, ~24.6.27+ chars)
        private static readonly Regex DiscordBotTokenRegex = new(
            @"\b[MNO][A-Za-z0-9_\-]{23,25}\.[A-Za-z0-9_\-]{6}\.[A-Za-z0-9_\-]{27,}\b",
            RegexOptions.Compiled);

        // %LOCALAPPDATA%, %APPDATA%, %USERPROFILE% — both literal expansions and variable forms.
        private static readonly Regex AppDataLiteralRegex = new(
            @"(?i)%(?:LOCALAPPDATA|APPDATA|USERPROFILE)%",
            RegexOptions.Compiled);

        // Expanded forms: C:\Users\<name>\AppData\Local\... and ...\AppData\Roaming\...
        // Handled indirectly via UserPathRegex (which captures the username) — we leave
        // the rest of the path intact so debug info is preserved.

        // Timestamp formats to normalize. Each pattern is tried in order; the first
        // matching span is replaced with its UTC-rounded-to-minute form.
        // Supported formats:
        //   yyyy-MM-dd HH:mm:ss        (crash log format, App.xaml.cs LogCrashDetails)
        //   yyyy-MM-dd HH:mm:ss.fff    (Serilog default)
        //   yyyy-MM-ddTHH:mm:ss[.fff][Z|±hh:mm]   (ISO 8601)
        private static readonly Regex TimestampRegex = new(
            @"\b(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2}):(\d{2})(?:\.\d+)?(Z|[+\-]\d{2}:?\d{2})?\b",
            RegexOptions.Compiled);

        /// <summary>
        /// Scrub a string, returning the redacted text plus per-category counts.
        /// </summary>
        public static (string Scrubbed, ScrubberCounts Counts) Scrub(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return (string.Empty, ScrubberCounts.Empty);

            int paths = 0, emails = 0, tokens = 0, appdata = 0;

            // 1. User paths (home folder → redacted).
            var step1 = UserPathRegex.Replace(input, m =>
            {
                paths++;
                return $"{m.Groups[1].Value}Users\\<redacted>";
            });

            // 2. Email addresses.
            var step2 = EmailRegex.Replace(step1, _ =>
            {
                emails++;
                return "[email redacted]";
            });

            // 3. JSON/key-value tokens.
            var step3 = JsonTokenRegex.Replace(step2, m =>
            {
                tokens++;
                return $"{m.Groups[1].Value}[token redacted]";
            });

            // 4. Bearer tokens.
            var step4 = BearerRegex.Replace(step3, _ =>
            {
                tokens++;
                return "Bearer [token redacted]";
            });

            // 5. Discord bot tokens.
            var step5 = DiscordBotTokenRegex.Replace(step4, _ =>
            {
                tokens++;
                return "[discord token redacted]";
            });

            // 6. %APPDATA% / %LOCALAPPDATA% / %USERPROFILE% literal env var references.
            var step6 = AppDataLiteralRegex.Replace(step5, _ =>
            {
                appdata++;
                return "%APPDATA%";
            });

            // 7. Timestamp normalization (last, so previous substitutions don't interfere).
            var step7 = TimestampRegex.Replace(step6, m =>
            {
                if (TryNormalizeTimestamp(m, out var normalized))
                    return normalized;
                return m.Value;
            });

            return (step7, new ScrubberCounts(paths, emails, tokens, appdata));
        }

        private static bool TryNormalizeTimestamp(Match m, out string normalized)
        {
            normalized = string.Empty;
            try
            {
                int year = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                int day = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                int hour = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                int minute = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
                // Seconds are discarded — we round to the minute.

                // Build a DateTimeOffset. If a zone suffix was captured, parse the whole
                // match through DateTimeOffset.TryParse so we honor it; otherwise treat
                // as local time (crash log / Serilog default) and convert to UTC.
                DateTimeOffset dto;
                var zone = m.Groups[7].Value;
                if (!string.IsNullOrEmpty(zone))
                {
                    if (!DateTimeOffset.TryParse(m.Value, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out dto))
                        return false;
                }
                else
                {
                    // No zone info — treat as local and convert to UTC.
                    var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local);
                    dto = new DateTimeOffset(local).ToUniversalTime();
                }

                var utc = dto.ToUniversalTime();
                // Round down to minute (we already dropped seconds above).
                var rounded = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
                normalized = rounded.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
