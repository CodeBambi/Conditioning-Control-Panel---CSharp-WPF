using System;
using System.Linq;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class ConversationRecallTests
{
    private static readonly DateTime Old = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static ChatSession History()
    {
        var session = new ChatSession();
        session.Append(TurnKind.UserChat, "My favorite tea is jasmine, with no sugar.", utc: Old);
        session.Append(TurnKind.AssistantChat, "Jasmine tea, no sugar. Got it.", utc: Old.AddSeconds(1));
        for (var i = 0; i < 45; i++)
        {
            session.Append(TurnKind.UserChat, "Another round at the arcade", utc: Old.AddMinutes(i + 1));
            session.Append(TurnKind.AssistantChat, "That was close.", utc: Old.AddMinutes(i + 1).AddSeconds(1));
        }
        session.Append(TurnKind.UserChat, "Which tea did I say I liked?", utc: Old.AddDays(1));
        return session;
    }

    private static PromptAssembler Assembler(bool memory = true, DateTime? identityFence = null, DateTime? personaFence = null) =>
        new(new InertMemoryStore(), new RecentRecommendations(), systemPromptProvider: () => "Current character",
            identityFence: () => identityFence, personaFence: () => personaFence, preview: () => true,
            chatMemoryEnabled: () => memory, lockdownContext: () => null);

    [Fact]
    public void EarlierPreference_ReachesPromptAsQuotedBoundedContext()
    {
        var session = History();
        var request = Assembler().BuildRequest(AiPurpose.Chat, session, "Which tea did I say I liked?");
        Assert.Contains("EARLIER CONVERSATION EXCERPTS", request.SystemPrompt);
        Assert.Contains("jasmine", request.SystemPrompt);
        var recall = ConversationRecall.Build(session.Turns, session.BuildWindow(ChatWindowSpec.Chat), session.Turns, "tea", true);
        Assert.True(ChatSession.ApproxTokens(recall) <= ConversationRecall.TokenBudget);
        Assert.Equal(1, request.Messages.Count(m => m.Content == "Which tea did I say I liked?"));
    }

    [Fact]
    public void UnrelatedEarlierDialogue_IsNotRecalled() =>
        Assert.DoesNotContain("EARLIER CONVERSATION EXCERPTS", Assembler().BuildRequest(AiPurpose.Chat, History(), "How is the weather?").SystemPrompt);

    [Fact]
    public void MemoryOff_DoesNotRecallOlderDialogue() =>
        Assert.DoesNotContain("jasmine", Assembler(memory: false).BuildRequest(AiPurpose.Chat, History(), "tea").SystemPrompt);

    [Fact]
    public void ForgetThread_RemovesRecallSource()
    {
        var session = History();
        session.Clear();
        Assert.DoesNotContain("jasmine", Assembler().BuildRequest(AiPurpose.Chat, session, "tea").SystemPrompt);
    }

    [Fact]
    public void IdentitySwitch_ExcludesEarlierUserAndCompanion()
    {
        var prompt = Assembler(identityFence: Old.AddHours(1)).BuildRequest(AiPurpose.Chat, History(), "tea").SystemPrompt;
        Assert.DoesNotContain("jasmine", prompt);
    }

    [Fact]
    public void PersonalitySwitch_RecallsUserFactWithoutPreviousVoice()
    {
        var prompt = Assembler(personaFence: Old.AddHours(1)).BuildRequest(AiPurpose.Chat, History(), "tea").SystemPrompt;
        Assert.Contains("jasmine", prompt);
        Assert.DoesNotContain("Companion replied:", prompt);
    }

    [Theory]
    [InlineData("We were talking about \"The Long Goodbye\".")]
    [InlineData("Try reading \"The Left Hand of Darkness\".")]
    [InlineData("Your cat is called \"Captain Marmalade\".")]
    public void Preview_HistoryPreservesQuotedNamesInsteadOfReplacingThemWithMedia(string reply)
    {
        var session = new ChatSession();
        session.Append(TurnKind.UserChat, "Continue our conversation.");
        session.Append(TurnKind.AssistantChat, reply);
        session.Append(TurnKind.UserChat, "Tell me more.");
        var assembler = new PromptAssembler(new InertMemoryStore(), new RecentRecommendations(),
            systemPromptProvider: () => "Current character", preview: () => true,
            linkPool: () => new[] { ("Training Tape", "https://example.test/tape") }, lockdownContext: () => null);
        var request = assembler.BuildRequest(AiPurpose.Chat, session, "Tell me more.");
        Assert.Contains(request.Messages, m => m.Role == ChatMessage.RoleAssistant && m.Content == reply);
        Assert.DoesNotContain(request.Messages, m => m.Content.Contains("Training Tape"));
    }

    [Fact]
    public void FailedUnpairedTurn_IsNeverRecalled()
    {
        var session = new ChatSession();
        session.Append(TurnKind.UserChat, "unanswered jasmine question");
        Assert.Null(ConversationRecall.Build(session.Turns, Array.Empty<CompanionTurn>(), session.Turns, "jasmine", true));
    }
}
