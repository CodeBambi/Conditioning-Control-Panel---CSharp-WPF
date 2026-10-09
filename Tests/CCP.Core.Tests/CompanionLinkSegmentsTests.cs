using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The pure half of WPF BuildLinkedInlines: which parts of a companion line are links.</summary>
public sealed class CompanionLinkSegmentsTests
{
    private static readonly Dictionary<string, string> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bambi TikTok - In Beat - Longer Version"] = "https://example.test/video/in-beat-longer-1.html",
        ["Bambi TikTok 1-8"] = "https://example.test/video/bambi-tiktok-1-8-2.html",
        ["Naughty Bambi"] = "https://example.test/video/naughty-bambi-3.html",
    };

    private static string Joined(IEnumerable<CompanionLinkSegments.Segment> s) => string.Concat(s.Select(x => x.Text));

    [Fact]
    public void KnownTitleInAPlainLineBecomesALink()
    {
        const string line = "Alright, let's watch Bambi TikTok - In Beat - Longer Version and see how it goes!";
        var segments = CompanionLinkSegments.Parse(line, Table);

        Assert.Equal(3, segments.Count);
        Assert.Equal("Alright, let's watch ", segments[0].Text);
        Assert.False(segments[0].IsLink);
        Assert.Equal("Bambi TikTok - In Beat - Longer Version", segments[1].Text);
        Assert.Equal(Table["Bambi TikTok - In Beat - Longer Version"], segments[1].Url);
        Assert.Equal(" and see how it goes!", segments[2].Text);
        Assert.Equal(line, Joined(segments));
    }

    [Fact]
    public void PlainLineStaysOnePlainSegment()
    {
        var segments = CompanionLinkSegments.Parse("just breathe for me", Table);
        Assert.Single(segments);
        Assert.False(segments[0].IsLink);
        Assert.Empty(CompanionLinkSegments.Parse("", Table));
        Assert.Empty(CompanionLinkSegments.Parse(null, Table));
    }

    [Fact]
    public void MarkdownUrlWinsOverTheTitleTable()
    {
        var segments = CompanionLinkSegments.Parse("try [Naughty Bambi](https://example.test/other) tonight", Table);
        var link = Assert.Single(segments, s => s.IsLink);
        Assert.Equal("Naughty Bambi", link.Text);
        Assert.Equal("https://example.test/other", link.Url);
        Assert.Equal("try Naughty Bambi tonight", Joined(segments));
        Assert.Equal("try Naughty Bambi tonight", CompanionLinkSegments.Flatten("try [Naughty Bambi](https://example.test/other) tonight"));
    }

    [Fact]
    public void NonWebMarkdownIsCollapsedAndNeverLinked()
    {
        var segments = CompanionLinkSegments.Parse("open [this](file:///c:/x.exe) now", new Dictionary<string, string>());
        Assert.DoesNotContain(segments, s => s.IsLink);
        Assert.Equal("open this now", Joined(segments));
    }

    [Fact]
    public void LongestTitleWinsAndTitlesMatchAnyCase()
    {
        var segments = CompanionLinkSegments.Parse("put on bambi tiktok - in beat - longer version", Table);
        var link = Assert.Single(segments, s => s.IsLink);
        Assert.Equal(Table["Bambi TikTok - In Beat - Longer Version"], link.Url);
        Assert.Equal("bambi tiktok - in beat - longer version", link.Text);   // her casing is kept
    }

    [Fact]
    public void NearMissTitleShowsTheRealPoolTitle()
    {
        var segments = CompanionLinkSegments.Parse("watch \"Bambi TikTok Mix 1-8\" for me", Table);
        var link = Assert.Single(segments, s => s.IsLink);
        Assert.Equal("Bambi TikTok 1-8", link.Text);
        Assert.Equal(Table["Bambi TikTok 1-8"], link.Url);
    }

    [Fact]
    public void RawUrlShowsThePoolTitleOrAReadableSlug()
    {
        var known = CompanionLinkSegments.Parse("here: https://example.test/video/naughty-bambi-3.html ok", Table);
        var a = Assert.Single(known, s => s.IsLink);
        Assert.Equal("Naughty Bambi", a.Text);

        var unknown = CompanionLinkSegments.Parse("here: https://example.test/video/soft-spiral-99.html ok", Table);
        var b = Assert.Single(unknown, s => s.IsLink);
        Assert.Equal("https://example.test/video/soft-spiral-99.html", b.Url);
        Assert.DoesNotContain("http", b.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TableKeepsHttpsPoolLetsKnownWinAndFoldsBuiltInUnder()
    {
        var table = CompanionLinkSegments.BuildTable(
            new Dictionary<string, string> { ["Pool Only"] = "https://example.test/pool", ["Plain Http"] = "http://example.test/no", ["Both"] = "https://example.test/pool-both" },
            new Dictionary<string, string> { ["both"] = "https://example.test/known-both" },
            new Dictionary<string, string> { ["Both"] = "https://example.test/builtin-both", ["Built In"] = "https://example.test/builtin" });

        Assert.Equal("https://example.test/pool", table["Pool Only"]);
        Assert.False(table.ContainsKey("Plain Http"));
        Assert.Equal("https://example.test/known-both", table["BOTH"]);
        Assert.Equal("https://example.test/builtin", table["Built In"]);
    }
}
