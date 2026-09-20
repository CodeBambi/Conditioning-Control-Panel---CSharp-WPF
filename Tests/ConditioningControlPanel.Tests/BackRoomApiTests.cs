using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Back Room relay (CONTRACT section 3): the Ops whitelist is the only door to the server,
/// the token door is attached by the host and never by the page, a network failure is retried
/// exactly once with the same idem, and every <c>sp</c> in a reply is adopted.
/// </summary>
public class BackRoomApiTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Url, string? Token, string? Body)> Seen = new();
        public Func<int, HttpResponseMessage> Answer = _ => Json(200, "{\"ok\":true}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content == null ? null : await r.Content.ReadAsStringAsync(ct);
            Seen.Add((r.Method, r.RequestUri!.ToString(), r.Headers.TryGetValues("X-Auth-Token", out var v) ? v.First() : null, body));
            return Answer(Seen.Count);
        }
    }

    private static HttpResponseMessage Json(int status, string body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body) };

    private static (BackRoomApi Api, FakeHandler H, List<int> Sp) Make((string, string)? identity = null, bool signedOut = false)
    {
        var h = new FakeHandler();
        var sp = new List<int>();
        var api = new BackRoomApi(new HttpClient(h), () => signedOut ? null : identity ?? ("uid-1", "tok-1"), sp.Add);
        return (api, h, sp);
    }

    [Theory]
    [InlineData("slot", "state", "GET")]
    [InlineData("slot", "tape", "POST")]
    [InlineData("slot", "cursor", "POST")]
    [InlineData("slot", "chase", "POST")]
    [InlineData("wheel", "state", "GET")]
    [InlineData("wheel", "spin", "POST")]
    [InlineData("counter", "state", "GET")]
    [InlineData("counter", "buy", "POST")]
    [InlineData("decorations", "state", "GET")]
    [InlineData("decorations", "buy", "POST")]
    [InlineData("decorations", "layout", "POST")]
    public void Whitelist_ResolvesTheContractRows(string station, string op, string method)
    {
        Assert.True(BackRoomApi.TryResolve(station, op, out var m, out var path));
        Assert.Equal(method, m);
        Assert.Equal($"/v2/backroom/{station}/{op}", path);
    }

    [Theory]
    [InlineData("slot", "spin")]
    [InlineData("slot", "STATE")]
    [InlineData("wheel", "tape")]
    [InlineData("wheel", "cursor")]
    [InlineData("counter", "grant")]
    [InlineData("slot", "../admin")]
    [InlineData("", "state")]
    [InlineData(null, null)]
    public void Whitelist_RefusesEverythingElse(string? station, string? op)
        => Assert.False(BackRoomApi.TryResolve(station, op, out _, out _));

    [Fact]
    public async Task BadOp_NeverTouchesTheNetwork()
    {
        var (api, h, _) = Make();
        var r = await api.RelayAsync("slot", "withdraw", null, null, TestContext.Current.CancellationToken);
        Assert.Equal(("bad_op", 0, false), (r.Reason, r.Status, r.Ok));
        Assert.Empty(h.Seen);
    }

    [Fact]
    public async Task SignedOut_RefusesOffline()
    {
        var (api, h, _) = Make(signedOut: true);
        var r = await api.RelayAsync("slot", "state", null, null, TestContext.Current.CancellationToken);
        Assert.Equal("offline", r.Reason);
        Assert.Empty(h.Seen);
    }

    [Fact]
    public async Task Post_StampsIdemAndUnifiedIdOverThePageBody()
    {
        var (api, h, _) = Make();
        var body = JObject.Parse("{\"count\":10,\"unified_id\":\"someone-else\"}");
        await api.RelayAsync("slot", "tape", "idem-0123456789abcdef", body, TestContext.Current.CancellationToken);

        var (method, url, token, sent) = Assert.Single(h.Seen);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal(BackRoomApi.BaseUrl + "/v2/backroom/slot/tape", url);
        Assert.Equal("tok-1", token);
        var o = JObject.Parse(sent!);
        Assert.Equal("uid-1", (string?)o["unified_id"]);
        Assert.Equal("idem-0123456789abcdef", (string?)o["idem"]);
        Assert.Equal(10, (int)o["count"]!);
        Assert.Equal("someone-else", (string?)body["unified_id"]);   // the page's object is not mutated
    }

    [Fact]
    public async Task Get_CarriesUnifiedIdInTheQuery()
    {
        var (api, h, _) = Make();
        await api.RelayAsync("slot", "state", null, null, TestContext.Current.CancellationToken);
        Assert.Equal(BackRoomApi.BaseUrl + "/v2/backroom/slot/state?unified_id=uid-1", h.Seen[0].Url);
        Assert.Null(h.Seen[0].Body);
    }

    [Fact]
    public async Task NetworkFailure_RetriesOnceWithTheSameIdem()
    {
        var (api, h, _) = Make();
        h.Answer = n => n == 1 ? throw new HttpRequestException("reset") : Json(200, "{\"ok\":true,\"sp\":51}");
        var r = await api.RelayAsync("slot", "tape", "idem-aaaaaaaaaaaaaaaa", new JObject { ["count"] = 1 }, TestContext.Current.CancellationToken);

        Assert.True(r.Ok);
        Assert.Equal(2, h.Seen.Count);
        Assert.All(h.Seen, s => Assert.Equal("idem-aaaaaaaaaaaaaaaa", (string?)JObject.Parse(s.Body!)["idem"]));
    }

    [Fact]
    public async Task NetworkFailureTwice_IsOffline_AndStopsAtTwoAttempts()
    {
        var (api, h, _) = Make();
        h.Answer = _ => throw new HttpRequestException("down");
        var r = await api.RelayAsync("slot", "state", null, null, TestContext.Current.CancellationToken);
        Assert.Equal("offline", r.Reason);
        Assert.Equal(2, h.Seen.Count);
    }

    [Fact]
    public async Task Refusal_KeepsServerReasonAndStatus_AndStillAdoptsSp()
    {
        var (api2, h2, sp2) = Make();
        h2.Answer = _ => Json(200, "{\"ok\":false,\"reason\":\"insufficient\",\"sp\":3}");
        var r = await api2.RelayAsync("slot", "tape", "idem-bbbbbbbbbbbbbbbb", null, TestContext.Current.CancellationToken);
        Assert.Equal((false, 200, "insufficient"), (r.Ok, r.Status, r.Reason));
        Assert.Equal(new[] { 3 }, sp2);

        h2.Answer = _ => Json(403, "{\"ok\":false,\"reason\":\"closed\"}");
        var closed = await api2.RelayAsync("slot", "state", null, null, TestContext.Current.CancellationToken);
        Assert.Equal((false, 403, "closed"), (closed.Ok, closed.Status, closed.Reason));
    }

    [Theory]
    [InlineData(502, "<html>gateway</html>")]
    [InlineData(504, "")]
    [InlineData(200, "{\"ok\":false}")]
    [InlineData(500, "{\"error\":\"boom\"}")]
    [InlineData(200, "[1,2]")]
    public async Task UnwordedReplies_AreRefusedOffline(int status, string body)
    {
        // CONTRACT 2.2: the host's own reasons are offline, closed, bad_op and timeout, nothing else.
        var (api, h, sp) = Make();
        h.Answer = _ => Json(status, body);
        var r = await api.RelayAsync("slot", "state", null, null, TestContext.Current.CancellationToken);
        Assert.Equal((false, status, "offline"), (r.Ok, r.Status, r.Reason));
        Assert.Empty(sp);
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html>Cannot POST /v2/backroom/slot/chase</html>")]
    [InlineData("")]
    public async Task Unworded404_IsBadOp_NotOffline(string body)
    {
        // A route the server does not have will not appear by retrying it. Wording that 'offline' put it
        // in the page's RETRYABLE set, so the slot's undeployed optional chase route refused the whole
        // sit-down after four retries and the cabinet read "closed for a moment".
        var (api, h, sp) = Make();
        h.Answer = _ => Json(404, body);
        var r = await api.RelayAsync("slot", "chase", null, null, TestContext.Current.CancellationToken);
        Assert.Equal((false, 404, "bad_op"), (r.Ok, r.Status, r.Reason));
        Assert.Empty(sp);
    }

    private static (BackRoomApi Api, FakeHandler H, List<(string Account, long Revision, string[] Grants)> Applied) MakeCounter()
    {
        var h = new FakeHandler();
        var applied = new List<(string, long, string[])>();
        var api = new BackRoomApi(new HttpClient(h), () => ("uid-1", "tok-1"), null, (a, r, g) => applied.Add((a, r, g)));
        return (api, h, applied);
    }

    private const string Prizes = "\"prizes\":{\"revision\":4,\"grants\":[\"fx.jackpot_remix\",\"rt.original.00\",7]}";

    [Theory]
    [InlineData("state", "{\"ok\":true,\"open\":true,\"sp\":812," + Prizes + "}")]
    [InlineData("buy", "{\"ok\":true,\"prizeId\":\"jackpot_remix\",\"sp\":797," + Prizes + "}")]
    [InlineData("buy", "{\"ok\":false,\"reason\":\"owned\"," + Prizes + "}")]
    public async Task Counter_PrizesBlock_IsAppliedForTheAccountSent(string op, string body)
    {
        // CONTRACT 10.17.E feed 2: state, a buy and the owned refusal all carry the snapshot.
        var (api, h, applied) = MakeCounter();
        h.Answer = _ => Json(200, body);
        await api.RelayAsync("counter", op, op == "buy" ? "idem-cccccccccccccccc" : null, null, TestContext.Current.CancellationToken);
        var (account, revision, grants) = Assert.Single(applied);
        Assert.Equal(("uid-1", 4L), (account, revision));
        Assert.Equal(new[] { "fx.jackpot_remix", "rt.original.00" }, grants);
    }

    [Theory]
    [InlineData("counter", 200, "{\"ok\":false,\"reason\":\"insufficient\",\"sp\":3," + Prizes + "}")]
    [InlineData("counter", 403, "{\"ok\":false,\"reason\":\"owned\"," + Prizes + "}")]
    [InlineData("counter", 200, "{\"ok\":true,\"prizes\":{\"revision\":\"4\",\"grants\":[]}}")]
    [InlineData("counter", 200, "{\"ok\":true,\"prizes\":{\"revision\":4}}")]
    [InlineData("counter", 200, "{\"ok\":true,\"open\":false}")]
    [InlineData("slot", 200, "{\"ok\":true," + Prizes + "}")]
    public async Task PrizesBlock_IsIgnoredOffTheCounterRules(string station, int status, string body)
    {
        var (api, h, applied) = MakeCounter();
        h.Answer = _ => Json(status, body);
        await api.RelayAsync(station, "state", null, null, TestContext.Current.CancellationToken);
        Assert.Empty(applied);
    }

    [Fact]
    public async Task Counter_Buy_UnlocksOwnership_AndAReplayedOlderReceiptDoesNotRollBack()
    {
        var ownership = new OwnershipService(() => "uid-1", null, a => a());
        var h = new FakeHandler();
        var api = new BackRoomApi(new HttpClient(h), () => ("uid-1", "tok-1"), null, ownership.ApplySnapshot);
        h.Answer = _ => Json(200, "{\"ok\":true,\"sp\":5,\"prizes\":{\"revision\":5,\"grants\":[\"fx.flash.pendulum\"]}}");
        await api.RelayAsync("counter", "buy", "idem-dddddddddddddddd", null, TestContext.Current.CancellationToken);
        Assert.True(ownership.IsGranted("fx.flash.pendulum"));

        h.Answer = _ => Json(200, "{\"ok\":true,\"sp\":5,\"prizes\":{\"revision\":3,\"grants\":[]}}");
        await api.RelayAsync("counter", "state", null, null, TestContext.Current.CancellationToken);
        Assert.Equal(5, ownership.Revision);
        Assert.True(ownership.IsGranted("fx.flash.pendulum"));
    }
    [Theory]
    [InlineData("state", null, false)]
    [InlineData("layout", null, false)]
    [InlineData("buy", null, true)]
    [InlineData("buy", "owned", true)]
    [InlineData("buy", "insufficient", true)]
    public async Task Decorations_OnlyPurchasesAdoptBalance(string op, string? reason, bool adopts)
    {
        var (api, h, sp) = Make();
        var reply = new JObject { ["ok"] = reason == null, ["sp"] = 19,
            ["catalogVersion"] = "v1", ["decorations"] = new JObject { ["revision"] = 4 } };
        if (reason != null) reply["reason"] = reason;
        h.Answer = _ => Json(200, reply.ToString());
        var body = new JObject { ["decorationId"] = "plant", ["catalogVersion"] = "v1",
            ["revision"] = 3, ["layout"] = new JObject { ["plant"] = "fern" }, ["unified_id"] = "forged" };
        var result = await api.RelayAsync("decorations", op, "idem-decoration", body, TestContext.Current.CancellationToken);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(4, (int)result.Body!["decorations"]!["revision"]!);
        Assert.Equal(adopts ? new[] { 19 } : Array.Empty<int>(), sp);
        if (op != "state")
        {
            var sent = JObject.Parse(Assert.Single(h.Seen).Body!);
            Assert.Equal("uid-1", (string?)sent["unified_id"]);
            Assert.Equal("idem-decoration", (string?)sent["idem"]);
            Assert.True(JToken.DeepEquals(body["layout"], sent["layout"]));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccountChangesDuringRequest_DropsBalanceAndOwnership(bool signOut)
    {
        (string UnifiedId, string Token)? identity = ("first", "token");
        var h = new FakeHandler();
        var sp = new List<int>();
        int grants = 0;
        var api = new BackRoomApi(new HttpClient(h), () => identity, sp.Add, (_, _, _) => grants++);
        h.Answer = _ => {
            identity = signOut ? null : ("second", "token2");
            return Json(200, "{\"ok\":true,\"sp\":99," + Prizes + "}");
        };
        var result = await api.RelayAsync("counter", "buy", "idem-old", null, TestContext.Current.CancellationToken);
        Assert.Equal("closed", result.Reason);
        Assert.Null(result.Body);
        Assert.Empty(sp);
        Assert.Equal(0, grants);
    }

    [Fact]
    public async Task AccountChangesAfterNetworkFailure_DoesNotRetryOldMutation()
    {
        (string UnifiedId, string Token)? identity = ("first", "token");
        var h = new FakeHandler();
        var api = new BackRoomApi(new HttpClient(h), () => identity, null);
        h.Answer = _ => { identity = ("second", "token2"); throw new HttpRequestException("lost"); };
        var result = await api.RelayAsync("decorations", "buy", "idem-old", null, TestContext.Current.CancellationToken);
        Assert.Equal("closed", result.Reason);
        Assert.Single(h.Seen);
    }
}
