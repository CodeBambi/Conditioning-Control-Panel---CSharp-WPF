using System;
using System.Collections.Generic;
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

/// <summary>Racing Thoughts host (ledger P5), one test per frame family: the boot frames carry the
/// race's own settings block, a run pays exactly once and only after run-started, the watchdog pings
/// once then closes, and a grant change reaches the open page (or closes it).</summary>
public sealed class RaceHostTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static (GameWindow Window, List<JObject> Posted) Open(params int[] owned)
    {
        EnsureApp();
        var w = new GameWindow(RaceWindow.Spec);
        var tracks = owned;
        w.RaceOwnedTracks = () => tracks;
        w.RaceCanLaunch = () => tracks.Length > 0;
        var posted = new List<JObject>();
        w.Posted += json => posted.Add(JObject.Parse(json));
        w.StartRace();
        w.Show();
        return (w, posted);
    }

    private static int Count(List<JObject> posted, string type) => posted.Count(p => (string?)p["type"] == type);

    [Fact]
    public async Task Ready_PostsTheRaceInit_Manifest_AndLoomList()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var (w, posted) = Open(0, 3);
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                var init = posted.Single(p => (string?)p["type"] == "init");
                Assert.Equal(1, (int)init["protocol"]!);
                var settings = (JObject)init["settings"]!;
                Assert.Equal(new[] { 0, 3 }, settings["racingTracks"]!.Select(t => (int)t).ToArray());
                Assert.Equal(GameWindow.RaceCloudAvailable, (bool)settings["cloud"]!);
                Assert.False((bool)settings["returnToCasino"]!);
                Assert.InRange((int)settings["masterVolume"]!, 0, 100);
                Assert.NotNull(settings["reducedMotion"]);
                Assert.False(string.IsNullOrEmpty((string?)init["modId"]));
                Assert.Equal(1, Count(posted, "manifest"));
                Assert.Equal(1, Count(posted, "loom-list"));
                Assert.True(w.IsReady);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Run_PaysOnce_AndNeverWithoutAStart()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var (w, posted) = Open(0);
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                const string ended = "{\"type\":\"run-ended\",\"score\":1000,\"durationSec\":60,\"bestCombo\":4,\"popped\":0,\"effects\":2,\"laps\":1}";
                w.HandleMessage(ended);
                Assert.Equal(0, Count(posted, "payout-result"));

                w.HandleMessage("{\"type\":\"run-started\",\"seed\":\"7\"}");
                Assert.True(w.RaceRunActive);
                w.HandleMessage("{\"type\":\"run-started\",\"seed\":\"7\"}");
                w.HandleMessage(ended);
                w.HandleMessage(ended);
                var pay = posted.Single(p => (string?)p["type"] == "payout-result");
                Assert.Equal(200, (int)pay["baseXp"]!);
                Assert.False((bool)pay["dryRun"]!);
                Assert.NotNull(pay["sparksEarned"]);
                Assert.NotNull(pay["previousBest"]);
                Assert.False(w.RaceRunActive);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Payout_DividesTheScoreByFive_UnderTheDescentCap()
    {
        Assert.Equal((200, 300), Pick(GameWindow.RacePayout(1000, 60, 1.5)));
        Assert.Equal((250, 250), Pick(GameWindow.RacePayout(100000, 60, 1.0)));   // 250 XP a minute
        Assert.Equal((4, 4), Pick(GameWindow.RacePayout(99999, 0, 1.0)));         // a zero length reads as one second
        Assert.Equal((0, 0), Pick(GameWindow.RacePayout(-5, 60, 1.0)));
        static (int, int) Pick((int BaseXp, int FinalXp, double DurationSec, double SparkScore) p) => (p.BaseXp, p.FinalXp);
    }

    [Fact]
    public async Task Watchdog_PingsOnce_ThenCloses_AndABeatResetsIt()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var (w, posted) = Open(0);
            bool closed = false;
            w.Closed += (_, _) => closed = true;
            try
            {
                Assert.Equal("idle", w.CheckRaceHeartbeat(DateTime.UtcNow.AddSeconds(60)));   // still loading
                w.HandleMessage("{\"type\":\"ready\"}");
                Assert.Equal("ok", w.CheckRaceHeartbeat(DateTime.UtcNow.AddSeconds(15)));
                Assert.Equal("pinged", w.CheckRaceHeartbeat(DateTime.UtcNow.AddSeconds(25)));
                Assert.Equal(1, Count(posted, "ping"));
                w.HandleMessage("{\"type\":\"pong\"}");
                Assert.Equal("ok", w.CheckRaceHeartbeat(DateTime.UtcNow.AddSeconds(15)));

                // Mid-run the limit is 10 s.
                w.HandleMessage("{\"type\":\"run-started\"}");
                Assert.Equal("pinged", w.CheckRaceHeartbeat(DateTime.UtcNow.AddSeconds(12)));
                Assert.Equal("closed", w.CheckRaceHeartbeat(DateTime.UtcNow.AddSeconds(18)));
                Assert.True(closed);
            }
            finally { if (!closed) w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Grants_ANewTrackReachesThePage_ALostOneClosesTheRace()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var (w, posted) = Open(0);
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                w.ApplyRaceGrants();
                Assert.Equal(0, Count(posted, "race-ownership"));

                var tracks = new[] { 0, 5 };
                w.RaceOwnedTracks = () => tracks;
                w.ApplyRaceGrants();
                var own = posted.Single(p => (string?)p["type"] == "race-ownership");
                Assert.Equal(new[] { 0, 5 }, own["tracks"]!.Select(t => (int)t).ToArray());

                tracks = new[] { 5 };
                w.ApplyRaceGrants();
                Assert.Equal(1, Count(posted, "exit-request"));
                w.ApplyRaceGrants();
                Assert.Equal(1, Count(posted, "exit-request"));   // already on its way out
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task VideoPayload_IsRefused_AndUnknownFramesFallThroughToTheShell()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var (w, posted) = Open(0);
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                int before = posted.Count;
                w.HandleMessage("{\"type\":\"fire-payload\",\"kind\":\"video\",\"strength\":60}");
                w.HandleMessage("{\"type\":\"fire-payload\",\"kind\":\"flash\"}");
                Assert.Equal(before, posted.Count);
                // fullscreen-set is the shell's: the echo still comes back.
                w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":false}");
                Assert.Equal(1, Count(posted, "fullscreen"));
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }
}
