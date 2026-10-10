using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Racing Thoughts, BambiCloud levels (ledger P5): cloud-open / cloud-start drive the browser
/// frame, the watcher's frames become the run's clock, a level whose pack is not owned is refused on
/// the host, and the desktop only downloads audio under the remote media consent rule.</summary>
// Runs alone: each test awaits with a race window open, and a panic test in another class running in
// that gap would close every game window (PanicSurfaces "games") under it.
[Collection(RunsAloneCollection.Name)]
public sealed class RaceCloudTests
{
    private sealed class FakeCloud : IRaceCloudWindow
    {
        public event Action<JObject>? Message;
        public event Action? Hidden;
        public readonly List<string> Calls = new();
        public readonly List<JObject> ToPage = new();
        public bool Disposed;
        public void ShowOrFocus(string? url = null) => Calls.Add("front:" + (url ?? ""));
        public void ShowInBackground(string? url = null) => Calls.Add("back:" + (url ?? ""));
        public void RequestStart() => Calls.Add("start");
        public void PostToPage(object msg) => ToPage.Add(JObject.FromObject(msg));
        public void Dispose() => Disposed = true;
        public void Send(object msg) => Message?.Invoke(JObject.FromObject(msg));
        public void Hide() => Hidden?.Invoke();
    }

