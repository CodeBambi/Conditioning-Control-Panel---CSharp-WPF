using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Stakes;

/// <summary>What a player puts on a PvP match. None is always allowed.</summary>
public enum StakeKind { None, Time, Sp }

/// <summary>One side's stake: a kind and an amount (seconds for Time, Sparkle Points for Sp, 0 for None).</summary>
public readonly record struct Stake(StakeKind Kind, int Amount)
{
    public static readonly Stake None = new(StakeKind.None, 0);
    public bool IsNone => Kind == StakeKind.None;
}

/// <summary>
/// PVP STAKES, the client's copy of the constants table (brief, owner 2026-09-28). The server has
/// its own table and is the authority: the options the page offers come from
/// <c>/v2/stakes/limits</c> when it answers, and these are only the fallback and the validator.
///
/// <para>Each player stakes against the HOUSE, never the opponent. SP never passes between
/// players. A time stake that is lost is booked on the loser's OWN Chaster tab by the loser's own
/// client (row <see cref="LossRowId"/>), so the player's own limits, safety hold and Remote cap
/// still decide. Leaving mid-match never costs anything: the server voids it, and the client
/// books nothing it did not get a settled <c>lost</c> for.</para>
///
/// <para>Stakes are online PvP only (chess and the Goon Game). Never solo, never vs the bot.
/// Pure.</para>
/// </summary>
public static class StakeRules
{
    /// <summary>The Circe's Tab row a lost time stake books on. Opt-in like every row: picking a
    /// time stake is the consent and switches it on.</summary>
    public const string LossRowId = "pvp_loss";

    /// <summary>Time options, seconds. 15 and 30 minutes.</summary>
    public static readonly IReadOnlyList<int> TimeOptions = new[] { 900, 1800 };

    /// <summary>Sparkle Point options.</summary>
    public static readonly IReadOnlyList<int> SpOptions = new[] { 5, 10, 25 };

    /// <summary>No time stake is ever more than this.</summary>
    public const int MaxTimeSeconds = 1800;

    /// <summary>A won time stake pays one SP per this many staked seconds (15 min = 5, 30 min = 10).</summary>
    public const int SecondsPerPrizeSp = 180;

    /// <summary>Staked matches that can settle per player per UTC day.</summary>
    public const int DailyStakedMatches = 3;

    /// <summary>House prize SP a player can win per UTC day; past it a won SP stake only returns its ante.</summary>
    public const int DailyPrizeSp = 30;

    /// <summary>How long the host keeps asking whether a finished match has settled, and how
    /// often. Past the last step it gives up: treated as void locally, nothing booked.</summary>
    public static readonly IReadOnlyList<TimeSpan> SettlePollDelays = new[]
    {
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(13), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(40),
    };

    public static string KindWire(StakeKind kind) => kind switch
    {
        StakeKind.Time => "time",
        StakeKind.Sp => "sp",
        _ => "none",
    };

    public static StakeKind ParseKind(string? wire) => (wire ?? "").Trim().ToLowerInvariant() switch
    {
        "time" => StakeKind.Time,
        "sp" => StakeKind.Sp,
        _ => StakeKind.None,
    };

    /// <summary>Is this a stake the table offers. None (any amount, read as 0) always is.</summary>
    public static bool IsValid(StakeKind kind, int amount) => kind switch
    {
        StakeKind.None => true,
        StakeKind.Time => Contains(TimeOptions, amount) && amount <= MaxTimeSeconds,
        StakeKind.Sp => Contains(SpOptions, amount),
        _ => false,
    };

    /// <summary>The stake to send, or null when the page asked for something off the table.
    /// None always normalises to amount 0.</summary>
    public static Stake? Normalise(string? kindWire, int amount)
    {
        var kind = ParseKind(kindWire);
        if (kind == StakeKind.None) return Stake.None;
        return IsValid(kind, amount) ? new Stake(kind, amount) : null;
    }

    /// <summary>The house prize for winning a time stake: floor(T / 180) SP.</summary>
    public static int TimePrizeSp(int seconds) => seconds <= 0 ? 0 : Math.Min(seconds, MaxTimeSeconds) / SecondsPerPrizeSp;

    /// <summary>
    /// May the picker offer TIME right now. Chaster linked, the tab on and not paused (or a loss
    /// would book nothing while the card said it would), and no safety hold after panic.
    /// </summary>
    public static bool TimeAllowed(bool linked, bool tabEnabled, bool paused, TimeSpan safetyHoldLeft) =>
        linked && tabEnabled && !paused && safetyHoldLeft <= TimeSpan.Zero;

    /// <summary>Whole minutes for a label ("15 min").</summary>
    public static int Minutes(int seconds) => Math.Max(0, seconds) / 60;

    private static bool Contains(IReadOnlyList<int> list, int v)
    {
        foreach (var x in list) if (x == v) return true;
        return false;
    }
}
