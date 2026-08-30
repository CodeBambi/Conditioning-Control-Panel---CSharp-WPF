using System.Linq;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The book's inline-emphasis parser. The card copy is authored by hand in a plain string, so the
/// failure that matters here is not "the emphasis landed on the wrong word" but "the line vanished":
/// a fumbled asterisk must degrade to prose, never to a blank row on the panel.
/// </summary>
public class EmiBookTextTests
{
    [Fact]
    public void A_line_with_no_markup_is_one_plain_run()
    {
        var runs = EmiBookText.Parse("they land on the monitor you pick");
        Assert.Single(runs);
        Assert.False(runs[0].Hot);
        Assert.Equal("they land on the monitor you pick", runs[0].Text);
    }

    [Fact]
    public void A_pair_of_asterisks_marks_one_hot_run()
    {
        var runs = EmiBookText.Parse("pick the *speed* and the *size*");
        Assert.Equal(new[] { "pick the ", "speed", " and the ", "size" }, runs.Select(r => r.Text));
        Assert.Equal(new[] { false, true, false, true }, runs.Select(r => r.Hot));
    }

    [Fact]
    public void Emphasis_at_the_very_start_and_end_still_parses()
    {
        var runs = EmiBookText.Parse("*one key* stops *everything*");
        Assert.Equal(new[] { "one key", " stops ", "everything" }, runs.Select(r => r.Text));
        Assert.Equal(new[] { true, false, true }, runs.Select(r => r.Hot));
    }

    // The whole point of the parser: markup mistakes cost a star, never a line.
    [Theory]
    [InlineData("an *unclosed run", "an *unclosed run")]
    [InlineData("*", "*")]
    [InlineData("nothing to see here", "nothing to see here")]
    [InlineData("an empty ** pair", "an empty  pair")]
    [InlineData("***", "*")]
    public void Fumbled_markup_never_loses_the_words(string input, string expected)
    {
        Assert.Equal(expected, EmiBookText.Strip(input));
        Assert.NotEmpty(EmiBookText.Parse(input));
    }

    [Fact]
    public void Strip_removes_paired_markup_and_nothing_else()
    {
        Assert.Equal("flash your gifs, from a folder",
                     EmiBookText.Strip("flash your *gifs*, from a *folder*"));
    }

    [Fact]
    public void Null_and_empty_are_safe()
    {
        Assert.Empty(EmiBookText.Parse(null));
        Assert.Empty(EmiBookText.Parse(""));
        Assert.Equal(string.Empty, EmiBookText.Strip(null));
    }

    [Fact]
    public void Adjacent_runs_of_the_same_weight_are_merged()
    {
        // "**" contributes nothing, so the two plain halves must arrive as ONE run rather than as
        // two the text engine lays out separately.
        var runs = EmiBookText.Parse("size and**speed");
        Assert.Single(runs);
        Assert.Equal("size andspeed", runs[0].Text);
    }

    [Fact]
    public void Every_card_line_in_the_deck_survives_a_round_trip()
    {
        foreach (var card in EmiBookCards.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(EmiBookText.Strip(card.GistEn)));
            Assert.False(string.IsNullOrWhiteSpace(EmiBookText.Strip(card.CatchEn)));
            foreach (var n in card.NudgesEn)
                Assert.False(string.IsNullOrWhiteSpace(EmiBookText.Strip(n)));
        }
    }

    [Fact]
    public void No_card_carries_an_unpaired_asterisk()
    {
        // An unpaired star renders literally, which on a finished card is a typo somebody has to
        // spot by eye. Cheaper to fail here.
        foreach (var card in EmiBookCards.All)
        {
            foreach (var line in card.NudgesEn.Append(card.GistEn).Append(card.CatchEn))
            {
                Assert.True(line.Count(c => c == '*') % 2 == 0,
                            $"{card.Id} has an unpaired asterisk in: {line}");
            }
        }
    }
}
