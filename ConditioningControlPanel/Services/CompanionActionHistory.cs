using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// In-memory short-term history of conditioning actions performed in the app.
    /// Provides the AI companion with context about recent activity so it can pace
    /// itself and avoid repeating intense effects too frequently.
    /// </summary>
    public class CompanionActionHistory
    {
        // Hard cap on raw events to keep memory bounded during very long sessions.
        public const int MaxEvents = 200;

        private readonly List<CompanionActionEvent> _events = new();
        private readonly ReaderWriterLockSlim _lock = new();

        /// <summary>
        /// Records a new action event.
        /// </summary>
        public void Record(string action, string source, bool succeeded, string? summary = null)
        {
            if (string.IsNullOrWhiteSpace(action)) return;

            var evt = new CompanionActionEvent
            {
                Timestamp = DateTime.Now,
                Action = action.ToLowerInvariant(),
                Source = source.ToLowerInvariant(),
                Succeeded = succeeded,
                Summary = summary
            };

            _lock.EnterWriteLock();
            try
            {
                _events.Add(evt);
                PruneUnlocked();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Clears all recorded history.
        /// </summary>
        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                _events.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Builds a compact, adult-focused summary of recent actions for the AI prompt.
        /// </summary>
        /// <param name="hours">How far back to summarize.</param>
        /// <param name="maxChars">Rough maximum length of the returned text.</param>
        public string BuildSummary(int hours = 2, int maxChars = 350)
        {
            var cutoff = DateTime.Now.AddHours(-Math.Max(1, hours));

            _lock.EnterReadLock();
            List<CompanionActionEvent> recent;
            try
            {
                recent = _events
                    .Where(e => e.Timestamp >= cutoff && e.Succeeded)
                    .OrderBy(e => e.Timestamp)
                    .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }

            if (recent.Count == 0)
                return "No conditioning actions have happened recently.";

            var sb = new StringBuilder();
            sb.AppendLine($"Recent conditioning activity (last {hours}h):");

            // Group by source + action, ordered by frequency.
            var grouped = recent
                .GroupBy(e => (e.Source, e.Action))
                .Select(g => (g.Key.Source, g.Key.Action, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .ToList();

            var aiActions = grouped.Where(x => x.Source == "ai").ToList();
            var autonomyActions = grouped.Where(x => x.Source == "autonomy").ToList();
            var userActions = grouped.Where(x => x.Source == "user").ToList();

            void AppendGroup(string label, List<(string Source, string Action, int Count)> items)
            {
                if (items.Count == 0) return;
                var parts = items.Select(x => $"{x.Count} {Pluralize(x.Action, x.Count)}").ToList();
                sb.AppendLine($"- {label}: {string.Join(", ", parts)}.");
            }

            AppendGroup("You triggered", aiActions);
            AppendGroup("Autonomy triggered", autonomyActions);
            AppendGroup("User triggered", userActions);

            // Mention the most recent action with a relative time.
            var last = recent.Last();
            var elapsed = DateTime.Now - last.Timestamp;
            var timeText = elapsed.TotalMinutes < 1
                ? "just now"
                : $"{((int)elapsed.TotalMinutes)} min ago";
            sb.AppendLine($"Last action: {last.Action} ({timeText}).");

            sb.AppendLine("Pace yourself: if something intense fired recently, tease or deny rather than immediately repeating it.");

            var text = sb.ToString();
            if (text.Length > maxChars)
            {
                text = text.Substring(0, maxChars);
                var lastNewline = text.LastIndexOf('\n');
                if (lastNewline > maxChars * 0.7)
                    text = text.Substring(0, lastNewline);
                text = text.TrimEnd() + "…";
            }
            return text;
        }

        private void PruneUnlocked()
        {
            // Drop oldest events if we exceed the cap.
            while (_events.Count > MaxEvents)
                _events.RemoveAt(0);

            // Also drop anything older than 2x the configured window to keep the list tidy.
            var hardCutoff = DateTime.Now.AddHours(-4);
            _events.RemoveAll(e => e.Timestamp < hardCutoff);
        }

        private static string Pluralize(string word, int count)
        {
            if (count == 1) return word;
            if (word.EndsWith("s")) return word;
            return word + "s";
        }
    }
}
