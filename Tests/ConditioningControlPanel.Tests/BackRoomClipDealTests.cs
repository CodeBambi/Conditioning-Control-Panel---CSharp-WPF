using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// EVERY SURFACE IS DEALT CLIPS (CONTRACT section 5, 10.13.C; "discard stills", 2026-09-17). The five
/// sources, the mixed ratio and the pool's plumbing live in <see cref="BackRoomMediaSourceTests"/>; this
/// suite holds what changed when the provider's static posters stopped being a class of media the room
/// deals: one warm set for the wall and every chair, the ladder that now goes clips -> the player's own
/// folders -> the built-in loops, and the three things that make clips arrive in seconds rather than
/// minutes - the GIF-only feed kind, the small rendition, and four download lanes.
///
/// <para>Nothing here touches a network: the batch fetch and the materialize are seams, and the
/// "downloads" are real files written under a throwaway assets root, which is exactly what gives them
/// <c>ccp.assets</c> urls. The clip files keep their <c>.mp4</c> / <c>.webm</c> extension because
/// <c>RemoteMediaCache.MaterializeAsync</c> takes it from the url - the one fact the page's routing
/// (<c>room\gif.js</c>, <c>stations\slot\media.js</c>, <c>shared\hypno\media.js</c> ->
/// <c>room\clip-source.js</c>) stands on.</para>
/// </summary>
public sealed class BackRoomClipDealTests : IDisposable
{
    private sealed class Capture : ILogEventSink
    {
        public readonly List<LogEvent> Events = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    /// <summary>The wall's own station id, named rather than spelled, so this suite breaks if the wire
    /// word ever moves.</summary>
    private const string Room = BackRoomMedia.StationRoom;

    /// <summary>The page's own test for "is this a clip", copied from <c>room\clip-source.js</c>'s
    /// <c>CLIP_EXT</c>. If these two ever disagree a surface shows a still image of frame one, so the
    /// assertion is worth making on the page's terms rather than on the host's.</summary>
    private static readonly Regex ClipExt = new(@"\.(webm|mp4|m4v)($|\?|#)", RegexOptions.IgnoreCase);
    private static bool IsClip(string url) => ClipExt.IsMatch(url ?? string.Empty);

    private readonly string _root;
    private readonly string _temp;
    private readonly Capture _capture = new();
    private readonly ILogger _logger;

