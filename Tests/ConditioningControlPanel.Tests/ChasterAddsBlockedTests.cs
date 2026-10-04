using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaster;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A keyholder can switch "add time" off for the wearer (ticket 2026-10-03). The lock's own
/// permissions say so, and update-time answers 403. Either way the tab keeps the balance, stops
/// asking, and the page names the reason instead of reading as offline.
/// </summary>
public class ChasterAddsBlockedTests : IDisposable
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
        public readonly List<string> Seen = new();
        public Func<string, HttpResponseMessage> Answer = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path == "/chaster/refresh")
                return Task.FromResult(Json(200, "{\"access_token\":\"AT\",\"expires_in\":300}"));
            Seen.Add(path);
            return Task.FromResult(Answer(path));
        }
    }

    private static HttpResponseMessage Json(int status, string body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body) };

    // The live shape (GET /locks, 2026-10-03), trimmed to the grant that matters.
    private const string Standard =
        "{\"preset\":\"standard\",\"grants\":[{\"resource\":\"lock.time.add\",\"category\":\"time\",\"labelKey\":\"x\",\"subjects\":{\"wearer\":[\"edit\"],\"keyholder\":[\"edit\"]}}]}";
    private const string WearerOff =
        "{\"preset\":\"custom\",\"grants\":[{\"resource\":\"lock.time.add\",\"category\":\"time\",\"labelKey\":\"x\",\"subjects\":{\"wearer\":[],\"keyholder\":[\"edit\"]}}]}";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-chaster-blocked-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _http = new();
    private readonly MemoryStore _store = new();
    private readonly DateTime _utc = new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);
    private ChasterOptions _options = new(true, "lock1", new HashSet<string> { "watcher" });

    public ChasterAddsBlockedTests()
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

    private static string LockBody(string? permissions) =>
        "[{\"_id\":\"lock1\",\"role\":\"wearer\",\"endDate\":\"2026-10-31T00:00:00Z\""
        + (permissions == null ? "" : ",\"permissions\":" + permissions) + "}]";

    private void Serve(string? permissions, int updateStatus = 204) =>
        _http.Answer = p => p == "/locks" ? Json(200, LockBody(permissions))
            : p.EndsWith("update-time") ? new HttpResponseMessage((HttpStatusCode)updateStatus)
            : new HttpResponseMessage(HttpStatusCode.NoContent);

    private int Pushes => _http.Seen.Count(p => p.EndsWith("update-time"));

    [Theory]
    [InlineData(Standard, true)]
    [InlineData(WearerOff, false)]
    [InlineData("{\"preset\":\"custom\",\"grants\":[{\"resource\":\"lock.time.add\",\"subjects\":{\"wearer\":[\"view\"]}}]}", false)]
    [InlineData("{\"preset\":\"custom\",\"grants\":[{\"resource\":\"lock.time.add\",\"subjects\":{\"keyholder\":[\"edit\"]}}]}", null)]
    [InlineData("{\"preset\":\"standard\",\"grants\":[{\"resource\":\"lock.freeze\",\"subjects\":{\"wearer\":[]}}]}", null)]
    [InlineData("{\"preset\":\"standard\"}", null)]
    [InlineData("{\"grants\":\"nope\"}", null)]
    [InlineData("[1,2]", null)]
    [InlineData("null", null)]
    public void The_time_add_grant_says_whether_the_wearer_may_add(string permissions, bool? expected)
    {
        Assert.Equal(expected, ChasterLock.WearerCanAddTime(JToken.Parse(permissions)));
    }

    [Fact]
    public void A_lock_without_permissions_or_with_odd_ones_still_reads()
    {
        var locks = ChasterClient.ParseLocks("[" + LockBody(WearerOff).Trim('[', ']') + ",{\"_id\":\"lock2\",\"permissions\":7},{\"_id\":\"lock3\"}]");

        Assert.Equal(new[] { "lock1", "lock2", "lock3" }, locks.Select(l => l.Id));
        Assert.False(locks[0].WearerMayAddTime);
        Assert.Null(locks[1].WearerMayAddTime);
        Assert.Null(locks[2].WearerMayAddTime);
    }

    [Fact]
    public async Task A_lock_that_says_no_is_never_asked_and_the_tab_keeps_the_time()
    {
        Serve(WearerOff);
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.AddsBlocked, await service.SettleAsync());
        Assert.Equal(0, Pushes);
        Assert.Equal(300, service.BalanceSeconds);
        Assert.True(service.AddsBlocked);
        Assert.True(service.IsLinked);
    }

    [Fact]
    public async Task A_403_on_the_add_blocks_that_lock_without_a_retry_storm()
    {
        Serve(null, updateStatus: 403);
        using var service = Make();
        service.NoteSeconds("watcher", 300);

        Assert.Equal(SettleOutcome.AddsBlocked, await service.SettleAsync());
        Assert.Equal(SettleOutcome.AddsBlocked, await service.SettleAsync());
        Assert.Equal(SettleOutcome.AddsBlocked, await service.SettleAsync());

        Assert.Equal(1, Pushes);
        Assert.Equal(300, service.BalanceSeconds);
        Assert.True(service.AddsBlocked);
        Assert.True(service.IsLinked);
        Assert.Equal(LockLookup.Unlinked, service.LockLookup); // nothing said "offline"
    }

    [Fact]
    public async Task The_keyholder_turning_adds_back_on_lets_the_tab_land()
    {
        Serve(WearerOff);
        using var service = Make();
        service.NoteSeconds("watcher", 300);
        Assert.Equal(SettleOutcome.AddsBlocked, await service.SettleAsync());

        Serve(Standard);
        Assert.Equal(SettleOutcome.Pushed, await service.SettleAsync());
        Assert.False(service.AddsBlocked);
        Assert.Equal(1, Pushes);
        Assert.Equal(0, service.BalanceSeconds);
    }

    [Fact]
    public async Task The_block_belongs_to_one_lock_and_shows_on_the_lock_refresh()
    {
        Serve(WearerOff);
        using var service = Make();
        var changes = 0;
        service.LockChanged += () => changes++;

        await service.RefreshLockAsync();
        Assert.Equal(LockLookup.Chosen, service.LockLookup);
        Assert.True(service.AddsBlocked);
        Assert.True(changes > 0);

        _options = _options with { LockId = "lock2" };
        Assert.False(service.AddsBlocked);
    }

    [Fact]
    public void The_page_names_the_block_in_the_hint_and_on_the_tag()
    {
        Assert.Equal("chaster_state_keyholder_blocked",
            TabPageText.SetupHint(linked: true, LockLookup.Chosen, hasLock: true, enabled: true, addsBlocked: true));
        Assert.Null(TabPageText.SetupHint(linked: true, LockLookup.Chosen, hasLock: true, enabled: true, addsBlocked: false));
        Assert.Equal("chaster_state_away",
            TabPageText.SetupHint(linked: true, LockLookup.Away, hasLock: true, enabled: true, addsBlocked: true));

        Assert.Equal("chaster_tag_blocked", TabPageText.Tag(300, 300, paused: false, lockPicked: true, addsBlocked: true).Key);
        Assert.Equal("chaster_tag_lands", TabPageText.Tag(300, 300, paused: false, lockPicked: true).Key);
    }
}
