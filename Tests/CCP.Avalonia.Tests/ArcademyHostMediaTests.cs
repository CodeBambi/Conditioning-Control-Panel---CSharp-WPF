using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Arcademy;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Arcademy host, second pass (WPF ArcademyHostService): media batches with the remote
/// consent rule, the local sampler, the sub probe, the panic ladder, the boot deadline, the graceful
/// close, the student ID link and the share card. One test per frame family, page frame in, frame or
/// state out. No network: the provider calls go through the window's test seams.</summary>
[Collection(RunsAloneCollection.Name)]   // names process-wide paths and flips consent / offline settings
public sealed class ArcademyHostMediaTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static void Pump(Func<bool> until, int ms = 4000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (!until() && DateTime.UtcNow < end)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Ctx
    {
        public required Func<bool, GameWindow> Open;      // true = send ready + a first heartbeat
        public required List<JObject> Posted;
        public required string Dir;
        public JObject Last(string type) => Posted.Last(p => (string?)p["type"] == type);
        public int Count(string type) => Posted.Count(p => (string?)p["type"] == type);
    }

    /// <summary>Opens the campus on a throwaway save (offline by default), runs the body, restores every setting.</summary>
    private static Task Campus(Action<Ctx> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var dir = Path.Combine(Path.GetTempPath(), "ccp-arcademy2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var s = CoreSettings.Current;
        bool offline = s.OfflineMode, consented = s.RemoteMediaConsented, audioOnly = s.AudioOnlySession;
        string source = s.MediaSource, share = s.ArcademyPresenceShare;
        var open = new List<GameWindow>();
        var posted = new List<JObject>();
        GameWindow.ArcademyMetaPathOverride = Path.Combine(dir, "arcademy_meta.json");
        ArcademyAvatarCache.DirOverride = Path.Combine(dir, "avatar");
        s.OfflineMode = true;
        try
        {
            body(new Ctx
            {
                Dir = dir,
                Posted = posted,
                Open = ready =>
                {
                    var w = new GameWindow(GameWindow.Games["arcademy"]);
                    w.Posted += json => posted.Add(JObject.Parse(json));
                    w.Show();
                    open.Add(w);
                    if (ready)
                    {
                        w.HandleMessage("{\"type\":\"ready\",\"protocol\":1}");
                        w.HandleMessage("{\"type\":\"heartbeat\"}");
                    }
                    return w;
                },
            });
        }
        finally
        {
            foreach (var w in open) { try { w.Close(); } catch { } }
            GameWindow.ArcademyMetaPathOverride = null;
            GameWindow.ArcademyAssetsRootOverride = null;
            ArcademyAvatarCache.DirOverride = null;
            s.OfflineMode = offline;
            s.RemoteMediaConsented = consented;
            s.MediaSource = source;
            s.AudioOnlySession = audioOnly;
            s.ArcademyPresenceShare = share;
            CoreSettings.SaveImmediate();
            try { Directory.Delete(dir, true); } catch { }
        }
        return Task.CompletedTask;
    });

    private static void AllowRemote()
    {
        var s = CoreSettings.Current;
        s.OfflineMode = false;
        s.RemoteMediaConsented = true;
        s.MediaSource = "mixed";
    }

    private static FypAssetManifest.Entry Still(string sub, int n) => new()
    {
        Id = "scrolller/" + sub + "/" + n,
        Url = "https://images.example.test/" + sub + "/" + n + ".jpg",
        Type = RemoteMediaFormats.TypeImage,
        Folder = "r/" + sub,
    };

    // ---- assets-request -----------------------------------------------------------------------

    [Fact]
    public Task AssetsRequest_WithTheGateClosed_AnswersEmptyAndDone_AndNeverAsksTheProvider() => Campus(c =>
    {
        var w = c.Open(true);
        int asked = 0;
        w.ArcFetchBatchOverride = (_, _) => { asked++; return Task.FromResult((new List<FypAssetManifest.Entry> { Still("cats", 1) }, (string?)null)); };

        // Offline mode.
        w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"a1\",\"count\":4,\"kind\":\"still\"}");
        var a1 = c.Last("assets");
        Assert.Equal("a1", (string?)a1["reqId"]);
        Assert.Empty((JArray)a1["urls"]!);
        Assert.True((bool)a1["done"]!);

        // Online, consent given, but the player keeps media local: WPF's rule is BOTH halves.
        AllowRemote();
        CoreSettings.Current.MediaSource = "local";
        w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"a2\",\"count\":4,\"kind\":\"still\",\"subs\":[\"cats\"],\"tag\":\"target\"}");
        var a2 = c.Last("assets");
        Assert.Equal("a2", (string?)a2["reqId"]);
        Assert.Equal("target", (string?)a2["tag"]);
        Assert.Empty((JArray)a2["urls"]!);
        Assert.True((bool)a2["done"]!);

        // Source says mixed but consent was never given.
        CoreSettings.Current.MediaSource = "mixed";
        CoreSettings.Current.RemoteMediaConsented = false;
        if (!CoreSettings.Current.HasRemoteMediaConsent)
        {
            w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"a3\",\"count\":4,\"kind\":\"still\"}");
            Assert.Empty((JArray)c.Last("assets")["urls"]!);
        }
        Pump(() => false, 60);
        Assert.Equal(0, asked);
    });

    [Fact]
    public Task AssetsRequest_AppWide_StreamsABatchUnderTheSameReqId_AndBuffersTheRest() => Campus(c =>
    {
        var w = c.Open(true);
        AllowRemote();
        w.ArcFetchBatchOverride = (tag, kind) =>
        {
            Assert.Equal("", tag);
            Assert.Equal(FeedMediaKind.Image, kind);
            return Task.FromResult((Enumerable.Range(1, 5).Select(i => Still("cats", i)).ToList(), (string?)null));
        };
        w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"b1\",\"count\":2,\"kind\":\"still\",\"niches\":[\"x\"]}");
        Pump(() => c.Posted.Any(p => (string?)p["type"] == "assets" && (bool)p["done"]!));
        var first = c.Posted.First(p => (string?)p["type"] == "assets");
        Assert.False((bool)first["done"]!);                     // the immediate reply never blocks on the network
        var batch = c.Last("assets");
        Assert.Equal("b1", (string?)batch["reqId"]);
        Assert.Equal(2, ((JArray)batch["urls"]!).Count);
        Assert.Equal("image/jpeg", (string?)batch["urls"]![0]!["mime"]);
        Assert.Null(batch["tag"]);                              // the app-wide envelope carries no tag

        // The other three were prewarmed: the next ask is served at once, with no fetch.
        w.ArcFetchBatchOverride = (_, _) => throw new InvalidOperationException("no second fetch");
        w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"b2\",\"count\":3,\"kind\":\"still\"}");
        var b2 = c.Last("assets");
        Assert.Equal("b2", (string?)b2["reqId"]);
        Assert.Equal(3, ((JArray)b2["urls"]!).Count);
        Assert.True((bool)b2["done"]!);
    });

    [Fact]
    public Task AssetsRequest_Tagged_DealsOnlyThePilesOwnSubs_WithTagAndSrcOnEveryRow() => Campus(c =>
    {
        var w = c.Open(true);
        AllowRemote();
        w.ArcFetchBatchOverride = (tag, _) =>
        {
            Assert.Equal("noise", tag);
            return Task.FromResult((new List<FypAssetManifest.Entry> { Still("cats", 1), Still("dogs", 2), Still("cats", 3) }, (string?)null));
        };
        w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"t1\",\"count\":8,\"kind\":\"still\",\"subs\":[\"cats\"],\"tag\":\"Noise\"}");
        Pump(() => c.Posted.Any(p => (string?)p["reqId"] == "t1" && (bool)p["done"]!));
        var rows = (JArray)c.Last("assets")["urls"]!;
        Assert.Equal(2, rows.Count);                            // the r/dogs row is not this pile's
        Assert.All(rows, r => { Assert.Equal("noise", (string?)r["tag"]); Assert.Equal("r/cats", (string?)r["src"]); });

        // A subs field that is present but empty is refused, never waved through to the app-wide pull.
        w.ArcFetchBatchOverride = (_, _) => throw new InvalidOperationException("an empty pile never fetches");
        w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"t2\",\"count\":8,\"kind\":\"still\",\"subs\":\"cats\",\"tag\":\"target\"}");
        var t2 = c.Last("assets");
        Assert.Equal("t2", (string?)t2["reqId"]);
        Assert.Empty((JArray)t2["urls"]!);
        Assert.True((bool)t2["done"]!);
    });

    [Fact]
    public Task AssetsRequest_ConsentWithdrawnMidFetch_SendsNothingRemote() => Campus(c =>
    {
        var w = c.Open(true);
        AllowRemote();
        var gate = new TaskCompletionSource<(List<FypAssetManifest.Entry>, string?)>();
        w.ArcFetchBatchOverride = (_, _) => gate.Task;
        w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"w1\",\"count\":4,\"kind\":\"still\"}");
        CoreSettings.Current.MediaSource = "local";             // the player turns remote media off
        gate.SetResult((new List<FypAssetManifest.Entry> { Still("cats", 1) }, null));
        Pump(() => c.Posted.Any(p => (string?)p["reqId"] == "w1" && (bool)p["done"]!));
        var done = c.Posted.Last(p => (string?)p["reqId"] == "w1");
        Assert.True((bool)done["done"]!);
        Assert.Empty((JArray)done["urls"]!);
    });

    // ---- local-sample-request -----------------------------------------------------------------

    [Fact]
    public Task LocalSample_DealsThePlayersOwnFolder_OnTheAssetServer_WithNoConsentNeeded() => Campus(c =>
    {
        var root = Path.Combine(c.Dir, "assets");
        Directory.CreateDirectory(Path.Combine(root, "images", "pile"));
        File.WriteAllBytes(Path.Combine(root, "images", "pile", "one two.gif"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(root, "images", "other.gif"), new byte[] { 1 });
        GameWindow.ArcademyAssetsRootOverride = root;
        var w = c.Open(true);                                   // offline: local media needs no remote gate
        w.HandleMessage("{\"type\":\"local-sample-request\",\"reqId\":\"l1\",\"count\":8,\"kind\":\"loop\",\"folders\":[\"images/pile\",\"../..\"],\"tag\":\"target\"}");
        Pump(() => c.Posted.Any(p => (string?)p["reqId"] == "l1"));
        var reply = c.Posted.Last(p => (string?)p["reqId"] == "l1");
        Assert.True((bool)reply["done"]!);
        var row = Assert.Single((JArray)reply["urls"]!);
        Assert.Equal(WebAssetServer.Shared.AssetUrl("images/pile/one two.gif"), (string?)row["url"]);
        Assert.Contains("/ccp.assets/images/pile/one%20two.gif", (string?)row["url"]);
        Assert.Equal("loop", (string?)row["kind"]);
        Assert.Equal("image/gif", (string?)row["mime"]);
        Assert.Equal("target", (string?)row["tag"]);
        Assert.Equal("images/pile", (string?)row["src"]);

        // init carries the same library: a manifest, the folder counts.
        w.Close();
        c.Posted.Clear();
        c.Open(true);
        var bag = c.Last("init")["settings"]!;
        Assert.Equal(2, ((JArray)bag["localAssets"]!["gifs"]!).Count);
        Assert.Contains((JArray)bag["localFolders"]!, f => (string?)f["path"] == "images/pile" && (int)f["gifs"]! == 1);
        Assert.Equal(JTokenType.Array, bag["loomSpirals"]!.Type);
    });

    // ---- probe-sub ----------------------------------------------------------------------------

    [Fact]
    public Task ProbeSub_AlwaysReplies_AndAVerifiedNameLandsInTheLibraryOnly() => Campus(c =>
    {
        var w = c.Open(true);
        w.HandleMessage("{\"type\":\"probe-sub\",\"reqId\":\"p0\",\"name\":\"!!\"}");
        Assert.Equal("invalid", (string?)c.Last("sub-probe")["error"]);

        const string sub = "ccpparitytestsub";
        var s = CoreSettings.Current;
        s.RemoveLibrarySub(sub);
        s.FypOnlineSubVerdicts.Remove(sub);
        var feedBefore = (s.FypOnlineCustomSubs ?? new List<string>()).ToList();
        try
        {
            w.HandleMessage("{\"type\":\"probe-sub\",\"reqId\":\"p1\",\"name\":\"" + sub + "\"}");
            var off = c.Last("sub-probe");
            Assert.Equal("p1", (string?)off["reqId"]);
            Assert.False((bool)off["ok"]!);
            Assert.Equal("offline", (string?)off["error"]);     // offline mode: no request leaves the machine

            AllowRemote();
            w.ArcProbeSubOverride = name => Task.FromResult(new SubProbe { Ok = true, VideoCount = 0 });
            w.HandleMessage("{\"type\":\"probe-sub\",\"reqId\":\"p2\",\"name\":\"r/" + sub + "\",\"scope\":\"sort\",\"pile\":\"noise\"}");
            Pump(() => c.Posted.Any(p => (string?)p["reqId"] == "p2"));
            var ok = c.Posted.Last(p => (string?)p["reqId"] == "p2");
            Assert.True((bool)ok["ok"]!);
            Assert.Equal(sub, (string?)ok["name"]);
            Assert.True((bool)ok["stillOnly"]!);                // zero videos with ok is a real answer
            Assert.True(s.FypOnlineSubVerdicts.ContainsKey(sub));
            Assert.Contains((JArray)c.Last("library")["subLibrary"]!, r => (string?)r["name"] == sub);
            // Noise the player picked to sort against must not start showing on their desktop.
            Assert.Equal(feedBefore, (s.FypOnlineCustomSubs ?? new List<string>()).ToList());
        }
        finally
        {
            s.RemoveLibrarySub(sub);
            s.FypOnlineSubVerdicts.Remove(sub);
        }
    });

    // ---- panic ---------------------------------------------------------------------------------

    /// <summary>Owner, 10 Oct 2026: "One press, host side". A healthy, beating page mid-run is closed by the
    /// first panic press; nothing is posted for the page to answer first.</summary>
    [Fact]
    public Task Panic_OnePressClosesTheArcademy_HostSide() => Campus(c =>
    {
        Assert.False(GameWindow.ArcademyPanicLadder);
        var w = c.Open(true);
        GameWindow.CloseAllForPanic();
        Assert.True(w.IsClosedOrClosing);
        Assert.False(w.ArcademyPanicSuspended);
        Assert.Equal(0, c.Count("suspend"));
        Assert.Equal(0, c.Count("end-run"));
    });

    [Fact]
    public Task Panic_OnePress_CancelsAnOpenLinkUp() => Campus(c =>
    {
        var w = c.Open(true);
        var never = new TaskCompletionSource<bool>();
        w.ArcLinkFlowOverride = ct => { ct.Register(() => never.TrySetCanceled()); return never.Task; };
        w.HandleMessage("{\"type\":\"link-discord\"}");
        GameWindow.CloseAllForPanic();
        Assert.True(w.IsClosedOrClosing);
        Pump(() => never.Task.IsCompleted);
        Assert.True(never.Task.IsCanceled);
    });

    // ---- the WPF ladder, kept behind the switch ------------------------------------------------

    private static Task Ladder(Action<Ctx> body) => Campus(c =>
    {
        GameWindow.ArcademyPanicLadder = true;
        try { body(c); }
        finally { GameWindow.ArcademyPanicLadder = false; }
    });

    [Fact]
    public Task Panic_PressOneFreezes_ResumeIsTheHostsToGrant_PressTwoCloses() => Ladder(c =>
    {
        var w = c.Open(true);
        // A resume nobody is owed is ignored.
        w.HandleMessage("{\"type\":\"resume-request\",\"reason\":\"panic\"}");
        Assert.Equal(0, c.Count("suspend"));

        GameWindow.CloseAllForPanic();
        Assert.False(w.IsClosedOrClosing);
        Assert.True(w.ArcademyPanicSuspended);
        var freeze = c.Last("suspend");
        Assert.True((bool)freeze["on"]!);
        Assert.Equal("panic", (string?)freeze["reason"]);

        // The page may only ask; a resume for any other reason is refused.
        w.HandleMessage("{\"type\":\"resume-request\",\"reason\":\"video\"}");
        Assert.True(w.ArcademyPanicSuspended);
        w.HandleMessage("{\"type\":\"resume-request\",\"reason\":\"panic\"}");
        Assert.False(w.ArcademyPanicSuspended);
        Assert.False((bool)c.Last("suspend")["on"]!);

        // A slow second press is a fresh press one; a quick one closes.
        var t0 = DateTime.UtcNow;
        Assert.True(w.ArcademyPanicPress(t0));
        Assert.True(w.ArcademyPanicPress(t0.AddSeconds(5)));
        Assert.False(w.IsClosedOrClosing);
        GameWindow.CloseAllForPanic();
        Assert.True(w.IsClosedOrClosing);
    });

    [Fact]
    public Task Panic_APageThatCannotBeFrozenIsClosedAtOnce() => Ladder(c =>
    {
        var booting = c.Open(false);                            // never said ready
        Assert.False(booting.ArcademyPanicPress(DateTime.UtcNow));
        GameWindow.CloseAllForPanic();
        Assert.True(booting.IsClosedOrClosing);

        var silent = c.Open(true);                              // ready, then its heartbeat stops
        Assert.False(silent.ArcademyPanicPress(DateTime.UtcNow.AddSeconds(60)));

        var off = c.Open(true);
        GameWindow.ArcademyPanicLadder = false;
        Assert.False(off.ArcademyPanicPress(DateTime.UtcNow));
        GameWindow.ArcademyPanicLadder = true;
    });

    [Fact]
    public Task Panic_AnAudioOnlySessionOutranksTheResume() => Ladder(c =>
    {
        var w = c.Open(true);
        Assert.True(w.ArcademyPanicPress(DateTime.UtcNow));
        CoreSettings.Current.AudioOnlySession = true;
        int suspends = c.Count("suspend");
        w.HandleMessage("{\"type\":\"resume-request\",\"reason\":\"panic\"}");
        Assert.True(w.ArcademyPanicSuspended);
        // Only the audio-only suspend itself may have been posted; no un-freeze went out.
        Assert.DoesNotContain(c.Posted.Skip(0), p => (string?)p["type"] == "suspend" && (string?)p["reason"] == "panic" && !(bool)p["on"]!);
        Assert.True(c.Count("suspend") >= suspends);
    });

    [Fact]
    public Task Video_SuspendsTheClass_AndItsEndNeverLiftsAPanicFreeze() => Ladder(c =>
    {
        var w = c.Open(true);
        w.ArcademyVideoSuspend(true);
        Assert.Equal("video", (string?)c.Last("suspend")["reason"]);
        Assert.True((bool)c.Last("suspend")["on"]!);
        w.ArcademyVideoSuspend(false);
        Assert.False((bool)c.Last("suspend")["on"]!);

        w.ArcademyVideoSuspend(true);
        Assert.True(w.ArcademyPanicPress(DateTime.UtcNow));
        int before = c.Count("suspend");
        w.ArcademyVideoSuspend(false);
        Assert.Equal(before, c.Count("suspend"));               // the panic freeze stands
    });

    // ---- boot deadline, graceful close --------------------------------------------------------

    [Fact]
    public Task BootDeadline_APageThatReportsNothingIsClosedAndRemembered_AndAGoodBootClearsIt() => Campus(c =>
    {
        var w = c.Open(false);
        Assert.Equal("waiting", w.CheckArcademyBootDeadline(DateTime.UtcNow.AddSeconds(10)));
        Assert.Equal("failed", w.CheckArcademyBootDeadline(DateTime.UtcNow.AddSeconds(46)));
        Assert.True(w.IsClosedOrClosing);
        Assert.True(GameWindow.ArcademyBootFailedThisSession);

        var ok = c.Open(true);
        Assert.False(GameWindow.ArcademyBootFailedThisSession);
        Assert.Equal("done", ok.CheckArcademyBootDeadline(DateTime.UtcNow.AddSeconds(500)));
        Assert.False(ok.IsClosedOrClosing);

        // The page's own boot-error closes the window and sets the same latch.
        ok.HandleMessage("{\"type\":\"boot-error\",\"msg\":\"shell failed\"}");
        Assert.True(ok.IsClosedOrClosing);
        Assert.True(GameWindow.ArcademyBootFailedThisSession);
    });

    [Fact]
    public Task EndRun_TheHostAsksThePageToWindDown_ThenExitDoneCloses() => Campus(c =>
    {
        var w = c.Open(true);
        w.ArcademyCloseActive();
        Assert.Equal("host", (string?)c.Last("end-run")["reason"]);
        Assert.True(w.ArcademyExiting);
        Assert.False(w.IsClosedOrClosing);                      // the 1200 ms watchdog or exit-done closes it
        w.ArcademyCloseActive();
        Assert.Equal(1, c.Count("end-run"));                    // idempotent
        w.HandleMessage("{\"type\":\"exit-done\"}");
        Assert.True(w.IsClosedOrClosing);

        var booting = c.Open(false);
        booting.ArcademyCloseActive();                          // nothing to ask: close now
        Assert.True(booting.IsClosedOrClosing);
    });

    // ---- student ID: link + share -------------------------------------------------------------

    [Fact]
    public Task LinkDiscord_OneClickAppliesTheDiscordRung_AndEveryOutcomeAnswersTheChip() => Campus(c =>
    {
        var s = CoreSettings.Current;
        s.ArcademyPresenceShare = "off";
        var w = c.Open(true);

        w.ArcLinkFlowOverride = _ => Task.FromResult(true);
        w.HandleMessage("{\"type\":\"link-discord\"}");
        Pump(() => c.Posted.Any(p => (string?)p["type"] == "profile" && p["result"]?.Type == JTokenType.String));
        Assert.Equal("linked", (string?)c.Posted.Last(p => (string?)p["type"] == "profile" && p["result"]?.Type == JTokenType.String)["result"]);
        Assert.Equal("discord", s.ArcademyPresenceShare);

        c.Posted.Clear();
        w.ArcLinkFlowOverride = _ => Task.FromResult(false);
        w.HandleMessage("{\"type\":\"link-discord\"}");
        Pump(() => c.Posted.Any(p => (string?)p["type"] == "profile" && p["result"]?.Type == JTokenType.String));
        Assert.Equal("cancelled", (string?)c.Posted.Last(p => p["result"]?.Type == JTokenType.String)["result"]);

        c.Posted.Clear();
        w.ArcLinkFlowOverride = _ => Task.FromException<bool>(new InvalidOperationException("sign in to the app first"));
        w.HandleMessage("{\"type\":\"link-discord\"}");
        Pump(() => c.Posted.Any(p => (string?)p["type"] == "profile" && p["result"]?.Type == JTokenType.String));
        Assert.Equal("failed", (string?)c.Posted.Last(p => p["result"]?.Type == JTokenType.String)["result"]);

        // An open link-up is part of what the emergency stop stops: the chip hears "cancelled".
        c.Posted.Clear();
        var never = new TaskCompletionSource<bool>();
        w.ArcLinkFlowOverride = ct => { ct.Register(() => never.TrySetCanceled()); return never.Task; };
        w.HandleMessage("{\"type\":\"link-discord\"}");
        Assert.False(w.ArcademyPanicPress(DateTime.UtcNow));     // one press: the caller closes, the chip still hears it
        Assert.Equal("cancelled", (string?)c.Posted.Last(p => (string?)p["type"] == "profile")["result"]);
        Pump(() => never.Task.IsCompleted);
        Assert.True(never.Task.IsCanceled);
        Assert.Equal(1, c.Posted.Count(p => (string?)p["type"] == "profile" && p["result"]?.Type == JTokenType.String));
    });

    [Fact]
    public Task Profile_SendsNoPhotoBelowTheDiscordRung() => Campus(c =>
    {
        CoreSettings.Current.ArcademyPresenceShare = "off";
        c.Open(true);
        var profile = c.Last("init")["profile"]!;
        Assert.Equal(JTokenType.Null, profile["avatarUrl"]!.Type);
        Assert.Equal("off", (string?)profile["presenceShare"]);
    });

    [Fact]
    public void AvatarEncode_ScalesFlattensAndFitsTheCap()
    {
        using var bmp = new SkiaSharp.SKBitmap(256, 256);
        using (var canvas = new SkiaSharp.SKCanvas(bmp)) canvas.Clear(new SkiaSharp.SKColor(200, 40, 120, 128));
        using var png = SkiaSharp.SKImage.FromBitmap(bmp).Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        var jpeg = GameWindow.ArcEncodeAvatarJpeg(png.ToArray(), 128, 85);
        Assert.NotNull(jpeg);
        Assert.True(jpeg![0] == 0xFF && jpeg[1] == 0xD8, "not a JPEG");
        Assert.True(jpeg.Length < ArcademyAvatarCache.MaxDataUriChars * 3 / 4);
        using var back = SkiaSharp.SKBitmap.Decode(jpeg);
        Assert.Equal(128, back.Width);
        Assert.Null(GameWindow.ArcEncodeAvatarJpeg(new byte[] { 1, 2, 3 }, 128, 85));
    }

    [Fact]
    public Task ShareImage_AlwaysAnswersOnce_AndOnlyARealPngReachesTheClipboard() => Campus(c =>
    {
        var w = c.Open(true);
        byte[]? got = null;
        w.ArcClipboardOverride = bytes => { got = bytes; return Task.FromResult(true); };

        w.HandleMessage("{\"type\":\"share-image\",\"png\":\"not base64 !!\"}");
        Assert.False((bool)c.Last("share-image-result")["ok"]!);
        w.HandleMessage("{\"type\":\"share-image\",\"png\":\"" + Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5 }) + "\"}");
        Assert.False((bool)c.Last("share-image-result")["ok"]!);        // a JPEG is not a share card
        w.HandleMessage("{\"type\":\"share-image\"}");
        Assert.False((bool)c.Last("share-image-result")["ok"]!);
        Assert.Null(got);

        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 };
        w.HandleMessage("{\"type\":\"share-image\",\"png\":\"" + Convert.ToBase64String(png) + "\"}");
        Assert.True((bool)c.Last("share-image-result")["ok"]!);
        Assert.Equal(png, got);
        Assert.Equal(4, c.Count("share-image-result"));

        w.ArcClipboardOverride = _ => Task.FromResult(false);           // a clipboard that takes no image
        w.HandleMessage("{\"type\":\"share-image\",\"png\":\"" + Convert.ToBase64String(png) + "\"}");
        Assert.False((bool)c.Last("share-image-result")["ok"]!);        // the page falls to its download rung
        Assert.Null(GameWindow.ArcDecodeSharePng(new string('A', GameWindow.ArcMaxShareImageChars + 4)));
    });

    // ---- init: clips, hosts, flags ------------------------------------------------------------

    [Fact]
    public Task Init_CarriesTriggersWithClipsOrNull_AndTheHostFlags() => Campus(c =>
    {
        c.Open(true);
        var init = c.Last("init");
        Assert.Equal(JTokenType.Boolean, init["platform"]!["hasHaptics"]!.Type);
        Assert.Equal(ConditioningControlPanel.Avalonia.Views.Controls.WebHost.AutoplayWithoutGesture, (bool)init["autoplayOk"]!);
        var words = (JArray)init["words"]!;
        var triggers = (JArray)init["triggers"]!;
        Assert.Equal(words.Count, triggers.Count);              // triggers always describe words
        Assert.All(triggers, t => Assert.True(t["audio"]!.Type is JTokenType.Null or JTokenType.String));
        // The three audio / spiral hosts are routes on the asset server once the campus is up.
        foreach (var host in new[] { GameWindow.ArcSubAudioHost, GameWindow.ArcModAudioHost, GameWindow.ArcSpiralsHost })
            Assert.True(WebAssetServer.Shared.Hosts.ContainsKey(host), host);
    });

    [Fact]
    public Task Triggers_ABambiClipIsOnlyOfferedToAModThePolicyAllows_AndNeverWhenMuted() => Campus(c =>
    {
        var s = CoreSettings.Current;
        bool subOn = s.SubAudioEnabled, subMuted = s.SubAudioMuted;
        var modWas = CoreMods.ActiveModIdProvider;
        var shared = Path.Combine(AppContext.BaseDirectory, "Resources", "sub_audio");
        var clip = Directory.Exists(shared) ? Directory.EnumerateFiles(shared, "*.mp3").FirstOrDefault() : null;
        try
        {
            s.SubAudioEnabled = true; s.SubAudioMuted = false;
            CoreMods.ActiveModIdProvider = () => BuiltInMods.CCPDefaultId;
            var phrase = clip != null ? Path.GetFileNameWithoutExtension(clip) : "GOOD GIRL";
            dynamic row = GameWindow.BuildArcademyTriggers(new[] { phrase }, (host, file) => host + "/" + file)[0];
            Assert.Null((string?)row.audio);                    // CCP Default never borrows the Bambi clips
            Assert.Null(WebAssetServer.Shared.Hosts.TryGetValue(GameWindow.ArcSubAudioHost, out var probe) ? probe() : null);

            if (clip != null)
            {
                CoreMods.ActiveModIdProvider = () => BuiltInMods.BambiSleepId;
                row = GameWindow.BuildArcademyTriggers(new[] { phrase }, (host, file) => host + "/" + file)[0];
                Assert.Equal(GameWindow.ArcSubAudioHost + "/" + Path.GetFileName(clip), (string?)row.audio);
                s.SubAudioMuted = true;                      // the whisper mute: text only
                row = GameWindow.BuildArcademyTriggers(new[] { phrase }, (host, file) => host + "/" + file)[0];
                Assert.Null((string?)row.audio);
            }
            // A phrase with no recorded clip is a text row: nothing is ever synthesised.
            s.SubAudioEnabled = true; s.SubAudioMuted = false;
            row = GameWindow.BuildArcademyTriggers(new[] { "a phrase nobody recorded" }, (host, file) => host + "/" + file)[0];
            Assert.Null((string?)row.audio);
        }
        finally
        {
            CoreMods.ActiveModIdProvider = modWas;
            s.SubAudioEnabled = subOn; s.SubAudioMuted = subMuted;
        }
        c.Open(true);                                           // registers the hosts for the probe above on a cold run
    });

    [Fact]
    public void AssetServerHosts_ServeFlatClipsAndSpiralsOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-arc-hosts-" + Guid.NewGuid().ToString("N"));
        var web = Path.Combine(root, "web");
        var spirals = Path.Combine(root, "spirals");
        Directory.CreateDirectory(web);
        Directory.CreateDirectory(Path.Combine(spirals, "sub"));
        File.WriteAllText(Path.Combine(web, "index.html"), "x");
        File.WriteAllBytes(Path.Combine(spirals, "loom_a.gif"), new byte[] { 1 });
        File.WriteAllText(Path.Combine(spirals, "loom_a.json"), "{}");
        File.WriteAllBytes(Path.Combine(spirals, "sub", "deep.gif"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(root, "outside.gif"), new byte[] { 1 });
        using var server = new WebAssetServer(web);
        try
        {
            Assert.Null(server.ResolveFile("/ccp.spirals/loom_a.gif"));          // not mapped yet
            string? folder = spirals;
            server.Hosts["ccp.spirals"] = () => folder;
            Assert.Equal(Path.Combine(spirals, "loom_a.gif"), server.ResolveFile("/ccp.spirals/loom_a.gif"));
            Assert.Null(server.ResolveFile("/ccp.spirals/loom_a.json"));         // params stay private
            Assert.Null(server.ResolveFile("/ccp.spirals/sub/deep.gif"));        // flat only
            Assert.Null(server.ResolveFile("/ccp.spirals/..%2Foutside.gif"));
            Assert.Null(server.ResolveFile("/ccp.spirals/missing.gif"));
            Assert.EndsWith("/ccp.spirals/loom%20b.gif", server.HostUrl("ccp.spirals", "loom b.gif"));
            folder = null;                                                       // the policy says no
            Assert.Null(server.ResolveFile("/ccp.spirals/loom_a.gif"));
            Assert.NotNull(server.ResolveFile("/index.html"));                   // the page root is untouched
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public Task Launch_IsRefusedOnAnAudioOnlyDay() => Campus(c =>
    {
        CoreSettings.Current.AudioOnlySession = false;
        Assert.True(GameWindow.ArcademyLaunchAllowed());
        CoreSettings.Current.AudioOnlySession = true;
        Assert.False(GameWindow.ArcademyLaunchAllowed());
    });

    [Fact]
    public Task LockerWrite_TellsTheDeskAtOnce_AndTheOpenCampusIsTheLiveWallet() => Campus(c =>
    {
        int raised = 0;
        Action onChange = () => raised++;
        ArcademyHostService.EmiOutfitChanged += onChange;
        try
        {
            var w = c.Open(true);
            Assert.Same(w.ArcademyMeta, ArcademyHostService.LiveMeta);
            w.HandleMessage("{\"type\":\"meta-command\",\"op\":\"set\",\"key\":\"emiName\",\"value\":\"Dot\"}");
            Assert.Equal(0, raised);
            w.HandleMessage("{\"type\":\"meta-command\",\"op\":\"set\",\"key\":\"lockerOutfit\",\"value\":\"swim\"}");
            Assert.Equal(1, raised);
            Assert.Null(ArcademyHostService.EquippedEmiOutfit());   // armed but not bought: standard art
            w.Close();
            Assert.Null(ArcademyHostService.LiveMeta);
            Assert.Equal(2, raised);                                // the close is the backstop
        }
        finally { ArcademyHostService.EmiOutfitChanged -= onChange; }
    });
}
