using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>Shared filtering and ordering for the session rack. Head callers own the rows they
    /// expose; this helper owns only the query contract and the portable file-mtime probe.</summary>
    public static class SessionRackQuery
    {
        /// <summary>Whether a session matches the rack's source, difficulty and raw-text filters.
        /// The search text is deliberately not trimmed here; the WPF handler normalizes it before
        /// calling this method.</summary>
        public static bool RackAccepts(
            Session session,
            string sourceFilter,
            ISet<SessionDifficulty> difficulties,
            string search)
        {
            switch (sourceFilter)
            {
                case "builtin":
                    if (session.Source != SessionSource.BuiltIn) return false;
                    break;
                case "yours":
                    if (session.Source != SessionSource.Custom) return false;
                    break;
                case "catalogue":
                    if (session.Source != SessionSource.Imported) return false;
                    break;
            }

            if (!difficulties.Contains(session.Difficulty)) return false;

            if (search.Length > 0)
            {
                var name = session.GetModeAwareName() ?? "";
                var description = session.GetModeAwareDescription() ?? "";
                if (name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    description.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            return true;
        }

        /// <summary>Orders visible rows using the unfiltered registry for the final stable tie
        /// break. Unknown sort tokens use the rack's file-based recent order.</summary>
        public static List<Session> SortRackSessions(
            IReadOnlyList<Session> rows,
            IReadOnlyList<Session> registryOrder,
            string sort)
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < registryOrder.Count; i++)
                index[registryOrder[i].Id ?? ""] = i;

            int Idx(Session session) => index.TryGetValue(session.Id ?? "", out var i) ? i : int.MaxValue;

            switch (sort)
            {
                case "name":
                    return rows.OrderBy(s => s.GetModeAwareName() ?? "", StringComparer.OrdinalIgnoreCase)
                               .ThenBy(Idx).ToList();
                case "easiest":
                    return rows.OrderBy(s => (int)s.Difficulty)
                               .ThenBy(s => s.DurationMinutes)
                               .ThenBy(Idx).ToList();
                case "hardest":
                    return rows.OrderByDescending(s => (int)s.Difficulty)
                               .ThenByDescending(s => s.DurationMinutes)
                               .ThenBy(Idx).ToList();
                case "shortest":
                    return rows.OrderBy(s => s.DurationMinutes).ThenBy(Idx).ToList();
                case "xp":
                    return rows.OrderByDescending(s => s.BonusXP).ThenBy(Idx).ToList();
                default:
                {
                    var stamps = new Dictionary<string, DateTime?>(StringComparer.Ordinal);
                    foreach (var session in rows)
                        stamps[session.Id ?? ""] = RackFileStamp(session);

                    DateTime? Stamp(Session session) =>
                        stamps.TryGetValue(session.Id ?? "", out var stamp) ? stamp : null;

                    return rows.OrderByDescending(s => Stamp(s).HasValue)
                               .ThenByDescending(s => Stamp(s) ?? DateTime.MinValue)
                               .ThenBy(Idx).ToList();
                }
            }
        }

        /// <summary>Returns the local last-write time of a backing file, or null when its path is
        /// empty, missing or unreadable. The check/read race remains the same as the old WPF code.
        /// </summary>
        private static DateTime? RackFileStamp(Session session)
        {
            try
            {
                var path = session.SourceFilePath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                return File.GetLastWriteTime(path);
            }
            catch { return null; }
        }
    }
}
