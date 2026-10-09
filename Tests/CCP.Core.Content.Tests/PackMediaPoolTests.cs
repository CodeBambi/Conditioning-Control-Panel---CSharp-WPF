using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Core.Content.Tests;

/// <summary>
/// The pack half of the flash / video / bubble pools (WPF FlashService PackBag + _tempPackFiles,
/// VideoService _packVideoQueue): a shuffled walk keyed on the SOURCE key, decrypts on record and
/// swept, the list re-read after PacksChanged, and the local split weighted by count.
/// </summary>
public sealed class PackMediaPoolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-packpool-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings = new();

    public PackMediaPoolTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private ContentPackStore NewStore() =>
        new(() => _root, () => _settings, () => { }, tempDir: () => Path.Combine(_root, ".temp"));

    [Fact]
    public void Walk_CoversEveryEntryOnce_BeforeAnyRepeat_AndDecryptsAreFresh()
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.png", "image"), ("b.png", "image"), ("c.png", "image"), ("v.mp4", "video"));
        var store = NewStore();
        var pool = new PackMediaPool(ContentPackStore.ImageType, new Random(7), () => store);

        Assert.Equal(3, pool.Count);
        var keys = new List<string>();
        var temps = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            Assert.True(pool.TryNext(out var e));
            keys.Add(ContentPackStore.SourceKey(e.PackId, e.File));
            temps.Add(pool.Decrypt(e)!);
        }
        Assert.Equal(3, keys.Distinct().Count());
        Assert.All(temps, t => Assert.True(File.Exists(t)));
        Assert.Equal(3, temps.Distinct().Count());
        Assert.True(store.TryGetSourceKey(temps[0], out var k0));
        Assert.Equal(keys[0], k0);

        pool.Release(temps[0]);
        Assert.False(File.Exists(temps[0]));
        Assert.Equal(2, pool.TempCount);
        pool.CleanupTemps();
        Assert.All(temps, t => Assert.False(File.Exists(t)));
        Assert.Equal(0, pool.TempCount);
    }

    [Fact]
    public void Decrypts_PastTheCap_DropTheOldestHalf()
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.mp4", "video"));
        var store = NewStore();
        var pool = new PackMediaPool(ContentPackStore.VideoType, new Random(1), () => store);
        Assert.True(pool.TryNext(out var e));
        var temps = Enumerable.Range(0, PackMediaPool.TempCap + 1).Select(_ => pool.Decrypt(e)!).ToList();
        Assert.Equal(PackMediaPool.TempCap + 1, pool.TempCount);

        var newest = pool.Decrypt(e)!;
        Assert.False(File.Exists(temps[0]));
        Assert.True(File.Exists(temps[^1]));
        Assert.True(File.Exists(newest));
        Assert.True(pool.TempCount <= PackMediaPool.TempCap / 2 + 2);
        pool.CleanupTemps();
    }

    [Fact]
    public void PacksChanged_And_OptOuts_ReachTheNextDraw()
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.png", "image"), ("b.png", "image"));
        var store = NewStore();
        var pool = new PackMediaPool(ContentPackStore.ImageType, new Random(3), () => store);
        Assert.Equal(2, pool.Count);

        store.DeactivatePack("p1");
        Assert.Equal(0, pool.Count);
        Assert.False(pool.TryNext(out _));

        store.ActivatePack("p1");
        Assert.Equal(2, pool.Count);

        var a = store.GetPackFiles("p1").First(f => f.OriginalName == "a.png");
        _settings.DisabledAssetPaths.Add(ContentPackStore.SelectionKey("p1", a));
        pool.Invalidate();
        Assert.Equal(1, pool.Count);
        Assert.True(pool.TryNext(out var only));
        Assert.Equal("b.png", only.File.OriginalName);
    }

    [Fact]
    public void ShouldDrawPack_WeighsByCount()
    {
        var rng = new Random(11);
        Assert.False(FlashSourceRules.ShouldDrawPack(5, 0, rng));
        Assert.True(FlashSourceRules.ShouldDrawPack(0, 3, rng));
        var packs = Enumerable.Range(0, 4000).Count(_ => FlashSourceRules.ShouldDrawPack(30, 10, rng));
        Assert.InRange(packs, 800, 1200);   // 10 of 40 = a quarter
    }

    [Fact]
    public void VideoScheduler_PlaysAPackClip_WhenOnlyPacksHoldVideos()
    {
        var (_, plain) = ContentPackStoreTests.WritePack(_root, "p1", "One", ("v.mp4", "video"));
        var store = NewStore();
        var pool = new PackMediaPool(ContentPackStore.VideoType, new Random(2), () => store);
        var scheduler = new MandatoryVideoScheduler(new NullHost(), library: () => Array.Empty<string>()) { PackVideos = pool };

        var pick = scheduler.PickNext();
        Assert.NotNull(pick);
        Assert.Equal(plain["v.mp4"], File.ReadAllBytes(pick!));
        Assert.True(store.TryGetSourceKey(pick!, out _));

        var local = new MandatoryVideoScheduler(new NullHost(), library: () => Array.Empty<string>());
        Assert.Null(local.PickNext());
        pool.CleanupTemps();
    }

    private sealed class NullHost : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }
}
