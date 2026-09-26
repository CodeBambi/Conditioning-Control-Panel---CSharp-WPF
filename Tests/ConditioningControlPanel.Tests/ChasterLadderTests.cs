using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaster;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The heads-up clock and the raffle wire: CCP-added time is counted gross (added only, nothing
/// taken off lowers it), per UTC month and lifetime; the raffle's numbers are the server's reading,
/// asked for at most every 15 minutes, only while linked with a lock picked; the wire degrades to null.
/// </summary>
public class ChasterLadderTests : IDisposable
{
    private static readonly DateTime Utc = new(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Local = new(2026, 9, 26, 12, 0, 0);

    // ---- the counter ----

    [Fact]
    public void A_landed_add_counts_lifetime_and_month()
    {
        var s = new TabState { BalanceSeconds = 900 };
        CircesTab.ApplyPush(s, new TabPush(TabPushKind.Add, 600), Local, Utc);

        Assert.Equal(600, s.AddedTotalSeconds);
        Assert.Equal("2026-09", s.AddedMonth);
        Assert.Equal(600, ChasterLadder.ThisMonth(s, Utc));
        Assert.Equal(600, ChasterLadder.Lifetime(s));
    }

    [Fact]
    public void Removals_credits_and_the_wipe_never_lower_it()
    {
        var s = new TabState { BalanceSeconds = 900 };
        CircesTab.ApplyPush(s, new TabPush(TabPushKind.Add, 900), Local, Utc);
        s.BalanceSeconds = -300;
        CircesTab.ApplyPush(s, new TabPush(TabPushKind.Remove, 300), Local, Utc);
        s.BalanceSeconds = 100;
        CircesTab.Wipe(s, Utc, Utc);

        Assert.Equal(600, s.PushedNetSeconds);
        Assert.Equal(900, s.AddedTotalSeconds);
        Assert.Equal(900, ChasterLadder.ThisMonth(s, Utc));
    }

    [Fact]
    public void An_unanswered_add_counted_as_landed_is_counted_as_added()
    {
        var s = new TabState { BalanceSeconds = 900 };
        CircesTab.MarkPending(s, new TabPush(TabPushKind.Add, 900), Local);
        CircesTab.ResolvePending(s, Utc);

        Assert.Equal(900, s.AddedTotalSeconds);
        Assert.Equal(900, ChasterLadder.ThisMonth(s, Utc));
    }

    [Fact]
    public void A_failed_or_empty_push_counts_nothing()
    {
        var s = new TabState();
        CircesTab.ApplyPush(s, new TabPush(TabPushKind.None, 0), Local, Utc);
        CircesTab.ApplyPush(s, new TabPush(TabPushKind.Add, 0), Local, Utc);
        Assert.Equal(0, s.AddedTotalSeconds);
        Assert.Equal(0, CircesTab.ResolvePending(s, Utc));
        Assert.Equal(0, s.AddedTotalSeconds);
    }

    [Fact]
    public void The_month_turns_over_on_the_utc_month()
    {
        var s = new TabState();
        ChasterLadder.NoteAdded(s, 600, new DateTime(2026, 9, 30, 23, 59, 0, DateTimeKind.Utc));
        var october = new DateTime(2026, 10, 1, 0, 1, 0, DateTimeKind.Utc);

        Assert.Equal(0, ChasterLadder.ThisMonth(s, october));
        ChasterLadder.NoteAdded(s, 60, october);
        Assert.Equal(60, ChasterLadder.ThisMonth(s, october));
        Assert.Equal(660, s.AddedTotalSeconds);
    }

    [Fact]
    public void A_tab_from_before_the_counter_starts_its_lifetime_at_what_it_pushed()
    {
        var s = new TabState { PushedNetSeconds = 7200 };
        Assert.Equal(7200, ChasterLadder.Lifetime(s));
        ChasterLadder.NoteAdded(s, 600, Utc);
        Assert.Equal(7200, ChasterLadder.Lifetime(s));
        s.AddedTotalSeconds = 9000;
        Assert.Equal(9000, ChasterLadder.Lifetime(s));
    }

    [Fact]
    public void Negative_and_zero_adds_are_ignored()
    {
        var s = new TabState();
        ChasterLadder.NoteAdded(s, -600, Utc);
        ChasterLadder.NoteAdded(s, 0, Utc);
        Assert.Equal(0, s.AddedTotalSeconds);
        Assert.Null(s.AddedMonth);
    }

    [Fact]
    public void The_counter_round_trips_through_the_tab_file()
    {
        var s = new TabState();
        ChasterLadder.NoteAdded(s, 600, Utc);
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<TabState>(Newtonsoft.Json.JsonConvert.SerializeObject(s))!;
        Assert.Equal(600, back.AddedTotalSeconds);
        Assert.Equal("2026-09", back.AddedMonth);
        Assert.Equal(600, back.AddedMonthSeconds);
    }

    // ---- the clock, the throttle, the board ----

    [Theory]
    [InlineData(0, "0:00:00")]
    [InlineData(59, "0:00:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(453849, "126:04:09")]
    [InlineData(-5, "0:00:00")]
    public void The_clock_reads_like_a_stopwatch(long seconds, string expected) =>
        Assert.Equal(expected, ChasterLadder.FormatClock(seconds));

    [Fact]
    public void Verify_goes_at_most_every_fifteen_minutes()
    {
        Assert.True(ChasterLadder.VerifyDue(null, Utc));
        Assert.False(ChasterLadder.VerifyDue(Utc, Utc.AddMinutes(14)));
        Assert.True(ChasterLadder.VerifyDue(Utc, Utc.AddMinutes(15)));
        Assert.Equal(Utc.AddMinutes(15), ChasterLadder.VerifyAt(Utc, Utc.AddMinutes(1)));
        Assert.Null(ChasterLadder.VerifyAt(Utc, Utc.AddMinutes(20)));
    }

    [Fact]
    public void The_card_parses_and_clamps()
    {
        var o = new JObject
        {
            ["ok"] = true, ["month"] = "2026-10", ["days_in_month"] = 31, ["today"] = 12,
            ["days"] = new JArray(3, 1, 1, 0, 40, "x", 2), ["total_seconds"] = 5000, ["need_days"] = 25, ["need_seconds"] = 111600,
            ["post_days"] = true, ["tickets_frozen"] = false, ["ticket"] = null,
        };
        var card = ChasterLadderApi.ParseCard(o)!;

        Assert.Equal(new[] { 1, 2, 3 }, card.Days);
        Assert.Equal(12, card.Today);
        Assert.Equal(5000, card.TotalSeconds);
        Assert.True(card.PostDays);
        Assert.Null(card.Ticket);
        Assert.Equal(25, card.NeedDays);
    }

    [Fact]
    public void A_bad_card_reply_is_no_card()
    {
        Assert.Null(ChasterLadderApi.ParseCard(null));
        Assert.Null(ChasterLadderApi.ParseCard(new JObject { ["ok"] = false, ["reason"] = "too_fast" }));
        Assert.Null(ChasterLadderApi.ParseCard(new JObject { ["ok"] = true, ["month"] = "junk", ["days_in_month"] = 31 }));
        Assert.Null(ChasterLadderApi.ParseCard(new JObject { ["ok"] = true, ["month"] = "2026-10", ["days_in_month"] = 99 }));
        var odd = ChasterLadderApi.ParseCard(new JObject { ["ok"] = true, ["month"] = "2026-10", ["days_in_month"] = 31, ["today"] = 500, ["total_seconds"] = -9, ["ticket"] = 0 })!;
        Assert.Equal(32, odd.Today);
        Assert.Equal(0, odd.TotalSeconds);
        Assert.Null(odd.Ticket);
        Assert.Equal(ChasterRaffle.DefaultNeedDays, odd.NeedDays);
        Assert.Equal(ChasterRaffle.DefaultNeedSeconds, odd.NeedSeconds);
    }

    // ---- the pinned top ten ----

    [Fact]
    public void The_board_parses_and_keeps_ten()
    {
        var rows = new JArray();
        for (var i = 1; i <= 12; i++) rows.Add(new JObject { ["rank"] = i, ["name"] = $"Locked {i:X6}", ["named"] = false, ["added_seconds"] = 900 * (13 - i), ["you"] = false });
        var o = new JObject { ["ok"] = true, ["month"] = "2026-10", ["show_name"] = true, ["rows"] = rows, ["you"] = new JObject { ["rank"] = 17, ["name"] = "Me", ["named"] = true, ["added_seconds"] = 61 } };
        var board = ChasterLadderApi.ParseBoard(o)!;

        Assert.Equal(10, board.Rows.Count);
        Assert.Equal("2026-10", board.Month);
        Assert.True(board.ShowName);
        Assert.Equal(17, board.You!.Rank);
        Assert.True(board.You.You);
        Assert.Same(board.You, ChasterLadder.OwnRowBelow(board));
    }

    [Fact]
    public void The_own_row_is_not_repeated_when_it_is_in_the_ten()
    {
        var mine = new LadderRow(2, "Me", true, 60, true);
        var board = new LadderBoard("2026-10", new[] { new LadderRow(1, "Locked A1B2C3", false, 900, false), mine }, mine, true);
        Assert.Null(ChasterLadder.OwnRowBelow(board));
    }

    [Fact]
    public void An_unranked_player_has_no_own_row()
    {
        var board = new LadderBoard("2026-10", new[] { new LadderRow(1, "Locked A1B2C3", false, 900, false) }, null, false);
        Assert.Null(ChasterLadder.OwnRowBelow(board));
    }

    [Fact]
    public void A_bad_reply_is_no_board_and_junk_rows_are_dropped()
    {
        Assert.Null(ChasterLadderApi.ParseBoard(null));
        Assert.Null(ChasterLadderApi.ParseBoard(new JObject { ["ok"] = false, ["reason"] = "too_fast" }));
        var o = new JObject { ["ok"] = true, ["rows"] = new JArray(new JObject { ["rank"] = 0, ["name"] = "x" }, new JObject { ["rank"] = 1, ["name"] = "" }, new JObject { ["rank"] = 2, ["name"] = new string('n', 60), ["added_seconds"] = -5 }) };
        var board = ChasterLadderApi.ParseBoard(o)!;
        var row = Assert.Single(board.Rows);
        Assert.Equal(32, row.Name.Length);
        Assert.Equal(0, row.AddedSeconds);
        Assert.Null(board.You);
    }

    [Fact]
    public void The_demo_board_is_ten_mixed_rows_and_the_player_at_twenty_three()
    {
        var off = ChasterService.DemoRaffle.Board(false);
        Assert.Equal(10, off.Rows.Count);
        Assert.Contains(off.Rows, r => r.Named);
        Assert.Contains(off.Rows, r => !r.Named);
        var mine = ChasterLadder.OwnRowBelow(off)!;
        Assert.Equal(23, mine.Rank);
        Assert.False(mine.Named);
        var on = ChasterLadder.OwnRowBelow(ChasterService.DemoRaffle.Board(true))!;
        Assert.Equal("You", on.Name);
        Assert.True(on.Named);
    }

    [Fact]
    public void A_verify_reply_is_worded()
    {
        Assert.Null(ChasterLadderApi.ParseVerify(null));
        Assert.Equal(new LadderVerify(true, 3600, null, 12), ChasterLadderApi.ParseVerify(new JObject { ["ok"] = true, ["added_seconds"] = 3600, ["days_counted"] = 12 }));
        Assert.Equal("test_lock", ChasterLadderApi.ParseVerify(new JObject { ["ok"] = false, ["reason"] = "test_lock" })!.Reason);
    }

    // ---- the wire ----

    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<(string Path, string? Token, string Body)> Seen = new();
        public Func<string, HttpResponseMessage> Answer = _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>404</html>") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct);
            r.Headers.TryGetValues("X-Auth-Token", out var t);
            Seen.Add((r.RequestUri!.AbsolutePath, t == null ? null : string.Join(",", t), body));
            return Answer(r.RequestUri!.AbsolutePath);
        }
    }

    [Fact]
    public async Task A_proxy_without_the_routes_is_simply_no_raffle()
    {
        var h = new Handler();
        var api = new ChasterLadderApi(new HttpClient(h), () => ("u_test0001", "tok"), "https://proxy.test");

        Assert.Null(await api.MeAsync());
        Assert.Null(await api.VerifyAsync("lock1", "AT"));
        Assert.False(await api.OptInAsync(true));
        Assert.All(h.Seen, s => Assert.Equal("tok", s.Token));
        Assert.Equal("/chaster/raffle/me", h.Seen[0].Path);
        Assert.Equal("/chaster/raffle/verify", h.Seen[1].Path);
        Assert.Equal("/chaster/raffle/optin", h.Seen[2].Path);
        Assert.Contains("\"post_days\":true", h.Seen[2].Body);
        Assert.Contains("\"lock_id\":\"lock1\"", h.Seen[1].Body);
        Assert.Contains("\"unified_id\":\"u_test0001\"", h.Seen[1].Body);
    }

    [Fact]
    public async Task No_account_sends_nothing()
    {
        var h = new Handler();
        var api = new ChasterLadderApi(new HttpClient(h), () => null, "https://proxy.test");
        Assert.Null(await api.MeAsync());
        Assert.Empty(h.Seen);
    }

    // ---- the service ----

    private sealed class Store : IChasterTokenStore
    {
        public ChasterStoredTokens? Tokens;
        public ChasterStoredTokens? Read() => Tokens;
        public void Write(ChasterStoredTokens tokens) => Tokens = tokens;
        public void Clear() => Tokens = null;
    }

    private sealed class FakeLadder : IChasterLadderApi
    {
        public readonly List<(string LockId, string Token)> Verifies = new();
        public readonly List<bool> OptIns = new();
        public bool ServerPostDays;
        public int Mes;
        public Task<LadderVerify?> VerifyAsync(string lockId, string accessToken, CancellationToken ct = default)
        {
            Verifies.Add((lockId, accessToken));
            return Task.FromResult<LadderVerify?>(new LadderVerify(true, 42, null));
        }
        public Task<bool> OptInAsync(bool postDays, CancellationToken ct = default)
        {
            OptIns.Add(postDays);
            ServerPostDays = postDays;
            return Task.FromResult(true);
        }
        public readonly List<bool> NameOptIns = new();
        public bool ServerShowName;
        public int Tops;
        public Task<LadderBoard?> TopAsync(CancellationToken ct = default)
        {
            Tops++;
            return Task.FromResult<LadderBoard?>(new LadderBoard("2026-10", Array.Empty<LadderRow>(), null, ServerShowName));
        }
        public Task<bool> ShowNameAsync(bool showName, CancellationToken ct = default)
        {
            NameOptIns.Add(showName);
            ServerShowName = showName;
            return Task.FromResult(true);
        }
        public Task<RaffleCard?> MeAsync(CancellationToken ct = default)
        {
            Mes++;
            return Task.FromResult<RaffleCard?>(new RaffleCard("2026-10", 31, 12, new[] { 1, 2 }, 900, 25, 111600, ServerPostDays, false, null));
        }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-ladder-" + Guid.NewGuid().ToString("N"));
    private readonly Store _store = new();
    private DateTime _utc = Utc;
    private ChasterOptions _options = new(true, "lock1", new HashSet<string> { "typo" });
    private bool _postDays;
    private bool _showName;

    public ChasterLadderTests()
    {
        Directory.CreateDirectory(_dir);
        _store.Tokens = new ChasterStoredTokens("AT", "RT", Utc.AddHours(1));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } // swallow: temp dir, best effort
    }

    private ChasterService Make(FakeLadder ladder) =>
        new(new ChasterClient(new Handler()), _store, Path.Combine(_dir, "tab.json"), () => _options, () => _utc, () => _utc.ToLocalTime())
        {
            LadderApi = ladder,
            RafflePostDays = () => _postDays,
            LadderShowName = () => _showName,
        };

    [Fact]
    public async Task Verify_sends_the_picked_lock_and_the_live_token_at_most_every_fifteen_minutes()
    {
        var ladder = new FakeLadder();
        using var service = Make(ladder);

        await service.VerifyLadderAsync(CancellationToken.None);
        await service.VerifyLadderAsync(CancellationToken.None);
        _utc = _utc.AddMinutes(16);
        _store.Tokens = new ChasterStoredTokens("AT2", "RT", _utc.AddHours(1));
        await service.VerifyLadderAsync(CancellationToken.None);

        Assert.Equal(new[] { ("lock1", "AT"), ("lock1", "AT2") }, ladder.Verifies);
        Assert.Equal(42, service.LastLadderVerify!.Seconds);
    }

    [Fact]
    public async Task Nothing_is_verified_unlinked_or_with_no_lock_picked()
    {
        var ladder = new FakeLadder();
        _options = _options with { LockId = null };
        using (var service = Make(ladder)) await service.VerifyLadderAsync(CancellationToken.None);
        _options = _options with { LockId = "lock1" };
        _store.Tokens = null;
        using (var service = Make(ladder)) await service.VerifyLadderAsync(CancellationToken.None);

        Assert.Empty(ladder.Verifies);
    }

    [Fact]
    public async Task The_card_brings_the_server_post_switch_in_line()
    {
        var ladder = new FakeLadder();
        _postDays = true;
        using var service = Make(ladder);

        var card = await service.RaffleAsync();

        Assert.Equal(new[] { true }, ladder.OptIns);
        Assert.True(card!.PostDays);
        Assert.Equal(new[] { 1, 2 }, card.Days);
        await service.RaffleAsync();
        Assert.Single(ladder.OptIns);
        Assert.Equal(2, ladder.Mes);
    }

    [Fact]
    public async Task No_raffle_api_is_no_card()
    {
        using var service = new ChasterService(new ChasterClient(new Handler()), _store, Path.Combine(_dir, "tab2.json"), () => _options, () => _utc, () => _utc.ToLocalTime());
        Assert.Null(await service.RaffleAsync());
        Assert.False(await service.SetRafflePostDaysAsync(true));
        Assert.Null(await service.LadderAsync());
        Assert.False(await service.SetLadderShowNameAsync(true));
    }

    [Fact]
    public async Task The_board_brings_the_server_name_switch_in_line_and_never_touches_the_post_switch()
    {
        var ladder = new FakeLadder();
        _showName = true;
        using var service = Make(ladder);

        var board = await service.LadderAsync();

        Assert.Equal(new[] { true }, ladder.NameOptIns);
        Assert.Empty(ladder.OptIns);
        Assert.True(board!.ShowName);
        await service.LadderAsync();
        Assert.Single(ladder.NameOptIns);
        Assert.Empty(ladder.Verifies); // the raffle read verifies, the board read does not
    }
}
