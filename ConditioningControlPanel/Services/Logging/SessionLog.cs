using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConditioningControlPanel.Services.Logging
{
    /// <summary>
    /// One file per run: <c>logs/session-yyyyMMdd-HHmmss.log</c>.
    ///
    /// <para>The daily <c>app-YYYYMMDD.log</c> was the wrong unit. Nobody debugs a Tuesday; they
    /// debug a run - and a user who has to relaunch after a freeze in order to file a report was
    /// appending the relaunch to the very file that held the evidence, behind a hundred lines of
    /// startup chatter. A file per session means "the log" and "the thing that went wrong" are the
    /// same object, and the bug reporter can attach it whole.</para>
    ///
    /// <para>The header carries what used to be repeated on all 34,000 lines a week: the date, and
    /// the environment. Paths appear as the redactor's own tokens because the real ones are exactly
    /// what we do not want in a file people paste into Discord.</para>
    /// </summary>
    public static class SessionLog
    {
        public const int MaxSessionFiles = 20;
        public const long MaxSessionBytes = 20L * 1024 * 1024;

        /// <summary>Old daily logs age out on their own; this is the sweep that finally removes them.</summary>
        public const int LegacyAppLogDays = 14;

        public static string FileName(string sessionId) => "session-" + sessionId + ".log";

        /// <summary>
        /// Enforce retention over the EXISTING files, then write the header for this run. Returns
        /// the full path of this session's file.
        /// </summary>
        public static string Prepare(string logsDir, string sessionId, string version, string language, string modId)
        {
            try { Directory.CreateDirectory(logsDir); } catch { /* swallow: the sink reports this better */ }

            Prune(logsDir);
            SweepLegacyAppLogs(logsDir);

            var path = Path.Combine(logsDir, FileName(sessionId));
            try
            {
                var sb = new StringBuilder(256);
                sb.Append("== CCP v").Append(version)
                  .Append(" session ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))
                  .Append(" pid ").Append(Environment.ProcessId.ToString(CultureInfo.InvariantCulture))
                  .AppendLine(" ==");
                sb.Append("os=").Append(Safe(() => Environment.OSVersion.ToString()))
                  .Append(" dotnet=").Append(Safe(() => Environment.Version.ToString()))
                  .Append(" install=%APP% data=%DATA%")
                  .Append(" lang=").Append(string.IsNullOrWhiteSpace(language) ? "?" : language)
                  .Append(" mod=").Append(string.IsNullOrWhiteSpace(modId) ? "none" : modId)
                  .AppendLine();
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { /* swallow: a missing header must not cost us the log */ }
            return path;
        }

        /// <summary>
        /// The closing line. Written after the sinks are closed, because the file sink holds the
        /// handle while it is alive.
        /// </summary>
        public static void WriteFooter(string logsDir, string sessionId, TimeSpan uptime, int warnings, int errors, int suppressed)
        {
            try
            {
                var line = string.Format(CultureInfo.InvariantCulture,
                    "== end: uptime {0:h\\:mm\\:ss} warn={1} err={2} suppressed={3} ==",
                    uptime, warnings, errors, suppressed);
                File.AppendAllText(Path.Combine(logsDir, FileName(sessionId)), line + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* swallow: shutdown is best effort */ }
        }

        /// <summary>
        /// Oldest first, delete until the folder holds at most <see cref="MaxSessionFiles"/> files
        /// AND <see cref="MaxSessionBytes"/>. Both limits, because 20 crash-loop sessions can be
        /// far larger than 20 ordinary ones.
        /// </summary>
        public static void Prune(string logsDir)
        {
            try
            {
                var dir = new DirectoryInfo(logsDir);
                if (!dir.Exists) return;
                var files = dir.GetFiles("session-*.log");
                if (files.Length == 0) return;
                Array.Sort(files, (a, b) => string.CompareOrdinal(a.Name, b.Name)); // name IS the timestamp

                long total = 0;
                foreach (var f in files) total += Length(f);

                int count = files.Length;
                for (int i = 0; i < files.Length && (count > MaxSessionFiles || total > MaxSessionBytes); i++)
                {
                    long len = Length(files[i]);
                    try { files[i].Delete(); total -= len; count--; }
                    catch { /* swallow: locked file, try again next launch */ }
                }
            }
            catch { /* swallow */ }
        }

        /// <summary>Remove the pre-session daily logs once they are older than two weeks.</summary>
        public static void SweepLegacyAppLogs(string logsDir)
        {
            try
            {
                var dir = new DirectoryInfo(logsDir);
                if (!dir.Exists) return;
                var cutoff = DateTime.Now.AddDays(-LegacyAppLogDays);
                foreach (var f in dir.GetFiles("app-*.log"))
                {
                    try { if (f.LastWriteTime < cutoff) f.Delete(); }
                    catch { /* swallow */ }
                }
            }
            catch { /* swallow */ }
        }

        private static long Length(FileInfo f)
        {
            try { return f.Length; } catch { return 0; }
        }

        private static string Safe(Func<string> get)
        {
            try { return get() ?? "?"; } catch { return "?"; }
        }
    }
}
