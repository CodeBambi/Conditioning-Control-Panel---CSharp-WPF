using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Fyp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// WHERE THE BACK ROOM'S PICTURES COME FROM (CONTRACT section 5, 10.13.C). The local dealing rules
/// live in <see cref="BackRoomMediaTests"/>; this suite holds the five sources, the mixed ratio, the
/// channel fallback, the warm remote pool and - the one that matters most - that a provider being
/// down looks like "no remote content" rather than an empty wall.
///
/// <para>Nothing here touches a network: the pool's batch fetch and its materialize are both seams,
/// and the "downloads" are real files written under a throwaway assets root, which is exactly what
/// makes them <c>ccp.assets</c> urls.</para>
/// </summary>
public sealed class BackRoomMediaSourceTests : IDisposable
{
    private sealed class Capture : ILogEventSink
    {
        public readonly List<LogEvent> Events = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private readonly string _root;
    private readonly string _temp;
    private readonly Capture _capture = new();
    private readonly ILogger _logger;

    public BackRoomMediaSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ccp-br-src-" + Guid.NewGuid().ToString("N"), "PrivateUserName", "assets");
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

    private static AppSettings Settings(string room = "auto", string app = "local", bool consent = false,
        int ratio = 30, IEnumerable<string>? subs = null, IEnumerable<string>? off = null,
        IEnumerable<string>? niches = null, IEnumerable<string>? custom = null)
    {
        var s = new AppSettings
        {
            RemoteMediaConsented = consent,
            MediaSource = app,
            BackRoomMediaSource = room,
            BackRoomMediaSubs = (subs ?? Array.Empty<string>()).ToList(),
            BackRoomMediaSubsOff = (off ?? Array.Empty<string>()).ToList(),
            FypOnlineNiches = (niches ?? Array.Empty<string>()).ToList(),
            FypOnlineCustomSubs = (custom ?? Array.Empty<string>()).ToList(),
        };
        // After MediaSource, because the ratio setter clamps to 5..95 and nothing else moves it.
        s.RemoteMediaRatio = ratio;
        return s;
    }

    /// <summary>A pool that is simply "these clips are warm", so a deal test never needs a fetch.
    /// Clip urls end <c>.mp4</c>, which is the only thing that makes one a clip anywhere in this
    /// feature; the deal rules under test here do not care what a remote pick is.</summary>
    private sealed class FakePool : IBackRoomRemotePool
    {
        public readonly List<BackRoomRemoteClip> Clips = new();
        public int Warms, Waits, Drains;
        public bool Wanted { get; set; } = true;
        public void EnsureWarm() => Warms++;
        public Task WarmAsync(CancellationToken ct) { Waits++; return Task.CompletedTask; }
        public IReadOnlyList<BackRoomRemoteClip> Ready() => Clips;
        public void Drain() { Drains++; Clips.Clear(); }

        public FakePool With(int n)
        {
            for (int i = 0; i < n; i++)
                Clips.Add(new BackRoomRemoteClip("scrolller/sub/c" + i, "https://ccp.assets/.temp/clip" + i + ".mp4", 640, 360));
            return this;
        }
    }

    private BackRoomMedia Media(AppSettings? settings, IBackRoomRemotePool? pool, IEnumerable<string>? images = null)
    {
        var imgs = (images ?? Array.Empty<string>()).ToList();
        return new BackRoomMedia(() => imgs, () => Array.Empty<string>(), () => _root, null, () => _logger,
            () => settings, () => pool);
    }

    private string LogText() => string.Join("\n", _capture.Events.Select(e =>
        e.RenderMessage() + " " + string.Join(" ", e.Properties.Values.Select(v => v.ToString()))));

    // ---------------------------------------------------------------- the source table

