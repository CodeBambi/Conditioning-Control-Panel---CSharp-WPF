using System;
using System.Collections.Generic;
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
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Stakes;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The chess host (GameWindow.Pbp.cs, WPF PieceByPieceHostService 7.1.5), one test per frame
/// family: page frame in, expected frame or state out. Swaps process-wide seams, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class PbpHostTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static (GameWindow W, List<JObject> Posted) Board()
    {
        EnsureApp();
        var w = new GameWindow(GameWindow.Games[GameWindow.PbpId]);
        var posted = new List<JObject>();
        w.Posted += json => posted.Add(JObject.Parse(json));
        w.Show();
        return (w, posted);
    }

    private static List<JObject> Of(List<JObject> posted, string type) =>
        posted.Where(p => (string?)p["type"] == type).ToList();

    /// <summary>No online pictures for the test: the switch off posts 'off' and never builds a pool.</summary>
    private static IDisposable NoOnlinePictures()
    {
        var s = CoreSettings.Current;
        bool was = s.PbpMediaOnline;
        s.PbpMediaOnline = false;
        return new Restore(() => { s.PbpMediaOnline = was; CoreSettings.SaveImmediate(); });
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Seen = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Seen) Seen.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"tables\":[]}") });
        }
    }

    private sealed class NoAccountStakes : IStakeApi
    {
        public string? Account() => null;
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default) => Task.FromResult<JObject?>(null);
    }

    [Fact]
    public async Task Ready_PostsSettingsThenIdentity_OncePerBoot_ThenMediaState()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            using var _ = NoOnlinePictures();
            var (w, posted) = Board();
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                w.HandleMessage("{\"type\":\"ready\"}");
                var settings = Assert.Single(Of(posted, "pbp:settings"));
                Assert.InRange((int)settings["videoHoldSec"]!, 10, 30);
                Assert.NotNull(settings["reducedMotion"]);
                Assert.IsType<JArray>(settings["whispers"]);

                var identity = Assert.Single(Of(posted, "pbp:identity"));
                Assert.True(posted.IndexOf(settings) < posted.IndexOf(identity));
                Assert.True((bool)identity["net"]!["viaHost"]!);
                Assert.Equal(CoreAccount.UnifiedUserId ?? "", (string?)identity["unifiedId"]);
                // Online needs the account AND its token; with neither, solo play is all there is.
                bool both = !string.IsNullOrEmpty(CoreAccount.UnifiedUserId) && !string.IsNullOrEmpty(CoreSettings.Current.AuthToken);
                Assert.Equal(both, (bool)identity["online"]!);
                // The host hands over who is playing and nothing about anyone's rating.
                Assert.DoesNotContain(identity.Properties(), p => p.Name.Contains("iq", StringComparison.OrdinalIgnoreCase));

                var state = Of(posted, "pbp:media-state").Last();
                Assert.False((bool)state["online"]!);
                Assert.True((bool)state["library"]!);
                Assert.Equal("off", (string?)Of(posted, "pbp:online-media").Last()["state"]);
                // The shell still answers ready with its own init.
                Assert.Contains(posted, p => (string?)p["type"] == "init");
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Net_OutsideThePbpRoutesIsRefusedAtOnce_AndAnAllowedCallIsAnsweredWithItsId()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var handler = new FakeHandler();
            var http = GameWindow.PbpHttpClient;
            var baseUrl = GameWindow.PbpServerBase;
            GameWindow.PbpHttpClient = () => new HttpClient(handler);
            GameWindow.PbpServerBase = () => "https://example.test";
            var (w, posted) = Board();
            try
            {
                w.HandleMessage("{\"type\":\"pbp:net\",\"id\":\"n1\",\"method\":\"POST\",\"path\":\"/v2/stakes/offer\",\"body\":{}}");
                var refused = Assert.Single(Of(posted, "pbp:net-result"));
                Assert.Equal("n1", (string?)refused["id"]);
                Assert.Equal(0, (int)refused["status"]!);
                Assert.Equal("forbidden_path", (string?)refused["body"]);
                Assert.Empty(handler.Seen);

                w.HandleMessage("{\"type\":\"pbp:net\",\"id\":\"n2\",\"method\":\"GET\",\"path\":\"/v2/pbp/lobby?unified_id=u_1\",\"body\":null}");
                for (int i = 0; i < 200 && Of(posted, "pbp:net-result").Count < 2; i++) await Task.Delay(10);
                var ok = Of(posted, "pbp:net-result").Single(r => (string?)r["id"] == "n2");
                Assert.Equal(200, (int)ok["status"]!);
                Assert.Equal("{\"ok\":true,\"tables\":[]}", (string?)ok["body"]);
                Assert.Equal("https://example.test/v2/pbp/lobby?unified_id=u_1", handler.Seen.Single().RequestUri!.ToString());
            }
            finally
            {
                w.Close();
                GameWindow.PbpHttpClient = http;
                GameWindow.PbpServerBase = baseUrl;
            }
        });
    }

    [Fact]
    public async Task MediaRequest_AnswersFromTheLibrary_GifsSplitOut_AndAnEmptyLibraryStillAnswers()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var library = GameWindow.PbpLibrary;
            var (w, posted) = Board();
            try
            {
                GameWindow.PbpLibrary = () => new[]
                {
                    ("a.jpg", "http://h/a.jpg", true), ("b.gif", "http://h/b.gif", true), ("c.mp4", "http://h/c.mp4", false),
                };
                w.HandleMessage("{\"type\":\"pbp:media-request\",\"kinds\":[\"image\",\"gif\",\"video\"],\"count\":24}");
                var media = Assert.Single(Of(posted, "pbp:media"));
                Assert.Equal("http://h/a.jpg", (string?)media["images"]!.Single());
                Assert.Equal("http://h/b.gif", (string?)media["gifs"]!.Single());
                Assert.Equal("http://h/c.mp4", (string?)media["videos"]!.Single());

                GameWindow.PbpLibrary = () => Array.Empty<(string, string, bool)>();
                w.HandleMessage("{\"type\":\"pbp:media-request\"}");
                var empty = Of(posted, "pbp:media").Last();
                Assert.Empty((JArray)empty["images"]!);
                Assert.Empty((JArray)empty["gifs"]!);
                Assert.Empty((JArray)empty["videos"]!);
            }
            finally { w.Close(); GameWindow.PbpLibrary = library; }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task LobbyIntent_WaitsForIdentityOnAFreshBoard_AndGoesAtOnceToOneAlreadyUp()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            using var _ = NoOnlinePictures();
            var launch = GameWindow.PbpLaunch;
            GameWindow? w = null;
            List<JObject> posted = new();
            GameWindow.PbpLaunch = () =>
            {
                if (w == null) (w, posted) = Board();
                return w;
            };
            try
            {
                // A bad id never opens a board.
                Assert.False(GameWindow.PbpJoinOpenTable("not-a-table"));
                Assert.False(GameWindow.PbpJoinOpenTable("c_0123456789abcdef"));
                Assert.Null(w);

                // Fresh launch: nothing goes out until the page has its identity.
                Assert.True(GameWindow.PbpJoinOpenTable("p_table9"));
                Assert.Empty(Of(posted, "pbp:friend"));
                w!.HandleMessage("{\"type\":\"ready\"}");
                var join = Assert.Single(Of(posted, "pbp:friend"));
                Assert.Equal("join", (string?)join["mode"]);
                Assert.Equal("p_table9", (string?)join["target"]);
                Assert.True(posted.IndexOf(Of(posted, "pbp:identity").Single()) < posted.IndexOf(join));

                // A board already up and talking takes the next one now, and only once.
                Assert.True(GameWindow.PbpHostOpenTable());
                var frames = Of(posted, "pbp:friend");
                Assert.Equal(2, frames.Count);
                Assert.Equal("host", (string?)frames[1]["mode"]);
                w.HandleMessage("{\"type\":\"ready\"}");
                Assert.Equal(2, Of(posted, "pbp:friend").Count);

                // The Lobby's own doors land on the same frames.
                MainShellWindow.LobbyJoinChess("p_other");
                Assert.Equal("p_other", (string?)Of(posted, "pbp:friend").Last()["target"]);
                MainShellWindow.LobbyHostChess();
                Assert.Equal("host", (string?)Of(posted, "pbp:friend").Last()["mode"]);
            }
            finally { w?.Close(); GameWindow.PbpLaunch = launch; }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task FriendChallenge_ThePagesIdComesBack_ABadOneIsNull_AndTheFriendsTileIsOpen()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            using var _ = NoOnlinePictures();
            var launch = GameWindow.PbpLaunch;
            GameWindow? w = null;
            List<JObject> posted = new();
            GameWindow.PbpLaunch = () =>
            {
                if (w == null) (w, posted) = Board();
                return w;
            };
            try
            {
                // The friends drawer's chess tile is no longer shut, and its challenge door is the board.
                Assert.True(FriendsInviteCodes.HostsChess());
                Assert.Null(FriendsInviteCodes.BlockedKey(InviteDestination.Chess));

                var task = FriendsInviteCodes.ChallengeFriend("u_friend", TimeSpan.FromSeconds(30));
                Assert.Same(task, GameWindow.PbpChallengeFriendAsync("u_friend", TimeSpan.FromSeconds(30)));
                w!.HandleMessage("{\"type\":\"ready\"}");
                var challenge = Assert.Single(Of(posted, "pbp:friend"));
                Assert.Equal("challenge", (string?)challenge["mode"]);
                Assert.Equal("u_friend", (string?)challenge["friendId"]);
                w.HandleMessage("{\"type\":\"pbp:friend-challenge\",\"friendId\":\"u_friend\",\"challengeId\":\"c_0123456789abcdef\"}");
                Assert.Equal("c_0123456789abcdef", await task);

                // The page could not make one: null, and never a string that is not a challenge id.
                var second = GameWindow.PbpChallengeFriendAsync("u_friend", TimeSpan.FromSeconds(30));
                w.HandleMessage("{\"type\":\"pbp:friend-challenge\",\"friendId\":\"u_friend\",\"challengeId\":\"<b>hello</b>\"}");
                Assert.Null(await second);

                // The friend's side: accept goes out as its own frame; a bad id never does.
                GameWindow.PbpJoinFriendChallenge("nope");
                GameWindow.PbpJoinFriendChallenge("c_0123456789abcdef");
                var accept = Of(posted, "pbp:friend").Last();
                Assert.Equal("accept", (string?)accept["mode"]);
                Assert.Equal("c_0123456789abcdef", (string?)accept["challengeId"]);
                Assert.Equal(3, Of(posted, "pbp:friend").Count);

                // A board that closes with a challenge out answers null instead of hanging the drawer.
                var third = GameWindow.PbpChallengeFriendAsync("u_other", TimeSpan.FromSeconds(30));
                w.Close();
                Assert.Null(await third);
            }
            finally { w?.Close(); GameWindow.PbpLaunch = launch; }
        });
    }

    [Fact]
    public async Task Stakes_AStakeFrameIsAnsweredByTheSharedBridge_AsGamePbp()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (w, posted) = Board();
            var settlement = new StakeSettlement((_, _) => 0, null, () => null);
            GameWindow.PbpStakes = new StakeBridge("pbp", o => w.Post(o), new NoAccountStakes(), settlement, () => true, () => { });
            try
            {
                w.HandleMessage("{\"type\":\"stake-limits\"}");
                for (int i = 0; i < 200 && Of(posted, "stake").Count < 1; i++) await Task.Delay(10);
                var limits = Assert.Single(Of(posted, "stake"));
                Assert.Equal("limits", (string?)limits["op"]);
                Assert.Equal("pbp", (string?)limits["game"]);
                Assert.False((bool)limits["ok"]!);
                Assert.Equal("signin", (string?)limits["reason"]);
                Assert.NotNull(limits["labels"]);

                // An offer with no match is refused on the host side and never sent anywhere.
                w.HandleMessage("{\"type\":\"stake-offer\",\"kind\":\"sp\",\"amount\":50}");
                for (int i = 0; i < 200 && Of(posted, "stake").Count < 2; i++) await Task.Delay(10);
                Assert.Equal("no_match", (string?)Of(posted, "stake").Last()["reason"]);
            }
            finally { w.Close(); GameWindow.PbpStakes = null!; }
        });
    }

    [Fact]
    public async Task Watchdog_SilentPageGetsOnePing_ThenCloses_AndAPongKeepsItOpen()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            using var _ = NoOnlinePictures();
            var (w, posted) = Board();
            try
            {
                var t0 = DateTime.UtcNow;
                Assert.Equal("idle", w.CheckPbpWatch(t0.AddSeconds(30)));   // loading, inside the boot deadline
                w.HandleMessage("{\"type\":\"ready\"}");
                w.HandleMessage("{\"type\":\"heartbeat\"}");
                Assert.Equal("ok", w.CheckPbpWatch(DateTime.UtcNow.AddSeconds(10)));
                Assert.Equal("ping", w.CheckPbpWatch(DateTime.UtcNow.AddSeconds(21)));
                Assert.Single(Of(posted, "ping"));
                w.HandleMessage("{\"type\":\"pong\"}");
                Assert.Equal("ok", w.CheckPbpWatch(DateTime.UtcNow.AddSeconds(5)));
                Assert.Equal("ping", w.CheckPbpWatch(DateTime.UtcNow.AddSeconds(21)));
                Assert.Equal("closed", w.CheckPbpWatch(DateTime.UtcNow.AddSeconds(26)));
                Assert.True(w.IsClosedOrClosing);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task BootDeadline_APageThatNeverSpeaksIsClosed_AndTheFlagClearsOnTheNextGoodBoot()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            using var _ = NoOnlinePictures();
            var (dead, _) = Board();
            try
            {
                Assert.Equal("boot-timeout", dead.CheckPbpWatch(DateTime.UtcNow.AddSeconds(46)));
                Assert.True(dead.IsClosedOrClosing);
                Assert.True(GameWindow.PbpBootFailedThisSession);
            }
            finally { dead.Close(); }

            var (good, _) = Board();
            try
            {
                good.HandleMessage("{\"type\":\"ready\"}");
                Assert.False(GameWindow.PbpBootFailedThisSession);
                // Esc / Leave on the menu: the frame is the close.
                good.HandleMessage("{\"type\":\"pbp:exit\"}");
                Assert.True(good.IsClosedOrClosing);
            }
            finally { good.Close(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>IB3: the whisper fallback hosts are routes on the asset server (the request gate), one
    /// plain clip name each; a host that is not registered is never handed to the page.</summary>
    [Fact]
    public void WhisperFallbackHosts_AreServedOnlyOnceRegistered_OnePlainClipNameEach()
    {
        var web = System.IO.Directory.CreateTempSubdirectory("ccp-pbp-whisper-").FullName;
        try
        {
            using var server = new ConditioningControlPanel.Avalonia.Platform.WebAssetServer(web);
            Assert.Null(GameWindow.PbpPageUrl("https://ccp.words/hello.mp3", server));
            Assert.Null(GameWindow.PbpPageUrl("https://ccp.subaudio/x.mp3", server));

            GameWindow.RegisterPbpWhisperHosts(server);
            Assert.EndsWith("/ccp.words/hello.mp3", GameWindow.PbpPageUrl("https://ccp.words/hello.mp3", server));
            Assert.EndsWith("/ccp.subaudio/good%20girl.mp3", GameWindow.PbpPageUrl("https://ccp.subaudio/good%20girl.mp3", server));
            Assert.Null(GameWindow.PbpPageUrl("https://ccp.words/..%2Fsecret.mp3", server));     // one plain name
            Assert.Null(GameWindow.PbpPageUrl("https://ccp.words/sub/hello.mp3", server));
            Assert.Null(GameWindow.PbpPageUrl("https://ccp.words/settings.json", server));       // clips only
            Assert.Null(GameWindow.PbpPageUrl("https://ccp.words.evil.example/hello.mp3", server));

            // The gate answers for the two names, and a file that is not there is refused.
            Assert.True(server.IsVirtual(new Uri("https://ccp.words/hello.mp3")));
            Assert.Null(server.ResolveVirtual(new Uri("https://ccp.words/no-such-clip-k26.mp3"), out _, out _));
            Assert.Null(server.ResolveVirtual(new Uri("https://ccp.words/..%2F..%2Fsettings.json"), out _, out _));
        }
        finally { try { System.IO.Directory.Delete(web, true); } catch { } }
    }

    [Fact]
    public async Task PageUrl_OnlyCcpAssetsIsServed_AndTheRelativePathSurvivesEscaping()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            Assert.Null(GameWindow.PbpPageUrl("https://evil.example/a.jpg"));
            var url = GameWindow.PbpPageUrl("https://ccp.assets/braindrain/my%20clip.mp3");
            Assert.NotNull(url);
            Assert.StartsWith("http://127.0.0.1:", url);
            Assert.EndsWith("/ccp.assets/braindrain/my%20clip.mp3", url);
            Assert.EndsWith("/ccp.assets/.temp/ccp_remote_ab.webm", GameWindow.PbpPageUrl("https://ccp.assets/.temp/ccp_remote_ab.webm"));
            return Task.CompletedTask;
        });
    }
}
