using System;
using System.Linq;
using System.Windows.Media;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
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
        const string raw = "a round? <ccp-action>game.arcademy</ccp-action> <ccp-action>game.arcademy</ccp-action> <ccp-action>unknown</ccp-action> <ccp-action>page.studio</ccp-action> <ccp-action>page.assets</ccp-action>";
        var cloud = CompanionProxyContract.CleanReply(raw, "stop");
        var local = new AiResponseParser(() => "fallback").Parse(raw).CleanText;
        foreach (var cleaned in new[] { cloud, local })
        {
            var reply = ConversationDelivery.Parse(cleaned, offered);
            Assert.Equal("a round?", reply.Text);
            Assert.Equal(new[] { "game.arcademy", "page.studio" }, reply.Ids);
        }
        Assert.Empty(ConversationDelivery.Parse("<ccp-action>unknown</ccp-action>", offered).Text);
        Assert.Equal("hello.", ConversationDelivery.Parse("hello. <ccp-action>page.st", offered).Text);
        Assert.Empty(ConversationDelivery.Parse(raw, Array.Empty<CompanionActivity>()).Ids);
        Assert.Empty(CompanionProxyContract.CleanReply(raw, "length"));
    }

    [Theory]
    [InlineData("hi emi", false, 120)]
    [InlineData("you're cute", false, 120)]
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

    [Theory]
    [InlineData("hi emi")]
    [InlineData("you're cute")]
    [InlineData("what do you think about trance?")]
    [InlineData("no games, just chat")]
    [InlineData("stop suggesting activities")]
    public void OrdinaryChatAndDeclinesDoNotExposeCatalog(string input)
    {
        Assert.Empty(ConversationDelivery.Select(new[] { Activity("game.test"), Activity("page.presets") },
            input, Array.Empty<CompanionTurn>()));
    }

    [Fact]
    public void MediaRequestOffersLibraryNotGamesOrAnInventedVideo()
    {
        var offered = ConversationDelivery.Select(new[] { Activity("page.assets"), Activity("game.test") },
            "any video for me?", Array.Empty<CompanionTurn>());
        Assert.Equal("page.assets", Assert.Single(offered).Id);
        var raw = new PromptRequest("voice", new[] { ChatMessage.System("voice"), ChatMessage.User("any video for me?") });
        Assert.Contains("no retrieved video link", ConversationDelivery.Apply(raw, "any video for me?", offered, true).SystemPrompt);
        Assert.Equal("here.", ConversationDelivery.Parse("here. [video link] [ ]", offered).Text);
    }

    [Fact]
    public void RoutineNudgeRequiresEightExchangesAndRespectsRecentDecline()
    {
        var turns = Enumerable.Range(0, 8).SelectMany(_ => new[] {
            CompanionTurn.Create(TurnKind.UserChat, "chat"), CompanionTurn.Create(TurnKind.AssistantChat, "reply") }).ToList();
        var activities = new[] { Activity("page.presets"), Activity("game.test") };
        Assert.Empty(ConversationDelivery.Select(activities, "my routine", turns.Take(14).ToArray()));
        Assert.Equal("page.presets", Assert.Single(ConversationDelivery.Select(activities, "my routine", turns)).Id);
        turns.Add(CompanionTurn.Create(TurnKind.AssistantChat, "try this") with { ActivityIds = new[] { "page.presets" } });
        Assert.Empty(ConversationDelivery.Select(activities, "my routine", turns));
        turns.RemoveAt(turns.Count - 1);
        turns.Add(CompanionTurn.Create(TurnKind.UserChat, "no activity suggestions"));
        Assert.Empty(ConversationDelivery.Select(activities, "my routine", turns));
        Assert.NotEmpty(ConversationDelivery.Select(activities, "suggest a game", turns));
    }

    [Fact]
    public void DeliverySharesOneBoundedSystemMessageWithCharacterAndSafety()
    {
        var voice = "You are EMI. " + new string('x', 18000) + SafetyComposer.Floor;
        var messages = new[] { ChatMessage.System(voice), ChatMessage.System("CURRENT CONTEXT"),
            ChatMessage.Assistant("old reply"), ChatMessage.User("hi emi") };
        var activity = new CompanionActivity("game.test", new string('L', 10000), new string('D', 10000), () => true, () => false);
        var output = ConversationDelivery.Apply(new PromptRequest(voice, messages), "hi emi", Enumerable.Repeat(activity, 20).ToArray(), true);
        var system = Assert.Single(output.Messages.Where(m => m.Role == ChatMessage.RoleSystem));
        Assert.Equal(output.SystemPrompt, system.Content);
        Assert.True(system.Content.Length <= 10000, system.Content.Length.ToString());
        Assert.Contains("You are EMI.", system.Content);
        Assert.Contains("CURRENT CONTEXT", system.Content);
        Assert.Contains("EMI VOICE REFERENCES", system.Content);
        Assert.EndsWith(SafetyComposer.Floor, system.Content);
        Assert.Equal(messages.Skip(2), output.Messages.Skip(1));
    }
    [Fact]
    public void CorrectiveTurnOmitsRejectedAssistantStyleWithoutChangingStoredHistory()
    {
        var raw = new PromptRequest("voice", new[] { ChatMessage.System("voice"), ChatMessage.User("hello"),
            ChatMessage.Assistant("a long promotional game pitch"), ChatMessage.User("no games, just chat") });
        var request = ConversationDelivery.Apply(raw, "no games, just chat", Array.Empty<CompanionActivity>(), true);
        Assert.DoesNotContain(request.Messages, m => m.Role == ChatMessage.RoleAssistant);
        Assert.Equal(2, request.Messages.Count(m => m.Role == ChatMessage.RoleUser));
        Assert.Single(raw.Messages.Where(m => m.Role == ChatMessage.RoleAssistant));
        Assert.Contains(ConversationDelivery.Apply(raw, "no games, just chat", Array.Empty<CompanionActivity>(), false).Messages,
            m => m.Role == ChatMessage.RoleAssistant);
    }

    [Fact]
    public void NamedOfferedDestinationGetsButtonWhenProviderOmitsTag()
    {
        var offered = new[] { Activity("game.test") with { Label = "The Back Room" } };
        Assert.Equal(new[] { "game.test" }, ConversationDelivery.Parse("try the back room.", offered).Ids);
        Assert.Empty(ConversationDelivery.Parse("try a made-up game.", offered).Ids);
        var bareId = ConversationDelivery.Parse("try game.test.", offered);
        Assert.Equal("try The Back Room.", bareId.Text);
        Assert.Equal(new[] { "game.test" }, bareId.Ids);
        Assert.Empty(ConversationDelivery.Parse("game.test.fake", offered).Ids);
        Assert.Equal(new[] { "page.assets" }, ConversationDelivery.Parse("i don't have a video link.", new[] { Activity("page.assets") }).Ids);
    }

    [Fact]
    public void NewEmiRevisionFencesOldRepliesOnceAndKeepsOtherAvatars()
    {
        var settings = new ConditioningControlPanel.Models.AppSettings { CompanionEmiFixedVoiceApplied = true };
        Assert.False(EmiPersonality.FenceOldVoice(settings, false, false));
        Assert.True(EmiPersonality.FenceOldVoice(settings, true, true));
        Assert.Equal(EmiPersonality.VoiceRevision, settings.CompanionEmiVoiceRevision);
        var fence = settings.PersonaVoiceFenceUtc;
        Assert.False(EmiPersonality.FenceOldVoice(settings, true, true));
        Assert.Equal(fence, settings.PersonaVoiceFenceUtc);
    }
    private static CompanionActivity Activity(string id) => new(id, id, "test", () => true,
        () => throw new Exception("a reply must never open an activity"));
}
