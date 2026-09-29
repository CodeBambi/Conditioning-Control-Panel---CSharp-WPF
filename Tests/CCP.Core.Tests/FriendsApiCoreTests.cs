using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The friends wire now in Core, driven by a fake handler only (never the real server).</summary>
public class FriendsApiCoreTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Calls;
        public string? Body, Uri, Token;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            Uri = r.RequestUri!.ToString();
            Body = r.Content == null ? null : await r.Content.ReadAsStringAsync(ct);
            Token = r.Headers.TryGetValues("X-Auth-Token", out var v) ? v.First() : null;
            return answer(r);
        }
    }

    private static HttpResponseMessage Reply(HttpStatusCode s, string body) =>
        new(s) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(200, "{\"ok\":true,\"status\":\"sent\"}", SendResult.Sent)]
    [InlineData(200, "{\"ok\":false,\"reason\":\"not_friends\"}", SendResult.NotFriends)]
    [InlineData(429, "", SendResult.TooFast)]
    [InlineData(502, "<html>bad gateway</html>", SendResult.TryLater)]
    public async Task SendIsWordedAndStamped(int status, string body, SendResult expected)
    {
        var h = new Handler(_ => Reply((HttpStatusCode)status, body));
        var api = new FriendsApi(new HttpClient(h), () => ("u_me", "tok"), "http://127.0.0.1:1");
        Assert.Equal(expected, await api.SendAsync("u_1", SendKind.Poke, "hi", null, null, null));
        Assert.Equal("http://127.0.0.1:1/v2/friends/send", h.Uri);
        Assert.Equal("tok", h.Token);
        Assert.Equal("u_me", (string?)JObject.Parse(h.Body!)["unified_id"]);
    }

    [Fact]
    public async Task EveryReplyReachesTheMergedAccountHook()
    {
        (int, string)? seen = null;
        var h = new Handler(_ => Reply(HttpStatusCode.Conflict, "{\"ok\":false,\"reason\":\"merged\"}"));
        var api = new FriendsApi(new HttpClient(h), () => ("u_me", "tok"), "http://127.0.0.1:1",
            (s, b) => { seen = (s, b); return true; });
        await api.ActAsync("remove", "u_1");
        Assert.Equal((409, "{\"ok\":false,\"reason\":\"merged\"}"), seen);
    }

    [Fact]
    public async Task NoAccountSendsNothing()
    {
        var h = new Handler(_ => Reply(HttpStatusCode.OK, "{}"));
        var api = new FriendsApi(new HttpClient(h), () => null, "http://127.0.0.1:1");
        Assert.Null(await api.StateAsync());
        Assert.Equal(0, h.Calls);
    }
}
