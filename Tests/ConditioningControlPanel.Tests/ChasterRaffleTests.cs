using System;
using System.Linq;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Locktober raffle card's rules (owner, 2026-09-26): in the draw at 25 counted days AND
/// 31:00:00; out once 25 days cannot be reached; the status line a card gets; the month strip.
/// </summary>
public class ChasterRaffleTests
{
    private const long Need = 31 * 3600;

    private static RaffleCard Card(int today, int[] days, long total, bool frozen = false, int? ticket = null) =>
        new("2026-10", 31, today, days, total, 25, Need, false, frozen, ticket);

    private static int[] Range(int from, int to) => Enumerable.Range(from, to - from + 1).ToArray();

    [Fact]
    public void Twenty_five_days_and_the_time_is_in_the_draw()
    {
        var card = Card(26, Range(1, 25), Need);
        Assert.True(ChasterRaffle.Eligible(card));
        Assert.Equal(RaffleStatus.InTheDraw, ChasterRaffle.Status(card));
        Assert.Equal(("chaster_raffle_in", (string?)null), ChasterRaffle.StatusText(card));
    }

    [Fact]
    public void Both_bars_are_needed()
    {
        Assert.False(ChasterRaffle.Eligible(Card(31, Range(1, 24), 99 * 3600)));
        Assert.False(ChasterRaffle.Eligible(Card(31, Range(1, 31), Need - 1)));
    }

    [Fact]
    public void Days_are_counted_once_and_only_inside_the_month()
    {
        var card = Card(10, new[] { 3, 3, 0, 32, 1, 2 }, 0);
        Assert.Equal(3, ChasterRaffle.DaysCounted(card));
        Assert.Equal(new[] { 1, 2, 3 }, ChasterRaffle.Counted(card));
    }

    [Fact]
    public void Days_short_are_counted_down()
    {
        var card = Card(12, Range(1, 12), 20 * 3600);
        Assert.Equal(RaffleStatus.NeedDays, ChasterRaffle.Status(card));
        Assert.Equal(13, ChasterRaffle.DaysShort(card));
        Assert.Equal(("chaster_raffle_need_days", "13"), ChasterRaffle.StatusText(card));
    }

    [Fact]
    public void One_day_short_reads_in_the_singular()
    {
        var card = Card(25, Range(1, 24), Need);
        Assert.Equal(("chaster_raffle_need_day", (string?)null), ChasterRaffle.StatusText(card));
    }

    [Fact]
    public void Days_met_time_short_says_how_much_to_add()
    {
        var card = Card(27, Range(1, 26), 30 * 3600);
        Assert.Equal(RaffleStatus.NeedTime, ChasterRaffle.Status(card));
        Assert.Equal(("chaster_raffle_need_time", "1:00:00"), ChasterRaffle.StatusText(card));
    }

    [Fact]
    public void Today_still_counts_until_it_is_over()
    {
        // Day 10, nine counted: today plus 21 days ahead are still open.
        var card = Card(10, Range(1, 9), 0);
        Assert.Equal(22, ChasterRaffle.DaysStillOpen(card));
        // Today already counted: only the days after it are open.
        Assert.Equal(21, ChasterRaffle.DaysStillOpen(Card(10, Range(1, 10), 0)));
    }

    [Fact]
    public void Out_once_twenty_five_days_cannot_be_reached()
    {
        // Day 8 with nothing counted: 24 days left, one short of the bar.
        var card = Card(8, Array.Empty<int>(), 0);
        Assert.True(ChasterRaffle.CannotReachDays(card));
        Assert.Equal(RaffleStatus.Out, ChasterRaffle.Status(card));
        Assert.Equal(("chaster_raffle_out", "25"), ChasterRaffle.StatusText(card));
        // Day 7 with nothing counted: exactly 25 left, still in reach.
        Assert.Equal(RaffleStatus.NeedDays, ChasterRaffle.Status(Card(7, Array.Empty<int>(), 0)));
    }

