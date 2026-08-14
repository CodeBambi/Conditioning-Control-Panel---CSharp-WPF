using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Live play-test 2026-08-06: with BarkEcho turns in the window rendered as
/// «Bambi said aloud: "…"», the cloud model imitated the pattern and wrapped its OWN replies in the
/// sigil — every chat bubble arrived as «Bambi said aloud: "Your cat's name is Waffles, isn't it?"».
/// The unwrap has to be narrow: the sigil is machine-made (guillemets + a short speaker + the exact
/// phrase), so prose that merely mentions "said aloud" must pass through untouched.
/// </summary>
public class AiSpokenSigilUnwrapTests
{
    [Theory]
    [InlineData("«Bambi said aloud: \"Your cat's name is Waffles, isn't it?\"»",
                "Your cat's name is Waffles, isn't it?")]
    [InlineData("«DroneOS said aloud: recalibrate for me»", "recalibrate for me")]
    [InlineData("«Bambi said aloud: \"good girl~", "good girl~")]              // cap ate the close
    [InlineData("  «Bambi Said Aloud: \"so deep now\"»  ", "so deep now")]     // case + padding
    public void WrappedRepliesAreUnwrapped(string input, string expected)
        => Assert.Equal(expected, AiTextHygiene.UnwrapSpokenSigil(input));

    [Fact]
    public void OnlyTheOuterQuoteLayerIsShed()
        => Assert.Equal("she said \"hi\" to me",
            AiTextHygiene.UnwrapSpokenSigil("«Bambi said aloud: \"she said \"hi\" to me\"»"));

    [Fact]
    public void ADoubleWrappedReplyUnwrapsFully()
        => Assert.Equal("good girl~",
            AiTextHygiene.UnwrapSpokenSigil("«Bambi said aloud: \"«Bambi said aloud: \"good girl~\"»\"»"));

    [Theory]
    [InlineData("good girl~")]
    [InlineData("you said aloud: yes, I heard it")]                            // no « prefix = prose
    [InlineData("«whisper» is my favorite word")]                              // « but no sigil phrase
    [InlineData("")]
    public void OrdinaryRepliesPassThroughUntouched(string input)
        => Assert.Equal(input, AiTextHygiene.UnwrapSpokenSigil(input));

    [Fact]
    public void ALongClauseBeforeThePhraseIsNotMistakenForASpeaker()
    {
        var text = "«" + new string('x', 120) + " said aloud: \"not a sigil\"»";
        Assert.Equal(text, AiTextHygiene.UnwrapSpokenSigil(text));
    }

    [Fact]
    public void AnAllShellReplyUnwrapsToEmpty()
        => Assert.Equal("", AiTextHygiene.UnwrapSpokenSigil("«Bambi said aloud: \"\"»"));

    // ── trailing debris: the second live shape (orphan close + section-separator hashes) ────
    [Theory]
    [InlineData("I never forget about your adorable kitty, Waffles! She's the cutest, right?\"» ###",
                "I never forget about your adorable kitty, Waffles! She's the cutest, right?")]
    [InlineData("«Bambi said aloud: \"good girl~\"» ###", "good girl~")]
    [InlineData("so deep now ###", "so deep now")]
    [InlineData("she said \"hi\" to me»", "she said \"hi\" to me")]   // balanced quotes survive
    public void TrailingSigilDebrisIsShed(string input, string expected)
        => Assert.Equal(expected, AiTextHygiene.UnwrapSpokenSigil(input));

    [Fact]
    public void AHashtagInsideTheSentenceSurvives()
        => Assert.Equal("you're my #1 fan", AiTextHygiene.UnwrapSpokenSigil("you're my #1 fan"));

    // ── transcript salvage: the third live shape (0813, after a mod switch) ────
    //
    // With stale bark echoes from the PREVIOUS mod still in the window («BambiSprite said
    // aloud: …» next to «DroneOS said aloud: …»), the model answered with a whole multi-speaker
    // transcript. The start-anchored unwrap stripped only the first opener and the rest reached
    // the bubble verbatim — sigils, foreign speaker and all.

    [Fact]
    public void AMultiBlockTranscriptKeepsOnlyTheActiveSpeakersLines()
    {
        var raw =
            "«DroneOS said aloud: \"hihi~ that's my kind of morning~\"»\n" +
            "«BambiSprite said aloud: \"unit reports... seems like a productive day~\"»\n" +
            "«DroneOS said aloud: \"good morning *yawns*... got it. unit reporting in~";

        Assert.Equal(
            "hihi~ that's my kind of morning~ good morning *yawns*... got it. unit reporting in~",
            AiTextHygiene.UnwrapSpokenSigil(raw, "DroneOS"));
    }

    [Fact]
    public void TheMangledShapeTheAnchoredUnwrapLeavesBehindIsAlsoSalvaged()
    {
        // What actually reached the bubble live: the first block's opener already eaten, its
        // orphan close ("») left mid-text, then two intact blocks with a truncated final close.
        var raw =
            "hihi~ that's my kind of morning~\"»\n" +
            "«BambiSprite said aloud: \"unit reports... seems like a productive day~\"»\n" +
            "«DroneOS said aloud: \"good morning *yawns*... got it. unit reporting in~";

        Assert.Equal(
            "hihi~ that's my kind of morning~ good morning *yawns*... got it. unit reporting in~",
            AiTextHygiene.UnwrapSpokenSigil(raw, "DroneOS"));
    }

    [Fact]
    public void WithoutASpeakerEveryBlocksInnerSpeechIsKept()
    {
        var raw =
            "«BambiSprite said aloud: \"unit reports~\"» «DroneOS said aloud: \"reporting in~\"»";

        Assert.Equal("unit reports~ reporting in~", AiTextHygiene.UnwrapSpokenSigil(raw));
    }

    [Fact]
    public void ATranscriptThatIsAllOtherPeoplesLinesSalvagesToEmpty()
    {
        var raw =
            "«BambiSprite said aloud: \"hihi~\"» «BambiSprite said aloud: \"good girl~\"»";

        Assert.Equal("", AiTextHygiene.UnwrapSpokenSigil(raw, "DroneOS"));
    }

    [Fact]
    public void ASingleBlockBuriedMidTextIsUnwrappedInPlace()
        => Assert.Equal("sure! recalibrating~ anyway, ready?",
            AiTextHygiene.UnwrapSpokenSigil(
                "sure! «DroneOS said aloud: \"recalibrating~\"» anyway, ready?", "DroneOS"));

    [Fact]
    public void TheSpeakerFilterToleratesTheModelDecoratingTheName()
        => Assert.Equal("unit online~",
            AiTextHygiene.UnwrapSpokenSigil(
                "«DroneOS v1.0 said aloud: \"unit online~\"» «Bambi said aloud: \"hi~\"»", "DroneOS"));
}