    [Theory]
    // auto follows the app, which is the default and the whole point of it.
    [InlineData("auto", "local", true, "local")]
    [InlineData("auto", "online", true, "online")]
    [InlineData("auto", "mixed", true, "mixed")]
    // ...and collapses when the user never agreed to remote content.
    [InlineData("auto", "online", false, "local")]
    [InlineData("auto", "mixed", false, "local")]
    // The room's own pick overrides the app in both directions.
    [InlineData("local", "online", true, "local")]
    [InlineData("online", "local", true, "online")]
    [InlineData("mixed", "local", true, "mixed")]
    [InlineData("online", "local", false, "local")]
    [InlineData("mixed", "local", false, "local")]
    // bundled is a choice, not a fallback, so consent and the app never touch it.
    [InlineData("bundled", "online", true, "bundled")]
    [InlineData("bundled", "local", false, "bundled")]
    public void Source_ResolvesTheWholeTable(string room, string app, bool consent, string expected)
    {
        var s = Settings(room, app, consent);
        // One authority, and the feed asks it rather than deciding for itself.
        Assert.Equal(expected, BackRoomHostService.EffectiveMediaSource(s));
        Assert.Equal(expected, Media(s, null).EffectiveSource(null));
        Assert.Equal(expected, Media(s, null).Deal("slot", 3).Source);
    }

    [Fact]
    public void Source_NoSettingsAtAll_IsLocal()
    {
        Assert.Equal("local", Media(null, null).EffectiveSource(null));
        Assert.Equal("local", Media(null, null).EffectiveSource("auto"));
    }

    [Theory]
    [InlineData("local", "local")]
    [InlineData("bundled", "bundled")]
    [InlineData("auto", "online")]      // auto hands it back to the setting, which is online here
    [InlineData(null, "online")]
    [InlineData("nonsense", "online")]  // off the whitelist: the setting decides, not the page
    public void Request_MayNarrowTheSource(string? requested, string expected)
        => Assert.Equal(expected, Media(Settings("online", "local", consent: true), null).EffectiveSource(requested));

    [Theory]
    [InlineData("online")]
    [InlineData("mixed")]
    public void Request_NeverWidensPastConsent(string requested)
        => Assert.Equal("local", Media(Settings("local", "local", consent: false), null).EffectiveSource(requested));

    // ---------------------------------------------------------------- what each source deals

    [Fact]
    public void Local_DealsTheAssetsFolderAndNeverTheWarmPool()
    {
        var pool = new FakePool().With(8);
        var deal = Media(Settings("local"), pool, new[] { Gif("a.gif"), Gif("b.gif") }).Deal("slot", 5);

        Assert.All(deal.Gifs, g => Assert.Equal("pool", g.Src));
        Assert.All(deal.Gifs, g => Assert.StartsWith("https://ccp.assets/images/", g.Url));
        Assert.Equal("local", deal.Source);
        // Not even asked: a local room must not pay for a warm.
        Assert.Equal(0, pool.Warms);
    }

    [Fact]
    public void Bundled_DealsTheFourBuiltInLoops_OnPurpose_EvenWithAFullLibrary()
    {
        var files = Enumerable.Range(0, 10).Select(i => Gif($"f{i}.gif")).ToList();
        var deal = Media(Settings("bundled"), new FakePool().With(8), files).Deal("cards", 9, 13);

        Assert.Equal(4, deal.Gifs.Count);
        Assert.All(deal.Gifs, g => Assert.Equal("fallback", g.Src));
        Assert.All(deal.Gifs, g => Assert.StartsWith(BackRoomMedia.FallbackBase, g.Url));
        Assert.Equal("bundled", deal.Source);
    }

    [Fact]
    public void Online_DealsWarmClips_MarkedOnline_AndNeverTheAnimatedHint()
    {
        var pool = new FakePool().With(8);
        var deal = Media(Settings("online", consent: true), pool, new[] { Gif("a.gif") }).Deal("room", 11, 8);

        Assert.Equal(8, deal.Gifs.Count);
        Assert.All(deal.Gifs, g => Assert.Equal("online", g.Src));
        Assert.All(deal.Gifs, g => Assert.StartsWith("https://ccp.assets/.temp/", g.Url));
        // The animated-webp hint is for a local .webp the host decodes. A clip is PLAYED by the page,
        // so claiming it decodes would only cost the host a decode it cannot do.
        Assert.All(deal.Gifs, g => Assert.DoesNotContain("#", g.Url));
        Assert.Equal(8, deal.Gifs.Select(g => g.Url).Distinct().Count());
        Assert.Equal("online", deal.Source);
    }

    [Fact]
    public void Online_SameSeedDealsTheSameWall()
    {
        var a = Media(Settings("online", consent: true), new FakePool().With(8), null).Deal("room", 77, 4);
        var b = Media(Settings("online", consent: true), new FakePool().With(8), null).Deal("room", 77, 4);
        Assert.Equal(a.Gifs.Select(g => g.Url), b.Gifs.Select(g => g.Url));
    }

