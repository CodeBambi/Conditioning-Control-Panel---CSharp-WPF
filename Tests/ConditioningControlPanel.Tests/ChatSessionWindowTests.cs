using System;
using System.Linq;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Train 1 — <see cref="ChatSession"/> prompt-window assembly. This is the pure logic that decides
/// what the model actually sees, so it carries the whole cost story (a runaway window is a runaway
/// bill) and the whole anti-fixation story (an ambient call that drags the chat thread along is what
/// made past "watch X~" lines act as few-shot bait).
///
/// Windowing in Train 1 is SIMPLE TRUNCATION — no rolling summary, no LLM compaction. These tests
/// assert exactly that: turns that don't fit are gone from the prompt and nothing stands in for them.
/// </summary>
public class ChatSessionWindowTests
{
    private static readonly DateTime T0 = new(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);

    private static ChatSession SessionWith(params (TurnKind Kind, string Text)[] turns)
    {
        var s = new ChatSession();
        for (int i = 0; i < turns.Length; i++)
            s.Append(turns[i].Kind, turns[i].Text, utc: T0.AddSeconds(i));
        return s;
    }

    /// <summary>"abcd" = 1 token under the chars/4 estimate; N tokens = 4N chars.</summary>
    private static string Tokens(int n) => new('x', n * 4);

    [Fact]
    public void ApproxTokens_IsCharsOverFour()
    {
        Assert.Equal(0, ChatSession.ApproxTokens(null));
        Assert.Equal(0, ChatSession.ApproxTokens(""));
        Assert.Equal(0, ChatSession.ApproxTokens("abc"));   // integer division, deliberately
        Assert.Equal(1, ChatSession.ApproxTokens("abcd"));
        Assert.Equal(25, ChatSession.ApproxTokens(new string('x', 100)));
    }

    [Fact]
    public void Window_KeepsNewestTurns_AndDropsWhatDoesNotFit()
    {
        // 10 turns of 100 tokens each against a 250-token budget: only the newest two fit.
        var s = new ChatSession();
        for (int i = 0; i < 10; i++)
            s.Append(i % 2 == 0 ? TurnKind.UserChat : TurnKind.AssistantChat, Tokens(100) + i, utc: T0.AddSeconds(i));

        var window = s.BuildWindow(new ChatWindowSpec(TokenBudget: 250));

        Assert.Equal(2, window.Count);
        Assert.EndsWith("8", window[0].Text);
        Assert.EndsWith("9", window[1].Text);
        // Oldest first — the wire order the provider expects.
        Assert.True(window[0].Utc < window[1].Utc);
        // Truncation only: nothing was substituted for the eight dropped turns.
        Assert.All(window, t => Assert.NotEqual(TurnKind.SystemNote, t.Kind));
    }

    [Fact]
    public void Window_AlwaysIncludesNewestTurn_EvenWhenItAloneBlowsTheBudget()
    {
        // A window that drops the very thing being reacted to is useless.
        var s = SessionWith(
            (TurnKind.UserChat, "old"),
            (TurnKind.UserChat, Tokens(500)));

        var window = s.BuildWindow(new ChatWindowSpec(TokenBudget: 10));

        Assert.Single(window);
        Assert.Equal(Tokens(500), window[0].Text);
    }

    [Fact]
    public void Window_SelfCapsAt40Messages_EvenWithBudgetToSpare()
    {
        // The proxy accepts 50; we cap at 40 so its server-side trim is never load-bearing.
        var s = new ChatSession();
        for (int i = 0; i < 120; i++)
            s.Append(i % 2 == 0 ? TurnKind.UserChat : TurnKind.AssistantChat, "hi", utc: T0.AddSeconds(i));

        var window = s.BuildWindow(new ChatWindowSpec(TokenBudget: 1_000_000));

        Assert.Equal(ChatSession.MaxWindowMessages, window.Count);
        Assert.Equal(40, window.Count);
        // Kept the NEWEST 40 (turns 80..119).
        Assert.Equal(T0.AddSeconds(80), window[0].Utc);
        Assert.Equal(T0.AddSeconds(119), window[^1].Utc);
    }

    [Fact]
    public void Window_KeepsAtMostFiveBarkEchoes_ButStillReachesOlderDialogue()
    {
        // Echoes are flavor; they must be capped WITHOUT walling off the real dialogue behind them.
        var s = new ChatSession();
        s.Append(TurnKind.UserChat, "the oldest real line", utc: T0);
        for (int i = 0; i < 12; i++)
            s.Append(TurnKind.BarkEcho, $"«she said aloud: \"bark {i}\"»", utc: T0.AddSeconds(i + 1));

        var window = s.BuildWindow(new ChatWindowSpec(TokenBudget: 1_000_000));

        Assert.Equal(ChatSession.MaxBarkEchoesInWindow, window.Count(t => t.Kind == TurnKind.BarkEcho));
        Assert.Equal(5, window.Count(t => t.Kind == TurnKind.BarkEcho));
        // The five KEPT echoes are the newest ones.
        Assert.Contains(window, t => t.Text.Contains("bark 11"));
        Assert.DoesNotContain(window, t => t.Text.Contains("bark 0"));
        // ...and the dialogue turn buried underneath twelve echoes still made it in.
        Assert.Contains(window, t => t.Text == "the oldest real line");
    }

