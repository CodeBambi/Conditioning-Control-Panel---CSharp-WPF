using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Chaster;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Circe's tab, rule by rule: events net inside CCP, the floor, 60:00 a day, the way out never
/// costs, the jackpot wipes the tab only, one push a day, and the bill.
/// </summary>
public class CircesTabTests
{
    private static readonly DateTime Run = new(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Noon = new(2026, 9, 21, 12, 0, 0);

    private static TabBooking Book(TabState s, string id, int seconds, DateTime? local = null, bool safety = false) =>
        CircesTab.Book(s, id, seconds, Run.AddMinutes(s.Entries.Count + 1), local ?? Noon, Run, safety);

    [Fact]
    public void Events_net_on_the_tab_and_the_lock_hears_nothing()
    {
        var s = new TabState();
        Book(s, "typo", 15);
        Book(s, "typo", 15);
        Book(s, "lockcard", -30);

        Assert.Equal(0, s.BalanceSeconds);
        Assert.Equal(0, s.PushedNetSeconds);
        Assert.Equal(TabPushKind.None, CircesTab.PlanPush(s, Noon, canRemove: true).Kind);
    }

    [Fact]
    public void A_credit_stops_at_the_floor_when_ccp_has_pushed_nothing()
    {
        var s = new TabState();
        Book(s, "attention", 120);

        var booking = Book(s, "session", -600);

        Assert.Equal(-120, booking.AppliedSeconds);
        Assert.Equal(TabRefusal.Floor, booking.Refusal);
        Assert.Equal(0, s.BalanceSeconds);
        Assert.Equal(TabRefusal.Floor, Book(s, "session", -600).Refusal);
        Assert.Equal(0, s.BalanceSeconds);
    }

    [Fact]
    public void A_credit_reaches_back_only_as_far_as_what_ccp_pushed()
    {
        var s = new TabState { PushedNetSeconds = 400 };

        Book(s, "session", -600);

        Assert.Equal(-400, s.BalanceSeconds);
    }

    [Fact]
    public void A_day_adds_sixty_minutes_and_not_a_second_more()
    {
        var s = new TabState();
        for (var i = 0; i < 11; i++) Book(s, "watcher", 300);
        var last = Book(s, "watcher", 300);
        var over = Book(s, "typo", 15);

        Assert.Equal(300, last.AppliedSeconds);
        Assert.Equal(CircesTab.DailyCapSeconds, s.BalanceSeconds);
        Assert.Equal(new TabBooking(0, TabRefusal.DailyCap), over);
    }

    [Fact]
    public void The_cap_clamps_the_last_add_and_earning_back_hands_no_room_back()
    {
        var s = new TabState { Day = CircesTab.DayKey(Noon), DayAddedSeconds = 3590 };

        var clamped = Book(s, "attention", 120);
        Book(s, "session", -600);
        var after = Book(s, "typo", 15);

        Assert.Equal(new TabBooking(10, TabRefusal.DailyCap), clamped);
        Assert.Equal(TabRefusal.DailyCap, after.Refusal);
    }

    [Fact]
    public void A_new_local_day_opens_a_new_sixty_minutes()
    {
        var s = new TabState { Day = CircesTab.DayKey(Noon), DayAddedSeconds = 3600 };

        var booking = Book(s, "typo", 15, Noon.AddDays(1));

        Assert.Equal(15, booking.AppliedSeconds);
        Assert.Equal(15, s.DayAddedSeconds);
    }

    [Fact]
    public void Nothing_adds_while_a_safety_exit_is_up_but_earning_back_still_works()
    {
        var s = new TabState { BalanceSeconds = 600 };

        var add = Book(s, "escape", 180, safety: true);
        var credit = Book(s, "session", -300, safety: true);

        Assert.Equal(new TabBooking(0, TabRefusal.SafetyExit), add);
        Assert.Equal(-300, credit.AppliedSeconds);
        Assert.Equal(300, s.BalanceSeconds);
    }

    [Fact]
    public void The_jackpot_wipes_the_tab_and_never_the_lock()
    {
        var s = new TabState { BalanceSeconds = 1500, PushedNetSeconds = 900 };

        var wipe = CircesTab.Wipe(s, Run.AddMinutes(1), Run);

        Assert.Equal(-1500, wipe.AppliedSeconds);
        Assert.Equal(0, s.BalanceSeconds);
        Assert.Equal(900, s.PushedNetSeconds);
        Assert.Equal(CircesTab.JackpotEventId, s.Entries.Single().EventId);
    }

    [Fact]
    public void The_jackpot_leaves_a_credit_alone()
    {
        var s = new TabState { BalanceSeconds = -200, PushedNetSeconds = 900 };

        Assert.False(CircesTab.Wipe(s, Run, Run).Booked);
        Assert.Equal(-200, s.BalanceSeconds);
    }

    [Fact]
    public void A_positive_balance_is_pushed_once_a_day()
    {
        var s = new TabState { BalanceSeconds = 750 };

        var push = CircesTab.PlanPush(s, Noon, canRemove: false);
        CircesTab.ApplyPush(s, push, Noon);
        Book(s, "typo", 15);

        Assert.Equal(new TabPush(TabPushKind.Add, 750), push);
        Assert.Equal(750, s.PushedNetSeconds);
        Assert.Equal(TabPushKind.None, CircesTab.PlanPush(s, Noon.AddHours(6), canRemove: false).Kind);
        Assert.Equal(new TabPush(TabPushKind.Add, 15), CircesTab.PlanPush(s, Noon.AddDays(1), canRemove: false));
    }

    [Fact]
    public void A_push_chaster_refused_changes_nothing()
    {
        var s = new TabState { BalanceSeconds = 750 };

        CircesTab.PlanPush(s, Noon, canRemove: false);

        Assert.Equal(750, s.BalanceSeconds);
        Assert.Null(s.LastPushDay);
        Assert.Equal(TabPushKind.Add, CircesTab.PlanPush(s, Noon, canRemove: false).Kind);
    }

    [Fact]
    public void A_wearer_link_keeps_a_credit_on_the_tab()
    {
        var s = new TabState { BalanceSeconds = -300, PushedNetSeconds = 900 };

        Assert.Equal(TabPushKind.None, CircesTab.PlanPush(s, Noon, canRemove: false).Kind);
    }

    [Fact]
    public void A_link_that_can_remove_takes_back_no_more_than_ccp_added()
    {
        var s = new TabState { BalanceSeconds = -300, PushedNetSeconds = 200 };

        var push = CircesTab.PlanPush(s, Noon, canRemove: true);
        CircesTab.ApplyPush(s, push, Noon);

        Assert.Equal(new TabPush(TabPushKind.Remove, 200), push);
        Assert.Equal(0, s.PushedNetSeconds);
        Assert.Equal(-100, s.BalanceSeconds);
    }

    [Fact]
    public void Repeats_in_one_run_fold_into_one_line()
    {
        var s = new TabState();
        for (var i = 0; i < 124; i++) Book(s, "remote_media", 30, Noon.AddDays(i / 100));

        var line = Assert.Single(s.Entries);
        Assert.Equal(124, line.Count);
    }

    [Fact]
    public void A_line_from_an_earlier_run_is_never_folded_into()
    {
        var s = new TabState();
        s.Entries.Add(new TabEntry { AtUtc = Run.AddDays(-1), EventId = "typo", Seconds = 15 });

        Book(s, "typo", 15);

        Assert.Equal(2, s.Entries.Count);
    }

    [Fact]
    public void The_ledger_keeps_its_newest_lines()
    {
        var s = new TabState { PushedNetSeconds = int.MaxValue / 2 };
        for (var i = 0; i < CircesTab.MaxEntries + 20; i++) Book(s, i % 2 == 0 ? "session" : "quest", -1);

        Assert.Equal(CircesTab.MaxEntries, s.Entries.Count);
    }

    [Fact]
    public void The_bill_is_this_run_folded_by_event_with_the_three_totals()
    {
        var s = new TabState();
        s.Entries.Add(new TabEntry { AtUtc = Run.AddDays(-1), EventId = "watcher", Seconds = 300 });
        for (var i = 0; i < 124; i++) Book(s, "remote_media", 30, Noon.AddDays(i / 100));
        Book(s, "typo", 15);
        Book(s, "remote_media", 30, Noon.AddDays(2));
        Book(s, "session", -600, Noon.AddDays(2));

        var bill = TabBill.Build(s.Entries, Run, pushedSeconds: 3165);

        Assert.Equal(new[] { "remote_media", "session", "typo" }, bill.Lines.Select(l => l.EventId));
        Assert.Equal(new TabBillLine("remote_media", 125, 3750), bill.Lines[0]);
        Assert.Equal(3765, bill.AddedSeconds);
        Assert.Equal(-600, bill.EarnedBackSeconds);
        Assert.Equal(3165, bill.NetSeconds);
        Assert.Equal(3165, bill.PushedSeconds);
    }

    [Fact]
    public void A_quiet_run_prints_no_bill()
    {
        Assert.True(TabBill.Build(new List<TabEntry>(), Run, 0).IsEmpty);
    }

    [Theory]
    [InlineData(30, "+0:30")]
    [InlineData(-600, "-10:00")]
    [InlineData(3600, "+60:00")]
    [InlineData(5999, "+99:59")]
    [InlineData(6000, "+1:40:00")]
    [InlineData(-86400, "-24:00:00")]
    [InlineData(0, "+0:00")]
    public void Time_reads_as_the_number_that_flashes(int seconds, string expected)
    {
        Assert.Equal(expected, CircesTab.Format(seconds));
        Assert.DoesNotContain('−', CircesTab.Format(seconds));
    }

    [Fact]
    public void The_state_round_trips_through_json()
    {
        var s = new TabState { PushedNetSeconds = 900 };
        Book(s, "typo", 15);
        CircesTab.ApplyPush(s, CircesTab.PlanPush(s, Noon, false), Noon);

        var back = JsonConvert.DeserializeObject<TabState>(JsonConvert.SerializeObject(s))!;

        Assert.Equal(s.PushedNetSeconds, back.PushedNetSeconds);
        Assert.Equal(s.LastPushDay, back.LastPushDay);
        Assert.Equal(s.Entries.Single().EventId, back.Entries.Single().EventId);
    }

    [Fact]
    public void Every_price_is_opt_in()
    {
        var none = new HashSet<string>();
        Assert.All(TabPrices.All, p => Assert.Equal(0, TabPrices.Resolve(p.Id, none)));
        Assert.All(TabPrices.All, p => Assert.Equal(0, TabPrices.Resolve(p.Id, null)));
        Assert.Equal(15, TabPrices.Resolve("typo", new HashSet<string> { "typo" }));
    }

    [Fact]
    public void The_way_out_can_never_be_priced()
    {
        var everything = new HashSet<string>(TabPrices.All.Select(p => p.Id).Concat(TabPrices.NeverPriced));

        Assert.All(TabPrices.NeverPriced, id => Assert.Equal(0, TabPrices.Resolve(id, everything)));
        Assert.DoesNotContain(TabPrices.All, p => TabPrices.NeverPriced.Contains(p.Id));
    }

    [Fact]
    public void A_broken_streak_pays_per_rep_and_no_single_event_passes_the_day()
    {
        var on = new HashSet<string> { "mantra", "session" };

        Assert.Equal(70, TabPrices.Resolve("mantra", on, units: 7));
        Assert.Equal(CircesTab.DailyCapSeconds, TabPrices.Resolve("mantra", on, units: 100000));
        Assert.Equal(-600, TabPrices.Resolve("session", on, units: 7));
    }

    [Fact]
    public void Price_ids_are_unique_and_never_zero()
    {
        Assert.Equal(TabPrices.All.Count, TabPrices.All.Select(p => p.Id).Distinct().Count());
        Assert.All(TabPrices.All, p => Assert.NotEqual(0, p.Seconds));
    }
}
