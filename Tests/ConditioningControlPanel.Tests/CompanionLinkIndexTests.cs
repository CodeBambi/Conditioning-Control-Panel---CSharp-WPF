using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// She names a title; the app owns the link (2026-08-06 fix). These pin the half that turns her
/// wording into something clickable — the half that must never attach a video to a sentence that
/// never suggested one.
/// </summary>
[Collection("CompanionLinkIndex")]
public class CompanionLinkIndexTests
{
    public CompanionLinkIndexTests() => CompanionLinkIndex.ResetForTests();

    [Fact]
    public void ATitleSheNamedResolvesToTheAppsOwnLink()
    {
        var hit = CompanionLinkIndex.FindMentionedTitle("How about Bambi's Naughty TikTok Collection tonight?~");

        Assert.NotNull(hit);
        Assert.Equal("Bambi's Naughty TikTok Collection", hit!.Value.Title);
        Assert.StartsWith("https://hypnotube.com/", hit.Value.Url);
    }

    [Fact]
    public void QuotesAndPunctuationAroundTheTitleAreNormal()
    {
        var hit = CompanionLinkIndex.FindMentionedTitle("Watch \"Bambi's Naughty TikTok Collection\", good girl.");
        Assert.NotNull(hit);
    }

    [Fact]
    public void TheMatchIsCaseInsensitive()
        => Assert.NotNull(CompanionLinkIndex.FindMentionedTitle("go watch bambi's naughty tiktok collection~"));

    [Fact]
    public void AShortTitleIsNotMatchedInsideOrdinaryProse()
    {
        // "Overload" is a real catalogue entry, but a title that short appears in normal sentences
        // by accident — attaching a video to "sensory overload" would be the model looking broken.
        Assert.Null(CompanionLinkIndex.FindMentionedTitle("that was sensory overload, wasn't it?"));
    }

    [Fact]
    public void ATitleGluedInsideALongerWordIsNotAMention()
        => Assert.Null(CompanionLinkIndex.FindMentionedTitle("xxBambi's Naughty TikTok Collectionxx"));

    [Fact]
    public void NothingNamedMeansNoChip()
    {
        Assert.Null(CompanionLinkIndex.FindMentionedTitle("hi cutie, how was your day?"));
        Assert.Null(CompanionLinkIndex.FindMentionedTitle(""));
        Assert.Null(CompanionLinkIndex.FindMentionedTitle(null));
    }

    [Fact]
    public void TheAppsOwnLinksAreSanctionedAndAnythingElseIsNot()
    {
        var hit = CompanionLinkIndex.FindMentionedTitle("Bambi's Naughty TikTok Collection");
        Assert.NotNull(hit);

        Assert.True(CompanionLinkIndex.IsSanctioned(hit!.Value.Url));
        Assert.False(CompanionLinkIndex.IsSanctioned("https://youtu.be/9d7WKQwz7qCcM"));
        Assert.False(CompanionLinkIndex.IsSanctioned(""));
        Assert.False(CompanionLinkIndex.IsSanctioned(null));
    }

    [Fact]
    public void TrailingSentencePunctuationDoesNotUnsanctionOurOwnLink()
    {
        var hit = CompanionLinkIndex.FindMentionedTitle("Bambi's Naughty TikTok Collection");
        Assert.True(CompanionLinkIndex.IsSanctioned(hit!.Value.Url + "."));
        Assert.True(CompanionLinkIndex.IsSanctioned(hit.Value.Url + "!"));
    }

    [Fact]
    public void TheIndexIsBuiltOnceAndReusedUntilTheCatalogueMoves()
    {
        CompanionLinkIndex.FindMentionedTitle("warm the cache");
        var after = CompanionLinkIndex.BuildCount;

        for (int i = 0; i < 5; i++) CompanionLinkIndex.FindMentionedTitle("nothing named here");

        Assert.Equal(after, CompanionLinkIndex.BuildCount);
    }
}