    [Fact]
    public void Online_ADryPoolDegradesToLocal_AndAnEmptyLibraryToBundled()
    {
        var s = Settings("online", consent: true);

        // The provider is down or the pool is still cold: the player's own folders, not an empty wall.
        var degraded = Media(s, new FakePool(), new[] { Gif("a.gif"), Gif("b.gif") }).Deal("slot", 4);
        Assert.Equal(2, degraded.Gifs.Count);
        Assert.All(degraded.Gifs, g => Assert.Equal("pool", g.Src));

        // Nothing remote AND nothing local: the built-in loops, which is never an empty deal.
        var bottom = Media(s, new FakePool(), Array.Empty<string>()).Deal("slot", 4);
        Assert.Equal(4, bottom.Gifs.Count);
        Assert.All(bottom.Gifs, g => Assert.Equal("fallback", g.Src));

        // A pool that throws is the same thing as a pool that is empty.
        var thrown = Media(s, new ThrowingPool(), new[] { Gif("c.gif") }).Deal("slot", 4);
        Assert.All(thrown.Gifs, g => Assert.Equal("pool", g.Src));
    }

    private sealed class ThrowingPool : IBackRoomRemotePool
    {
        public bool Wanted => true;
        public void EnsureWarm() => throw new InvalidOperationException("nope");
        public Task WarmAsync(CancellationToken ct) => throw new InvalidOperationException("nope");
        public IReadOnlyList<BackRoomRemoteClip> Ready() => throw new InvalidOperationException("nope");
        public void Drain() => throw new InvalidOperationException("nope");
    }

    // ---------------------------------------------------------------- the mixed ratio

    [Theory]
    [InlineData(95, 0.75, 1.0)]
    [InlineData(5, 0.0, 0.25)]
    [InlineData(50, 0.3, 0.7)]
    public void Mixed_RemoteMediaRatioIsTheShareDrawnRemotely(int ratio, double low, double high)
    {
        var s = Settings("mixed", consent: true, ratio: ratio);
        var files = Enumerable.Range(0, 13).Select(i => Gif($"m{i}.gif")).ToList();

        int online = 0, total = 0;
        foreach (var seed in new[] { 1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 144, 233 })
        {
            var deal = Media(s, new FakePool().With(13), files).Deal("cards", seed, 13);
            Assert.Equal(13, deal.Gifs.Count);
            Assert.Equal("mixed", deal.Source);
            online += deal.Gifs.Count(g => g.Src == "online");
            total += deal.Gifs.Count;
        }

        double share = online / (double)total;
        Assert.InRange(share, low, high);
    }

    [Fact]
    public void Mixed_WhicheverSideIsDryYieldsToTheOther()
    {
        // Ratio 95 with an empty warm pool: still a full local deal, never a short one.
        var local = Media(Settings("mixed", consent: true, ratio: 95), new FakePool(),
            Enumerable.Range(0, 4).Select(i => Gif($"l{i}.gif"))).Deal("slot", 6);
        Assert.Equal(4, local.Gifs.Count);
        Assert.All(local.Gifs, g => Assert.Equal("pool", g.Src));

        // Ratio 5 with an empty library: still a full remote deal.
        var remote = Media(Settings("mixed", consent: true, ratio: 5), new FakePool().With(4),
            Array.Empty<string>()).Deal("slot", 6);
        Assert.Equal(4, remote.Gifs.Count);
        Assert.All(remote.Gifs, g => Assert.Equal("online", g.Src));
    }

    // ---------------------------------------------------------------- the channels

    [Fact]
    public void Channels_AreTheRoomsOwnList_MinusTheOnesSwitchedOff_CappedAtEight()
    {
        var s = Settings(
            subs: new[] { "one", "r/two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" },
            off: new[] { "three" });

        var channels = BackRoomRemotePool.RoomChannels(s);

        Assert.Equal(AppSettings.BackRoomMediaSubCap, channels.Count);
        Assert.DoesNotContain("three", channels);
        Assert.Contains("two", channels);   // "r/two" sanitizes the same way it does everywhere
        // The switched-off pill comes out BEFORE the cap, so turning one off promotes the next one
        // rather than leaving a hole: nine survives and ten is what the cap drops.
        Assert.Contains("nine", channels);
        Assert.DoesNotContain("ten", channels);
    }

