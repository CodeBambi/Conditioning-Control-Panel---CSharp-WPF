using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// Counts the reasons the companion stayed quiet, so a bug report can say WHY.
    ///
    /// <para><b>Why this exists (ccp-bugs #1176, "does not comment on programs/window titles at
    /// all").</b> Every decision point in the awareness pipeline already logs its reason — the
    /// privacy drop, the do-not-disturb gate, the worthiness verdict and the arbiter's gate all
    /// write an <c>[AWARE]</c> line. All of them write it at <b>Debug</b>, deliberately: those lines
    /// name the resolved app id, adult-cluster ones included, and the session log is the file the
    /// bug-report flow attaches. Debug never reaches that file
    /// (<c>LogPipeline</c> floors the file sink at Information unless the run asked for verbose) and
    /// <c>[AWARE]</c> is not one of <c>BugReportService.DiagMarkers</c>, so the flight-recorder
    /// sample does not carry it either. The result is the one in #1176: a report about awareness
    /// saying nothing with 107 log lines and not a single one of them about awareness.</para>
    ///
    /// <para><b>What makes this safe to log at Information.</b> Reason tokens and counts, and
    /// nothing else. No app id, no window title, no cluster — the buckets are enum names the code
    /// already owns, so the summary cannot leak what the per-frame lines are kept at Debug to
    /// protect. That is the same discipline the logging policy asks for: "IDs, counts, enums, and
    /// status codes only".</para>
    /// </summary>
    internal sealed class AwarenessTally
    {
        /// <summary>How often the summary is emitted while the observer runs.</summary>
        public static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(10);

        private readonly object _gate = new();
        private readonly Dictionary<string, Dictionary<string, int>> _buckets =
            new(StringComparer.Ordinal);
        private int _total;

        /// <summary>True while nothing has happened worth reporting.</summary>
        public bool IsEmpty { get { lock (_gate) return _total == 0; } }

        /// <summary>
        /// Records one outcome. <paramref name="bucket"/> is the stage ("drop", "dnd", "scored",
        /// "arbiter"); <paramref name="reason"/> is that stage's own reason token.
        /// </summary>
        public void Note(string bucket, string? reason)
        {
            if (string.IsNullOrWhiteSpace(bucket)) return;
            var key = Normalize(reason);

            lock (_gate)
            {
                if (!_buckets.TryGetValue(bucket, out var counts))
                {
                    counts = new Dictionary<string, int>(StringComparer.Ordinal);
                    _buckets[bucket] = counts;
                }
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
                _total++;
            }
        }

        /// <summary>Records one outcome from an enum value, using its lower-cased name.</summary>
        public void Note<T>(string bucket, T reason) where T : struct, Enum =>
            Note(bucket, reason.ToString());

        /// <summary>
        /// Renders the summary and clears the counters, so each line covers only its own window and a
        /// quiet stretch after a busy one cannot read as more of the same.
        /// </summary>
        public string Drain()
        {
            lock (_gate)
            {
                if (_total == 0) return string.Empty;

                var sb = new StringBuilder();
                foreach (var bucket in _buckets.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    var counts = _buckets[bucket];
                    if (counts.Count == 0) continue;

                    if (sb.Length > 0) sb.Append("; ");
                    sb.Append(bucket).Append('=');

                    var first = true;
                    // Loudest reason first: the answer to "why is she quiet" is almost always the top one.
                    foreach (var pair in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
                    {
                        if (!first) sb.Append(',');
                        sb.Append(pair.Key).Append(':').Append(pair.Value);
                        first = false;
                    }
                }

                _buckets.Clear();
                _total = 0;
                return sb.ToString();
            }
        }

        /// <summary>Longest a reason token may be. Every code-owned token is far shorter.</summary>
        private const int MaxTokenLength = 32;

        /// <summary>
        /// Reason tokens are log KEYS, not free text. Every caller passes an enum name or a token the
        /// code owns ("below-floor", "llm-unavailable/no-bark"), so anything that does not look like
        /// one — too long, or carrying a character a token never has — is replaced wholesale with
        /// <c>other</c> rather than slugified through.
        ///
        /// <para>This line is Information, which means it ships in bug reports, which means a caller
        /// that one day passes a window title would leak it. Refusing the shape is cheap; noticing the
        /// leak afterwards is not.</para>
        /// </summary>
        private static string Normalize(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return "none";

            var trimmed = reason!.Trim();
            if (trimmed.Length > MaxTokenLength) return "other";

            var sb = new StringBuilder(trimmed.Length);
            foreach (var ch in trimmed)
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
                else if (ch == '-' || ch == '_' || ch == '/' || ch == ' ')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
                }
                else return "other";   // punctuation a reason token never carries: @ ( ) : \ . …
            }

            var token = sb.ToString().Trim('-');
            return token.Length == 0 ? "none" : token;
        }
    }
}
