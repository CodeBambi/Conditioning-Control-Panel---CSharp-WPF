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
    public async Task A_settle_adds_the_balance_to_the_chosen_lock_once_a_day()
    {
        using var service = Make();
        service.NoteSeconds("watcher", 600);

        var first = await service.SettleAsync();
        service.Note("typo");
        var again = await service.SettleAsync();

        Assert.Equal(SettleOutcome.Pushed, first);
        Assert.Equal(SettleOutcome.Nothing, again);
        Assert.Equal(15, service.BalanceSeconds);
        Assert.Equal(600, service.Bill().PushedSeconds);
        var call = Assert.Single(_http.Seen);
        Assert.Equal("/locks/lock1/update-time", call.Path);
        Assert.Contains("\"duration\":600", call.Body);
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
    public async Task A_backlog_lands_an_hour_at_a_time()
    {
        using var service = Make();
        for (var day = 0; day < 3; day++)
        {
            service.NoteSeconds("watcher", 3600);
            _utc = _utc.AddDays(1);
        }

        await service.SettleAsync();

        Assert.Contains("\"duration\":3600", _http.Seen.Single().Body);
        Assert.Equal(7200, service.BalanceSeconds);
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
}
