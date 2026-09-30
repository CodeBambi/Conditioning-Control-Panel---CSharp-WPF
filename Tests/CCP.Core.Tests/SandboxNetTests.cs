using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// This assembly runs in a CCP_USERDATA_DIR sandbox (RoadmapTestProfile), so touching CorePaths installs
/// the SandboxNet guard. Targets are ".invalid" hosts: if the guard broke, the worst case is a DNS miss
/// (HostNotFound), never a real server - and HostNotFound is exactly what fails these tests.
/// </summary>
public sealed class SandboxNetTests
{
    private const string Remote = "http://sandbox-probe.invalid/v1/ping";

    public SandboxNetTests() => Assert.StartsWith(Path.GetTempPath(), CorePaths.UserData);

    private static bool Refused(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            // Port 0: refused on Linux/macOS, "address not available" on Windows. Never HostNotFound.
            if (ex is SocketException { SocketErrorCode: SocketError.ConnectionRefused or SocketError.AddressNotAvailable }) return true;
        return false;
    }

    public static TheoryData<string> Shapes => new() { "HttpClient", "HttpClientHandler", "SocketsHttpHandler", "ServerClockHandler" };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task EveryHandlerShapeFailsClosedAtTheDeadLoopbackPort(string shape)
    {
        foreach (var url in new[] { Remote, "https://sandbox-probe.invalid/v1/ping" })   // https: the CONNECT tunnel too
        {
            using var client = shape switch
            {
                "HttpClient" => new HttpClient(),
                "HttpClientHandler" => new HttpClient(new HttpClientHandler()),
                "SocketsHttpHandler" => new HttpClient(new SocketsHttpHandler()),
                _ => new HttpClient(new ServerClockHandler()),
            };
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync(url));
            Assert.True(Refused(ex), url + ": " + ex);
        }
    }

    [Fact]
    public async Task UrlSafetyGuardedHandlerOnlyEverDialsTheLoopbackProxy()
    {
        using var client = new HttpClient(UrlSafety.CreateGuardedHandler());
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync(Remote));
        Assert.Contains("127.0.0.1", ex.ToString());
    }

    [Fact]
    public async Task ClientWebSocketFailsClosed()
    {
        using var ws = new ClientWebSocket();
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => ws.ConnectAsync(new Uri("ws://sandbox-probe.invalid/"), CancellationToken.None));
        Assert.True(Refused(ex), ex.ToString());
    }

    [Theory]
    [InlineData("http://sandbox-probe.invalid/", false)]   // routed to the dead proxy, never DNS for the host
    [InlineData("http://127.0.0.1:9/", true)]              // honoured loopback override: dialled directly
    [InlineData("http://localhost:9/", true)]
    public async Task OnlyLoopbackIsDialledDirectly(string url, bool direct)
    {
        DnsEndPoint? dialled = null;
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = (ctx, _) => { dialled = ctx.DnsEndPoint; throw new IOException("stop"); },
        };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync(url));
        Assert.NotNull(dialled);
        var target = new Uri(url);
        Assert.Equal(direct, dialled!.Host == target.Host && dialled.Port == target.Port);
        if (!direct) Assert.Equal(SandboxNet.DeadProxy, new Uri($"http://{dialled.Host}:{dialled.Port}/"));
    }

    [Theory]
    [InlineData("https://codebambi-proxy.vercel.app/", true, false)]
    [InlineData("https://app.cclabs.app/spiral", true, false)]
    [InlineData("http://127.0.0.1:3001/auth", true, true)]
    [InlineData("about:blank", true, true)]
    [InlineData("file:///tmp/page.html", true, true)]
    [InlineData("file://server/share/page.html", true, false)]   // UNC: a network share
    [InlineData("https://codebambi-proxy.vercel.app/", false, true)]   // production unchanged
    public void AllowsNonHttpEgressOnlyToLoopbackInASandbox(string url, bool sandboxed, bool allowed) =>
        Assert.Equal(allowed, SandboxNet.Allows(new Uri(url), sandboxed));

    [Theory]
    [InlineData("http://127.0.0.1:9/", true)]
    [InlineData("http://127.0.0.2:9/", true)]
    [InlineData("http://[::1]:9/", true)]
    [InlineData("http://LOCALHOST:9/", true)]
    [InlineData("http://loopback:9/", false)]   // .NET calls this name loopback; DNS/hosts decide what it is
    [InlineData("http://localhost.example/", false)]
    public void OnlyLiteralLoopbackBypassesTheProxy(string url, bool bypassed) =>
        Assert.Equal(bypassed, HttpClient.DefaultProxy!.IsBypassed(new Uri(url)));

    [Fact]
    public async Task UrlSafetyPreflightSkipsDnsInASandbox() =>
        // A public IP literal passes the pre-flight without any lookup outside a sandbox; inside one it never does.
        Assert.False(await UrlSafety.IsSafePublicHttpsAsync(new Uri("https://93.184.216.34/"), CancellationToken.None));

    [Fact]
    public void TheSandboxInstallsTheGuard()
    {
        Assert.True(SandboxNet.Active);
        Assert.IsType<SandboxNet.Refuser>(HttpClient.DefaultProxy);
        Assert.True(SandboxNet.Allows(null, sandboxed: false));
    }

    private static string RepoRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "ConditioningControlPanel.sln"))) root = root.Parent!;
        return root.FullName;
    }

    private static List<string> Scan(IEnumerable<string> dirs, string pattern, Regex bad, Func<string, bool>? exempt = null) =>
        dirs.Select(d => Path.Combine(RepoRoot(), d)).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, pattern, SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((l, i) => (f, i, l)))
            .Where(x => !Regex.IsMatch(x.l, @"^\s*(//|\*|<!--)") && bad.IsMatch(x.l) && exempt?.Invoke(x.l) != true)
            .Select(x => $"{x.f}:{x.i + 1}: {x.l.Trim()}")
            .ToList();

    /// <summary>Every link/file/folder launch in the Avalonia head goes through ExternalOpener (SandboxNet.Allows,
    /// refusals logged). The elevated installer run (Verb = "runas") is a program, not a link.</summary>
    [Fact]
    public void EveryLaunchGoesThroughExternalOpener()
    {
        // Any UseShellExecute that is not a literal false, any desktop opener binary in any form, any Launcher call.
        var launch = new Regex(@"UseShellExecute\s*=(?!\s*false\b)|\.Launch(Uri|File|FileInfo|DirectoryInfo)Async\b|\b(xdg-open|gio|kde-open\d*|gnome-open)\b|new\s+HyperlinkButton\b");
        var hits = Scan(new[] { "CCP.Avalonia" }, "*.cs", launch, l => l.Contains("\"runas\""))
            .Concat(Scan(new[] { "CCP.Avalonia" }, "*.axaml", new Regex(@"<HyperlinkButton\b")))   // SafeHyperlinkButton instead
            .Where(h => !h.Contains($"{Path.DirectorySeparatorChar}ExternalOpener.cs:"))
            .ToList();
        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }

    /// <summary>The guard covers a client only while it leaves the proxy alone. A client that opts out
    /// (UseProxy = false, its own Proxy) must be routed through SandboxNet first, so this names it.</summary>
    [Fact]
    public void NoProductCodeOptsOutOfTheDefaultProxy()
    {
        var optOut = new Regex(@"\bUseProxy\b|\.Proxy\s*=|\bProxy\s*=\s*new\b|new\s+WebProxy\b");
        Assert.Empty(Scan(new[] { "CCP.Core", "CCP.Avalonia", "ConditioningControlPanel", "CCP.VR" }, "*.cs", optOut));
    }
}
