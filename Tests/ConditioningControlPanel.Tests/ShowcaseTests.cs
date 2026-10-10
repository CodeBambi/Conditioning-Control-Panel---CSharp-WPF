using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Showcase;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Tonight Board's premium showcase: manifest parsing, who sees what, the per-install order,
/// one clip per cycle, and the sha256-checked cache. No player is started here.
/// </summary>
public class ShowcaseTests
{
    private static readonly Uri ManifestUri = new("https://example.test/releases/download/showcase-clips/showcase.json");

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static string ClipJson(string id, string tier, byte[] video, byte[]? poster = null) =>
        "{\"id\":\"" + id + "\",\"tier\":\"" + tier + "\",\"file\":\"showcase-" + id + ".mp4\",\"bytes\":" + video.Length +
        ",\"sha256\":\"" + Sha(video) + "\"" +
        (poster == null ? "" : ",\"poster\":\"showcase-" + id + ".jpg\",\"posterBytes\":" + poster.Length + ",\"posterSha256\":\"" + Sha(poster) + "\"") +
        ",\"seconds\":7}";

    private static string Manifest(params string[] clips) => "{\"version\":1,\"clips\":[" + string.Join(",", clips) + "]}";

    private static string? Copy(string key) => key.StartsWith("showcase_clip_unknown", StringComparison.Ordinal) ? null : "text:" + key;

    // ---- manifest -----------------------------------------------------------------

    [Fact]
    public void Manifest_reads_good_clips_and_drops_bad_ones()
    {
        var v = new byte[] { 1, 2, 3 };
        var json = Manifest(
            ClipJson("remote", "basic", v, new byte[] { 9 }),
            ClipJson("dtrh", "PRIME", v),
            ClipJson("chess", "free", v),                                   // not a paid tier
            ClipJson("Bad-Id", "prime", v),                                 // id pattern
            "{\"id\":\"evil\",\"tier\":\"prime\",\"file\":\"../evil.mp4\",\"bytes\":3,\"sha256\":\"" + Sha(v) + "\"}",
            "{\"id\":\"noshas\",\"tier\":\"prime\",\"file\":\"a.mp4\",\"bytes\":3,\"sha256\":\"xyz\"}",
            "{\"id\":\"huge\",\"tier\":\"prime\",\"file\":\"a.mp4\",\"bytes\":99999999,\"sha256\":\"" + Sha(v) + "\"}",
            ClipJson("remote", "prime", v));                                // duplicate id: first wins

        var m = ShowcaseManifestParser.Parse(json);

        Assert.Equal(1, m.Version);
        Assert.Equal(new[] { "remote", "dtrh" }, m.Clips.Select(c => c.Id));
        Assert.Equal(ShowcaseTier.Basic, m.Clips[0].Tier);
        Assert.Equal(ShowcaseTier.Prime, m.Clips[1].Tier);
        Assert.Equal("showcase-remote.jpg", m.Clips[0].Poster);
        Assert.Null(m.Clips[1].Poster);
    }

