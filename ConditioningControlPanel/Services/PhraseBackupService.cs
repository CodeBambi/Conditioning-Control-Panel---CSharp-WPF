using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Exports/imports the user's editable phrase pools (lock-card phrases, subliminals,
    /// bouncing text, attention words, mantras, custom triggers, custom companion phrases)
    /// to a small standalone <c>.ccpphrases.json</c> file. This is a user-facing safety net
    /// so a bad update (or a move to a new machine) can never permanently cost someone the
    /// phrases they wrote — they can export a backup and re-import it.
    ///
    /// The set is captured BY PROPERTY NAME from the serialized settings rather than as a
    /// fixed typed DTO, so adding/renaming a pool's type later can't silently drop it or
    /// break an old backup file.
    /// </summary>
    public class PhraseBackupService
    {
        public const string Schema = "ccp-phrases/v1";

        /// <summary>
        /// AppSettings property names that hold user-editable phrase content. Each is copied
        /// verbatim (as JSON) on export and applied back on import. Includes the per-mod /
        /// per-mode variants and the add/remove tracking sets so a restore is faithful and the
        /// cross-mod prune doesn't later delete restored custom phrases.
        /// </summary>
        internal static readonly string[] PhraseProperties =
        {
            // Subliminals (+ tracking so restored custom phrases survive the prune)
            "SubliminalPool", "SubliminalPoolByMod", "SubliminalPoolByMode",
            "UserAddedSubliminals", "RemovedDefaultSubliminals",
            // Lock card phrases
            "LockCardPhrases", "LockCardPhrasesByMod", "LockCardPhrasesByMode",
            // Bouncing text
            "BouncingTextPool", "BouncingTextPoolByMod",
            // Attention words
            "AttentionPool", "AttentionPoolByMod", "AttentionPoolByMode",
            // Mantras
            "MantraPool",
            // Custom triggers
            "CustomTriggers", "CustomTriggersByMod",
            // Custom companion phrases
            "CustomCompanionPhrases",
        };

        /// <summary>Suggested file name for an export dialog.</summary>
        public static string GetExportFileName()
            => $"ccp-phrases-{DateTime.Now:yyyyMMdd}.ccpphrases.json";

        /// <summary>
        /// Writes the current phrase pools to <paramref name="filePath"/>. Returns a rough
        /// count of phrase entries written (for a confirmation message).
        /// </summary>
        public int Export(AppSettings settings, string filePath)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var full = JObject.FromObject(settings);
            var phrases = new JObject();
            foreach (var name in PhraseProperties)
            {
                var token = full[name];
                if (token != null && token.Type != JTokenType.Null)
                    phrases[name] = token;
            }

            var backup = new JObject
            {
                ["schema"] = Schema,
                ["exported_at"] = DateTime.UtcNow.ToString("o"),
                ["app_version"] = Services.UpdateService.AppVersion,
                ["phrases"] = phrases,
            };

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(filePath, backup.ToString(Formatting.Indented));

            return CountEntries(phrases);
        }

        /// <summary>
        /// Validates that <paramref name="filePath"/> is a phrase backup this app understands.
        /// </summary>
        public bool Validate(string filePath, out string error)
        {
            error = "";
            try
            {
                if (!File.Exists(filePath)) { error = "File not found"; return false; }
                var obj = JObject.Parse(File.ReadAllText(filePath));
                var schema = obj["schema"]?.ToString();
                if (schema != Schema) { error = $"Unrecognized file (schema '{schema}')"; return false; }
                if (obj["phrases"] is not JObject p || !p.HasValues) { error = "No phrases in file"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Applies the phrase pools from a backup file onto <paramref name="settings"/> in place,
        /// REPLACING each pool (an import is a restore, not a merge). Unknown/legacy members are
        /// skipped rather than aborting the whole import. Returns the count of entries applied.
        /// Caller is responsible for persisting (App.Settings.Save()) and refreshing UI.
        /// </summary>
        public int Import(AppSettings settings, string filePath)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var obj = JObject.Parse(File.ReadAllText(filePath));
            if (obj["schema"]?.ToString() != Schema)
                throw new InvalidOperationException("File is not a CCP phrase backup.");

            if (obj["phrases"] is not JObject phrases)
                throw new InvalidOperationException("Backup contains no phrases.");

            // Only apply whitelisted phrase properties — never let a crafted file populate
            // arbitrary settings (auth tokens, feature gates, etc.).
            var filtered = new JObject();
            foreach (var name in PhraseProperties)
            {
                var token = phrases[name];
                if (token != null && token.Type != JTokenType.Null)
                    filtered[name] = token;
            }

            var populateSettings = new JsonSerializerSettings
            {
                // Replace so an imported pool fully overwrites the current one (restore semantics),
                // and tolerate a single unparseable member instead of failing the whole import.
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                Error = (sender, args) =>
                {
                    App.Logger?.Warning("Skipping unimportable phrase member '{Path}': {Error}",
                        args.ErrorContext.Path, args.ErrorContext.Error.Message);
                    args.ErrorContext.Handled = true;
                }
            };

            JsonConvert.PopulateObject(filtered.ToString(), settings, populateSettings);
            App.Logger?.Information("Imported phrases from {Path}", filePath);
            return CountEntries(filtered);
        }

        /// <summary>Rough total of phrase entries across all pools, for confirmation messages.</summary>
        private static int CountEntries(JObject phrases)
        {
            int total = 0;
            foreach (var prop in phrases.Properties())
                total += CountToken(prop.Value);
            return total;
        }

        private static int CountToken(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Array:
                    return ((JArray)token).Count;
                case JTokenType.Object:
                    // Flat pool (phrase -> bool) counts its keys; nested (mod -> pool) sums children.
                    var o = (JObject)token;
                    if (o.Properties().All(p => p.Value.Type is JTokenType.Boolean))
                        return o.Count;
                    return o.Properties().Sum(p => CountToken(p.Value));
                default:
                    return 0;
            }
        }
    }
}
