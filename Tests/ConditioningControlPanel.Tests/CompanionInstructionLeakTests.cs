using System;
using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Owner desk run 2026-09-25 (Circe, companion v2, MythoMax with no system role): replies ended in
/// an author's note the model wrote about its own reply, the same pool title came back three turns
/// running, and the persona fence logged a cut on every send.
/// </summary>
public class CompanionInstructionLeakTests
{
    // ---------- 1. leaked instruction text ----------

    [Fact]
    public void TheLiveGuillemetNoteIsCut()
    {
        const string live = "Well, I have just the thing for you. Why don't you try this one: \"Cuckold SPH - You Sleep On The Floor\". I think it will be perfect for your conditioning needs. » (Note: The response should be in Circe's usual style, but she should also acknowledge and address the pet's specific request for a video to condition with.)";
        Assert.Equal(
            "Well, I have just the thing for you. Why don't you try this one: \"Cuckold SPH - You Sleep On The Floor\". I think it will be perfect for your conditioning needs.",
            AiTextHygiene.StripInstructionLeak(live));
    }

    [Fact]
    public void TheLiveTruncatedNoteIsCutWithItsDebris()
    {
        const string live = "Oh, hello there! Just a bit distracted lately, but I'm okay now. How about you?\"? (Note: The response should reflect Circe's usual";
        Assert.Equal("Oh, hello there! Just a bit distracted lately, but I'm okay now. How about you?",
            AiTextHygiene.StripInstructionLeak(live));
    }

    [Fact]
    public void CleanRunsTheStripSoEveryProviderPathGetsIt()
        => Assert.Equal("Good pet.", AiTextHygiene.Clean("Good pet. (OOC: stay in character as Circe)"));

    [Theory]
    [InlineData("Stay locked for me, pet. [Note: she should sound possessive here]", "Stay locked for me, pet.")]
    [InlineData("Mine, always.\nNote: the response should stay short and in character.", "Mine, always.")]
    [InlineData("Edge for me, sweet thing.\n### Instruction: continue the roleplay", "Edge for me, sweet thing.")]
    [InlineData("### Response: Hello, pet.", "Hello, pet.")]
    [InlineData("Watch this one for me. Click here: [PLAY THE VIDEO DESCRIBED HERE]", "Watch this one for me.")]
    [InlineData("(Author's note: Circe keeps her voice warm)", "")]
    public void OtherLeakShapesAreCut(string input, string expected)
        => Assert.Equal(expected, AiTextHygiene.StripInstructionLeak(input));

    [Theory]
    [InlineData("Take note of how warm you feel right now, pet.")]
    [InlineData("You look so needy (and so cute) when you beg.")]
    [InlineData("(giggles) Good pet~")]
    [InlineData("Note to self: keep you locked all week.")]
    [InlineData("Here is one for you: [Deep Acceptance]")]
    [InlineData("Try \"Deep Acceptance\" tonight (it's a long one).")]
    [InlineData("Mmm, the key stays with me. Always.")]
    public void LegitRepliesSurvive(string reply)
        => Assert.Equal(reply, AiTextHygiene.StripInstructionLeak(reply));

    [Fact]
    public void WireHistoryIsCleanedAndAnAllLeakTurnLeaves()
    {
        var window = new[]
        {
            CompanionTurn.Create(TurnKind.UserChat, "hi circe (note: be nice)"),
            CompanionTurn.Create(TurnKind.AssistantChat, "Hello, pet. » (Note: The response should reflect Circe's usual style)"),
            CompanionTurn.Create(TurnKind.AssistantChat, "(OOC: the reply should be short)"),
            CompanionTurn.Create(TurnKind.UserChat, "video?"),
        };

        var wire = PromptAssembler.StripLeakedInstructionText(window);

        Assert.Equal(new[] { "hi circe (note: be nice)", "Hello, pet.", "video?" }, wire.Select(t => t.Text).ToArray());
    }

    [Fact]
    public void ChatInstructionForbidsNotesAboutTheReply()
        => Assert.Contains("no notes", PromptAssembler.ChatInstruction);

