using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The lock-card answer normaliser (<c>LockCardText</c>).
///
/// <para><b>The trap (ccp-bugs#1169).</b> A card whose phrase carries a typographic ellipsis
/// (U+2026) was unpassable, reported against the Infection Control mod: the user types three full
/// stops, the phrase holds one character no keyboard produces, and an ordinal compare says no
/// forever. Curly quotes and a non-breaking space set the same trap, and they arrive by default
/// from Word, from a mod author's autocorrect and from every AI-written phrase.</para>
///
/// <para>What must NOT change: a WRONG answer is still wrong. Normalising touches punctuation
/// SHAPE and whitespace only, never letters, digits or word order.</para>
/// </summary>
public class LockCardTextTests
{
    private const string Ellipsis = "…";
    private const string LeftSingle = "‘";
    private const string RightSingle = "’";
    private const string LeftDouble = "“";
    private const string RightDouble = "”";
    private const string Nbsp = " ";
    private const string ZeroWidthSpace = "​";
    private const string EmDash = "—";

    [Fact]
    public void Typed_dots_pass_a_phrase_written_with_a_typographic_ellipsis()
    {
        var phrase = "good girls don" + RightSingle + "t think" + Ellipsis;

        Assert.True(LockCardText.Matches("good girls don't think...", phrase));
    }

    [Fact]
    public void Typed_ellipsis_passes_a_phrase_written_with_three_dots()
    {
        Assert.True(LockCardText.Matches("deeper" + Ellipsis, "deeper..."));
    }

    [Theory]
    [InlineData("she said \"obey\"")]
    [InlineData("she said “obey”")]
    public void Curly_and_straight_quotes_are_the_same_answer(string typed)
    {
        Assert.True(LockCardText.Matches(typed, "she said " + LeftDouble + "obey" + RightDouble));
    }

    [Fact]
    public void Curly_apostrophes_fold_to_straight_ones()
    {
        Assert.True(LockCardText.Matches("i can't resist", "i can" + RightSingle + "t resist"));
        Assert.True(LockCardText.Matches("i can" + LeftSingle + "t resist", "i can't resist"));
    }

    [Fact]
    public void Dashes_of_every_width_fold_together()
    {
        Assert.True(LockCardText.Matches("empty - obedient", "empty " + EmDash + " obedient"));
    }

    [Fact]
    public void Non_breaking_and_zero_width_characters_do_not_block_a_match()
    {
        Assert.True(LockCardText.Matches("obey and drop", "obey" + Nbsp + "and" + ZeroWidthSpace + " drop"));
    }

    [Fact]
    public void Case_leading_and_trailing_space_and_double_spaces_are_forgiven()
    {
        Assert.True(LockCardText.Matches("   OBEY   AND    DROP  ", "obey and drop"));
    }

    [Fact]
    public void A_wrong_answer_is_still_wrong()
    {
        Assert.False(LockCardText.Matches("obey and drip", "obey and drop"));
        Assert.False(LockCardText.Matches("drop and obey", "obey and drop"));
        Assert.False(LockCardText.Matches("obey", "obey and drop"));
        Assert.False(LockCardText.Matches("", "obey and drop"));
    }

    [Fact]
    public void Normalize_flattens_punctuation_and_collapses_whitespace()
    {
        Assert.Equal("good girl... i can't stop", LockCardText.Normalize("  good girl" + Ellipsis + "  i can" + RightSingle + "t	stop "));
        Assert.Equal("", LockCardText.Normalize(null));
        Assert.Equal("", LockCardText.Normalize("   "));
    }

    /// <summary>
    /// The mistake counter runs off the prefix check, so it has to speak the same dialect as the
    /// match: half-typed "..." against a one-character ellipsis is progress, not an error.
    /// </summary>
    [Fact]
    public void Prefix_check_tracks_a_partly_typed_answer()
    {
        var phrase = "hush now" + Ellipsis + " sleep";

        Assert.True(LockCardText.IsPrefixOf("", phrase));
        Assert.True(LockCardText.IsPrefixOf("hush", phrase));
        Assert.True(LockCardText.IsPrefixOf("hush now.", phrase));
        Assert.True(LockCardText.IsPrefixOf("hush now...", phrase));
        Assert.True(LockCardText.IsPrefixOf("HUSH NOW... SLE", phrase));
        Assert.False(LockCardText.IsPrefixOf("hush then", phrase));
    }

    /// <summary>
    /// A trailing space mid-typing must not read as a mistake: it is collapsed away, leaving the
    /// prefix exactly as long as the letters the user has actually committed.
    /// </summary>
    [Fact]
    public void Prefix_check_survives_a_trailing_space()
    {
        Assert.True(LockCardText.IsPrefixOf("hush ", "hush now"));
    }
}