    [Fact]
    public void AmbientWindow_CarriesOnlyTheLastFourDialogueTurns()
    {
        // Doc 01 §1.4: the fixation fix is a SMALL ambient window plus structural dedupe, not a
        // stateless call. Twenty past chat turns must not become few-shot bait for a one-line quip.
        var s = new ChatSession();
        for (int i = 0; i < 20; i++)
            s.Append(i % 2 == 0 ? TurnKind.UserChat : TurnKind.AssistantChat, $"turn{i}", utc: T0.AddSeconds(i));
        s.Append(TurnKind.AmbientEvent, "finished mandatory video 'Bambi Bae'", utc: T0.AddSeconds(50));

        var window = s.BuildWindow(ChatWindowSpec.Ambient);

        Assert.Equal(ChatSession.AmbientDialogueTurnLimit, window.Count(t => t.IsDialogue));
        Assert.Equal(4, window.Count(t => t.IsDialogue));
        // The event itself is always present — it is the thing being reacted to.
        Assert.Equal(TurnKind.AmbientEvent, window[^1].Kind);
        // The retained dialogue is the newest four (turn16..turn19).
        Assert.Contains(window, t => t.Text == "turn19");
        Assert.DoesNotContain(window, t => t.Text == "turn15");
    }

    [Fact]
    public void ChatWindow_HasNoDialogueCap_UnlikeAmbient()
    {
        Assert.Equal(int.MaxValue, ChatWindowSpec.Chat.MaxDialogueTurns);
        Assert.Equal(ChatSession.ChatHistoryTokenBudget, ChatWindowSpec.Chat.TokenBudget);
        Assert.Equal(ChatSession.AmbientHistoryTokenBudget, ChatWindowSpec.Ambient.TokenBudget);
    }

    [Fact]
    public void SystemNotes_NeverEnterAWindow()
    {
        var s = SessionWith(
            (TurnKind.SystemNote, "app closed 2026-08-05 01:14"),
            (TurnKind.UserChat, "hi"),
            (TurnKind.SystemNote, "mod switched to Sissy"),
            (TurnKind.AssistantChat, "hi you~"));

        var window = s.BuildWindow(ChatWindowSpec.Chat);

        Assert.Equal(2, window.Count);
        Assert.DoesNotContain(window, t => t.Kind == TurnKind.SystemNote);
    }

    [Fact]
    public void ToMessages_MapsRolesAndAppliesTheEventSigil()
    {
        var s = SessionWith(
            (TurnKind.UserChat, "hey"),
            (TurnKind.AssistantChat, "hey you~"),
            (TurnKind.AmbientEvent, "level up -> 41"),
            (TurnKind.BarkEcho, "«Bambi said aloud: \"good girl~\"»"));

        var messages = ChatSession.ToMessages(s.BuildWindow(ChatWindowSpec.Chat));

        Assert.Equal(4, messages.Count);
        Assert.Equal(ChatMessage.RoleUser, messages[0].Role);
        Assert.Equal(ChatMessage.RoleAssistant, messages[1].Role);
        // Events ride as user-role (things that happened TO her), wearing the sigil so they can
        // never be confused with typed input.
        Assert.Equal(ChatMessage.RoleUser, messages[2].Role);
        Assert.Equal("«event: level up -> 41»", messages[2].Content);
        // Echoes ride as assistant-role (things she said).
        Assert.Equal(ChatMessage.RoleAssistant, messages[3].Role);
    }

    [Fact]
    public void Remove_RollsBackByIdentity_NotByPosition()
    {
        // P2/H5 rollback must survive a bark echo landing between the send and the refusal, which
        // is exactly what a positional "remove the last turn" would get wrong.
        var s = new ChatSession();
        s.Append(TurnKind.UserChat, "earlier", utc: T0);
        var userTurn = s.Append(TurnKind.UserChat, "the refused line", utc: T0.AddSeconds(1));
        s.Append(TurnKind.BarkEcho, "«she said aloud: \"mm~\"»", utc: T0.AddSeconds(2));

        Assert.True(s.Remove(userTurn));

        Assert.Equal(2, s.Count);
        Assert.DoesNotContain(s.Turns, t => t.Text == "the refused line");
        Assert.Contains(s.Turns, t => t.Kind == TurnKind.BarkEcho);
        Assert.False(s.Remove(userTurn)); // idempotent
    }

    [Fact]
    public void DialogueTurns_ExcludeEverythingThatMustNotReachDisk()
    {
        var s = SessionWith(
            (TurnKind.UserChat, "u"),
            (TurnKind.AssistantChat, "a"),
            (TurnKind.AmbientEvent, "e"),
            (TurnKind.BarkEcho, "b"),
            (TurnKind.SystemNote, "n"));

        var dialogue = s.DialogueTurns();

        Assert.Equal(2, dialogue.Count);
        Assert.All(dialogue, t => Assert.True(t.IsDialogue));
    }

    [Fact]
    public void Restore_SetsRestoredTurnCount_AndClearWipesIt()
    {
        var s = new ChatSession();
        s.Restore(new[]
        {
            CompanionTurn.Create(TurnKind.UserChat, "from last launch"),
            CompanionTurn.Create(TurnKind.AssistantChat, "i remember~")
        });

        Assert.Equal(2, s.RestoredTurnCount);

        s.Append(TurnKind.UserChat, "new");
        Assert.Equal(2, s.RestoredTurnCount); // fresh turns don't inflate it

        s.Clear();
        Assert.Equal(0, s.RestoredTurnCount);
        Assert.Equal(0, s.Count);
    }
}
