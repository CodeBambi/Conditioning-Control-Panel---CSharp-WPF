using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>The moments Circe has a word for. The four Bill* moments are the bill's verdict.</summary>
public enum CirceMoment { Held, Popped, Landed, Warmer, Cooler, BillCredit, BillEven, BillOwed, BillBig }

/// <summary>
/// Circe's lines (tab wave 1, 2026-09-29): a short line when a red bubble is held or popped, when
/// the tab lands on the lock, when her mood moves, and a verdict on the bill. Text only, never
/// spoken (house rule: no synthetic speech). Every line is a loc key, <c>chaster_line_*</c>.
///
/// <para><b>Rules</b>: never the same line twice in a row for a moment; at most one line every
/// <see cref="ThrottleSeconds"/>, except that a landed push and the bill always speak (and reset
/// the clock). Pure and deterministic with an injected <see cref="Random"/>.</para>
/// </summary>
public sealed class CirceLines
{
    public const double ThrottleSeconds = 20;

    /// <summary>A bill within this of zero reads as even: nothing worth a verdict either way.</summary>
    public const int EvenBandSeconds = 60;

    /// <summary>From here up the bill is a big one.</summary>
    public const int BigOwedSeconds = 30 * 60;

    /// <summary>The ids that raise a line when they book (lane RED defines natasha_held).</summary>
    public const string HeldEventId = "natasha_held";
    public const string PoppedEventId = NatashasFavourite.EventId;

    private static readonly IReadOnlyDictionary<CirceMoment, (string Stem, int Count)> Table =
        new Dictionary<CirceMoment, (string, int)>
        {
            [CirceMoment.Held] = ("held", 4),
            [CirceMoment.Popped] = ("popped", 4),
            [CirceMoment.Landed] = ("landed", 4),
            [CirceMoment.Warmer] = ("warmer", 4),
            [CirceMoment.Cooler] = ("cooler", 4),
            [CirceMoment.BillCredit] = ("bill_credit", 2),
            [CirceMoment.BillEven] = ("bill_even", 2),
            [CirceMoment.BillOwed] = ("bill_owed", 2),
            [CirceMoment.BillBig] = ("bill_big", 2),
        };

    /// <summary>The one shared picker, so the rail, the page and the bill share one throttle.</summary>
    public static CirceLines Shared { get; } = new(new Random());

    private readonly Random _rng;
    private readonly object _gate = new();
    private readonly Dictionary<CirceMoment, int> _last = new();
    private DateTime _lastSpokeUtc = DateTime.MinValue;

    public CirceLines(Random rng) => _rng = rng;

    public static int Variants(CirceMoment moment) => Table[moment].Count;

    /// <summary>"chaster_line_held_2". Variants count from 1.</summary>
    public static string Key(CirceMoment moment, int variant) => $"chaster_line_{Table[moment].Stem}_{variant}";

    /// <summary>Every key in the table, for the loc tests.</summary>
    public static IEnumerable<string> AllKeys()
    {
        foreach (var (moment, (_, count)) in Table)
            for (var v = 1; v <= count; v++) yield return Key(moment, v);
    }

    public static bool AlwaysWins(CirceMoment moment) => moment is CirceMoment.Landed
        or CirceMoment.BillCredit or CirceMoment.BillEven or CirceMoment.BillOwed or CirceMoment.BillBig;

    /// <summary>The bill's verdict from its net seconds (negative is credit).</summary>
    public static CirceMoment Verdict(int netSeconds) =>
        Math.Abs(netSeconds) < EvenBandSeconds ? CirceMoment.BillEven
        : netSeconds < 0 ? CirceMoment.BillCredit
        : netSeconds >= BigOwedSeconds ? CirceMoment.BillBig
        : CirceMoment.BillOwed;

    /// <summary>The moment a booking is, or null when Circe has nothing to say about it.</summary>
    public static CirceMoment? ForBooking(string? eventId, int appliedSeconds) => eventId switch
    {
        HeldEventId when appliedSeconds < 0 => CirceMoment.Held,
        PoppedEventId when appliedSeconds > 0 => CirceMoment.Popped,
        _ => null,
    };

    /// <summary>The moment a mood change is, or null when it did not move.</summary>
    public static CirceMoment? ForMood(CircesMood before, CircesMood after) =>
        after.Step > before.Step ? CirceMoment.Warmer : after.Step < before.Step ? CirceMoment.Cooler : null;

    /// <summary>A line for <paramref name="moment"/> as a loc key, or null when she spoke less
    /// than <see cref="ThrottleSeconds"/> ago and this moment does not always win.</summary>
    public string? Pick(CirceMoment moment, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (!AlwaysWins(moment) && (nowUtc - _lastSpokeUtc).TotalSeconds < ThrottleSeconds) return null;
            var count = Variants(moment);
            var last = _last.TryGetValue(moment, out var l) ? l : 0;
            // One of the others, evenly: draw from count - 1 and step over the last one.
            var variant = last == 0 || count < 2 ? _rng.Next(1, count + 1) : _rng.Next(1, count);
            if (last != 0 && count >= 2 && variant >= last) variant++;
            _last[moment] = variant;
            _lastSpokeUtc = nowUtc;
            return Key(moment, variant);
        }
    }
}
