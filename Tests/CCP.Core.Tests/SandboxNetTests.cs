using System;
using System.IO;
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
            if (ex is SocketException { SocketErrorCode: SocketError.ConnectionRefused }) return true;
        return false;
    }

    public static TheoryData<string> Shapes => new() { "HttpClient", "HttpClientHandler", "SocketsHttpHandler", "ServerClockHandler" };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task EveryHandlerShapeFailsClosedAtTheDeadLoopbackPort(string shape)
    {
        using var client = shape switch
        {
            "HttpClient" => new HttpClient(),
            "HttpClientHandler" => new HttpClient(new HttpClientHandler()),
            "SocketsHttpHandler" => new HttpClient(new SocketsHttpHandler()),
            _ => new HttpClient(new ServerClockHandler()),
        };
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync(Remote));
        Assert.True(Refused(ex), ex.ToString());
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
        if (!direct) Assert.Equal("127.0.0.1", dialled.Host);
    }

    [Theory]
    [InlineData("https://codebambi-proxy.vercel.app/", true, false)]
    [InlineData("https://app.cclabs.app/spiral", true, false)]
    [InlineData("http://127.0.0.1:3001/auth", true, true)]
    [InlineData("about:blank", true, true)]
    [InlineData("file:///tmp/page.html", true, true)]
    [InlineData("https://codebambi-proxy.vercel.app/", false, true)]   // production unchanged
    public void AllowsNonHttpEgressOnlyToLoopbackInASandbox(string url, bool sandboxed, bool allowed) =>
        Assert.Equal(allowed, SandboxNet.Allows(new Uri(url), sandboxed));

    [Fact]
    public void ProductionNeverInstallsTheGuardAndTheSandboxDoes()
    {
        Assert.True(SandboxNet.Active);
        Assert.IsType<SandboxNet.Refuser>(HttpClient.DefaultProxy);
        Assert.True(SandboxNet.Allows(null, sandboxed: false));
    }

    /// <summary>The guard covers a client only while it leaves the proxy alone. A client that opts out
    /// (UseProxy = false, its own Proxy) must be routed through SandboxNet first, so this names it.</summary>
    [Fact]
    public void NoProductCodeOptsOutOfTheDefaultProxy()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "ConditioningControlPanel.sln"))) root = root.Parent!;
        var optOut = new Regex(@"\bUseProxy\b|\.Proxy\s*=|\bProxy\s*=\s*new\b|new\s+WebProxy\b");
        var hits = new[] { "CCP.Core", "CCP.Avalonia", "ConditioningControlPanel", "CCP.VR" }
            .Select(d => Path.Combine(root.FullName, d)).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((l, i) => (f, i, l)))
            .Where(x => optOut.IsMatch(x.l))
            .Select(x => $"{x.f}:{x.i + 1}: {x.l.Trim()}")
            .ToList();
        Assert.Empty(hits);
    }
}