    [Fact]
    public void Before_the_month_it_is_not_started()
    {
        var card = Card(0, Array.Empty<int>(), 0);
        Assert.Equal(RaffleStatus.NotStarted, ChasterRaffle.Status(card));
        Assert.Equal(31, ChasterRaffle.DaysStillOpen(card));
        Assert.Equal(0, ChasterRaffle.DayShown(card));
    }

    [Fact]
    public void After_the_month_a_miss_is_a_miss_and_a_hit_waits_for_its_ticket()
    {
        Assert.Equal(RaffleStatus.Missed, ChasterRaffle.Status(Card(32, Range(1, 20), Need)));
        var inDraw = Card(32, Range(1, 25), Need);
        Assert.Equal(RaffleStatus.InTheDraw, ChasterRaffle.Status(inDraw));
        Assert.Equal(31, ChasterRaffle.DayShown(inDraw));
    }

    [Fact]
    public void Ticket_number_on_the_first_of_next_month_until_frozen()
    {
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), ChasterRaffle.TicketDue(Card(26, Range(1, 25), Need)));
        Assert.Null(ChasterRaffle.TicketDue(Card(12, Range(1, 12), 0)));
        Assert.Null(ChasterRaffle.TicketDue(Card(32, Range(1, 25), Need, frozen: true, ticket: 42)));
    }

    [Fact]
    public void A_frozen_list_shows_the_ticket_or_the_miss()
    {
        var mine = Card(32, Range(1, 25), Need, frozen: true, ticket: 42);
        Assert.Equal(RaffleStatus.Ticket, ChasterRaffle.Status(mine));
        Assert.Equal(("chaster_raffle_ticket", "42"), ChasterRaffle.StatusText(mine));
        // Frozen without me: whatever my numbers say now, I am not on the list.
        Assert.Equal(RaffleStatus.Missed, ChasterRaffle.Status(Card(32, Range(1, 31), Need, frozen: true)));
    }

    [Fact]
    public void The_strip_has_one_pip_a_day()
    {
        var pips = ChasterRaffle.Pips(Card(5, new[] { 1, 3 }, 0));
        Assert.Equal(31, pips.Count);
        Assert.Equal(RafflePip.Counted, pips[0]);
        Assert.Equal(RafflePip.Missed, pips[1]);
        Assert.Equal(RafflePip.Counted, pips[2]);
        Assert.Equal(RafflePip.Missed, pips[3]);
        Assert.Equal(RafflePip.Today, pips[4]);
        Assert.Equal(RafflePip.Ahead, pips[5]);
        // Today counted shows as counted.
        Assert.Equal(RafflePip.Counted, ChasterRaffle.Pips(Card(5, new[] { 5 }, 0))[4]);
    }

    [Fact]
    public void The_month_start_parses()
    {
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), ChasterRaffle.MonthStart("2026-10"));
        Assert.Null(ChasterRaffle.MonthStart("2026-13"));
        Assert.Null(ChasterRaffle.MonthStart(""));
    }

    [Theory]
    [InlineData("chaster_raffle_soon")]
    [InlineData("chaster_raffle_in")]
    [InlineData("chaster_raffle_need_day")]
    [InlineData("chaster_raffle_need_days")]
    [InlineData("chaster_raffle_need_time")]
    [InlineData("chaster_raffle_out")]
    [InlineData("chaster_raffle_missed")]
    [InlineData("chaster_raffle_ticket")]
    [InlineData("chaster_raffle_ticket_on")]
    public void Every_status_line_is_in_every_language(string key)
    {
        var dir = LocDir();
        foreach (var file in System.IO.Directory.GetFiles(dir, "*.json"))
        {
            var o = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(file));
            var v = o.Value<string>(key);
            Assert.False(string.IsNullOrWhiteSpace(v), $"{key} missing in {System.IO.Path.GetFileName(file)}");
            Assert.DoesNotContain("\u2014", v);
            Assert.DoesNotContain("!", v);
        }
    }

    private static string LocDir()
    {
        var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "ConditioningControlPanel", "Localization", "Languages")))
            d = d.Parent;
        Assert.NotNull(d);
        return System.IO.Path.Combine(d!.FullName, "ConditioningControlPanel", "Localization", "Languages");
    }
}
