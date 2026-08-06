using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Train 1 — the one-time migration of <c>local_chat_history.json</c> into the brain's session, and
/// the session.json round trip.
///
/// The import runs exactly once per user and there is no second chance to get it right: a local-AI
/// user who loses their history here loses conversations they had with the companion, and the
/// <c>she_remembers</c> achievement (which keys off restored turns) silently stops working. Hence
/// tests against the real legacy file shape, including its enrichment preamble.
/// </summary>
public class CompanionSessionStoreTests
{
    private const string ContextBlock = "[CONTEXT BLOCK — NOT DIALOGUE]";

    // ---------- legacy import ----------

    [Fact]
    public void ImportLegacyHistory_MapsRolesOntoTurnKinds_InOrder()
    {
        const string json = """
        [
          {"Role":"user","Content":"hi bambi"},
          {"Role":"assistant","Content":"hi you~"},
          {"Role":"user","Content":"remember my cat?"},
          {"Role":"assistant","Content":"Prime Minister Beans~"}
        ]
        """;

        var turns = CompanionSessionStore.ImportLegacyHistory(json);

        Assert.Equal(4, turns.Count);
        Assert.Equal(TurnKind.UserChat, turns[0].Kind);
        Assert.Equal("hi bambi", turns[0].Text);
        Assert.Equal(TurnKind.AssistantChat, turns[1].Kind);
        Assert.Equal("Prime Minister Beans~", turns[3].Text);
        Assert.All(turns, t => Assert.True(t.IsDialogue));
    }

    [Fact]
    public void ImportLegacyHistory_DropsTheEnrichmentPreambleAndSystemTurns()
    {
        // The enrichment block is a USER-role message that is context, not conversation —
        // LocalAiService.IsDialogueTurn excluded it from persistence and the import must agree,
        // or every migrated user gets a stale facts dump replayed as their first "message".
        var json = $$"""
        [
          {"Role":"system","Content":"you are bambi"},
          {"Role":"user","Content":"{{ContextBlock}} time=now facts={}"},
          {"Role":"user","Content":"hi"},
          {"Role":"assistant","Content":"hi~"}
        ]
        """;

        var turns = CompanionSessionStore.ImportLegacyHistory(json);

        Assert.Equal(2, turns.Count);
        Assert.Equal("hi", turns[0].Text);
        Assert.Equal("hi~", turns[1].Text);
        Assert.DoesNotContain(turns, t => t.Text.Contains(ContextBlock));
    }

    [Fact]
    public void ImportLegacyHistory_SkipsBlankAndUnknownRoles()
    {
        const string json = """
        [
          {"Role":"","Content":"orphan"},
          {"Role":"user","Content":""},
          {"Role":"tool","Content":"not a dialogue role"},
          {"Role":"user","Content":"the only real one"}
        ]
        """;

        var turns = CompanionSessionStore.ImportLegacyHistory(json);

        Assert.Single(turns);
        Assert.Equal("the only real one", turns[0].Text);
    }

    [Fact]
    public void ImportLegacyHistory_TrimsToTheHundredMostRecentTurns()
    {
        var entries = Enumerable.Range(0, 150)
            .Select(i => $"{{\"Role\":\"{(i % 2 == 0 ? "user" : "assistant")}\",\"Content\":\"m{i}\"}}");
        var json = "[" + string.Join(",", entries) + "]";

        var turns = CompanionSessionStore.ImportLegacyHistory(json);

        Assert.Equal(CompanionSessionStore.MaxPersistedTurns, turns.Count);
        Assert.Equal(100, turns.Count);
        Assert.Equal("m50", turns[0].Text);    // oldest 50 dropped from the FRONT
        Assert.Equal("m149", turns[^1].Text);  // newest kept
    }

    [Fact]
    public void ImportLegacyHistory_SurvivesGarbage()
    {
        // A corrupt file must cost memories, not the chat box.
        Assert.Empty(CompanionSessionStore.ImportLegacyHistory("not json at all"));
        Assert.Empty(CompanionSessionStore.ImportLegacyHistory("{\"not\":\"an array\"}"));
        Assert.Empty(CompanionSessionStore.ImportLegacyHistory(""));
        Assert.Empty(CompanionSessionStore.ImportLegacyHistory("[]"));
    }

    // ---------- session.json ----------

    [Fact]
    public void ParseSession_ReadsBackWhatSaveWould()
    {
        const string json = """
        {
          "Version": 1,
          "Turns": [
            {"Kind":"UserChat","Text":"hi","Mood":null,"Utc":"2026-08-05T22:14:03Z"},
            {"Kind":"AssistantChat","Text":"hi you~","Mood":"bubbly","Utc":"2026-08-05T22:14:05Z"}
          ]
        }
        """;

        var turns = CompanionSessionStore.ParseSession(json);

        Assert.Equal(2, turns.Count);
        Assert.Equal(TurnKind.UserChat, turns[0].Kind);
        Assert.Equal("bubbly", turns[1].Mood);
        Assert.Equal(new DateTime(2026, 8, 5, 22, 14, 3, DateTimeKind.Utc), turns[0].Utc.ToUniversalTime());
    }

    [Fact]
    public void ParseSession_RejectsNonDialogueKinds()
    {
        // Only UserChat/AssistantChat are ever written; a file claiming otherwise (hand-edited, or
        // written by a future schema) must not smuggle bark echoes or events back into the window.
        const string json = """
        {
          "Version": 1,
          "Turns": [
            {"Kind":"BarkEcho","Text":"«she said aloud: \"hi\"»"},
            {"Kind":"AmbientEvent","Text":"user opened Amazon"},
            {"Kind":"SystemNote","Text":"app closed"},
            {"Kind":"Nonsense","Text":"???"},
            {"Kind":"UserChat","Text":"the real one"}
          ]
        }
        """;

        var turns = CompanionSessionStore.ParseSession(json);

        Assert.Single(turns);
        Assert.Equal("the real one", turns[0].Text);
    }

    [Fact]
    public void ParseSession_SurvivesGarbage()
    {
        Assert.Empty(CompanionSessionStore.ParseSession("not json"));
        Assert.Empty(CompanionSessionStore.ParseSession("{}"));
        Assert.Empty(CompanionSessionStore.ParseSession(""));
    }

    [Fact]
    public void Trim_KeepsTheNewestHundred()
    {
        var turns = Enumerable.Range(0, 130)
            .Select(i => CompanionTurn.Create(TurnKind.UserChat, $"m{i}"))
            .ToList();

        var trimmed = CompanionSessionStore.Trim(turns);

        Assert.Equal(100, trimmed.Count);
        Assert.Equal("m30", trimmed[0].Text);
        Assert.Equal("m129", trimmed[^1].Text);
    }

    [Fact]
    public void Trim_LeavesAnUndersizedListAlone()
    {
        var turns = new List<CompanionTurn> { CompanionTurn.Create(TurnKind.UserChat, "only") };
        Assert.Same(turns, CompanionSessionStore.Trim(turns));
    }
}
