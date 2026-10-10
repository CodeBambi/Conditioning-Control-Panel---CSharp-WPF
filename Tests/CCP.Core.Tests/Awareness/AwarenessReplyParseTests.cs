using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The awareness response contract, as the arbiter honours it: one line, an optional
/// <c>CALLBACK:</c> variant for the stale-delivery case, and <c>[PASS]</c> for "I have nothing good".
///
/// <para><b>One parser.</b> <see cref="AwarenessReply.Parse"/> delegates to
/// <c>AwarenessReactionService.Parse</c> — the implementation the production path already runs. It
/// used to be a second implementation of the same contract that expected <c>ALT:</c> while the
/// shipped prompt teaches <c>CALLBACK:</c>; these tests exist to keep the delegation honest, because
/// the failure mode of a divergent parser is silence, not an exception.</para>
/// </summary>
public class AwarenessReplyParseTests
{
    [Theory]
    [InlineData("[PASS]")]
    [InlineData("  [pass]  ")]
    [InlineData("\"[PASS]\"")]
    [InlineData("[Pass].")]
    [InlineData("PASS")]
    public void TheSilenceTokenIsHonouredHoweverTheModelDressesItUp(string raw)
    {
        var reply = AwarenessReply.Parse(raw);

        Assert.True(reply.IsPass);
        Assert.False(reply.HasLine);
    }

    [Fact]
    public void APlainLineParsesAsTheLine()
    {
        var reply = AwarenessReply.Parse("  fourth time on that site today~  ");

        Assert.False(reply.IsPass);
        Assert.Equal("fourth time on that site today~", reply.Line);
        Assert.False(reply.HasAlternate);
    }

    /// <summary>The keyword the prompt actually teaches. If this fails, the two dialects are back.</summary>
    [Fact]
    public void TheCallbackLineIsSplitOutAndNotSpokenByDefault()
    {
        var reply = AwarenessReply.Parse("still scrolling?\nCALLBACK: I saw you on there a minute ago~");

        Assert.Equal("still scrolling?", reply.Line);
        Assert.Equal("I saw you on there a minute ago~", reply.Alternate);
    }

    [Fact]
    public void TheCallbackMayComeFirst()
    {
        var reply = AwarenessReply.Parse("callback: caught you earlier~\nstill scrolling?");

        Assert.Equal("still scrolling?", reply.Line);
        Assert.Equal("caught you earlier~", reply.Alternate);
    }

    /// <summary>
    /// The prefix the DEAD parser used must not be treated as a marker any more, or a model that
    /// happened to emit it would have its callback spoken as the line.
    /// </summary>
    [Fact]
    public void TheRetiredAltPrefixIsNoLongerAContractKeyword()
    {
        var reply = AwarenessReply.Parse("still scrolling?\nALT: caught you earlier~");

        Assert.Equal("still scrolling?", reply.Line);
        Assert.False(reply.HasAlternate);
    }

    [Fact]
    public void NothingUsableParsesAsEmptySoTheArbiterFallsBackToABark()
    {
        Assert.False(AwarenessReply.Parse(null).HasLine);
        Assert.False(AwarenessReply.Parse("").HasLine);
        Assert.False(AwarenessReply.Parse("   \n  ").HasLine);
        Assert.False(AwarenessReply.Parse(null).IsPass);
    }

    [Fact]
    public void AnAbsurdlyLongLineIsCappedRatherThanPastedIntoTheBubble()
    {
        var reply = AwarenessReply.Parse(new string('a', AwarenessReply.MaxLineLength * 3));

        Assert.NotNull(reply.Line);
        Assert.True(reply.Line!.Length <= AwarenessReply.MaxLineLength);
    }

    /// <summary>The cap and the sentinels are the service's, not a second set of numbers.</summary>
    [Fact]
    public void TheContractConstantsForwardToTheOneParser()
    {
        Assert.Equal(AwarenessReactionService.PassToken, AwarenessReply.PassSentinel);
        Assert.Equal(AwarenessReactionService.CallbackPrefix, AwarenessReply.CallbackPrefix);
        Assert.Equal(AwarenessReactionService.MaxLineLength, AwarenessReply.MaxLineLength);
    }
}
