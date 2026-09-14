using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Remix;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The pure rules of the Jackpot Remix builder: cycling to eight, the size cap, the
/// asset url, and the two-file cache bound.</summary>
public class JackpotRemixPlanTests
{
    [Fact]
    public void Cycle_fills_eight_from_three_in_order()
    {
        var got = JackpotRemixPlan.Cycle(new[] { "a", "b", "c" });
        Assert.Equal(new[] { "a", "b", "c", "a", "b", "c", "a", "b" }, got);
    }

    [Fact]
    public void Cycle_one_source_repeats_eight_times()
    {
        var got = JackpotRemixPlan.Cycle(new[] { "only" });
        Assert.Equal(8, got.Count);
        Assert.All(got, s => Assert.Equal("only", s));
    }

    [Fact]
    public void Cycle_eight_or_more_takes_the_first_eight_untouched()
    {
        var ten = Enumerable.Range(0, 10).Select(i => "g" + i).ToList();
        Assert.Equal(ten.Take(8), JackpotRemixPlan.Cycle(ten));
    }

    [Fact]
    public void Cycle_nothing_is_nothing()
    {
        Assert.Empty(JackpotRemixPlan.Cycle(Array.Empty<string>()));
        Assert.Empty(JackpotRemixPlan.Cycle(null!));
        Assert.Equal(8, JackpotRemixPlan.Tiles);
    }

    [Fact]
    public void FilterBySize_drops_over_cap_missing_and_blank()
    {
        var sizes = new Dictionary<string, long>
        {
            ["small.gif"] = 1024,
            ["edge.gif"] = JackpotRemixPlan.MaxSourceBytes,
            ["big.gif"] = JackpotRemixPlan.MaxSourceBytes + 1,
            ["gone.gif"] = -1,
        };
        var got = JackpotRemixPlan.FilterBySize(
            new[] { "small.gif", "edge.gif", "big.gif", "gone.gif", "", "throws.gif" },
            p => sizes.TryGetValue(p, out var n) ? n : throw new IOException(p));
        Assert.Equal(new[] { "small.gif", "edge.gif" }, got);
    }

    [Fact]
    public void ToAssetUrl_maps_under_root_with_escaped_forward_slash_segments()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-remix-root");
        var file = Path.Combine(root, "My Gifs", "loop #1.gif");
        Assert.Equal("https://ccp.assets/My%20Gifs/loop%20%231.gif", JackpotRemixPlan.ToAssetUrl(root, file));
        Assert.Equal("https://ccp.assets/My%20Gifs/loop%20%231.gif", JackpotRemixPlan.ToAssetUrl(root + Path.DirectorySeparatorChar, file));
    }

    [Fact]
    public void ToAssetUrl_refuses_files_outside_the_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-remix-root");
        Assert.Null(JackpotRemixPlan.ToAssetUrl(root, Path.Combine(Path.GetTempPath(), "elsewhere", "x.gif")));
        Assert.Null(JackpotRemixPlan.ToAssetUrl(root, Path.Combine(root, "..", "x.gif")));
        Assert.Null(JackpotRemixPlan.ToAssetUrl(root, root));                 // the root itself is not a file under it
        Assert.Null(JackpotRemixPlan.ToAssetUrl(root + "2", Path.Combine(root, "x.gif"))); // prefix, not parent
        Assert.Null(JackpotRemixPlan.ToAssetUrl("", "x.gif"));
    }

    [Fact]
    public void Excess_keeps_the_two_newest_and_names_the_rest()
    {
        var t0 = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        var files = new[]
        {
            ("old.gif", t0),
            ("newest.gif", t0.AddMinutes(3)),
            ("mid.gif", t0.AddMinutes(1)),
            ("older.gif", t0.AddMinutes(-5)),
        };
        Assert.Equal(new[] { "old.gif", "older.gif" }, JackpotRemixPlan.Excess(files));
        Assert.Empty(JackpotRemixPlan.Excess(files.Take(2)));
        Assert.Equal(new[] { "old.gif", "newest.gif", "mid.gif", "older.gif" }.OrderBy(x => x),
            JackpotRemixPlan.Excess(files, 0).OrderBy(x => x));
        Assert.Equal(2, JackpotRemixPlan.KeepFiles);
    }

    [Fact]
    public void Bound_deletes_only_older_remix_files_on_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-remix-bound-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var t = DateTime.UtcNow;
            void Put(string name, int minutesAgo)
            {
                var p = Path.Combine(dir, name);
                File.WriteAllBytes(p, new byte[] { 1 });
                File.SetLastWriteTimeUtc(p, t.AddMinutes(-minutesAgo));
            }
            Put("remix-AAAA-1.gif", 30);
            Put("remix-BBBB-2.gif", 20);
            Put("remix-CCCC-3.gif", 10);
            Put("remix-DDDD-4.gif", 0);
            Put("keep.txt", 40);
            JackpotRemixPlan.Bound(dir);
            var left = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { "keep.txt", "remix-CCCC-3.gif", "remix-DDDD-4.gif" }, left);
            JackpotRemixPlan.Bound(Path.Combine(dir, "missing"));   // never throws
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void OutputName_carries_the_code_and_survives_bad_characters()
    {
        var utc = new DateTime(2026, 9, 14, 8, 5, 9, DateTimeKind.Utc);
        Assert.Equal("remix-CCP-7Q2K-20260914-080509.gif", JackpotRemixPlan.OutputName("CCP-7Q2K", utc));
        Assert.Equal("remix-a_b-20260914-080509.gif", JackpotRemixPlan.OutputName("a/b", utc));
        Assert.Equal("remix-0-20260914-080509.gif", JackpotRemixPlan.OutputName("", utc));
    }
}
