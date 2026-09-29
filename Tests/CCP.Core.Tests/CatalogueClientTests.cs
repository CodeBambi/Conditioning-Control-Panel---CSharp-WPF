using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Core CatalogueClient (WPF CatalogueService before the move) against a fake handler: no network. The token cache is
/// per instance, so each test's fresh client starts empty.
/// </summary>
public sealed class CatalogueClientTests
{
    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<(HttpRequestMessage Req, string Body)> Seen = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Submit = _ => new(HttpStatusCode.Created);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add((r, r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct)));
            return r.RequestUri!.AbsolutePath == "/api/auth/token-exchange"
                ? Json(HttpStatusCode.OK, $"{{\"access_token\":\"sb-{Seen.Count}\",\"expires_at\":\"{DateTimeOffset.UtcNow.AddHours(1):O}\"}}")
                : Submit(r);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode s, string body) => new(s) { Content = new StringContent(body) };

    private static CatalogueClient Client(Fake f, string? token = "ccp-tok", string? uid = "uid-1") =>
        new(() => token, () => uid, "9.9.9", f);

    private static Task<SubmissionResult> Submit(CatalogueClient c) =>
        c.SubmitCatalogueAssetAsync("presets", new JObject { ["n"] = 1 }, "ccp-preset", "me", new[] { " a ", "" }, CancellationToken.None);

    [Fact]
    public async Task EnhancementSuccessFiresSubmissionSucceededOnceButAssetSuccessDoesNot()
    {
        var f = new Fake { Submit = _ => Json(HttpStatusCode.Created, "{\"id\":\"e1\"}") };
        var c = Client(f);
        var fired = new List<string>();
        c.SubmissionSucceeded += (_, s) => fired.Add(s.Id);
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".ccpenh.json");
        try
        {
            System.IO.File.WriteAllText(file, "{\"title\":\"t\"}");
            Assert.IsType<SubmissionResult.Success>(await c.SubmitEnhancementAsync(file, CancellationToken.None));
            Assert.Equal(new[] { "e1" }, fired);

            Assert.IsType<SubmissionResult.Success>(await Submit(c));
            Assert.Equal(new[] { "e1" }, fired);
        }
        finally { System.IO.File.Delete(file); }
    }

    [Fact]
    public async Task ExchangesTokenThenPostsWithBearerAndVersionHeaders()
    {
        var f = new Fake();
        Assert.IsType<SubmissionResult.Success>(await Submit(Client(f)));

        Assert.Equal(2, f.Seen.Count);
        var (ex, exBody) = f.Seen[0];
        Assert.Equal("https://app.cclabs.app/api/auth/token-exchange", ex.RequestUri!.ToString());
        Assert.Equal("ccp-tok", ex.Headers.GetValues("X-CCP-Auth-Token").Single());
        Assert.Equal("uid-1", (string)JObject.Parse(exBody)["unified_id"]!);

        var (post, postBody) = f.Seen[1];
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.Equal("https://app.cclabs.app/api/catalogue/presets", post.RequestUri!.ToString());
        Assert.Equal("Bearer sb-1", post.Headers.Authorization!.ToString());
        Assert.Equal("9.9.9", post.Headers.GetValues("X-Client-Version").Single());
        Assert.Contains("ConditioningControlPanel/9.9.9", post.Headers.UserAgent.ToString());
        var env = JObject.Parse(postBody);
        Assert.True((bool)env["affirmation"]!["affirmed"]!);
        Assert.Equal(new[] { "a" }, env["bundle"]!["metadata"]!["tags"]!.ToObject<string[]>());
    }

    public static IEnumerable<object[]> Statuses() => new[]
    {
        new object[] { 201, "{\"id\":\"x\",\"status\":\"published\"}", new SubmissionResult.Success("x", "published") },
        new object[] { 409, "{\"existing_id\":\"y\"}", new SubmissionResult.Duplicate("y", "pending") },
        new object[] { 400, "{\"error\":\"bad_tags\"}", new SubmissionResult.ValidationError("bad_tags") },
        new object[] { 401, "{}", new SubmissionResult.AuthFailed() },
        new object[] { 403, "{\"error\":\"no_profile\"}", new SubmissionResult.AuthFailed() },
        new object[] { 413, "", new SubmissionResult.TooLarge() },
        new object[] { 429, "{\"retry_after\":42}", new SubmissionResult.RateLimited(42) },
        new object[] { 500, "boom", new SubmissionResult.UnknownError(500, "boom") },
    };

    [Theory]
    [MemberData(nameof(Statuses))]
    public async Task EachStatusMapsToItsResult(int status, string body, SubmissionResult expected)
    {
        var f = new Fake { Submit = _ => Json((HttpStatusCode)status, body) };
        Assert.Equal(expected, await Submit(Client(f)));
    }

    [Fact]
    public async Task RetryAfterHeaderWinsOverBody()
    {
        var f = new Fake
        {
            Submit = _ =>
            {
                var r = Json((HttpStatusCode)429, "{\"retry_after\":42}");
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                return r;
            },
        };
        Assert.Equal(new SubmissionResult.RateLimited(7), await Submit(Client(f)));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task CachedTokenIsDroppedAfterAuthFailure(int status)
    {
        var f = new Fake { Submit = _ => Json((HttpStatusCode)status, "{}") };
        var c = Client(f);
        await Submit(c);
        f.Submit = _ => Json(HttpStatusCode.Created, "{}");
        await Submit(c);
        Assert.Equal(2, f.Seen.Count(s => s.Req.RequestUri!.AbsolutePath == "/api/auth/token-exchange"));
    }

    [Fact]
    public async Task CachedTokenIsReusedAfterSuccess()
    {
        var f = new Fake();
        var c = Client(f);
        await Submit(c);
        await Submit(c);
        Assert.Equal(1, f.Seen.Count(s => s.Req.RequestUri!.AbsolutePath == "/api/auth/token-exchange"));
    }

    [Theory]
    [InlineData(null, "uid-1")]
    [InlineData("ccp-tok", null)]
    public async Task SignedOutSendsNothing(string? token, string? uid)
    {
        var f = new Fake();
        var c = Client(f, token, uid);
        Assert.IsType<SubmissionResult.AuthFailed>(await Submit(c));
        Assert.Null(await c.FetchMySubmissionsAsync(CancellationToken.None));
        Assert.Empty(f.Seen);
    }

    [Theory]
    [InlineData(null, false, "https://app.cclabs.app")]
    [InlineData("http://127.0.0.1:5055/", false, "http://127.0.0.1:5055")]
    [InlineData("http://127.0.0.1:5055/", true, "http://127.0.0.1:5055")]
    [InlineData(null, true, null)]
    [InlineData("https://evil.example", true, null)]
    [InlineData("https://evil.example", false, "https://app.cclabs.app")]
    public void BaseUrlOverrideIsLoopbackOnlyAndASandboxNeverReachesTheRealServer(string? overrideUrl, bool sandboxed, string? expected) =>
        Assert.Equal(expected, CatalogueClient.ResolveBaseUrl(overrideUrl, sandboxed));
}
