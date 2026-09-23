using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>One line on the tab. Repeats of the same event inside one run of the app fold into
/// one line (124 remote pictures is one row with a count, not 124 rows).</summary>
public sealed class TabEntry
{
    [JsonProperty("at")] public DateTime AtUtc { get; set; }
    [JsonProperty("id")] public string EventId { get; set; } = "";
    [JsonProperty("s")] public int Seconds { get; set; }
    [JsonProperty("n")] public int Count { get; set; } = 1;
}

/// <summary>Everything the tab remembers between runs. Plain data so it round-trips through
/// Newtonsoft; days are "yyyy-MM-dd" strings in the player's local calendar.</summary>
public sealed class TabState
{
    /// <summary>What is on the tab and not on the lock yet. Negative is credit.</summary>
    [JsonProperty("balance")] public int BalanceSeconds { get; set; }

    /// <summary>Lifetime seconds CCP has put on the lock, minus what it took back. The floor
    /// hangs off this number: CCP only ever removes time CCP added.</summary>
    [JsonProperty("pushed_net")] public int PushedNetSeconds { get; set; }

    [JsonProperty("last_push_day")] public string? LastPushDay { get; set; }
    [JsonProperty("day")] public string? Day { get; set; }

    /// <summary>Gross adds booked on <see cref="Day"/>. Credits never hand room back.</summary>
    [JsonProperty("day_added")] public int DayAddedSeconds { get; set; }

    [JsonProperty("entries")] public List<TabEntry> Entries { get; set; } = new();

    /// <summary>Seconds of a push that went out and was never answered (a timeout, or the app
    /// died mid-call). 0 when nothing is in doubt. See <see cref="CircesTab.ResolvePending"/>.</summary>
    [JsonProperty("pending")] public int PendingSeconds { get; set; }
    [JsonProperty("pending_day")] public string? PendingDay { get; set; }

    /// <summary>The last local day CCP ran with an account linked. "Circe misses you" counts
    /// from here. Null until the first linked run, so linking never charges for the past.</summary>
    [JsonProperty("last_seen_day")] public string? LastSeenDay { get; set; }

    /// <summary>Half of the last "misses you" charge, waiting for the first finished session.</summary>
    [JsonProperty("forgivable")] public int ForgivableSeconds { get; set; }
}

public enum TabRefusal
{
    None,
    /// <summary>Panic or Emergency Exit is in play. Those never cost time, and neither does
    /// anything that happens while one is up.</summary>
    SafetyExit,
    /// <summary>The day's 60:00 is spent.</summary>
    DailyCap,
    /// <summary>The tab already holds all the unpaid time it is allowed to hold.</summary>
    Backlog,
    /// <summary>The credit would take back more than CCP ever put on the lock.</summary>
    Floor,
    Nothing,
}

/// <summary>What a booking did. <see cref="AppliedSeconds"/> can be smaller than what was asked:
/// the cap and the floor clamp, they do not refuse the part that still fits.</summary>
public readonly record struct TabBooking(int AppliedSeconds, TabRefusal Refusal)
{
    public bool Booked => AppliedSeconds != 0;
}

public enum TabPushKind { None, Add, Remove }

public readonly record struct TabPush(TabPushKind Kind, int Seconds);

/// <summary>
/// Circe's tab: the rules between "something happened in CCP" and "time moved on a Chaster lock".
///
/// <para>Events net INSIDE CCP. A typo adds fifteen seconds, a finished session takes ten minutes
/// off, and the lock hears about neither. Once a day the balance is settled: a positive balance is
/// added to the lock, a credit waits on the tab (or comes off the lock, when the link is one that
/// can remove time, and never more than CCP put there).</para>
///
/// <para>Pure on purpose. No clock, no settings, no network: the service hands in the time, the
/// price and whether a safety exit is up, and saves the state that comes back. Every rule the
/// owner set (floor, 60:00 a day, panic never adds, the jackpot wipes the tab only, one push a
/// day) is a row in a test, not a desk run with a real lock.</para>
/// </summary>
public static class CircesTab
{
    /// <summary>The default most a single local day can add (owner, 2026-09-23: three hours).
    /// The player can move it inside <see cref="TabLimits"/>'s range.</summary>
    public const int DailyCapSeconds = 3 * 60 * 60;

