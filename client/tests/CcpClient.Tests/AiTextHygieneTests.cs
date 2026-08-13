using CcpClient.Desktop.Ai;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-069 pure-layer facts for the three reply-hygiene rules (WPF
/// `ConditioningControlPanel/Services/AIService/AiTextHygiene.cs` — `Clean` :25/:30/:287,
/// `StripMetadataTags` :36/:40/:44/:53/:61 + collapse/trim :79-80; leak predicate from
/// `AiResponseParser.LooksLikeEnvelopeLeak` :53). Every layer carries a NEGATIVE CONTROL —
/// input that must pass through byte-identical — so no pin can be satisfied by a layer that
/// eats everything. All rules are subtractive: these tests never write a reply fragment,
/// stripped tag, or leaked JSON anywhere but an in-memory assertion.
/// </summary>
public class AiTextHygieneTests
{
    // ---- H1: reasoning blocks (WPF AiTextHygiene.cs:25 — IgnoreCase, Singleline, unterminated-tolerant) ----

    [Theory]
    [InlineData("think")]
    [InlineData("thinking")]
    [InlineData("reasoning")]
    [InlineData("thought")]
    public void H1_ReasoningBlock_EachTagName_RemovedWithContents(string tag)
    {
        // Clean does NOT collapse the whitespace removal leaves behind (WPF :287-297 — Trim only).
        Assert.Equal("hello  world", AiTextHygiene.Clean($"hello <{tag}>secret scratchpad</{tag}> world"));
    }

    [Fact]
    public void H1_ReasoningBlock_CaseInsensitive()
    {
        Assert.Equal("answer", AiTextHygiene.Clean("<THINK>scratch</THINK> answer"));
    }

    [Fact]
    public void H1_UnterminatedBlock_EatsToEndOfString()
    {
        // The (</\1>|$) alternation (WPF :26): a reply truncated mid-thought renders NOTHING of
        // the scratchpad. Chain-of-thought is never user-intended content — fail-closed.
        Assert.Equal("answer", AiTextHygiene.Clean("answer <thinking>cut off mid-thought"));
    }

    [Fact]
    public void H1_OrphanClosingTag_Removed()
    {
        // WPF :30 — a stray closer left over once its opener was trimmed upstream.
        Assert.Equal("hello  world", AiTextHygiene.Clean("hello </reasoning> world"));
    }

    [Fact]
    public void H1_TokenizerArtifact_GSpace_MappedToSpace()
    {
        // WPF :296 — raw GPT-2/llama.cpp tokens reaching the bubble.
        Assert.Equal("hello world", AiTextHygiene.Clean("ĠhelloĠworld"));
    }

    [Fact]
    public void H1_TokenizerArtifact_CNewline_MappedToNewline()
    {
        Assert.Equal("line one\nline two", AiTextHygiene.Clean("line oneĊline two"));
    }

    [Theory]
    [InlineData("a < b and c > d")]
    [InlineData("use { curly } braces freely")]
    [InlineData("[as it happens] brackets are prose too")]
    public void H1_NegativeControl_PlainReplyPassesThroughByteIdentical(string text)
    {
        Assert.Equal(text, AiTextHygiene.Clean(text));
    }

    [Fact]
    public void H1_LossyBoundary_LegitReplyQuotingTheTagVerbatim_IsStripped()
    {
        // Documented subtractive boundary (WPF-identical, lossy by design): a reply that
        // quotes the tag shape verbatim loses that span. Recorded, not hidden.
        Assert.Equal("thoughts on  game", AiTextHygiene.Clean("thoughts on <thinking> as a word</thinking> game"));
    }

    // ---- H2: metadata tags (WPF AiTextHygiene.cs:36,40,44,53,61 — FIVE patterns, FIXED order) ----

    [Theory]
    // 1. ClosedCategoryTag (:36) — the awareness context line echoed back.
    [InlineData("sure! [Category: Media | App: VLC | Title: x | Duration: 12m]", "sure!")]
    // 2. ReactionCategoryTag (:40) — [Media/Streaming] shape.
    [InlineData("nice [Media/Streaming] pick", "nice pick")]
    // 3. ClosedMetadataTag (:44) — any closed known-keyword tag.
    [InlineData("[App: VLC] done", "done")]
    // 4. UnclosedKnownTag (:53) — end-anchored, known keyword, colon optional.
    [InlineData("done [Category foo", "done")]
    // 5. UnclosedKeyedTag (:61) — end-anchored, unknown keyword, colon required.
    [InlineData("done [Mood: playful", "done")]
    public void H2_EachOfTheFiveShapes_Removed(string input, string expected)
    {
        Assert.Equal(expected, AiTextHygiene.StripMetadataTags(input));
    }

    [Fact]
    public void H2_PortTrigger_AwarenessContextLineEcho_Stripped()
    {
        // THE PORT'S OWN TRIGGER: AiAwarenessService.cs:229 sends the model exactly
        //   [Category: {Category} | App: {App} | Title: {title} | Duration: {DurationText}]
        // Local models mirror bracketed metadata back. This fixture is built from that line's
        // shape; H2 must strip the echo and keep the reply.
        Assert.Equal(
            "On it!",
            AiTextHygiene.StripMetadataTags("On it! [Category: Media | App: VLC | Title: Some Video | Duration: 12m]"));
    }

    [Fact]
    public void H2_WhitespaceCollapse_AndTrim()
    {
        // WPF :79-80 — the removal leaves double whitespace; collapsed to one, then trimmed.
        Assert.Equal("hello world", AiTextHygiene.StripMetadataTags("  hello  [App: x]   world  "));
    }

    [Fact]
    public void H2_OrderPin_NestedTruncation_ProvesFixedOrder()
    {
        // record.md §2: "[Category: [Foo]: bar" (a truncated tag nested inside another — the
        // WPF :48-52 comment documents truncation cutting a tag before its bracket).
        //   WPF order (closed passes FIRST): ClosedCategoryTag eats "[Category: [Foo]" → ": bar".
        //   Permuted (unclosed passes first): UnclosedKeyedTag eats "[Foo]: bar" leaving
        //   "[Category: ", which UnclosedKnownTag then eats → "".
        // Pinning ": bar" fails any permutation of the five patterns.
        Assert.Equal(": bar", AiTextHygiene.StripMetadataTags("[Category: [Foo]: bar"));
    }

    [Theory]
    // \b discipline (WPF :52 comment): "[Apple pie" is not "App".
    [InlineData("recipe: [Apple pie", "recipe: [Apple pie")]
    // Stage directions survive — the colon is what marks metadata (WPF :57-60 comment).
    [InlineData("she laughs [giggles", "she laughs [giggles")]
    // Citations survive.
    [InlineData("see page [3", "see page [3")]
    // A CLOSED unknown-keyed tag is not one of the five shapes — WPF leaves it.
    [InlineData("remember [Note: important] that", "remember [Note: important] that")]
    // A bracketed phrase that is not a metadata tag at all.
    [InlineData("well [as it happens] yes", "well [as it happens] yes")]
    public void H2_NegativeControl_NonMetadataBracketsPassThroughByteIdentical(string input, string expected)
    {
        Assert.Equal(expected, AiTextHygiene.StripMetadataTags(input));
    }

    // ---- H3: envelope-leak DETECTION only (WPF AiResponseParser.cs:53-58, verbatim) ----

    [Theory]
    [InlineData("{\"response\":\"hi\",\"commands\":[]}")]
    [InlineData("  \n {\"response\": \"x\"}")] // TrimStart before the '{' test
    [InlineData("{\"response\": \"cut off")] // truncated mid-envelope — still a leak
    public void H3_EnvelopeLeak_Detected(string text)
    {
        Assert.True(AiTextHygiene.LooksLikeEnvelopeLeak(text));
    }

    [Theory]
    [InlineData("use { curly } braces freely")] // a legitimate reply containing '{'
    [InlineData("{\"reply\":\"hi\"}")] // no "response" field
    [InlineData("sure, {\"response\": \"x\"} as json")] // prose first — not an envelope
    [InlineData("she said \"response\" loudly")] // the word without the envelope shape
    [InlineData("")]
    [InlineData(null)]
    public void H3_NegativeControl_NonEnvelopePasses(string? text)
    {
        Assert.False(AiTextHygiene.LooksLikeEnvelopeLeak(text));
    }
}
