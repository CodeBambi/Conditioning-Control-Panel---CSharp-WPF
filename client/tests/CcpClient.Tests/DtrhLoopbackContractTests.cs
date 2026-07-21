using System.Net;
using System.Text.Json;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// §4 loopback security contract + §3.3 inbox endpoint (dtrh-admission.md), exercised
/// against the REAL LoopbackServer on 127.0.0.1 with synthetic payload/overlay trees in
/// temp dirs. Covers: GET-only (405), route 404s, overlay-first shadowing, Range 206/416,
/// MIME allowlist 415 deny-by-default + nosniff, CORS preflight + CORS-on-errors,
/// traversal refusal, token-required inbox, and the sensitive-logging ban (the token and
/// query strings never appear in logs).
/// </summary>
public sealed class DtrhLoopbackContractTests : IDisposable
{
    private const string Token = "testtoken-Ab3-123_Xy";
    private readonly string _root;
    private readonly Inbox _inbox = new();
    private readonly CollectingLog _log = new();
    private readonly LoopbackServer _server;

    public DtrhLoopbackContractTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ccp-sp023-loopback-" + Guid.NewGuid().ToString("N"));
        var payload = Path.Combine(_root, "payload");
        var overlay = Path.Combine(_root, "overlay");
        var media = Path.Combine(payload, "assets");
        Directory.CreateDirectory(media);
        Directory.CreateDirectory(overlay);
        File.WriteAllText(Path.Combine(payload, "index.html"), "<html>payload index</html>");
        File.WriteAllText(Path.Combine(payload, "bridge.js"), "// payload bridge (must be shadowed)");
        File.WriteAllText(Path.Combine(payload, "weird.bin"), "binary");
        File.WriteAllText(Path.Combine(overlay, "bridge.js"), "// product derivative bridge");
        File.WriteAllText(Path.Combine(overlay, "probe.html"), "<html>probe</html>");
        File.WriteAllBytes(Path.Combine(media, "clip.mp3"), Enumerable.Range(0, 1000).Select(i => (byte)(i % 251)).ToArray());
        File.WriteAllText(Path.Combine(media, "note.json"), "{}");

