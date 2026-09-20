using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Deeper;
using Xunit;
using static ConditioningControlPanel.Services.Deeper.EnhancementLibraryFilter;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Deeper library filter/sort/count rules. The pill counts used to be computed
/// against search-only while the list applied search + type + tags, so the
/// numbers on the pills disagreed with the rows under them.
/// </summary>
public class EnhancementLibraryFilterTests
{
    private static EnhancementLibraryEntry E(string name, string type, double dur = 0, string creator = "", DateTime? mod = null, params string[] tags)
        => new()
        {
            FilePath = "C:\\lib\\" + name + ".ccpenh.json",
            Name = name,
            Creator = creator,
            MediaType = type,
            DurationSeconds = dur,
            LastModified = mod ?? DateTime.MinValue,
            AutoTags = tags.ToList(),
        };

    private static readonly string V = ConditioningControlPanel.Models.Deeper.MediaTypes.Video;
    private static readonly string A = ConditioningControlPanel.Models.Deeper.MediaTypes.Audio;

    private static List<EnhancementLibraryEntry> Library() => new()
    {
        E("Alpha", V, 120, "mort", new DateTime(2026, 9, 1), EnhancementAutoTagger.TagHaptics),
        E("Beta", A, 300, "mort", new DateTime(2026, 9, 3)),
        E("Gamma", V, 60, "zed", new DateTime(2026, 9, 2), EnhancementAutoTagger.TagWebcam, EnhancementAutoTagger.TagHaptics),
        E("Delta", A, 0, "zed", new DateTime(2026, 8, 30), EnhancementAutoTagger.TagWebcam),
    };

    [Fact]
    public void Search_MatchesNameCreatorAndTags()
    {
        var lib = Library();
        Assert.True(MatchesSearch(lib[0], "alp"));
        Assert.True(MatchesSearch(lib[0], "MORT"));
        Assert.True(MatchesSearch(lib[0], EnhancementAutoTagger.TagHaptics));
        Assert.False(MatchesSearch(lib[0], "zed"));
        Assert.True(MatchesSearch(lib[0], ""));
        Assert.False(MatchesSearch(null, "x"));
    }

    [Fact]
    public void PillCounts_AgreeWithTheFilteredList()
    {
        var lib = Library();
        var c = new Criteria("", MediaTypeFilter.Video, Haptics: false, Webcam: false);
        var counts = CountPills(lib, c);
        Assert.Equal(4, counts.All);
        Assert.Equal(2, counts.Video);
        Assert.Equal(2, counts.Audio);
        // Haptics/Webcam pills count against the active type filter (Video).
        Assert.Equal(2, counts.Haptics);
        Assert.Equal(1, counts.Webcam);
        Assert.Equal(counts.Video, lib.Count(e => Matches(e, c)));
    }

    [Fact]
    public void PillCounts_TypePillsRespectTagFilters()
    {
        var lib = Library();
        var c = new Criteria("", MediaTypeFilter.All, Haptics: true, Webcam: false);
        var counts = CountPills(lib, c);
        Assert.Equal(2, counts.All);
        Assert.Equal(2, counts.Video);
        Assert.Equal(0, counts.Audio);
        Assert.Equal(counts.All, lib.Count(e => Matches(e, c)));
    }

    [Fact]
    public void PillCounts_RespectSearch()
    {
        var lib = Library();
        var c = new Criteria("zed", MediaTypeFilter.All, false, false);
        var counts = CountPills(lib, c);
        Assert.Equal(2, counts.All);
        Assert.Equal(1, counts.Video);
        Assert.Equal(1, counts.Audio);
        Assert.Equal(2, counts.Webcam);
        Assert.Equal(1, counts.Haptics);
    }

    [Fact]
    public void Sort_ByDurationDescendingThenName()
    {
        var names = Sort(Library(), SortMode.Duration, descending: true).Select(e => e.Name).ToList();
        Assert.Equal(new[] { "Beta", "Alpha", "Gamma", "Delta" }, names);
        var asc = Sort(Library(), SortMode.Duration, descending: false).Select(e => e.Name).ToList();
        Assert.Equal(new[] { "Delta", "Gamma", "Alpha", "Beta" }, asc);
    }

    [Fact]
    public void Sort_ByRecentAndName()
    {
        Assert.Equal(new[] { "Beta", "Gamma", "Alpha", "Delta" },
            Sort(Library(), SortMode.Recent, descending: true).Select(e => e.Name).ToList());
        Assert.Equal(new[] { "Alpha", "Beta", "Delta", "Gamma" },
            Sort(Library(), SortMode.Name, descending: false).Select(e => e.Name).ToList());
        Assert.Equal(new[] { "Gamma", "Delta", "Beta", "Alpha" },
            Sort(Library(), SortMode.Name, descending: true).Select(e => e.Name).ToList());
    }

    [Fact]
    public void Sort_ByCreatorIsStableOnName()
    {
        Assert.Equal(new[] { "Alpha", "Beta", "Delta", "Gamma" },
            Sort(Library(), SortMode.Creator, descending: false).Select(e => e.Name).ToList());
    }

    [Theory]
    [InlineData(SortMode.Recent, true)]
    [InlineData(SortMode.Duration, true)]
    [InlineData(SortMode.Name, false)]
    [InlineData(SortMode.Creator, false)]
    public void DefaultDirection(SortMode mode, bool descending)
        => Assert.Equal(descending, DefaultDescending(mode));
}
