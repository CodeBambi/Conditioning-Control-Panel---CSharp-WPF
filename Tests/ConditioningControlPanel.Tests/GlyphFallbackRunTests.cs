using System.Linq;
using ConditioningControlPanel.Services.Compositor;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #717 — the compositor's Skia text layers must draw EVERY codepoint of a phrase, not just the
/// ones the first resolved face happens to cover.
///
/// #615 picked one fallback face per string, from the first codepoint the primary could not draw.
/// The zh-CN reporter's subliminal phrase is "♤雌畜人妖♤": Arial lacks U+2664 WHITE SPADE SUIT AND
/// the Han body, U+2664 comes first, and the font manager answers it with Segoe UI Symbol — which
/// has no ideographs, so the Chinese drew as .notdef boxes. These tests assert the run split, so
/// the regression cannot come back through the same door.
///
/// They read the machine's real font table (that is the thing under test — coverage, not a
/// codepoint-range guess), so they assert the invariant "whatever face a run got, it can draw that
/// run" rather than naming families that differ across Windows builds.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class GlyphFallbackRunTests
{
    private static readonly SKTypeface Arial =
        SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;

    /// <summary>The exact phrase from BUG-29VA4QQQPU / BUG-VNGUWSPH7E.</summary>
    private const string ReportedPhrase = "♤雌畜人妖♤";

    private static void AssertEveryGlyphDrawable(string text)
    {
        var runs = GlyphFallback.Split(text, Arial, SKFontStyle.Bold);

        Assert.NotEmpty(runs);
        Assert.Equal(text, string.Concat(runs.Select(r => r.Text)));

        foreach (var run in runs)
            foreach (var rune in run.Text.EnumerateRunes())
                Assert.True(run.Face.ContainsGlyph(rune.Value),
                    $"U+{rune.Value:X4} was handed to '{run.Face.FamilyName}', which cannot draw it");
    }

    [Fact]
    public void ReportedPhrase_EveryCodepointGetsAFaceThatCanDrawIt()
        => AssertEveryGlyphDrawable(ReportedPhrase);

    [Theory]
    [InlineData("雌畜人妖")]              // plain Chinese
    [InlineData("GOOD GIRL ♥")]                      // Latin + a suit Arial does have
    [InlineData("♤ 雌畜 ♤")]            // the reported mix, spaced
    [InlineData("好女孩 💖")]        // Chinese + astral-plane emoji
    [InlineData("Послушная")]  // Cyrillic
    public void MixedScripts_EveryCodepointGetsAFaceThatCanDrawIt(string text)
        => AssertEveryGlyphDrawable(text);

    [Fact]
    public void AsciiText_StaysOnThePrimaryFaceInASingleRun()
    {
        var runs = GlyphFallback.Split("GOOD GIRL", Arial, SKFontStyle.Bold);
        Assert.Single(runs);
        Assert.Same(Arial, runs[0].Face);
        Assert.Equal("GOOD GIRL", runs[0].Text);
    }

    [Fact]
    public void LatinReturnsToThePrimaryAfterAFallbackRun()
    {
        // The fallback must not swallow the rest of the line: Arial text on both sides of a Han
        // island keeps Arial, or every localized phrase with a stray ideograph would restyle.
        var runs = GlyphFallback.Split("GOOD 雌 GIRL", Arial, SKFontStyle.Bold);
        Assert.Equal(3, runs.Length);
        Assert.Same(Arial, runs[0].Face);
        Assert.NotSame(Arial, runs[1].Face);
        Assert.Same(Arial, runs[2].Face);
    }

    [Fact]
    public void ContiguousHanStaysInOneRun()
    {
        // Regression guard on cost, not correctness: resolving per character would turn one
        // DrawText into N on the render tick (and the subliminal draws the line 9 times).
        var runs = GlyphFallback.Split("雌畜人妖", Arial, SKFontStyle.Bold);
        Assert.Single(runs);
    }

    [Fact]
    public void EmptyText_YieldsNoRuns()
    {
        Assert.Empty(GlyphFallback.Split(null, Arial, SKFontStyle.Bold));
        Assert.Empty(GlyphFallback.Split("", Arial, SKFontStyle.Bold));
    }

    [Fact]
    public void LoneSurrogate_IsCarriedRatherThanDropped()
    {
        // Invalid text nothing can draw - but silently editing a user's phrase list is worse.
        const string broken = "A\uD83DB";
        var runs = GlyphFallback.Split(broken, Arial, SKFontStyle.Bold);
        Assert.Equal(broken, string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void EmojiSequences_AreNotCutInHalf()
    {
        // U+2764 U+FE0F (heart + variation selector) and a ZWJ sequence. The font manager answers
        // U+FE0F with nothing and U+200D with Segoe UI, so resolving those independently would
        // shatter the cluster and shape it as broken pieces.
        Assert.Single(GlyphFallback.Split("❤️", Arial, SKFontStyle.Bold));
        Assert.Single(GlyphFallback.Split("\U0001F469‍\U0001F467", Arial, SKFontStyle.Bold));
    }

    [Fact]
    public void HanIsResolvedOnItsOwnMerits_NotInheritedFromALeadingDingbat()
    {
        // The #717 quality half: "♤雌..." used to drag the whole line into whichever family won
        // U+2664 — on a stock Windows that is a JAPANESE face, so a zh-CN user's Chinese came out
        // in Japanese glyph forms. The Han must resolve to the same family the font manager picks
        // for it directly under the app's language.
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("zh-CN");
            var runs = GlyphFallback.Split(ReportedPhrase, Arial, SKFontStyle.Bold);

            var hanRun = runs.First(r => r.Text.Contains('雌'));
            var direct = SKFontManager.Default.MatchCharacter(
                null, SKFontStyle.Bold, new[] { "zh", "zh-CN" }, '雌');

            Assert.NotNull(direct);
            Assert.Equal(direct!.FamilyName, hanRun.Face.FamilyName);
        }
        finally { loc.SetLanguage(previous); }
    }

    [Fact]
    public void SwitchingLanguage_DropsTheMemoisedRuns()
    {
        // Han unification: the same codepoint wants a different face per language, and the split
        // is cached by (primary, style, string) — which does NOT include the language.
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("zh-CN");
            GlyphFallback.Split("雌畜人妖", Arial, SKFontStyle.Bold);   // seed the cache

            loc.SetLanguage("ko");
            var after = GlyphFallback.Split("雌畜人妖", Arial, SKFontStyle.Bold);
            // "ko" has no region, so the manager gets the single tag the helper builds for it.
            var direct = SKFontManager.Default.MatchCharacter(
                null, SKFontStyle.Bold, new[] { "ko" }, '雌');

            Assert.NotNull(direct);
            Assert.Equal(direct!.FamilyName, after[0].Face.FamilyName);
        }
        finally { loc.SetLanguage(previous); }
    }

    [Fact]
    public void MeasureSumsTheRunsAndTakesTheWidestVerticalExtents()
    {
        var runs = GlyphFallback.Split(ReportedPhrase, Arial, SKFontStyle.Bold);
        using var paint = new SKPaint { TextSize = 120f, Typeface = Arial };

        var widths = new float[runs.Length];
        var total = GlyphFallback.Measure(runs, paint, widths, out var ascent, out var descent);

        Assert.True(total > 0, "the phrase must have a positive advance width");
        Assert.Equal(widths.Sum(), total, 2);
        Assert.True(ascent < 0, "ascent is measured up from the baseline");
        Assert.True(descent > 0, "descent is measured down from the baseline");
    }
}
