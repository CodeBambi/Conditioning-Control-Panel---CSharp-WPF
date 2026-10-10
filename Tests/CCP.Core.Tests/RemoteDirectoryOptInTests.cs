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

/// <summary>The directory opt-in on Core RemoteRelay (WPF RemoteControlService.OptInToDirectoryAsync :247 and
/// RepublishDirectoryIfOptedInAsync): a fake relay, no network.</summary>
[Collection(SessionStatics.Name)]
public sealed class RemoteDirectoryOptInTests
{
    private sealed class FakeRelay : HttpMessageHandler
    {
        public readonly List<(string Path, JObject Body)> Seen = new();
        public string Poll = "{}";
        public HttpStatusCode OptIn = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            var seen = JObject.Parse(await r.Content!.ReadAsStringAsync(ct));
            lock (Seen) Seen.Add((path, seen));
            return path switch
            {
                "/v2/remote/start" => Json(HttpStatusCode.OK, "{\"code\":\"ABC123\"}"),
                "/v2/remote/poll" => Json(HttpStatusCode.OK, Poll),
                "/v2/directory/opt-in" => Json(OptIn, "{}"),
                _ => Json(HttpStatusCode.OK, "{}"),
            };
        }

        public List<JObject> OptIns() { lock (Seen) return Seen.Where(s => s.Path == "/v2/directory/opt-in").Select(s => s.Body).ToList(); }
    }

    private static HttpResponseMessage Json(HttpStatusCode s, string body) => new(s) { Content = new StringContent(body) };

    private static RemoteRelay Relay(FakeRelay f) =>
        new(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => { }, f) { AutoPoll = false };

    [Fact]
    public async Task No_session_lists_nobody()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        Assert.False(await r.OptInToDirectoryAsync(new List<string> { "trance" }, "hi"));
        Assert.False(r.DirectoryOptedIn);
        Assert.Empty(f.Seen);
    }

    [Fact]
    public async Task Opt_in_posts_the_code_pin_tags_and_status_and_a_refusal_lists_nobody()
    {
        var f = new FakeRelay { OptIn = HttpStatusCode.TooManyRequests };
        using var r = Relay(f);
        await r.StartAsync("light");
        Assert.False(await r.OptInToDirectoryAsync(new List<string> { "trance" }, "hi"));
        Assert.False(r.DirectoryOptedIn);

        f.OptIn = HttpStatusCode.OK;
        Assert.True(await r.OptInToDirectoryAsync(new List<string> { "trance", "soft_only" }, "hi"));
        Assert.True(r.DirectoryOptedIn);
        var body = f.OptIns().Last();
        Assert.Equal("uid-1", (string?)body["unified_id"]);
        Assert.Equal("ABC123", (string?)body["code"]);
        Assert.Equal(r.ConnectPin, (string?)body["pin"]);
        Assert.Equal(new[] { "trance", "soft_only" }, body["tags"]!.Select(t => (string?)t).ToArray());
        Assert.Equal("hi", (string?)body["status_text"]);
    }

    [Fact]
    public async Task A_controller_leaving_puts_the_listing_back_and_a_stop_forgets_it()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        await r.StartAsync("light");
        Assert.True(await r.OptInToDirectoryAsync(new List<string> { "drone" }, "status"));
        Assert.Single(f.OptIns());

        f.Poll = "{\"controller_connected\":true}";
        await r.PollOnceAsync();
        Assert.True(r.ControllerConnected);
        Assert.Single(f.OptIns());   // joining publishes nothing

        f.Poll = "{\"controller_connected\":false}";
        await r.PollOnceAsync();
        for (var i = 0; i < 200 && f.OptIns().Count < 2; i++) await Task.Delay(10);
        var again = f.OptIns();
        Assert.Equal(2, again.Count);   // the same payload, re-published
        Assert.Equal("drone", (string?)again[1]["tags"]![0]);
        Assert.Equal("status", (string?)again[1]["status_text"]);

        await r.StopAsync();
        Assert.False(r.DirectoryOptedIn);
        await r.RepublishDirectoryIfOptedInAsync();
        Assert.Equal(2, f.OptIns().Count);
    }

    [Fact]
    public async Task A_session_that_never_opted_in_publishes_nothing_when_the_controller_leaves()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        await r.StartAsync("light");
        f.Poll = "{\"controller_connected\":true}";
        await r.PollOnceAsync();
        f.Poll = "{\"controller_connected\":false}";
        await r.PollOnceAsync();
        await Task.Delay(50);
        Assert.Empty(f.OptIns());
    }
}

