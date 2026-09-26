using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>
/// What the menu says about each price row, kept out of the view: the loc keys for a row's short
/// name, its "where" line, its one-line flavour and its plain explanation, and which feature art
/// the row wears. The page builds its rows from <see cref="TabPrices.All"/> and asks here for the
/// dressing, so a row with no copy still renders (its id for a name, no picture) and copy for a
/// row that is not on the table is simply never asked for.
/// </summary>
public static class TabMenuCopy
{
    /// <summary>The jackpot is not a price row (it wipes, it does not book) but it sits on the
    /// menu, gold and dashed, so it has copy like the rows do.</summary>
    public const string JackpotId = "jackpot";

    public static string ShortKey(string id) => "chaster_short_" + id;
    public static string WhereKey(string id) => "chaster_where_" + id;
    public static string FlavourKey(string id) => "chaster_flavour_" + id;
    public static string WhyKey(string id) => "chaster_why_" + id;

    /// <summary>Feature art per row, as a path under Resources/. Rows share pictures where they
    /// share a feature; a row not listed here draws no thumbnail.</summary>
    private static readonly Dictionary<string, string> Art = new(StringComparer.Ordinal)
    {
        [CircesMisses.EventId] = "features/Phrase_Lock.png",
        ["typo"] = "features/Phrase_Lock.png",
        ["lockcard"] = "features/Phrase_Lock.png",
        ["attention"] = "features/awareness.png",
        ["mantra"] = "features/bouncing_text.png",
        ["program_skipped"] = "features/deeper.png",
        ["program_done"] = "features/deeper.png",
        ["escape"] = "features/takeover.png",
        ["watcher"] = "features/lab_focusgaze_hero.png",
        ["remote_media"] = "features/remote_control.png",
        ["remote_video"] = "features/remote_control.png",
        ["melt"] = "features/backroom.png",
        ["bubbles"] = "features/Bubble_pop.png",
        ["natasha"] = "features/Bubble_pop.png",
        ["ball"] = "features/arcademy.png",
        ["wall"] = "features/arcademy.png",
        ["padlock"] = "features/race.png",
        ["crash"] = "features/race.png",
        ["session"] = "features/spiral_overlay.png",
        ["quest"] = "features/free_today_stamp.png",
        ["quest_weekly"] = "features/vault.png",
        ["video"] = "features/mandatory_videos.png",
        ["levelup"] = "features/4new.png",
        ["leash"] = "features/remote_control.png",
        ["leash_credit"] = "features/remote_control.png",
        [TabDayEnd.IdleEventId] = "features/spiral_overlay.png",
        [TabDayEnd.DailiesEventId] = "features/free_today_stamp.png",
        [TabDayEnd.StreakEventId] = "features/spiral_overlay.png",
        [JackpotId] = "features/backroom.png",
    };

    /// <summary>Rows that play another row's scene: the trailers page has 27 scenes and two
    /// rows about the same feature share one.</summary>
    private static readonly Dictionary<string, string> Vignette = new(StringComparer.Ordinal)
    {
        ["lockcard"] = "typo",
        ["quest_weekly"] = "quest",
        ["program_done"] = "program",
        ["program_skipped"] = "program",
        ["remote_video"] = "remote_media",
        ["natasha"] = "bubbles",
        ["leash"] = "detention",
        ["leash_credit"] = "pardon",
        [TabDayEnd.IdleEventId] = "session",
        [TabDayEnd.DailiesEventId] = "quest",
        [TabDayEnd.StreakEventId] = "session",
    };

    /// <summary>The scene id the trailers page mounts for a row: its own id unless it shares one.</summary>
    public static string VignetteFor(string id) =>
        id != null && Vignette.TryGetValue(id, out var v) ? v : id ?? "";

    /// <summary>The art for a row, or null when it has none.</summary>
    public static string? ArtFor(string? id) =>
        id != null && Art.TryGetValue(id, out var art) ? art : null;

    /// <summary>Which rows have a picture. The render test walks this against the resources on
    /// disk so a renamed png cannot become a blank thumbnail at runtime.</summary>
    public static IReadOnlyCollection<string> ArtIds => Art.Keys;

    /// <summary>The short name to print for a row: the localised one when it exists, the id
    /// otherwise, so a new price row shows up on the menu before its copy is written.</summary>
    public static string ShortName(string id, Func<string, string?> loc)
    {
        var key = ShortKey(id);
        var text = loc(key);
        return string.IsNullOrEmpty(text) || text == key ? id : text;
    }

    /// <summary>The tier badge a gate wears: 0 for none (free rows and Sparkles rows, which have
    /// no neon sign of their own), 1 or 2 for the two subscriber tiers.</summary>
    public static int BadgeTier(TabPriceGate gate) => gate switch
    {
        TabPriceGate.Tier1 => 1,
        TabPriceGate.Tier2 => 2,
        _ => 0,
    };
}
