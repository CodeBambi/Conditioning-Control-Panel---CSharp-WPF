using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The apps offered as one-click rows in the awareness list picker.
    ///
    /// <para><b>Why this is not just the trigger ring.</b> Both awareness lists used to source their
    /// candidates from <c>KeywordTriggerService.GetRecentForegroundApps()</c>, which only collects
    /// while that service is running - and <c>KeywordTriggersEnabled</c> defaults to false. So the
    /// normal user, who has never turned keyword triggers on, opened the list editor and was handed a
    /// completely blank box plus an instruction to type process names from memory. Awareness has its
    /// own, better answer to "which apps have I used": the ledger it has been keeping all along.</para>
    ///
    /// <para>Sources are merged newest-most-relevant first and de-duplicated after sanitising, so an
    /// app that arrives from two sources under two spellings cannot appear twice.</para>
    /// </summary>
    public static class AwarenessAppCandidates
    {
        /// <summary>Rows beyond this stop being a list and start being a wall.</summary>
        public const int MaxCandidates = 40;

        /// <summary>
        /// Gathers candidates, sanitised and de-duplicated. Never throws and never returns null: a
        /// dead source degrades to fewer rows, which is a smaller dialog, not a broken one.
        /// </summary>
        /// <param name="exclude">
        /// Entries already listed. They are shown by the dialog itself, at the top, so re-offering
        /// them as candidates would duplicate every current row.
        /// </param>
        public static IReadOnlyList<string> Gather(IEnumerable<string>? exclude = null)
        {
            var ordered = new List<string>();

            // 1. What is in front of you right now - overwhelmingly the reason the dialog is open.
            TryAdd(ordered, () => new[] { App.WindowAwareness?.CurrentServiceName ?? string.Empty });

            // 2. This session's app switches, newest first.
            TryAdd(ordered, () => AwarenessLive.Ledger?.RecentTransitions.Select(t => t.AppId)
                                  ?? Enumerable.Empty<string>());

            // 3. The persisted per-app counters - the apps she is actually keeping numbers on, which
            //    survive a restart and are therefore the only source that works on a cold launch.
            TryAdd(ordered, () => AwarenessLive.Ledger?.KnownAppIds ?? Enumerable.Empty<string>());

            // 4. The trigger ring last: still worth folding in when that service happens to be on.
            TryAdd(ordered, () => App.KeywordTriggers?.GetRecentForegroundApps()
                                  ?? Enumerable.Empty<string>());

            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in exclude ?? Enumerable.Empty<string>())
            {
                var clean = AwarenessText.SanitizeRuleEntry(entry);
                if (clean != null) skip.Add(clean);
            }

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in ordered)
            {
                if (result.Count >= MaxCandidates) break;
                var clean = AwarenessText.SanitizeRuleEntry(raw);
                if (clean == null) continue;
                if (skip.Contains(clean) || !seen.Add(clean)) continue;
                result.Add(clean);
            }

            return result;
        }

        /// <summary>
        /// Appends one source. Each is wrapped separately: the ledger may be null before awareness has
        /// ever started and the trigger service may be null entirely, and neither should cost the
        /// dialog the sources that did work.
        /// </summary>
        private static void TryAdd(List<string> into, Func<IEnumerable<string>> source)
        {
            try
            {
                foreach (var entry in source())
                {
                    if (!string.IsNullOrWhiteSpace(entry)) into.Add(entry);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Awareness candidates: a source was unavailable ({E})", ex.Message);
            }
        }
    }
}
