using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Bark
{
    /// <summary>
    /// Loads bark rule manifests from disk. Two-tier, mirroring how
    /// <see cref="CompanionPhraseService.VoiceLineFolder"/> resolves audio: the embedded
    /// base manifest ships with the app; the active mod may ship its own
    /// <c>bark_rules.json</c> alongside its companion audio, which OVERRIDES/EXTENDS the
    /// base by rule id (mod rule wins on id collision).
    /// </summary>
    public static class BarkRuleLoader
    {
        public const string ManifestFileName = "bark_rules.json";

        /// <summary>Embedded base manifest: Resources/sounds/companion_audio/bark_rules.json.</summary>
        public static string EmbeddedManifestPath =>
            Path.Combine(CompanionPhraseService.CompanionAudioFolder, ManifestFileName);

        /// <summary>Active mod's manifest, if the mod ships one. Null when no mod / no file.</summary>
        public static string? ActiveModManifestPath
        {
            get
            {
                var modPath = App.Mods?.ActiveMod?.InstalledPath;
                if (string.IsNullOrEmpty(modPath)) return null;
                var p = Path.Combine(modPath, "resources", "sounds", "companion_audio", ManifestFileName);
                return File.Exists(p) ? p : null;
            }
        }

        /// <summary>
        /// Build the active rule set: load embedded base first, then overlay the active mod's
        /// rules (same id replaces). Never throws — a missing/garbled manifest yields whatever
        /// loaded successfully (possibly an empty set) and is logged.
        /// </summary>
        public static BarkRuleSet Load()
        {
            // Keyed by id so the mod overlay can replace base rules deterministically.
            var merged = new Dictionary<string, BarkRule>(StringComparer.OrdinalIgnoreCase);

            int baseCount = LoadInto(merged, EmbeddedManifestPath, "embedded");
            int modCount = 0;
            var modPath = ActiveModManifestPath;
            if (modPath != null)
                modCount = LoadInto(merged, modPath, "mod");

            App.Logger?.Information(
                "BarkRuleLoader: loaded {Total} rules ({Base} base, {Mod} mod-overlay)",
                merged.Count, baseCount, modCount);

            return new BarkRuleSet(merged.Values);
        }

        private static int LoadInto(Dictionary<string, BarkRule> merged, string path, string source)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return 0;

                var rules = JsonConvert.DeserializeObject<List<BarkRule>>(json);
                if (rules == null) return 0;

                int added = 0;
                foreach (var rule in rules)
                {
                    if (rule == null || !rule.IsValid())
                    {
                        App.Logger?.Warning("BarkRuleLoader: skipped invalid rule in {Source} manifest (id='{Id}')",
                            source, rule?.Id);
                        continue;
                    }
                    merged[rule.Id] = rule; // mod overlay replaces base on id collision
                    added++;
                }
                return added;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BarkRuleLoader: failed to read {Source} manifest at {Path}", source, path);
                return 0;
            }
        }
    }
}
