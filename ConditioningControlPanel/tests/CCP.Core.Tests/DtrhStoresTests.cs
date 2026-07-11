using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Core.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Coverage for the three DTRH telemetry/manifest stores ported to CCP.Core (slice S1 of the
/// DTRH web-game port). Reuses the temp-dir IAppEnvironment pattern from AchievementServiceLot7Tests.
/// All facts are deterministic; background writes are awaited via the internal WhenSaved seam.
/// </summary>
public class DtrhStoresTests
{
    private sealed class TestAppEnvironment : IAppEnvironment
    {
        public string Root { get; }
        public string BaseDirectory { get; } = AppContext.BaseDirectory;
        public string UserDataPath { get; }
        public string ApplicationDataPath { get; }
        public string EffectiveAssetsPath { get; }

        public TestAppEnvironment()
        {
            Root = Path.Combine(Path.GetTempPath(), $"ccp-dtrh-{Guid.NewGuid():N}");
            UserDataPath = Path.Combine(Root, "local");
            ApplicationDataPath = Path.Combine(Root, "roaming");
            EffectiveAssetsPath = Path.Combine(Root, "assets");
            Directory.CreateDirectory(UserDataPath);
            Directory.CreateDirectory(ApplicationDataPath);
            Directory.CreateDirectory(EffectiveAssetsPath);
        }

        public void Cleanup()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); } catch { }
        }
    }

    private static JObject Row(string name, string kind = "image",
        double seconds = 0, double weighted = 0, long grabs = 0, long pops = 0,
        long defuses = 0, long flings = 0) =>
        new()
        {
            ["name"] = name,
            ["kind"] = kind,
            ["seconds"] = seconds,
            ["weighted"] = weighted,
            ["grabs"] = grabs,
            ["pops"] = pops,
            ["defuses"] = defuses,
            ["flings"] = flings,
        };

    private static JObject StatsBatch(params JObject[] rows) => new() { ["stats"] = new JArray(rows) };

    // ---- DtrhAssetStatsStore ----

    [Fact]
    public async Task AssetStats_Merge_SumsAcrossBatches_AndClampsNegatives()
    {
        var env = new TestAppEnvironment();
        try
        {
            var store = new DtrhAssetStatsStore(env, NullLogger<DtrhAssetStatsStore>.Instance);
            store.Merge(StatsBatch(Row("a.gif", seconds: 2, weighted: 4, grabs: 1, pops: 0)));
            store.Merge(StatsBatch(
                Row("a.gif", seconds: 3, weighted: 6, grabs: 2, pops: 1),
                Row("neg.gif", seconds: -5, weighted: -5, grabs: -3, pops: -2)));   // all clamped to 0

            await store.WhenSaved;

            var path = Path.Combine(env.UserDataPath, "dtrh_asset_stats.json");
            Assert.True(File.Exists(path));
            var disk = JsonConvert.DeserializeObject<Dictionary<string, DtrhAssetStatsStore.AssetStat>>(File.ReadAllText(path))!;
            Assert.Equal(5, disk["a.gif"].Seconds);
            Assert.Equal(10, disk["a.gif"].Weighted);
            Assert.Equal(3, disk["a.gif"].Grabs);
            Assert.Equal(1, disk["a.gif"].Pops);
            Assert.Equal(0, disk["neg.gif"].Seconds);   // Math.Max(0, -5) clamp
            Assert.Equal(0, disk["neg.gif"].Weighted);
            Assert.Equal(0, disk["neg.gif"].Grabs);
            Assert.Equal(0, disk["neg.gif"].Pops);
        }
        finally { env.Cleanup(); }
    }

    [Fact]
    public async Task AssetStats_Merge_RoundTripsAcrossInstances()
    {
        var env = new TestAppEnvironment();
        try
        {
            var first = new DtrhAssetStatsStore(env, NullLogger<DtrhAssetStatsStore>.Instance);
            first.Merge(StatsBatch(Row("a.gif", weighted: 4, grabs: 1)));
            await first.WhenSaved;

            // A fresh instance must load dtrh_asset_stats.json and reflect the persisted totals.
            var second = new DtrhAssetStatsStore(env, NullLogger<DtrhAssetStatsStore>.Instance);
            Assert.Equal(new[] { "a.gif" }, second.TopAssets(10));
        }
        finally { env.Cleanup(); }
    }

    [Fact]
    public async Task AssetStats_Merge_IsCaseInsensitive()
    {
        var env = new TestAppEnvironment();
        try
        {
            var store = new DtrhAssetStatsStore(env, NullLogger<DtrhAssetStatsStore>.Instance);
            store.Merge(StatsBatch(Row("Foo", weighted: 5)));
            store.Merge(StatsBatch(Row("foo", weighted: 3)));   // same key (OrdinalIgnoreCase)
            await store.WhenSaved;

            var path = Path.Combine(env.UserDataPath, "dtrh_asset_stats.json");
            var disk = JsonConvert.DeserializeObject<Dictionary<string, DtrhAssetStatsStore.AssetStat>>(File.ReadAllText(path))!;
            Assert.Single(disk);                 // folded into one entry
            Assert.Equal(8, disk.Values.Single().Weighted);
        }
        finally { env.Cleanup(); }
    }

    [Fact]
    public void AssetStats_TopAssets_RanksByWeightedPlusGrabs8PlusPops2()
    {
        var env = new TestAppEnvironment();
        try
        {
            var store = new DtrhAssetStatsStore(env, NullLogger<DtrhAssetStatsStore>.Instance);
            // beta:  10 + 1*8 + 0   = 18
            // gamma:  0 + 0*8 + 5*2 = 10
            // alpha:  0 + 0*8 + 0   = 0
            store.Merge(StatsBatch(
                Row("beta", weighted: 10, grabs: 1),
                Row("gamma", pops: 5),
                Row("alpha")));

            Assert.Equal(new[] { "beta", "gamma", "alpha" }, store.TopAssets(3));
            Assert.Equal(new[] { "beta", "gamma" }, store.TopAssets(2));   // Take(n) bound
            Assert.Empty(store.TopAssets(0));                                 // Take(0) -> empty
        }
        finally { env.Cleanup(); }
    }

    // ---- DtrhSessionStatsStore ----

    [Fact]
    public void SessionStats_Record_SumsLifetime_AndTracksBestComboMax()
    {
        var env = new TestAppEnvironment();
        try
        {
            var store = new DtrhSessionStatsStore(env, NullLogger<DtrhSessionStatsStore>.Instance);
            var run1 = new JObject { ["bubblesPopped"] = 10, ["bestCombo"] = 5, ["sparksEarned"] = 100 };
            var run2 = new JObject { ["bubblesPopped"] = 7, ["bestCombo"] = 9, ["sparksEarned"] = 50 };

            store.Record(run1, "Gentle");
            var t = store.Record(run2, "Brutal");

            Assert.Equal(2, t.Runs);
            Assert.Equal(17, t.BubblesPopped);     // cumulative SUM
            Assert.Equal(150, t.SparksEarned);
            Assert.Equal(9, t.BestComboEver);       // MAX, not sum
        }
        finally { env.Cleanup(); }
    }

    [Fact]
    public void SessionStats_Record_MergesEffectsByKind()
    {
        var env = new TestAppEnvironment();
        try
        {
            var store = new DtrhSessionStatsStore(env, NullLogger<DtrhSessionStatsStore>.Instance);
            store.Record(new JObject { ["effectsByKind"] = new JObject { ["spark"] = 2, ["flash"] = 1 } }, "Gentle");
            var t = store.Record(new JObject { ["effectsByKind"] = new JObject { ["Spark"] = 1, ["mist"] = 3 } }, "Gentle");

            Assert.Equal(3, t.EffectsByKind["spark"]);              // OrdinalIgnoreCase merge: Spark+spark
            Assert.Equal(1, t.EffectsByKind["flash"]);
            Assert.Equal(3, t.EffectsByKind["mist"]);
        }
        finally { env.Cleanup(); }
    }

    [Fact]
    public async Task SessionStats_Record_GrowsRecentHistory()
    {
        var env = new TestAppEnvironment();
        try
        {
            var store = new DtrhSessionStatsStore(env, NullLogger<DtrhSessionStatsStore>.Instance);
            store.Record(new JObject { ["bubblesPopped"] = 1 }, "Gentle");
            store.Record(new JObject { ["bubblesPopped"] = 2 }, "Brutal");
            await store.WhenSaved;

            var root = JsonConvert.DeserializeObject<DtrhSessionStatsStore.Root>(
                File.ReadAllText(Path.Combine(env.UserDataPath, "dtrh_session_stats.json")))!;
            Assert.Equal(2, root.Recent.Count);
            Assert.Equal("Gentle", root.Recent[0].Difficulty);
            Assert.Equal("Brutal", root.Recent[1].Difficulty);
        }
        finally { env.Cleanup(); }
    }

    [Fact]
    public async Task SessionStats_Record_CapsRecentAtTwentyFive()
    {
        var env = new TestAppEnvironment();
        try
        {
            var store = new DtrhSessionStatsStore(env, NullLogger<DtrhSessionStatsStore>.Instance);
            for (int i = 0; i < 27; i++)
                store.Record(new JObject { ["bubblesPopped"] = i }, $"D{i}");
            await store.WhenSaved;

            var root = JsonConvert.DeserializeObject<DtrhSessionStatsStore.Root>(
                File.ReadAllText(Path.Combine(env.UserDataPath, "dtrh_session_stats.json")))!;
            Assert.Equal(25, root.Recent.Count);                  // HistoryCap trimming
            Assert.Equal("D2", root.Recent[0].Difficulty);        // oldest two dropped
            Assert.Equal("D26", root.Recent[24].Difficulty);      // newest kept
        }
        finally { env.Cleanup(); }
    }

    [Fact]
    public async Task SessionStats_RoundTripsAcrossInstances()
    {
        var env = new TestAppEnvironment();
        try
        {
            var first = new DtrhSessionStatsStore(env, NullLogger<DtrhSessionStatsStore>.Instance);
            first.Record(new JObject { ["bubblesPopped"] = 4 }, "Gentle");
            first.Record(new JObject { ["bubblesPopped"] = 6 }, "Gentle");
            await first.WhenSaved;

            // A fresh instance loads the persisted lifetime: Runs continues from 2.
            var second = new DtrhSessionStatsStore(env, NullLogger<DtrhSessionStatsStore>.Instance);
            var t = second.Record(new JObject { ["bubblesPopped"] = 1 }, "Gentle");
            Assert.Equal(3, t.Runs);
            Assert.Equal(11, t.BubblesPopped);     // 4 + 6 + 1
        }
        finally { env.Cleanup(); }
    }

    // ---- DtrhAssetManifest ----

    [Fact]
    public void Manifest_Build_ListsOnlyBrowserDecodable_AndCountsSkipped()
    {
        var env = new TestAppEnvironment();
        try
        {
            var images = Path.Combine(env.EffectiveAssetsPath, "images");
            var videos = Path.Combine(env.EffectiveAssetsPath, "videos");
            Directory.CreateDirectory(images);
            Directory.CreateDirectory(videos);
            // images/: decodable a.jpg/b.png/c.gif, junk .txt (ignored), browser-undecodable .wmv (skipped)
            File.WriteAllText(Path.Combine(images, "a.jpg"), "x");
            File.WriteAllText(Path.Combine(images, "b.png"), "x");
            File.WriteAllText(Path.Combine(images, "c.gif"), "x");
            File.WriteAllText(Path.Combine(images, "junk.txt"), "x");
            File.WriteAllText(Path.Combine(images, "only.wmv"), "x");
            // videos/: decodable v.mp4/w.webm, browser-undecodable x.avi (skipped)
            File.WriteAllText(Path.Combine(videos, "v.mp4"), "x");
            File.WriteAllText(Path.Combine(videos, "w.webm"), "x");
            File.WriteAllText(Path.Combine(videos, "x.avi"), "x");

            var manifest = new DtrhAssetManifest(env, NullLogger<DtrhAssetManifest>.Instance);
            var m = manifest.Build();

            Assert.Equal(3, m.Images.Count);
            Assert.Equal(2, m.Videos.Count);
            Assert.Equal(2, m.Skipped);              // wmv + avi
            Assert.False(m.Truncated);

            Assert.Equal(new[] { "a.jpg", "b.png", "c.gif" }, m.Images.Select(e => e.Name).OrderBy(n => n).ToArray());
            Assert.Equal(new[] { "v.mp4", "w.webm" }, m.Videos.Select(e => e.Name).OrderBy(n => n).ToArray());
            Assert.All(m.Images.Concat(m.Videos), e => Assert.StartsWith("https://ccp.assets/", e.Url));
            Assert.All(m.Images.Concat(m.Videos), e => Assert.DoesNotContain('\\', e.Url));
        }
        finally { env.Cleanup(); }
    }
}
