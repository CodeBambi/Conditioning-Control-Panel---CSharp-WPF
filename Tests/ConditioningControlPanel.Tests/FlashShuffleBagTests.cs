using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #tier-2 2026-08-26 (Yiin, echoed by Wobberjockey): "about 1000 gifs in the flash folder and I
/// keep seeing the same 50". The flash picker drew with replacement - a fresh
/// <c>Random.Next(pool.Count)</c> per image - so the pool was never actually walked. These tests
/// pin the shuffle-bag replacement: N draws from an N-file pool return N distinct files, the bag
/// reshuffles only when it is spent, and a new bag never opens on the file the old one closed on.
/// </summary>
public class FlashShuffleBagTests
{
    private static List<string> Pool(int n) =>
        Enumerable.Range(0, n).Select(i => $"C:/assets/images/gif_{i:D4}.gif").ToList();

    [Fact]
    public void ThousandDrawsFromThousandFiles_AreAllDistinct()
    {
        var pool = Pool(1000);
        var bag = new ShuffleBag<string>(new Random(20260826));

        var drawn = new List<string>(1000);
        for (int i = 0; i < 1000; i++)
        {
            Assert.True(bag.TryNext(pool, out var path));
            drawn.Add(path);
        }

        Assert.Equal(1000, drawn.Distinct().Count());
        Assert.Equal(pool.OrderBy(p => p, StringComparer.Ordinal),
                     drawn.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void TheOldWithReplacementDraw_MissesMostOfTheFolder()
    {
        // The regression this replaces, stated as a number: 1000 independent picks from a
        // 1000-file pool surface roughly 632 distinct files, so a third of the library never
        // appears at all and the rest repeats. Fixed seed, so this is a fact, not a coin flip.
        var pool = Pool(1000);
        var rng = new Random(20260826);
        var drawn = new List<string>(1000);
        for (int i = 0; i < 1000; i++) drawn.Add(pool[rng.Next(pool.Count)]);

        Assert.InRange(drawn.Distinct().Count(), 1, 800);
    }

    [Fact]
    public void ThreeFullCycles_ShowEveryFileExactlyThreeTimes()
    {
        var pool = Pool(250);
        var bag = new ShuffleBag<string>(new Random(7));

        var counts = new Dictionary<string, int>();
        for (int i = 0; i < 750; i++)
        {
            Assert.True(bag.TryNext(pool, out var path));
            counts[path] = counts.TryGetValue(path, out var c) ? c + 1 : 1;
        }

        Assert.Equal(250, counts.Count);
        Assert.All(counts.Values, v => Assert.Equal(3, v));
    }

    [Fact]
    public void ReshuffleSeam_NeverRepeatsTheLastDraw()
    {
        // A tiny pool makes the seam come round constantly, which is where a naive
        // "shuffle, drain, shuffle" leaks a back-to-back repeat.
        for (int seed = 0; seed < 25; seed++)
        {
            var pool = Pool(4);
            var bag = new ShuffleBag<string>(new Random(seed));
            string? previous = null;
            for (int i = 0; i < 4000; i++)
            {
                Assert.True(bag.TryNext(pool, out var path));
                Assert.NotEqual(previous, path);
                previous = path;
            }
        }
    }

    [Fact]
    public void GrowingThePool_StartsDrawingTheNewFile()
    {
        var pool = Pool(100);
        var bag = new ShuffleBag<string>(new Random(11));
        for (int i = 0; i < 100; i++) Assert.True(bag.TryNext(pool, out _));

        pool.Add("C:/assets/images/brand_new.gif");
        var seen = new HashSet<string>();
        for (int i = 0; i < 101; i++)
        {
            Assert.True(bag.TryNext(pool, out var path));
            seen.Add(path);
        }

        Assert.Contains("C:/assets/images/brand_new.gif", seen);
        Assert.Equal(101, seen.Count);
    }

    [Fact]
    public void Invalidate_ReshufflesWithoutRepeatingTheLastDraw()
    {
        // Same count, different contents (a rescan that swapped a file, or a mod switch).
        var pool = Pool(50);
        var bag = new ShuffleBag<string>(new Random(3));
        Assert.True(bag.TryNext(pool, out var last));

        bag.Invalidate();
        Assert.Equal(0, bag.Remaining);

        Assert.True(bag.TryNext(pool, out var first));
        Assert.NotEqual(last, first);
        Assert.Equal(49, bag.Remaining);
    }

    [Fact]
    public void ShrinkingThePoolUnderTheBag_DoesNotThrow()
    {
        var pool = Pool(500);
        var bag = new ShuffleBag<string>(new Random(5));
        for (int i = 0; i < 10; i++) Assert.True(bag.TryNext(pool, out _));

        pool.RemoveRange(1, 499);   // deselecting almost everything, without an Invalidate
        for (int i = 0; i < 20; i++)
        {
            Assert.True(bag.TryNext(pool, out var path));
            Assert.Contains(path, pool);
        }
    }

    [Fact]
    public void EmptyPool_DrawsNothing()
    {
        var bag = new ShuffleBag<string>(new Random(1));
        Assert.False(bag.TryNext(new List<string>(), out _));
        Assert.False(bag.TryNext(null!, out _));
        Assert.Equal(0, bag.Remaining);
    }

    [Fact]
    public void SingleFilePool_KeepsReturningIt()
    {
        // The one case where a repeat is unavoidable: the seam guard must not deadlock or throw.
        var pool = Pool(1);
        var bag = new ShuffleBag<string>(new Random(2));
        for (int i = 0; i < 10; i++)
        {
            Assert.True(bag.TryNext(pool, out var path));
            Assert.Equal(pool[0], path);
        }
    }
}