    public BackRoomClipDealTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ccp-br-clip-" + Guid.NewGuid().ToString("N"), "PrivateUserName", "assets");
        _temp = Path.Combine(_root, ".temp");
        Directory.CreateDirectory(Path.Combine(_root, "images"));
        Directory.CreateDirectory(_temp);
        _logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_capture).CreateLogger();
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(_root))!, true); } catch { }
    }

    // ---------------------------------------------------------------- fixtures

    /// <summary>An animated GIF in the library, real enough for the header probe.</summary>
    private string Gif(string rel)
    {
        var path = Path.Combine(_root, "images", rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var b = new byte[32];
        "GIF89a"u8.CopyTo(b);
        b[6] = 64; b[8] = 48;
        File.WriteAllBytes(path, b);
        return path;
    }

    private static AppSettings Settings(string room = "online", string app = "local", bool consent = true,
        int ratio = 30)
    {
        var s = new AppSettings
        {
            RemoteMediaConsented = consent,
            MediaSource = app,
            BackRoomMediaSource = room,
            BackRoomMediaSubs = new List<string>(),
            BackRoomMediaSubsOff = new List<string>(),
            FypOnlineNiches = new List<string>(),
            FypOnlineCustomSubs = new List<string>(),
        };
        // After MediaSource, because the ratio setter clamps to 5..95 and nothing else moves it.
        s.RemoteMediaRatio = ratio;
        return s;
    }

    /// <summary>A pool that is simply "these clips are warm", so a deal test never needs a fetch. Urls
    /// end <c>.mp4</c>, which is the only thing that makes one a clip anywhere in this feature.</summary>
    private sealed class FakePool : IBackRoomRemotePool
    {
        public readonly List<BackRoomRemoteClip> Clips = new();
        public int Warms, Waits, Drains;
        public bool Wanted { get; set; } = true;
        public void EnsureWarm() => Warms++;
        public Task WarmAsync(CancellationToken ct) { Waits++; return Task.CompletedTask; }
        public IReadOnlyList<BackRoomRemoteClip> Ready() => Clips;
        public void Drain() { Drains++; Clips.Clear(); }

        public FakePool WithClips(int n)
        {
            for (int i = 0; i < n; i++)
                Clips.Add(new BackRoomRemoteClip("scrolller/sub/c" + i, "https://ccp.assets/.temp/clip" + i + ".mp4", 640, 360));
            return this;
        }
    }

    /// <summary>A pool where every read is a failure. The room must deal anyway.</summary>
    private sealed class ThrowingPool : IBackRoomRemotePool
    {
        public bool Wanted => true;
        public void EnsureWarm() => throw new InvalidOperationException("nope");
        public Task WarmAsync(CancellationToken ct) => throw new InvalidOperationException("nope");
        public IReadOnlyList<BackRoomRemoteClip> Ready() => throw new InvalidOperationException("nope");
        public void Drain() => throw new InvalidOperationException("nope");
    }

    private BackRoomMedia Media(AppSettings? settings, IBackRoomRemotePool? pool, IEnumerable<string>? images = null)
    {
        var imgs = (images ?? Array.Empty<string>()).ToList();
        return new BackRoomMedia(() => imgs, () => Array.Empty<string>(), () => _root, null, () => _logger,
            () => settings, () => pool);
    }

    private string LogText() => string.Join("\n", _capture.Events.Select(e =>
        e.RenderMessage() + " " + string.Join(" ", e.Properties.Values.Select(v => v.ToString()))));

    private static FypAssetManifest.Entry Still(string id, int w = 640, int h = 480)
        => new() { Id = id, Url = "https://cdn.example.com/" + id.Replace('/', '-') + ".webp", Type = "image", Width = w, Height = h, Origin = "online" };

    private static FypAssetManifest.Entry Clip(string id, string ext = ".mp4", int w = 1280, int h = 720, string? small = null)
        => new() { Id = id, Url = "https://cdn.example.com/" + id.Replace('/', '-') + ext, SmallUrl = small, Type = "video", Width = w, Height = h, Origin = "online" };

    /// <summary>The real pool with the batch fetch and the materialize as seams. A "download" writes a
    /// real file under the assets temp folder, carrying the url's extension exactly as
    /// <c>RemoteMediaCache</c> does, and a NEW guid name per call - which is the dedupe trap.
    ///
    /// <para>THE RECORDER LOCK IS LOAD-BEARING: the pool runs <see cref="BackRoomRemotePool.MaterializeConcurrency"/>
    /// downloads at once, so these lists are written from several threads, and List&lt;T&gt;.Add from
    /// two threads silently loses an item. A count that comes up short here is a dropped Add, not a
    /// slow fill.</para></summary>
    private (BackRoomRemotePool Pool, List<string> Materialized, List<string> Released) Pool(
        AppSettings settings, List<FypAssetManifest.Entry>? clips = null, string? error = null,
        Func<Task>? beforeWrite = null)
    {
        var materialized = new List<string>();
        var released = new List<string>();
        var recorder = new object();
        var pool = new BackRoomRemotePool(
            () => settings,
            _ => Task.FromResult((clips ?? new List<FypAssetManifest.Entry>(), error)),
            async (url, _) =>
            {
                lock (recorder) materialized.Add(url);
                if (beforeWrite != null) await beforeWrite();
                var path = Path.Combine(_temp, "ccp_temp_remote_" + Guid.NewGuid().ToString("N") + Path.GetExtension(url));
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                return path;
            },
            p => { lock (recorder) released.Add(p); try { File.Delete(p); } catch { } },
            () => _root,
            () => _logger);
        return (pool, materialized, released);
    }

    /// <summary>The bounded wait returns when the CAP elapses, not when the batch ends (that is its
    /// whole job), so a test asserting on a whole batch waits for the batch rather than the wait. It
    /// gives up QUIETLY so the caller's own assertion reports the real shortfall.</summary>
    private static async Task Settle(Func<int> count, int want)
    {
        for (int i = 0; i < 300 && count() < want; i++) await Task.Delay(10);
    }

    // ---------------------------------------------------------------- every surface gets clips

    [Fact]
    public void Wall_IsDealtTheWarmClips_OnCcpAssets_AndWithoutTheAnimatedHint()
    {
        var deal = Media(Settings(), new FakePool().WithClips(8), new[] { Gif("a.gif") }).Deal(Room, 11, 8);

        Assert.Equal(8, deal.Gifs.Count);
        Assert.All(deal.Gifs, g => Assert.True(IsClip(g.Url), "the wall's pictures are clips: " + g.Url));
        // No new wire word: a clip is an "online" pick the page routes on the extension.
        Assert.All(deal.Gifs, g => Assert.Equal("online", g.Src));
        Assert.All(deal.Gifs, g => Assert.StartsWith("https://ccp.assets/.temp/", g.Url));
        // The animated-webp hint is for a local .webp. A clip is PLAYED, so claiming it decodes would
        // only cost the host a decode it cannot do.
        Assert.All(deal.Gifs, g => Assert.DoesNotContain("#", g.Url));
        Assert.Equal(8, deal.Gifs.Select(g => g.Url).Distinct().Count());
        Assert.Equal("online", deal.Source);
    }

    [Theory]
    [InlineData("slot", 4)]
    [InlineData("cards", 13)]
    [InlineData("wheel", 8)]
    [InlineData("roulette", 4)]
    // The wire word is ordinal; a station calling itself "Room" is just another chair, dealt the same.
    [InlineData("Room", 4)]
    public void Station_IsDealtClips_ExactlyAsTheWallIs(string station, int count)
    {
        // The old split (clips on the wall, posters at the chairs) is gone: the reels, the deck and the
        // wedges route a webm to clip-source.js themselves now, and the surfaces that hold many pictures
        // cap and pause them on the page. The host deals one class of remote picture.
        var deal = Media(Settings(), new FakePool().WithClips(BackRoomRemotePool.ReadyTarget), null).Deal(station, 5, count);

        Assert.Equal(count, deal.Gifs.Count);
        Assert.All(deal.Gifs, g => Assert.True(IsClip(g.Url), "a chair is dealt clips too: " + g.Url));
        Assert.All(deal.Gifs, g => Assert.Equal("online", g.Src));
        Assert.Equal(count, deal.Gifs.Select(g => g.Url).Distinct().Count());
    }

    [Fact]
    public void SameSeed_DealsTheSameHand_AndASmallerDealIsAPrefixOfTheLarger()
    {
        var a = Media(Settings(), new FakePool().WithClips(16), null).Deal(Room, 77, 8);
        var b = Media(Settings(), new FakePool().WithClips(16), null).Deal(Room, 77, 8);
        Assert.Equal(a.Gifs.Select(g => g.Url), b.Gifs.Select(g => g.Url));

        // One seeded partial shuffle, consumed as it goes: the slot's four are the first four of the
        // wall's eight for the same seed, so a re-deal at a different count never reshuffles the room.
        var four = Media(Settings(), new FakePool().WithClips(16), null).Deal("slot", 77, 4);
        Assert.Equal(a.Gifs.Take(4).Select(g => g.Url), four.Gifs.Select(g => g.Url));
    }

    [Fact]
    public void Mixed_BlendsClipsWithThePlayersOwnFolders()
    {
        var s = Settings("mixed", ratio: 50);
        var files = Enumerable.Range(0, 8).Select(i => Gif($"m{i}.gif")).ToList();

        int clips = 0, local = 0;
        foreach (var seed in new[] { 1, 2, 3, 5, 8, 13 })
        {
            var deal = Media(s, new FakePool().WithClips(8), files).Deal("cards", seed, 8);
            Assert.Equal(8, deal.Gifs.Count);
            Assert.Equal("mixed", deal.Source);
            clips += deal.Gifs.Count(g => IsClip(g.Url));
            local += deal.Gifs.Count(g => g.Src == "pool");
        }
        Assert.True(clips > 0 && local > 0, "a blended deal draws from both halves");
    }

    // ---------------------------------------------------------------- the ladder

    [Fact]
    public void Ladder_GoesClipsThenTheFoldersThenBundled_AndThereIsNoStillsRung()
    {
        var s = Settings();
        var library = new[] { Gif("a.gif"), Gif("b.gif") };

        // Rung 1: clips.
        var clips = Media(s, new FakePool().WithClips(4), library).Deal("slot", 3, 4);
        Assert.All(clips.Gifs, g => Assert.True(IsClip(g.Url)));

        // Rung 2: a cold pool -> the player's own folders. There is no poster in between any more: a
        // static picture is not a class of media the room deals (2026-09-17).
        var local = Media(s, new FakePool(), library).Deal("slot", 3, 4);
        Assert.Equal(2, local.Gifs.Count);
        Assert.All(local.Gifs, g => Assert.Equal("pool", g.Src));

        // Rung 3, the floor: nothing anywhere -> the built-in loops, never an empty deal. No surface is
        // ever left with nothing.
        var bundled = Media(s, new FakePool(), Array.Empty<string>()).Deal("slot", 3, 4);
        Assert.Equal(4, bundled.Gifs.Count);
        Assert.All(bundled.Gifs, g => Assert.Equal("fallback", g.Src));

        // A pool that throws on every read is the same thing as a pool that is empty.
        var thrown = Media(s, new ThrowingPool(), library).Deal(Room, 3, 8);
        Assert.All(thrown.Gifs, g => Assert.Equal("pool", g.Src));
        Assert.NotEmpty(thrown.Gifs);
    }

    [Fact]
    public async Task DealAsync_DealsTheFoldersWhenThePoolIsCold_AndClipsOnceItIsWarm()
    {
        // The batch can land after the wait cap. That is not a bug, it is rung 2: this deal is the
        // player's own files and the next one is clips.
        var (pool, _, _) = Pool(Settings(), clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/c") });
        var cold = await Media(Settings(), new FakePool(), new[] { Gif("own.gif") }).DealAsync("slot", 5, 4);
        Assert.Equal("pool", Assert.Single(cold.Gifs).Src);

        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.Ready().Count, 1);
        var warm = await Media(Settings(), pool, new[] { Gif("own.gif") }).DealAsync("slot", 5, 4);
        Assert.True(IsClip(Assert.Single(warm.Gifs).Url));
        Assert.Equal("online", warm.Gifs[0].Src);
    }

    // ---------------------------------------------------------------- the warm set

    [Fact]
    public async Task WarmPool_MaterializesOneEntryIdExactlyOnce()
    {
        // The trap: every MaterializeAsync mints a new guid filename, so materializing one clip twice
        // gives the page two urls it cannot dedupe and the same clip plays on two screens.
        var (pool, materialized, _) = Pool(Settings(), clips: new List<FypAssetManifest.Entry>
        {
            Clip("scrolller/s/a"), Clip("scrolller/s/a"), Clip("scrolller/s/b", ".webm"), Clip("scrolller/s/a"),
        });

        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.Ready().Count, 2);

        Assert.Equal(2, materialized.Count);
        var ready = pool.Ready();
        Assert.Equal(2, ready.Count);
        Assert.Equal(2, ready.Select(r => r.Url).Distinct().Count());
        Assert.Equal(new[] { "scrolller/s/a", "scrolller/s/b" }, ready.Select(r => r.Id).OrderBy(x => x));
        // The extension survives the materialize, which is the one fact the page's routing rests on.
        Assert.All(ready, r => Assert.True(IsClip(r.Url)));
        Assert.All(ready, r => Assert.StartsWith("https://ccp.assets/.temp/ccp_temp_remote_", r.Url));
        Assert.All(ready, r => Assert.Equal((1280, 720), (r.W, r.H)));
    }

    [Fact]
    public async Task WarmPool_KeepsOnlyPlayableClips_AndNeverAPoster()
    {
        var (pool, materialized, _) = Pool(Settings(), clips: new List<FypAssetManifest.Entry>
        {
            Clip("scrolller/s/ok"),
            Still("scrolller/s/poster"),                                                                  // the class the room discarded
            new() { Id = "", Url = "https://cdn.example.com/x.mp4", Type = "video", Origin = "online" },   // no dedupe key
            new() { Id = "scrolller/s/mkv", Url = "https://cdn.example.com/x.mkv", Type = "video", Origin = "online" },
            new() { Id = "scrolller/s/bad:id", Url = "https://cdn.example.com/y.mp4", Type = "video", Origin = "online" },
        });

        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.Ready().Count, 1);

        // RemoteMediaFormats is the single authority: a clip is .mp4 / .webm and nothing else, so an
        // .mkv the page could not decode is never downloaded, and a poster is never downloaded either.
        Assert.Single(materialized);
        Assert.Equal("scrolller/s/ok", Assert.Single(pool.Ready()).Id);
    }

    [Fact]
    public void TheRoomsFeedKind_TakesAClipAndRefusesAPoster()
    {
        // GifClip is the GIF filter's clip half. The validator has to know it as a video kind, or the
        // pool would download posters it can no longer deal.
        Assert.True(RemoteMediaFormats.Validate(Clip("scrolller/s/a"), FeedMediaKind.GifClip, out _));
        Assert.False(RemoteMediaFormats.Validate(Still("scrolller/s/p"), FeedMediaKind.GifClip, out var why));
        Assert.Contains("still", why);
        // And the flashes' own kind is untouched: a poster is still what GifStill wants.
        Assert.True(RemoteMediaFormats.Validate(Still("scrolller/s/p"), FeedMediaKind.GifStill, out _));
    }

    // ---------------------------------------------------------------- A: the small rendition

    [Fact]
    public void DownloadUrl_IsTheSmallRendition_FallingBackToTheBest()
    {
        // The page paints at 384 px or less, so the 1920 px rendition the entry's Url names is bytes
        // nobody sees. That waste, not the posters, was the 12-seconds-a-clip the room had.
        Assert.Equal("https://cdn.example.com/small.mp4",
            BackRoomRemotePool.DownloadUrl(Clip("scrolller/s/a", small: "https://cdn.example.com/small.mp4")));
        // No smaller rendition: the best one, never a dropped entry.
        Assert.Equal("https://cdn.example.com/scrolller-s-b.mp4", BackRoomRemotePool.DownloadUrl(Clip("scrolller/s/b")));
        Assert.Equal("https://cdn.example.com/scrolller-s-c.mp4", BackRoomRemotePool.DownloadUrl(Clip("scrolller/s/c", small: "")));
        // A small rendition the page could not play is not a rendition.
        Assert.Equal("https://cdn.example.com/scrolller-s-d.mp4",
            BackRoomRemotePool.DownloadUrl(Clip("scrolller/s/d", small: "https://cdn.example.com/small.mkv")));
    }

    [Fact]
    public async Task WarmPool_DownloadsTheSmallRendition_AndKeepsThePostsSize()
    {
        var (pool, materialized, _) = Pool(Settings(), clips: new List<FypAssetManifest.Entry>
        {
            Clip("scrolller/s/a", small: "https://cdn.example.com/a-640.mp4"),
            Clip("scrolller/s/b"),
        });
        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.Ready().Count, 2);

        Assert.Equal(new[] { "https://cdn.example.com/a-640.mp4", "https://cdn.example.com/scrolller-s-b.mp4" },
            materialized.OrderBy(x => x));
        // W and H are the post's, for aspect: the page paints at its own edge whatever the file is.
        Assert.All(pool.Ready(), r => Assert.Equal((1280, 720), (r.W, r.H)));
    }

    // ---------------------------------------------------------------- B: four lanes

    [Fact]
    public async Task WarmPool_RunsSeveralDownloadsAtOnce_AndNeverMoreThanTheLanes_AndNeverPastTheCeiling()
    {
        int inFlight = 0, peak = 0;
        var (pool, materialized, released) = Pool(Settings(),
            clips: Enumerable.Range(0, BackRoomRemotePool.ReadyMax + 10).Select(i => Clip("scrolller/s/c" + i)).ToList(),
            beforeWrite: async () =>
            {
                int now = Interlocked.Increment(ref inFlight);
                int seen;
                do { seen = peak; } while (now > seen && Interlocked.CompareExchange(ref peak, now, seen) != seen);
                await Task.Delay(25);
                Interlocked.Decrement(ref inFlight);
            });

        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.Ready().Count, BackRoomRemotePool.ReadyMax);
        // The batch may still be landing its last lane when the count is reached; let it close.
        await Task.Delay(100);

        Assert.Equal(BackRoomRemotePool.ReadyMax, pool.Ready().Count);
        Assert.True(peak >= 2, "downloads ran one at a time; the lanes are not being used");
        Assert.True(peak <= BackRoomRemotePool.MaterializeConcurrency, $"{peak} downloads in flight, over the lane count");
        // In-flight downloads count against the ceiling, so the lanes cannot carry the set past it
        // and then throw the extra files away: exactly the ceiling is downloaded, nothing is released.
        Assert.Equal(BackRoomRemotePool.ReadyMax, materialized.Count);
        Assert.Empty(released);
    }

    [Fact]
    public async Task WarmPool_ABoundedWaitReturnsBeforeASlowBatchDoes()
    {
        // The whole reason the pool exists: a sit-down never waits on the network's clock. Four lanes
        // do not change that; the cap does the waiting, the batch carries on behind it.
        var gate = new TaskCompletionSource();
        var (pool, _, _) = Pool(Settings(), clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/slow") },
            beforeWrite: () => gate.Task);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await pool.WarmAsync(CancellationToken.None);
        sw.Stop();
        Assert.InRange(sw.ElapsedMilliseconds, 0, BackRoomRemotePool.WarmWaitMs + 1500);
        Assert.Empty(pool.Ready());

        gate.SetResult();
        await Settle(() => pool.Ready().Count, 1);
        Assert.Single(pool.Ready());
    }

    // ---------------------------------------------------------------- D: the size

    [Fact]
    public void ThePoolIsSizedForEverySurface_AndLeavesTheTempTrackerRoom()
    {
        // One clip set feeds the wall (8), the slot (4), the wheel (8) and the card table (13), each a
        // seeded draw over the whole set: the target has to cover the largest single deal, and the
        // wall plus a chair should only partly overlap.
        Assert.True(BackRoomRemotePool.ReadyTarget >= BackRoomMedia.MaxCount, "a 13-card deck must come out of one warm set");
        Assert.True(BackRoomRemotePool.ReadyTarget >= 8, "one full wall");
        Assert.InRange(BackRoomRemotePool.ReadyMax, BackRoomRemotePool.ReadyTarget, BackRoomRemotePool.ReadyTarget * 2);
        // RemoteMediaCache.MaxTrackedTempFiles = 50 deletes OLDEST-FIRST across every consumer. The only
        // other tenant that materializes is the desktop wallpaper (RemotePoolTarget = 4); the flash pool,
        // the For You feed and the video service never touch disk. The room must leave that tenant and
        // real headroom, or it sweeps its own live urls out from under the page.
        const int WallpaperPool = 4, Headroom = 16;
        Assert.True(BackRoomRemotePool.ReadyMax + WallpaperPool + Headroom <= 50,
            "the room's ceiling must leave the wallpaper and headroom inside the 50-file tracker");
        // Lanes: enough to be parallel, few enough not to fight the wallpaper for one CDN.
        Assert.InRange(BackRoomRemotePool.MaterializeConcurrency, 2, 6);
    }

    [Fact]
    public async Task WarmPool_ForgetsAClipWhoseFileWentAway()
    {
        var (pool, _, _) = Pool(Settings(), clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/a"), Clip("scrolller/s/b") });
        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.Ready().Count, 2);

        // Clips share the over-50 budget with the wallpaper, so a file can still go out from under a
        // warm url. A dealt url always loads or it is not dealt.
        foreach (var f in Directory.GetFiles(_temp, "*.mp4").Take(1)) File.Delete(f);

        Assert.Single(pool.Ready());
    }

    [Fact]
    public async Task WarmPool_DrainReleasesEveryClip()
    {
        var (pool, _, released) = Pool(Settings(), clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/a"), Clip("scrolller/s/b") });
        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.Ready().Count, 2);

        pool.Drain();

        Assert.Equal(2, released.Count);
        Assert.Empty(pool.Ready());
        Assert.Empty(Directory.GetFiles(_temp));
    }

    [Fact]
    public async Task WarmPool_WarmsNothingWhenTheSourceDoesNotWantRemote()
    {
        foreach (var room in new[] { "local", "bundled", "auto" })
        {
            var (pool, materialized, _) = Pool(Settings(room, "local"), clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/a") });
            Assert.False(pool.Wanted);
            pool.EnsureWarm();
            await pool.WarmAsync(CancellationToken.None);
            Assert.Empty(materialized);
            Assert.Empty(pool.Ready());
        }

        // Consent withdrawn is the same refusal, whatever the room asked for.
        var (denied, deniedMaterialized, _) = Pool(Settings("online", "online", consent: false),
            clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/a") });
        Assert.False(denied.Wanted);
        await denied.WarmAsync(CancellationToken.None);
        Assert.Empty(deniedMaterialized);
    }

    [Fact]
    public async Task WarmPool_ATransportFailureWarmsNothingAndTheDealTakesTheFolders()
    {
        var s = Settings();
        var (pool, materialized, _) = Pool(s, clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/a") }, error: "offline");

        await pool.WarmAsync(CancellationToken.None);

        Assert.Empty(materialized);
        Assert.Empty(pool.Ready());
        // A provider that is down must look like "no remote content", never like a broken wall.
        var deal = Media(s, pool, new[] { Gif("fallbackToThis.gif") }).Deal(Room, 1, 8);
        Assert.Equal("pool", Assert.Single(deal.Gifs).Src);
        Assert.Equal("online", deal.Source);
    }

    // ---------------------------------------------------------------- what the log and the wire carry

    [Fact]
    public async Task WarmPool_LogsCountsOnly_NeverAPathAndNeverAUrl()
    {
        var (pool, _, _) = Pool(Settings(), clips: new List<FypAssetManifest.Entry>
        {
            Clip("scrolller/s/a", small: "https://cdn.example.com/a-640.mp4"),
            new() { Id = "scrolller/s/mkv", Url = "https://cdn.example.com/x.mkv", Type = "video", Origin = "online" },
        });
        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.Ready().Count, 1);
        Media(Settings(), pool, new[] { Gif("x.gif") }).Deal("slot", 1);

        var log = LogText();
        Assert.NotEmpty(_capture.Events);
        // "warmed 1 clip(s), dropped 1" reads without a url to explain it, and the deal line keeps the
        // "(N clip)" pair a session log is grepped for.
        Assert.Contains("clip", log);
        // RenderMessage quotes a string property, so the station reads "slot" here and slot in the file.
        Assert.Matches(@"dealt ""?slot""? .*\(1 clip\)", log);
        Assert.DoesNotContain(_root, log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateUserName", log);
        Assert.DoesNotContain("ccp.assets", log);
        Assert.DoesNotContain("example.com", log);
        Assert.DoesNotContain("cdn.", log);
        Assert.DoesNotContain(".mp4", log);
        Assert.DoesNotContain("ccp_temp_remote_", log);
    }

    [Fact]
    public async Task Deal_CarriesNoPathOrRemoteHost()
    {
        var (pool, _, _) = Pool(Settings(), clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/a") });
        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.Ready().Count, 1);

        var json = JsonConvert.SerializeObject(await Media(Settings(), pool, null).DealAsync("slot", 5, 4));

        // It really is the clip on the wire, and still only ever a ccp.assets url.
        Assert.Contains(".mp4", json);
        Assert.DoesNotContain(_root, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateUserName", json);
        Assert.DoesNotContain("example.com", json);
        Assert.DoesNotContain("\\\\", json);
    }
}
