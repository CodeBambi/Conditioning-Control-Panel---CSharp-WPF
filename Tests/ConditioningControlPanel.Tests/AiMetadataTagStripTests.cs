using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// OpenRouter provider drift had the cloud model fabricating "[Category: ...]" style metadata in
/// ordinary replies. Both sanitizers stripped those tags only when the closing bracket was present -
/// but replies are hard-capped at 100 tokens, so the cap regularly cut the tag in half and the
/// fragment ("[Satisf: ...") rendered raw in the speech bubble.
///
/// The strip has to stay narrow: eating a stage direction ("[giggles]") or a citation would turn a
/// rendering bug into a content one.
/// </summary>
public class AiMetadataTagStripTests
{
    // ── closed tags: existing behavior, pinned ──────────────────────────────────────────────
    [Theory]
    [InlineData("[Category: Media] good girl~", "good girl~")]
    [InlineData("good girl~ [Category: Media | App: VLC | Duration: 12m]", "good girl~")]
    [InlineData("[Media/Streaming] enjoying the show?", "enjoying the show?")]
    [InlineData("[App: Chrome] browsing again?", "browsing again?")]
    [InlineData("[Title: something] hi", "hi")]
    [InlineData("[Context: idle] hi", "hi")]
    [InlineData("[CATEGORY: media] hi", "hi")]
    public void ClosedMetadataTagsAreStripped(string input, string expected)
        => Assert.Equal(expected, AiTextHygiene.StripMetadataTags(input));

    // ── unclosed tags: the truncation the 100-token cap produces ────────────────────────────
    [Theory]
    [InlineData("good girl~ [Category: Med", "good girl~")]
    [InlineData("good girl~ [Satisf: hig", "good girl~")]
    [InlineData("good girl~ [App: Chro", "good girl~")]
    [InlineData("good girl~ [Duration:", "good girl~")]
    [InlineData("good girl~ [Category", "good girl~")]
    [InlineData("good girl~ [Mood: playful", "good girl~")]
    public void UnclosedMetadataTagAtEndIsStripped(string input, string expected)
        => Assert.Equal(expected, AiTextHygiene.StripMetadataTags(input));

    [Fact]
    public void UnclosedTagAfterAClosedOneIsAlsoStripped()
        => Assert.Equal("good girl~",
            AiTextHygiene.StripMetadataTags("[Media/Streaming] good girl~ [Category: Gam"));

    // ── things that must survive ────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("[giggles] good girl~")]                    // closed stage direction, no colon
    [InlineData("that's the [3] one you liked")]            // mid-sentence citation
    [InlineData("mmm [giggles] you're doing so well [3]")]
    [InlineData("wait for it [giggles")]                    // unclosed, but no colon = prose
    [InlineData("count to [3")]
    [InlineData("the [cat is out")]                         // shares a prefix with "Category" - still prose
    [InlineData("[Apple pie is the best")]                  // "App" needs \b - "Apple" is prose
    [InlineData("good girl~")]
    [InlineData("look at the time: 3:00 already")]
    public void LegitimateBracketsAndProseAreUntouched(string input)
        => Assert.Equal(input, AiTextHygiene.StripMetadataTags(input));

    [Fact]
    public void AMidReplyBracketInAMultiLineAnswerDoesNotEatTheRest()
        // The blank line collapses (pre-existing whitespace pass) but every word survives -
        // the old [^\]]* patterns crossed the newline and deleted everything after "Rule".
        => Assert.Equal("Rule [one: stay still Now breathe deep for me, good girl",
            AiTextHygiene.StripMetadataTags("Rule [one: stay still\n\nNow breathe deep for me, good girl"));

    [Fact]
    public void ATruncatedTagOnTheLastLineOfAMultiLineAnswerIsStillStripped()
        => Assert.Equal("So deep now~",
            AiTextHygiene.StripMetadataTags("So deep now~\n[Category: Gam"));

    // ── whitespace + all-metadata ───────────────────────────────────────────────────────────
    [Fact]
    public void WhitespaceLeftByARemovedTagIsCollapsed()
        => Assert.Equal("so good", AiTextHygiene.StripMetadataTags("so [Category: Media] good"));

    [Theory]
    [InlineData("[Category: Media]")]
    [InlineData("[Category: Med")]
    [InlineData("[Media/Streaming]")]
    [InlineData("")]
    public void AnAllMetadataReplyStripsToNothing(string input)
        => Assert.Equal("", AiTextHygiene.StripMetadataTags(input));

    // ── parser path: same strip, plus its fallback when nothing survives ────────────────────
    [Theory]
    [InlineData("hey cutie [Category: Med", "hey cutie")]
    [InlineData("hey cutie [Category: Media]", "hey cutie")]
    [InlineData("[giggles] hey cutie", "[giggles] hey cutie")]
    public void ParserSanitizesPlainTextReplies(string reply, string expected)
    {
        var parser = new AiResponseParser(() => "FALLBACK");
        Assert.Equal(expected, parser.Parse(reply).CleanText);
    }

    [Fact]
    public void ParserFallsBackWhenTheReplyIsAllMetadata()
    {
        var parser = new AiResponseParser(() => "FALLBACK");
        Assert.Equal("FALLBACK", parser.Parse("[Category: Med").CleanText);
    }

    [Fact]
    public void ParserSanitizesTheResponseFieldOfJsonReplies()
    {
        var parser = new AiResponseParser(() => "FALLBACK");
        Assert.Equal("good girl~", parser.Parse("{\"response\": \"good girl~ [Category: Media]\"}").CleanText);
    }
}
