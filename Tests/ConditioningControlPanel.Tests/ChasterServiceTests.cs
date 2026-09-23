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

        Assert.Equal(15, service.BalanceSeconds);
        Assert.Equal(new[] { ("typo", 15) }, seen);
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

        Assert.Equal(15, service.TodayAddedSeconds);
        _utc = _utc.AddDays(1);
        Assert.Equal(0, service.TodayAddedSeconds);
    }

    [Fact]
    public void The_page_lists_costs_then_earn_backs_biggest_first_and_names_every_row()
    {
        var (costs, earnBacks) = TabPageText.Split(TabPrices.All);

        Assert.Equal(TabPrices.All.Count, costs.Count + earnBacks.Count);
        Assert.Equal("program_skipped", costs[0].Id);
        Assert.Equal("quest_weekly", earnBacks[0].Id);
        Assert.Equal("+0:10 each", TabPageText.Price(TabPrices.Find("mantra")!, "{0} each"));
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
        Assert.Equal(-15, credit.AppliedSeconds);
        Assert.Equal(15, after.AppliedSeconds);
    }

    [Fact]
    public void The_tab_survives_a_restart()
    {
        using (var first = Make()) first.Note("typo");

        using var second = Make();

        Assert.Equal(15, second.BalanceSeconds);
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
        Assert.Equal(615, service.Bill().PushedSeconds);
        Assert.Equal(2, _http.Seen.Count);
        Assert.All(_http.Seen, c => Assert.Equal("/locks/lock1/update-time", c.Path));
        Assert.Contains("\"duration\":600", _http.Seen[0].Body);
        Assert.Contains("\"duration\":15", _http.Seen[1].Body);
    }

    [Fact]
    public async Task A_credit_or_an_empty_tab_sends_nothing()
    {
        using var service = Make();

        Assert.Equal(SettleOutcome.Nothing, await service.SettleAsync());
        Assert.Empty(_http.Seen);
    }

    [Fact]
    public async Task With_no_lock_chosen_the_only_active_lock_is_used_and_two_are_never_guessed()
    {
        _options = _options with { LockId = null };
        _http.Answer = p => p == "/locks" ? Json(200, "[{\"_id\":\"solo9\",\"role\":\"wearer\"}]") : new HttpResponseMessage(HttpStatusCode.NoContent);
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.Pushed, await service.SettleAsync());
        Assert.Contains(_http.Seen, s => s.Path == "/locks/solo9/update-time");

        _utc = _utc.AddDays(1);
        _http.Answer = p => p == "/locks" ? Json(200, "[{\"_id\":\"a1\",\"role\":\"wearer\"},{\"_id\":\"b2\",\"role\":\"wearer\"}]") : new HttpResponseMessage(HttpStatusCode.NoContent);
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.NoLockChosen, await service.SettleAsync());
        Assert.Equal(300, service.BalanceSeconds);
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
        Assert.Single(_http.Seen);
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

        Assert.Contains("\"duration\":10800", _http.Seen.Single().Body);
        Assert.Equal(0, service.BalanceSeconds);
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

        Assert.Equal(new[] { "/chaster/refresh", "/locks/lock1/update-time" }, _http.Seen.Select(s => s.Path));
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
        Assert.Equal(15, service.BalanceSeconds);
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
    public async Task The_only_active_lock_is_taken_when_none_was_picked()
    {
        _options = _options with { LockId = null };
        AnswerLocks("[{\"_id\":\"solo9\",\"role\":\"wearer\"}]");
        using var service = Make();

        var snapshot = await service.RefreshLockAsync();

        Assert.Equal(LockLookup.Chosen, service.LockLookup);
        Assert.Equal("solo9", snapshot!.Id);
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