/// <summary>The controller side of the directory: WPF AvailableSubjectsService.TryClaimAsync, on Core
/// RemoteDirectoryApi. The session url carries a PIN in its fragment, so only a plain web address comes back.</summary>
public sealed class RemoteDirectoryClaimTests
{
    private sealed class Fake : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "{}";
        public HttpRequestMessage? Last;
        public string? LastBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Last = r;
            LastBody = await r.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }

    private static ConditioningControlPanel.Services.Lobby.RemoteDirectoryApi Api(Fake f, bool signedIn = true) =>
        new(new HttpClient(f), () => signedIn ? ("me-1", "tok") : null, "http://127.0.0.1:1");

    [Fact]
    public async Task A_claim_posts_the_subject_with_the_callers_headers_and_returns_the_session_url()
    {
        var f = new Fake { Body = "{\"session_url\":\"https://cclabs.app/remote/#code=ABC123&pin=0420\"}" };
        var (url, lost) = await Api(f).ClaimAsync("sub-9");
        Assert.Equal("https://cclabs.app/remote/#code=ABC123&pin=0420", url);
        Assert.False(lost);
        Assert.Equal("/v2/directory/claim", f.Last!.RequestUri!.AbsolutePath);
        Assert.Equal("tok", f.Last.Headers.GetValues("X-Auth-Token").Single());
        Assert.Equal("me-1", f.Last.Headers.GetValues("X-Caller-Unified-Id").Single());
        Assert.Equal("sub-9", (string?)JObject.Parse(f.LastBody!)["unified_id"]);
    }

    [Fact]
    public async Task Someone_claiming_first_is_a_lost_race_and_a_refusal_or_no_account_is_nothing()
    {
        var f = new Fake { Status = HttpStatusCode.Conflict };
        Assert.Equal((null, true), await Api(f).ClaimAsync("sub-9"));
        f.Status = HttpStatusCode.TooManyRequests;
        Assert.Equal((null, false), await Api(f).ClaimAsync("sub-9"));
        f.Last = null;
        Assert.Equal((null, false), await Api(f, signedIn: false).ClaimAsync("sub-9"));
        Assert.Equal((null, false), await Api(f).ClaimAsync(""));
        Assert.Null(f.Last);   // neither asked the server
    }

    [Theory]
    [InlineData("https://cclabs.app/remote/#code=A&pin=1", true)]
    [InlineData("http://127.0.0.1:9000/remote/#code=A", true)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("calc.exe", false)]
    [InlineData("https://cclabs.app/\" & calc", false)]
    [InlineData("https://cclabs.app/\nfoo", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_plain_web_address_reaches_the_browser(string? url, bool ok) =>
        Assert.Equal(ok, ConditioningControlPanel.Services.Lobby.RemoteDirectoryApi.SafeSessionUrl(url) != null);
}

/// <summary>The Mind Wipe verbs (WPF RemoteControlService.cs:1457-1470) on Core RemoteCommands.</summary>
[Collection(SessionStatics.Name)]
public sealed class RemoteMindWipeVerbTests
{
    [Fact]
    public void Mind_wipe_verbs_reach_the_player_and_both_stop_paths_stop_it()
    {
        var saved = (ConditioningControlPanel.CoreMindWipe.TriggerOnceProvider, ConditioningControlPanel.CoreMindWipe.StartProvider,
            ConditioningControlPanel.CoreMindWipe.StopProvider, ConditioningControlPanel.CoreMindWipe.ClipCountProvider);
        var s = ConditioningControlPanel.CoreSettings.Current;
        var (freq, vol) = (s.MindWipeFrequency, s.MindWipeVolume);
        try
        {
            (ConditioningControlPanel.CoreMindWipe.TriggerOnceProvider, ConditioningControlPanel.CoreMindWipe.StartProvider) = (null, null);
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("trigger_mind_wipe", null));
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("start_mind_wipe", null));

            int once = 0, stops = 0, clips = 0;
            (double F, double V)? started = null;
            ConditioningControlPanel.CoreMindWipe.TriggerOnceProvider = () => once++;
            ConditioningControlPanel.CoreMindWipe.StartProvider = (f, v) => started = (f, v);
            ConditioningControlPanel.CoreMindWipe.StopProvider = () => stops++;
            ConditioningControlPanel.CoreMindWipe.ClipCountProvider = () => clips;

            Assert.Equal("no clips", RemoteCommands.Execute("trigger_mind_wipe", null));   // the controller is told
            Assert.Equal(0, once);
            clips = 3;
            Assert.Null(RemoteCommands.Execute("trigger_mind_wipe", null));
            Assert.Equal(1, once);

            (s.MindWipeFrequency, s.MindWipeVolume) = (6, 50);
            Assert.Null(RemoteCommands.Execute("start_mind_wipe", null));
            Assert.Equal((6.0, 0.5), started);   // the subject's own frequency and volume, never the controller's

            Assert.Null(RemoteCommands.Execute("stop_mind_wipe", null));
            Assert.Equal(1, stops);
            RemoteCommands.StopEffects(force: false);   // the controller left / the session ended
            Assert.Equal(2, stops);
        }
        finally
        {
            (ConditioningControlPanel.CoreMindWipe.TriggerOnceProvider, ConditioningControlPanel.CoreMindWipe.StartProvider,
                ConditioningControlPanel.CoreMindWipe.StopProvider, ConditioningControlPanel.CoreMindWipe.ClipCountProvider) = saved;
            (s.MindWipeFrequency, s.MindWipeVolume) = (freq, vol);
        }
    }
}