    /// <summary>The default most unpaid time the tab ever holds (owner, 2026-09-23: twelve
    /// hours). A month with no lock, or with Chaster unreachable, is not a month of debt waiting
    /// for the next lock: past this, prices stop adding until some is pushed or earned back.</summary>
    public const int BacklogCapSeconds = 12 * 60 * 60;

    /// <summary>The ledger keeps this many lines. The bill only ever reads the current run.</summary>
    public const int MaxEntries = 500;

    public const string JackpotEventId = "jackpot";

    public static string DayKey(DateTime localNow) =>
        localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Book one event. <paramref name="seconds"/> is signed: positive costs, negative
    /// earns back. <paramref name="runStartUtc"/> is when this run of the app began; only lines
    /// from the same run fold together, so the bill at close stays this run's bill.</summary>
    public static TabBooking Book(TabState state, string eventId, int seconds,
        DateTime nowUtc, DateTime localNow, DateTime runStartUtc, bool safetyExit, TabLimits? limits = null)
    {
        var caps = limits ?? TabLimits.Default;
        if (seconds == 0 || string.IsNullOrEmpty(eventId)) return new(0, TabRefusal.Nothing);
        RollDay(state, localNow);

        int applied;
        if (seconds > 0)
        {
            if (safetyExit) return new(0, TabRefusal.SafetyExit);
            var room = Math.Max(0, caps.DailySeconds - state.DayAddedSeconds);
            var backlogRoom = Math.Max(0, caps.BacklogSeconds - state.BalanceSeconds);
            applied = Math.Min(seconds, Math.Min(room, backlogRoom));
            if (applied == 0) return new(0, room == 0 ? TabRefusal.DailyCap : TabRefusal.Backlog);
            state.DayAddedSeconds += applied;
        }
        else
        {
            // The floor: lock time CCP did not add is never CCP's to take. A credit can clear the
            // tab and then reach back only as far as what earlier pushes put on the lock.
            var floor = -Math.Max(0, state.PushedNetSeconds);
            applied = Math.Max(seconds, Math.Min(0, floor - state.BalanceSeconds));
            if (applied == 0) return new(0, TabRefusal.Floor);
        }

        state.BalanceSeconds += applied;
        Record(state, eventId, applied, nowUtc, runStartUtc);
        var clamped = applied != seconds;
        if (!clamped) return new(applied, TabRefusal.None);
        if (seconds < 0) return new(applied, TabRefusal.Floor);
        return new(applied, state.DayAddedSeconds >= caps.DailySeconds ? TabRefusal.DailyCap : TabRefusal.Backlog);
    }

    /// <summary>The jackpot wipes the TAB. It never touches the lock: what was already pushed
    /// stays pushed, and a credit is left alone.</summary>
    public static TabBooking Wipe(TabState state, DateTime nowUtc, DateTime runStartUtc)
    {
        if (state.BalanceSeconds <= 0) return new(0, TabRefusal.Nothing);
        var applied = -state.BalanceSeconds;
        state.BalanceSeconds = 0;
        Record(state, JackpotEventId, applied, nowUtc, runStartUtc);
        return new(applied, TabRefusal.None);
    }

    /// <summary>What to send the lock right now, if anything. Live since 2026-09-23 (owner): a
    /// positive balance goes out on the next push, and a credit stays on the tab, where it cancels
    /// the next slip-ups before they reach the lock. <paramref name="canRemove"/> is false for a
    /// wearer link, which Chaster only lets add.</summary>
    public static TabPush PlanPush(TabState state, bool canRemove)
    {
        if (state.BalanceSeconds > 0) return new(TabPushKind.Add, state.BalanceSeconds);
        if (state.BalanceSeconds < 0 && canRemove)
        {
            var take = Math.Min(-state.BalanceSeconds, Math.Max(0, state.PushedNetSeconds));
            if (take > 0) return new(TabPushKind.Remove, take);
        }
        return new(TabPushKind.None, 0);
    }

