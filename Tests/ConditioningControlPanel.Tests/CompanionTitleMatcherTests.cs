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
    public void ApostrophesInContractionsAreNotQuoteDelimiters()
    {
        // Live bug: "It's … from PlatinumPuppets. It's …" extracted the garbage between two
        // apostrophes as a "quoted title" and search-linked it.
        var text = "It's a special video from PlatinumPuppets. It's perfect for you~";
        var spans = CompanionTitleMatcher.CandidateSpans(text);
        Assert.DoesNotContain(spans,
            s => text.Substring(s.Start, s.Length).Contains("from PlatinumPuppets. It"));
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

    [Theory]
    [InlineData("Try \"Bimbo Fun 1-3\" from someone.")]
    [InlineData("How about \"Bimbo Love - Nonstop Compilation 1-3\"?")]
    [InlineData("Here's a video called \"Sissy Fucked Hard\".")]
    public void SuggestionPhrasingsPassTheContextGate(string text)
    {
        // Live misses: she leads with "Try X" / "How about X", which contained no media noun.
        var span = CompanionTitleMatcher.CandidateSpans(text).First(s => s.Quoted);
        Assert.True(CompanionTitleMatcher.LooksLikeVideoContext(text, span.Start));
    }

    // ── the off-pool rewrite (owner decision: curation stays closed, no search links) ────
    [Fact]
    public void InventedTitleIsRewrittenToNearestPoolVideo()
    {
        var text = "Try \"Bambi TikTok Mix 1-8 - Nonstop Edit\" tonight, it's perfect~";
        var result = CompanionTitleMatcher.RewriteOffPoolTitles(text, Pool, out var n);
        Assert.Equal(1, n);
        Assert.Contains("\"Bambi TikTok 1-8 - Nonstop Edit\"", result);
        Assert.DoesNotContain("Mix", result);
    }

    [Fact]
    public void SiblingEpisodeRewritesToTheRealOne()
    {
        // 0.5 similarity: below the confident-link floor, above the rewrite floor - the
        // curation-closed answer is the nearest real episode.
        var text = "Here's a video called \"Sissy Dreams 7\" for you.";
        var result = CompanionTitleMatcher.RewriteOffPoolTitles(text, Pool, out var n);
        Assert.Equal(1, n);
        Assert.Contains("\"Sissy Dreams 3\"", result);
    }

    [Fact]
    public void HopelessInventionStaysPlainText()
    {
        var text = "How about \"Bimbo Love - Nonstop Compilation 1-3\"?";
        var result = CompanionTitleMatcher.RewriteOffPoolTitles(text, Pool, out var n);
        Assert.Equal(0, n);
        Assert.Equal(text, result);
    }

    [Fact]
    public void ExactPoolTitleIsLeftUntouched()
    {
        var text = "Watch \"Sissy Dreams 3\" right now.";
        var result = CompanionTitleMatcher.RewriteOffPoolTitles(text, Pool, out var n);
        Assert.Equal(0, n);
        Assert.Equal(text, result);
    }

    [Fact]
    public void QuotedProseOutsideVideoContextIsNotRewritten()
    {
        var text = "she whispered \"Bambi TikTok Mix 1-8 - Nonstop Edit\" in my ear";
        var result = CompanionTitleMatcher.RewriteOffPoolTitles(text, Pool, out var n);
        Assert.Equal(0, n);
        Assert.Equal(text, result);
    }

    [Fact]
    public void MultipleInventionsRewriteWithoutCorruptingOffsets()
    {
        var text = "Try \"Bambi TikTok Mix 1-8 - Nonstop Edit\" then watch \"Femdom Sissy Cocklust\" after~";
        var result = CompanionTitleMatcher.RewriteOffPoolTitles(text, Pool, out var n);
        Assert.Equal(2, n);
        Assert.Contains("\"Bambi TikTok 1-8 - Nonstop Edit\"", result);
        Assert.Contains("\"Femdom Enticed Sissy Cocklust\"", result);
        Assert.EndsWith("after~", result);
    }

    // ── the wire-history variant (root cause 0807: her own stored inventions were the
    //    few-shot bait that out-competed the in-prompt pool list, and sub-floor inventions
    //    were immortal because nothing could rewrite them) ───────────────────────────────
    [Fact]
    public void ForPrompt_HopelessInventionIsSubstitutedWithARealPoolTitle()
    {
        // The exact live reply (2026-08-07 16:17): zero pool overlap, so the display rewrite
        // leaves it — but on the wire it must become a real title or the model re-learns it.
        var text = "Of course, my love! Here's a video called \"Bimbo Love - Nonstop Compilation 1-3\" from Dvdhurytwuios. Enjoy!";
        var result = CompanionTitleMatcher.RewriteOffPoolTitlesForPrompt(text, Pool, out var n);

        Assert.Equal(1, n);
        Assert.DoesNotContain("Bimbo Love", result);
        Assert.Contains(Pool, e => result.Contains("\"" + e.Title + "\""));
    }

    [Fact]
    public void ForPrompt_UploaderAttributionIsStrippedWithTheInvention()
    {
        // "from Dvdhurytwuios" / "from PlatinumPuppets" is the other half of the taught
        // pattern; leaving it in the wire history keeps teaching creator-attribution.
        var text = "Here's a video called \"Bimbo Fun 1-10\" from Dvdhurytwuios. It's got everything you need.";
        var result = CompanionTitleMatcher.RewriteOffPoolTitlesForPrompt(text, Pool, out var n);

        Assert.Equal(1, n);
        Assert.DoesNotContain("from Dvdhurytwuios", result);
        Assert.Contains(". It's got everything you need.", result);
    }

    [Fact]
    public void ForPrompt_IsDeterministic()
    {
        // string.GetHashCode is per-process randomised; the substitution must not be, or the
        // same history renders different bytes call to call (and test to app).
        var text = "How about \"Sissy Training - Nonstop Compilation\" from PlatinumPuppets?";
        var a = CompanionTitleMatcher.RewriteOffPoolTitlesForPrompt(text, Pool, out _);
        var b = CompanionTitleMatcher.RewriteOffPoolTitlesForPrompt(text, Pool, out _);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ForPrompt_NearMissStillPrefersTheNearestTitle()
    {
        var text = "Try \"Bambi TikTok Mix 1-8 - Nonstop Edit\" tonight~";
        var result = CompanionTitleMatcher.RewriteOffPoolTitlesForPrompt(text, Pool, out var n);
        Assert.Equal(1, n);
        Assert.Contains("\"Bambi TikTok 1-8 - Nonstop Edit\"", result);
    }

    [Fact]
    public void ForPrompt_ExactPoolTitleAndPlainProseAreUntouched()
    {
        var exact = "Watch \"Sissy Dreams 3\" right now.";
        Assert.Equal(exact, CompanionTitleMatcher.RewriteOffPoolTitlesForPrompt(exact, Pool, out var n1));
        Assert.Equal(0, n1);

        var prose = "she whispered \"Bimbo Love - Nonstop Compilation 1-3\" in my ear";
        Assert.Equal(prose, CompanionTitleMatcher.RewriteOffPoolTitlesForPrompt(prose, Pool, out var n2));
        Assert.Equal(0, n2);
    }
}
