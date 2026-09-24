using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class RemoteFlashPoolTests
{
    [Fact]
    public void DrawsConsumeBatchSoPrefetchCanResume()
    {
        var pool = new RemoteFlashPool();
        pool.Select(new[] { "one" });
        for (int i = 0; i < 30; i++) Assert.True(pool.Add(pool.Generation, i.ToString()));
        var seen = new System.Collections.Generic.HashSet<string>();
        var random = new Random(1);
        for (int i = 0; i < 30; i++) Assert.True(seen.Add(pool.Take(random, _ => true)!));
        Assert.Equal(0, pool.Count);
        Assert.Null(pool.Take(random, _ => true));
    }

    [Fact]
    public void NicheChangeDropsReadyAndRejectsOldDownload()
    {
        var pool = new RemoteFlashPool();
        pool.Select(new[] { "old" });
        int old = pool.Generation;
        pool.Add(old, "cached");
        pool.Select(new[] { "new" });
        Assert.Equal(0, pool.Count);
        Assert.False(pool.Add(old, "download-finished-late"));
        Assert.True(pool.Add(pool.Generation, "new-clip"));
        Assert.Equal("new-clip", pool.Take(new Random(1), _ => true));
    }

    [Fact]
    public void DrainedPoolReusesShownClipsOldestFirst()
    {
        var pool = new RemoteFlashPool();
        pool.Select(new[] { "one" });
        for (int i = 0; i < 3; i++) pool.Add(pool.Generation, i.ToString());
        var random = new Random(1);
        var order = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 3; i++) order.Add(pool.Take(random, _ => true)!);
        Assert.Equal(0, pool.Count);
        Assert.Equal(3, pool.Available);
        // A burst of 8 against 3 downloaded clips: every pick is a picture, never null.
        for (int i = 0; i < 5; i++) Assert.Equal(order[i % 3], pool.TakeShown(_ => true));
    }

    [Fact]
    public void ShownReuseSkipsEvictedFilesAndNicheChangeForgetsThem()
    {
        var pool = new RemoteFlashPool();
        pool.Select(new[] { "one" });
        pool.Add(pool.Generation, "gone");
        pool.Add(pool.Generation, "kept");
        var random = new Random(1);
        pool.Take(random, _ => true);
        pool.Take(random, _ => true);
        Assert.Equal("kept", pool.TakeShown(url => url == "kept"));
        Assert.Equal(1, pool.Available);
        pool.Select(new[] { "two" });
        Assert.Null(pool.TakeShown(_ => true));
    }

    [Fact]
    public void ReorderedSameSelectionKeepsReadyClips()
    {
        var pool = new RemoteFlashPool();
        pool.Select(new[] { "One", "two" });
        pool.Add(pool.Generation, "clip");
        Assert.False(pool.Select(new[] { "TWO", "one" }));
        Assert.Equal(1, pool.Count);
    }
}
