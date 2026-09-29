using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>What the Circe's tab page prints, kept out of the view so it is tested without WPF:
/// which loc key names a row, how a price reads, and how the rows split into what costs and what
/// earns back.</summary>
public static class TabPageText
{
    public static string NameKey(string eventId) => "chaster_price_" + eventId;

    /// <summary>"+0:15", "-10:00", or "+0:10 each" for a per-unit row. <paramref name="eachFormat"/>
    /// is the localised "{0} each".</summary>
    public static string Price(TabPrice price, string eachFormat)
    {
        var figure = CircesTab.Format(price.Seconds);
        return price.PerUnit ? string.Format(eachFormat, figure) : figure;
    }

    /// <summary>Costs first (biggest first), then what earns time back (biggest first). Two short
    /// lists read at a glance; one list sorted by id does not.</summary>
    public static (IReadOnlyList<TabPrice> Costs, IReadOnlyList<TabPrice> EarnBacks) Split(IEnumerable<TabPrice> prices)
    {
        var all = prices.ToList();
        return (all.Where(p => p.Seconds > 0).OrderByDescending(p => p.Seconds).ToList(),
                all.Where(p => p.Seconds < 0).OrderBy(p => p.Seconds).ToList());
    }

    /// <summary>The next setup step stays visible until the tab can run.</summary>
    internal static string? SetupHint(bool linked, LockLookup lookup, bool hasLock, bool enabled) =>
        !linked ? null
        : lookup == LockLookup.Away ? "chaster_state_away"
        : lookup == LockLookup.None ? "chaster_setup_none"
        : !hasLock ? "chaster_setup_pick"
        : !enabled ? "chaster_setup_run"
        : null;

    // ============================== the hero: the lock, in words ==============================

    /// <summary>One piece of the hero countdown: a loc key that reads "{0} days" or "{0} hour",
    /// and the number to put in it.</summary>
    public readonly record struct CountdownPart(string Key, int Value);

    /// <summary>
    /// The lock's time left, long form, as one or two parts the page joins with a space: "12 days
    /// 4 hours", "4 hours 12 minutes", "12 minutes". An EMPTY list means less than a minute is
    /// left (the page says so in one word instead of counting seconds nobody can act on).
    ///
    /// <para>Deliberately not the rail chip's "12d 4h": the chip has a rail's width and the page
    /// has a hero. Singular and plural are separate keys, so no language is stuck printing
    /// "1 days".</para>
    /// </summary>
    public static IReadOnlyList<CountdownPart> Countdown(TimeSpan left)
    {
        if (left < TimeSpan.FromMinutes(1)) return Array.Empty<CountdownPart>();
        var days = (int)left.TotalDays;
        var hours = left.Hours;
        var minutes = left.Minutes;
        var parts = new List<CountdownPart>(2);
        if (days >= 1)
        {
            parts.Add(new CountdownPart(days == 1 ? "chaster_unit_day" : "chaster_unit_days", days));
            if (hours > 0) parts.Add(new CountdownPart(hours == 1 ? "chaster_unit_hour" : "chaster_unit_hours", hours));
        }
        else if (left.TotalHours >= 1)
        {
            parts.Add(new CountdownPart(hours == 1 ? "chaster_unit_hour" : "chaster_unit_hours", hours));
            if (minutes > 0) parts.Add(new CountdownPart(minutes == 1 ? "chaster_unit_minute" : "chaster_unit_minutes", minutes));
        }
        else
        {
            parts.Add(new CountdownPart(minutes == 1 ? "chaster_unit_minute" : "chaster_unit_minutes", minutes));
        }
        return parts;
    }

    /// <summary>The line under the countdown: why the number is what it is, or null when the lock
    /// speaks for itself. One line, never two: the hero is the lock, not a status board.</summary>
    public static string? HeroState(LockLookup lookup, LockSnapshot? snapshot) => lookup switch
    {
        LockLookup.Unlinked => null,
        LockLookup.Away => "chaster_state_away",
        LockLookup.None => "chaster_state_no_lock",
        LockLookup.Ambiguous => "chaster_state_pick",
        _ => snapshot == null ? "chaster_state_no_lock"
            : snapshot.IsFrozen ? "chaster_state_frozen"
            : snapshot.TimerHidden ? "chaster_state_hidden"
            : snapshot.EndsAtUtc == null ? "chaster_state_no_end"
            : snapshot.IsTestLock ? "chaster_state_test"
            : null,
    };

    // ============================== the tag ==============================

    /// <summary>The line under the tag's amount, as a loc key and its figures. A positive balance
    /// splits into what goes to the lock today (the day's push ceiling allows it) and what waits
    /// for tomorrow; paused or with no lock picked, all of it waits.</summary>
    public readonly record struct TagLine(string Key, string? Today = null, string? Later = null);

    public static TagLine Tag(int balanceSeconds, int pushableTodaySeconds, bool paused, bool lockPicked)
    {
        if (balanceSeconds < 0) return new("chaster_tag_credit_lands");
        if (balanceSeconds == 0) return new("chaster_tag_lands");
        if (paused) return new("chaster_tag_paused");
        if (!lockPicked) return new("chaster_tag_nolock");
        var today = Math.Clamp(pushableTodaySeconds, 0, balanceSeconds);
        var later = balanceSeconds - today;
        if (later == 0) return new("chaster_tag_lands");
        if (today == 0) return new("chaster_tag_tomorrow");
        return new("chaster_tag_split", CircesTab.Format(today, signed: false), CircesTab.Format(later, signed: false));
    }

    // ============================== the day ==============================

    /// <summary>How full today's 60:00 is, 0 to 1. Clamped both ways: the cap clamps bookings, but
    /// a settings file or an old state can still hold a bigger number and the meter must not run
    /// off the end of its track.</summary>
    public static double CapFraction(int todayAddedSeconds, int capSeconds = CircesTab.DailyCapSeconds)
    {
        if (capSeconds <= 0) return 0;
        return Math.Clamp(todayAddedSeconds / (double)capSeconds, 0, 1);
    }
}
