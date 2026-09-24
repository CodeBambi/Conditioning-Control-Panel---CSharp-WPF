using System;
using System.Linq;
using System.Windows.Media;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class ConversationDeliveryTests
{
    [Theory]
    [InlineData(true, false, true, false, false, true)]
    [InlineData(true, true, true, false, false, false)]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(true, false, true, true, false, false)]
    [InlineData(true, false, true, false, true, false)]
    public void SuggestionsUseActualLauncherEligibility(bool available, bool locked, bool revealed,
        bool signedOut, bool audioOnly, bool expected)
    {
        var game = new LauncherEntry("test", "title", "blurb", null, "*", Colors.White,
            () => available, () => locked, () => throw new Exception("must not launch"), () => false, () => revealed);
        Assert.Equal(expected, CompanionActivities.CanOffer(game, signedOut, audioOnly));
    }

    [Fact]
    public void ExpiredTierOrPassIsCheckedAgainAtClick()
    {
        var access = true;
        var opened = 0;
        var activity = new CompanionActivity("game.test", "Test", "A test activity",
            () => access, () => { opened++; return true; });
        Assert.True(activity.Allowed);
        access = false;
        Assert.False(activity.TryOpen());
        Assert.Equal(0, opened);
        access = true;
        Assert.True(activity.TryOpen());
        Assert.Equal(1, opened);
        Assert.False((activity with { CheckAccess = () => throw new Exception() }).Allowed);
    }

    [Fact]
    public void MarkersSurviveProviderCleanersButNeverBecomeProseOrArbitraryActions()
    {
        var offered = new[] { Activity("game.arcademy"), Activity("page.studio"), Activity("page.assets") };
        const string raw = "a round? [[ccp:game.arcademy]] [[ccp:game.arcademy]] [[ccp:unknown]] [[ccp:page.studio]] [[ccp:page.assets]]";
        var cloud = CompanionProxyContract.CleanReply(raw, "stop");
        var local = new AiResponseParser(() => "fallback").Parse(raw).CleanText;
        foreach (var cleaned in new[] { cloud, local })
        {
            var reply = ConversationDelivery.Parse(cleaned, offered);
            Assert.Equal("a round?", reply.Text);
            Assert.Equal(new[] { "game.arcademy", "page.studio" }, reply.Ids);
        }
        Assert.Empty(ConversationDelivery.Parse("[[ccp:unknown]]", offered).Text);
        Assert.Equal("hello.", ConversationDelivery.Parse("hello. [[ccp:page.st", offered).Text);
        Assert.Empty(ConversationDelivery.Parse(raw, Array.Empty<CompanionActivity>()).Ids);
        Assert.Empty(CompanionProxyContract.CleanReply(raw, "length"));
    }

    [Theory]
    [InlineData("hi emi", false, 160)]
    [InlineData("you're cute", false, 160)]
    [InlineData("explain the options", false, 240)]
    [InlineData("tell me more details", false, 240)]
    [InlineData("hi", true, 240)]
    public void BrevityKeepsRoomForRequestedDetailsAndEffects(string input, bool effects, int tokens)
    {
        var options = ConversationDelivery.Options(AiCallOptions.Chat, input, effects);
        Assert.Equal(tokens, options.MaxTokens);
        Assert.True(options.CompanionV2);
        Assert.NotNull(options.RequestId);
    }

    private static CompanionActivity Activity(string id) => new(id, id, "test", () => true,
        () => throw new Exception("a reply must never open an activity"));
}