        _server = new LoopbackServer(payload, overlay, media, _inbox, Token, _log,
            longPollTimeout: TimeSpan.FromMilliseconds(200));
        _server.Start();
    }

    public void Dispose() => _server.Dispose();

    private sealed class CollectingLog : ILogSink
    {
        private readonly object _gate = new();
        private readonly List<string> _lines = [];

        public void Log(string message)
        {
            lock (_gate) { _lines.Add(message); }
        }

        public string All { get { lock (_gate) { return string.Join('\n', _lines); } } }
    }

    [Fact]
    public async Task Health_Returns200()
    {
        using var client = new HttpClient();
        var response = await client.GetAsync(_server.PageOrigin + "/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PageOrigin_DtrhRoot_ServesIndexHtml()
    {
        using var client = new HttpClient();
        var body = await client.GetStringAsync(_server.PageOrigin + "/dtrh/", TestContext.Current.CancellationToken);
        Assert.Contains("payload index", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OverlayFirst_BridgeJsShadowedByProductDerivative()
    {
        // The ONE admitted shadow: /dtrh/bridge.js serves the overlay derivative (§3.1 diff).
        using var client = new HttpClient();
        var body = await client.GetStringAsync(_server.PageOrigin + "/dtrh/bridge.js", TestContext.Current.CancellationToken);
        Assert.Contains("product derivative bridge", body, StringComparison.Ordinal);
        Assert.DoesNotContain("must be shadowed", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OverlayNewPath_ProbeHtmlServed()
    {
        using var client = new HttpClient();
        var body = await client.GetStringAsync(_server.PageOrigin + "/dtrh/probe.html", TestContext.Current.CancellationToken);
        Assert.Contains("probe", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRoute_404_AndNonGet_405()
    {
        using var client = new HttpClient();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync(_server.PageOrigin + "/elsewhere", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync(_server.PageOrigin + "/dtrh/missing.png", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await client.PostAsync(_server.PageOrigin + "/dtrh/index.html", new StringContent("x"), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await client.PostAsync(_server.MediaOrigin + "/media/clip.mp3", new StringContent("x"), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task MimeAllowlist_UnknownExtension_415_WithNosniff()
    {
        using var client = new HttpClient();
        var denied = await client.GetAsync(_server.PageOrigin + "/dtrh/weird.bin", TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode)415, denied.StatusCode);
        var ok = await client.GetAsync(_server.PageOrigin + "/dtrh/index.html", TestContext.Current.CancellationToken);
        Assert.Equal("nosniff", string.Join(",", ok.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("text/html", ok.Content.Headers.ContentType!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/dtrh/..%2Fsecret")]
    [InlineData("/dtrh/%2e%2e%2Fsecret")]
    [InlineData("/dtrh/back%5Cslash")]
    [InlineData("/dtrh/c%3A%5Cwindows")]
    public async Task Traversal_Refused403(string path)
    {
        using var client = new HttpClient();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync(_server.PageOrigin + path, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Range_Valid206_Invalid416_Suffix206()
    {
        using var client = new HttpClient();
        var ranged = new HttpRequestMessage(HttpMethod.Get, _server.MediaOrigin + "/media/clip.mp3");
        ranged.Headers.TryAddWithoutValidation("Range", "bytes=0-9");
        var partial = await client.SendAsync(ranged, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal("bytes 0-9/1000", partial.Content.Headers.GetValues("Content-Range").Single());
        Assert.Equal(10, (await partial.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Length);

        var suffixReq = new HttpRequestMessage(HttpMethod.Get, _server.MediaOrigin + "/media/clip.mp3");
        suffixReq.Headers.TryAddWithoutValidation("Range", "bytes=-5");
        var suffix = await client.SendAsync(suffixReq, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.PartialContent, suffix.StatusCode);
        Assert.Equal("bytes 995-999/1000", suffix.Content.Headers.GetValues("Content-Range").Single());

        var invalidReq = new HttpRequestMessage(HttpMethod.Get, _server.MediaOrigin + "/media/clip.mp3");
        invalidReq.Headers.TryAddWithoutValidation("Range", "bytes=999999-");
        var invalid = await client.SendAsync(invalidReq, TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode)416, invalid.StatusCode);
    }

    [Fact]
    public async Task MediaCors_ScopedToPageOrigin_ExposeContentRange_Preflight204()
    {
        using var client = new HttpClient();
        var response = await client.GetAsync(_server.MediaOrigin + "/media/clip.mp3", TestContext.Current.CancellationToken);
        Assert.Equal(_server.PageOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("Content-Range", response.Headers.GetValues("Access-Control-Expose-Headers").Single());

        var preflight = new HttpRequestMessage(HttpMethod.Options, _server.MediaOrigin + "/media/clip.mp3");
        var pre = await client.SendAsync(preflight, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, pre.StatusCode);
        Assert.Equal("range", pre.Headers.GetValues("Access-Control-Allow-Headers").Single());
    }

    [Fact]
    public async Task CorsOnErrors_MediaRefusalsCarryCorsHeaders()
    {
        // SP-011 W18 lesson: a CORS-less error surfaces to fetch() as an opaque TypeError.
        using var client = new HttpClient();
        var missing = await client.GetAsync(_server.MediaOrigin + "/media/nope.mp3", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(_server.PageOrigin, missing.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var unknownRoute = await client.GetAsync(_server.MediaOrigin + "/other", TestContext.Current.CancellationToken);
        Assert.Equal(_server.PageOrigin, unknownRoute.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    // ---------- §3.3 inbox endpoint ----------

    [Fact]
    public async Task Inbox_WrongToken_404_NonGet_405()
    {
        using var client = new HttpClient();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync(_server.PageOrigin + "/bridge/wrong-token/inbox?after=0", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync(_server.PageOrigin + "/bridge/", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await client.PostAsync(_server.PageOrigin + $"/bridge/{Token}/inbox?after=0", new StringContent("x"), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Inbox_MissingOrBadAfter_400()
    {
        using var client = new HttpClient();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync(_server.PageOrigin + $"/bridge/{Token}/inbox", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync(_server.PageOrigin + $"/bridge/{Token}/inbox?after=abc", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Inbox_SeqAckAndJsonShape()
    {
        _inbox.Enqueue("{\"type\":\"init\",\"protocol\":1}");
        _inbox.Enqueue("{\"type\":\"manifest\",\"skipped\":0}");
        using var client = new HttpClient();
        var first = await client.GetStringAsync(_server.PageOrigin + $"/bridge/{Token}/inbox?after=0", TestContext.Current.CancellationToken);
        using (var doc = JsonDocument.Parse(first))
        {
            var messages = doc.RootElement.GetProperty("messages");
            Assert.Equal(2, messages.GetArrayLength());
            Assert.Equal(1, messages[0].GetProperty("seq").GetInt64());
            Assert.Equal("init", messages[0].GetProperty("body").GetProperty("type").GetString());
            Assert.Equal(2, messages[1].GetProperty("seq").GetInt64());
        }

        // Ack seq<=1: only seq 2 remains.
        var second = await client.GetStringAsync(_server.PageOrigin + $"/bridge/{Token}/inbox?after=1", TestContext.Current.CancellationToken);
        using (var doc = JsonDocument.Parse(second))
        {
            var messages = doc.RootElement.GetProperty("messages");
            Assert.Single(messages.EnumerateArray());
            Assert.Equal(2, messages[0].GetProperty("seq").GetInt64());
        }

        // Ack everything: empty (bounded timeout returns quickly in this fixture).
        var third = await client.GetStringAsync(_server.PageOrigin + $"/bridge/{Token}/inbox?after=2", TestContext.Current.CancellationToken);
        using (var doc = JsonDocument.Parse(third))
        {
            Assert.Equal(0, doc.RootElement.GetProperty("messages").GetArrayLength());
        }
    }

    [Fact]
    public async Task Inbox_JsonOnly_ContentType()
    {
        _inbox.Enqueue("{\"type\":\"x\"}");
        using var client = new HttpClient();
        var response = await client.GetAsync(_server.PageOrigin + $"/bridge/{Token}/inbox?after=0", TestContext.Current.CancellationToken);
        Assert.Contains("application/json", response.Content.Headers.ContentType!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inbox_LongPollHangs_UntilEnqueue()
    {
        using var client = new HttpClient();
        var pending = client.GetStringAsync(_server.PageOrigin + $"/bridge/{Token}/inbox?after=0", TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _inbox.Enqueue("{\"type\":\"init\"}");
        var body = await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        Assert.Single(doc.RootElement.GetProperty("messages").EnumerateArray());
    }

    // ---------- §4.8 sensitive-logging ban ----------

    [Fact]
    public async Task Logs_NeverContainToken_OrQueryStrings()
    {
        using var client = new HttpClient();
        await client.GetStringAsync(_server.PageOrigin + $"/dtrh/index.html?bridge={Token}", TestContext.Current.CancellationToken);
        _inbox.Enqueue("{\"type\":\"x\"}");
        await client.GetStringAsync(_server.PageOrigin + $"/bridge/{Token}/inbox?after=0", TestContext.Current.CancellationToken);
        await client.GetAsync(_server.PageOrigin + "/bridge/wrong-token/inbox?after=0", TestContext.Current.CancellationToken);
        Assert.DoesNotContain(Token, _log.All, StringComparison.Ordinal);
        Assert.DoesNotContain("after=", _log.All, StringComparison.Ordinal);
        Assert.DoesNotContain("bridge=", _log.All, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong-token", _log.All, StringComparison.Ordinal);
    }
}
