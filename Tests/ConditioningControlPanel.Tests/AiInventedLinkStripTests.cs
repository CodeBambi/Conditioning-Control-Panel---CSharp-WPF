using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Live play-test 2026-08-06: asked "any video to train on?" on a mod with no configured links, she
/// answered with invented YouTube URLs — plausible-looking, unvetted, and one of them not even a
/// valid video id ("9d7WKQwz7qCcM" is 13 chars; a YouTube id is 11). With Train 1's multi-turn
/// window her own fabricated link then sat in context, so "give me another one" copied the pattern
/// and drifted further off-brand.
///
/// <para>The rule: a link the app issued survives; a link she wrote from memory takes its whole
/// sentence with it. Sentence-level because "Click here:" left dangling reads worse than a clean
/// cut, and because she frequently glues the next word onto the URL.</para>
/// </summary>
public class AiInventedLinkStripTests
{
    // Stands in for the real catalogue; "did we give her this link?" and nothing more.
    private static bool OnlyOurs(string url) =>
        url == "https://hypnotube.com/video/naughty-bambi-109749.html";

    [Fact]
    public void AnInventedLinkTakesItsSentenceWithIt()
    {
        var reply = "Of course! How about \"Bambi TikTok Mix\"? It's a fun one! " +
                    "Click here: https://youtu.be/9d7WKQwz7qCcM Let's get you hypnotized. 💕";

        var cleaned = AiTextHygiene.StripUnsanctionedLinks(reply, OnlyOurs);

        Assert.DoesNotContain("youtu.be", cleaned);
        Assert.DoesNotContain("Click here", cleaned);
        Assert.Contains("Bambi TikTok Mix", cleaned);   // the naming survives; only the link goes
        Assert.Contains("💕", cleaned);
    }

    [Fact]
    public void AGluedFollowingWordGoesWithTheSentenceRatherThanBeingHalfEaten()
    {
        // The exact second live shape: no space between the URL and "Let's". No URL-shaped pattern
        // can split that correctly, which is precisely why the whole sentence is dropped.
        var reply = "Sure! It's got catchy tunes. Click here: https://youtu.be/6VwZJ7UfK60cLet's " +
                    "get moving and grooving together! 💃";

        var cleaned = AiTextHygiene.StripUnsanctionedLinks(reply, OnlyOurs);

        // Trailing decoration after the terminator is its own fragment and carries no link, so it
        // survives — keeping a stray emoji beats eating a sentence we had no reason to drop.
        Assert.Equal("Sure! It's got catchy tunes. 💃", cleaned);
        Assert.DoesNotContain("grooving", cleaned);     // the glued remainder left with its sentence
        Assert.DoesNotContain("youtu.be", cleaned);
    }

    [Fact]
    public void ALinkTheAppIssuedIsLeftAlone()
    {
        var reply = "Watch https://hypnotube.com/video/naughty-bambi-109749.html for me~";
        Assert.Equal(reply, AiTextHygiene.StripUnsanctionedLinks(reply, OnlyOurs));
    }

    [Fact]
    public void OurLinkSurvivesEvenWithTheSentencePeriodStuckToIt()
    {
        var reply = "Go watch https://hypnotube.com/video/naughty-bambi-109749.html. You'll love it.";
        Assert.Equal(reply, AiTextHygiene.StripUnsanctionedLinks(reply, OnlyOurs));
    }

    [Fact]
    public void OneStraySentenceDoesNotTakeTheGoodOneWithIt()
    {
        var reply = "Try https://hypnotube.com/video/naughty-bambi-109749.html first. " +
                    "Or this one: https://youtu.be/aaaaaaaaaaa instead!";

        var cleaned = AiTextHygiene.StripUnsanctionedLinks(reply, OnlyOurs);

        Assert.Contains("hypnotube.com", cleaned);
        Assert.DoesNotContain("youtu.be", cleaned);
    }

    [Fact]
    public void AReplyThatWasNothingButAnInventedLinkStripsToEmpty()
    {
        // The caller treats empty as "no reply" and falls back to a canned line, exactly as it does
        // for an all-sigil reply.
        Assert.Equal("", AiTextHygiene.StripUnsanctionedLinks("https://youtu.be/9d7WKQwz7qCcM", OnlyOurs));
    }

    [Theory]
    [InlineData("good girl~ no links here at all")]
    [InlineData("")]
    [InlineData("I love the http protocol joke")]      // contains "http" but no URL
    public void OrdinaryRepliesPassThroughUntouched(string reply)
        => Assert.Equal(reply, AiTextHygiene.StripUnsanctionedLinks(reply, OnlyOurs));

    [Fact]
    public void ASchemelessAddressIsStillALink()
    {
        var reply = "Check it out. Go to www.youtube.com/watch?v=abcdefghijk now!";
        var cleaned = AiTextHygiene.StripUnsanctionedLinks(reply, OnlyOurs);

        Assert.Equal("Check it out.", cleaned);
    }

    [Fact]
    public void TheStripSurvivesAMultiLineReply()
    {
        var reply = "First line is fine.\nhttps://youtu.be/aaaaaaaaaaa\nThird line is fine too.";
        var cleaned = AiTextHygiene.StripUnsanctionedLinks(reply, OnlyOurs);

        Assert.Contains("First line is fine.", cleaned);
        Assert.Contains("Third line is fine too.", cleaned);
        Assert.DoesNotContain("youtu.be", cleaned);
    }

    [Fact]
    public void TheLinkFloorRuleIsPartOfEveryPromptNotOneBranch()
    {
        // The bug was structural: the "never write a URL" rule lived only in the branch that shipped
        // a video list, so the mod with nothing real to offer was the one under no prohibition.
        var rule = ConditioningControlPanel.Services.BambiSprite.LinkFloorRule;

        Assert.Contains("NEVER write a URL", rule);
        Assert.Contains("NAME it in words only", rule);
    }
}
