using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
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
    public void Whitelist_ResolvesTheContractRows(string station, string op, string method)
    {
        Assert.True(BackRoomApi.TryResolve(station, op, out var m, out var path));
        Assert.Equal(method, m);
        Assert.Equal($"/v2/backroom/{station}/{op}", path);
    }

    [Theory]
    [InlineData("slot", "spin")]
    [InlineData("slot", "STATE")]
    [InlineData("wheel", "state")]
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

    [Fact]
    public async Task NonJsonReply_IsBadReply()
    {
        var (api, h, sp) = Make();
        h.Answer = _ => Json(502, "<html>gateway</html>");
        var r = await api.RelayAsync("slot", "state", null, null, TestContext.Current.CancellationToken);
        Assert.Equal((false, 502, "bad_reply"), (r.Ok, r.Status, r.Reason));
        Assert.Empty(sp);
    }
}
