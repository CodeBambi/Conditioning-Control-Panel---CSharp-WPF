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
        // Default hard cap on raw events to keep memory bounded during very long sessions.
        public const int DefaultMaxEvents = 200;

        private readonly List<CompanionActionEvent> _events = new();
        private readonly ReaderWriterLockSlim _lock = new();

        private int EffectiveMaxEvents => Math.Max(50, App.Settings?.Current?.CompanionPrompt?.AiActionHistoryMaxEvents ?? DefaultMaxEvents);

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
        /// Re-applies the current settings cap immediately (e.g. after the user lowers the limit).
        /// </summary>
        public void SetMaxEvents(int maxEvents)
        {
            _lock.EnterWriteLock();
            try
            {
                while (_events.Count > Math.Max(50, maxEvents))
                    _events.RemoveAt(0);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Returns a snapshot of events since the given cutoff. Used by the subject
        /// profile service at session end without exposing the internal list.
        /// </summary>
        public List<CompanionActionEvent> GetEventsSince(DateTime cutoff)
        {
            _lock.EnterReadLock();
            try
            {
                return _events
                    .Where(e => e.Timestamp >= cutoff)
                    .OrderBy(e => e.Timestamp)
                    .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Builds a compact, adult-focused summary of recent actions for the AI prompt.
        /// The output budget scales with the configured window so longer windows can
        /// mention more actions without exploding the prompt.
        /// </summary>
        /// <param name="minutes">How far back to summarize.</param>
        public string BuildSummary(int minutes = 120)
        {
            minutes = Math.Max(1, minutes);
            // Scale summary size with the window: 15m→150, 30m→200, 1h→250, 2h→350,
            // 4h→550, 8h→800, 12h→1000, 24h→1200.
            var maxChars = minutes switch
            {
                <= 15 => 150,
                <= 30 => 200,
                <= 60 => 250,
                <= 120 => 350,
                <= 240 => 550,
                <= 480 => 800,
                <= 720 => 1000,
                _ => 1200
            };

            var cutoff = DateTime.Now.AddMinutes(-minutes);

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
            var windowLabel = minutes >= 1440 ? "1 day" : minutes >= 60 ? $"{minutes / 60}h" : $"{minutes}m";
            sb.AppendLine($"Recent conditioning activity (last {windowLabel}):");

            // Group by source + action, ordered by frequency.
            var grouped = recent
                .GroupBy(e => (e.Source, e.Action))
                .Select(g => (g.Key.Source, g.Key.Action, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .ToList();

            var aiActions = grouped.Where(x => x.Source == "ai").ToList();
            var autonomyAiActions = grouped.Where(x => x.Source == "autonomy-ai").ToList();
            var autonomyActions = grouped.Where(x => x.Source == "autonomy").ToList();
            var userActions = grouped.Where(x => x.Source == "user").ToList();

            void AppendGroup(string label, List<(string Source, string Action, int Count)> items)
            {
                if (items.Count == 0) return;
                var parts = items.Select(x => $"{x.Count} {Pluralize(x.Action, x.Count)}").ToList();
                sb.AppendLine($"- {label}: {string.Join(", ", parts)}.");
            }

            AppendGroup("You triggered", aiActions);
            AppendGroup("AI-guided Takeover triggered", autonomyAiActions);
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
            var settings = App.Settings?.Current?.CompanionPrompt;
            var minutes = Math.Max(1, settings?.AiActionHistoryMinutes > 0 ? settings.AiActionHistoryMinutes : settings?.AiActionHistoryHours * 60 ?? 120);
            var maxEvents = EffectiveMaxEvents;

            // Drop oldest events if we exceed the cap.
            while (_events.Count > maxEvents)
                _events.RemoveAt(0);

            // Also drop anything older than 2x the configured window to keep the list tidy.
            var hardCutoff = DateTime.Now.AddMinutes(-minutes * 2);
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
