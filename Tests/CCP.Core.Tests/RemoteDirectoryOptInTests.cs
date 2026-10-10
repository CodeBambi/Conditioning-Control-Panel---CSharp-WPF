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
