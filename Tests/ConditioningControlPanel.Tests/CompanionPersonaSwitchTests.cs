using System;
using System.Linq;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Kathryn, 2026-09-14: "I switched from circe back to CCP Default, but the companion in the
/// 'talk to her' box is still referring to itself as Circe". She fixed it by wiping the chat,
/// which names the carrier: the history, not the prompt.
///
/// The prompt half was already right - a mod switch invalidates the stable prefix, resets the
/// personality cache and stamps the voice fence. The voice fence, though, deliberately keeps the
/// user's own turns, so the wire window still opened with lines addressed to the old companion and
/// the model read one unbroken conversation with her.
/// </summary>
public class CompanionPersonaSwitchTests
{
    // ---------- the rule ----------

    [Fact]
    public void ADifferentModWithADifferentCompanionIsAPersonaChange()
        => Assert.True(PersonaSwitchRules.ChangesPersona("circe", "ccp-default", "Circe", "EMI"));

    [Fact]
    public void ADifferentModWearingTheSameCompanionIsNot()
    {
        // Two skins that both speak as EMI: same person, new coat. The thread survives.
        Assert.False(PersonaSwitchRules.ChangesPersona("skin-a", "skin-b", "EMI", "emi"));
        Assert.False(PersonaSwitchRules.ChangesPersona("skin-a", "skin-b", "EMI", " EMI "));
    }

    [Fact]
    public void TheSameModIsNeverAPersonaChange()
        => Assert.False(PersonaSwitchRules.ChangesPersona("circe", "CIRCE", "Circe", "Circe"));

    [Fact]
    public void AnUnknownNameOnEitherSideIsNotEvidence()
    {
        // A mod stack that has not answered yet must not cost the user their thread.
        Assert.False(PersonaSwitchRules.ChangesPersona("a", "b", "", "EMI"));
        Assert.False(PersonaSwitchRules.ChangesPersona("a", "b", "Circe", null));
    }

    // ---------- the cut ----------

    [Fact]
    public void AfterACompanionChangeNothingFromBeforeItGoesOnTheWire()
    {
        var fence = new DateTime(2026, 9, 14, 22, 0, 0, DateTimeKind.Utc);
        var before = fence.AddMinutes(-10);
        var after = fence.AddMinutes(1);
        var assembler = Assembler(personaFence: () => fence, identityFence: () => fence);

        var window = new[]
        {
            CompanionTurn.Create(TurnKind.UserChat, "hi Circe", utc: before),
            CompanionTurn.Create(TurnKind.AssistantChat, "Circe is listening, pet.", utc: before),
            CompanionTurn.Create(TurnKind.BarkEcho, "«Circe said aloud: \"good boy\"»", utc: before),
            CompanionTurn.Create(TurnKind.AmbientEvent, "finished video 'X'", utc: before),
            CompanionTurn.Create(TurnKind.UserChat, "who are you?", utc: after),
        };

        var fenced = assembler.FenceHistoryToPersona(window);

        Assert.Equal(new[] { "who are you?" }, fenced.Select(t => t.Text).ToArray());
    }

    [Fact]
    public void WithoutACompanionChangeTheVoiceFenceStillKeepsTheUsersOwnWords()
    {
        // A preset switch inside the same companion: unchanged behaviour, and the reason the
        // identity fence had to be a second setting rather than a widening of the first.
        var fence = new DateTime(2026, 9, 14, 22, 0, 0, DateTimeKind.Utc);
        var before = fence.AddMinutes(-10);
        var assembler = Assembler(personaFence: () => fence, identityFence: () => null);

        var window = new[]
        {
            CompanionTurn.Create(TurnKind.UserChat, "what should i watch?", utc: before),
            CompanionTurn.Create(TurnKind.AssistantChat, "old voice reply", utc: before),
            CompanionTurn.Create(TurnKind.UserChat, "thanks", utc: fence.AddMinutes(1)),
        };

        var fenced = assembler.FenceHistoryToPersona(window);

        Assert.Equal(new[] { "what should i watch?", "thanks" }, fenced.Select(t => t.Text).ToArray());
    }

    [Fact]
    public void TheCutReachesTheBuiltRequest()
    {
        var fence = new DateTime(2026, 9, 14, 22, 0, 0, DateTimeKind.Utc);
        var session = new ChatSession();
        session.Append(TurnKind.UserChat, "hi Circe", utc: fence.AddMinutes(-5));
        session.Append(TurnKind.AssistantChat, "Circe smiles.", utc: fence.AddMinutes(-4));
        session.Append(TurnKind.UserChat, "who am i talking to?", utc: fence.AddMinutes(1));

        var request = Assembler(() => fence, () => fence).BuildRequest(AiPurpose.Chat, session, null);

        Assert.DoesNotContain(request.Messages, m => m.Content.Contains("Circe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(request.Messages, m => m.Role == ChatMessage.RoleUser
            && m.Content.Contains("who am i talking to?", StringComparison.Ordinal));
    }

    [Fact]
    public void NoFenceAtAllLeavesTheWindowUntouched()
    {
        var assembler = Assembler(() => null, () => null);
        var window = new[] { CompanionTurn.Create(TurnKind.UserChat, "hi", utc: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)) };

        Assert.Same(window, assembler.FenceHistoryToPersona(window));
    }

    // ---------- the boundary note ----------

    [Fact]
    public void ACompanionChangeLeavesABoundaryInTheLogButNotOnTheWire()
    {
        var session = new ChatSession();
        session.Append(TurnKind.UserChat, "hi Circe");
        session.Append(TurnKind.BarkEcho, "«Circe said aloud: \"good boy\"»");

        // Mirrors what ActivateMod does through App.Brain.
        int purged = session.RemoveAll(t => t.Kind == TurnKind.BarkEcho);
        session.Append(TurnKind.SystemNote, "companion changed to EMI");

        Assert.Equal(1, purged);
        Assert.Contains(session.Turns, t => t.Kind == TurnKind.SystemNote);
        // SystemNotes are excluded from every window, so the boundary costs the prompt nothing.
        Assert.DoesNotContain(session.BuildWindow(ChatWindowSpec.Chat), t => t.Kind == TurnKind.SystemNote);
    }

    // ---------- helpers ----------

    private static PromptAssembler Assembler(Func<DateTime?> personaFence, Func<DateTime?> identityFence) =>
        new(new InertMemoryStore(), new RecentRecommendations(),
            systemPromptProvider: () => "PREFIX",
            localClock: () => new DateTime(2026, 9, 14, 22, 5, 0),
            linkPool: () => Array.Empty<(string, string)>(),
            personaFence: personaFence,
            identityFence: identityFence);
}
