using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #739 — "companion randomly spits out gibberish text instead of an actual message".
///
/// Two cleanup steps existed but reached only one of the three provider paths: the tokenizer-artifact
/// strip was private to OpenAiCompatibleService, and nothing anywhere removed reasoning blocks. The
/// cloud path the reporter was on ran neither. This helper is now shared by all three, so it needs to
/// be right - and it must not eat ordinary text, which would turn a rendering bug into a content one.
/// </summary>
public class AiTextHygieneTests
{
    [Theory]
    [InlineData("<think>the user wants praise</think>good girl~", "good girl~")]
    [InlineData("<thinking>hmm</thinking>hello", "hello")]
    [InlineData("<reasoning>step 1\nstep 2</reasoning>done", "done")]
    [InlineData("<THINK>upper</THINK>ok", "ok")]
    public void ReasoningBlocksAreRemoved(string input, string expected)
        => Assert.Equal(expected, AiTextHygiene.Clean(input));

    [Fact]
    public void MultiLineReasoningIsRemoved()
    {
        var input = "<think>\nline one\nline two\n</think>\nthe actual reply";
        Assert.Equal("the actual reply", AiTextHygiene.Clean(input));
    }

    [Fact]
    public void UnclosedReasoningBlockDoesNotLeakTheWholeScratchpad()
    {
        // A reply truncated mid-thought. Without the open-ended match the entire scratchpad rendered.
        Assert.Equal("", AiTextHygiene.Clean("<think>I should say something flirty and then"));
    }

    [Fact]
    public void OrphanClosingTagIsRemoved()
        => Assert.Equal("hello", AiTextHygiene.Clean("hello</think>"));

    [Fact]
    public void TokenizerArtifactsBecomeWhitespace()
    {
        Assert.Equal("good girl", AiTextHygiene.Clean("goodĠgirl"));
        Assert.Equal("one\ntwo", AiTextHygiene.Clean("oneĊtwo"));
    }

    [Theory]
    [InlineData("just a normal sentence")]
    [InlineData("niiice~ loooove it, shhh")]
    [InlineData("2 < 3 and 5 > 4")]
    [InlineData("use the <b>bold</b> tag")]
    [InlineData("emoji stay 💕 and accents café")]
    [InlineData("中文也要保留")]
    public void OrdinaryTextIsUntouched(string input)
        => Assert.Equal(input, AiTextHygiene.Clean(input));

    [Fact]
    public void ReasoningStripLeavesLaterAngleBracketsAlone()
        => Assert.Equal("a < b", AiTextHygiene.Clean("<think>compare</think>a < b"));

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void EmptyInputIsSafe(string? input, string expected)
        => Assert.Equal(expected, AiTextHygiene.Clean(input));
}
