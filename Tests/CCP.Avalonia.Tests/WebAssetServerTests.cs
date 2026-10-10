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
        Assert.Equal("nosniff", Assert.Single(first.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(first.Headers.GetValues("Referrer-Policy")));
        Assert.StartsWith($"ccp_t_{s.Port}=", Assert.Single(first.Headers.GetValues("Set-Cookie")));
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
    public void RefusesSymlinksThatLeaveTheRoot()
    {
        var (s, root) = Make();
        using (s)
        {
            var outside = Path.Combine(Path.GetDirectoryName(root)!, "secret.txt");
            try { File.CreateSymbolicLink(Path.Combine(root, "leak.txt"), outside); }
            catch (IOException) when (OperatingSystem.IsWindows())
            {
                // Windows creates symlinks only elevated or with Developer Mode on; the refusal
                // under test is the same code path on every OS, so Linux CI keeps the proof.
                Assert.Skip("Windows refused to create a symlink (needs admin or Developer Mode).");
            }
            Directory.CreateSymbolicLink(Path.Combine(root, "up"), Path.GetDirectoryName(root)!);
            File.CreateSymbolicLink(Path.Combine(root, "ok.html"), Path.Combine(root, "game", "index.html"));
            Assert.Null(s.ResolveFile("/leak.txt"));
            Assert.Null(s.ResolveFile("/up/secret.txt"));
            Assert.NotNull(s.ResolveFile("/ok.html"));
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

    /// <summary>IB10: on Windows a library path that differs from the profile path only in letter case
    /// is the same folder, so the "assets root holds the profile" refusal ignores case there.</summary>
    [Theory]
    [InlineData("c:/users/a/", "C:/Users/A/AppData/CCP/", true, true)]
    [InlineData("C:/Users/A/AppData/CCP/", "c:/users/a/appdata/ccp/", true, true)]
    [InlineData("c:/users/a/", "C:/Users/A/AppData/CCP/", false, false)]     // elsewhere a path is its letters
    [InlineData("C:/Users/A/", "C:/Users/A/AppData/CCP/", false, true)]
    [InlineData("C:/Users/A/Pictures/", "C:/Users/A/AppData/CCP/", true, false)]
    [InlineData("C:/Users/A/AppData/CCP/assets/", "C:/Users/A/AppData/CCP/", true, false)]   // a folder INSIDE the profile is fine
    public void TheProfileRefusal_IgnoresLetterCaseOnWindowsOnly(string root, string profile, bool ignoreCase, bool holds) =>
        Assert.Equal(holds, WebAssetServer.FolderHolds(root, profile, ignoreCase));
}