    [Fact]
    public void Channels_FollowTheAppsOwnPickerWhenTheRoomsListIsEmpty()
    {
        // A player who never opens the room's picker gets the niches they already chose in Assets.
        var s = Settings(custom: new[] { "appOne", "r/appTwo" });
        var channels = BackRoomRemotePool.RoomChannels(s);
        Assert.Equal(new[] { "appOne", "appTwo" }, channels);

        // Every pill switched off is the same as an empty list, not an empty pool.
        var allOff = Settings(subs: new[] { "roomOne" }, off: new[] { "roomOne" }, custom: new[] { "appOne" });
        Assert.Equal(new[] { "appOne" }, BackRoomRemotePool.RoomChannels(allOff));

        // And no settings at all is not a crash.
        Assert.NotNull(BackRoomRemotePool.RoomChannels(null));
    }

    // ---------------------------------------------------------------- the warm pool

    /// <summary>A clip entry, the only class the pool keeps. <c>.mp4</c> and <c>video</c>, because
    /// <c>RemoteMediaFormats.Validate</c> is the single authority on what a remote entry may be.</summary>
    private FypAssetManifest.Entry Entry(string id, string ext = ".mp4", int w = 640, int h = 480)
        => new() { Id = id, Url = "https://cdn.example.com/" + id.Replace('/', '-') + ext, Type = "video", Width = w, Height = h, Origin = "online" };

    /// <summary>The real pool, with the fetch and the materialize as seams. A "download" writes a real
    /// file under the assets temp folder, which is what gives it a ccp.assets url.
    ///
    /// <para>The recorder lists are locked because the pool materializes up to
    /// <see cref="BackRoomRemotePool.MaterializeConcurrency"/> entries at once: List&lt;T&gt;.Add from
    /// two threads silently loses an item, and a test would then fail one run in three with
    /// "expected 2, actual 1" - a dropped Add, not a slow fill.</para></summary>
    private (BackRoomRemotePool Pool, List<string> Materialized, List<string> Released) Pool(
        AppSettings settings, params (List<FypAssetManifest.Entry>? Entries, string? Error)[] batches)
    {
        var materialized = new List<string>();
        var released = new List<string>();
        var recorder = new object();
        int batch = 0;
        var pool = new BackRoomRemotePool(
            () => settings,
            _ =>
            {
                var b = batches[Math.Min(batch++, batches.Length - 1)];
                return Task.FromResult((b.Entries ?? new List<FypAssetManifest.Entry>(), b.Error));
            },
            (url, _) =>
            {
                lock (recorder) materialized.Add(url);
                // A NEW guid path per call, exactly like RemoteMediaCache: that is the whole trap.
                var path = Path.Combine(_temp, "ccp_temp_remote_" + Guid.NewGuid().ToString("N") + Path.GetExtension(url));
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                return Task.FromResult<string?>(path);
            },
            p => { lock (recorder) released.Add(p); try { File.Delete(p); } catch { } },
            () => _root,
            () => _logger);
        return (pool, materialized, released);
    }