    private sealed class FakeSite : HttpMessageHandler
    {
        public readonly List<string> Requests = new();
        public byte[] Body = Array.Empty<byte>();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add(request.Method + " " + request.RequestUri);
            if (request.Method == HttpMethod.Head) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Body) });
        }
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static (GameWindow Window, List<JObject> Posted, FakeCloud Cloud) Open(bool consent = false)
    {
        EnsureApp();
        var w = new GameWindow(RaceWindow.Spec);
        w.RaceOwnedTracks = () => new[] { 0 };
        w.RaceCanLaunch = () => true;
        w.RaceCanOpenCloud = _ => true;
        w.RaceMayFetchRemote = () => consent;
        w.RaceNetAllows = _ => true;
        var cloud = new FakeCloud();
        w.RaceNewCloudWindow = () => cloud;
        var posted = new List<JObject>();
        w.Posted += json => { lock (posted) posted.Add(JObject.Parse(json)); };
        w.StartRace();
        w.Show();
        w.HandleMessage("{\"type\":\"ready\"}");
        return (w, posted, cloud);
    }

    private static List<JObject> Of(List<JObject> posted, string type)
    {
        lock (posted) return posted.Where(p => (string?)p["type"] == type).ToList();
    }

    // The host queues cloud work on the dispatcher (RaceQueue): wait for the queue, not for a clock.
    private static async Task Queued()
    {
        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.ContextIdle);
        await Task.Delay(100);
    }

    private static async Task Settle(GameWindow w)
    {
        await w.RaceAnalysis;
        await Task.Delay(150);
    }

    private static string Src(string ext = ".mp3") => "https://cdn.bambicloud.com/" + Guid.NewGuid().ToString("D") + ext;

    [Fact]
    public async Task CloudOpen_AndCloudStart_DriveTheBrowserFrame()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (w, posted, cloud) = Open();
            try
            {
                Assert.True((bool)Of(posted, "init")[0]["settings"]!["cloud"]!);

                w.HandleMessage("{\"type\":\"cloud-open\",\"url\":\"https://bambicloud.com/file/abc\"}");
                w.HandleMessage("{\"type\":\"cloud-open\",\"url\":\"https://evil.example/file/abc\"}");
                w.HandleMessage("{\"type\":\"cloud-open\",\"front\":true}");
                w.HandleMessage("{\"type\":\"cloud-start\"}");
                await Queued();
                Assert.Equal(new[] { "back:https://bambicloud.com/file/abc", "back:", "front:", "start" }, cloud.Calls);

                // Closed with no cloud track in hand: the "opening" plate comes back down.
                cloud.Hide();
                Assert.Contains(Of(posted, "track-progress"), p => (string?)p["stage"] == "cancelled");
            }
            finally { w.Close(); }
            Assert.True(cloud.Disposed);   // the race closing (panic included) is what stops their audio
        });
    }

    [Fact]
    public async Task ALevelWhosePackIsNotOwned_IsRefusedOnTheHost_AndRetriedOnlyOnARealPlay()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (w, posted, cloud) = Open();
            try
            {
                bool owned = false;
                w.RaceCanOpenCloud = _ => owned;

                w.HandleMessage("{\"type\":\"cloud-open\",\"url\":\"https://bambicloud.com/file/abc\"}");
                await Queued();
                Assert.Empty(cloud.Calls);
                Assert.Equal(Loc.Get("race_track_locked"), (string?)Of(posted, "track-error").Single()["message"]);

                // Their player started a source the account does not own: paused over there, refused here.
                w.HandleMessage("{\"type\":\"cloud-start\"}");
                await Queued();
                var src = Src();
                cloud.Send(new { type = "cloud-track", src, title = "Track one", durationSec = 80 });
                Assert.Contains(cloud.ToPage, m => (string?)m["type"] == "cloud-set-paused" && (bool)m["on"]!);
                Assert.Equal(2, Of(posted, "track-error").Count);
                cloud.Send(new { type = "cloud-play" });
                Assert.Empty(Of(posted, "cloud-run"));
                Assert.Empty(Of(posted, "track-clock"));

                // Bought since: the purchase starts nothing, the next real play retries the source.
                owned = true;
                Assert.Empty(Of(posted, "cloud-run"));
                cloud.Send(new { type = "cloud-play" });
                Assert.Single(Of(posted, "cloud-run"));
                Assert.True((bool)Of(posted, "track-clock")[^1]["playing"]!);
                await Settle(w);
            }
            finally { w.Close(); }
        });
    }

    [Fact]
    public async Task TheirPlayerIsTheClock_TheBrakePausesThem_AndTheNextTrackIsTheNextLap()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (w, posted, cloud) = Open();
            try
            {
                w.HandleMessage("{\"type\":\"cloud-start\"}");
                await Queued();
                cloud.Send(new { type = "cloud-track", src = Src(), title = " Track one ", durationSec = 80 });
                var clock = Of(posted, "track-clock")[^1];
                Assert.True((bool)clock["playing"]!);
                Assert.Equal(80.0, (double)clock["durationSec"]!);

                cloud.Send(new { type = "cloud-play" });
                Assert.Single(Of(posted, "cloud-run"));
                w.HandleMessage("{\"type\":\"run-started\"}");
                w.HandleMessage("{\"type\":\"track-play\"}");   // a cloud element is already running: left alone

                cloud.Send(new { type = "cloud-clock", t = 12.5, playing = true, durationSec = 0 });
                w.PostRaceClock();
                clock = Of(posted, "track-clock")[^1];
                Assert.Equal(12.5, (double)clock["t"]!);
                Assert.Equal(80.0, (double)clock["durationSec"]!);   // a 0 length never wipes the known one

                // The Brake pauses their player; their own pause holds the run.
                w.HandleMessage("{\"type\":\"track-pause\",\"on\":true}");
                Assert.Contains(cloud.ToPage, m => (string?)m["type"] == "cloud-set-paused" && (bool)m["on"]!);
                Assert.False((bool)Of(posted, "track-clock")[^1]["playing"]!);
                w.HandleMessage("{\"type\":\"track-pause\",\"on\":false}");
                Assert.Contains(cloud.ToPage, m => (string?)m["type"] == "cloud-set-paused" && !(bool)m["on"]!);
                cloud.Send(new { type = "cloud-pause" });
                Assert.False((bool)Of(posted, "track-clock")[^1]["playing"]!);
                cloud.Send(new { type = "cloud-play" });
                Assert.Single(Of(posted, "cloud-run"));   // a run is live: play resumes it, never restarts it

                // The next track while the run is live is the next lap, and the run-ended that answers
                // must not take the new clock away.
                cloud.Send(new { type = "cloud-track", src = Src(), title = "Track two", durationSec = 60 });
                Assert.Single(Of(posted, "track-ended"));
                w.HandleMessage("{\"type\":\"run-ended\",\"score\":100,\"durationSec\":60}");
                Assert.Single(Of(posted, "payout-result"));
                int clocks = Of(posted, "track-clock").Count;
                w.PostRaceClock();
                Assert.Equal(clocks + 1, Of(posted, "track-clock").Count);
                Assert.Equal(60.0, (double)Of(posted, "track-clock")[^1]["durationSec"]!);

                cloud.Send(new { type = "cloud-ended" });
                Assert.Equal(2, Of(posted, "track-ended").Count);

                cloud.Send(new { type = "cloud-failed" });
                Assert.Equal(GameWindow.RaceCloudDownMessage, (string?)Of(posted, "track-error")[^1]["message"]);
                await Settle(w);
            }
            finally { w.Close(); }
        });
    }

    [Fact]
    public async Task TheDesktopDownloadsAudio_OnlyWithRemoteMediaConsent_AndOnlyFromTheSite()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            // No consent: nothing is asked of the site, the run still follows their player.
            var site = new FakeSite { Body = Wav(20) };
            var (w, posted, cloud) = Open(consent: false);
            try
            {
                w.RaceCloudHttp = new HttpClient(site);
                w.HandleMessage("{\"type\":\"cloud-start\"}");
                await Queued();
                cloud.Send(new { type = "cloud-track", src = Src(".wav"), title = "Quiet", durationSec = 20 });
                await Settle(w);
                Assert.Empty(site.Requests);
                Assert.Empty(Of(posted, "track-chart"));
                Assert.DoesNotContain(Of(posted, "track-progress"), p => (string?)p["stage"] == "fetching");
                Assert.Contains(Of(posted, "track-progress"), p => (string?)p["stage"] == "cancelled");
                Assert.True((bool)Of(posted, "track-clock")[^1]["playing"]!);
                Assert.Empty(Of(posted, "track-error"));
            }
            finally { w.Close(); }

            // Consent given: the file comes down, is charted like a picked one, and is deleted after.
            site = new FakeSite { Body = Wav(20) };
            (w, posted, cloud) = Open(consent: true);
            try
            {
                w.RaceCloudHttp = new HttpClient(site);
                w.HandleMessage("{\"type\":\"cloud-start\"}");
                await Queued();
                var src = Src(".wav");
                cloud.Send(new { type = "cloud-track", src, title = "Loud", durationSec = 20 });
                await Settle(w);
                Assert.Contains(site.Requests, r => r == "GET " + src);
                Assert.All(site.Requests, r => Assert.EndsWith(src, r));
                var charts = Of(posted, "track-chart");
                Assert.Equal(2, charts.Count);
                Assert.Equal("Loud", (string?)charts[^1]["chart"]!["source"]!["name"]);
                Assert.Contains(Of(posted, "track-progress"), p => (string?)p["stage"] == "fetching");
                var temp = Path.Combine(CorePaths.UserData, "race", "cloud");
                Assert.True(!Directory.Exists(temp) || Directory.GetFiles(temp, "cloud-*").Length == 0);

                // A sandbox never reaches the site, consent or not.
                int quiet = site.Requests.Count;
                w.RaceNetAllows = _ => false;
                cloud.Send(new { type = "cloud-track", src = Src(".wav"), title = "Sandboxed", durationSec = 20 });
                await Settle(w);
                Assert.Equal(quiet, site.Requests.Count);
                w.RaceNetAllows = _ => true;

                // A source that is not the site's own address is never fetched, whatever the page says.
                int before = site.Requests.Count;
                cloud.Send(new { type = "cloud-track", src = "https://evil.example/" + Guid.NewGuid().ToString("D") + ".wav", title = "Elsewhere", durationSec = 20 });
                await Settle(w);
                Assert.Equal(before, site.Requests.Count);
                Assert.Equal(GameWindow.RaceCloudDownMessage, (string?)Of(posted, "track-error")[^1]["message"]);
            }
            finally { w.Close(); }
        });
    }

    [Fact]
    public async Task TheBrowserFrame_StaysOnTheSite_AndReadsOnlyItsOwnWatcher()
    {
        Assert.True(RaceCloudWindow.IsSiteUri("https://bambicloud.com/file/abc"));
        Assert.True(RaceCloudWindow.IsSiteUri("https://cdn.bambicloud.com/x.mp3?token=1"));
        Assert.False(RaceCloudWindow.IsSiteUri("http://bambicloud.com/"));
        Assert.False(RaceCloudWindow.IsSiteUri("https://notbambicloud.com/"));
        Assert.False(RaceCloudWindow.IsSiteUri("https://bambicloud.com.evil.example/"));
        Assert.False(RaceCloudWindow.IsSiteUri("file:///C:/x.mp3"));
        Assert.False(RaceCloudWindow.IsSiteUri(null));

        var opened = new List<string>();
        var old = RaceCloudWindow.OpenInBrowser;
        RaceCloudWindow.OpenInBrowser = opened.Add;
        try
        {
            RaceCloudWindow.OpenExternally(new Uri("https://example.com/a"));
            RaceCloudWindow.OpenExternally(new Uri("file:///C:/Windows/System32/calc.exe"));
            RaceCloudWindow.OpenExternally(new Uri("ms-settings:privacy"));
            RaceCloudWindow.OpenExternally(null);
            Assert.Equal(new[] { "https://example.com/a" }, opened);
        }
        finally { RaceCloudWindow.OpenInBrowser = old; }

        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            using var win = new RaceCloudWindow();
            var got = new List<string>();
            win.Message += o => got.Add((string)o["type"]!);
            win.OnPageMessage("{\"type\":\"cloud-play\"}", "https://bambicloud.com/file/abc");
            win.OnPageMessage("\"{\\\"type\\\":\\\"cloud-ended\\\"}\"", "https://bambicloud.com/file/abc");   // a posted string
            win.OnPageMessage("{\"type\":\"cloud-play\"}", "https://evil.example/");       // not from the site
            win.OnPageMessage("{\"type\":\"track-error\"}", "https://bambicloud.com/");    // not a watcher frame
            win.OnPageMessage("{\"type\":\"run-ended\",\"score\":999999}", "https://bambicloud.com/");
            win.OnPageMessage("not json", "https://bambicloud.com/");
            Assert.Equal(new[] { "cloud-play", "cloud-ended" }, got);
            return Task.CompletedTask;
        });

        Assert.Contains("c.host({\"type\":\"cloud-set-paused\",\"on\":true})", RaceCloudWindow.HostCall("{\"type\":\"cloud-set-paused\",\"on\":true}"));
        Assert.Contains("invokeCSharpAction", RaceCloudWindow.WatcherScript);
        Assert.Contains("__ccpRaceCloud.host", RaceCloudWindow.WatcherScript);
        Assert.DoesNotContain("speechSynthesis", RaceCloudWindow.WatcherScript);
        Assert.Equal(".mp3", GameWindow.RaceCloudExtension("https://cdn.bambicloud.com/x.exe?y=1"));
        Assert.Equal(".flac", GameWindow.RaceCloudExtension("https://cdn.bambicloud.com/x.FLAC"));
    }

    private static byte[] Wav(double seconds)
    {
        int frames = (int)(16000 * seconds);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + frames * 2); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(frames * 2);
        var rng = new Random();
        for (int i = 0; i < frames; i++)
        {
            double t = i / 16000.0;
            double env = 0.15 + 0.8 * Math.Pow(Math.Sin(Math.PI * t / 2.0), 2);
            w.Write((short)(env * 20000 * Math.Sin(2 * Math.PI * 220 * t) + rng.Next(-200, 200)));
        }
        w.Flush();
        return ms.ToArray();
    }
}
