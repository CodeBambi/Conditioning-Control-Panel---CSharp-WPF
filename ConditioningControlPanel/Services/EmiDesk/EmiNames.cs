using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// The one place a machine key becomes something EMI is allowed to say out loud.
///
/// <para>LINES-SCHEMA 5.3 is strict about this: <c>{target}</c> reaches the engine as a lowercase
/// human display name, never a raw key. The engine does not translate - it substitutes - so a tab
/// key (<c>gradedintake</c>, <c>bambitakeover</c>), a rack key (<c>pinkfilter</c>) or a video file
/// name (<c>Deep_Drop_02.mp4</c>) has to be mapped by the HOOK before it is handed over. Every
/// mapping lives here so no hook has to grow its own half of the table.</para>
///
/// <para>Resolution order is deliberate: the ring's own catalogue first (it is already localized,
/// already the name the user sees on her cards, and already the name she used when she offered the
/// door), then the tab's own localization key, then nothing. <b>Nothing means null</b>, not the raw
/// key: a moment fired without <c>{target}</c> simply skips the token lines and draws a plain
/// sibling, which is the correct failure. Speaking "gradedintake" out loud is not.</para>
/// </summary>
internal static class EmiNames
{
    /// <summary>
    /// Tab / feature keys that have no ring card, mapped to the localization key of the name the
    /// nav rail already shows. Keys are the strings that actually arrive at the hooks: the bark
    /// vocabulary (which renames "play" to the legacy "lab"), plus the two window-only doors that
    /// return before <c>ShowTab</c>'s switch.
    ///
    /// <para>MOMENTS 4.E: <c>"settings"</c> is HOME. The real settings door is <c>"appsettings"</c>,
    /// which resolves through the ring catalogue instead.</para>
    /// </summary>
    private static readonly Dictionary<string, string> _tabLocKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["settings"] = "tab_dashboard",
        ["progression"] = "tab_progression",
        ["studio"] = "nav_door_studio",
        ["haptics"] = "tab_haptics",
        ["shelistening"] = "tab_shelistening",
        ["play"] = "tab_lab",
        ["lab"] = "tab_lab",
        ["deeper"] = "tab_deeper",
        ["gradedintake"] = "tab_gradedintake",
        ["blinktrainer"] = "tab_blink_trainer",
        ["availablesubjects"] = "tab_available_subjects",
        ["spiral"] = "tab_spiral",
        ["quests"] = "tab_quests",
        ["achievements"] = "tab_achievements",
        ["enhancements"] = "tab_enhancements",
        ["programs"] = "tab_programs",
        ["leaderboard"] = "tab_leaderboard",
        ["assets"] = "tab_assets",
        ["fyp"] = "tab_fyp",
        ["overlays"] = "tab_overlays",
    };

    /// <summary>
    /// Tab keys that ARE a ring target under a different name. The ring already owns this map
    /// privately for its usage scoring; this is the display-name half of the same idea, kept here
    /// so <see cref="Feature"/> can answer for a tab key without the ring having to expose it.
    /// </summary>
    private static readonly Dictionary<string, string> _tabTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["presets"] = "sessions",
        ["remotecontrol"] = "remote",
        ["bambitakeover"] = "takeover",
        ["exclusives"] = "vault",
        ["discord"] = "profile",
        ["appsettings"] = "settings",
        ["flash"] = "flashes",
        ["video"] = "videos",
        ["subliminal"] = "subliminals",
    };

    /// <summary>
    /// A tab key, rack key, ring target id or feature key as a lowercase display name, or null when
    /// nothing in the app knows a human name for it. Never throws and never returns the raw key.
    /// </summary>
    public static string? Feature(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        try
        {
            var k = key!.Trim();

            // 1. the ring catalogue, directly (arcademy, loom, vault, ...).
            var name = FromTarget(k);
            if (name != null) return name;

            // 2. a tab or rack key that IS a ring target under another id.
            if (_tabTargets.TryGetValue(k, out var mapped))
            {
                name = FromTarget(mapped);
                if (name != null) return name;
            }

            // 3. a door with no ring card: the name the nav rail shows.
            if (_tabLocKeys.TryGetValue(k, out var locKey)) return Localized(locKey);

            return null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] name lookup for {Key} failed", key);
            return null;
        }
    }

    /// <summary>A ring target id as a lowercase display name, or null when it is not in the catalogue.</summary>
    private static string? FromTarget(string id)
    {
        try
        {
            var t = EmiTargets.Find(id);
            if (t == null) return null;
            return Clean(t.Label);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] target label for {Id} failed", id);
            return null;
        }
    }

    /// <summary>
    /// A localization key as a lowercase display name, or null when the key is missing.
    /// <c>LocalizationManager.Get</c> hands back the key itself on a miss (it is a development aid),
    /// so an unresolved key would otherwise be spoken verbatim.
    /// </summary>
    private static string? Localized(string locKey)
    {
        try
        {
            var v = Loc.Get(locKey);
            if (string.IsNullOrWhiteSpace(v)) return null;
            if (string.Equals(v, locKey, StringComparison.Ordinal)) return null;
            return Clean(v);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] localized name for {Key} failed", locKey);
            return null;
        }
    }

    /// <summary>
    /// A video path or title as a lowercase display name: no folders, no extension, separators back
    /// to spaces. <c>videos\Deep_Drop-02.mp4</c> becomes <c>deep drop 02</c>.
    /// </summary>
    public static string? VideoName(string? pathOrTitle)
    {
        if (string.IsNullOrWhiteSpace(pathOrTitle)) return null;
        try
        {
            var s = pathOrTitle!.Trim();
            // Path.GetFileNameWithoutExtension throws on the invalid characters a title may carry,
            // so a title that is not a path is used as-is rather than rejected.
            try
            {
                var stem = Path.GetFileNameWithoutExtension(s);
                if (!string.IsNullOrWhiteSpace(stem)) s = stem;
            }
            catch { }

            var sb = new StringBuilder(s.Length);
            foreach (var c in s) sb.Append(c == '_' || c == '-' || c == '.' ? ' ' : c);
            return Clean(sb.ToString());
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] video name for {Path} failed", pathOrTitle);
            return null;
        }
    }

    /// <summary>
    /// An achievement id as its unlocked display name (loc key <c>achievement_&lt;id&gt;_name</c>),
    /// or null when the id is unknown.
    /// </summary>
    public static string? Achievement(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return Localized("achievement_" + id!.Trim() + "_name");
    }

    /// <summary>
    /// An XP source enum as a lowercase phrase (<c>SessionComplete</c> -&gt; <c>session complete</c>).
    /// The sources are code identifiers with no localization of their own, so this is the honest
    /// best a line can say about where the points came from.
    /// </summary>
    public static string? XpSource(object? source)
    {
        if (source == null) return null;
        try
        {
            var s = source.ToString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (string.Equals(s, "Other", StringComparison.OrdinalIgnoreCase)) return null;

            var sb = new StringBuilder(s!.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return Clean(sb.ToString());
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] xp source name failed");
            return null;
        }
    }

    /// <summary>Collapse whitespace and lowercase. Null for anything that ends up empty.</summary>
    private static string? Clean(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var parts = v!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        return string.Join(" ", parts).ToLowerInvariant();
    }
}
