using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Core.Services.Awareness;

/// <summary>
/// Fine-grained app/window classification for the awareness-gated bark rules: maps a window-title
/// substring to an <c>app_cluster</c> id (game_competitive, site_doomscroll, …) or, for bespoke
/// single titles, to an <c>app</c> id (hades, obs, discord). This is a layer ON TOP of the
/// awareness engine's broad <see cref="ActivityCategory"/>.
///
/// Ported verbatim from the WPF head's embedded defaults (Services/AppClusterMap.cs:104-137
/// tables, :83-108 Classify/BestMatch). The WPF-side optional <c>app_clusters.json</c> override
/// (loaded from the companion-audio resource folder, WPF Services/AppClusterMap.cs:38-61) is NOT
/// ported in this slice — the embedded defaults are authoritative in the port until the AI-10
/// bark row needs the override hook.
///
/// Privacy: only the resolved id is ever surfaced — the raw window title is never stored or logged
/// (consistent with the awareness engine). All of this is reached only while AwarenessMode is on,
/// because that toggle is what runs the awareness engine at all.
/// </summary>
public static class AppClusterMap
{
    /// <summary>
    /// Classify a raw window title into (cluster, app) ids. Either may be null. Bespoke apps win over
    /// clusters; within each, the longest matching substring wins (so "youtube music" beats "youtube").
    /// (WPF Services/AppClusterMap.cs:83-92)
    /// </summary>
    public static (string? Cluster, string? App) Classify(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return (null, null);
        var t = windowTitle.ToLowerInvariant();
        string? app = BestMatch(t, DefaultApps);
        string? cluster = BestMatch(t, DefaultClusters);
        return (cluster, app);
    }

    /// <summary>Id whose longest substring is contained in <paramref name="title"/>, or null.
    /// (WPF Services/AppClusterMap.cs:95-108)</summary>
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

    // ----- embedded defaults (WPF Services/AppClusterMap.cs:112-129) -----

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

    // (WPF Services/AppClusterMap.cs:131-136)
    private static readonly Dictionary<string, string[]> DefaultApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hades"] = new[] { "hades" },
        ["obs"] = new[] { "obs studio", "obs " },
        ["discord"] = new[] { "discord" },
    };
}
