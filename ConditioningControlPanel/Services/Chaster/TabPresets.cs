using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>One named set of price rows. <see cref="PriceIds"/> is exactly what gets written to
/// <c>AppSettings.ChasterPrices</c> when the player picks it.</summary>
public sealed record TabPreset(string Id, IReadOnlyList<string> PriceIds);

/// <summary>
/// Three ready-made sets of prices, so the page can lead with a choice instead of twenty-two
/// switches. A dense grid of checkboxes reads as homework; a preset is one press and the rows
/// behind Customize still say exactly what it did.
///
/// <para><b>Why these three.</b> The price table has two halves (rows that cost time and rows that
/// earn it back), so the useful presets are the two ends and one middle.</para>
/// <list type="bullet">
/// <item><b>Gentle</b> is every row that earns time back plus the two cheapest costs (a lock-card
/// typo at 0:15, a racing crash at 0:05). It leans down: someone who plays at all takes time OFF.
/// It is the honest first set for anyone who has never let an app touch their lock.</item>
/// <item><b>Strict</b> is every row that costs, plus one way down (a finished session, -10:00).
/// Not zero ways down: with none, the balance only ever climbs to the 3 h backlog cap and the
/// daily push becomes a formality, which is not a game, it is a countdown.</item>
/// <item><b>Circe's choice</b> is the middle and the one that works on ANY account: only free-tier
/// rows, both directions, the everyday ones (lock cards, attention, mantras, sessions, quests,
/// videos, level ups, programs, days away). Nothing in it is gated behind tier 1, tier 2 or
/// Sparkles, so it never offers a price for a feature the player cannot reach.</item>
/// </list>
///
/// <para>Presets are only an offer, like every single row: picking one writes the ids and the
/// player can still switch any of them off. <see cref="TabPrices.NeverPriced"/> ids can never
/// appear here, and every id is checked against the live table.</para>
/// </summary>
public static class TabPresets
{
    public const string Gentle = "gentle";
    public const string Strict = "strict";
    public const string Circe = "circe";

    /// <summary>What <see cref="Match"/> answers for a hand-edited set: not a preset, still a set.</summary>
    public const string Custom = "custom";

    private static readonly string[] GentleIds =
    {
        // everything that earns time back
        "lockcard", "session", "quest", "quest_weekly", "video", "levelup", "program_done", "wall",
        // and the two smallest costs, so the tab is not a one-way street
        "typo", "crash",
    };

    private static readonly string[] StrictIds =
    {
        CircesMisses.EventId, "typo", "attention", "mantra", "program_skipped",
        "remote_media", "remote_video", "escape", "watcher",
        "melt", "bubbles", "ball", "padlock", "crash",
        // the one way down
        "session",
    };

    private static readonly string[] CirceIds =
    {
        // costs
        CircesMisses.EventId, "typo", "attention", "mantra", "program_skipped", "melt",
        // earns back
        "lockcard", "session", "quest", "video", "levelup", "program_done",
    };

    public static readonly IReadOnlyList<TabPreset> All = new[]
    {
        new TabPreset(Gentle, Clean(GentleIds)),
        new TabPreset(Strict, Clean(StrictIds)),
        new TabPreset(Circe, Clean(CirceIds)),
    };

    /// <summary>Only ids that are really on the price table and are allowed to carry a price. A
    /// typo in a preset must drop the row, never invent one.</summary>
    private static IReadOnlyList<string> Clean(IEnumerable<string> ids) =>
        ids.Where(id => TabPrices.Find(id) != null && !TabPrices.NeverPriced.Contains(id))
           .Distinct(StringComparer.Ordinal)
           .ToList();

    public static TabPreset? Find(string? id) =>
        id == null ? null : All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    /// <summary>The ids to write for a preset, or an empty list for an unknown one (which writes
    /// nothing rather than clearing the player's set by accident).</summary>
    public static IReadOnlyList<string> Apply(string? presetId) =>
        Find(presetId)?.PriceIds ?? Array.Empty<string>();

    /// <summary>Which chip is lit for the set the player currently has on: a preset id when it
    /// matches one exactly (order and repeats do not count), <see cref="Custom"/> when it is a set
    /// of its own, and null when nothing is on at all - nothing on is not a choice yet, so no chip
    /// lights and the page can say "pick a set".</summary>
    public static string? Match(IEnumerable<string>? enabledIds)
    {
        var on = new HashSet<string>(enabledIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        on.RemoveWhere(id => TabPrices.Find(id) == null || TabPrices.NeverPriced.Contains(id));
        if (on.Count == 0) return null;
        foreach (var preset in All)
            if (on.SetEquals(preset.PriceIds)) return preset.Id;
        return Custom;
    }

    public static string NameKey(string presetId) => "chaster_preset_" + presetId;

    public static string HintKey(string presetId) => "chaster_preset_" + presetId + "_hint";
}
