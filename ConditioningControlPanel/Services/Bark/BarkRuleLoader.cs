using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Bark
{
    /// <summary>
    /// Loads bark rule manifests from disk. Two-tier, mirroring how
    /// <see cref="CompanionPhraseService.VoiceLineFolder"/> resolves audio: the embedded
    /// base manifest ships with the app; the active mod may ship its own
    /// <c>bark_rules.json</c> alongside its companion audio, which overrides/extends the
    /// base.
    ///
    /// Merge is FIELD-LEVEL by rule id: a mod rule that supplies only
    /// <c>{ id, variant_pool }</c> inherits trigger / conditions / priority / cooldown /
    /// scope / class / mood from the base rule of the same id. Only the fields the mod
    /// actually specifies override the base (omitted fields fall back). This lets skins
    /// (Drone/Kept/…) ship content-only manifests. Array fields (e.g. variant_pool) are
    /// REPLACED, not concatenated, so a skin fully owns its line set.
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

        private static readonly JsonMergeSettings MergeSettings = new()
        {
            MergeArrayHandling = MergeArrayHandling.Replace,      // skin owns its variant_pool outright
            MergeNullValueHandling = MergeNullValueHandling.Ignore // explicit null in mod won't blank a base field
        };

        /// <summary>
        /// Build the active rule set: load embedded base first, then field-level merge the
        /// active mod's rules over it (same id → per-field override). Never throws — a
        /// missing/garbled manifest yields whatever loaded successfully (possibly empty).
        /// </summary>
        public static BarkRuleSet Load()
        {
            // Merge at the JSON-object level so we can tell "field omitted" from "field = default".
            var merged = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

            int baseCount = MergeFile(merged, EmbeddedManifestPath, "embedded");
            int modCount = 0;
            var modPath = ActiveModManifestPath;
            if (modPath != null)
                modCount = MergeFile(merged, modPath, "mod");

            var rules = new List<BarkRule>();
            foreach (var obj in merged.Values)
            {
                try
                {
                    var rule = obj.ToObject<BarkRule>();
                    if (rule != null && rule.IsValid())
                        rules.Add(rule);
                    else
                        App.Logger?.Warning("BarkRuleLoader: merged rule invalid after merge (id='{Id}')", rule?.Id);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "BarkRuleLoader: failed to materialize a merged rule");
                }
            }

            App.Logger?.Information(
                "BarkRuleLoader: loaded {Total} rules ({Base} base, {Mod} mod-overlay; field-level merge)",
                rules.Count, baseCount, modCount);

            return new BarkRuleSet(rules);
        }

        /// <summary>
        /// Parse a manifest and merge each rule object into <paramref name="merged"/> by id.
        /// New id → inserted; existing id → field-level merge (incoming fields win).
        /// Returns the count of rule objects seen.
        /// </summary>
        private static int MergeFile(Dictionary<string, JObject> merged, string path, string source)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return 0;

                var arr = JArray.Parse(json);
                int n = 0;
                foreach (var token in arr)
                {
                    if (token is not JObject obj) continue;
                    var id = obj.Value<string>("id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        App.Logger?.Warning("BarkRuleLoader: skipped a rule with no id in {Source} manifest", source);
                        continue;
                    }

                    if (merged.TryGetValue(id, out var existing))
                        existing.Merge(obj, MergeSettings); // mutates existing in place; mod fields override base
                    else
                        merged[id] = obj;

                    n++;
                }
                return n;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BarkRuleLoader: failed to read {Source} manifest at {Path}", source, path);
                return 0;
            }
        }
    }
}
