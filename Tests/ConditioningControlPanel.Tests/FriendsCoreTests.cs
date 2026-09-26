using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The friends core: the poll cadence, the service over a fake wire, and the wire's wording.</summary>
public class FriendsPollRuleTests
{
    [Theory]
    [InlineData(false, true, true, true, 0)]
    [InlineData(true, true, false, false, 20)]
    [InlineData(true, false, true, true, 20)]
    [InlineData(true, false, true, false, 120)]
    [InlineData(true, false, false, true, 120)]
    [InlineData(true, false, false, false, 120)]
    public void Cadence(bool signedIn, bool drawer, bool online, bool fg, int expected)
        => Assert.Equal(expected, FriendsPollRule.NextIntervalSeconds(signedIn, drawer, online, fg));

    [Fact]
    public void StateRidesEveryFifthPollAndOnOpen()
    {
        var hits = Enumerable.Range(0, 11).Where(i => FriendsPollRule.FetchState(i, false)).ToArray();
        Assert.Equal(new[] { 0, 5, 10 }, hits);
        Assert.True(FriendsPollRule.FetchState(3, true));
    }
}

public class FriendsServiceTests
{
    private sealed class FakeApi : IFriendsApi
    {
        public FriendsSnapshot? State = FriendsSnapshot.Empty;
        public Queue<FriendsPollReply?> Polls = new();
        public int StateCalls;
        public List<(PresenceActivity? Activity, int? LockDay, bool Shared)> PollCalls = new();
        public List<(string To, SendKind Kind)> Sends = new();
        public SendResult SendAnswer = SendResult.Sent;
        public List<(string Op, string Id)> Acts = new();

        public Task<FriendsSnapshot?> StateAsync(CancellationToken ct = default) { StateCalls++; return Task.FromResult(State); }

        public Task<FriendsPollReply?> PollAsync(PresenceActivity? activity, int? lockDay, bool shared, CancellationToken ct = default)
        {
            PollCalls.Add((activity, lockDay, shared));
            return Task.FromResult(Polls.Count > 0 ? Polls.Dequeue() : new FriendsPollReply(Array.Empty<string>(), Array.Empty<InboxItem>()));
        }

        public Task<AddResult> RequestAsync(string code, CancellationToken ct = default) => Task.FromResult(AddResult.Sent);

        public Task<SendResult> SendAsync(string to, SendKind kind, string? poke, string? destination, string? code, WatchRef? watch,
            CancellationToken ct = default)
        {
            Sends.Add((to, kind));
            return Task.FromResult(SendAnswer);
        }

        public Task<bool> ActAsync(string op, string id, JObject? extra = null, CancellationToken ct = default)
        {
            Acts.Add((op, id));
            return Task.FromResult(true);
        }
    }

    private static readonly DateTimeOffset T0 = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private static Friend F(string id, bool online = false) =>
        new(id, "name " + id, null, 0, online, new FriendPresence(online ? PresenceActivity.Panel : PresenceActivity.Offline, null, T0), false);

    private static FriendsSnapshot Snap(params Friend[] friends) =>
        new(friends.ToList(), new List<FriendRequest>(), new List<FriendRequest>(), "CCP-ABCDE", FriendPresence.None);

    private static InboxItem Poke(string id, DateTimeOffset at, DateTimeOffset? expires = null) =>
        new(id, SendKind.Poke, "u_a", "A", null, "hi", null, null, null, at, expires ?? at.AddHours(24));

    private static (FriendsService Svc, FakeApi Api, Func<bool> Shared) Make(bool shared = false, string? account = "u_me")
    {
        var api = new FakeApi();
        bool s = shared;
        var svc = new FriendsService(api, () => account, () => true, () => T0, () => s, v => s = v);
        return (svc, api, () => s);
    }

    [Fact]
    public async Task DeliversEachItemOnceOldestFirstAndDropsExpired()
    {
        var (svc, api, _) = Make();
        var got = new List<string>();
        svc.Delivered += i => got.Add(i.Id);
        api.Polls.Enqueue(new FriendsPollReply(Array.Empty<string>(), new[]
        {
            Poke("b", T0.AddSeconds(-5)),
            Poke("a", T0.AddSeconds(-10)),
            Poke("old", T0.AddHours(-25), T0.AddHours(-1)),
        }));
        api.Polls.Enqueue(new FriendsPollReply(Array.Empty<string>(), new[] { Poke("b", T0.AddSeconds(-5)), Poke("c", T0) }));
        await svc.TickAsync();
        await svc.TickAsync();
        Assert.Equal(new[] { "a", "b", "c" }, got);
    }

