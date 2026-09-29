using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaster;

public sealed record TabBillLine(string EventId, int Count, int Seconds);

/// <summary>
/// The bill: the receipt CCP prints when it closes. This run's lines folded by event, biggest
/// first, then the three totals the owner asked for (added, earned back, pushed to the lock).
/// Pure, so the receipt's arithmetic is tested without a window.
/// </summary>
public sealed record TabBill(IReadOnlyList<TabBillLine> Lines, int AddedSeconds, int EarnedBackSeconds, int PushedSeconds)
{
    public int NetSeconds => AddedSeconds + EarnedBackSeconds;

    /// <summary>Nothing happened this run: no receipt, not an empty one.</summary>
    public bool IsEmpty => Lines.Count == 0 && PushedSeconds == 0;

    /// <param name="pushedSeconds">Signed: what this run sent to the lock (negative = taken off).</param>
    public static TabBill Build(IEnumerable<TabEntry> entries, DateTime runStartUtc, int pushedSeconds)
    {
        var lines = entries
            .Where(e => e.AtUtc >= runStartUtc && e.Seconds != 0)
            // One event can both cost and pay (a program day done or skipped), and the receipt
            // shows those as two rows, so the sign is part of the key.
            .GroupBy(e => (e.EventId, Cost: e.Seconds > 0))
            .Select(g => new TabBillLine(g.Key.EventId, g.Sum(e => e.Count), g.Sum(e => e.Seconds)))
            .OrderByDescending(l => Math.Abs(l.Seconds))
            .ThenBy(l => l.EventId, StringComparer.Ordinal)
            .ToList();

        return new TabBill(
            lines,
            lines.Where(l => l.Seconds > 0).Sum(l => l.Seconds),
            lines.Where(l => l.Seconds < 0).Sum(l => l.Seconds),
            pushedSeconds);
    }
}
