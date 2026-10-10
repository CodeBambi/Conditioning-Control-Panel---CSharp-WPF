using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.PieceByPiece;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The pure half of the chess host (WPF PieceByPieceHostService 7.1.5): the net whitelist, the
/// identity and settings frames, the library reservoir, the friend / Lobby intents and the watchdog.</summary>
public sealed class PbpHostRulesTests
{
    private sealed class Capture : HttpMessageHandler
    {
        public HttpRequestMessage? Seen;
        public string? Body;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public bool Throw;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Throw) throw new HttpRequestException("offline");
            Seen = request;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status) { Content = new StringContent("{\"ok\":true}") };
        }
    }

    [Theory]
    [InlineData("/v2/goon/room", "GET", "forbidden_path")]
    [InlineData("/admin/get-key", "POST", "forbidden_path")]
    [InlineData("https://evil.example/v2/pbp/x", "GET", "forbidden_path")]
    [InlineData("/v2/pbpx", "GET", "forbidden_path")]
    [InlineData("", "GET", "forbidden_path")]
    [InlineData("/v2/pbp/lobby", "DELETE", "forbidden_method")]
    [InlineData("/v2/pbp/lobby", "PUT", "forbidden_method")]
    public void Net_OnlyTheGamesOwnRoutes_AndOnlyGetAndPost(string path, string method, string refusal)
    {
        var call = PbpHostRules.ReadNet(new JObject { ["id"] = "n1", ["method"] = method, ["path"] = path });
        Assert.Equal(refusal, call.Refusal);
    }

    [Fact]
    public async Task Net_RefusedCall_NeverReachesTheNetwork_AndAnswersStatusZero()
    {
        var h = new Capture();
        var call = PbpHostRules.ReadNet(new JObject { ["id"] = "n1", ["method"] = "POST", ["path"] = "/v2/stakes/offer" });
        var (status, body) = await PbpHostRules.SendAsync(new HttpClient(h), "https://example.test", call, "tok", "7.1.5");
        Assert.Equal(0, status);
        Assert.Equal("forbidden_path", body);
        Assert.Null(h.Seen);
    }

    [Fact]
    public async Task Net_AttachesTheTokenOnTheHostSide_AndSendsAnObjectBodyCompact()
    {
        var h = new Capture();
        var call = PbpHostRules.ReadNet(new JObject
        {
            ["id"] = "n7", ["method"] = "post", ["path"] = "/v2/pbp/lobby/enter",
            ["body"] = new JObject { ["unified_id"] = "u_1", ["mode"] = "quick" },
        });
        Assert.Null(call.Refusal);
        Assert.Equal("POST", call.Method);
        var (status, body) = await PbpHostRules.SendAsync(new HttpClient(h), "https://example.test", call, "tok", "7.1.5");
        Assert.Equal(200, status);
        Assert.Equal("{\"ok\":true}", body);
        Assert.Equal("https://example.test/v2/pbp/lobby/enter", h.Seen!.RequestUri!.ToString());
        Assert.Equal("tok", h.Seen.Headers.GetValues("X-Auth-Token").Single());
        Assert.Equal("7.1.5", h.Seen.Headers.GetValues("X-Client-Version").Single());
        Assert.Equal("{\"unified_id\":\"u_1\",\"mode\":\"quick\"}", h.Body);
    }

    [Fact]
    public async Task Net_NoToken_SendsNoHeader_AndAGetCarriesNoBody()
    {
        var h = new Capture();
        var call = PbpHostRules.ReadNet(new JObject { ["id"] = "n2", ["path"] = "/v2/pbp/lobby?unified_id=u_1", ["body"] = null });
        await PbpHostRules.SendAsync(new HttpClient(h), "https://example.test", call, "", "7.1.5");
        Assert.Equal(HttpMethod.Get, h.Seen!.Method);
        Assert.False(h.Seen.Headers.Contains("X-Auth-Token"));
        Assert.Null(h.Body);
    }

    [Fact]
    public async Task Net_AFaultOrASandboxWithNoServer_IsStatusZero_NeverAThrow()
    {
        var call = PbpHostRules.ReadNet(new JObject { ["id"] = "n3", ["path"] = "/v2/pbp/lobby" });
        var offline = await PbpHostRules.SendAsync(new HttpClient(new Capture { Throw = true }), "https://example.test", call, "tok", "7.1.5");
        Assert.Equal((0, ""), offline);
        var h = new Capture();
        var sandbox = await PbpHostRules.SendAsync(new HttpClient(h), null, call, "tok", "7.1.5");
        Assert.Equal((0, ""), sandbox);
        Assert.Null(h.Seen);
    }

    [Fact]
    public void NetResult_IsTheShapeThePageResolvesOn()
    {
        var r = PbpHostRules.NetResult("n9", 409, "{\"reason\":\"full\"}");
        Assert.Equal("pbp:net-result", (string?)r["type"]);
        Assert.Equal("n9", (string?)r["id"]);
        Assert.Equal(409, (int)r["status"]!);
        Assert.Equal("{\"reason\":\"full\"}", (string?)r["body"]);
    }

    [Fact]
    public void Identity_OnlineNeedsBothTheAccountAndTheToken_AndRoutesThroughTheHost()
    {
        var full = PbpHostRules.IdentityFrame("u_1", "tok", "Mort", "7.1.5");
        Assert.Equal("pbp:identity", (string?)full["type"]);
        Assert.Equal("u_1", (string?)full["unifiedId"]);
        Assert.Equal("Mort", (string?)full["displayName"]);
        Assert.Equal("7.1.5", (string?)full["appVersion"]);
        Assert.True((bool)full["online"]!);
        Assert.Equal(PbpHostRules.ProxyBaseUrl, (string?)full["net"]!["serverBase"]);
        Assert.Equal("tok", (string?)full["net"]!["authToken"]);
        Assert.True((bool)full["net"]!["viaHost"]!);

        // Solo play stays free with no account: an empty identity is a normal state, not a failure.
        var solo = PbpHostRules.IdentityFrame(null, null, " ", "7.1.5");
        Assert.False((bool)solo["online"]!);
        Assert.Equal("", (string?)solo["unifiedId"]);
        Assert.Equal("", (string?)solo["net"]!["authToken"]);
        Assert.Equal("Player", (string?)solo["displayName"]);
        Assert.False((bool)PbpHostRules.IdentityFrame("u_1", "", "x", "7")["online"]!);
        Assert.False((bool)PbpHostRules.IdentityFrame("", "tok", "x", "7")["online"]!);
    }

    [Fact]
    public void Identity_CarriesNoRating_SoTheHostCanNeverHandOverAnOpponentsIq()
    {
        var names = PbpHostRules.IdentityFrame("u_1", "tok", "Mort", "7.1.5").Properties().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "type", "unifiedId", "displayName", "appVersion", "online", "net" }, names);
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(-4, 15)]
    [InlineData(5, 10)]
    [InlineData(20, 20)]
    [InlineData(600, 30)]
    public void VideoHold_IsThePlayersOwnFloor_Clamped(int configured, int expected)
        => Assert.Equal(expected, PbpHostRules.VideoHoldSec(configured));

    [Fact]
    public void Settings_Frame_IsTheThreeFieldsThePageReads()
    {
        var f = PbpHostRules.SettingsFrame(0, true, new[] { "http://x/a.mp3" });
        Assert.Equal("pbp:settings", (string?)f["type"]);
        Assert.Equal(15, (int)f["videoHoldSec"]!);
        Assert.True((bool)f["reducedMotion"]!);
        Assert.Equal("http://x/a.mp3", (string?)f["whispers"]![0]);
    }

    [Fact]
    public void MediaRequest_DefaultsAndClamps()
    {
        Assert.Equal(24, PbpHostRules.ReadCount(new JObject()));
        Assert.Equal(1, PbpHostRules.ReadCount(new JObject { ["count"] = -3 }));
        Assert.Equal(PbpHostRules.MaxMediaPerKind, PbpHostRules.ReadCount(new JObject { ["count"] = 100000 }));
        Assert.Equal(24, PbpHostRules.ReadCount(new JObject { ["count"] = "lots" }));
        Assert.Equal(3, PbpHostRules.ReadKinds(null).Count);
        Assert.Equal(3, PbpHostRules.ReadKinds(new JArray(1, 2)).Count);
        Assert.Equal(new[] { "gif" }, PbpHostRules.ReadKinds(new JArray(" GIF ")).Select(k => k.ToLowerInvariant()));
    }

    [Fact]
    public void Sample_SplitsGifsOutOfImages_HonoursKinds_AndNeverPassesTheCount()
    {
        var pool = new List<(string, string, bool)>();
        for (int i = 0; i < 40; i++) pool.Add(($"a{i}.jpg", $"u/a{i}.jpg", true));
        for (int i = 0; i < 40; i++) pool.Add(($"g{i}.GIF", $"u/g{i}.gif", true));
        for (int i = 0; i < 40; i++) pool.Add(($"v{i}.mp4", $"u/v{i}.mp4", false));
        var all = PbpHostRules.SampleMedia(pool, PbpHostRules.ReadKinds(null), 8, new Random(1));
        Assert.Equal(8, all.Images.Length);
        Assert.Equal(8, all.Gifs.Length);
        Assert.Equal(8, all.Videos.Length);
        Assert.All(all.Images, u => Assert.EndsWith(".jpg", u));
        Assert.All(all.Gifs, u => Assert.EndsWith(".gif", u));
        Assert.All(all.Videos, u => Assert.EndsWith(".mp4", u));
        Assert.Equal(8, all.Images.Distinct().Count());

        var onlyVideo = PbpHostRules.SampleMedia(pool, PbpHostRules.ReadKinds(new JArray("video")), 8, new Random(1));
        Assert.Empty(onlyVideo.Images);
        Assert.Empty(onlyVideo.Gifs);
        Assert.Equal(8, onlyVideo.Videos.Length);

        // A fresh install has no media at all, and the page must survive that.
        var empty = PbpHostRules.SampleMedia(Array.Empty<(string, string, bool)>(), PbpHostRules.ReadKinds(null), 8, new Random(1));
        var frame = PbpHostRules.MediaFrame(empty);
        Assert.Equal("pbp:media", (string?)frame["type"]);
        Assert.Empty((JArray)frame["images"]!);
        Assert.Empty((JArray)frame["gifs"]!);
        Assert.Empty((JArray)frame["videos"]!);
    }

    [Theory]
    [InlineData("p_abc123", true)]
    [InlineData("p_a-b_C9", true)]
    [InlineData("p_", false)]
    [InlineData("c_0123456789abcdef", false)]
    [InlineData("p_has space", false)]
    [InlineData("p_<script>", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TableId_IsTheServersOpaquePId(string? id, bool ok)
        => Assert.Equal(ok, PbpHostRules.IsTableId(id));

    [Fact]
    public void TableId_RefusesAnOverlongOne()
        => Assert.False(PbpHostRules.IsTableId("p_" + new string('a', 63)));

    [Fact]
    public void Intents_AreThePbpFriendFramesThePageReads()
    {
        Assert.Equal("{\"type\":\"pbp:friend\",\"mode\":\"challenge\",\"friendId\":\"u_2\"}",
            PbpHostRules.ChallengeIntent("u_2").ToString(Newtonsoft.Json.Formatting.None));
        Assert.Equal("{\"type\":\"pbp:friend\",\"mode\":\"accept\",\"challengeId\":\"c_1\"}",
            PbpHostRules.AcceptIntent("c_1").ToString(Newtonsoft.Json.Formatting.None));
        Assert.Equal("{\"type\":\"pbp:friend\",\"mode\":\"join\",\"target\":\"p_9\"}",
            PbpHostRules.JoinIntent("p_9").ToString(Newtonsoft.Json.Formatting.None));
        Assert.Equal("{\"type\":\"pbp:friend\",\"mode\":\"host\"}",
            PbpHostRules.HostIntent().ToString(Newtonsoft.Json.Formatting.None));
    }

    [Fact]
    public void Heartbeat_SleepsUntilReady_PingsOnce_ThenCloses()
    {
        Assert.Equal(PbpHostRules.WatchStep.Idle, PbpHostRules.HeartbeatStep(false, 999, false));
        Assert.Equal(PbpHostRules.WatchStep.Ok, PbpHostRules.HeartbeatStep(true, 20, false));
        Assert.Equal(PbpHostRules.WatchStep.Ping, PbpHostRules.HeartbeatStep(true, 20.5, false));
        Assert.Equal(PbpHostRules.WatchStep.Close, PbpHostRules.HeartbeatStep(true, 26, true));
    }

    [Fact]
    public void BootDeadline_IsProgressAware_AndNeverFiresOnAReadyPage()
    {
        Assert.False(PbpHostRules.BootTimedOut(false, TimeSpan.FromSeconds(44)));
        Assert.True(PbpHostRules.BootTimedOut(false, TimeSpan.FromSeconds(45)));
        Assert.False(PbpHostRules.BootTimedOut(true, TimeSpan.FromHours(1)));
    }
}
