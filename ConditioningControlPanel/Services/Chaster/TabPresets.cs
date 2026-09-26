using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>One named set of price rows. <see cref="PriceIds"/> is exactly what gets written to
/// <c>AppSettings.ChasterPrices</c> when the player picks it, and the two limits go with it.</summary>
public sealed record TabPreset(string Id, IReadOnlyList<string> PriceIds, int DayMinutes, int BacklogMinutes);

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
/// <para><b>Each preset sets the stakes too (2026-09-26).</b> The prices only decide how fast a player
/// reaches the day limit; the limit decides what a month can cost. Locktober players are locked
/// all of October anyway, so what they weigh is how far PAST the lock end the tab can push them,
/// and one 3 h default for every preset meant a Gentle player could still end four days late.
/// Anchored to what Chaster locks do in the wild (published Locktober 2025 setups add 1-3 h per
/// wheel spin, dice roll or missed task; public locks are named "chance of November"): Gentle
/// 30 min a day (a full month of misses is under a day late), Strict 3 h (about four days, a
/// strict Chaster lock), Circe 6 h (about eight days, the heavy community locks). Raising a limit
/// through a preset waits a day like the slider does (<see cref="LimitChange"/>).</para>
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
        TabDayEnd.StreakEventId, "lockcard", "session", "quest", "quest_weekly", "video", "levelup", "program_done", "wall",
        // and the two smallest costs, so the tab is not a one-way street
        "typo", "crash",
    };

    private static readonly string[] StrictIds =
    {
        CircesMisses.EventId, "typo", "attention", NatashasFavourite.EventId, "mantra", "program_skipped",
        "remote_media", "remote_video", "escape", "watcher",
        "melt", "bubbles", "ball", "padlock", "crash",
        TabDayEnd.IdleEventId, TabDayEnd.DailiesEventId, TabDayEnd.HeatId,
        // the one way down
        "session",
    };

    private static readonly string[] CirceIds =
    {
        // costs
        CircesMisses.EventId, "typo", "attention", NatashasFavourite.EventId, "mantra", "program_skipped", "melt",
        TabDayEnd.DailiesEventId, TabDayEnd.HeatId,
        // earns back
        TabDayEnd.StreakEventId, "lockcard", "session", "quest", "video", "levelup", "program_done",
    };

    public static readonly IReadOnlyList<TabPreset> All = new[]
    {
        new TabPreset(Gentle, Clean(GentleIds), DayMinutes: 30, BacklogMinutes: 2 * 60),
        new TabPreset(Strict, Clean(StrictIds), DayMinutes: 3 * 60, BacklogMinutes: 12 * 60),
        new TabPreset(Circe, Clean(CirceIds), DayMinutes: 6 * 60, BacklogMinutes: 24 * 60),
    };

    /// <summary>Only ids that are really on the price table and are allowed to carry a price. A
    /// typo in a preset must drop the row, never invent one.</summary>
    private static IReadOnlyList<string> Clean(IEnumerable<string> ids) =>
        ids.Where(id => (TabPrices.Find(id) != null || TabPrices.Modifiers.Contains(id)) && !TabPrices.NeverPriced.Contains(id))
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
        on.RemoveWhere(id => (TabPrices.Find(id) == null && !TabPrices.Modifiers.Contains(id)) || TabPrices.NeverPriced.Contains(id));
        if (on.Count == 0) return null;
        foreach (var preset in All)
            if (on.SetEquals(preset.PriceIds)) return preset.Id;
        return Custom;
    }

    /// <summary>The month the stakes line is priced over: October.</summary>
    public const int MonthDays = 31;

    /// <summary>The most a preset can put on a lock in a month, every day at its limit. Under two
    /// days it reads in hours (rounded up), past that in whole days (rounded up).</summary>
    public static (int Value, bool Days) WorstMonth(TabPreset preset)
    {
        var hours = preset.DayMinutes * MonthDays / 60.0;
        return hours < 48 ? ((int)Math.Ceiling(hours), false) : ((int)Math.Ceiling(hours / 24), true);
    }

    /// <summary>The two limit settings after picking <paramref name="preset"/>: a lower limit lands
    /// now, a higher one waits its day, exactly as if the player had moved the sliders.</summary>
    public static (LimitSetting Day, LimitSetting Backlog) RequestLimits(TabPreset preset, LimitSetting day, LimitSetting backlog, DateTime utcNow)
    {
        var wanted = TabLimits.FromMinutes(preset.DayMinutes, preset.BacklogMinutes);
        return (LimitChange.Request(day, wanted.DailySeconds / 60, utcNow),
                LimitChange.Request(backlog, wanted.BacklogSeconds / 60, utcNow));
    }

    public static string NameKey(string presetId) => "chaster_preset_" + presetId;

    public static string HintKey(string presetId) => "chaster_preset_" + presetId + "_hint";
}
