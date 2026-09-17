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
using Newtonsoft.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// CLIPS ON THE WALLS, STILLS AT THE CHAIRS (CONTRACT section 5, 10.13.C). The five sources, the mixed
/// ratio and the still pool live in <see cref="BackRoomMediaSourceTests"/>; this suite holds the
/// animated half: the second warm set fetched as <c>FeedMediaKind.Video</c>, the wall/station
/// split that keeps a webm away from thirteen reel textures, and the ladder that means a cold clip set
/// costs the player a still rather than a black wall.
///
/// <para>Nothing here touches a network: both batch fetches and the materialize are seams, and the
/// "downloads" are real files written under a throwaway assets root, which is exactly what gives them
/// <c>ccp.assets</c> urls. The clip files keep their <c>.mp4</c> / <c>.webm</c> extension because
/// <c>RemoteMediaCache.MaterializeAsync</c> takes it from the url - the one fact the page's routing
/// (<c>room\gif.js</c> -> <c>room\clip-source.js</c>) stands on.</para>
/// </summary>
public sealed class BackRoomWallClipTests : IDisposable
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
    /// <c>CLIP_EXT</c>. If these two ever disagree the wall shows a still image of frame one, so the
    /// assertion is worth making on the page's terms rather than on the host's.</summary>
    private static readonly Regex ClipExt = new(@"\.(webm|mp4|m4v)($|\?|#)", RegexOptions.IgnoreCase);
    private static bool IsClip(string url) => ClipExt.IsMatch(url ?? string.Empty);

    private readonly string _root;
    private readonly string _temp;
    private readonly Capture _capture = new();
    private readonly ILogger _logger;

    public BackRoomWallClipTests()
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

    /// <summary>A pool that is simply "these are warm", in both sets, so a deal test never needs a
    /// fetch. Clip urls end <c>.mp4</c> and still urls end <c>.webp</c>, which is the only thing that
    /// makes one a clip anywhere in this feature.</summary>
    private sealed class FakePool : IBackRoomRemotePool
    {
        public readonly List<BackRoomRemoteStill> Stills = new();
        public readonly List<BackRoomRemoteClip> Clips = new();
        public int Warms, Waits, Drains;
        public bool Wanted { get; set; } = true;
        public void EnsureWarm() => Warms++;
        public Task WarmAsync(CancellationToken ct) { Waits++; return Task.CompletedTask; }
        public IReadOnlyList<BackRoomRemoteStill> Ready() => Stills;
        public IReadOnlyList<BackRoomRemoteClip> ReadyClips() => Clips;
        public void Drain() { Drains++; Stills.Clear(); Clips.Clear(); }

        public FakePool WithStills(int n)
        {
            for (int i = 0; i < n; i++)
                Stills.Add(new BackRoomRemoteStill("scrolller/sub/p" + i, "https://ccp.assets/.temp/still" + i + ".webp", 300, 200));
            return this;
        }

        public FakePool WithClips(int n)
        {
            for (int i = 0; i < n; i++)
                Clips.Add(new BackRoomRemoteClip("scrolller/sub/c" + i, "https://ccp.assets/.temp/clip" + i + ".mp4", 1280, 720));
            return this;
        }
    }

    /// <summary>A pool where every read is a failure. The room must deal anyway.</summary>
    private sealed class ThrowingPool : IBackRoomRemotePool
    {
        public bool Wanted => true;
        public void EnsureWarm() => throw new InvalidOperationException("nope");
        public Task WarmAsync(CancellationToken ct) => throw new InvalidOperationException("nope");
        public IReadOnlyList<BackRoomRemoteStill> Ready() => throw new InvalidOperationException("nope");
        public IReadOnlyList<BackRoomRemoteClip> ReadyClips() => throw new InvalidOperationException("nope");
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

    private static FypAssetManifest.Entry Clip(string id, string ext = ".mp4", int w = 1280, int h = 720)
        => new() { Id = id, Url = "https://cdn.example.com/" + id.Replace('/', '-') + ext, Type = "video", Width = w, Height = h, Origin = "online" };

    /// <summary>The real pool with BOTH batch fetches and the materialize as seams. A "download" writes
    /// a real file under the assets temp folder, carrying the url's extension exactly as
    /// <c>RemoteMediaCache</c> does, and a NEW guid name per call - which is the dedupe trap.</summary>
    private (BackRoomRemotePool Pool, List<string> Materialized, List<string> Released) Pool(
        AppSettings settings, List<FypAssetManifest.Entry>? stills = null,
        List<FypAssetManifest.Entry>? clips = null, string? error = null)
    {
        var materialized = new List<string>();
        var released = new List<string>();
        Task<(List<FypAssetManifest.Entry> Entries, string? Error)> Batch(List<FypAssetManifest.Entry>? entries)
            => Task.FromResult((entries ?? new List<FypAssetManifest.Entry>(), error));

        var pool = new BackRoomRemotePool(
            () => settings,
            _ => Batch(stills),
            (url, _) =>
            {
                materialized.Add(url);
                var path = Path.Combine(_temp, "ccp_temp_remote_" + Guid.NewGuid().ToString("N") + Path.GetExtension(url));
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                return Task.FromResult<string?>(path);
            },
            p => { released.Add(p); try { File.Delete(p); } catch { } },
            () => _root,
            () => _logger,
            _ => Batch(clips));
        return (pool, materialized, released);
    }

    /// <summary>The bounded wait returns when the CAP elapses, not when the batch ends (that is its
    /// whole job), so a test asserting on a whole batch waits for the batch rather than the wait.</summary>
    private static async Task Settle(Func<int> count, int want)
    {
        for (int i = 0; i < 300 && count() < want; i++) await Task.Delay(10);
    }

    // ---------------------------------------------------------------- the wall gets clips

    [Fact]
    public void Wall_IsDealtTheWarmClips_OnCcpAssets_AndWithoutTheAnimatedHint()
    {
        var deal = Media(Settings(), new FakePool().WithClips(8).WithStills(8), new[] { Gif("a.gif") })
            .Deal(Room, 11, 8);

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
    [InlineData("slot")]
    [InlineData("cards")]
    [InlineData("wheel")]
    // Ordinal and exact: a station that calls itself "Room" is not the room.
    [InlineData("Room")]
    public void Station_StaysOnStills_EvenWhenClipsAreWarm(string station)
    {
        // The card table asks for 13 (10.13.C) and the slot paints its art into reel textures through
        // decodedSource with a hidden <img> fallback - neither takes a webm, so a clip dealt here would
        // silently become fallback art, and thirteen decoders at once would be reckless even if it did.
        var deal = Media(Settings(), new FakePool().WithClips(8).WithStills(13), null).Deal(station, 5, 13);

        Assert.Equal(13, deal.Gifs.Count);
        Assert.All(deal.Gifs, g => Assert.False(IsClip(g.Url), "a chair is never dealt a clip: " + g.Url));
        Assert.All(deal.Gifs, g => Assert.Equal("online", g.Src));
    }

    [Fact]
    public void Wall_TakesClipsFirstAndTopsUpWithStills()
    {
        // Three clips warm and eight screens: the wall still fills. MAX_PICTURES is 8 in
        // room\screens.js, which is also the count room\main.js asks for.
        var deal = Media(Settings(), new FakePool().WithClips(3).WithStills(8), null).Deal(Room, 7, 8);

        Assert.Equal(8, deal.Gifs.Count);
        Assert.Equal(3, deal.Gifs.Count(g => IsClip(g.Url)));
        // Clips come first in deal order, so the moving pictures land on the first screens rather than
        // wherever a shuffle put them.
        Assert.All(deal.Gifs.Take(3), g => Assert.True(IsClip(g.Url)));
        Assert.All(deal.Gifs.Skip(3), g => Assert.False(IsClip(g.Url)));
        Assert.Equal(8, deal.Gifs.Select(g => g.Url).Distinct().Count());
    }

    [Fact]
    public void Wall_SameSeedDealsTheSameWall_AndClipsCannotMoveAStationsStills()
    {
        var a = Media(Settings(), new FakePool().WithClips(5).WithStills(8), null).Deal(Room, 77, 8);
        var b = Media(Settings(), new FakePool().WithClips(5).WithStills(8), null).Deal(Room, 77, 8);
        Assert.Equal(a.Gifs.Select(g => g.Url), b.Gifs.Select(g => g.Url));

        // The clip draw has its own stream, so how many clips happen to be warm cannot reshuffle the
        // stills a chair is dealt.
        var withClips = Media(Settings(), new FakePool().WithClips(5).WithStills(8), null).Deal("slot", 77, 4);
        var without = Media(Settings(), new FakePool().WithStills(8), null).Deal("slot", 77, 4);
        Assert.Equal(without.Gifs.Select(g => g.Url), withClips.Gifs.Select(g => g.Url));
    }

    [Fact]
    public void Mixed_TheWallBlendsClipsWithThePlayersOwnFolders()
    {
        var s = Settings("mixed", ratio: 50);
        var files = Enumerable.Range(0, 8).Select(i => Gif($"m{i}.gif")).ToList();

        int clips = 0, local = 0;
        foreach (var seed in new[] { 1, 2, 3, 5, 8, 13 })
        {
            var deal = Media(s, new FakePool().WithClips(8), files).Deal(Room, seed, 8);
            Assert.Equal(8, deal.Gifs.Count);
            Assert.Equal("mixed", deal.Source);
            clips += deal.Gifs.Count(g => IsClip(g.Url));
            local += deal.Gifs.Count(g => g.Src == "pool");
        }
        Assert.True(clips > 0 && local > 0, "a blended wall draws from both halves");
    }

    // ---------------------------------------------------------------- the ladder

    [Fact]
    public void Wall_DegradesOneDirectionOnly_ClipsThenStillsThenLocalThenBundled()
    {
        var s = Settings();
        var library = new[] { Gif("a.gif"), Gif("b.gif") };

        // Rung 1: clips.
        var clips = Media(s, new FakePool().WithClips(4).WithStills(4), library).Deal(Room, 3, 4);
        Assert.All(clips.Gifs, g => Assert.True(IsClip(g.Url)));

        // Rung 2: no clips warm yet (or every one refused) -> stills, not the folders. A wall showing a
        // still is fine; a wall showing nothing is a bug.
        var stills = Media(s, new FakePool().WithStills(4), library).Deal(Room, 3, 4);
        Assert.Equal(4, stills.Gifs.Count);
        Assert.All(stills.Gifs, g => Assert.Equal("online", g.Src));
        Assert.All(stills.Gifs, g => Assert.False(IsClip(g.Url)));

        // Rung 3: nothing remote at all -> the player's own folders.
        var local = Media(s, new FakePool(), library).Deal(Room, 3, 4);
        Assert.Equal(2, local.Gifs.Count);
        Assert.All(local.Gifs, g => Assert.Equal("pool", g.Src));

        // Rung 4, the floor: nothing anywhere -> the built-in loops, never an empty deal.
        var bundled = Media(s, new FakePool(), Array.Empty<string>()).Deal(Room, 3, 4);
        Assert.Equal(4, bundled.Gifs.Count);
        Assert.All(bundled.Gifs, g => Assert.Equal("fallback", g.Src));

        // A pool that throws on every read is the same thing as a pool that is empty.
        var thrown = Media(s, new ThrowingPool(), library).Deal(Room, 3, 4);
        Assert.All(thrown.Gifs, g => Assert.Equal("pool", g.Src));
        Assert.NotEmpty(thrown.Gifs);
    }

    [Fact]
    public async Task DealAsync_StillDealsTheWallWhenTheClipSetIsCold()
    {
        // The clip batch is the second GraphQL call of the pair and can land after the wait cap. That
        // is not a bug, it is rung 2: this deal gets stills and a later one gets clips.
        var (pool, _, _) = Pool(Settings(), stills: new List<FypAssetManifest.Entry> { Still("scrolller/s/p") });
        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.Ready().Count, 1);

        var deal = await Media(Settings(), pool, null).DealAsync(Room, 5, 8);
        Assert.Empty(pool.ReadyClips());
        Assert.Equal("online", Assert.Single(deal.Gifs).Src);
        Assert.False(IsClip(deal.Gifs[0].Url));
    }

    // ---------------------------------------------------------------- the clip warm set

    [Fact]
    public async Task WarmPool_MaterializesOneClipEntryIdExactlyOnce()
    {
        // The trap: every MaterializeAsync mints a new guid filename, so materializing one clip twice
        // gives the page two urls it cannot dedupe and the same clip plays on two screens.
        var (pool, materialized, _) = Pool(Settings(), clips: new List<FypAssetManifest.Entry>
        {
            Clip("scrolller/s/a"), Clip("scrolller/s/a"), Clip("scrolller/s/b", ".webm"), Clip("scrolller/s/a"),
        });

        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.ReadyClips().Count, 2);

        Assert.Equal(2, materialized.Count);
        var ready = pool.ReadyClips();
        Assert.Equal(2, ready.Count);
        Assert.Equal(2, ready.Select(r => r.Url).Distinct().Count());
        Assert.Equal(new[] { "scrolller/s/a", "scrolller/s/b" }, ready.Select(r => r.Id).OrderBy(x => x));
        // The extension survives the materialize, which is the one fact the page's routing rests on.
        Assert.All(ready, r => Assert.True(IsClip(r.Url)));
        Assert.All(ready, r => Assert.StartsWith("https://ccp.assets/.temp/ccp_temp_remote_", r.Url));
        Assert.All(ready, r => Assert.Equal((1280, 720), (r.W, r.H)));
    }

    [Fact]
    public async Task WarmPool_DropsAnythingThatIsNotAPlayableClip()
    {
        var (pool, materialized, _) = Pool(Settings(), clips: new List<FypAssetManifest.Entry>
        {
            Clip("scrolller/s/ok"),
            Still("scrolller/s/poster"),                                                                  // the poster, not the clip
            new() { Id = "", Url = "https://cdn.example.com/x.mp4", Type = "video", Origin = "online" },   // no dedupe key
            new() { Id = "scrolller/s/mkv", Url = "https://cdn.example.com/x.mkv", Type = "video", Origin = "online" },
            new() { Id = "scrolller/s/bad:id", Url = "https://cdn.example.com/y.mp4", Type = "video", Origin = "online" },
        });

        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.ReadyClips().Count, 1);

        // RemoteMediaFormats is the single authority: video is .mp4 / .webm and nothing else, so an
        // .mkv the page could not decode is never downloaded in the first place.
        Assert.Single(materialized);
        Assert.Equal("scrolller/s/ok", Assert.Single(pool.ReadyClips()).Id);
        // And nothing leaked sideways into the stills a chair reads.
        Assert.Empty(pool.Ready());
    }

    [Fact]
    public async Task WarmPool_FetchesStillsAndClipsAsTwoSeparateSets()
    {
        var (pool, materialized, _) = Pool(Settings(),
            stills: new List<FypAssetManifest.Entry> { Still("scrolller/s/p1"), Still("scrolller/s/p2") },
            clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/c1") });

        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.Ready().Count + pool.ReadyClips().Count, 3);

        Assert.Equal(3, materialized.Count);
        Assert.Equal(2, pool.Ready().Count);
        Assert.Single(pool.ReadyClips());
        Assert.All(pool.Ready(), s => Assert.False(IsClip(s.Url)));
        Assert.All(pool.ReadyClips(), c => Assert.True(IsClip(c.Url)));
    }

    [Fact]
    public async Task WarmPool_EachSetStopsAtItsOwnCeiling()
    {
        var (pool, _, _) = Pool(Settings(),
            stills: Enumerable.Range(0, BackRoomRemotePool.ReadyMax + 6).Select(i => Still("scrolller/s/p" + i)).ToList(),
            clips: Enumerable.Range(0, BackRoomRemotePool.ClipReadyMax + 6).Select(i => Clip("scrolller/s/c" + i)).ToList());

        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.Ready().Count, BackRoomRemotePool.ReadyMax);
        await Settle(() => pool.ReadyClips().Count, BackRoomRemotePool.ClipReadyMax);

        Assert.Equal(BackRoomRemotePool.ReadyMax, pool.Ready().Count);
        Assert.Equal(BackRoomRemotePool.ClipReadyMax, pool.ReadyClips().Count);
    }

    [Fact]
    public void ClipPoolIsSizedForTheWall_NotCopiedFromTheStills()
    {
        // The wall shows at most MAX_PICTURES = 8 (room\screens.js) and asks for 8 (room\main.js), so
        // the target is exactly one full wall and the ceiling sits barely over it: a clip costs a video
        // decoder when it plays, where a still costs a texture upload.
        Assert.Equal(8, BackRoomRemotePool.ClipReadyTarget);
        Assert.InRange(BackRoomRemotePool.ClipReadyMax, BackRoomRemotePool.ClipReadyTarget, 12);
        Assert.True(BackRoomRemotePool.ClipReadyTarget < BackRoomRemotePool.ReadyTarget,
            "the stills also feed a 13-picture card deal; clips only ever feed one wall");
        // Both ceilings together have to stay clear of RemoteMediaCache.MaxTrackedTempFiles = 50, which
        // deletes OLDEST-FIRST across every consumer: the flash pool and the For You feed materialize
        // into the same 50, and a room that spent it all would sweep its own live wall urls away.
        Assert.True(BackRoomRemotePool.ReadyMax + BackRoomRemotePool.ClipReadyMax <= 34,
            "the room's two ceilings must leave the other consumers room inside the 50-file tracker");
    }

    [Fact]
    public async Task WarmPool_ForgetsAClipWhoseFileWentAway()
    {
        var (pool, _, _) = Pool(Settings(),
            clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/a"), Clip("scrolller/s/b") });
        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.ReadyClips().Count, 2);

        // Clips share the over-50 budget with every other consumer, so a file can go out from under a
        // warm url. A dealt url always loads or it is not dealt.
        foreach (var f in Directory.GetFiles(_temp, "*.mp4").Take(1)) File.Delete(f);

        Assert.Single(pool.ReadyClips());
    }

    [Fact]
    public async Task WarmPool_DrainReleasesTheClipsAndTheStillsAlike()
    {
        var (pool, _, released) = Pool(Settings(),
            stills: new List<FypAssetManifest.Entry> { Still("scrolller/s/p") },
            clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/c") });
        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.Ready().Count + pool.ReadyClips().Count, 2);

        pool.Drain();

        Assert.Equal(2, released.Count);
        Assert.Empty(pool.Ready());
        Assert.Empty(pool.ReadyClips());
        Assert.Empty(Directory.GetFiles(_temp));
    }

    [Fact]
    public async Task WarmPool_WarmsNoClipsWhenTheSourceDoesNotWantRemote()
    {
        foreach (var room in new[] { "local", "bundled", "auto" })
        {
            var (pool, materialized, _) = Pool(Settings(room, "local"),
                clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/a") });
            Assert.False(pool.Wanted);
            pool.EnsureWarm();
            await pool.WarmAsync(CancellationToken.None);
            Assert.Empty(materialized);
            Assert.Empty(pool.ReadyClips());
        }

        // Consent withdrawn is the same refusal, whatever the room asked for.
        var (denied, deniedMaterialized, _) = Pool(Settings("online", "online", consent: false),
            clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/a") });
        Assert.False(denied.Wanted);
        await denied.WarmAsync(CancellationToken.None);
        Assert.Empty(deniedMaterialized);
    }

    [Fact]
    public async Task WarmPool_ATransportFailureWarmsNoClipsAndTheWallDegrades()
    {
        var s = Settings();
        var (pool, materialized, _) = Pool(s, clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/a") },
            error: "offline");

        await pool.WarmAsync(CancellationToken.None);

        Assert.Empty(materialized);
        Assert.Empty(pool.ReadyClips());
        // A provider that is down must look like "no remote content", never like a broken wall.
        var deal = Media(s, pool, new[] { Gif("fallbackToThis.gif") }).Deal(Room, 1, 8);
        Assert.Equal("pool", Assert.Single(deal.Gifs).Src);
        Assert.Equal("online", deal.Source);
    }

    // ---------------------------------------------------------------- what the log and the wire carry

    [Fact]
    public async Task WarmPool_TheClipSetLogsCountsOnly_NeverAPathAndNeverAUrl()
    {
        var (pool, _, _) = Pool(Settings(), clips: new List<FypAssetManifest.Entry>
        {
            Clip("scrolller/s/a"),
            new() { Id = "scrolller/s/mkv", Url = "https://cdn.example.com/x.mkv", Type = "video", Origin = "online" },
        });
        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.ReadyClips().Count, 1);
        Media(Settings(), pool, new[] { Gif("x.gif") }).Deal(Room, 1, 8);

        var log = LogText();
        Assert.NotEmpty(_capture.Events);
        // The set names itself, so "warmed 1 clip(s), dropped 1" reads without a url to explain it.
        Assert.Contains("clip", log);
        Assert.DoesNotContain(_root, log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateUserName", log);
        Assert.DoesNotContain("ccp.assets", log);
        Assert.DoesNotContain("example.com", log);
        Assert.DoesNotContain("cdn.", log);
        Assert.DoesNotContain(".mp4", log);
        Assert.DoesNotContain("ccp_temp_remote_", log);
    }

    [Fact]
    public async Task Deal_CarriesNoPathOrRemoteHostWhenTheWallIsDealtClips()
    {
        var (pool, _, _) = Pool(Settings(), clips: new List<FypAssetManifest.Entry> { Clip("scrolller/s/a") });
        await pool.WarmAsync(CancellationToken.None);
        await Settle(() => pool.ReadyClips().Count, 1);

        var json = JsonConvert.SerializeObject(await Media(Settings(), pool, null).DealAsync(Room, 5, 8));

        // It really is the clip on the wire, and still only ever a ccp.assets url.
        Assert.Contains(".mp4", json);
        Assert.DoesNotContain(_root, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateUserName", json);
        Assert.DoesNotContain("example.com", json);
        Assert.DoesNotContain("\\\\", json);
    }
}
