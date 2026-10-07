using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The persisted Season Recap snapshots (one JSON per season under UserData/season-recaps), the
    /// half of WPF SeasonRecapService both heads need to re-view a recap. Moved verbatim from
    /// ConditioningControlPanel/Services/Progression/SeasonRecapService.cs, which now delegates here.
    /// </summary>
    public static class SeasonRecapStore
    {
        private static string SnapshotDir => Path.Combine(CorePaths.UserData, "season-recaps");

        private static string PathFor(string seasonKey) =>
            Path.Combine(SnapshotDir, $"{seasonKey}.json");

        public static void Save(SeasonRecapSnapshot snapshot)
        {
            try
            {
                Directory.CreateDirectory(SnapshotDir);
                var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
                File.WriteAllText(PathFor(snapshot.SeasonKey), json);
                Log.Information("SeasonRecap: saved snapshot for {Season}", snapshot.SeasonKey);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SeasonRecap: failed to save snapshot for {Season}", snapshot.SeasonKey);
            }
        }

        public static SeasonRecapSnapshot? Load(string seasonKey)
        {
            try
            {
                var path = PathFor(seasonKey);
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<SeasonRecapSnapshot>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SeasonRecap: failed to load snapshot {Season}", seasonKey);
                return null;
            }
        }

        /// <summary>Most recently completed season's snapshot, or null if none exist yet.</summary>
        public static SeasonRecapSnapshot? LoadLatest()
        {
            var keys = ListSeasonKeys();
            return keys.Count == 0 ? null : Load(keys[0]);
        }

        /// <summary>Available snapshot season keys, newest first.</summary>
        public static List<string> ListSeasonKeys()
        {
            try
            {
                if (!Directory.Exists(SnapshotDir)) return new List<string>();
                return Directory.GetFiles(SnapshotDir, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(k => !string.IsNullOrEmpty(k))
                    .OrderByDescending(k => k, StringComparer.Ordinal)
                    .Cast<string>()
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SeasonRecap: failed to list snapshots");
                return new List<string>();
            }
        }

        public static bool HasAnySnapshot() => ListSeasonKeys().Count > 0;
    }
}
