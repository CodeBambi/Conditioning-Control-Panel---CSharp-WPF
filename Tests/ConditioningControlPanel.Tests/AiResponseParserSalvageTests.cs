using ConditioningControlPanel.Services.AIService;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The effects envelope ({"response": ..., "effects": [...]}) gets guillotined by the
/// output token cap - almost always mid-string, often before ANY closing brace exists
/// (beebee, 2026-08-07). The parser used to give up on that shape and hand the raw JSON
/// straight to the speech bubble. These tests pin the salvage ladder: repair when the
/// structure is recoverable, lift just the "response" string when it is not, and never
/// let anything brace-shaped reach the user.
/// </summary>
public class AiResponseParserSalvageTests
{
    private const string Fallback = "~fallback~";

    private static AiResponseParser NewParser() => new(() => Fallback);

    // ── the exact reported shape: truncation before any closing brace ───────────────────
    [Fact]
    public void TruncatedEnvelopeWithNoClosingBraceRecoversResponseText()
    {
        var raw = "{ \"response\": \"Pathetic~ But I'll allow one more chance if you eat it right now and thank me for the privilege.\", \"effects\": [ { \"command\": \"subliminal\", \"data\": { \"Text\": \"Delicious";
        var parsed = NewParser().Parse(raw);
        Assert.StartsWith("Pathetic~", parsed.CleanText);
        Assert.DoesNotContain("{", parsed.CleanText);
        Assert.DoesNotContain("\"response\"", parsed.CleanText);
    }

    // ── truncation inside the response string itself ────────────────────────────────────
    [Fact]
    public void TruncationInsideResponseStringStillYieldsProse()
    {
        var raw = "{ \"response\": \"Good girl~ you know exactly what happens ne";
        var parsed = NewParser().Parse(raw);
        Assert.StartsWith("Good girl~", parsed.CleanText);
        Assert.DoesNotContain("{", parsed.CleanText);
    }

    // ── truncation after a complete effect: response AND effect both survive ────────────
    [Fact]
    public void TruncationAfterCompleteEffectKeepsEffect()
    {
        var raw = "{ \"response\": \"Watch closely.\", \"effects\": [ { \"command\": \"subliminal\", \"data\": { \"Text\": \"obey\" } }, { \"command\": \"flash\", \"data\": { \"Cou";
        var parsed = NewParser().Parse(raw);
        Assert.Equal("Watch closely.", parsed.CleanText);
        Assert.True(parsed.Commands.Count >= 1);
    }

    // ── escaped newlines inside the lifted string are unescaped ─────────────────────────
    [Fact]
    public void LiftedResponseUnescapesNewlines()
    {
        var raw = "{ \"response\": \"line one\\n\\nline two\", \"effects\": [ { \"command\": \"subliminal\", \"data\": { \"Text\": \"cut";
        var parsed = NewParser().Parse(raw);
        Assert.Contains("line one", parsed.CleanText);
        Assert.Contains("line two", parsed.CleanText);
        Assert.DoesNotContain("\\n", parsed.CleanText);
    }

    // ── unliftable envelope (cut before the response value) falls back, never leaks ─────
    [Fact]
    public void EnvelopeCutBeforeResponseValueFallsBack()
    {
        var raw = "{ \"response\": ";
        var parsed = NewParser().Parse(raw);
        Assert.DoesNotContain("{", parsed.CleanText);
        Assert.DoesNotContain("\"response\"", parsed.CleanText);
    }

    // ── regression pins: intact inputs keep working ─────────────────────────────────────
    [Fact]
    public void IntactEnvelopeStillParsesNormally()
    {
        var raw = "{ \"response\": \"Hello there.\", \"effects\": [ { \"command\": \"subliminal\", \"data\": { \"Text\": \"drip\" } } ] }";
        var parsed = NewParser().Parse(raw);
        Assert.Equal("Hello there.", parsed.CleanText);
        Assert.Single(parsed.Commands);
    }

    [Fact]
    public void PlainProseIsUntouched()
    {
        var parsed = NewParser().Parse("Just a normal sentence, no JSON at all.");
        Assert.Equal("Just a normal sentence, no JSON at all.", parsed.CleanText);
        Assert.Empty(parsed.Commands);
    }

    [Fact]
    public void ProseContainingABraceIsNotMangledIntoFallback()
    {
        var parsed = NewParser().Parse("I rate that a 10/10 {giggle} honestly~");
        Assert.Contains("10/10", parsed.CleanText);
        Assert.NotEqual(Fallback, parsed.CleanText);
    }

    [Fact]
    public void CodeFencedIntactJsonStillParses()
    {
        var raw = "```json\n{ \"response\": \"Fenced.\", \"effects\": [] }\n```";
        var parsed = NewParser().Parse(raw);
        Assert.Equal("Fenced.", parsed.CleanText);
    }
}
