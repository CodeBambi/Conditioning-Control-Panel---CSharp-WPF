using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Goon Game host, one test per frame family (WPF GoonHostService.OnPageMessage): page
/// frame in, expected frame or state out. Process-wide state (the Core host, settings), so alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class GoonHostTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static (GameWindow W, List<JObject> Posted) Open()
    {
        EnsureApp();
        GoonHostService.DetachWindow();
        var w = new GameWindow(GameWindow.Games["goon"]);
        var posted = new List<JObject>();
        w.Posted += json => { lock (posted) posted.Add(JObject.Parse(json)); };
        w.Show();
        return (w, posted);
    }

    private static List<JObject> Of(List<JObject> posted, string type)
    {
        lock (posted) return posted.Where(p => (string?)p["type"] == type).ToList();
    }

    [Fact]
    public async Task Ready_GetsInit_Manifest_AndTheRealWindowState()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var (w, posted) = Open();
            try
            {
                w.HandleMessage("{\"type\":\"ready\",\"protocol\":1}");
                Assert.True(GoonHostService.IsActive);
                var types = posted.Select(p => (string?)p["type"]).ToList();
                Assert.Equal(new[] { "init", "manifest", "fullscreen" }, types);
                var init = posted[0];
                Assert.Equal(1, (int)init["protocol"]!);
                Assert.True((bool)init["net"]!["viaHost"]!);
                Assert.NotNull(init["caps"]!["canHost"]);
                Assert.NotNull(init["caps"]!["canJoin"]);
                Assert.False((bool)init["caps"]!["mediaTransfer"]!);   // the transfer cache is not ported
                Assert.NotNull(init["consent"]!["payloadMinGapMs"]);
                Assert.NotNull(init["discord"]!["avatarState"]);
                Assert.Empty((JArray)posted[1]["received"]!);          // the inbox is empty at boot
                Assert.False((bool)posted[2]["on"]!);
            }
            finally { w.Close(); }
            Assert.False(GoonHostService.IsActive);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task NetPost_IsAnsweredOnce_WithTheSameId()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (w, posted) = Open();
            try
            {
                var asked = new List<(string? Path, string Body)>();
                w.GoonNetPost = (path, body) => { lock (asked) asked.Add((path, body)); return Task.FromResult((200, "{\"code\":\"ABCD\"}")); };
                w.HandleMessage("{\"type\":\"ready\"}");
                w.HandleMessage("{\"type\":\"net-post\",\"id\":\"n7\",\"path\":\"/v2/goon/invite\",\"body\":{\"unified_id\":\"u\"}}");
                for (int i = 0; i < 100 && Of(posted, "net-post-result").Count == 0; i++) await Task.Delay(20);
                var r = Assert.Single(Of(posted, "net-post-result"));
                Assert.Equal("n7", (string?)r["id"]);
                Assert.Equal(200, (int)r["status"]!);
                Assert.Equal("{\"code\":\"ABCD\"}", (string?)r["body"]);
                Assert.Equal(("/v2/goon/invite", "{\"unified_id\":\"u\"}"), Assert.Single(asked));
            }
            finally { w.Close(); }
        });
    }

    [Fact]
    public async Task NetPost_OffTheWhitelist_FailsClosed()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (w, posted) = Open();
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                w.HandleMessage("{\"type\":\"net-post\",\"id\":\"x\",\"path\":\"/admin/update-key\",\"body\":\"{}\"}");
                for (int i = 0; i < 100 && Of(posted, "net-post-result").Count == 0; i++) await Task.Delay(20);
                var r = Assert.Single(Of(posted, "net-post-result"));
                Assert.Equal(0, (int)r["status"]!);
                Assert.Equal("forbidden_path", (string?)r["body"]);
            }
            finally { w.Close(); }
        });
    }

    [Fact]
    public async Task Watchdog_PingsAQuietPage_ReloadsOnce_ThenCloses_AndStandsDownOnExit()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var (w, posted) = Open();
            Assert.Equal("idle", w.CheckGoonWatch(DateTime.UtcNow.AddMinutes(5)));   // never ready
            w.HandleMessage("{\"type\":\"ready\"}");
            w.HandleMessage("{\"type\":\"heartbeat\",\"t\":1,\"paint\":10,\"vis\":\"visible\"}");
            Assert.Equal("ok", w.CheckGoonWatch(DateTime.UtcNow.AddSeconds(5)));
            Assert.Equal("ping", w.CheckGoonWatch(DateTime.UtcNow.AddSeconds(10)));
            Assert.Single(Of(posted, "ping"));
            Assert.Equal("recovered", w.CheckGoonWatch(DateTime.UtcNow.AddSeconds(15).AddSeconds(8)));
            w.HandleMessage("{\"type\":\"ready\"}");
            w.HandleMessage("{\"type\":\"pong\"}");
            Assert.Equal("closed", w.CheckGoonWatch(DateTime.UtcNow.AddSeconds(30)));
            Assert.False(w.IsVisible);

            // A page winding down on its own exit is never "recovered" into a relaunch.
            var (w2, _) = Open();
            try
            {
                w2.HandleMessage("{\"type\":\"ready\"}");
                w2.HandleMessage("{\"type\":\"heartbeat\"}");
                w2.HandleMessage("{\"type\":\"exit\"}");
                Assert.Equal("idle", w2.CheckGoonWatch(DateTime.UtcNow.AddMinutes(2)));
            }
            finally { w2.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task PaintStall_AFrozenPictureWithLiveBeats_ReloadsOnce_ButAHiddenPageNever()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var (w, _) = Open();
            try
            {
                var t0 = DateTime.UtcNow;
                var now = t0;
                w.GoonClock = () => now;
                w.HandleMessage("{\"type\":\"ready\"}");
                w.CheckGoonWatch(now);
                // Hidden: beats arrive, the counter stands still, and that is a legitimate reason not to paint.
                for (int s = 5; s <= 60; s += 5)
                {
                    now = t0.AddSeconds(s);
                    w.HandleMessage("{\"type\":\"heartbeat\",\"paint\":5,\"vis\":\"hidden\"}");
                    Assert.Equal("ok", w.CheckGoonWatch(now));
                }
                // Visible, beats alive, the picture frozen: healthy up to 20 s, then the one reload.
                var seen = new List<string>();
                for (int s = 65; s <= 90; s += 5)
                {
                    now = t0.AddSeconds(s);
                    w.HandleMessage("{\"type\":\"heartbeat\",\"paint\":5,\"vis\":\"visible\"}");
                    seen.Add(w.CheckGoonWatch(now));
                }
                Assert.Equal(new[] { "ok", "ok", "ok", "ok", "ok", "recovered" }, seen);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task RoomCode_AndHostBusy_ReachTheInviteSeam()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var (w, posted) = Open();
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                int busy = 0;
                void OnBusy() => busy++;
                GoonHostService.HostBusy += OnBusy;
                try
                {
                    w.HandleMessage("{\"type\":\"room-code\",\"code\":\"ab-cd\"}");
                    Assert.Equal("ABCD", GoonHostService.RoomCode);
                    w.HandleMessage("{\"type\":\"host-busy\"}");
                    Assert.Equal(1, busy);
                    // A live window takes a join or a host ask as a frame (WPF Launch / LaunchToHost).
                    GoonHostService.Launch("wxyz12");
                    GoonHostService.LaunchToHost();
                    Assert.Equal("WXYZ12", (string?)Assert.Single(Of(posted, "join-code"))["code"]);
                    Assert.Single(Of(posted, "host-now"));
                    w.HandleMessage("{\"type\":\"room-code\",\"code\":\"\"}");
                    Assert.Null(GoonHostService.RoomCode);
                }
                finally { GoonHostService.HostBusy -= OnBusy; }
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Fullscreen_IsEchoed_AndRemembered()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            bool before = CoreSettings.Current.GoonFullscreen;
            var (w, posted) = Open();
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
                var echo = Of(posted, "fullscreen").Last();
                Assert.Equal(w.IsHostFullscreen, (bool)echo["on"]!);
                Assert.Equal(w.IsHostFullscreen, CoreSettings.Current.GoonFullscreen);
                w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":false}");
                Assert.False(CoreSettings.Current.GoonFullscreen);
            }
            finally
            {
                w.Close();
                CoreSettings.Current.GoonFullscreen = before;
                CoreSettings.SaveImmediate();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Media_PeerNichesDeclinedWhenOnlinePicturesAreOff_NoiseNeedsAnOptIn_UnknownBoardIgnored()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var s = CoreSettings.Current;
            var (online, source, flavour, subs) = (s.GoonMediaOnline, s.MediaSource, s.GoonMediaFlavour, s.GoonMediaSubs);
            var (w, posted) = Open();
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                s.GoonMediaOnline = false;
                w.HandleMessage("{\"type\":\"peer-niches\",\"subs\":[\"Good_one\"]}");
                var peer = Of(posted, "peer-media").Last();
                Assert.Equal("declined", (string?)peer["state"]);
                Assert.Empty((JArray)peer["images"]!);

                w.HandleMessage("{\"type\":\"noise-want\",\"set\":\"cats\"}");
                var noise = Assert.Single(Of(posted, "noise-media"));
                Assert.Equal("declined", (string?)noise["state"]);
                Assert.Equal("cats", (string?)noise["set"]);

                // Only a set id crosses; a niche name of the page's choosing is not a board.
                w.HandleMessage("{\"type\":\"noise-want\",\"set\":\"some_niche\"}");
                Assert.Single(Of(posted, "noise-media"));

                // The switch off: the pick is stored, nothing is fetched, the page is told "off".
                w.HandleMessage("{\"type\":\"media-flavour\",\"flavour\":\"pink\",\"subs\":[\"Good_one\"],\"online\":false}");
                Assert.False(GoonHostService.SessionOptIn);
                Assert.Empty(Of(posted, "online-media"));
            }
            finally
            {
                w.Close();
                (s.GoonMediaOnline, s.MediaSource, s.GoonMediaFlavour, s.GoonMediaSubs) = (online, source, flavour, subs);
                CoreSettings.SaveImmediate();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task UnportedLanes_AnswerInThePagesOwnVocabulary_NeverSilently()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var (w, posted) = Open();
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                w.HandleMessage("{\"type\":\"goon-recv-begin\",\"id\":\"j1\",\"sha256\":\"00\",\"mime\":\"video/mp4\",\"bytes\":10}");
                var r = Assert.Single(Of(posted, "goon-recv-result"));
                Assert.Equal("j1", (string?)r["id"]);
                Assert.False((bool)r["ok"]!);
                Assert.Equal("io-failed", (string?)r["error"]);
                w.HandleMessage("{\"type\":\"share-card\",\"id\":\"s1\",\"op\":\"copy\"}");
                Assert.False((bool)Assert.Single(Of(posted, "share-card-result"))["ok"]!);
                // Stakes are not offered on this head yet: no stake frame, so nothing can be booked.
                w.HandleMessage("{\"type\":\"stake-offer\",\"code\":\"ABCD\",\"kind\":\"time\",\"amount\":30}");
                Assert.Empty(Of(posted, "stake"));
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Close_SaysEndRun_AndForgetsTheRoom()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var (w, posted) = Open();
            w.HandleMessage("{\"type\":\"ready\"}");
            w.HandleMessage("{\"type\":\"room-code\",\"code\":\"ABCD\"}");
            w.Close();
            Assert.Equal("dispose", (string?)Assert.Single(Of(posted, "end-run"))["reason"]);
            Assert.Null(GoonHostService.RoomCode);
            Assert.False(GoonHostService.IsActive);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void OnlineMedia_ForNoiseRefusesAnUnknownBoard_AndMoreIsANoOpWithNoWave()
    {
        Assert.Null(GoonOnlineMedia.ForNoise("porn", _ => { }));
        using var m = GoonOnlineMedia.ForNoise("cats", _ => { });
        Assert.NotNull(m);
        using var own = new GoonOnlineMedia(_ => { });
        Assert.False(own.More());
    }

    [Fact]
    public void PageUrl_MovesCcpAssetsOntoTheLoopbackServer()
    {
        var url = GameWindow.GoonPageUrl("https://ccp.assets/.temp/ccp_remote_ab%20c.webp");
        Assert.StartsWith("http://127.0.0.1:", url);
        Assert.EndsWith("/ccp.assets/.temp/ccp_remote_ab%20c.webp", url);
        Assert.Equal("https://example.org/x.webp", GameWindow.GoonPageUrl("https://example.org/x.webp"));
    }
}