    [Fact]
    public async Task RequestsAlreadyWaitingAtStartStaySilentNewOnesRaiseOnce()
    {
        var (svc, api, _) = Make();
        var arrived = new List<string>();
        var gone = new List<string>();
        svc.RequestArrived += r => arrived.Add(r.Id);
        svc.RequestGone += id => gone.Add(id);
        FriendsSnapshot WithIncoming(params string[] ids) => Snap() with
        {
            Incoming = ids.Select(i => new FriendRequest(i, "n", null, null, T0)).ToList(),
        };

        api.State = WithIncoming("old");
        await svc.RefreshAsync();
        Assert.Empty(arrived);

        api.State = WithIncoming("old", "new");
        await svc.RefreshAsync();
        await svc.RefreshAsync();
        Assert.Equal(new[] { "new" }, arrived);

        api.State = WithIncoming("new");
        await svc.RefreshAsync();
        Assert.Equal(new[] { "old" }, gone);
    }

    [Fact]
    public async Task SnapshotChangedOnlyOnARealChange()
    {
        var (svc, api, _) = Make();
        int changes = 0;
        svc.SnapshotChanged += _ => changes++;
        api.State = Snap(F("u_1"), F("u_2"));
        await svc.RefreshAsync();
        Assert.Equal(1, changes);

        api.State = Snap(F("u_1"), F("u_2"));   // same contents, new lists
        await svc.RefreshAsync();
        Assert.Equal(1, changes);

        api.State = Snap(F("u_1", online: true), F("u_2"));
        await svc.RefreshAsync();
        Assert.Equal(2, changes);
        Assert.Equal(1, svc.Snapshot.OnlineCount);
    }

    [Fact]
    public async Task PollOnlineListFlipsTheRows()
    {
        var (svc, api, _) = Make();
        api.State = Snap(F("u_1"), F("u_2"));
        await svc.RefreshAsync();
        int changes = 0;
        svc.SnapshotChanged += _ => changes++;
        api.Polls.Enqueue(new FriendsPollReply(new[] { "u_2" }, Array.Empty<InboxItem>()));
        api.Polls.Enqueue(new FriendsPollReply(new[] { "u_2" }, Array.Empty<InboxItem>()));
        api.State = null;   // state fails on poll 0; the online list still lands
        await svc.TickAsync();
        await svc.TickAsync();
        Assert.Equal(1, changes);
        Assert.True(svc.Snapshot.Friends.Single(f => f.Id == "u_2").Online);
        Assert.Equal(FriendsPollRule.FastSeconds, svc.NextIntervalSeconds());
    }

    [Fact]
    public async Task PresenceIsSentOnlyWhileShared()
    {
        var (svc, api, shared) = Make(shared: false);
        svc.SetActivity(PresenceActivity.BackRoom);
        await svc.TickAsync();
        Assert.Equal(((PresenceActivity?)null, (int?)null, false), api.PollCalls.Last());

        svc.PresenceShared = true;
        Assert.True(shared());
        await svc.TickAsync();
        Assert.Equal(((PresenceActivity?)PresenceActivity.BackRoom, (int?)null, true), api.PollCalls.Last());
    }

    [Fact]
    public async Task SignedOutMakesNoRequest()
    {
        var (svc, api, _) = Make(account: null);
        await svc.TickAsync();
        await svc.RefreshAsync();
        Assert.Empty(api.PollCalls);
        Assert.Equal(0, api.StateCalls);
        Assert.False(svc.Available);
        Assert.Equal(0, svc.NextIntervalSeconds());
    }

    [Fact]
    public async Task SendsCheckGrammarAndRaiseSentOnlyOnSent()
    {
        var (svc, api, _) = Make();
        api.State = Snap(F("u_1", online: true));
        await svc.RefreshAsync();
        var sent = new List<(SendKind, string)>();
        svc.Sent += (k, f) => sent.Add((k, f.Name));

        Assert.Equal(SendResult.Refused, await svc.PokeAsync("u_1", "nope"));
        Assert.Equal(SendResult.Refused, await svc.InviteAsync("u_1", "goon", null));
        Assert.Equal(SendResult.Refused, await svc.SendWatchAsync("u_1", new WatchRef(WatchKind.Ht, "12a", null)));
        Assert.Empty(api.Sends);

        Assert.Equal(SendResult.Sent, await svc.PokeAsync("u_1", "hi"));
        Assert.Equal(SendResult.TooFast, await svc.PokeAsync("u_1", "wp"));   // client mirror of the 10 s pair cooldown
        Assert.Single(api.Sends);

        api.SendAnswer = SendResult.Offline;
        Assert.Equal(SendResult.Offline, await svc.InviteAsync("u_1", "backroom", "IGNORED"));
        Assert.Equal(new[] { (SendKind.Poke, "name u_1") }, sent);
    }

