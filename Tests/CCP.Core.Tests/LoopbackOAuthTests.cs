using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The shared OAuth callback: a scripted browser GET against a real loopback port, and a fake proxy
/// (HttpMessageHandler) for the code exchange. Expected strings are WPF's (PatreonService.cs before the move).
/// </summary>
public sealed class LoopbackOAuthTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>What a browser sends after the proxy redirect: Host is always localhost:port.</summary>
    private static async Task<(HttpStatusCode Status, string Body)> BrowserGet(string host, int port, string query)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var req = new HttpRequestMessage(HttpMethod.Get, $"http://{host}:{port}/callback/?{query}");
        req.Headers.Host = $"localhost:{port}";
        var res = await http.SendAsync(req);
        return (res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    private static Task<System.Collections.Specialized.NameValueCollection> Wait(LoopbackOAuth l, int ms = 10_000) =>
        l.WaitAsync(TimeSpan.FromMilliseconds(ms), "OAuth login timed out. Please try again.",
            LoopbackOAuth.SuccessHtml, LoopbackOAuth.FailureHtml, CancellationToken.None);

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    public async Task GoodState_OnEitherLoopback_ServesSuccessPage_AndReturnsCode(string host)
    {
        var port = FreePort();
        using var oauth = new LoopbackOAuth(port, useHttpListener: false);
        Assert.Equal($"http://localhost:{port}/callback/", oauth.CallbackUrl);

        var wait = Wait(oauth);
        var (status, body) = await BrowserGet(host, port, $"code=abc%2F1&state={oauth.State}");
        var query = await wait;

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(LoopbackOAuth.SuccessHtml, body);
        Assert.Equal("abc/1", query["code"]);
    }

    [Fact]
    public async Task HttpListenerPath_GoodState_ServesSuccessPage()
    {
        // The Windows (http.sys) path; on Linux the managed HttpListener binds one loopback, so go by name.
        var port = FreePort();
        using var oauth = new LoopbackOAuth(port, useHttpListener: true);
        var wait = Wait(oauth);
        var (status, body) = await BrowserGet("localhost", port, $"code=abc&state={oauth.State}");
        Assert.Equal("abc", (await wait)["code"]);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(LoopbackOAuth.SuccessHtml, body);
    }

    [Fact]
    public async Task BadState_Throws_AfterAnsweringTheBrowser()
    {
        var port = FreePort();
        using var oauth = new LoopbackOAuth(port, useHttpListener: false);
        var wait = Wait(oauth);
        var (_, body) = await BrowserGet("127.0.0.1", port, "code=abc&state=00FF");

        var ex = await Assert.ThrowsAsync<SecurityException>(() => wait);
        Assert.Equal("OAuth state mismatch - possible CSRF attack", ex.Message);
        Assert.Equal(LoopbackOAuth.SuccessHtml, body); // WPF answered before checking
    }

    [Fact]
    public async Task ErrorCallback_ServesFailurePage_AndReturnsError()
    {
        var port = FreePort();
        using var oauth = new LoopbackOAuth(port, useHttpListener: false);
        var wait = Wait(oauth);
        var (_, body) = await BrowserGet("127.0.0.1", port, $"error=access_denied&error_description=nope&state={oauth.State}");

        var query = await wait;
        Assert.Equal(LoopbackOAuth.FailureHtml, body);
        Assert.Equal("access_denied", query["error"]);
        Assert.Equal("nope", query["error_description"]);
    }

    [Fact]
    public async Task NoCallback_TimesOut_WithWpfMessage()
    {
        using var oauth = new LoopbackOAuth(FreePort(), useHttpListener: false);
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => Wait(oauth, ms: 200));
        Assert.Equal("OAuth login timed out. Please try again.", ex.Message);
    }

    [Fact]
    public void BusyPort_FailsAtOnce_WithClearMessage()
    {
        var busy = new TcpListener(IPAddress.Loopback, 0);
        busy.Start();
        try
        {
            var port = ((IPEndPoint)busy.LocalEndpoint).Port;
            var ex = Assert.Throws<IOException>(() => new LoopbackOAuth(port, useHttpListener: false));
            Assert.Contains($"Is another program using port {port}?", ex.Message);
        }
        finally { busy.Stop(); }
    }

    /// <summary>What an HTTP client sends, raw: status line of the reply.</summary>
    private static async Task<string> RawGet(int port, string requestLine, string host)
    {
        using var c = new TcpClient();
        await c.ConnectAsync(IPAddress.Loopback, port);
        var s = c.GetStream();
        await s.WriteAsync(Encoding.ASCII.GetBytes($"{requestLine}\r\nHost: {host}\r\n\r\n"));
        return (await new StreamReader(s).ReadLineAsync())!;
    }

    [Theory]
    [InlineData("GET /favicon.ico HTTP/1.1", "localhost", "HTTP/1.1 404 Not Found")]
    [InlineData("GET /callbackX?code=evil HTTP/1.1", "localhost", "HTTP/1.1 404 Not Found")]
    [InlineData("GET /callback/?code=evil HTTP/1.1", "evil", "HTTP/1.1 400 Bad Request")]
    public async Task StrayRequest_IsRefused_AndTheRealCallbackStillLands(string requestLine, string hostName, string expected)
    {
        var port = FreePort();
        using var oauth = new LoopbackOAuth(port, useHttpListener: false);
        var wait = Wait(oauth);

        Assert.Equal(expected, await RawGet(port, requestLine, $"{hostName}:{port}"));
        Assert.False(wait.IsCompleted);

        await BrowserGet("127.0.0.1", port, $"code=abc&state={oauth.State}");
        Assert.Equal("abc", (await wait)["code"]);
    }

    [Fact]
    public async Task OversizedHeaders_Are400()
    {
        var port = FreePort();
        using var oauth = new LoopbackOAuth(port, useHttpListener: false);
        _ = Wait(oauth);
        var many = string.Concat(Enumerable.Repeat("X-Pad: 1\r\n", 101));
        Assert.Equal("HTTP/1.1 400 Bad Request", await RawGet(port, $"GET /callback/ HTTP/1.1\r\nHost: localhost:{port}\r\n" + many.TrimEnd(), $"localhost:{port}"));
    }

    [Fact]
    public async Task SilentSocket_DoesNotHoldThePort_AfterDispose()
    {
        var port = FreePort();
        using var silent = new TcpClient();
        using (var oauth = new LoopbackOAuth(port, useHttpListener: false))
        {
            _ = Wait(oauth);
            await silent.ConnectAsync(IPAddress.Loopback, port); // browser pre-connect that never sends
            await Task.Delay(100);
        }
        new LoopbackOAuth(port, useHttpListener: false).Dispose(); // a retry can bind again
    }

    /// <summary>SHA-256 (LF line ends) of the pages in the pre-move WPF sources, origin/avalonia-port/v2-client-core.</summary>
    [Theory]
    [InlineData(nameof(LoopbackOAuth.SuccessHtml), "8B0F0D3419AF1F1877918F550D79FB03DDE1BAD2A68DDCC26B8D1364806E24C3")]
    [InlineData(nameof(LoopbackOAuth.FailureHtml), "F25AE5262C2115C7598896BC9BDD847A963B65909DCC66B0A4502C5CCF5DED90")]
    [InlineData(nameof(LoopbackOAuth.DiscordSuccessHtml), "C72708B0A22C0E8BCB17677F2D00B7C5400F860AA951C24E201049A9D86A3520")]
    [InlineData(nameof(LoopbackOAuth.DiscordFailureHtml), "E36155BE44CAADCD3B0A39FA19AECE6732EE660C9E58FDDCC947ED0465ED8697")]
    public void BrowserPages_MatchWpfByteForByte(string field, string sha256)
    {
        var html = ((string)typeof(LoopbackOAuth).GetField(field)!.GetValue(null)!).Replace("\r\n", "\n");
        Assert.Equal(sha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(html))));
    }

    private sealed class FakeProxy(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Path, Body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.Method + " " + request.RequestUri!.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static HttpClient Proxy(FakeProxy fake) => new(fake) { BaseAddress = new Uri("https://codebambi-proxy.vercel.app") };

    [Fact]
    public async Task EndToEnd_CallbackCode_IsTradedAtTheProxy_WithTheExactRedirect()
    {
        var port = FreePort();
        using var oauth = new LoopbackOAuth(port, useHttpListener: false);
        var wait = Wait(oauth);
        await BrowserGet("127.0.0.1", port, $"code=abc&state={oauth.State}");
        var code = (await wait)["code"];

        var fake = new FakeProxy(HttpStatusCode.OK, "{\"access_token\":\"A\",\"refresh_token\":\"R\",\"expires_in\":3600}");
        var tokens = await LoopbackOAuth.ExchangeAsync(Proxy(fake), "/patreon/token", new { code, redirect_uri = oauth.CallbackUrl });

        Assert.Equal("POST /patreon/token", fake.Path);
        Assert.Equal($"{{\"code\":\"abc\",\"redirect_uri\":\"http://localhost:{port}/callback/\"}}", fake.Body);
        Assert.Equal(("A", "R", 3600), (tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresIn));
    }

    [Fact]
    public async Task Exchange_NonSuccessStatus_Throws_WithStatusAndBody()
    {
        var fake = new FakeProxy(HttpStatusCode.BadRequest, "bad code");
        var ex = await Assert.ThrowsAsync<Exception>(() => LoopbackOAuth.ExchangeAsync(Proxy(fake), "/discord/token", new { code = "x" }));
        Assert.Equal("Token exchange failed: BadRequest - bad code", ex.Message);
    }

    [Fact]
    public async Task Exchange_ErrorBody_Throws_WithDescription()
    {
        var fake = new FakeProxy(HttpStatusCode.OK, "{\"error\":\"invalid_grant\",\"error_description\":\"expired\"}");
        var ex = await Assert.ThrowsAsync<Exception>(() => LoopbackOAuth.ExchangeAsync(Proxy(fake), "/substar/token", new { code = "x" }));
        Assert.Equal("Token exchange failed: expired", ex.Message);
    }

    // --- The browser flow moved from WPF Patreon/Discord/SubscribeStarService.StartOAuthFlowAsync (login-ui). ---

    [Theory] // Transcribed from the WPF services before the move (PatreonService.cs:108, DiscordService.cs:82, SubscribeStarService.cs:83).
    [InlineData("patreon", 47832, "https://codebambi-proxy.vercel.app/patreon/authorize?redirect_uri=http%3A%2F%2Flocalhost%3A47832%2Fcallback%2F&state=ST")]
    [InlineData("discord", 47833, "https://codebambi-proxy.vercel.app/discord/authorize?redirect_uri=http%3A%2F%2Flocalhost%3A47833%2Fcallback%2F&state=ST")]
    [InlineData("substar", 47834, "https://codebambi-proxy.vercel.app/substar/authorize?state=ST&code_challenge=CH&code_challenge_method=S256")]
    public void AuthorizeUrl_And_Port_MatchWpf(string provider, int port, string expected)
    {
        Assert.Equal(port, LoopbackOAuth.Port(provider));
        Assert.Equal(expected, LoopbackOAuth.AuthorizeUrl(provider, $"http://localhost:{port}/callback/", "ST", provider == "substar" ? "CH" : null));
    }

    [Fact]
    public void Pkce_IsRfc7636S256_AndStateIs32Hex()
    {
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", LoopbackOAuth.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        var verifier = LoopbackOAuth.NewVerifier();
        Assert.Matches("^[A-Za-z0-9_-]{43}$", verifier);
        Assert.NotEqual(verifier, LoopbackOAuth.NewVerifier());
        using var oauth = new LoopbackOAuth(FreePort(), useHttpListener: false);
        Assert.Matches("^[A-F0-9]{32}$", oauth.State);
    }

    private static async Task<(LoopbackOAuth.Callback? Result, Exception? Error, string Url)> Flow(string provider, string query)
    {
        var port = FreePort();
        var opened = new TaskCompletionSource<string>();
        var run = LoopbackOAuth.SignInAsync(provider, u => opened.SetResult(u), CancellationToken.None, port, false, TimeSpan.FromSeconds(10));
        var url = await opened.Task;
        var state = HttpUtilityState(url, port);
        await BrowserGet("127.0.0.1", port, query.Replace("{state}", state));
        try { return (await run, null, url); } catch (Exception ex) { return (null, ex, url); }
    }

    // The state rides in the authorize URL; the proxy round-trips it to the callback.
    private static string HttpUtilityState(string url, int port) =>
        System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["state"]!;

    [Fact]
    public async Task SignIn_Substar_CarriesPkce_AndReturnsTheVerifierForTheExchange()
    {
        var (cb, err, url) = await Flow("substar", "code=C1&state={state}");
        Assert.Null(err);
        var q = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("C1", cb!.Code);
        Assert.Equal(q["state"], cb.State);
        Assert.Equal(q["code_challenge"], LoopbackOAuth.Challenge(cb.Verifier!));
        Assert.StartsWith("https://codebambi-proxy.vercel.app/substar/authorize?state=", url);
    }

    [Fact]
    public async Task SignIn_Patreon_RedirectIsTheCallback_NoVerifier()
    {
        var (cb, err, url) = await Flow("patreon", "code=C2&state={state}");
        Assert.Null(err);
        Assert.Null(cb!.Verifier);
        Assert.Contains("redirect_uri=" + Uri.EscapeDataString(cb.CallbackUrl), url);
    }

    [Theory] // WPF's messages, per provider.
    [InlineData("discord", "error=access_denied&error_description=nah&state={state}", "Discord authorization failed: nah")]
    [InlineData("patreon", "error=access_denied&state={state}", "Patreon authorization failed: Unknown error")]
    [InlineData("substar", "error=access_denied&state={state}", "SubscribeStar authorization failed: access_denied")]
    [InlineData("patreon", "state={state}", "No authorization code received")]
    [InlineData("substar", "state={state}", "SubscribeStar sign-in returned no code. Please try again.")]
    public async Task SignIn_ErrorOrNoCode_ThrowsWpfMessage(string provider, string query, string message)
    {
        var (_, err, _) = await Flow(provider, query);
        Assert.Equal(message, err!.Message);
    }

    [Fact]
    public async Task SignIn_ForgedState_IsRefused()
    {
        var (_, err, _) = await Flow("discord", "code=C3&state=FORGED");
        Assert.IsType<SecurityException>(err);
    }
}
