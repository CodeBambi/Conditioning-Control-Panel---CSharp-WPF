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
        // The path, email and token rules are the SHARED primitives in
        // Services/Logging/LogRedactor.cs. They used to be declared here, which meant the upload
        // path (this class) and the write path (the redactor) each carried their own copy of the
        // same seven regexes - two places for one rule to be fixed in, and the reason a rule fixed
        // for a bug report could silently stay broken for the log file itself. The aliases keep the
        // rest of this class, and its output shapes, exactly as they were.
        private static readonly Regex UserPathRegex = Logging.LogRedactor.UserPathRegex;
        private static readonly Regex PosixHomePathRegex = Logging.LogRedactor.PosixHomePathRegex;
        private static readonly Regex EmailRegex = Logging.LogRedactor.EmailRegex;
        private static readonly Regex JsonTokenRegex = Logging.LogRedactor.JsonTokenRegex;
        private static readonly Regex BearerRegex = Logging.LogRedactor.BearerRegex;
        private static readonly Regex DiscordBotTokenRegex = Logging.LogRedactor.DiscordBotTokenRegex;
        private static readonly Regex AppDataLiteralRegex = Logging.LogRedactor.AppDataLiteralRegex;

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

            // 1. User paths (home folder → redacted). Windows shape first, then POSIX/macOS,
            //    so a report from any head redacts the username the same way.
            var step1 = UserPathRegex.Replace(input, m =>
            {
                paths++;
                return $"{m.Groups[1].Value}Users\\<redacted>";
            });

            step1 = PosixHomePathRegex.Replace(step1, m =>
            {
                paths++;
                return $"{m.Groups[1].Value}<redacted>";
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
