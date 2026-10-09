using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Games.BackRoom;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Parity wave 3, r6 (owner desk report 2026-10-09): Esc in Breakout opened from the launcher
/// closed the window (it fired the panic), and Breakout's bubbles were empty (the room dealt the null
/// media seam, on ccp.game urls the loopback server never answers).</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class GameEscapeAndMediaTests
{
    private static readonly DateTime T0 = new(2026, 10, 9, 20, 0, 0, DateTimeKind.Utc);

    private static (GameWindow.EscapeCandidate Game, List<string> Posted) Game(string id, bool inFront = true, bool ready = true)
    {
        var posted = new List<string>();
        return (new GameWindow.EscapeCandidate(id, inFront, ready, o => posted.Add((string)JObject.FromObject(o)["type"]!)), posted);
    }

    [Fact]
    public void EscapeInFrontOfBreakoutIsItsPauseAndTheSecondWithin2sIsAPanic()
    {
        GameWindow.ResetEscapeClaimForTest();
        try
        {
            var (bo, posted) = Game("breakout");
            Assert.True(GameWindow.TryKeepEscapeAsPause(new[] { bo }, "Escape", engineRunning: false, lockCardOpen: false, T0));
            Assert.Equal(new[] { "kept-escape" }, posted);
            // A quick second Escape is a full panic (getting out is two taps at most), and resets the claim.
            Assert.False(GameWindow.TryKeepEscapeAsPause(new[] { bo }, "Escape", false, false, T0.AddMilliseconds(800)));
            Assert.True(GameWindow.TryKeepEscapeAsPause(new[] { bo }, "Escape", false, false, T0.AddSeconds(1)));
            Assert.True(GameWindow.TryKeepEscapeAsPause(new[] { bo }, "Escape", false, false, T0.AddSeconds(4)));
            // The demo door rides the same rule.
            GameWindow.ResetEscapeClaimForTest();
            var (demo, demoPosted) = Game("breakoutdemo");
            Assert.True(GameWindow.TryKeepEscapeAsPause(new[] { demo }, "Escape", false, false, T0));
            Assert.Equal(new[] { "kept-escape" }, demoPosted);
        }
        finally { GameWindow.ResetEscapeClaimForTest(); }
    }

    [Fact]
    public void PanicStaysPanicEverywhereWpfSaysSo()
    {
        GameWindow.ResetEscapeClaimForTest();
        try
        {
            var (bo, posted) = Game("breakout");
            Assert.False(GameWindow.TryKeepEscapeAsPause(new[] { bo }, "F12", false, false, T0));                 // a rebound key is always a panic
            Assert.False(GameWindow.TryKeepEscapeAsPause(new[] { bo }, "Escape", engineRunning: true, false, T0)); // a session is running
            Assert.False(GameWindow.TryKeepEscapeAsPause(new[] { bo }, "Escape", false, lockCardOpen: true, T0)); // a Lock Card is up
            var (behind, _) = Game("breakout", inFront: false);
            Assert.False(GameWindow.TryKeepEscapeAsPause(new[] { behind }, "Escape", false, false, T0));          // not the window in front
            var (room, _) = Game("backroom");
            Assert.False(GameWindow.TryKeepEscapeAsPause(new[] { room }, "Escape", false, false, T0));            // the casino is not a paused game in WPF
            Assert.False(GameWindow.TryKeepEscapeAsPause(Array.Empty<GameWindow.EscapeCandidate>(), "Escape", false, false, T0));
            Assert.Empty(posted);
        }
        finally { GameWindow.ResetEscapeClaimForTest(); }
    }

    [Fact]
    public void TheKeptPressGoesOnlyToAReadyPageWithItsOwnFrame()
    {
        GameWindow.ResetEscapeClaimForTest();
        try
        {
            // Booting page: the press is still kept (no panic), but nothing is queued to pause it later.
            var (booting, bootPosted) = Game("breakout", ready: false);
            Assert.True(GameWindow.TryKeepEscapeAsPause(new[] { booting }, "Escape", false, false, T0));
            Assert.Empty(bootPosted);

            GameWindow.ResetEscapeClaimForTest();
            var (board, boardPosted) = Game("piecebypiece");
            Assert.True(GameWindow.TryKeepEscapeAsPause(new[] { board }, "Escape", false, false, T0));
            Assert.Equal(new[] { "pbp:escape" }, boardPosted);

            GameWindow.ResetEscapeClaimForTest();
            var (race, racePosted) = Game("race");
            Assert.True(GameWindow.TryKeepEscapeAsPause(new[] { race }, "Escape", false, false, T0));
            Assert.Equal(new[] { "kept-escape" }, racePosted);
        }
        finally { GameWindow.ResetEscapeClaimForTest(); }
    }

    [Fact]
    public void DealtUrlsMoveOntoTheLoopbackServer()
    {
        string Page(string rel) => "http://127.0.0.1:5/" + rel + "?ccp_t=T";
        string Asset(string rel) => "http://127.0.0.1:5/ccp.assets/" + string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));
        Assert.Equal("http://127.0.0.1:5/backroom/stations/slot/fallback/gif0.webp?ccp_t=T",
            BackRoomMedia.ToLoopback("https://ccp.game/backroom/stations/slot/fallback/gif0.webp", Page, Asset));
        Assert.Equal("http://127.0.0.1:5/ccp.assets/.temp/ccp_remote_a.mp4",
            BackRoomMedia.ToLoopback("https://ccp.assets/.temp/ccp_remote_a.mp4", Page, Asset));
        // Escaped once, never twice; the animated-webp hint stays a fragment.
        Assert.Equal("http://127.0.0.1:5/ccp.assets/images/my%20loop.webp#.gif",
            BackRoomMedia.ToLoopback("https://ccp.assets/images/my%20loop.webp#.gif", Page, Asset));
        Assert.Equal("https://elsewhere/x.gif", BackRoomMedia.ToLoopback("https://elsewhere/x.gif", Page, Asset));
    }

    private sealed class WarmPool : IBackRoomRemotePool
    {
        public bool Wanted => true;
        public void EnsureWarm() { }
        public Task WarmAsync(CancellationToken ct) => Task.CompletedTask;
        public IReadOnlyList<BackRoomRemoteClip> Ready() => new[]
        {
            new BackRoomRemoteClip("a", "https://ccp.assets/.temp/ccp_remote_a.mp4", 320, 240),
            new BackRoomRemoteClip("b", "https://ccp.assets/.temp/ccp_remote_b.webm", 320, 240),
        };
        public void Drain() { }
    }

    [Fact]
    public async Task AnOnlineRoomDealsTheWarmScrolllerClipsOnLoopback()
    {
        var s = new AppSettings { MediaSource = "online", RemoteMediaConsented = true };
        var media = new BackRoomMedia(() => Array.Empty<string>(), () => Array.Empty<string>(), () => null, null, () => null,
            () => s, () => new WarmPool());
        var live = new LoopbackBackRoomMedia(media, rel => "http://127.0.0.1:5/" + rel, rel => "http://127.0.0.1:5/ccp.assets/" + rel);
        var deal = await live.DealAsync("breakout", 7, 4);
        Assert.Equal("online", deal.Source);
        Assert.NotEmpty(deal.Gifs);
        Assert.All(deal.Gifs, g => Assert.StartsWith("http://127.0.0.1:5/ccp.assets/.temp/ccp_remote_", g.Url));
        Assert.All(deal.Gifs, g => Assert.Equal("online", g.Src));

        // No consent: the room never deals remote media, whatever the source says (WPF EffectiveMediaSource).
        var refused = new AppSettings { MediaSource = "online", RemoteMediaConsented = false, FypOnlineConsented = false };
        Assert.Equal("local", BackRoomMedia.EffectiveMediaSource(refused));
        var none = new BackRoomMedia(() => Array.Empty<string>(), () => Array.Empty<string>(), () => null, null, () => null,
            () => refused, () => new WarmPool());
        var fallback = await new LoopbackBackRoomMedia(none, rel => "http://127.0.0.1:5/" + rel, rel => "http://127.0.0.1:5/ccp.assets/" + rel)
            .DealAsync("breakout", 7, 4);
        // Nothing local either: the four built-in loops, on the loopback server rather than a dead ccp.game url.
        Assert.Equal(4, fallback.Gifs.Count);
        Assert.All(fallback.Gifs, g => Assert.StartsWith("http://127.0.0.1:5/backroom/stations/slot/fallback/gif", g.Url));
    }

    /// <summary>The warm clips land in {assets}/.temp: WPF maps ccp.assets over the whole folder, so the
    /// loopback server serves .temp's own top-level media (no symlinks here, so it runs on Windows too).</summary>
    [Fact]
    public void TheAssetServerServesTheWarmClipsInTemp()
    {
        var web = System.IO.Directory.CreateTempSubdirectory("r6-web-").FullName;
        var assets = System.IO.Directory.CreateTempSubdirectory("r6-assets-").FullName;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(assets, ".temp", "sub"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(assets, ".packs"));
            foreach (var f in new[] { ".temp/ccp_remote_a.mp4", ".temp/sub/s.mp4", ".temp/.hidden.mp4", ".temp/notes.txt", ".packs/p.png" })
                System.IO.File.WriteAllText(System.IO.Path.Combine(assets, f), "x");
            using var server = new ConditioningControlPanel.Avalonia.Platform.WebAssetServer(web) { AssetsRoot = () => assets, DisabledAssets = () => null };
            string P(string rel) => "/" + ConditioningControlPanel.Avalonia.Platform.WebAssetServer.AssetsPrefix + rel;
            Assert.Equal(System.IO.Path.Combine(assets, ".temp", "ccp_remote_a.mp4"), server.ResolveFile(P(".temp/ccp_remote_a.mp4")));
            Assert.Null(server.ResolveFile(P(".temp/sub/s.mp4")));
            Assert.Null(server.ResolveFile(P(".temp/.hidden.mp4")));
            Assert.Null(server.ResolveFile(P(".temp/notes.txt")));
            Assert.Null(server.ResolveFile(P(".packs/p.png")));
        }
        finally
        {
            System.IO.Directory.Delete(web, true);
            System.IO.Directory.Delete(assets, true);
        }
    }
}