    /// <summary>Call only after Chaster said yes. A failed push changes nothing, so the balance
    /// simply waits for the next chance.</summary>
    public static void ApplyPush(TabState state, TabPush push, DateTime localNow)
    {
        if (push.Kind == TabPushKind.None || push.Seconds <= 0) return;
        if (push.Kind == TabPushKind.Add)
        {
            state.BalanceSeconds -= push.Seconds;
            state.PushedNetSeconds += push.Seconds;
        }
        else
        {
            state.BalanceSeconds += push.Seconds;
            state.PushedNetSeconds = Math.Max(0, state.PushedNetSeconds - push.Seconds);
        }
        state.LastPushDay = DayKey(localNow);
    }

    /// <summary>Write this down BEFORE an add goes out, and save. If the answer never comes the
    /// mark is still there on the next settle, and <see cref="ResolvePending"/> deals with it.</summary>
    public static void MarkPending(TabState state, TabPush push, DateTime localNow)
    {
        if (push.Kind != TabPushKind.Add || push.Seconds <= 0) return;
        state.PendingSeconds = push.Seconds;
        state.PendingDay = DayKey(localNow);
    }

    /// <summary>Chaster answered, yes or no. Nothing is in doubt any more.</summary>
    public static void ClearPending(TabState state)
    {
        state.PendingSeconds = 0;
        state.PendingDay = null;
    }

    /// <summary>An add that was never answered counts as LANDED. The other reading sends it
    /// again, and a lock that gains an hour nobody owed is the one mistake the tab must never
    /// make. The doubt goes the player's way twice: the seconds leave the tab, and they do not
    /// widen the floor, since CCP cannot prove it put them on the lock. Returns what it settled.</summary>
    public static int ResolvePending(TabState state)
    {
        var seconds = state.PendingSeconds;
        if (seconds <= 0) { ClearPending(state); return 0; }
        state.BalanceSeconds -= Math.Min(seconds, Math.Max(0, state.BalanceSeconds));
        state.LastPushDay = state.PendingDay ?? state.LastPushDay;
        ClearPending(state);
        return seconds;
    }

    /// <summary>"+0:30", "-10:00", "+1:05:00". The number that flashes when a price lands, and the
    /// figures on the bill. Plain hyphen, never a typographic minus.</summary>
    public static string Format(int seconds, bool signed = true)
    {
        var abs = Math.Abs((long)seconds);
        var body = abs >= 6000
            ? $"{abs / 3600}:{abs / 60 % 60:00}:{abs % 60:00}"
            : $"{abs / 60}:{abs % 60:00}";
        if (!signed) return body;
        return (seconds < 0 ? "-" : "+") + body;
    }

    private static void RollDay(TabState state, DateTime localNow)
    {
        var today = DayKey(localNow);
        if (state.Day == today) return;
        state.Day = today;
        state.DayAddedSeconds = 0;
    }

    private static void Record(TabState state, string eventId, int applied, DateTime nowUtc, DateTime runStartUtc)
    {
        var last = state.Entries.Count > 0 ? state.Entries[^1] : null;
        if (last != null && last.EventId == eventId && last.AtUtc >= runStartUtc
            && Math.Sign(last.Seconds) == Math.Sign(applied))
        {
            last.Seconds += applied;
            last.Count++;
            return;
        }
        state.Entries.Add(new TabEntry { AtUtc = nowUtc, EventId = eventId, Seconds = applied, Count = 1 });
        if (state.Entries.Count > MaxEntries)
            state.Entries.RemoveRange(0, state.Entries.Count - MaxEntries);
    }
}

/// <summary>The player's two limits, clamped to a range nobody can argue with: a day of
/// 15:00 to 12 hours, a backlog of 1 to 48 hours and never under the day's own limit.</summary>
public readonly record struct TabLimits(int DailySeconds, int BacklogSeconds)
{
    public const int MinDailySeconds = 15 * 60;
    public const int MaxDailySeconds = 12 * 60 * 60;
    public const int MinBacklogSeconds = 60 * 60;
    public const int MaxBacklogSeconds = 48 * 60 * 60;

    public static TabLimits Default => new(CircesTab.DailyCapSeconds, CircesTab.BacklogCapSeconds);

    public static TabLimits FromMinutes(int dailyMinutes, int backlogMinutes)
    {
        var daily = Math.Clamp((long)dailyMinutes * 60, MinDailySeconds, MaxDailySeconds);
        var backlog = Math.Clamp((long)backlogMinutes * 60, Math.Max(MinBacklogSeconds, daily), MaxBacklogSeconds);
        return new((int)daily, (int)backlog);
    }
}
