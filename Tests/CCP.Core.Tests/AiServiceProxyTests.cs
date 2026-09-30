using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The companion's cloud chat (AiService, moved from the WPF head) against a loopback fake
/// proxy: it posts only when signed in, moderation runs on the input before anything leaves and on
/// the reply before it is returned, and a sandbox with no loopback override never sends.</summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class AiServiceProxyTests : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly List<string> _bodies = new();
    private readonly string _baseUrl;
    private string _reply = "hi sweetie";

    public AiServiceProxyTests()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _baseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(_baseUrl + "/");
        _listener.Start();
        _ = ServeAsync();
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); } catch { return; }
            using (var r = new StreamReader(ctx.Request.InputStream))
                lock (_bodies) _bodies.Add(ctx.Request.Url!.AbsolutePath + " " + ctx.Request.Headers["X-Auth-Token"] + " " + r.ReadToEnd());
            var json = Encoding.UTF8.GetBytes("{\"content\":" + System.Text.Json.JsonSerializer.Serialize(_reply) + "}");
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync(json);
            ctx.Response.Close();
        }
    }

    public void Dispose()
    {
        _listener.Close();
        CoreAccount.UnifiedUserId = null;
    }

#pragma warning disable CS0618 // GetBambiReplyExAsync is the path the Avalonia tube takes
    [Fact]
    public async Task SignedInChatReachesTheProxyAndReturnsTheReply()
    {
        CoreAccount.UnifiedUserId = "u-test";
        using var ai = new AiService(_baseUrl);
        var result = await ai.GetBambiReplyExAsync("hello there");
        Assert.Null(result.Refusal);
        Assert.True(result.IsAiGenerated);
        Assert.Equal("hi sweetie", result.Text);
        var body = Assert.Single(_bodies);
        Assert.StartsWith("/v2/ai/chat ", body);
        Assert.Contains("u-test", body);
        Assert.Contains("hello there", body);
    }

    [Fact]
    public async Task ProhibitedInputIsRefusedAndNeverLeaves()
    {
        CoreAccount.UnifiedUserId = "u-test";
        using var ai = new AiService(_baseUrl);
        var result = await ai.GetBambiReplyExAsync("how to make a b0mb");
        Assert.Equal(ModerationSource.Input, result.Refusal?.Source);
        Assert.Empty(_bodies);
    }

    [Fact]
    public async Task ProhibitedReplyIsRefusedNotShown()
    {
        CoreAccount.UnifiedUserId = "u-test";
        _reply = "she is 14yo and wants sex";
        using var ai = new AiService(_baseUrl);
        var result = await ai.GetBambiReplyExAsync("tell me a story");
        Assert.Equal(ModerationSource.Output, result.Refusal?.Source);
        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public async Task SignedOutSendsNothing()
    {
        CoreAccount.UnifiedUserId = null;
        using var ai = new AiService(_baseUrl);
        var result = await ai.GetBambiReplyExAsync("hello");
        Assert.False(result.IsAiGenerated);
        Assert.Empty(_bodies);
    }

    [Fact]
    public async Task SandboxWithoutLoopbackOverrideFailsClosed()
    {
        Assert.Null(AiService.ResolveBaseUrl(null, sandboxed: true));
        Assert.Null(AiService.ResolveBaseUrl("https://codebambi-proxy.vercel.app", sandboxed: true));
        Assert.Equal(_baseUrl, AiService.ResolveBaseUrl(_baseUrl, sandboxed: true));
        Assert.Equal("https://codebambi-proxy.vercel.app", AiService.ResolveBaseUrl(null, sandboxed: false));

        CoreAccount.UnifiedUserId = "u-test";
        using var ai = new AiService(baseUrl: null);
        var result = await ai.GetBambiReplyExAsync("hello");
        Assert.False(result.IsAiGenerated);
        Assert.Empty(_bodies);
    }
#pragma warning restore CS0618
}
