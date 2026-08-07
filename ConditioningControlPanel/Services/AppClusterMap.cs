using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Fine-grained app/window classification for the awareness-gated bark rules: maps a window-title
    /// substring to an <c>app_cluster</c> id (game_competitive, site_doomscroll, …) or, for bespoke
    /// single titles, to an <c>app</c> id (hades, obs, discord). This is a layer ON TOP of
    /// <see cref="WindowAwarenessService"/>'s broad <see cref="ActivityCategory"/>.
    ///
    /// The table is data-driven so it can be extended WITHOUT touching bark logic: drop an
    /// <c>app_clusters.json</c> into the companion-audio resource folder (it auto-deploys with the
    /// other Resources\sounds content). When that file is present it is authoritative; otherwise the
    /// embedded defaults below are used. Matching is case-insensitive, longest-substring-wins, with
    /// bespoke <c>apps</c> taking precedence over <c>clusters</c>.
    ///
    /// Privacy: only the resolved id is ever surfaced — the raw window title is never stored or logged
    /// (consistent with WindowAwarenessService). All of this is reached only while AwarenessMode is on,
    /// because that toggle is what runs WindowAwarenessService at all.
    /// </summary>
    public static class AppClusterMap
    {
        public const string FileName = "app_clusters.json";

        // id -> lowercased title substrings. Insertion order is irrelevant (longest match wins).
        private static Dictionary<string, string[]> _clusters = DefaultClusters;
        private static Dictionary<string, string[]> _apps = DefaultApps;
        private static bool _loaded;

        private static string FilePath =>
            Path.Combine(CompanionPhraseService.CompanionAudioFolder, FileName);

        /// <summary>Largest override file that will be read at all. A mod file, not a database.</summary>
        public const int MaxFileBytes = 256 * 1024;

        /// <summary>
        /// Cluster ids the awareness code branches on BY NAME and which therefore may never disappear
        /// from the table, whatever an override says. Today that is the adult cluster, on which
        /// <c>ContextFrame.IsAdultCluster</c>, the cloud projection's withholding branch, the day arc's
        /// collapse, the title rules, <c>FrameDrop.AdultRecordingOff</c> and
        /// <c>DndGate.AdultReactionsOff</c> all depend.
        /// </summary>
        public static readonly string[] RequiredClusterIds = { Awareness.AwarenessClusters.Adult };

        /// <summary>
        /// Load the external override once (if present) by MERGING it over the embedded defaults.
        /// Falls back to embedded defaults on any error.
        ///
        /// <para><b>Merge, not replace.</b> This file is a documented extension point — the
        /// creator-mod pipeline emits one — so a mod that adds three bespoke apps used to REPLACE the
        /// entire table, taking <c>site_eh</c> with it. Nothing errored and nothing logged; the adult
        /// cluster simply stopped existing, and with it every rule in Awareness v2 that keys off it, so
        /// adult app ids and display names started crossing the wire. Anything the override does not
        /// mention keeps its embedded value, and the required ids are re-injected if a well-formed
        /// override happens to omit them.</para>
        /// </summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var path = FilePath;
                if (!File.Exists(path)) return; // keep embedded defaults

                var size = new FileInfo(path).Length;
                if (size > MaxFileBytes)
                {
                    App.Logger?.Warning("AppClusterMap: override is {Bytes} bytes (cap {Cap}) — using embedded defaults",
                        size, MaxFileBytes);
                    return;
                }

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return;
                var root = JObject.Parse(json);

                _clusters = Merge(DefaultClusters, ParseSection(root["clusters"] as JObject));
                _apps = Merge(DefaultApps, ParseSection(root["apps"] as JObject));

                // Fail closed on the ids the privacy rules name: an override that drops one does not get
                // to widen what leaves the machine.
                foreach (var required in RequiredClusterIds)
                {
                    if (_clusters.ContainsKey(required)) continue;
                    if (!DefaultClusters.TryGetValue(required, out var embedded)) continue;
                    _clusters[required] = embedded;
                    App.Logger?.Warning(
                        "AppClusterMap: override omitted the '{Cluster}' cluster — re-injected the embedded terms",
                        required);
                }

                App.Logger?.Information("AppClusterMap: loaded {Clusters} clusters, {Apps} bespoke apps from {Path}",
                    _clusters.Count, _apps.Count, path);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AppClusterMap: failed to load override — using embedded defaults");
                _clusters = DefaultClusters;
                _apps = DefaultApps;
            }
        }

        /// <summary>Override entries win per id; every id the override does not mention survives.</summary>
        private static Dictionary<string, string[]> Merge(
            Dictionary<string, string[]> embedded, Dictionary<string, string[]> overrides)
        {
            var merged = new Dictionary<string, string[]>(embedded, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in overrides) merged[pair.Key] = pair.Value;
            return merged;
        }

        private static Dictionary<string, string[]> ParseSection(JObject? section)
        {
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (section == null) return map;
            foreach (var prop in section.Properties())
            {
                var arr = (prop.Value as JArray)?.Select(t => (t?.ToString() ?? "").ToLowerInvariant())
                              .Where(s => s.Length > 0).ToArray();
                if (arr is { Length: > 0 }) map[prop.Name] = arr;
            }
            return map;
        }

        /// <summary>
        /// Classify a raw window title into (cluster, app) ids. Either may be null. Bespoke apps win over
        /// clusters; within each, the longest matching substring wins (so "youtube music" beats "youtube").
        /// </summary>
        public static (string? cluster, string? app) Classify(string? windowTitle)
        {
            if (string.IsNullOrWhiteSpace(windowTitle)) return (null, null);
            EnsureLoaded();
            var t = windowTitle.ToLowerInvariant();
            string? app = BestMatch(t, _apps);
            string? cluster = BestMatch(t, _clusters);
            return (cluster, app);
        }

        /// <summary>Id whose longest substring is contained in <paramref name="title"/>, or null.</summary>
        private static string? BestMatch(string title, Dictionary<string, string[]> table)
        {
            string? bestId = null;
            int bestLen = 0;
            foreach (var kvp in table)
                foreach (var needle in kvp.Value)
                    if (needle.Length > bestLen && title.Contains(needle))
                    {
                        bestLen = needle.Length;
                        bestId = kvp.Key;
                    }
            return bestId;
        }

        // ----- embedded defaults (mirrored by the shipped app_clusters.json) -----

        private static readonly Dictionary<string, string[]> DefaultClusters = new(StringComparer.OrdinalIgnoreCase)
        {
            ["game_competitive"] = new[] { "valorant", "league of legends", "counter-strike", "cs2", "csgo",
                "overwatch", "apex legends", "rainbow six", "dota 2", "rocket league", "fortnite", "call of duty", "warzone" },
            ["game_cozy"] = new[] { "stardew valley", "animal crossing", "minecraft", "terraria", "the sims",
                "cozy grove", "spiritfarer", "unpacking", "powerwash" },
            ["game_rpg"] = new[] { "elden ring", "baldur's gate", "skyrim", "the witcher", "cyberpunk",
                "final fantasy", "persona", "dark souls", "fallout", "diablo", "path of exile" },
            ["game_gacha"] = new[] { "genshin impact", "honkai", "star rail", "fate/grand", "arknights",
                "blue archive", "nikke", "wuthering waves", "zenless" },
            ["game_mmo"] = new[] { "world of warcraft", "final fantasy xiv", "ffxiv", "lost ark",
                "guild wars", "new world", "runescape", "black desert" },
            ["game_social_vr"] = new[] { "vrchat", "chilloutvr", "resonite", "rec room", "neos" },
            ["site_doomscroll"] = new[] { "twitter", "x.com", "reddit", "tiktok", "tumblr", "facebook",
                "instagram", "threads", "bluesky" },
            ["site_video"] = new[] { "youtube", "netflix", "twitch", "hulu", "disney+", "hbo max",
                "crunchyroll", "prime video" },
            ["site_music"] = new[] { "spotify", "soundcloud", "apple music", "youtube music" },
            ["site_shopping"] = new[] { "amazon", "ebay", "etsy", "aliexpress", "shein", "throne", "wishtender", "wish.com" },
            ["site_eh"] = new[] { "pornhub", "xvideos", "xhamster", "e-hentai", "nhentai", "rule34",
                "hypnotube", "bambicloud", "adult content" },
        };

        private static readonly Dictionary<string, string[]> DefaultApps = new(StringComparer.OrdinalIgnoreCase)
        {
            ["hades"] = new[] { "hades" },
            ["obs"] = new[] { "obs studio", "obs " },
            ["discord"] = new[] { "discord" },
        };
    }
}
