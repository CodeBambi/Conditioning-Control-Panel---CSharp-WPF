using System;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.Logging
{
    /// <summary>
    /// The single redaction rule set for everything the app writes to disk.
    ///
    /// <para><b>Why this exists next to <see cref="LogScrubber"/>.</b> The scrubber runs once, at
    /// bug-report upload time, over text that is already on disk: by then a week of
    /// <c>C:\Users\&lt;name&gt;\...</c> lines (1,763 of them in a one-week sample) has already been
    /// written to app-.log and crash.log, where anyone who opens the folder can read it. This class
    /// runs on the WRITE path instead, so the identifying text never reaches the file at all. The
    /// two share their regexes (below) so a rule can only be fixed in one place, but they differ in
    /// output: the scrubber keeps the shape (<c>Users\&lt;redacted&gt;</c>) because its output is
    /// read by a human triaging a report, while this one collapses whole known roots to short
    /// tokens (<c>%DATA%</c>) because it runs on every line and shorter is cheaper.</para>
    ///
    /// <para>All rules are ordinal and allocation-light on the common path: a line with no
    /// separator, no <c>@</c> and no long digit run returns the same reference it was handed.</para>
    /// </summary>
    public static class LogRedactor
    {
        // ---- Shared primitives (LogScrubber uses these too; do not duplicate them there) ----

        /// <summary>C:\Users\name\... and C:/Users/name/... - group 1 is the drive+separator.</summary>
        internal static readonly Regex UserPathRegex = new(
            @"(?i)([A-Z]:[\\/])Users[\\/]([^\\/\r\n""']+)",
            RegexOptions.Compiled);

        /// <summary>
        /// /home/name/... and /Users/name/... See LogScrubber for the boundary reasoning: the
        /// leading delimiter is a zero-width lookbehind so adjacent paths both match, "root" is
        /// deliberately preserved, and the match is case-sensitive so an HTTP route "/users/alice"
        /// is left alone.
        /// </summary>
        internal static readonly Regex PosixHomePathRegex = new(
            @"(?<=^|[\s""'(\[=:,;])(/home/|/Users/)(?!root(?![^/\r\n""'\s,;)\]]))([^/\r\n""'\s,;)\]]+)",
            RegexOptions.Compiled);

        internal static readonly Regex EmailRegex = new(
            @"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static readonly Regex JsonTokenRegex = new(
            @"(?i)(""?(?:access_token|refresh_token|auth_token|api_key|apikey|authorization|bearer_token|x-auth-token|x-admin-token|client_secret|patreon_token|discord_token)""?\s*[:=]\s*)""?[A-Z0-9._~+/=\-]{8,}""?",
            RegexOptions.Compiled);

        internal static readonly Regex BearerRegex = new(
            @"(?i)\bBearer\s+[A-Z0-9._~+/=\-]{16,}\b",
            RegexOptions.Compiled);

        internal static readonly Regex DiscordBotTokenRegex = new(
            @"\b[MNO][A-Za-z0-9_\-]{23,25}\.[A-Za-z0-9_\-]{6}\.[A-Za-z0-9_\-]{27,}\b",
            RegexOptions.Compiled);

        internal static readonly Regex AppDataLiteralRegex = new(
            @"(?i)%(?:LOCALAPPDATA|APPDATA|USERPROFILE)%",
            RegexOptions.Compiled);

        /// <summary>
        /// Snowflake-shaped ids (Discord ids are 17-19 digits; 20 covers headroom). Last four are
        /// kept so two lines about the same user can still be correlated during triage.
        /// </summary>
        private static readonly Regex LongIdRegex = new(
            @"(?<![0-9])[0-9]{17,20}(?![0-9])",
            RegexOptions.Compiled);

        /// <summary>Server-side user handles, e.g. <c>u_9f2a71bc</c>.</summary>
        private static readonly Regex UserHandleRegex = new(
            @"\bu_[a-z0-9]{8,}\b",
            RegexOptions.Compiled);

        // ---- Known roots ----

        private static string? _dataRoot;
        private static string? _appRoot;
        private static string? _assetsRoot;
        private static long _assetsStamp;
        private static bool _rootsResolved;

        /// <summary>
        /// Resolves the assets root on demand. Set by the pipeline; the assets folder is settings
        /// driven and settings load after the logger, so it cannot be captured once at startup.
        /// </summary>
        public static Func<string?>? AssetsRootProvider { get; set; }

        /// <summary>
        /// Point the root rules at explicit folders. Called by the pipeline at startup and by tests.
        /// Nulls leave a root unresolved (that rule is then skipped).
        /// </summary>
        public static void ConfigureRoots(string? dataRoot, string? appRoot = null, string? assetsRoot = null)
        {
            _dataRoot = Trim(dataRoot);
            _appRoot = Trim(appRoot);
            _assetsRoot = Trim(assetsRoot);
            _assetsStamp = Environment.TickCount64;
            _rootsResolved = true;
        }

        /// <summary>
        /// The crash writer redacts before the pipeline has configured anything (a crash during
        /// startup is exactly when that happens), so fall back to the app's own folders once.
        /// </summary>
        private static void EnsureRoots()
        {
            if (_rootsResolved) return;
            _rootsResolved = true;
            try { _dataRoot = Trim(App.UserDataPath); } catch { /* swallow: defaults are optional */ }
            try { _appRoot = Trim(AppContext.BaseDirectory); } catch { /* swallow */ }
        }

        private static string? Trim(string? path) =>
            string.IsNullOrWhiteSpace(path) ? null : path!.TrimEnd('\\', '/');

        private static string? AssetsRoot()
        {
            // Re-ask the provider at most once a minute: the user can repoint the assets folder at
            // runtime, but asking on every log line would mean a Directory.Exists per line.
            var provider = AssetsRootProvider;
            if (provider != null && (_assetsRoot == null || Environment.TickCount64 - _assetsStamp > 60_000))
            {
                _assetsStamp = Environment.TickCount64;
                try { _assetsRoot = Trim(provider()); }
                catch { /* swallow: a broken provider must never break logging */ }
            }
            return _assetsRoot;
        }

        /// <summary>
        /// Apply every rule, in order. Returns the input unchanged (same reference) when nothing
        /// matched, which is the overwhelmingly common case.
        /// </summary>
        public static string Redact(string? input)
        {
            if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
            if (!MightContainSecrets(input!)) return input!;

            var s = input!;
            try
            {
                EnsureRoots();

                // 1. Known roots first: %DATA% lives under C:\Users\<name>, so collapsing it here
                //    keeps the more useful token instead of falling through to the "~" rule.
                s = ReplaceRoot(s, _dataRoot, "%DATA%");
                s = ReplaceRoot(s, _appRoot, "%APP%");
                s = ReplaceRoot(s, AssetsRoot(), "%ASSETS%");

                // 2. Any remaining home directory.
                //    Both regexes consume only the "root + username" span and leave the following
                //    separator in place, so the replacement is a bare "~".
                s = UserPathRegex.Replace(s, "~");
                s = PosixHomePathRegex.Replace(s, "~");

                // 3-4. Contact details and credentials.
                s = EmailRegex.Replace(s, "<email>");
                s = JsonTokenRegex.Replace(s, m => m.Groups[1].Value + "<token>");
                s = BearerRegex.Replace(s, "Bearer <token>");
                s = DiscordBotTokenRegex.Replace(s, "<token>");

                // 5. Identifiers: keep the last four so lines stay correlatable.
                s = LongIdRegex.Replace(s, m => "<id:…" + m.Value.Substring(m.Value.Length - 4) + ">");
                s = UserHandleRegex.Replace(s, m => "u_…" + m.Value.Substring(m.Value.Length - 4));
            }
            catch
            {
                // swallow: redaction is best effort, but a partially redacted line is still better
                // than throwing out of a sink and losing the log.
            }
            return s;
        }

        /// <summary>
        /// Cheap pre-filter. Every rule needs at least one of: a path separator, an "@", an "_",
        /// a "%", or a run of 17+ digits. Most log lines have none of those and skip 10 regexes.
        /// </summary>
        private static bool MightContainSecrets(string s)
        {
            int digits = 0, run = 0;
            bool runDigit = false, runAlpha = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' || c == '/' || c == '@' || c == '_' || c == '%') return true;

                bool isDigit = c >= '0' && c <= '9';
                if (isDigit)
                {
                    if (++digits >= 17) return true;   // snowflake-shaped id
                }
                else digits = 0;

                // A long unbroken base64-ish run carrying both letters and digits. This is what a
                // bearer token or a Discord bot token looks like when the "Bearer " prefix is the
                // only separator on the line; without this arm those two rules would never run,
                // because the token itself contains none of the characters checked above.
                bool isAlpha = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                if (isDigit || isAlpha || c == '.' || c == '-' || c == '+' || c == '=' || c == '~')
                {
                    run++;
                    runDigit |= isDigit;
                    runAlpha |= isAlpha;
                    if (run >= 20 && runDigit && runAlpha) return true;
                }
                else
                {
                    run = 0;
                    runDigit = runAlpha = false;
                }
            }
            return false;
        }

        private static string ReplaceRoot(string s, string? root, string token)
        {
            if (root == null || root.Length == 0 || s.Length < root.Length) return s;
            if (s.IndexOf(root, StringComparison.OrdinalIgnoreCase) < 0)
            {
                // Same folder written with the other slash style - one normalised retry, no regex.
                var alt = root.IndexOf('\\') >= 0 ? root.Replace('\\', '/') : root.Replace('/', '\\');
                if (ReferenceEquals(alt, root) || s.IndexOf(alt, StringComparison.OrdinalIgnoreCase) < 0) return s;
                root = alt;
            }
            return Replace(s, root, token);
        }

        private static string Replace(string s, string find, string token)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            int at = 0;
            while (true)
            {
                int hit = s.IndexOf(find, at, StringComparison.OrdinalIgnoreCase);
                if (hit < 0) break;
                sb.Append(s, at, hit - at).Append(token);
                at = hit + find.Length;
            }
            if (at == 0) return s;
            sb.Append(s, at, s.Length - at);
            return sb.ToString();
        }
    }
}
