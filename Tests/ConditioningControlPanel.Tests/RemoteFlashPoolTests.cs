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
    public void ReorderedSameSelectionKeepsReadyClips()
    {
        var pool = new RemoteFlashPool();
        pool.Select(new[] { "One", "two" });
        pool.Add(pool.Generation, "clip");
        Assert.False(pool.Select(new[] { "TWO", "one" }));
        Assert.Equal(1, pool.Count);
    }
}
