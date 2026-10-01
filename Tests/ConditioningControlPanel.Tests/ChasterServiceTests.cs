using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Chaster service: nothing books unless the tab is on AND an account is linked, a safety
/// exit opens a hold, the settle only ever adds, a failed settle loses nothing, and a dead link
/// keeps the tab.
/// </summary>
public class ChasterServiceTests : IDisposable
{
    private sealed class MemoryStore : IChasterTokenStore
    {
        public ChasterStoredTokens? Tokens;
        public ChasterStoredTokens? Read() => Tokens;
        public void Write(ChasterStoredTokens tokens) => Tokens = tokens;
        public void Clear() => Tokens = null;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<(string Path, string? Body)> Seen = new();
        public bool RefreshIsReal;
        public TaskCompletionSource? HoldRefresh;
        public Func<string, HttpResponseMessage> Answer = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            var body = r.Content == null ? null : await r.Content.ReadAsStringAsync(ct);
            // A day later the 300 s token is stale, so every test that moves the clock refreshes.
            // Tests about the refresh itself set RefreshIsReal and see the call like any other.
            if (path == "/chaster/refresh" && !RefreshIsReal)
                return Json(200, "{\"access_token\":\"AT\",\"expires_in\":300}");
            Seen.Add((path, body));
            if (path == "/chaster/refresh" && HoldRefresh != null) await HoldRefresh.Task;
            return Answer(path);
        }
    }

    private static HttpResponseMessage Json(int status, string body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body) };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-chaster-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _http = new();
    private readonly MemoryStore _store = new();
    private DateTime _utc = new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);
    private ChasterOptions _options = new(true, "lock1", new HashSet<string> { "typo", "session", "watcher" });

    public ChasterServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _store.Tokens = new ChasterStoredTokens("AT", "RT", _utc.AddSeconds(300));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } // swallow: temp dir, best effort
    }

    private ChasterService Make() =>
        new(new ChasterClient(_http), _store, Path.Combine(_dir, "tab.json"), () => _options, () => _utc, () => _utc.ToLocalTime());

    [Fact]
    public void Nothing_books_with_the_tab_off()
    {
        _options = _options with { TabEnabled = false };
        using var service = Make();

        Assert.False(service.Note("typo").Booked);
        Assert.False(service.NoteSeconds("watcher", 300).Booked);
        Assert.Equal(0, service.BalanceSeconds);
    }

    [Fact]
    public void Nothing_books_with_no_account_linked()
    {
        _store.Tokens = null;
        using var service = Make();

        Assert.False(service.Note("typo").Booked);
        Assert.False(service.IsLinked);
    }

    [Fact]
    public void Only_a_row_the_player_switched_on_books()
    {
        using var service = Make();
        var seen = new List<(string, int)>();
        service.Booked += (id, b) => seen.Add((id, b.AppliedSeconds));

        service.Note("typo");
        service.Note("attention");

        Assert.Equal(30, service.BalanceSeconds);
        Assert.Equal(new[] { ("typo", 30) }, seen);
    }

    [Fact]
    public void An_awareness_trigger_names_its_own_price_but_never_the_way_out()
    {
        using var service = Make();

        service.NoteSeconds("watcher", 600);
        service.NoteSeconds("panic", 600);
        service.NoteSeconds("watcher", 999999);

        Assert.Equal(CircesTab.DailyCapSeconds, service.BalanceSeconds);
    }

    [Fact]
    public void A_self_priced_event_still_needs_its_row_switched_on()
    {
        _options = _options with { Prices = new HashSet<string> { "typo" } };
        using var service = Make();

        Assert.False(service.NoteSeconds("watcher", 600).Booked);
        Assert.Equal(0, service.BalanceSeconds);
    }

    [Fact]
    public void Today_reads_the_gross_adds_of_the_local_day_and_zero_on_a_new_one()
    {
        using var service = Make();
        service.Note("typo");
        service.Note("session");

        Assert.Equal(30, service.TodayAddedSeconds);
        _utc = _utc.AddDays(1);
        Assert.Equal(0, service.TodayAddedSeconds);
    }

    [Fact]
    public void The_page_lists_costs_then_earn_backs_biggest_first_and_names_every_row()
    {
        var (costs, earnBacks) = TabPageText.Split(TabPrices.All);

        Assert.Equal(TabPrices.All.Count, costs.Count + earnBacks.Count);
        Assert.Equal("program_skipped", costs[0].Id);
        Assert.Equal("streak", earnBacks[0].Id);
        Assert.Equal("+0:30 each", TabPageText.Price(TabPrices.Find("mantra")!, "{0} each"));
        Assert.Equal("-10:00", TabPageText.Price(TabPrices.Find("session")!, "{0} each"));

        var en = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages", "en.json")));
        foreach (var id in TabPrices.All.Select(p => p.Id).Append(CircesTab.JackpotEventId))
            Assert.NotNull(en[TabPageText.NameKey(id)]);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void A_safety_exit_holds_adds_for_ten_minutes_and_credits_still_land()
    {
        using var service = Make();
        service.Note("typo");

        service.NoteSafetyExit();
        var during = service.Note("typo");
        var credit = service.Note("session");
        _utc += ChasterService.SafetyHold + TimeSpan.FromSeconds(1);
        var after = service.Note("typo");

        Assert.Equal(TabRefusal.SafetyExit, during.Refusal);
        Assert.Equal(-30, credit.AppliedSeconds);
        Assert.Equal(30, after.AppliedSeconds);
    }

    /// <summary>DESK-6. The rail chip counts a hold down instead of the lock, and with no lock
    /// running its clock ticks once a minute, so a panic must repaint it at once: the hold is
    /// raised as a lock change, which the chip already repaints on (marshalled to its dispatcher).</summary>
    [Fact]
    public void A_safety_exit_repaints_the_lock_at_once()
    {
        using var service = Make();
        var raised = 0;
        service.LockChanged += () => raised++;

        service.NoteSafetyExit();

        Assert.Equal(1, raised);
        Assert.Equal(ChasterService.SafetyHold, service.SafetyHoldRemaining);
        var chip = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Controls", "ChasterRailChip.cs"));
        Assert.Contains("chaster.LockChanged += OnServiceChanged;", chip);
        Assert.Contains("Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Apply));", chip);
    }

    /// <summary>TAB-9. A red flash whose ring ran out still books +5:00 (and the ladder, the rail
    /// and the page still see it), but the booking says nobody prompted it, and Circe's line picker
    /// is handed the whole booking so she stays quiet for it. A popped red bubble still speaks.</summary>
    [Fact]
    public void A_ring_that_ran_out_books_but_Circe_does_not_say_it_was_popped()
    {
        _options = _options with { Prices = new HashSet<string> { NatashasFavourite.EventId } };
        using var service = Make();
        var seen = new List<TabBooking>();
        service.Booked += (_, b) => seen.Add(b);

        var ring = service.NoteAt(NatashasFavourite.EventId, new System.Windows.Point(10, 10), unprompted: true);
        var pop = service.NoteAt(NatashasFavourite.EventId, new System.Windows.Point(10, 10));

        Assert.Equal(new[] { 300, 300 }, seen.Select(b => b.AppliedSeconds));
        Assert.True(ring.Unprompted && seen[0].Unprompted);
        Assert.False(pop.Unprompted || seen[1].Unprompted);
        Assert.Null(CirceLines.ForBooking(NatashasFavourite.EventId, seen[0]));
        Assert.Equal(CirceMoment.Popped, CirceLines.ForBooking(NatashasFavourite.EventId, seen[1]));
        var lines = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.CirceLines.cs"));
        Assert.Contains("CirceLines.ForBooking(eventId, booking)", lines);
    }

    [Fact]
    public void The_tab_survives_a_restart()
    {
        using (var first = Make()) first.Note("typo");

        using var second = Make();

        Assert.Equal(30, second.BalanceSeconds);
    }

    [Fact]
    public async Task Every_settle_adds_what_is_waiting_to_the_chosen_lock()
    {
        using var service = Make();
        service.NoteSeconds("watcher", 600);

        var first = await service.SettleAsync();
        service.Note("typo");
        var again = await service.SettleAsync();
        var empty = await service.SettleAsync();

        Assert.Equal(SettleOutcome.Pushed, first);
        Assert.Equal(SettleOutcome.Pushed, again);
        Assert.Equal(SettleOutcome.Nothing, empty);
        Assert.Equal(0, service.BalanceSeconds);
        Assert.Equal(630, service.Bill().PushedSeconds);
        var pushes = _http.Seen.Where(c => c.Path == "/locks/lock1/update-time").ToList();
        Assert.Equal(2, pushes.Count);
        Assert.Contains("\"duration\":600", pushes[0].Body);
        Assert.Contains("\"duration\":30", pushes[1].Body);
    }

    private void LockEnds(DateTime endUtc, bool frozen = false) =>
        _http.Answer = p => p == "/locks"
            ? Json(200, "[{\"_id\":\"lock1\",\"role\":\"wearer\",\"isFrozen\":" + (frozen ? "true" : "false")
                + ",\"endDate\":\"" + endUtc.ToString("o") + "\"}]")
            : new HttpResponseMessage(HttpStatusCode.NoContent);

    [Fact]
    public async Task A_run_out_lock_is_left_ready_to_unlock_unless_the_player_opted_in()
    {
        // Chaster restarts a run-out timer from now (live, 2026-09-29), so any push would lock
        // it again. Off (the default): nothing goes out and the balance waits.
        LockEnds(_utc.AddMinutes(-18));
        using (var off = Make())
        {
            off.NoteSeconds("watcher", 300);
            Assert.Equal(SettleOutcome.LockRanOut, await off.SettleAsync());
            Assert.DoesNotContain(_http.Seen, s => s.Path.EndsWith("update-time"));
            Assert.Equal(300, off.BalanceSeconds);
        }

        // On: the push goes out for the price alone. No catch-up: Chaster already brings the
        // end up to now, so a catch-up would be paid twice.
        _options = _options with { RelockPastEnd = true };
        using var service = Make();
        Assert.Equal(SettleOutcome.Pushed, await service.SettleAsync());
        var push = Assert.Single(_http.Seen, s => s.Path.EndsWith("update-time"));
        Assert.Contains("\"duration\":300", push.Body);
        Assert.Equal(0, service.BalanceSeconds);
        Assert.Equal(300, service.AddedLifetimeSeconds);
    }

    [Fact]
    public async Task A_lock_that_ran_out_hours_ago_is_never_pulled_back_shut()
    {
        LockEnds(_utc.AddSeconds(-LockRelock.MaxLateSeconds - 60));
        _options = _options with { RelockPastEnd = true };
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.LockRanOut, await service.SettleAsync());
        Assert.DoesNotContain(_http.Seen, s => s.Path.EndsWith("update-time"));
        Assert.Equal(300, service.BalanceSeconds);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_running_or_frozen_lock_takes_the_price_whatever_the_option(bool relock, bool frozen)
    {
        LockEnds(frozen ? _utc.AddHours(-2) : _utc.AddHours(3), frozen);
        _options = _options with { RelockPastEnd = relock };
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.Pushed, await service.SettleAsync());
        var push = Assert.Single(_http.Seen, s => s.Path.EndsWith("update-time"));
        Assert.Contains("\"duration\":300", push.Body);
    }

    [Fact]
    public async Task A_lock_read_that_fails_never_blocks_the_push()
    {
        _http.Answer = p => p == "/locks" ? Json(503, "") : new HttpResponseMessage(HttpStatusCode.NoContent);
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.Pushed, await service.SettleAsync());
        Assert.Single(_http.Seen, s => s.Path.EndsWith("update-time"));
    }

    [Fact]
    public async Task An_api_401_refreshes_once_and_keeps_the_link()
    {
        _http.RefreshIsReal = true;
        var calls = 0;
        _http.Answer = p => p == "/chaster/refresh"
            ? Json(200, "{\"access_token\":\"NEW\",\"expires_in\":300}")
            : p.EndsWith("update-time") && ++calls == 1 ? Json(401, "") : new HttpResponseMessage(HttpStatusCode.NoContent);
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.Pushed, await service.SettleAsync());
        Assert.True(service.IsLinked);
        Assert.Equal(new[] { "/locks", "/locks/lock1/update-time", "/chaster/refresh", "/locks/lock1/update-time" }, _http.Seen.Select(s => s.Path));
        Assert.Equal("NEW", _store.Tokens!.AccessToken);
        Assert.Equal(0, service.BalanceSeconds);
    }

    [Fact]
    public async Task An_api_that_keeps_answering_401_never_drops_the_link()
    {
        _http.RefreshIsReal = true;
        _http.Answer = p => p == "/chaster/refresh"
            ? Json(200, "{\"access_token\":\"NEW\",\"expires_in\":300}")
            : Json(401, "");
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.TryLater, await service.SettleAsync());
        Assert.True(service.IsLinked);
        Assert.Equal(300, service.BalanceSeconds);
        Assert.Null(await service.GetLocksAsync());
        Assert.True(service.IsLinked);
    }

    [Fact]
    public async Task A_refresh_that_lands_after_an_unlink_is_thrown_away()
    {
        _http.RefreshIsReal = true;
        _http.HoldRefresh = new TaskCompletionSource();
        _store.Tokens = new ChasterStoredTokens("OLD", "RT", _utc.AddSeconds(-5));
        _http.Answer = p => p switch
        {
            "/chaster/refresh" => Json(200, "{\"access_token\":\"NEW\",\"refresh_token\":\"RT2\",\"expires_in\":300}"),
            "/chaster/revoke" => Json(200, "{\"ok\":true}"),
            _ => Json(200, "[]"),
        };
        using var service = Make();

        var locks = service.GetLocksAsync();
        await service.UnlinkAsync();
        _http.HoldRefresh.SetResult();

        Assert.Null(await locks);
        Assert.False(service.IsLinked);
        Assert.Null(_store.Tokens);
        // Both grants are revoked: the one unlinked, and the one the late refresh minted.
        await Task.Delay(50);
        var revoked = _http.Seen.Where(s => s.Path == "/chaster/revoke").Select(s => s.Body).ToList();
        Assert.Contains(revoked, b => b!.Contains("\"RT\""));
        Assert.Contains(revoked, b => b!.Contains("\"RT2\""));
    }

    [Fact]
    public async Task A_write_that_broke_mid_flight_keeps_its_pending_mark()
    {
        _http.Answer = _ => Json(502, "");
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.TryLater, await service.SettleAsync());
        // Counted as landed on the next settle, never sent again.
        _http.Answer = _ => new HttpResponseMessage(HttpStatusCode.NoContent);
        Assert.Equal(SettleOutcome.Nothing, await service.SettleAsync());
        Assert.Single(_http.Seen, s => s.Path.EndsWith("update-time"));
        Assert.Equal(0, service.BalanceSeconds);
    }

    [Fact]
    public void A_tab_file_is_clamped_when_it_is_read()
    {
        File.WriteAllText(Path.Combine(_dir, "tab.json"), "{\"balance\":99999999,\"pending\":-5,\"day_added\":-40}");
        using var service = Make();

        Assert.Equal(CircesTab.BacklogCapSeconds, service.BalanceSeconds);
    }

    [Theory]
    [InlineData(-60, false, true)]
    [InlineData(-60, true, true)]
    [InlineData(0, false, false)]
    [InlineData(18 * 60, false, false)]
    [InlineData(18 * 60, true, true)]
    [InlineData(LockRelock.MaxLateSeconds, true, true)]
    [InlineData(LockRelock.MaxLateSeconds + 1, true, false)]
    public void A_run_out_lock_takes_a_push_only_when_opted_in_and_recent(int lateSeconds, bool relock, bool expected)
    {
        var now = new DateTime(2026, 9, 23, 20, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, LockRelock.MayPush(now.AddSeconds(-lateSeconds), now, relock));
        Assert.True(LockRelock.MayPush(null, now, relock));
    }

    [Fact]
    public async Task A_credit_or_an_empty_tab_sends_nothing()
    {
        using var service = Make();

        Assert.Equal(SettleOutcome.Nothing, await service.SettleAsync());
        Assert.Empty(_http.Seen);
    }

    [Fact]
    public async Task A_lock_id_the_client_refuses_waits_and_is_never_counted_as_landed()
    {
        // The owner's install held "demo-lock" from a DEBUG demo run (2026-10-01): AddTimeAsync
        // threw before sending, the pending mark stayed, and the next settle booked 15 min as
        // landed that Chaster never got.
        _options = _options with { LockId = "demo-lock" };
        _http.Answer = p => p == "/locks" ? Json(200, "[{\"_id\":\"solo9\",\"role\":\"wearer\"}]") : new HttpResponseMessage(HttpStatusCode.NoContent);
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.NoLockChosen, await service.SettleAsync());
        Assert.Equal(SettleOutcome.NoLockChosen, await service.SettleAsync());
        Assert.DoesNotContain(_http.Seen, s => s.Path.EndsWith("update-time"));
        Assert.Equal(300, service.BalanceSeconds);
    }

    [Fact]
    public async Task With_no_lock_chosen_nothing_is_pushed_not_even_to_the_only_lock()
    {
        _options = _options with { LockId = null };
        _http.Answer = p => p == "/locks" ? Json(200, "[{\"_id\":\"solo9\",\"role\":\"wearer\"}]") : new HttpResponseMessage(HttpStatusCode.NoContent);
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.NoLockChosen, await service.SettleAsync());
        Assert.DoesNotContain(_http.Seen, s => s.Path.EndsWith("update-time"));
        Assert.Equal(300, service.BalanceSeconds);
        Assert.Equal(0, service.PushableTodaySeconds);

        _options = _options with { LockId = "solo9" };
        Assert.Equal(SettleOutcome.Pushed, await service.SettleAsync());
        Assert.Contains(_http.Seen, s => s.Path == "/locks/solo9/update-time");
    }

    [Fact]
    public async Task Paused_books_nothing_pushes_nothing_and_resuming_lets_the_balance_go()
    {
        _options = _options with { Prices = new HashSet<string> { "typo", "watcher", NatashasFavourite.EventId } };
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        _options = _options with { Paused = true };
        Assert.True(service.IsPaused);
        Assert.Equal(TabRefusal.Paused, service.Note("typo").Refusal);
        Assert.False(service.NoteSeconds("watcher", 300).Booked);
        Assert.False(service.CanBook(NatashasFavourite.EventId));
        Assert.Equal(0, service.PushableTodaySeconds);
        Assert.Equal(SettleOutcome.Nothing, await service.SettleAsync());
        Assert.Empty(_http.Seen);
        Assert.Equal(300, service.BalanceSeconds);

        _options = _options with { Paused = false };
        Assert.True(service.CanBook(NatashasFavourite.EventId));
        Assert.Equal(SettleOutcome.Pushed, await service.SettleAsync());
        Assert.Equal(0, service.BalanceSeconds);
    }

    [Fact]
    public void An_escape_attempt_books_three_times_a_day_at_most()
    {
        _options = _options with { Prices = new HashSet<string> { "escape" } };
        using var service = Make();

        for (var i = 0; i < 3; i++) Assert.Equal(180, service.Note("escape").AppliedSeconds);
        Assert.False(service.CanBook("escape"));
        var fourth = service.Note("escape");
        Assert.Equal(0, fourth.AppliedSeconds);
        Assert.Equal(TabRefusal.RowCap, fourth.Refusal);
        Assert.Equal(540, service.BalanceSeconds);

        _utc = _utc.AddDays(1);
        Assert.True(service.CanBook("escape"));
        Assert.Equal(180, service.Note("escape").AppliedSeconds);
    }

    [Fact]
    public void What_goes_today_stops_at_the_daily_limit_and_the_rest_waits()
    {
        _options = _options with { Limits = TabLimits.FromMinutes(15, 120) };
        using var service = Make();
        // Two days of bookings with no push in between: more than one day's limit waits.
        Assert.Equal(900, service.NoteSeconds("watcher", 900).AppliedSeconds);
        _utc = _utc.AddDays(1);
        Assert.Equal(900, service.NoteSeconds("watcher", 900).AppliedSeconds);

        Assert.Equal(1800, service.BalanceSeconds);
        Assert.Equal(900, service.PushableTodaySeconds);
        var line = TabPageText.Tag(service.BalanceSeconds, service.PushableTodaySeconds, paused: false, lockPicked: true);
        Assert.Equal("chaster_tag_split", line.Key);
        Assert.Equal("15:00", line.Today);
        Assert.Equal("15:00", line.Later);
    }

    [Fact]
    public async Task A_settle_chaster_did_not_take_loses_nothing_and_goes_again()
    {
        _http.Answer = _ => Json(503, "");
        using var service = Make();
        service.NoteSeconds("watcher", 600);

        Assert.Equal(SettleOutcome.TryLater, await service.SettleAsync());
        Assert.Equal(600, service.BalanceSeconds);
        Assert.True(service.IsLinked);

        _http.Answer = _ => new HttpResponseMessage(HttpStatusCode.NoContent);
        Assert.Equal(SettleOutcome.Pushed, await service.SettleAsync());
    }

    [Fact]
    public async Task A_push_nobody_answered_is_counted_as_landed_and_never_sent_twice()
    {
        _http.Answer = _ => throw new TaskCanceledException("timeout");
        using var service = Make();
        service.NoteSeconds("watcher", 600);

        Assert.Equal(SettleOutcome.TryLater, await service.SettleAsync());
        Assert.Equal(600, service.BalanceSeconds);

        _http.Answer = _ => new HttpResponseMessage(HttpStatusCode.NoContent);
        Assert.Equal(SettleOutcome.Nothing, await service.SettleAsync());
        Assert.Equal(0, service.BalanceSeconds);
        Assert.Equal(0, service.Bill().PushedSeconds);
        Assert.Single(_http.Seen, s => s.Path.EndsWith("update-time"));
    }

    [Fact]
    public async Task A_push_cut_off_by_a_crash_is_not_sent_again_on_the_next_launch()
    {
        var day = CircesTab.DayKey(_utc.ToLocalTime());
        File.WriteAllText(Path.Combine(_dir, "tab.json"), "{\"balance\":600,\"pending\":600,\"pending_day\":\"" + day + "\"}");
        using var service = Make();

        Assert.Equal(SettleOutcome.Nothing, await service.SettleAsync());
        Assert.Equal(0, service.BalanceSeconds);
        Assert.Empty(_http.Seen);
    }

    [Fact]
    public async Task A_chosen_lock_that_ended_asks_again_and_the_push_follows_the_new_pick()
    {
        _http.Answer = _ => Json(404, "");
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.NoLockChosen, await service.TickAsync());
        Assert.Equal(300, service.BalanceSeconds);
        Assert.True(service.IsLinked);

        _options = _options with { LockId = "lock2" };
        _http.Answer = _ => new HttpResponseMessage(HttpStatusCode.NoContent);
        Assert.Equal(SettleOutcome.Pushed, await service.TickAsync());
        // A settle now ends by re-reading the lock, so the last call is that GET. The push is the
        // last WRITE - see The_settle_re_reads_the_lock_it_just_moved.
        Assert.Equal("/locks/lock2/update-time", _http.Seen.Last(s => s.Path.EndsWith("update-time")).Path);
    }

    [Fact]
    public async Task Two_callers_with_a_stale_token_refresh_once()
    {
        _http.RefreshIsReal = true;
        _http.HoldRefresh = new TaskCompletionSource();
        _store.Tokens = new ChasterStoredTokens("OLD", "RT", _utc.AddSeconds(-5));
        _http.Answer = p => p == "/chaster/refresh"
            ? Json(200, "{\"access_token\":\"NEW\",\"refresh_token\":\"RT2\",\"expires_in\":300}")
            : Json(200, "[]");
        using var service = Make();

        var first = service.GetLocksAsync();
        var second = service.GetLocksAsync();
        _http.HoldRefresh.SetResult();
        await Task.WhenAll(first, second);

        Assert.Single(_http.Seen, s => s.Path == "/chaster/refresh");
        Assert.Equal("RT2", _store.Tokens!.RefreshToken);
    }

    [Fact]
    public async Task A_backlog_lands_in_one_push()
    {
        using var service = Make();
        for (var i = 0; i < 3; i++) service.NoteSeconds("watcher", 3600);

        await service.SettleAsync();

        Assert.Contains("\"duration\":10800", _http.Seen.Single(s => s.Path.EndsWith("update-time")).Body);
        Assert.Equal(0, service.BalanceSeconds);
    }

    [Fact]
    public async Task A_hand_edited_tab_file_never_sends_more_than_one_day_limit_a_day()
    {
        _options = _options with { Limits = TabLimits.FromMinutes(60, 180) };
        File.WriteAllText(Path.Combine(_dir, "tab.json"), "{\"balance\":999999999,\"pushed_net\":0}");
        using var service = Make();

        for (var i = 0; i < 6; i++) await service.SettleAsync();

        // Clamped to the backlog (3 h), and only the day's hour of it went out.
        Assert.Contains("\"duration\":3600", _http.Seen.Single(s => s.Path.EndsWith("update-time")).Body);
        Assert.Equal(2 * 3600, service.BalanceSeconds);

        _utc = _utc.AddDays(1);
        await service.SettleAsync();
        Assert.Equal(2, _http.Seen.Count(c => c.Path == "/locks/lock1/update-time"));
    }

    [Fact]
    public void A_remote_session_adds_at_most_half_an_hour_a_day_from_every_source()
    {
        _options = _options with { RemoteOpen = true };
        using var service = Make();

        var first = service.NoteSeconds("watcher", 1500);
        var second = service.NoteSeconds("watcher", 1500);
        var third = service.Note("typo");

        Assert.Equal(new TabBooking(1500, TabRefusal.None), first);
        Assert.Equal(new TabBooking(300, TabRefusal.Remote), second);
        Assert.Equal(new TabBooking(0, TabRefusal.Remote), third);
        Assert.False(service.CanBook("typo"));
        // Credits still land: the cap only ever holds time back.
        Assert.True(service.Note("session").Booked);

        // Reconnecting is no fresh share; tomorrow is.
        using (var again = Make()) Assert.False(again.Note("typo").Booked);
        _utc = _utc.AddDays(1);
        using var tomorrow = Make();
        Assert.True(tomorrow.Note("typo").Booked);
    }

    [Fact]
    public void With_a_remote_session_open_and_the_panic_key_off_nothing_adds()
    {
        _options = _options with { RemoteOpen = true, PanicArmed = false };
        using var service = Make();

        Assert.Equal(new TabBooking(0, TabRefusal.Remote), service.Note("typo"));
        Assert.False(service.CanBook("typo"));

        // Alone, a player may run with the panic key off: only a Remote session changes that.
        _options = _options with { RemoteOpen = false };
        Assert.True(service.Note("typo").Booked);
    }

    [Fact]
    public void The_limits_the_player_set_are_the_ones_a_booking_reads()
    {
        _options = _options with { Limits = TabLimits.FromMinutes(15, 60) };
        using var service = Make();

        service.NoteSeconds("watcher", 600);
        var clamped = service.NoteSeconds("watcher", 600);

        Assert.Equal(new TabBooking(300, TabRefusal.DailyCap), clamped);
        Assert.Equal(900, service.TodayAddedSeconds);
        Assert.Equal(900, service.Caps.DailySeconds);
    }

    [Fact]
    public async Task A_stale_token_is_refreshed_first_and_the_new_one_is_kept()
    {
        _http.RefreshIsReal = true;
        _store.Tokens = new ChasterStoredTokens("OLD", "RT", _utc.AddSeconds(10));
        _http.Answer = p => p == "/chaster/refresh"
            ? Json(200, "{\"access_token\":\"NEW\",\"expires_in\":300}")
            : new HttpResponseMessage(HttpStatusCode.NoContent);
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        await service.SettleAsync();

        Assert.Equal(new[] { "/chaster/refresh", "/locks", "/locks/lock1/update-time" }, _http.Seen.Select(s => s.Path));
        Assert.Equal(new ChasterStoredTokens("NEW", "RT", _utc.AddSeconds(300)), _store.Tokens);
    }

    [Fact]
    public async Task A_dead_link_is_dropped_and_the_tab_is_kept()
    {
        _http.RefreshIsReal = true;
        _store.Tokens = new ChasterStoredTokens("OLD", "RT", _utc.AddSeconds(-5));
        _http.Answer = _ => Json(401, "{\"error\":\"link_expired\"}");
        using var service = Make();
        service.NoteSeconds("watcher", 300);
        var changed = 0;
        service.LinkChanged += () => changed++;

        Assert.Equal(SettleOutcome.LinkExpired, await service.SettleAsync());
        Assert.False(service.IsLinked);
        Assert.Equal(300, service.BalanceSeconds);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task An_outage_during_refresh_never_drops_the_link()
    {
        _http.RefreshIsReal = true;
        _store.Tokens = new ChasterStoredTokens("OLD", "RT", _utc.AddSeconds(-5));
        _http.Answer = _ => Json(502, "");
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.TryLater, await service.SettleAsync());
        Assert.True(service.IsLinked);
    }

    [Fact]
    public async Task Unlinking_forgets_the_tokens_tells_chaster_and_keeps_the_tab()
    {
        using var service = Make();
        service.Note("typo");

        await service.UnlinkAsync();

        Assert.False(service.IsLinked);
        Assert.Equal("/chaster/revoke", _http.Seen.Single().Path);
        Assert.Equal(30, service.BalanceSeconds);
        Assert.False(service.Note("typo").Booked);
    }

    [Fact]
    public void The_jackpot_wipes_the_tab_through_the_service_too()
    {
        using var service = Make();
        service.NoteSeconds("watcher", 900);

        Assert.Equal(-900, service.Wipe().AppliedSeconds);
        Assert.Equal(0, service.BalanceSeconds);
    }

    [Fact]
    public void An_unreadable_tab_file_starts_clean_instead_of_throwing()
    {
        File.WriteAllText(Path.Combine(_dir, "tab.json"), "{not json");

        using var service = Make();

        Assert.Equal(0, service.BalanceSeconds);
    }

    [Fact]
    public async Task The_tick_retries_after_a_try_later_and_pushes_whatever_is_new()
    {
        _http.Answer = _ => Json(503, "");
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.TryLater, await service.TickAsync());
        Assert.Equal(300, service.BalanceSeconds);
        _http.Answer = _ => new HttpResponseMessage(HttpStatusCode.NoContent);
        Assert.Equal(SettleOutcome.Pushed, await service.TickAsync());
        Assert.Equal(SettleOutcome.Nothing, await service.TickAsync());

        service.NoteSeconds("watcher", 300);
        Assert.Equal(SettleOutcome.Pushed, await service.TickAsync());
    }

    // ============================== the lock snapshot the chip reads ==============================

    private const string TwoLocks = "[{\"_id\":\"lock1\",\"role\":\"wearer\",\"title\":\"Circe\",\"endDate\":\"2026-09-24T10:00:00Z\"},"
                                  + "{\"_id\":\"other\",\"role\":\"wearer\"}]";

    private void AnswerLocks(string json) =>
        _http.Answer = p => p == "/locks" ? Json(200, json) : new HttpResponseMessage(HttpStatusCode.NoContent);

    [Fact]
    public async Task The_chosen_lock_is_the_one_kept_even_when_several_are_active()
    {
        AnswerLocks(TwoLocks);
        using var service = Make();

        var snapshot = await service.RefreshLockAsync();

        Assert.Equal(LockLookup.Chosen, service.LockLookup);
        Assert.Equal("lock1", snapshot!.Id);
        Assert.Equal("Circe", snapshot.Title);
        Assert.Equal(new DateTime(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc), snapshot.EndsAtUtc);
        Assert.Equal(TimeSpan.FromDays(3), snapshot.Remaining(_utc));
    }

    [Fact]
    public async Task One_lock_and_no_pick_is_still_a_pick_to_make()
    {
        _options = _options with { LockId = null };
        AnswerLocks("[{\"_id\":\"solo9\",\"role\":\"wearer\"}]");
        using var service = Make();

        Assert.Null(await service.RefreshLockAsync());
        Assert.Equal(LockLookup.Ambiguous, service.LockLookup);
    }

    [Fact]
    public async Task A_picked_lock_that_is_gone_stays_unpicked_and_never_falls_back_to_another()
    {
        _options = _options with { LockId = "gone" };
        AnswerLocks("[{\"_id\":\"solo9\",\"role\":\"wearer\"}]");
        using var service = Make();

        Assert.Null(await service.RefreshLockAsync());
        Assert.Equal(LockLookup.Ambiguous, service.LockLookup);
    }

    [Fact]
    public async Task Several_locks_and_no_pick_is_ambiguous_and_never_guessed()
    {
        _options = _options with { LockId = null };
        AnswerLocks(TwoLocks);
        using var service = Make();

        Assert.Null(await service.RefreshLockAsync());
        Assert.Equal(LockLookup.Ambiguous, service.LockLookup);
    }

    [Fact]
    public async Task No_active_lock_reads_none_with_nothing_held()
    {
        AnswerLocks("[]");
        using var service = Make();

        Assert.Null(await service.RefreshLockAsync());
        Assert.Equal(LockLookup.None, service.LockLookup);
    }

    [Fact]
    public async Task An_unlinked_account_holds_no_lock_at_all()
    {
        _store.Tokens = null;
        using var service = Make();

        Assert.Null(await service.RefreshLockAsync());
        Assert.Equal(LockLookup.Unlinked, service.LockLookup);
        Assert.Empty(_http.Seen);
    }

    [Fact]
    public async Task Chaster_being_away_keeps_the_last_snapshot_rather_than_blanking_the_clock()
    {
        AnswerLocks(TwoLocks);
        using var service = Make();
        var first = await service.RefreshLockAsync();

        _http.Answer = _ => Json(503, "");
        var second = await service.RefreshLockAsync();

        Assert.Equal(LockLookup.Away, service.LockLookup);
        Assert.Equal(first, second);
        Assert.Equal(first, service.Lock);
    }

    [Fact]
    public async Task The_snapshot_changing_is_announced_once_per_real_change()
    {
        AnswerLocks(TwoLocks);
        using var service = Make();
        var announced = 0;
        service.LockChanged += () => announced++;

        await service.RefreshLockAsync();
        await service.RefreshLockAsync();   // the same answer twice is not a change

        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task The_settle_re_reads_the_lock_it_just_moved()
    {
        AnswerLocks(TwoLocks);
        using var service = Make();
        service.NoteSeconds("watcher", 600);

        Assert.Equal(SettleOutcome.Pushed, await service.TickAsync());

        Assert.Contains(_http.Seen, s => s.Path == "/locks/lock1/update-time");
        Assert.Equal("/locks", _http.Seen.Last().Path);
        Assert.Equal(LockLookup.Chosen, service.LockLookup);
    }

    [Fact]
    public async Task A_settle_that_pushed_nothing_pays_for_no_call()
    {
        AnswerLocks(TwoLocks);
        using var service = Make();

        Assert.Equal(SettleOutcome.Nothing, await service.TickAsync());
        Assert.Equal(SettleOutcome.Nothing, await service.TickAsync());

        // Nothing moved, so there is nothing to re-read: the re-read rides the push, not the tick.
        Assert.Empty(_http.Seen);
    }

    [Fact]
    public void The_tab_ships_switched_off_with_no_price_on()
    {
        var settings = new ConditioningControlPanel.Models.AppSettings();

        Assert.False(settings.ChasterTabEnabled);
        Assert.Null(settings.ChasterLockId);
        Assert.Empty(settings.ChasterPrices);
    }
}
