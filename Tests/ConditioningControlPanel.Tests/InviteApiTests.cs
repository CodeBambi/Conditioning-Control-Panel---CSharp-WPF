using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Invites;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The invites wire: what it sends, and that every fault is a quiet null.</summary>
public class InviteApiTests
{
    private sealed class Stub : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage? Last;
        public string? LastBody;

        public Stub(HttpStatusCode status, string body) { _status = status; _body = body; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }

    private static (InviteApi Api, Stub Stub) Make(HttpStatusCode status, string body, bool signedIn = true)
    {
        var stub = new Stub(status, body);
        var api = new InviteApi(new HttpClient(stub), () => signedIn ? ("u-123", "tok") : null, "https://example.test");
        return (api, stub);
    }

    [Fact]
    public async Task Redeem_PostsTheNormalizedCodeWithTheToken()
    {
        var (api, stub) = Make(HttpStatusCode.OK, "{\"ok\":true,\"grant_until\":\"2026-10-09T12:00:00Z\"}");
        var outcome = await api.RedeemAsync(" pink-mia-7q4x ", TestContext.Current.CancellationToken);

        Assert.True(outcome.Ok);
        Assert.Equal(HttpMethod.Post, stub.Last!.Method);
        Assert.Equal("https://example.test/v2/invites/redeem", stub.Last.RequestUri!.ToString());
        Assert.Equal("tok", string.Join("", stub.Last.Headers.GetValues("X-Auth-Token")));
        var sent = JObject.Parse(stub.LastBody!);
        Assert.Equal("PINK-MIA-7Q4X", sent.Value<string>("code"));
        Assert.Equal("u-123", sent.Value<string>("unified_id"));
    }

    [Fact]
    public async Task Redeem_ABadCodeNeverLeavesTheMachine()
    {
        var (api, stub) = Make(HttpStatusCode.OK, "{\"ok\":true}");
        var outcome = await api.RedeemAsync("no", TestContext.Current.CancellationToken);
        Assert.Equal("bad_code", outcome.Reason);
        Assert.Null(stub.Last);
    }

    [Fact]
    public async Task Redeem_SignedOutIsOffline()
    {
        var (api, stub) = Make(HttpStatusCode.OK, "{\"ok\":true}", signedIn: false);
        Assert.Equal(RedeemOutcome.Offline, await api.RedeemAsync("PINK-MIA-7Q4X", TestContext.Current.CancellationToken));
        Assert.Null(stub.Last);
    }

    [Fact]
    public async Task Redeem_AWordedRefusalKeepsItsReason()
    {
        var (api, _) = Make(HttpStatusCode.Conflict, "{\"ok\":false,\"reason\":\"used\"}");
        Assert.Equal("used", (await api.RedeemAsync("PINK-MIA-7Q4X", TestContext.Current.CancellationToken)).Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "<html>not here</html>")]
    [InlineData(HttpStatusCode.InternalServerError, "{\"ok\":false,\"reason\":\"boom\"}")]
    public async Task Mine_AServerWithoutInvitesIsUnreachable(HttpStatusCode status, string body)
    {
        var (api, _) = Make(status, body);
        Assert.Equal(InviteMine.Unreachable, await api.MineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Mine_ANonSubscriberIsReachableWithNoSnapshot()
    {
        var (api, _) = Make(HttpStatusCode.OK, "{\"ok\":false,\"reason\":\"not_subscribed\"}");
        var mine = await api.MineAsync(TestContext.Current.CancellationToken);
        Assert.True(mine.Reachable);
        Assert.Null(mine.Snapshot);
    }

    [Fact]
    public async Task Mine_ReadsTheSnapshot()
    {
        var (api, stub) = Make(HttpStatusCode.OK, "{\"ok\":true,\"converted_total\":2,\"codes\":[{\"code\":\"PINK-MIA-7Q4X\",\"state\":\"open\"}]}");
        var snap = (await api.MineAsync(TestContext.Current.CancellationToken)).Snapshot;
        Assert.Equal("https://example.test/v2/invites/mine", stub.Last!.RequestUri!.ToString());
        Assert.Equal(2, snap!.ConvertedTotal);
        Assert.Single(snap.Slots);
    }
}
