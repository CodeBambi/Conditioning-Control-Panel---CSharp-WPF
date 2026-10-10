using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Bark
{
    /// <summary>
    /// Port of WPF 7.1.5 <c>Services/Bark/BarkRuleLoader.cs</c>: the shipped base manifest
    /// (Resources/sounds/companion_audio/bark_rules.json) with the active mod's overlay merged over it
    /// FIELD BY FIELD per rule id (arrays replaced, explicit nulls ignored). Never throws. Named apart
    /// from the WPF loader so the WPF head never sees two types of one name.
    /// </summary>
    public static class BarkRuleManifest
    {
        public const string ManifestFileName = "bark_rules.json";

        /// <summary>The embedded base manifest path (install dir first, as ContentLocator resolves it).</summary>
        public static string EmbeddedManifestPath =>
            ContentLocator.Resolve(Path.Combine(CompanionContentResolver.CompanionAudioRelativeDir, ManifestFileName));

        private static readonly JsonMergeSettings MergeSettings = new()
        {
            MergeArrayHandling = MergeArrayHandling.Replace,
            MergeNullValueHandling = MergeNullValueHandling.Ignore
        };

        /// <summary>Base first, then the active mod overlay. <paramref name="basePath"/> / <paramref name="modPath"/>
        /// override the resolution (tests).</summary>
        public static BarkRuleSet Load(string? basePath = null, string? modPath = null, bool resolveMod = true)
        {
            var merged = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            int baseCount = MergeFile(merged, basePath ?? EmbeddedManifestPath, "embedded");
            int modCount = 0;
            if (modPath != null) modCount = MergeFile(merged, modPath, "mod");
            else if (resolveMod)
            {
                try
                {
                    var pick = ModCompanionContent.ResolveActive(CompanionChannel.BarkRules);
                    if (pick.Found) modCount = MergeFile(merged, pick.Path!, "mod");
                }
                catch (Exception ex) { Log.Debug("BarkRuleManifest: mod overlay lookup failed: {E}", ex.Message); }
            }

            var rules = new List<BarkRule>();
            foreach (var obj in merged.Values)
            {
                try
                {
                    var rule = obj.ToObject<BarkRule>();
                    if (rule != null && rule.IsValid()) rules.Add(rule);
                    else Log.Warning("BarkRuleManifest: merged rule invalid after merge (id='{Id}')", rule?.Id);
                }
                catch (Exception ex) { Log.Warning(ex, "BarkRuleManifest: failed to materialize a merged rule"); }
            }
            Log.Information("BarkRuleManifest: loaded {Total} rules ({Base} base, {Mod} mod-overlay)", rules.Count, baseCount, modCount);
            return new BarkRuleSet(rules);
        }

        private static int MergeFile(Dictionary<string, JObject> merged, string path, string source)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return 0;
                int n = 0;
                foreach (var token in JArray.Parse(json))
                {
                    if (token is not JObject obj) continue;
                    var id = obj.Value<string>("id");
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    if (merged.TryGetValue(id, out var existing)) existing.Merge(obj, MergeSettings);
                    else merged[id] = obj;
                    n++;
                }
                return n;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BarkRuleManifest: failed to read {Source} manifest at {Path}", source, path);
                return 0;
            }
        }
    }
}
