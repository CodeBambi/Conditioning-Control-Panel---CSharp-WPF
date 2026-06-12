using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Maintains a persisted, privacy-first behavioral profile of the subject across
    /// sessions. Updated at session end and injected into the system prompt once at
    /// session start. Never stores raw chat content.
    /// </summary>
    public class CompanionSubjectProfileService
    {
        private readonly string _filePath;
        private readonly object _lock = new();

        public CompanionSubjectProfile Profile { get; private set; } = new();

        public CompanionSubjectProfileService()
        {
            _filePath = Path.Combine(App.UserDataPath, "ai_subject_profile.json");
            Load();
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    Profile = new CompanionSubjectProfile();
                    return;
                }

                var json = File.ReadAllText(_filePath);
                var loaded = JsonConvert.DeserializeObject<CompanionSubjectProfile>(json);
                Profile = loaded ?? new CompanionSubjectProfile();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "CompanionSubjectProfileService: failed to load profile");
                Profile = new CompanionSubjectProfile();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                var json = JsonConvert.SerializeObject(Profile, Formatting.Indented);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "CompanionSubjectProfileService: failed to save profile");
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                Profile = new CompanionSubjectProfile();
            }

            try
            {
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "CompanionSubjectProfileService: failed to delete profile file");
            }
        }

        /// <summary>
        /// Updates the persisted profile from a finished session and the recent action
        /// history. Called once at session end, not every prompt.
        /// </summary>
        public void UpdateFromSession(SessionLog? sessionLog, IEnumerable<CompanionActionEvent>? recentActions)
        {
            if (App.Settings?.Current?.CompanionPrompt?.AiSubjectProfileEnabled != true)
                return;

            lock (_lock)
            {
                var actions = recentActions?.Where(e => e.Succeeded).ToList() ?? new List<CompanionActionEvent>();

                // Merge with existing totals.
                Profile.TotalSessions++;
                Profile.TotalActions += actions.Count;
                Profile.UpdatedAt = DateTime.Now;

                // Update average session length.
                if (sessionLog != null && sessionLog.Duration.TotalMinutes > 0)
                {
                    var sessionsSoFar = Math.Max(1, Profile.TotalSessions);
                    var currentAvg = Profile.AverageSessionMinutes ?? 0;
                    Profile.AverageSessionMinutes = ((currentAvg * (sessionsSoFar - 1)) + sessionLog.Duration.TotalMinutes) / sessionsSoFar;

                    // Common session start hour.
                    var startHour = sessionLog.StartedAt.Hour;
                    if (!Profile.CommonSessionHour.HasValue)
                    {
                        Profile.CommonSessionHour = startHour;
                    }
                    else
                    {
                        // Simple moving average toward the most common hour (circular).
                        var diff = ((startHour - Profile.CommonSessionHour.Value + 12) % 24) - 12;
                        Profile.CommonSessionHour = (Profile.CommonSessionHour.Value + (int)Math.Round(diff * 0.3)) % 24;
                        if (Profile.CommonSessionHour < 0) Profile.CommonSessionHour += 24;
                    }
                }

                // Favorite actions: top 3 by count across recent history.
                var actionCounts = actions
                    .GroupBy(a => a.Action)
                    .Select(g => new { Action = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(3)
                    .Select(x => x.Action)
                    .ToList();

                if (actionCounts.Count > 0)
                    Profile.FavoriteActions = actionCounts;

                // Declined actions: actions attempted but not succeeded.
                var declined = recentActions?
                    .Where(e => !e.Succeeded && !string.IsNullOrEmpty(e.Summary))
                    .GroupBy(e => e.Action)
                    .Select(g => new { Action = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(3)
                    .Select(x => x.Action)
                    .ToList() ?? new List<string>();

                if (declined.Count > 0)
                    Profile.DeclinedActions = declined;

                // Intensity preference from action summaries (very rough heuristic).
                var intensities = actions
                    .Where(a => (a.Action == "spiral" || a.Action == "pink_filter") && !string.IsNullOrEmpty(a.Summary))
                    .Select(a => ExtractIntensity(a.Summary!))
                    .Where(i => i.HasValue)
                    .Select(i => i!.Value)
                    .ToList();

                if (intensities.Count > 0)
                {
                    var avg = intensities.Average();
                    Profile.IntensityPreference = avg switch
                    {
                        < 20 => "gentle",
                        < 50 => "moderate",
                        _ => "intense"
                    };
                }

                // Short notes based on patterns (keep it generic and safe).
                var notes = new List<string>();
                if (Profile.IntensityPreference != null)
                    notes.Add($"tends toward {Profile.IntensityPreference} intensity");
                if (Profile.CommonSessionHour.HasValue)
                    notes.Add($"often starts sessions around {Profile.CommonSessionHour.Value:00}:00");
                if (declined.Contains("haptic"))
                    notes.Add("often declines haptic feedback");
                if (declined.Contains("video"))
                    notes.Add("often skips triggered videos");

                Profile.Notes = notes.Count > 0 ? string.Join("; ", notes) : null;

                // Persist a compact narrative summary of the recent action window.
                var settingsCp = App.Settings?.Current?.CompanionPrompt;
                var minutes = settingsCp?.AiActionHistoryMinutes > 0
                    ? settingsCp.AiActionHistoryMinutes
                    : (settingsCp?.AiActionHistoryHours ?? 2) * 60;
                Profile.RecentSessionSummary = App.ActionHistory?.BuildSummary(minutes);
            }

            Save();
        }

        /// <summary>
        /// Builds a short addendum for the system prompt at session start.
        /// </summary>
        public string BuildSystemPromptAddendum()
        {
            if (App.Settings?.Current?.CompanionPrompt?.AiSubjectProfileEnabled != true)
                return string.Empty;

            lock (_lock)
            {
                if (Profile.TotalSessions == 0)
                    return string.Empty;

                var parts = new List<string>
                {
                    $"Across {Profile.TotalSessions} sessions, the subject's most common effects are {string.Join(", ", Profile.FavoriteActions.DefaultIfEmpty("none"))}."
                };

                if (Profile.IntensityPreference != null)
                    parts.Add($"They lean toward {Profile.IntensityPreference} intensity.");

                if (!string.IsNullOrEmpty(Profile.Notes))
                    parts.Add(Profile.Notes);

                if (!string.IsNullOrWhiteSpace(Profile.RecentSessionSummary))
                    parts.Add($"Recent activity: {Profile.RecentSessionSummary}");

                var text = string.Join(" ", parts);
                return text.Length > 350 ? text.Substring(0, 350).TrimEnd() + "…" : text;
            }
        }

        private static int? ExtractIntensity(string summary)
        {
            // Look for "intensity=N%" or "N%" in the summary.
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(summary, @"intensity[=:]?\s*(\d+)|(\d+)%");
                if (match.Success)
                {
                    var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    if (int.TryParse(value, out var intensity))
                        return intensity;
                }
            }
            catch { }
            return null;
        }
    }
}