    [Fact]
    public void Manifest_with_a_bad_poster_keeps_the_clip()
    {
        var v = new byte[] { 1, 2, 3 };
        var json = Manifest("{\"id\":\"goon\",\"tier\":\"prime\",\"file\":\"g.mp4\",\"bytes\":3,\"sha256\":\"" + Sha(v) +
                            "\",\"poster\":\"..\\\\x.jpg\",\"posterBytes\":4,\"posterSha256\":\"" + Sha(v) + "\"}");
        var clip = Assert.Single(ShowcaseManifestParser.Parse(json).Clips);
        Assert.Null(clip.Poster);
        Assert.Null(clip.PosterSha256);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"clips\":\"nope\"}")]
    public void Manifest_that_is_not_one_reads_empty(string? json)
    {
        Assert.Empty(ShowcaseManifestParser.Parse(json).Clips);
    }

    // ---- tiers --------------------------------------------------------------------

    [Theory]
    [InlineData(BillboardTier.Free, ShowcaseTier.Basic, true)]
    [InlineData(BillboardTier.Free, ShowcaseTier.Prime, true)]
    [InlineData(BillboardTier.Basic, ShowcaseTier.Basic, false)]
    [InlineData(BillboardTier.Basic, ShowcaseTier.Prime, true)]
    [InlineData(BillboardTier.Prime, ShowcaseTier.Basic, false)]
    [InlineData(BillboardTier.Prime, ShowcaseTier.Prime, false)]
    public void Viewers_only_see_features_above_their_plan(BillboardTier viewer, ShowcaseTier feature, bool shows)
    {
        Assert.Equal(shows, ShowcaseRules.Shows(viewer, feature));
    }

    // ---- order ---------------------------------------------------------------------

    private static readonly string[] Ids = { "remote", "lockdown", "awareness", "breakout", "breakout_endless", "dtrh", "arcademy", "goon" };

    [Fact]
    public void Install_order_is_stable_for_a_seed_and_ignores_manifest_order()
    {
        var a = ShowcaseRules.InstallOrder(Ids, 12345);
        var b = ShowcaseRules.InstallOrder(Ids.Reverse(), 12345);
        Assert.Equal(a, b);
        Assert.Equal(Ids.OrderBy(x => x), a.OrderBy(x => x));
    }

    [Fact]
    public void Different_installs_get_different_orders()
    {
        var orders = Enumerable.Range(1, 40).Select(s => string.Join(",", ShowcaseRules.InstallOrder(Ids, s * 7919))).ToHashSet();
        Assert.True(orders.Count > 20, $"only {orders.Count} distinct orders out of 40 seeds");
    }

    [Fact]
    public void Install_order_matches_the_mockup_shuffle()
    {
        // The mockup's own LCG (mockup.html "per-install showcase order"), seed 0x4f2a, over the ids sorted.
        var order = ShowcaseRules.InstallOrder(new[] { "breakout", "dtrh", "lockdown", "remote" }, 0x4f2a);
        var expected = new[] { "breakout", "dtrh", "lockdown", "remote" };
        long r = 0x4f2a;
        for (int i = expected.Length - 1; i > 0; i--)
        {
            r = (r * 1103515245L + 12345L) & 0x7fffffffL;
            int j = (int)(r % (i + 1));
            (expected[i], expected[j]) = (expected[j], expected[i]);
        }
        Assert.Equal(expected, order);
    }

    [Fact]
    public void Next_clip_walks_the_order_one_per_cycle_and_wraps()
    {
        var order = new[] { "a", "b", "c", "d" };
        var all = new HashSet<string>(order);
        Assert.Equal("a", ShowcaseRules.NextClip(order, all, null));
        Assert.Equal("b", ShowcaseRules.NextClip(order, all, "a"));
        Assert.Equal("a", ShowcaseRules.NextClip(order, all, "d"));
        Assert.Equal("a", ShowcaseRules.NextClip(order, all, "gone-from-manifest"));
    }

    [Fact]
    public void Next_clip_skips_what_the_viewer_cannot_see()
    {
        var order = new[] { "a", "b", "c", "d" };
        var some = new HashSet<string> { "b", "d" };
        Assert.Equal("b", ShowcaseRules.NextClip(order, some, null));
        Assert.Equal("d", ShowcaseRules.NextClip(order, some, "b"));
        Assert.Equal("d", ShowcaseRules.NextClip(order, some, "c"));
        Assert.Equal("b", ShowcaseRules.NextClip(order, some, "d"));
        Assert.Null(ShowcaseRules.NextClip(order, new HashSet<string>(), null));
    }

    // ---- card -----------------------------------------------------------------------

    [Fact]
    public void Card_wears_its_tier_and_leads_to_premium()
    {
        var clip = new ShowcaseClip("dtrh", ShowcaseTier.Prime, "f.mp4", 3, new string('a', 64), null, 0, null, 7);
        var card = ShowcaseRules.BuildCard(clip, "art", Copy)!;

        Assert.Equal("showcase:dtrh", card.Id);
        Assert.Equal(BillboardCardKind.Showcase, card.Kind);
        Assert.Equal(BillboardBadge.Prime, card.Badge);
        Assert.Equal("clip", card.ArtKey);
        Assert.Equal("art", card.ArtData);
        Assert.Equal(BillboardActionKind.Tab, card.Action.Kind);
        Assert.Equal("premium", card.Action.Target);
        Assert.Equal("text:showcase_clip_dtrh_title", card.Title);
        Assert.Equal("text:showcase_eyebrow_prime", card.Eyebrow);
        Assert.True(card.Snoozable);

        var basic = ShowcaseRules.BuildCard(clip with { Id = "remote", Tier = ShowcaseTier.Basic }, null, Copy)!;
        Assert.Equal(BillboardBadge.Basic, basic.Badge);
        Assert.Equal(ShowcaseRules.BasicHue, basic.AccentHex);
    }

    [Fact]
    public void A_clip_without_copy_makes_no_card()
    {
        var clip = new ShowcaseClip("unknown", ShowcaseTier.Prime, "f.mp4", 3, new string('a', 64), null, 0, null, 7);
        Assert.Null(ShowcaseRules.BuildCard(clip, null, Copy));
    }

    [Fact]
    public void Every_clip_id_the_cutting_brief_allows_has_copy_in_english()
    {
        var en = File.ReadAllText(Path.Combine(SourceRoots.LanguagesDirectory, "en.json"));
        foreach (var id in Ids)
        {
            Assert.Contains("\"" + ShowcaseRules.TitleKey(id) + "\"", en);
            Assert.Contains("\"" + ShowcaseRules.LineKey(id) + "\"", en);
        }
    }

    // ---- cache ----------------------------------------------------------------------

    private sealed class FakeRelease : HttpMessageHandler
    {
        public readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);
        public readonly List<string> Asked = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var name = request.RequestUri!.Segments.Last();
            lock (Asked) Asked.Add(name);
            if (!Files.TryGetValue(name, out var data))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        }
    }

    private static string TempRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-showcase-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task Cache_keeps_a_clip_only_when_its_hash_matches()
    {
        var release = new FakeRelease();
        var good = Encoding.UTF8.GetBytes("a real clip");
        var poster = Encoding.UTF8.GetBytes("a poster");
        var json = Manifest(ClipJson("dtrh", "prime", good, poster), ClipJson("goon", "prime", good));
        var clips = ShowcaseManifestParser.Parse(json).Clips;
        release.Files["showcase-dtrh.mp4"] = good;
        release.Files["showcase-dtrh.jpg"] = poster;
        release.Files["showcase-goon.mp4"] = Encoding.UTF8.GetBytes("tampered!!!"); // same length, wrong bytes

        var cache = new ShowcaseCache(TempRoot(), ManifestUri, new HttpClient(release));

        Assert.False(cache.IsReady(clips[0]));
        Assert.True(await cache.EnsureAsync(clips[0]));
        Assert.True(cache.IsReady(clips[0]));
        Assert.Equal(good, File.ReadAllBytes(cache.ClipPath(clips[0])));
        Assert.NotNull(cache.ReadyPoster(clips[0]));

        Assert.False(await cache.EnsureAsync(clips[1]));
        Assert.False(cache.IsReady(clips[1]));
        Assert.False(File.Exists(cache.ClipPath(clips[1])));
    }

    [Fact]
    public async Task Cache_reuses_a_checked_file_and_replaces_a_corrupt_one()
    {
        var release = new FakeRelease();
        var good = Encoding.UTF8.GetBytes("clip bytes");
        var clip = ShowcaseManifestParser.Parse(Manifest(ClipJson("remote", "basic", good))).Clips[0];
        release.Files["showcase-remote.mp4"] = good;
        var root = TempRoot();

        var first = new ShowcaseCache(root, ManifestUri, new HttpClient(release));
        Assert.True(await first.EnsureAsync(clip));
        Assert.Single(release.Asked);

        // A new session finds it on disk and checks it without a download.
        var second = new ShowcaseCache(root, ManifestUri, new HttpClient(release));
        Assert.False(second.IsReady(clip));
        Assert.True(await second.EnsureAsync(clip));
        Assert.Single(release.Asked);

        // Corrupt on disk (same length): the next session throws it away and fetches again.
        File.WriteAllBytes(first.ClipPath(clip), Encoding.UTF8.GetBytes("clip bytez"));
        var third = new ShowcaseCache(root, ManifestUri, new HttpClient(release));
        Assert.True(await third.EnsureAsync(clip));
        Assert.Equal(2, release.Asked.Count);
        Assert.Equal(good, File.ReadAllBytes(third.ClipPath(clip)));
    }

    [Fact]
    public async Task Cache_refuses_a_body_bigger_than_the_manifest_says()
    {
        var release = new FakeRelease();
        var good = new byte[] { 1, 2, 3 };
        var clip = ShowcaseManifestParser.Parse(Manifest(ClipJson("goon", "prime", good))).Clips[0];
        release.Files["showcase-goon.mp4"] = new byte[1000];
        var cache = new ShowcaseCache(TempRoot(), ManifestUri, new HttpClient(release));
        Assert.False(await cache.EnsureAsync(clip));
    }

    [Fact]
    public async Task Prune_drops_files_the_manifest_no_longer_names()
    {
        var release = new FakeRelease();
        var a = Encoding.UTF8.GetBytes("aaa");
        var b = Encoding.UTF8.GetBytes("bbb");
        var clips = ShowcaseManifestParser.Parse(Manifest(ClipJson("dtrh", "prime", a), ClipJson("goon", "prime", b))).Clips;
        release.Files["showcase-dtrh.mp4"] = a;
        release.Files["showcase-goon.mp4"] = b;
        var cache = new ShowcaseCache(TempRoot(), ManifestUri, new HttpClient(release));
        await cache.EnsureAsync(clips[0]);
        await cache.EnsureAsync(clips[1]);

        cache.Prune(new[] { clips[1] });

        Assert.False(File.Exists(cache.ClipPath(clips[0])));
        Assert.True(File.Exists(cache.ClipPath(clips[1])));
        Assert.False(cache.IsReady(clips[0]));
    }

    // ---- provider -------------------------------------------------------------------

    private static async Task<bool> WaitFor(Func<bool> condition, int ms = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    private static BillboardContext Ctx(BillboardTier tier) => new(tier, DateTime.UtcNow, DateTime.Now);

    [Fact]
    public async Task Provider_fetches_then_shows_one_clip_and_moves_on_when_it_plays()
    {
        var release = new FakeRelease();
        var bytes = new Dictionary<string, byte[]>();
        var parts = new List<string>();
        foreach (var id in new[] { "remote", "dtrh", "goon" })
        {
            bytes[id] = Encoding.UTF8.GetBytes("clip " + id);
            release.Files["showcase-" + id + ".mp4"] = bytes[id];
            parts.Add(ClipJson(id, id == "remote" ? "basic" : "prime", bytes[id]));
        }
        release.Files["showcase.json"] = Encoding.UTF8.GetBytes(Manifest(parts.ToArray()));

        var provider = new ShowcaseProvider(new ShowcaseCache(TempRoot(), ManifestUri, new HttpClient(release)), Copy, () => DateTime.UtcNow);
        int changed = 0;
        provider.Changed += (_, _) => Interlocked.Increment(ref changed);

        // First ask: nothing on disk yet, the manifest fetch starts.
        Assert.Empty(provider.Current(Ctx(BillboardTier.Free)));
        Assert.True(await WaitFor(() => changed >= 1), "manifest fetch never raised Changed");

        // Second ask: the manifest is in, the first clip downloads.
        Assert.True(await WaitFor(() => provider.Current(Ctx(BillboardTier.Free)).Any()), "no card after the clip downloaded");

        var cards = provider.Current(Ctx(BillboardTier.Free)).ToList();
        var card = Assert.Single(cards);
        Assert.Equal(BillboardCardKind.Showcase, card.Kind);
        var art = Assert.IsType<ShowcaseClipArt>(card.ArtData);
        Assert.True(File.Exists(art.VideoPath));

        // Asking again without it playing shows the same clip.
        Assert.Equal(card.Id, provider.Current(Ctx(BillboardTier.Free)).Single().Id);

        // It plays: the next cycle brings a different clip (prefetched).
        art.Played!(art.ClipId);
        Assert.True(await WaitFor(() => provider.Current(Ctx(BillboardTier.Free)).Any(c => c.Id != card.Id)), "rotation did not move");

        // Prime never sees a showcase.
        Assert.Empty(provider.Current(Ctx(BillboardTier.Prime)));
    }

    [Fact]
    public async Task Basic_viewer_only_ever_gets_prime_clips()
    {
        var release = new FakeRelease();
        var parts = new List<string>();
        foreach (var (id, tier) in new[] { ("remote", "basic"), ("lockdown", "basic"), ("dtrh", "prime") })
        {
            var data = Encoding.UTF8.GetBytes("clip " + id);
            release.Files["showcase-" + id + ".mp4"] = data;
            parts.Add(ClipJson(id, tier, data));
        }
        release.Files["showcase.json"] = Encoding.UTF8.GetBytes(Manifest(parts.ToArray()));
        var provider = new ShowcaseProvider(new ShowcaseCache(TempRoot(), ManifestUri, new HttpClient(release)), Copy, () => DateTime.UtcNow);

        Assert.True(await WaitFor(() => provider.Current(Ctx(BillboardTier.Basic)).Any()));
        for (int i = 0; i < 4; i++)
        {
            var card = provider.Current(Ctx(BillboardTier.Basic)).Single();
            Assert.Equal("showcase:dtrh", card.Id);
            var art = (ShowcaseClipArt)card.ArtData!;
            art.Played!(art.ClipId);
        }
    }

    [Fact]
    public void Install_seed_survives_a_restart()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "state.json");
        var first = ShowcaseState.Load(path);
        Assert.NotEqual(0, first.Seed);
        first.LastShown = "dtrh";
        first.Save(path);
        var again = ShowcaseState.Load(path);
        Assert.Equal(first.Seed, again.Seed);
        Assert.Equal("dtrh", again.LastShown);
    }

    // ---- view -----------------------------------------------------------------------

    [Fact]
    public void Clip_view_shows_its_poster_and_starts_no_player_with_motion_off()
    {
        var root = TempRoot();
        var posterPath = Path.Combine(root, "poster.png");
        var videoPath = Path.Combine(root, "clip.mp4");
        File.WriteAllBytes(videoPath, new byte[] { 0 });

        WpfRenderHarness.OnStaThread(() =>
        {
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, new byte[4 * 4 * 4], 16);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var fs = File.Create(posterPath)) enc.Save(fs);

            var old = ConditioningControlPanel.Controls.Billboard.ClipArtView.MotionAllowed;
            ConditioningControlPanel.Controls.Billboard.ClipArtView.MotionAllowed = () => false;
            try
            {
                int played = 0;
                var view = new ConditioningControlPanel.Controls.Billboard.ClipArtView(
                    new ShowcaseClipArt("dtrh", videoPath, posterPath, _ => played++));
                var poster = Assert.IsType<System.Windows.Controls.Image>(Assert.Single(view.Children.Cast<object>()));
                Assert.NotNull(poster.Source);

                view.Play();
                Assert.Single(view.Children.Cast<object>()); // no MediaElement made
                Assert.Equal(0, played);

                view.Release();
                Assert.Null(poster.Source);
                view.Play(); // after Release: a no-op, never a throw
                view.Touch(new System.Windows.Point(.5, .5));

                // The poster file is not held open (BitmapCacheOption.OnLoad).
                File.Delete(posterPath);
            }
            finally
            {
                ConditioningControlPanel.Controls.Billboard.ClipArtView.MotionAllowed = old;
            }
        });
    }

    [Fact]
    public void Registration_makes_a_clip_view_for_the_clip_key()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            ShowcaseArtRegistration.Register();
            Assert.True(BillboardArt.IsRegistered("clip"));
            var view = BillboardArt.Create("clip", null);
            Assert.IsAssignableFrom<IBillboardArtView>(view);
            ((IBillboardArtView)view!).Release();
        });
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
