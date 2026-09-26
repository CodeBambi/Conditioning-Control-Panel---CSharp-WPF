using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Chaster;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Circe's tab, rule by rule: events net inside CCP, the floor, the player's two limits, the way
/// out never costs, the jackpot wipes the tab only, a positive balance pushes live, and the bill.
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
        Assert.Equal(TabPushKind.None, CircesTab.PlanPush(s, canRemove: true, TabLimits.Default, Noon).Kind);
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
    public void A_day_adds_three_hours_by_default_and_not_a_second_more()
    {
        var s = new TabState();
        for (var i = 0; i < 35; i++) Book(s, "watcher", 300);
        var last = Book(s, "watcher", 300);
        var over = Book(s, "typo", 15);

        Assert.Equal(300, last.AppliedSeconds);
        Assert.Equal(3 * 3600, s.BalanceSeconds);
        Assert.Equal(new TabBooking(0, TabRefusal.DailyCap), over);
    }

    [Fact]
    public void The_player_sets_the_day_and_the_backlog()
    {
        var s = new TabState();
        var caps = TabLimits.FromMinutes(30, 60);

        var first = CircesTab.Book(s, "watcher", 1500, Run, Noon, Run, false, caps);
        var clamped = CircesTab.Book(s, "watcher", 600, Run, Noon, Run, false, caps);
        CircesTab.Book(s, "watcher", 1500, Run, Noon.AddDays(1), Run, false, caps);
        var full = CircesTab.Book(s, "watcher", 900, Run, Noon.AddDays(2), Run, false, caps);

        Assert.Equal(1500, first.AppliedSeconds);
        Assert.Equal(new TabBooking(300, TabRefusal.DailyCap), clamped);
        Assert.Equal(new TabBooking(300, TabRefusal.Backlog), full);
        Assert.Equal(3600, s.BalanceSeconds);
    }

    [Theory]
    [InlineData(0, 0, 15 * 60, 60 * 60)]
    [InlineData(100000, 100000, 12 * 3600, 48 * 3600)]
    [InlineData(600, 60, 600 * 60, 600 * 60)]
    [InlineData(180, 720, 3 * 3600, 12 * 3600)]
    public void Limits_stay_inside_their_range_and_the_backlog_never_under_the_day(int day, int backlog, int wantDay, int wantBacklog)
    {
        Assert.Equal(new TabLimits(wantDay, wantBacklog), TabLimits.FromMinutes(day, backlog));
    }

    [Fact]
    public void The_cap_clamps_the_last_add_and_earning_back_hands_no_room_back()
    {
        var s = new TabState { Day = CircesTab.DayKey(Noon), DayAddedSeconds = CircesTab.DailyCapSeconds - 10 };

        var clamped = Book(s, "attention", 120);
        Book(s, "session", -600);
        var after = Book(s, "typo", 15);

        Assert.Equal(new TabBooking(10, TabRefusal.DailyCap), clamped);
        Assert.Equal(TabRefusal.DailyCap, after.Refusal);
    }

    [Fact]
    public void The_tab_never_holds_more_than_twelve_unpaid_hours_by_default()
    {
        var s = new TabState();
        for (var day = 0; day < 5; day++) Book(s, "watcher", 3 * 3600, Noon.AddDays(day));

        Assert.Equal(CircesTab.BacklogCapSeconds, s.BalanceSeconds);
        Assert.Equal(TabRefusal.Backlog, Book(s, "typo", 15, Noon.AddDays(6)).Refusal);

        Book(s, "session", -600, Noon.AddDays(6));
        var partial = Book(s, "watcher", 900, Noon.AddDays(6));
        Assert.Equal(600, partial.AppliedSeconds);
        Assert.Equal(TabRefusal.Backlog, partial.Refusal);
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
    public void A_positive_balance_pushes_live_and_a_credit_cancels_the_next_slip_ups()
    {
        var s = new TabState { BalanceSeconds = 750 };

        var push = CircesTab.PlanPush(s, canRemove: false, TabLimits.Default, Noon);
        CircesTab.ApplyPush(s, push, Noon);
        Assert.Equal(TabPushKind.None, CircesTab.PlanPush(s, canRemove: false, TabLimits.Default, Noon).Kind);
        Book(s, "typo", 15);
        var again = CircesTab.PlanPush(s, canRemove: false, TabLimits.Default, Noon);

        Assert.Equal(new TabPush(TabPushKind.Add, 750), push);
        Assert.Equal(750, s.PushedNetSeconds);
        Assert.Equal(new TabPush(TabPushKind.Add, 15), again);

        CircesTab.ApplyPush(s, again, Noon);
        Book(s, "session", -600);
        Book(s, "attention", 120);
        Assert.Equal(-480, s.BalanceSeconds);
        Assert.Equal(TabPushKind.None, CircesTab.PlanPush(s, canRemove: false, TabLimits.Default, Noon).Kind);
    }

    [Fact]
    public void A_push_chaster_refused_changes_nothing()
    {
        var s = new TabState { BalanceSeconds = 750 };

        CircesTab.PlanPush(s, canRemove: false, TabLimits.Default, Noon);

        Assert.Equal(750, s.BalanceSeconds);
        Assert.Null(s.LastPushDay);
        Assert.Equal(TabPushKind.Add, CircesTab.PlanPush(s, canRemove: false, TabLimits.Default, Noon).Kind);
    }

    [Fact]
    public void An_unanswered_push_counts_as_landed_and_does_not_widen_the_floor()
    {
        var s = new TabState();
        Book(s, "watcher", 900);
        CircesTab.MarkPending(s, new TabPush(TabPushKind.Add, 900), Noon);

        Assert.Equal(900, CircesTab.ResolvePending(s));

        Assert.Equal(0, s.BalanceSeconds);
        Assert.Equal(0, s.PushedNetSeconds);
        Assert.Equal(TabPushKind.None, CircesTab.PlanPush(s, canRemove: false, TabLimits.Default, Noon).Kind);
        Assert.Equal(0, CircesTab.ResolvePending(s));
    }

    [Fact]
    public void Resolving_a_doubt_never_hands_out_credit()
    {
        var s = new TabState();
        Book(s, "watcher", 900);
        CircesTab.MarkPending(s, new TabPush(TabPushKind.Add, 900), Noon);
        CircesTab.Wipe(s, Run.AddHours(1), Run);
        Book(s, "typo", 15);

        CircesTab.ResolvePending(s);

        Assert.Equal(0, s.BalanceSeconds);
    }

    [Fact]
    public void A_wearer_link_keeps_a_credit_on_the_tab()
    {
        var s = new TabState { BalanceSeconds = -300, PushedNetSeconds = 900 };

        Assert.Equal(TabPushKind.None, CircesTab.PlanPush(s, canRemove: false, TabLimits.Default, Noon).Kind);
    }

    [Fact]
    public void A_link_that_can_remove_takes_back_no_more_than_ccp_added()
    {
        var s = new TabState { BalanceSeconds = -300, PushedNetSeconds = 200 };

        var push = CircesTab.PlanPush(s, canRemove: true, TabLimits.Default, Noon);
        CircesTab.ApplyPush(s, push, Noon);

        Assert.Equal(new TabPush(TabPushKind.Remove, 200), push);
        Assert.Equal(0, s.PushedNetSeconds);
        Assert.Equal(-100, s.BalanceSeconds);
    }

    [Fact]
    public void No_day_sends_the_lock_more_than_the_daily_limit_whatever_the_balance()
    {
        var limits = TabLimits.FromMinutes(60, 2880);
        var s = new TabState { BalanceSeconds = 10 * 3600 };

        var first = CircesTab.PlanPush(s, false, limits, Noon);
        CircesTab.ApplyPush(s, first, Noon);
        var sameDay = CircesTab.PlanPush(s, false, limits, Noon.AddHours(3));
        var tomorrow = CircesTab.PlanPush(s, false, limits, Noon.AddDays(1));

        Assert.Equal(new TabPush(TabPushKind.Add, 3600), first);
        Assert.Equal(TabPushKind.None, sameDay.Kind);
        Assert.Equal(new TabPush(TabPushKind.Add, 3600), tomorrow);
        Assert.Equal(9 * 3600, s.BalanceSeconds);
    }

    [Fact]
    public void A_push_in_doubt_counts_against_the_day_it_went_out()
    {
        var limits = TabLimits.FromMinutes(60, 180);
        var s = new TabState { BalanceSeconds = 7200 };
        CircesTab.MarkPending(s, new TabPush(TabPushKind.Add, 3600), Noon);
        CircesTab.ResolvePending(s);

        Assert.Equal(TabPushKind.None, CircesTab.PlanPush(s, false, limits, Noon.AddHours(1)).Kind);
        Assert.Equal(3600, CircesTab.PlanPush(s, false, limits, Noon.AddDays(1)).Seconds);
    }

    [Fact]
    public void A_tampered_tab_is_clamped_the_players_way()
    {
        var limits = TabLimits.FromMinutes(180, 720);
        var s = new TabState
        {
            BalanceSeconds = int.MaxValue, PendingSeconds = int.MaxValue, PushedNetSeconds = -50,
            DayAddedSeconds = -99999, PushDaySeconds = -99999, RemoteDaySeconds = -99999, ForgivableSeconds = -5,
        };

        Assert.True(CircesTab.Sanitise(s, limits));

        Assert.Equal(720 * 60, s.BalanceSeconds);
        Assert.Equal(TabLimits.MaxDailySeconds, s.PendingSeconds);
        Assert.Equal(0, s.PushedNetSeconds);
        Assert.Equal(0, s.DayAddedSeconds);
        Assert.Equal(0, s.PushDaySeconds);
        Assert.Equal(0, s.RemoteDaySeconds);
        Assert.Equal(0, s.ForgivableSeconds);
        Assert.False(CircesTab.Sanitise(s, limits));

        var credit = new TabState { BalanceSeconds = -99999, PushedNetSeconds = 600 };
        CircesTab.Sanitise(credit, limits);
        Assert.Equal(-600, credit.BalanceSeconds);
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
        CircesTab.ApplyPush(s, CircesTab.PlanPush(s, false, TabLimits.Default, Noon), Noon);

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
        Assert.Equal(30, TabPrices.Resolve("typo", new HashSet<string> { "typo" }));
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

        Assert.Equal(210, TabPrices.Resolve("mantra", on, units: 7));
        Assert.Equal(30000, TabPrices.Resolve("mantra", on, units: 100000));
        Assert.Equal(-600, TabPrices.Resolve("session", on, units: 7));
    }

    [Fact]
    public void Price_ids_are_unique_and_never_zero()
    {
        Assert.Equal(TabPrices.All.Count, TabPrices.All.Select(p => p.Id).Distinct().Count());
        Assert.All(TabPrices.All, p => Assert.NotEqual(0, p.Seconds));
    }
}
