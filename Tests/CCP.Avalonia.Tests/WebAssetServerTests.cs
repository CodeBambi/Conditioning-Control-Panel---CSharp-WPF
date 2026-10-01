using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WebAssetServerTests
{
    static (WebAssetServer server, string root) Make()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-web-").FullName;
        var root = Path.Combine(dir, "web");
        Directory.CreateDirectory(Path.Combine(root, "game"));
        File.WriteAllText(Path.Combine(root, "game", "index.html"), "<p>hi</p>");
        File.WriteAllText(Path.Combine(dir, "secret.txt"), "outside");
        return (new WebAssetServer(root), root);
    }

    static HttpClient Client(bool cookies = true) =>
        new(new HttpClientHandler { UseCookies = cookies, CookieContainer = new CookieContainer() });

    [Fact]
    public async Task ServesWithToken_AndCookieCarriesLaterLoads()
    {
        using var s = Make().server;
        using var http = Client();
        var first = await http.GetAsync(s.Url("game/index.html"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("text/html", first.Content.Headers.ContentType!.MediaType);
        Assert.Equal("<p>hi</p>", await first.Content.ReadAsStringAsync());
        // A root-relative load from the page carries no query: the cookie must authorise it.
        var second = await http.GetAsync($"http://127.0.0.1:{s.Port}/game/");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task RefusesWithoutToken_WrongToken_AndForeignHost()
    {
        using var s = Make().server;
        using var http = Client(cookies: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync($"http://127.0.0.1:{s.Port}/game/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync($"http://127.0.0.1:{s.Port}/game/index.html?ccp_t=00")).StatusCode);
        var rebind = new HttpRequestMessage(HttpMethod.Get, s.Url("game/index.html"));
        rebind.Headers.Host = "evil.example";
        // HttpListener matches the 127.0.0.1 prefix on the Host header and answers 404 for any other name.
        Assert.Equal(HttpStatusCode.NotFound, (await http.SendAsync(rebind)).StatusCode);
    }

    [Fact]
    public void NeverResolvesOutsideRoot()
    {
        var (s, root) = Make();
        using (s)
        {
            Assert.Equal(Path.Combine(root, "game", "index.html"), s.ResolveFile("/game/index.html"));
            Assert.Null(s.ResolveFile("/../secret.txt"));
            Assert.Null(s.ResolveFile("/%2e%2e/secret.txt"));
            Assert.Null(s.ResolveFile("/game/%2e%2e/%2e%2e/secret.txt"));
            Assert.Null(s.ResolveFile("/" + Path.Combine(Path.GetDirectoryName(root)!, "secret.txt")));
        }
    }

    [Fact]
    public async Task SharedServer_ServesTheShippedIntakePage()
    {
        using var http = Client();
        var res = await http.GetAsync(WebAssetServer.Shared.Url("intake/index.html"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("<html", (await res.Content.ReadAsStringAsync()).ToLowerInvariant());
    }
}
