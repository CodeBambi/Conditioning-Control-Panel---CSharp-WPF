using System.Linq;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The companion is told to name pool titles VERBATIM and the app links them — but measured in
/// a real session (2026-08-07) ~87% of her suggestions were near-misses or inventions, and both
/// link surfaces matched exact-substring only, so almost no suggestion ever became clickable.
/// These tests pin the recovery ladder: fuzzy token match for near-misses, search fallback for
/// inventions, and the guards that keep ordinary prose from being linkified.
/// </summary>
public class CompanionTitleMatcherTests
{
    private static readonly (string Title, string Url)[] Pool =
    {
        ("Bambi TikTok 1-8 - Nonstop Edit", "https://hypnotube.com/video/bambi-tiktok-18-1111.html"),
        ("Sissy Dreams 3", "https://hypnotube.com/video/sissy-dreams-3-2222.html"),
        ("Femdom Enticed Sissy Cocklust", "https://hypnotube.com/video/femdom-enticed-3333.html"),
        ("Overload", "https://hypnotube.com/video/overload-4444.html"),
    };

    // ── the exact reported near-miss: one inserted word ─────────────────────────────────
    [Fact]
    public void OneInsertedWordStillMatches()
    {
        var hit = CompanionTitleMatcher.BestFuzzy("Bambi TikTok Mix 1-8 - Nonstop Edit", Pool);
        Assert.NotNull(hit);
        Assert.Equal("Bambi TikTok 1-8 - Nonstop Edit", hit!.Value.Title);
    }

    [Fact]
    public void DroppedWordStillMatches()
    {
        var hit = CompanionTitleMatcher.BestFuzzy("Femdom Sissy Cocklust", Pool);
        Assert.NotNull(hit);
        Assert.Equal("Femdom Enticed Sissy Cocklust", hit!.Value.Title);
    }

    // ── inventions must NOT silently link to the wrong video ────────────────────────────
    [Fact]
    public void UnrelatedInventedTitleDoesNotMatch()
    {
        Assert.Null(CompanionTitleMatcher.BestFuzzy("Bimbo Love - Nonstop Compilation 1-3", Pool));
    }

    [Fact]
    public void SiblingEpisodeDoesNotSilentlyMatch()
    {
        // "Sissy Dreams 7" vs pool "Sissy Dreams 3": Jaccard 0.5 — must fall through to the
        // search fallback, not link the wrong episode.
        Assert.Null(CompanionTitleMatcher.BestFuzzy("Sissy Dreams 7", Pool));
    }

    // ── prose safety ────────────────────────────────────────────────────────────────────
    [Fact]
    public void OrdinaryProseDoesNotMatch()
    {
        Assert.Null(CompanionTitleMatcher.BestFuzzy("such a good girl for me", Pool));
    }

    [Fact]
    public void SingleTokenSpanNeverFuzzyMatches()
    {
        Assert.Null(CompanionTitleMatcher.BestFuzzy("Overload", Pool)); // exact matching's job
    }

    // ── candidate span extraction ───────────────────────────────────────────────────────
    [Fact]
    public void QuotedSpanIsFound()
    {
        var text = "you should watch \"Bambi TikTok Mix 1-8 - Nonstop Edit\" tonight~";
        var spans = CompanionTitleMatcher.CandidateSpans(text);
        Assert.Contains(spans, s => text.Substring(s.Start, s.Length) == "Bambi TikTok Mix 1-8 - Nonstop Edit");
    }

    [Fact]
    public void TitleCaseRunIsFoundUnquoted()
    {
        var text = "go watch Sissy Dreams Three right now";
        var spans = CompanionTitleMatcher.CandidateSpans(text);
        Assert.Contains(spans, s => text.Substring(s.Start, s.Length).StartsWith("Sissy Dreams"));
    }

    // ── search fallback ─────────────────────────────────────────────────────────────────
    [Fact]
    public void SearchUrlDropsEpisodeDigitsAndEscapes()
    {
        var url = CompanionTitleMatcher.BuildSearchUrl("Bimbo Fun 1-10");
        Assert.StartsWith("https://hypnotube.com/search/?q=", url);
        Assert.Contains("bimbo%20fun", url);
        Assert.DoesNotContain("10", url);
    }

    [Fact]
    public void VideoContextGateBlocksBareProse()
    {
        var prose = "she said \"good girl energy\" to me";
        var idx = prose.IndexOf("good girl energy");
        Assert.False(CompanionTitleMatcher.LooksLikeVideoContext(prose, idx));

        var videoish = "you should watch \"good girl energy\" now";
        var idx2 = videoish.IndexOf("good girl energy");
        Assert.True(CompanionTitleMatcher.LooksLikeVideoContext(videoish, idx2));
    }
}