    [Fact]
    public void WatchTitleIsTrimmedNotRefused()
    {
        var w = FriendsService.CleanTitle(new WatchRef(WatchKind.Flavour, "pink", "Pink! <b>" + new string('x', 60)));
        Assert.NotNull(w.Title);
        Assert.True(w.Title!.Length <= 40);
        Assert.DoesNotContain('<', w.Title);
        Assert.Null(FriendsService.CleanTitle(new WatchRef(WatchKind.Flavour, "pink", "!!!")).Title);
    }

    [Fact]
    public async Task AddByCodeNormalisesAndRefusesBadCodesLocally()
    {
        var (svc, api, _) = Make();
        Assert.Equal(AddResult.NotFound, await svc.AddByCodeAsync("CCP-0O1I!"));
        Assert.Equal(AddResult.Sent, await svc.AddByCodeAsync("abcde"));
        Assert.Equal("CCP-ABCDE", FriendsApi.NormaliseCode(" ccp-abcde "));
        Assert.Null(FriendsApi.NormaliseCode("CCP-ABCD"));
    }
}

public class FriendsApiTests
{
    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;
        public string? LastBody;
        public string? LastUri;
        public string? LastToken;
        public Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) => _answer = answer;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri!.ToString();
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            LastToken = request.Headers.TryGetValues("X-Auth-Token", out var v) ? v.First() : null;
            return _answer(request);
        }
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string body, string type = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, type) };

    private static (FriendsApi Api, Handler H) Make(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var h = new Handler(answer);
        return (new FriendsApi(new HttpClient(h), () => ("u_me", "tok"), "http://127.0.0.1:1"), h);
    }

    [Theory]
    [InlineData(200, "{\"ok\":true,\"status\":\"sent\"}", SendResult.Sent)]
    [InlineData(200, "{\"ok\":true,\"status\":\"offline\"}", SendResult.Offline)]
    [InlineData(200, "{\"ok\":false,\"reason\":\"not_friends\"}", SendResult.NotFriends)]
    [InlineData(200, "{\"ok\":false,\"reason\":\"refused\"}", SendResult.Refused)]
    [InlineData(200, "{\"ok\":false,\"reason\":\"bad_input\"}", SendResult.Refused)]
    [InlineData(429, "{\"ok\":false,\"reason\":\"too_fast\"}", SendResult.TooFast)]
    [InlineData(429, "", SendResult.TooFast)]
    [InlineData(404, "<html>not here</html>", SendResult.TryLater)]
    [InlineData(502, "<html>bad gateway</html>", SendResult.TryLater)]
    [InlineData(401, "{\"ok\":false,\"reason\":\"auth\"}", SendResult.TryLater)]
    [InlineData(200, "{\"ok\":true,\"status\":\"mystery\"}", SendResult.TryLater)]
    public async Task SendWording(int status, string body, SendResult expected)
    {
        var (api, h) = Make(_ => Reply((HttpStatusCode)status, body));
        Assert.Equal(expected, await api.SendAsync("u_1", SendKind.Poke, "hi", null, null, null));
        var sent = JObject.Parse(h.LastBody!);
        Assert.Equal("u_me", (string?)sent["unified_id"]);
        Assert.Equal("poke", (string?)sent["kind"]);
        Assert.Equal("tok", h.LastToken);
        Assert.EndsWith("/v2/friends/send", h.LastUri);
    }

    [Theory]
    [InlineData("{\"ok\":true,\"status\":\"accepted\"}", AddResult.Accepted)]
    [InlineData("{\"ok\":true,\"status\":\"not_found\"}", AddResult.NotFound)]
    [InlineData("{\"ok\":true,\"status\":\"full\"}", AddResult.Full)]
    [InlineData("{\"ok\":false,\"reason\":\"bad_input\"}", AddResult.NotFound)]
    [InlineData("{\"ok\":false,\"reason\":\"busy\"}", AddResult.TryLater)]
    [InlineData("<html></html>", AddResult.TryLater)]
    public async Task RequestWording(string body, AddResult expected)
    {
        var (api, _) = Make(_ => Reply(HttpStatusCode.OK, body));
        Assert.Equal(expected, await api.RequestAsync("CCP-ABCDE"));
    }

    [Fact]
    public async Task NetworkFaultIsTryLaterNotAThrow()
    {
        var (api, _) = Make(_ => throw new HttpRequestException("socket"));
        Assert.Equal(SendResult.TryLater, await api.SendAsync("u_1", SendKind.Poke, "hi", null, null, null));
        Assert.Null(await api.StateAsync());
        Assert.Null(await api.PollAsync(null, null, false));
        Assert.False(await api.ActAsync("remove", "u_1"));
    }

    [Fact]
    public async Task NoAccountSendsNothing()
    {
        int calls = 0;
        var api = new FriendsApi(new HttpClient(new Handler(_ => { calls++; return Reply(HttpStatusCode.OK, "{}"); })), () => null, "http://127.0.0.1:1");
        Assert.Equal(SendResult.TryLater, await api.SendAsync("u_1", SendKind.Poke, "hi", null, null, null));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task StateParses()
    {
        const string body = "{\"ok\":true,\"code\":\"CCP-ABCDE\",\"me\":{\"activity\":\"panel\",\"lock_day\":3,\"shared\":true}," +
            "\"friends\":[{\"id\":\"u_1\",\"name\":\"Ann\",\"avatar\":\"/v2/friends/avatar/u_1\",\"tier\":2,\"online\":true," +
            "\"activity\":\"goon_hosting\",\"lock_day\":null,\"last_seen\":\"2026-09-23T10:00:00Z\",\"squelched\":false}," +
            "{\"id\":\"u_2\",\"name\":\"Bo\",\"avatar\":null,\"tier\":9,\"online\":false,\"activity\":\"race\",\"lock_day\":12," +
            "\"last_seen\":\"2026-09-22T10:00:00Z\",\"squelched\":true}]," +
            "\"incoming\":[{\"id\":\"u_3\",\"name\":\"Cy\",\"avatar\":null,\"via\":\"Ann\",\"at\":\"2026-09-23T09:00:00Z\"}],\"outgoing\":[]}";
        var (api, _) = Make(_ => Reply(HttpStatusCode.OK, body));
        var s = await api.StateAsync();
        Assert.NotNull(s);
        Assert.Equal("CCP-ABCDE", s!.MyCode);
        Assert.Equal(3, s.Me.LockDay);
        Assert.Equal(PresenceActivity.GoonHosting, s.Friends[0].Presence.Activity);
        Assert.Equal(PresenceActivity.Offline, s.Friends[1].Presence.Activity);
        Assert.Equal(2, s.Friends[1].Tier);
        Assert.True(s.Friends[1].Squelched);
        Assert.Equal(12, s.Friends[1].Presence.LockDay);
        Assert.Equal("Ann", s.Incoming.Single().Via);
        Assert.Equal(1, s.OnlineCount);
    }

    [Fact]
    public async Task BusyStateIsNoSnapshot()
    {
        var (api, _) = Make(_ => Reply(HttpStatusCode.OK, "{\"ok\":false,\"reason\":\"busy\"}"));
        Assert.Null(await api.StateAsync());
    }

    [Fact]
    public async Task PollParsesAndRefusesItemsOffGrammar()
    {
        const string body = "{\"ok\":true,\"online\":[\"u_1\"],\"inbox\":[" +
            "{\"id\":\"i1\",\"kind\":\"poke\",\"from\":\"u_1\",\"from_name\":\"Ann\",\"poke\":\"hi\",\"at\":\"2026-09-23T10:00:00Z\",\"expires_at\":\"2026-09-24T10:00:00Z\"}," +
            "{\"id\":\"i2\",\"kind\":\"poke\",\"from\":\"u_1\",\"poke\":\"say anything\",\"at\":\"2026-09-23T10:00:00Z\"}," +
            "{\"id\":\"i3\",\"kind\":\"invite\",\"from\":\"u_1\",\"destination\":\"goon\",\"code\":\"AB12-CD\",\"at\":\"2026-09-23T10:00:00Z\"}," +
            "{\"id\":\"i4\",\"kind\":\"watch\",\"from\":\"u_1\",\"watch\":{\"kind\":\"ht\",\"id\":\"123\",\"title\":\"x\"},\"at\":\"2026-09-23T10:00:00Z\"}," +
            "{\"id\":\"i5\",\"kind\":\"invite\",\"from\":\"u_1\",\"destination\":\"elsewhere\",\"at\":\"2026-09-23T10:00:00Z\"}]}";
        var (api, h) = Make(_ => Reply(HttpStatusCode.OK, body));
        var r = await api.PollAsync(PresenceActivity.Race, 4, true);
        Assert.NotNull(r);
        Assert.Equal(new[] { "u_1" }, r!.Online);
        Assert.Equal(new[] { "i1", "i3", "i4" }, r.Inbox.Select(i => i.Id));
        var invite = r.Inbox.Single(i => i.Id == "i3");
        Assert.Equal(invite.At.AddSeconds(InviteDestination.LifetimeSeconds), invite.ExpiresAt);
        var sent = JObject.Parse(h.LastBody!);
        Assert.Equal("race", (string?)sent["activity"]);
        Assert.Equal(4, (int?)sent["lock_day"]);

        await api.PollAsync(PresenceActivity.Race, 4, false);
        sent = JObject.Parse(h.LastBody!);
        Assert.Null(sent["activity"]);
        Assert.Null(sent["lock_day"]);
        Assert.False((bool)sent["shared"]!);
    }
}
