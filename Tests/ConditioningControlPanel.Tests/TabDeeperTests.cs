using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The deeper tab (2026-09-26): heat makes the same slip cost more on the same day, an idle day
/// and open dailies are judged once their day is over, and every seventh session day in a row
/// earns time back.
/// </summary>
public class TabDeeperTests : IDisposable
{
    private sealed class MemoryStore : IChasterTokenStore
    {
        public ChasterStoredTokens? Tokens;
        public ChasterStoredTokens? Read() => Tokens;
        public void Write(ChasterStoredTokens tokens) => Tokens = tokens;
        public void Clear() => Tokens = null;
    }

    private sealed class NoContent : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(r.RequestUri!.AbsolutePath == "/chaster/refresh"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"AT\",\"expires_in\":300}") }
                : new HttpResponseMessage(HttpStatusCode.NoContent));
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-deeper-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryStore _store = new();
    private DateTime _local = new(2026, 9, 21, 10, 0, 0, DateTimeKind.Local);
    private ChasterOptions _options = new(true, "lock1", new HashSet<string> { "typo", "attention", "session" });
    private readonly Dictionary<string, int> _minutes = new();

    public TabDeeperTests()
    {
        Directory.CreateDirectory(_dir);
        _store.Tokens = new ChasterStoredTokens("AT", "RT", DateTime.UtcNow.AddYears(1));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } // swallow: temp dir, best effort
    }

    private ChasterService Make() =>
        new(new ChasterClient(new NoContent()), _store, Path.Combine(_dir, "tab.json"), () => _options,
            () => _local.ToUniversalTime(), () => _local)
        { MinutesOn = d => _minutes.TryGetValue(d, out var m) ? m : 0 };

    private void On(params string[] ids)
    {
        var set = new HashSet<string>(_options.Prices);
        foreach (var id in ids) set.Add(id);
        _options = _options with { Prices = set };
    }

    [Fact]
    public void Heat_grows_by_half_each_time_and_stops_at_three_times()
    {
        Assert.Equal(30, TabDayEnd.Heated(30, 0));
        Assert.Equal(45, TabDayEnd.Heated(30, 1));
        Assert.Equal(68, TabDayEnd.Heated(30, 2));
        Assert.Equal(90, TabDayEnd.Heated(30, 3));
        Assert.Equal(90, TabDayEnd.Heated(30, 40));
        Assert.Equal(-60, TabDayEnd.Heated(-60, 5));
    }

    [Fact]
    public void Heat_off_means_list_price_every_time()
    {
        using var service = Make();
        service.Note("attention");
        service.Note("attention");
        Assert.Equal(600, service.BalanceSeconds);
    }

    [Fact]
    public void Heat_on_prices_the_second_slip_higher_and_resets_at_midnight()
    {
        On(TabDayEnd.HeatId);
        using var service = Make();

        service.Note("attention");
        service.Note("attention");
        Assert.Equal(300 + 450, service.BalanceSeconds);

        _local = _local.AddDays(1);
        service.Note("attention");
        Assert.Equal(300 + 450 + 300, service.BalanceSeconds);
    }

    [Fact]
    public void Heat_never_touches_the_escape_row_or_credits()
    {
        Assert.False(TabDayEnd.HeatApplies("escape"));
        Assert.False(TabDayEnd.HeatApplies(CircesMisses.EventId));
        Assert.False(TabDayEnd.HeatApplies(TabDayEnd.IdleEventId));
        Assert.True(TabDayEnd.HeatApplies("typo"));
    }

    [Fact]
    public void The_service_rows_cannot_be_booked_by_a_caller()
    {
        On(TabDayEnd.IdleEventId, TabDayEnd.DailiesEventId, TabDayEnd.StreakEventId);
        using var service = Make();
        Assert.False(service.Note(TabDayEnd.IdleEventId).Booked);
        Assert.False(service.Note(TabDayEnd.StreakEventId).Booked);
        Assert.Equal(0, service.BalanceSeconds);
    }

    [Fact]
    public void An_idle_day_is_charged_once_the_next_day_starts()
    {
        On(TabDayEnd.IdleEventId);
        using var service = Make();
        service.NoteSeen();
        _minutes[CircesTab.DayKey(_local)] = 4;

        _local = _local.AddDays(1);
        service.NoteSeen();
        service.NoteSeen();

        Assert.Equal(TabDayEnd.IdleSeconds, service.BalanceSeconds);
    }

    [Fact]
    public void A_day_with_enough_conditioning_is_not_idle()
    {
        On(TabDayEnd.IdleEventId);
        using var service = Make();
        service.NoteSeen();
        _minutes[CircesTab.DayKey(_local)] = 25;

        _local = _local.AddDays(1);
        service.NoteSeen();

        Assert.Equal(0, service.BalanceSeconds);
    }

    [Fact]
    public void A_day_nobody_can_read_is_never_charged()
    {
        Assert.Equal(0, TabDayEnd.IdleCharge(null));
        Assert.Equal(TabDayEnd.IdleSeconds, TabDayEnd.IdleCharge(0));
        Assert.Equal(0, TabDayEnd.IdleCharge(TabDayEnd.IdleMinutes));
    }

    [Fact]
    public void Open_dailies_are_charged_when_the_next_board_arrives()
    {
        On(TabDayEnd.DailiesEventId);
        using var service = Make();
        service.NoteQuestBoard(3);
        service.NoteQuestBoard(2);
        Assert.Equal(0, service.BalanceSeconds);

        _local = _local.AddDays(1);
        service.NoteQuestBoard(3);
        service.NoteQuestBoard(3);

        Assert.Equal(2 * TabDayEnd.PerDailySeconds, service.BalanceSeconds);
    }

    [Fact]
    public void A_board_left_from_before_a_restart_is_judged_at_the_next_launch()
    {
        On(TabDayEnd.DailiesEventId);
        using (var first = Make()) first.NoteQuestBoard(1);

        _local = _local.AddDays(1);
        using var second = Make();
        second.NoteSeen();

        Assert.Equal(TabDayEnd.PerDailySeconds, second.BalanceSeconds);
    }

    [Fact]
    public void Every_seventh_session_day_in_a_row_takes_time_back()
    {
        // The session row itself off: the streak counts finished sessions either way. A cost each
        // day gives the credit something to take back (a credit never reaches below the floor).
        _options = _options with { Prices = new HashSet<string> { "attention", TabDayEnd.StreakEventId } };
        using var service = Make();
        for (var day = 0; day < 7; day++)
        {
            service.Note("attention");
            service.Note("session");
            service.Note("session");
            if (day < 6) _local = _local.AddDays(1);
        }

        Assert.Equal(7 * 300 + TabDayEnd.StreakSeconds, service.BalanceSeconds);
        Assert.Contains(service.Bill().Lines, l => l.EventId == TabDayEnd.StreakEventId);
    }

    [Fact]
    public void A_gap_starts_the_streak_again()
    {
        Assert.Equal(1, TabDayEnd.NextStreak(null, 0, _local));
        Assert.Equal(5, TabDayEnd.NextStreak(CircesTab.DayKey(_local.AddDays(-1)), 4, _local));
        Assert.Equal(1, TabDayEnd.NextStreak(CircesTab.DayKey(_local.AddDays(-2)), 4, _local));
        Assert.Equal(4, TabDayEnd.NextStreak(CircesTab.DayKey(_local), 4, _local));
        Assert.True(TabDayEnd.StreakPays(6, 7));
        Assert.False(TabDayEnd.StreakPays(7, 7));
    }

    [Fact]
    public void The_pop_names_its_source_and_the_badge_stays_faded()
    {
        Assert.Equal(NatashasFavourite.EventId, BookedFlashPlan.For(NatashasFavourite.EventId, NatashasFavourite.Seconds, ConditioningControlPanel.Models.MotionLevel.Full)!.Value.Source);
        Assert.Null(BookedFlashPlan.For("", 30, ConditioningControlPanel.Models.MotionLevel.Off)!.Value.Source);
        Assert.InRange(BookedFlashPlan.SourceOpacity, 0.6, 0.8);
    }

    [Fact]
    public void Natasha_costs_more_and_whispers_quieter()
    {
        Assert.Equal(300, NatashasFavourite.Seconds);
        Assert.True(NatashasFavourite.HaloOpacity <= 0.2);
        Assert.True(NatashasFavourite.WashPeak <= 0.12);
        Assert.True(NatashasFavourite.BubbleWashBase <= 0.06);
    }

    [Fact]
    public void Each_preset_sets_its_own_stakes()
    {
        var gentle = TabPresets.Find(TabPresets.Gentle)!;
        var strict = TabPresets.Find(TabPresets.Strict)!;
        var circe = TabPresets.Find(TabPresets.Circe)!;
        Assert.True(gentle.DayMinutes < strict.DayMinutes && strict.DayMinutes < circe.DayMinutes);
        Assert.Equal(TabLimits.Default.DailySeconds / 60, strict.DayMinutes);
        // A whole month of Gentle at its limit is under a day late; Strict about four days, Circe eight.
        Assert.Equal((16, false), TabPresets.WorstMonth(gentle));
        Assert.Equal((4, true), TabPresets.WorstMonth(strict));
        Assert.Equal((8, true), TabPresets.WorstMonth(circe));
    }

    [Fact]
    public void A_preset_lowers_limits_at_once_and_raises_them_a_day_later()
    {
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var day = new LimitSetting(180, 0, null);
        var backlog = new LimitSetting(720, 0, null);

        var (d1, b1) = TabPresets.RequestLimits(TabPresets.Find(TabPresets.Gentle)!, day, backlog, now);
        Assert.Equal(30, LimitChange.Effective(d1, now));
        Assert.Equal(120, LimitChange.Effective(b1, now));

        var (d2, _) = TabPresets.RequestLimits(TabPresets.Find(TabPresets.Circe)!, d1, b1, now);
        Assert.Equal(30, LimitChange.Effective(d2, now));
        Assert.Equal(360, LimitChange.Effective(d2, now + LimitChange.RaiseDelay));
    }

    [Fact]
    public void Presets_carry_the_new_rows()
    {
        Assert.Contains(TabDayEnd.HeatId, TabPresets.Apply(TabPresets.Strict));
        Assert.Contains(TabDayEnd.IdleEventId, TabPresets.Apply(TabPresets.Strict));
        Assert.Contains(TabDayEnd.StreakEventId, TabPresets.Apply(TabPresets.Gentle));
        Assert.Equal(TabPresets.Strict, TabPresets.Match(TabPresets.Apply(TabPresets.Strict)));
    }
}