    [Fact]
    public async Task WarmPool_MaterializesOneEntryIdExactlyOnce()
    {
        // The trap: every MaterializeAsync mints a new guid filename, so materializing one picture
        // twice gives the page two urls it cannot dedupe and the same clip lands on the wall twice.
        var (pool, materialized, _) = Pool(Settings("online", consent: true),
            (new List<FypAssetManifest.Entry> { Entry("scrolller/s/a"), Entry("scrolller/s/a"), Entry("scrolller/s/b"), Entry("scrolller/s/a") }, null));

        await pool.WarmAsync(CancellationToken.None);

        Assert.Equal(2, materialized.Count);
        var ready = pool.Ready();
        Assert.Equal(2, ready.Count);
        Assert.Equal(2, ready.Select(r => r.Url).Distinct().Count());
        Assert.Equal(new[] { "scrolller/s/a", "scrolller/s/b" }, ready.Select(r => r.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task WarmPool_HandsTheDealCcpAssetsUrlsAndTheEntrySize()
    {
        var (pool, _, _) = Pool(Settings("online", consent: true),
            (new List<FypAssetManifest.Entry> { Entry("scrolller/s/a", w: 800, h: 600) }, null));
        await pool.WarmAsync(CancellationToken.None);

        var clip = Assert.Single(pool.Ready());
        Assert.StartsWith("https://ccp.assets/.temp/ccp_temp_remote_", clip.Url);
        Assert.Equal((800, 600), (clip.W, clip.H));

        // And that url survives the whole way into a deal, which is the unlock: a materialized remote
        // file is under EffectiveAssetsPath, so it already has a legal ccp.assets url.
        var deal = Media(Settings("online", consent: true), pool, null).Deal("room", 2, 4);
        Assert.Equal(clip.Url, Assert.Single(deal.Gifs).Url);
        Assert.Equal("online", deal.Gifs[0].Src);
    }

    [Fact]
    public async Task WarmPool_DropsAnythingThatIsNotAPlayableClip()
    {
        var (pool, materialized, _) = Pool(Settings("online", consent: true),
            (new List<FypAssetManifest.Entry>
            {
                Entry("scrolller/s/ok"),
                new() { Id = "scrolller/s/poster", Url = "https://cdn.example.com/p.webp", Type = "image", Origin = "online" },
                new() { Id = "", Url = "https://cdn.example.com/x.mp4", Type = "video", Origin = "online" },
                new() { Id = "scrolller/s/bad", Url = "https://cdn.example.com/x.mkv", Type = "video", Origin = "online" },
            }, null));

        await pool.WarmAsync(CancellationToken.None);

        // Only the clip was ever downloaded. The poster is the class the room discarded (2026-09-17):
        // a static picture never reaches a surface any more, not even as a fallback.
        Assert.Single(materialized);
        Assert.Equal("scrolller/s/ok", Assert.Single(pool.Ready()).Id);
    }

    [Fact]
    public async Task WarmPool_ATransportFailureWarmsNothingAndTheDealDegrades()
    {
        var s = Settings("online", consent: true);
        var (pool, materialized, _) = Pool(s, (null, "offline"));

        await pool.WarmAsync(CancellationToken.None);

        Assert.Empty(materialized);
        Assert.Empty(pool.Ready());
        // A provider that is down must look like "no remote content", never like a broken room.
        var deal = Media(s, pool, new[] { Gif("fallbackToThis.gif") }).Deal("slot", 1);
        Assert.Equal("pool", Assert.Single(deal.Gifs).Src);
        Assert.Equal("online", deal.Source);
    }

    [Fact]
    public async Task WarmPool_ForgetsAClipWhoseFileWentAway()
    {
        // RemoteMediaCache's over-50 rule deletes oldest-first across every consumer, so a file can
        // go out from under a warm url. A dealt url always loads or it is not dealt.
        var (pool, _, _) = Pool(Settings("online", consent: true),
            (new List<FypAssetManifest.Entry> { Entry("scrolller/s/a"), Entry("scrolller/s/b") }, null));
        await pool.WarmAsync(CancellationToken.None);
        Assert.Equal(2, pool.Ready().Count);

        foreach (var f in Directory.GetFiles(_temp).Take(1)) File.Delete(f);

        Assert.Single(pool.Ready());
    }

    [Fact]
    public async Task WarmPool_DoesNothingWhenTheSourceDoesNotWantRemote()
    {
        foreach (var room in new[] { "local", "bundled", "auto" })
        {
            var (pool, materialized, _) = Pool(Settings(room, "local", consent: true),
                (new List<FypAssetManifest.Entry> { Entry("scrolller/s/a") }, null));
            Assert.False(pool.Wanted);
            pool.EnsureWarm();
            await pool.WarmAsync(CancellationToken.None);
            Assert.Empty(materialized);
        }

        // Consent withdrawn is the same refusal, whatever the room asked for.
        var (denied, deniedMaterialized, _) = Pool(Settings("online", "online", consent: false),
            (new List<FypAssetManifest.Entry> { Entry("scrolller/s/a") }, null));
        Assert.False(denied.Wanted);
        await denied.WarmAsync(CancellationToken.None);
        Assert.Empty(deniedMaterialized);
    }

    [Fact]
    public async Task WarmPool_DoesNotFetchAgainInsideTheGap_AndDrainReleasesEveryFile()
    {
        var (pool, materialized, released) = Pool(Settings("online", consent: true),
            (new List<FypAssetManifest.Entry> { Entry("scrolller/s/a") }, null));

        await pool.WarmAsync(CancellationToken.None);
        await pool.WarmAsync(CancellationToken.None);   // inside WarmGapSeconds: no second batch
        Assert.Single(materialized);

        pool.Drain();
        Assert.Single(released);
        Assert.Empty(pool.Ready());
        Assert.Empty(Directory.GetFiles(_temp));
    }

    [Fact]
    public async Task WarmPool_LogsCountsOnly_NeverAPathAndNeverAUrl()
    {
        var (pool, _, _) = Pool(Settings("online", consent: true),
            (new List<FypAssetManifest.Entry> { Entry("scrolller/s/a") }, null));
        await pool.WarmAsync(CancellationToken.None);
        Media(Settings("online", consent: true), pool, new[] { Gif("x.gif") }).Deal("slot", 1);

        var log = LogText();
        Assert.NotEmpty(_capture.Events);
        Assert.DoesNotContain(_root, log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateUserName", log);
        Assert.DoesNotContain("ccp.assets", log);
        Assert.DoesNotContain("example.com", log);
        Assert.DoesNotContain("ccp_temp_remote_", log);
    }

    [Fact]
    public async Task Deal_CarriesNoPathOrRemoteHostEvenInOnlineMode()
    {
        var (pool, _, _) = Pool(Settings("online", consent: true),
            (new List<FypAssetManifest.Entry> { Entry("scrolller/s/a") }, null));
        await pool.WarmAsync(CancellationToken.None);

        var json = JsonConvert.SerializeObject(
            await Media(Settings("online", consent: true), pool, null).DealAsync("room", 5, 4));

        Assert.DoesNotContain(_root, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateUserName", json);
        Assert.DoesNotContain("example.com", json);
        Assert.DoesNotContain("\\\\", json);
    }

    // ---------------------------------------------------------------- the async path

    [Fact]
    public async Task DealAsync_TopsThePoolUpOnlyWhenTheSourceWantsRemote()
    {
        var pool = new FakePool().With(4);
        await Media(Settings("local"), pool, null).DealAsync("slot", 1);
        Assert.Equal(0, pool.Waits);

        await Media(Settings("bundled"), pool, null).DealAsync("slot", 1);
        Assert.Equal(0, pool.Waits);

        await Media(Settings("online", consent: true), pool, null).DealAsync("slot", 1);
        await Media(Settings("mixed", consent: true), pool, null).DealAsync("slot", 1);
        Assert.Equal(2, pool.Waits);
    }

    [Fact]
    public async Task DealAsync_DealsTheSameHandAsDeal_AndAPoolThatThrowsStillDeals()
    {
        var s = Settings("online", consent: true);
        var sync = Media(s, new FakePool().With(6), null).Deal("room", 31, 4);
        var async = await Media(s, new FakePool().With(6), null).DealAsync("room", 31, 4);
        Assert.Equal(sync.Gifs.Select(g => g.Url), async.Gifs.Select(g => g.Url));

        var survived = await Media(s, new ThrowingPool(), new[] { Gif("t.gif") }).DealAsync("slot", 2);
        Assert.Equal("pool", Assert.Single(survived.Gifs).Src);
    }

    [Fact]
    public void WarmForRoomOpen_KicksThePool_AndSurvivesOneThatThrows()
    {
        var pool = new FakePool();
        var media = Media(Settings("online", consent: true), pool, null);
        media.WarmForRoomOpen();
        media.ReleaseWarmPool();
        Assert.Equal(1, pool.Warms);
        Assert.Equal(1, pool.Drains);

        var thrower = Media(Settings("online", consent: true), new ThrowingPool(), null);
        thrower.WarmForRoomOpen();
        thrower.ReleaseWarmPool();
    }

    // ---------------------------------------------------------------- the wire

    [Theory]
    [InlineData("\"online\"", "online")]
    [InlineData("\"mixed\"", "mixed")]
    [InlineData("\"local\"", "local")]
    [InlineData("\"bundled\"", "bundled")]
    [InlineData("\"auto\"", "auto")]
    [InlineData("\"nonsense\"", null)]
    [InlineData("\"Online\"", null)]
    [InlineData("true", null)]
    [InlineData("3", null)]
    [InlineData("null", null)]
    [InlineData("{}", null)]
    public void MediaSourceRequest_IsShapeStrict(string json, string? expected)
        => Assert.Equal(expected, BackRoomBridge.MediaSourceRequest(JToken.Parse(json)));

    [Fact]
    public void MediaSourceRequest_IsNullWhenTheFieldIsAbsent()
        => Assert.Null(BackRoomBridge.MediaSourceRequest(JObject.Parse("{\"reqId\":\"x\"}")["source"]));

    /// <summary>A media feed that only records what the bridge asked it for.</summary>
    private sealed class RecordingMedia : IBackRoomMedia
    {
        public readonly List<(int Count, string? Source)> Asks = new();
        public string Report = "mixed";
        public BackRoomMediaDeal Deal(string station, int seed, int count = 4)
            => throw new InvalidOperationException("the bridge must use DealAsync");
        public Task<BackRoomMediaDeal> DealAsync(string station, int seed, int count = 4, string? source = null,
            CancellationToken ct = default)
        {
            Asks.Add((count, source));
            return Task.FromResult(new BackRoomMediaDeal(seed,
                new[] { new BackRoomGif("g0", "https://ccp.assets/.temp/a.webp", 4, 3, "online") },
                new[] { new BackRoomWord("s0", "Drop", "preset") }, Report));
        }
    }

    private static (BackRoomBridge Bridge, RecordingMedia Media, List<JObject> Posted) Rig()
    {
        var media = new RecordingMedia();
        var posted = new List<JObject>();
        var bridge = new BackRoomBridge(new BackRoomBridge.Deps
        {
            Post = m => posted.Add(JObject.FromObject(m)),
            Relay = new BackRoomBridgeTests.Relay(),
            Media = media,
            BuildInit = () => new { type = "init" },
            CloseWindow = () => { },
            Schedule = (_, _) => () => { },
            NextSeed = () => 42,
        });
        return (bridge, media, posted);
    }

    [Fact]
    public void MediaRequest_PassesTheSourceThrough_AndTheReplyEchoesWhatTheDealResolvedTo()
    {
        var (bridge, media, posted) = Rig();
        bridge.Handle(JObject.Parse("""{"type":"media-request","reqId":"m1","station":"room","count":8,"source":"online"}"""));

        Assert.Equal((8, "online"), Assert.Single(media.Asks));
        var reply = Assert.Single(posted, p => (string?)p["type"] == "media");
        // Additive and otherwise unchanged: the same reqId, seed, gifs and words.
        Assert.Equal("m1", (string?)reply["reqId"]);
        Assert.Equal(42, (int)reply["seed"]!);
        Assert.Equal("mixed", (string?)reply["source"]);
        var gif = Assert.Single((JArray)reply["gifs"]!);
        Assert.Equal(new[] { "key", "url", "w", "h", "src" }, ((JObject)gif).Properties().Select(p => p.Name));
        Assert.Equal("online", (string?)gif["src"]);
        var word = Assert.Single((JArray)reply["words"]!);
        Assert.Equal(new[] { "key", "text", "src" }, ((JObject)word).Properties().Select(p => p.Name));
    }

    [Fact]
    public void MediaRequest_WithoutASource_AsksForNull()
    {
        var (bridge, media, posted) = Rig();
        bridge.Handle(JObject.Parse("""{"type":"media-request","reqId":"m2","station":"slot","source":"nonsense"}"""));
        Assert.Equal((4, null), Assert.Single(media.Asks));
        Assert.Equal("mixed", (string?)Assert.Single(posted, p => (string?)p["type"] == "media")["source"]);
    }

    [Fact]
    public void MediaRequest_AFailingDealStillRepliesWithTheBundledLoops()
    {
        var posted = new List<JObject>();
        var bridge = new BackRoomBridge(new BackRoomBridge.Deps
        {
            Post = m => posted.Add(JObject.FromObject(m)),
            Relay = new BackRoomBridgeTests.Relay(),
            Media = new BrokenMedia(),
            BuildInit = () => new { type = "init" },
            CloseWindow = () => { },
            Schedule = (_, _) => () => { },
            NextSeed = () => 9,
        });
        bridge.Handle(JObject.Parse("""{"type":"media-request","reqId":"m3","station":"slot"}"""));

        var reply = Assert.Single(posted, p => (string?)p["type"] == "media");
        Assert.Equal("bundled", (string?)reply["source"]);
        Assert.Equal(4, ((JArray)reply["gifs"]!).Count);
    }

    private sealed class BrokenMedia : IBackRoomMedia
    {
        public BackRoomMediaDeal Deal(string station, int seed, int count = 4) => throw new InvalidOperationException("boom");
    }
}
