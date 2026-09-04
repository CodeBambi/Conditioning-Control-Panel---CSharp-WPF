using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Bookkeeping for exceptions the app deliberately swallows.
    ///
    /// The codebase used to be full of bodyless <c>catch { }</c> blocks: when something broke behind
    /// one of them there was nothing at all in the log, so triage started from zero. Every such catch
    /// now calls <see cref="Swallowed"/>, which names the site through caller info and keeps a count.
    ///
    /// Volume control (a swallowed exception is by definition not fatal, so it must never drown the
    /// log): the FIRST hit at a site logs one Warning, every hit up to the tenth logs Debug (Debug
    /// stays in the in-memory flight recorder rather than on disk), and after ten hits at that site
    /// only the counter moves. That bounds even per-frame and timer-tick call sites.
    ///
    /// This type must never throw and never allocate much: it is called from catch blocks, some of
    /// them inside shutdown paths where <see cref="App.Logger"/> is already gone.
    /// </summary>
    public static class Diag
    {
        /// <summary>Per-site log budget. Hits past this only increment the counter.</summary>
        private const int MaxLogsPerSite = 10;

        /// <summary>Site key ("File.cs:123") to number of hits this session.</summary>
        private static readonly ConcurrentDictionary<string, int> Sites = new(StringComparer.Ordinal);

        private static int _total;

        /// <summary>
        /// Test seam. When set, events go here instead of <see cref="App.Logger"/> so the unit tests
        /// can observe the real message templates without standing up a WPF <see cref="App"/>.
        /// </summary>
        internal static ILogger? LoggerOverride { get; set; }

        /// <summary>Total swallowed exceptions this session, including ones past the per-site cap.</summary>
        public static int SwallowCount => Volatile.Read(ref _total);

        /// <summary>Number of distinct call sites that have swallowed at least one exception.</summary>
        public static int SwallowSiteCount => Sites.Count;

        /// <summary>
        /// Records an exception that is being deliberately ignored.
        /// </summary>
        /// <param name="ex">The swallowed exception.</param>
        /// <param name="note">
        /// Optional short reason the catch is a no-op (for example "window tearing down"). Keep it
        /// free of user content: no paths, no text the user typed or the app displayed.
        /// </param>
        public static void Swallowed(
            Exception ex,
            string? note = null,
            [CallerFilePath] string file = "",
            [CallerMemberName] string member = "",
            [CallerLineNumber] int line = 0)
        {
            try
            {
                Interlocked.Increment(ref _total);

                var site = SiteKey(file, line);
                var hits = Sites.AddOrUpdate(site, 1, static (_, prev) => prev == int.MaxValue ? prev : prev + 1);
                if (hits > MaxLogsPerSite) return;

                var logger = LoggerOverride ?? App.Logger;
                if (logger == null) return;

                var exType = ex?.GetType().Name ?? "null";
                var exMessage = ex?.Message ?? string.Empty;

                if (note == null)
                {
                    if (hits == 1)
                    {
                        logger.Warning("[Swallow] {Site} {Member} {ExType}: {ExMessage}", site, member, exType, exMessage);
                    }
                    logger.Debug("[Swallow] {Site} {Member} {ExType}: {ExMessage}", site, member, exType, exMessage);
                }
                else
                {
                    if (hits == 1)
                    {
                        logger.Warning("[Swallow] {Site} {Member} {ExType}: {ExMessage} ({Note})", site, member, exType, exMessage, note);
                    }
                    logger.Debug("[Swallow] {Site} {Member} {ExType}: {ExMessage} ({Note})", site, member, exType, exMessage, note);
                }
            }
            catch { } // swallow: the swallow helper itself must never throw into a catch block
        }

        /// <summary>
        /// The busiest swallow sites this session, one "File.cs:123 count" line each, biggest first.
        /// Used by the session-file footer and by support dumps.
        /// </summary>
        public static string SwallowSummary(int top = 10)
        {
            try
            {
                if (top <= 0) return string.Empty;

                var sb = new StringBuilder();
                foreach (var pair in Sites.ToArray().OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(top))
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(pair.Key).Append(' ').Append(pair.Value);
                }
                return sb.ToString();
            }
            catch
            {
                return string.Empty; // swallow: summary is diagnostics-only, never worth an exception
            }
        }

        /// <summary>Clears the per-session state. Tests only.</summary>
        internal static void ResetForTests()
        {
            Sites.Clear();
            Volatile.Write(ref _total, 0);
            LoggerOverride = null;
        }

        /// <summary>"File.cs:123" from a caller file path, without allocating a Path.GetFileName miss.</summary>
        private static string SiteKey(string file, int line)
        {
            var name = file;
            if (!string.IsNullOrEmpty(name))
            {
                var slash = name.LastIndexOfAny(new[] { '\\', '/' });
                if (slash >= 0 && slash < name.Length - 1) name = name.Substring(slash + 1);
            }
            else
            {
                name = "?";
            }
            return name + ":" + line.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
