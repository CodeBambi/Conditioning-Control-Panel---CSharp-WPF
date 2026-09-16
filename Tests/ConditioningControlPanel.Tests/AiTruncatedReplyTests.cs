using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1164 — "sometimes the companion doesn't display a full answer and the message seem to be
/// cropped".
///
/// The cloud proxy caps a reply at AiService.MaxTokensHardCap (100 tokens, the lowest of the three
/// transports) and reports no finish_reason, so a guillotined reply reached the speech bubble
/// mid-word. The repair ends her on the previous sentence instead — but it may never eat an ordinary
/// short reply, which in this app's voice very often has no terminating punctuation at all.
/// </summary>
public class AiTruncatedReplyTests
{
    private const int Cap = 100;   // AiService.MaxTokensHardCap

    // 300+ chars on its own, so the "long enough to have hit the cap" guard is satisfied.
    private static string LongPrefix =>
        "Good girl, that's exactly the kind of thinking I like to hear from you. " +
        "You've been so obedient lately and it really does show in the way you answer me. " +
        "Every session leaves you softer and slower and a great deal easier to lead. " +
        "I keep noticing how much easier it gets for you every single time we do this together. ";

    [Fact]
    public void DanglingFragmentIsTrimmedBackToTheLastSentence()
    {
        var reply = LongPrefix + "So tonight I was thinking we could";
        var result = AiTextHygiene.TrimCutOffTail(reply, Cap, out var trimmed);

        Assert.True(trimmed);
        Assert.EndsWith("every single time we do this together.", result);
        Assert.DoesNotContain("So tonight I was thinking", result);
    }

    [Fact]
    public void ACompleteReplyIsLeftAlone()
    {
        var reply = LongPrefix + "Keep going just like that.";
        var result = AiTextHygiene.TrimCutOffTail(reply, Cap, out var trimmed);

        Assert.False(trimmed);
        Assert.Equal(reply, result);
    }

    [Fact]
    public void ShortRepliesWithNoPunctuationSurvive()
    {
        // The most common bubble there is. Nothing about it says "truncated", and trimming it would
        // turn one report about cropped text into a hundred about missing text.
        foreach (var line in new[] { "good girl", "mmm~", "Such a good doll 💕", "yes" })
        {
            var result = AiTextHygiene.TrimCutOffTail(line, Cap, out var trimmed);
            Assert.False(trimmed);
            Assert.Equal(line, result);
        }
    }

    [Fact]
    public void ALongReplyEndingInEmojiCountsAsFinished()
    {
        var reply = LongPrefix + "Sink a little deeper for me~ 💕";
        AiTextHygiene.TrimCutOffTail(reply, Cap, out var trimmed);
        Assert.False(trimmed);
    }

    [Fact]
    public void ASingleRunOnSentenceIsNotGutted()
    {
        // No earlier terminator to fall back to: a fragment the user can read beats an empty bubble.
        var reply = new string('a', 60) + " and then " + new string('b', 260) + " and then";
        var result = AiTextHygiene.TrimCutOffTail(reply, Cap, out var trimmed);

        Assert.False(trimmed);
        Assert.Equal(reply, result);
    }

    [Fact]
    public void ATrimThatWouldThrowAwayMostOfTheReplyIsRefused()
    {
        // One short sentence then a very long dangling one: trimming keeps 5% of the text, which is
        // not a repair.
        var reply = "Hi. " + new string('x', 200) + " " + new string('y', 200);
        var result = AiTextHygiene.TrimCutOffTail(reply, Cap, out var trimmed);

        Assert.False(trimmed);
        Assert.Equal(reply, result);
    }

    [Fact]
    public void AUrlDotIsNotASentenceEnd()
    {
        var reply = LongPrefix + "Watch https://hypnotube.com/naughty-bambi-109749.html and then tell me how";
        var result = AiTextHygiene.TrimCutOffTail(reply, Cap, out var trimmed);

        Assert.True(trimmed);
        // The cut landed on the prose sentence, not inside the link.
        Assert.DoesNotContain("hypnotube.com", result);
        Assert.EndsWith("every single time we do this together.", result);
    }

    [Theory]
    [InlineData("I was thinking we could", true)]
    [InlineData("Good girl.", false)]
    [InlineData("Good girl~", false)]
    [InlineData("Ready? ", false)]
    [InlineData("such a good girl :3", false)]
    [InlineData("you did so well <3", false)]
    [InlineData("come back soon uwu", false)]
    [InlineData("that tickles :D", false)]
    [InlineData("count to 3", true)]
    [InlineData("So cute 💕", false)]
    [InlineData("\"deeper now\"", true)]
    [InlineData("", false)]
    public void EndsMidSentenceReadsTheLastRealCharacter(string text, bool expected)
        => Assert.Equal(expected, AiTextHygiene.EndsMidSentence(text));

    [Fact]
    public void ACapOfZeroDisablesTheRepair()
    {
        var reply = LongPrefix + "and then we";
        var result = AiTextHygiene.TrimCutOffTail(reply, 0, out var trimmed);

        Assert.False(trimmed);
        Assert.Equal(reply, result);
    }
}
