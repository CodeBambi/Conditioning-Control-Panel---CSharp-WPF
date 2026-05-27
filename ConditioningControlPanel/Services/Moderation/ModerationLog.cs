using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.Moderation
{
    /// <summary>
    /// Append-only writer for moderation hits.
    ///
    /// File: <c>%APPDATA%/ConditioningControlPanel/logs/moderation.log</c>.
    ///
    /// Line format (pipe-delimited, fixed columns):
    /// <c>{ISO8601 UTC} | {category} | {source} | {session_id_hash} | {model_hint}</c>
    ///
    /// Rotation: when the active file exceeds <see cref="MaxBytes"/> it is renamed
    /// to <c>moderation.log.1</c> (oldest archive bumped first, capped at
    /// <see cref="MaxArchives"/>).
    ///
    /// CRITICAL: no message bodies. No user identifiers beyond the opaque per-launch
    /// hash. This is intentional — the file documents moderation activity for CCBill
    /// record-retention without becoming a subpoena target for user content. Do NOT
    /// extend this file to log message text.
    /// </summary>
    public sealed class ModerationLog
    {
        // 10 MB per file, keep 5 archives -> ~50 MB ceiling.
        private const long MaxBytes = 10L * 1024L * 1024L;
        private const int MaxArchives = 5;

        private readonly string _logDir;
        private readonly string _logPath;
        private readonly object _writeLock = new();
        private readonly ModerationSession _session;

        public ModerationLog(ModerationSession session)
        {
            _session = session;
            // App.UserDataPath is %APPDATA%/ConditioningControlPanel by convention.
            // We cannot reference App here without a circular dependency at startup,
            // so duplicate the path computation. Matches App.UserDataPath exactly.
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _logDir = Path.Combine(appData, "ConditioningControlPanel", "logs");
            _logPath = Path.Combine(_logDir, "moderation.log");
        }

        /// <summary>
        /// Records a moderation hit. <paramref name="source"/> is one of
        /// <c>input</c>, <c>output</c>, or <c>edit</c> (the latter reserved for
        /// future prompt-validator hooks). <paramref name="modelHint"/> should be
        /// <c>cloud</c> for the proxy AI service or <c>local:&lt;modelname&gt;</c>
        /// for the local Ollama service.
        /// </summary>
        public void Record(ProhibitedCategory category, string source, string modelHint)
        {
            try
            {
                lock (_writeLock)
                {
                    Directory.CreateDirectory(_logDir);
                    RotateIfNeeded();
                    var line = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:yyyy-MM-ddTHH:mm:ssZ} | {1} | {2} | {3} | {4}{5}",
                        DateTime.UtcNow,
                        SanitizeField(category.ToString()),
                        SanitizeField(source),
                        SanitizeField(_session.GetSessionIdHash()),
                        SanitizeField(modelHint),
                        Environment.NewLine);
                    File.AppendAllText(_logPath, line);
                }
            }
            catch
            {
                // Best-effort. A failed log entry must not break the user's chat. The
                // App.Logger pipeline (Serilog) already captures the broader event
                // via the call sites in AiService / LocalAiService.
            }
        }

        private void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(_logPath)) return;
                var fi = new FileInfo(_logPath);
                if (fi.Length < MaxBytes) return;

                // Bump archives: moderation.log.4 -> .5, .3 -> .4, ..., .log -> .1.
                for (int i = MaxArchives; i >= 1; i--)
                {
                    var src = i == 1 ? _logPath : _logPath + "." + (i - 1);
                    var dst = _logPath + "." + i;
                    if (!File.Exists(src)) continue;
                    if (File.Exists(dst))
                    {
                        try { File.Delete(dst); } catch { /* ignore */ }
                    }
                    try { File.Move(src, dst); } catch { /* ignore — next call will retry */ }
                }
            }
            catch
            {
                // Rotation is best-effort; if it fails we just keep appending to the
                // current file until the next call succeeds.
            }
        }

        /// <summary>
        /// Defense-in-depth scrubbing of any control char that could break the
        /// pipe-delimited format. Fields are short, predictable values from the
        /// caller — this is paranoia, not a real attack surface.
        /// </summary>
        private static string SanitizeField(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return new string(s.Select(c =>
                c == '|' ? '/' :
                c == '\r' || c == '\n' || c == '\t' ? ' ' :
                c
            ).ToArray());
        }
    }
}