    // ---------- 2. repetition ----------

    [Fact]
    public void ATitleSheJustSuggestedIsNamedInTheHint()
    {
        var window = new[]
        {
            CompanionTurn.Create(TurnKind.UserChat, "video pls"),
            CompanionTurn.Create(TurnKind.AssistantChat, "Click here: [Deep Acceptance]"),
            CompanionTurn.Create(TurnKind.UserChat, "another one"),
        };
        var line = PromptAssembler.JustSuggestedLine(window, new[] { "Deep Acceptance", "Other Video" });
        Assert.NotNull(line);
        Assert.Contains("\"Deep Acceptance\"", line);
        Assert.DoesNotContain("Other Video", line);
    }

    [Theory]
    [InlineData("play that one again")]
    [InlineData("give me deep acceptance")]
    public void AskingForItAgainLiftsTheHint(string ask)
    {
        var window = new[]
        {
            CompanionTurn.Create(TurnKind.AssistantChat, "Click here: [Deep Acceptance]"),
            CompanionTurn.Create(TurnKind.UserChat, ask),
        };
        Assert.Null(PromptAssembler.JustSuggestedLine(window, new[] { "Deep Acceptance" }));
    }

    [Fact]
    public void NoRecentSuggestionNoHint()
    {
        var window = new[]
        {
            CompanionTurn.Create(TurnKind.AssistantChat, "Hello, pet."),
            CompanionTurn.Create(TurnKind.UserChat, "hi"),
        };
        Assert.Null(PromptAssembler.JustSuggestedLine(window, new[] { "Deep Acceptance" }));
    }

    [Fact]
    public void TheHintRidesTheChatRequest()
    {
        var assembler = new PromptAssembler(new InertMemoryStore(), new RecentRecommendations(),
            systemPromptProvider: () => "PREFIX",
            localClock: () => new DateTime(2026, 9, 25, 11, 0, 0),
            linkPool: () => new[] { ("Deep Acceptance", "https://hypnotube.com/video/deep-acceptance-113157.html") },
            personaFence: () => null,
            identityFence: () => null);
        var session = new ChatSession();
        session.Append(TurnKind.UserChat, "video?");
        session.Append(TurnKind.AssistantChat, "Click here: [Deep Acceptance]");
        session.Append(TurnKind.UserChat, "more");

        var request = assembler.BuildRequest(ConditioningControlPanel.Services.AIService.AiPurpose.Chat, session, "more");

        Assert.Contains("You just suggested \"Deep Acceptance\"", request.SystemPrompt);
    }

    // ---------- 3. persona fence ----------

    [Fact]
    public void TheFenceStillCutsButLogsOncePerFence()
    {
        var fence = new DateTime(2026, 9, 25, 8, 12, 8, DateTimeKind.Utc);
        var assembler = new PromptAssembler(new InertMemoryStore(), new RecentRecommendations(),
            systemPromptProvider: () => "PREFIX",
            localClock: () => new DateTime(2026, 9, 25, 11, 0, 0),
            linkPool: () => Array.Empty<(string, string)>(),
            personaFence: () => fence,
            identityFence: () => fence);
        var window = new[]
        {
            CompanionTurn.Create(TurnKind.UserChat, "hi emi", utc: fence.AddHours(-2)),
            CompanionTurn.Create(TurnKind.AssistantChat, "hey", utc: fence.AddHours(-2)),
            CompanionTurn.Create(TurnKind.UserChat, "hi circe", utc: fence.AddHours(1)),
        };

        // The recall pass never logs.
        Assert.Single(assembler.FenceHistoryToPersona(window, log: false));
        Assert.Null(assembler.LastFenceLog);

        Assert.Single(assembler.FenceHistoryToPersona(window));
        var first = assembler.LastFenceLog;
        Assert.Equal(2, first!.Value.Cut);

        // Same fence, same counts: the cut still happens, no new log stamp.
        Assert.Single(assembler.FenceHistoryToPersona(window));
        Assert.Equal(first, assembler.LastFenceLog);
    }
}
