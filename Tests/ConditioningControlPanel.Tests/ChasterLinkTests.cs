using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaster;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The link flow against its real loopback listener: the browser's bounce links the account, a
/// knock with the wrong state cannot end a real attempt, a refusal links nothing, and only one
/// flow runs at a time. The "browser" here is an HttpClient; the broker is a fake handler.
/// </summary>
public class ChasterLinkTests : IDisposable
{
    private sealed class MemoryStore : IChasterTokenStore
    {
        public ChasterStoredTokens? Tokens;
        public ChasterStoredTokens? Read() => Tokens;
        public void Write(ChasterStoredTokens tokens) => Tokens = tokens;
        public void Clear() => Tokens = null;
    }

    private sealed class Broker : HttpMessageHandler
    {
        public readonly List<string> Bodies = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Bodies.Add(r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"AT\",\"refresh_token\":\"RT\",\"expires_in\":300,\"refresh_expires_in\":0}"),
            };
        }
    }

    private static readonly string Callback = $"http://localhost:{ChasterService.LoopbackPort}/callback/";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-chaster-link-" + Guid.NewGuid().ToString("N"));
    private readonly Broker _broker = new();
    private readonly MemoryStore _store = new();
    private readonly HttpClient _browser = new();
    private readonly ChasterService _service;

    public ChasterLinkTests()
    {
        Directory.CreateDirectory(_dir);
        _service = new ChasterService(new ChasterClient(_broker), _store, Path.Combine(_dir, "tab.json"), () => ChasterOptions.Off);
    }

    public void Dispose()
    {
        _service.Dispose();
        _browser.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { } // swallow: temp dir, best effort
    }

    // The listener is up and the consent url is out before LinkAsync first yields.
    private (Task<LinkOutcome> Flow, string State) Start()
    {
        string? url = null;
        var flow = _service.LinkAsync(u => url = u);
        Assert.NotNull(url);
        return (flow, url![(url.IndexOf("state=", StringComparison.Ordinal) + 6)..]);
    }

    [Fact]
    public async Task The_bounce_with_the_right_state_links_the_account()
    {
        var (flow, state) = Start();
        var changed = 0;
        _service.LinkChanged += () => changed++;

        var page = await _browser.GetStringAsync(Callback + "?state=" + state);

        Assert.Equal(LinkOutcome.Linked, await flow);
        Assert.Contains("Linked", page);
        Assert.True(_service.IsLinked);
        Assert.Equal(1, changed);
        Assert.Equal(state, JObject.Parse(Assert.Single(_broker.Bodies))["state"]!.Value<string>());
        Assert.False(_service.IsLinking);
    }

    [Fact]
    public async Task A_knock_with_the_wrong_state_cannot_end_a_real_attempt()
    {
        var (flow, state) = Start();

        var stray = await _browser.GetStringAsync(Callback + "?state=" + new string('0', 32));
        Assert.Contains("Not linked", stray);
        Assert.False(flow.IsCompleted);
        Assert.Empty(_broker.Bodies);

        await _browser.GetStringAsync(Callback + "?state=" + state);
        Assert.Equal(LinkOutcome.Linked, await flow);
    }

    [Fact]
    public async Task A_refusal_on_the_consent_page_links_nothing()
    {
        var (flow, state) = Start();

        await _browser.GetStringAsync(Callback + "?error=denied&state=" + state);

        Assert.Equal(LinkOutcome.Denied, await flow);
        Assert.False(_service.IsLinked);
        Assert.Empty(_broker.Bodies);
    }

    [Fact]
    public async Task One_flow_at_a_time_and_a_cancel_reads_as_a_cancel()
    {
        var (flow, _) = Start();

        Assert.Equal(LinkOutcome.Failed, await _service.LinkAsync(_ => { }));
        Assert.True(_service.IsLinking);

        _service.CancelLink();
        Assert.Equal(LinkOutcome.Cancelled, await flow);
        Assert.False(_service.IsLinked);
    }
}
