using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Wave B4 game bridges (play#1, #18, #36, #56): the Back Room protocol answers the page,
/// the heartbeat ladder recovers once then closes, DtRH never pays a run it did not see start,
/// and the media manifest is the real library, not an empty list.</summary>
public sealed class GameBridgesTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task BackRoom_ReadyGetsProtocolInit_AndEveryStationRequestExactlyOneResult()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var w = new GameWindow(GameWindow.Games["backroom"]);
            var posted = new List<JObject>();
            w.Posted += json => posted.Add(JObject.Parse(json));
            w.Show();
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                var init = posted.Single(p => (string?)p["type"] == "init");
                Assert.Equal(1, (int)init["protocol"]!);
                Assert.Contains("roulette", init["stations"]!.Select(t => (string?)t));

                // An unknown op: refused bad_op, and a replayed reqId gets no second answer.
                const string req = "{\"type\":\"station-request\",\"reqId\":\"0123456789abcdef01\",\"station\":\"slot\",\"op\":\"nope\"}";
                w.HandleMessage(req);
                w.HandleMessage(req);
                var results = posted.Where(p => (string?)p["type"] == "station-result").ToList();
                Assert.Single(results);
                Assert.False((bool)results[0]["ok"]!);
                Assert.Equal("bad_op", (string?)results[0]["reason"]);

                // A short reqId is refused without touching the network.
                w.HandleMessage("{\"type\":\"station-request\",\"reqId\":\"short\",\"station\":\"slot\",\"op\":\"state\"}");
                Assert.Equal(2, posted.Count(p => (string?)p["type"] == "station-result"));
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Heartbeat_IdleUntilFirstBeat_RecoversOnce_ThenCloses()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var w = new GameWindow(GameWindow.Games["arcademy"]);
            w.Show();
            var t0 = DateTime.UtcNow;
            Assert.Equal("idle", w.CheckHeartbeat(t0.AddMinutes(5)));      // never ready, never beat
            w.HandleMessage("{\"type\":\"ready\"}");
            w.HandleMessage("{\"type\":\"heartbeat\"}");
            Assert.Equal("ok", w.CheckHeartbeat(DateTime.UtcNow.AddSeconds(15)));
            w.InRun = true;   // mid-class: 12 s
            Assert.Equal("recovered", w.CheckHeartbeat(DateTime.UtcNow.AddSeconds(13)));
            w.HandleMessage("{\"type\":\"ready\"}");
            w.HandleMessage("{\"type\":\"heartbeat\"}");
            Assert.Equal("closed", w.CheckHeartbeat(DateTime.UtcNow.AddSeconds(30)));
            Assert.False(w.IsVisible);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Dtrh_RunEndedWithoutAStart_PaysNothing()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var w = new GameWindow(new GameWindow.Game("dtrh", "launcher_game_dtrh_title", "dtrh/index.html"));
            var posted = new List<string>();
            w.Posted += posted.Add;
            w.Show();
            try
            {
                w.HandleMessage("{\"type\":\"run-ended\",\"score\":9999,\"durationSec\":600,\"elapsedSec\":600}");
                Assert.DoesNotContain(posted, p => p.Contains("payout-result"));
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Manifest_ListsTheLibrary_SkipsUndecodable_AndHonoursDeselection()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-manifest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "images", "sub"));
            Directory.CreateDirectory(Path.Combine(root, "videos"));
            File.WriteAllBytes(Path.Combine(root, "images", "a.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(root, "images", "sub", "b c.jpg"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(root, "images", "off.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(root, "videos", "v.mp4"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(root, "videos", "old.avi"), new byte[] { 1 });
            File.WriteAllText(Path.Combine(root, "videos", "notes.txt"), "x");

            var m = GameMediaManifest.Build(root, rel => "u/" + rel, new[] { "images/off.png" });
            Assert.Equal(new[] { "a.png", "b c.jpg" }, m.Images.Select(e => e.Name).OrderBy(n => n));
            Assert.Contains(m.Images, e => e.Url == "u/images/sub/b c.jpg");
            Assert.Equal("v.mp4", Assert.Single(m.Videos).Name);
            Assert.Equal(1, m.Skipped);   // the .avi; the .txt is ignored, the deselected png is not "skipped"
            Assert.False(m.Truncated);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Theory]
    [InlineData("{\"type\":\"ready\"}", "{\"type\":\"ready\"}")]
    [InlineData("\"{\\\"type\\\":\\\"ready\\\"}\"", "{\"type\":\"ready\"}")]
    public void PageBodies_ArriveAsStringOrJson(string body, string expected)
        => Assert.Equal(expected, GameWindow.Unwrap(body));

    [Fact]
    public void NativeChannel_IsCoreWebView2PostWebMessageAsJson()
        => Assert.Equal(32, GameWindow.PostWebMessageAsJsonSlot);
}
