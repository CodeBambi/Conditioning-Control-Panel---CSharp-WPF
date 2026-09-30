using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>#627: a 1000-file flash folder is walked in full before anything repeats.</summary>
public class FlashShuffleBagTests
{
    private static List<string> Pool(int n) => Enumerable.Range(0, n).Select(i => $@"C:\a\{i:0000}.gif").ToList();

    private static ShuffleBag<string> Bag(int seed = 20260930) => new(p => p, new Random(seed));

    [Fact]
    public void A_thousand_draws_show_a_thousand_files()
    {
        var pool = Pool(1000);
        var bag = Bag();
        var seen = new HashSet<string>();
        for (int i = 0; i < 1000; i++)
        {
            Assert.True(bag.TryNext(pool, out var p));
            seen.Add(p);
        }
        Assert.Equal(1000, seen.Count);
    }

    [Fact]
    public void With_replacement_would_not_have()
    {
        // The old picker: independent draws. Kept as the number the bag fixes.
        var rng = new Random(20260930);
        var distinct = Enumerable.Range(0, 1000).Select(_ => rng.Next(1000)).Distinct().Count();
        Assert.InRange(distinct, 580, 680);
    }

    [Fact]
    public void Never_repeats_back_to_back_across_cycles()
    {
        var pool = Pool(5);
        for (int seed = 0; seed < 200; seed++)
        {
            var bag = Bag(seed);
            string? last = null;
            for (int i = 0; i < 40; i++)
            {
                Assert.True(bag.TryNext(pool, out var p));
                Assert.NotEqual(last, p);
                last = p;
            }
        }
    }

    [Fact]
    public void Each_cycle_is_a_full_permutation()
    {
        var pool = Pool(7);
        var bag = Bag();
        for (int cycle = 0; cycle < 5; cycle++)
        {
            var got = new HashSet<string>();
            for (int i = 0; i < 7; i++) { bag.TryNext(pool, out var p); got.Add(p); }
            Assert.Equal(7, got.Count);
        }
    }

    [Fact]
    public void A_single_entry_pool_still_deals()
    {
        var pool = Pool(1);
        var bag = Bag();
        for (int i = 0; i < 3; i++) { Assert.True(bag.TryNext(pool, out var p)); Assert.Equal(pool[0], p); }
    }

    [Fact]
    public void An_empty_pool_deals_nothing()
    {
        Assert.False(Bag().TryNext(new List<string>(), out _));
    }

    [Fact]
    public void A_pruned_pool_is_redealt_and_the_removed_entry_never_comes_back()
    {
        var pool = Pool(20);
        var bag = Bag();
        var seen = new HashSet<string>();
        for (int i = 0; i < 5; i++) { bag.TryNext(pool, out var p); seen.Add(p); }

        // The deselection prune edits the same list in place.
        var removed = pool.First(p => !seen.Contains(p));
        pool.Remove(removed);

        for (int i = 0; i < 14; i++)
        {
            Assert.True(bag.TryNext(pool, out var p));
            Assert.NotEqual(removed, p);
            Assert.True(seen.Add(p), "a refresh mid-cycle must not repeat what this cycle showed");
        }
        Assert.Equal(19, seen.Count);
    }

    [Fact]
    public void A_refreshed_list_keeps_the_cycle()
    {
        var pool = Pool(10);
        var bag = Bag();
        var seen = new HashSet<string>();
        for (int i = 0; i < 4; i++) { bag.TryNext(pool, out var p); seen.Add(p); }

        var fresh = new List<string>(pool); // RefreshImageLists hands back a new list
        for (int i = 0; i < 6; i++) { bag.TryNext(fresh, out var p); Assert.True(seen.Add(p)); }
        Assert.Equal(10, seen.Count);
    }

    [Fact]
    public void Identity_is_the_key_not_the_item()
    {
        // Pack entries decrypt to a fresh temp path each draw; the bag keys on the entry.
        var pool = new List<(string Pack, string Name)> { ("p", "a"), ("p", "b"), ("p", "a") };
        var bag = new ShuffleBag<(string Pack, string Name)>(e => $"pack:{e.Pack}/{e.Name}", new Random(1));
        var first = new List<string>();
        for (int i = 0; i < 2; i++) { bag.TryNext(pool, out var e); first.Add(e.Name); }
        Assert.Equal(new[] { "a", "b" }, first.OrderBy(x => x));
    }

    [Theory]
    [InlineData(@"C:\a\x.jpeg", ".jpe", false)]
    [InlineData(@"C:\a\x.jpeg", ".jpeg", true)]
    [InlineData(@"C:\a\x.tiff", ".tif", false)]
    [InlineData(@"C:\a\x.JPG", ".jpg", true)]
    public void Listing_keeps_a_file_on_its_own_extension_pass(string file, string ext, bool keep)
        => Assert.Equal(keep, MediaListing.Keep(file, ext, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

    [Fact]
    public void Listing_keeps_a_file_once()
    {
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(MediaListing.Keep(@"C:\a\x.gif", ".gif", listed));
        Assert.False(MediaListing.Keep(@"C:\A\X.gif", ".gif", listed));
    }
}
