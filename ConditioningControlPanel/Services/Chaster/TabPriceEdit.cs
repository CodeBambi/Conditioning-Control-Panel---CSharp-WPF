using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>
/// The player's own figure for a price row (owner, 2026-09-29, tester feedback: "clicking on the
/// time lets you set it"). The table in <see cref="TabPrices"/> stays the default; an edit is a
/// machine-local override (<c>AppSettings.ChasterPriceOverrides</c>, never cloud-synced because
/// every Chaster* setting stays on the PC).
///
/// <para>Rules. The SIGN never changes: a cost stays a cost and an earn-back stays an earn-back.
/// The size is clamped to <see cref="MinSeconds"/>..<see cref="MaxSeconds"/> every time it is
/// read, so a hand-edited settings file cannot book past it, and every booking still goes
/// through the day and backlog limits after that. Only rows booked through
/// <see cref="TabPrices.Resolve"/> with one fixed figure take an edit: the way out never has a
/// figure, "misses" doubles by itself, the leash and a stake name their own size, the
/// day-end rows are the service's own verdicts, and a row with a daily use ceiling (the escape
/// row: three a day, so 9:00 at most) keeps its figure, because that ceiling is a safety promise.
/// Figures are set only while no lock runs; the page enforces that with <see cref="CanEdit"/>.</para>
/// </summary>
public static class TabPriceEdit
{
    public const int MinSeconds = 5;
    public const int MaxSeconds = 60 * 60;

    /// <summary>May this row's figure be set by the player at all: one fixed figure, never a
    /// way out, not a day-end verdict, and not a row whose day is capped by a count of uses.</summary>
    public static bool Editable(string? id) =>
        TabMenuCopy.HasFixedFigure(id)
        && !TabPrices.NeverPriced.Contains(id!)
        && !TabDayEnd.ServiceRows.Contains(id!)
        && TabPrices.DailyMaxUses(id!) == null;

    /// <summary>Figures move only while no lock is running: not linked, or linked with no active
    /// lock. Chaster out of reach counts as a lock (it may well be one).</summary>
    public static bool CanEdit(bool linked, LockLookup lookup) => !linked || lookup == LockLookup.None;

    /// <summary>The signed seconds this row books for one event, with the player's figure when
    /// there is one. Unknown ids read 0.</summary>
    public static int Effective(string id, IReadOnlyDictionary<string, int>? overrides) =>
        TabPrices.Find(id) is { } price ? Effective(price, overrides) : 0;

    public static int Effective(TabPrice price, IReadOnlyDictionary<string, int>? overrides)
    {
        if (overrides == null || !Editable(price.Id) || !overrides.TryGetValue(price.Id, out var size)) return price.Seconds;
        var magnitude = Math.Clamp(Math.Abs((long)size), MinSeconds, MaxSeconds);
        return (int)(price.Seconds < 0 ? -magnitude : magnitude);
    }

    /// <summary>The row as the page shows it: the default record with the effective figure.</summary>
    public static TabPrice Shown(TabPrice price, IReadOnlyDictionary<string, int>? overrides) =>
        price with { Seconds = Effective(price, overrides) };

    public static bool IsEdited(string id, IReadOnlyDictionary<string, int>? overrides) =>
        TabPrices.Find(id) is { } price && Effective(price, overrides) != price.Seconds;

    /// <summary>What the player typed, as unsigned seconds. "2:30" and "1:05:00" are clock
    /// figures, a bare number is minutes ("5" is 5:00). Signs are ignored: the row keeps its own.
    /// Null for anything that does not read as a time (the edit is then dropped); empty text is
    /// 0, which <see cref="With"/> reads as "back to the default".</summary>
    public static int? Parse(string? text)
    {
        var t = (text ?? "").Trim().TrimStart('+', '-', '−').Trim();
        if (t.Length == 0) return 0;
        var parts = t.Split(':');
        if (parts.Length > 3) return null;
        long total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
            // Past the first part a field is a clock field: 0..59.
            if (i > 0 && (n > 59 || parts[i].Length != 2)) return null;
            total = total * 60 + n;
        }
        if (parts.Length == 1) total *= 60;
        return total > int.MaxValue ? null : (int)total;
    }

    /// <summary>The overrides after setting one row. <paramref name="seconds"/> null or 0, or the
    /// default's own size, drops the row's entry (back to the table). A new map every time: the
    /// service reads the setting from any thread and must never see one being edited.</summary>
    public static Dictionary<string, int> With(IReadOnlyDictionary<string, int>? overrides, string id, int? seconds)
    {
        var next = new Dictionary<string, int>(StringComparer.Ordinal);
        if (overrides != null)
            foreach (var (k, v) in overrides)
                if (Editable(k)) next[k] = v;
        if (!Editable(id) || TabPrices.Find(id) is not { } price) return next;
        next.Remove(id);
        if (seconds is { } s && s != 0)
        {
            var magnitude = Math.Clamp(Math.Abs(s), MinSeconds, MaxSeconds);
            if (magnitude != Math.Abs(price.Seconds)) next[id] = magnitude;
        }
        return next;
    }
}
