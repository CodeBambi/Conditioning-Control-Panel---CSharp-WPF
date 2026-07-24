using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.AIService;
using Xunit;
using ChatMessage = ConditioningControlPanel.Services.AIService.LocalAiService.ChatMessage;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #631 — LocalAiService kept appending every user/assistant turn to the in-memory
/// <c>_messages</c> list and posted the WHOLE list to Ollama each request, so the prompt/
/// KV-cache grew without bound until Ollama exhausted system RAM. The fix caps the in-memory
/// list to the same 50-pair window used for disk persistence via
/// <see cref="LocalAiService.TrimDialogueHistory(System.Collections.Generic.List{ConditioningControlPanel.Services.AIService.LocalAiService.ChatMessage}, int)"/>,
/// always preserving the system message and any non-dialogue preamble (the enrichment
/// context block) and dropping the OLDEST dialogue turns from the front.
///
/// The real contract is a message-count cap (max 2*pairs dialogue messages), NOT strict
/// pair alignment — these tests assert exactly what the production code guarantees.
/// </summary>
public class LocalAiHistoryTrimTests
{
    private const int MaxPairs = 50;
    private const string ContextBlock = "[CONTEXT BLOCK — NOT DIALOGUE]";

    private static ChatMessage Sys() => new("system", "you are bambi");
    private static ChatMessage Preamble() => new("user", ContextBlock + " time=now facts={}");
    private static ChatMessage User(int i) => new("user", $"u{i}");
    private static ChatMessage Asst(int i) => new("assistant", $"a{i}");

    /// <summary>Builds [system, preamble, (u0,a0), (u1,a1), ... (u{pairs-1},a{pairs-1})].</summary>
    private static List<ChatMessage> BuildHistory(int pairs, bool withPreamble = true)
    {
        var list = new List<ChatMessage> { Sys() };
        if (withPreamble) list.Add(Preamble());
        for (int i = 0; i < pairs; i++)
        {
            list.Add(User(i));
            list.Add(Asst(i));
        }
        return list;
    }

    private static List<ChatMessage> Dialogue(List<ChatMessage> list) =>
        list.Where(LocalAiService.IsDialogueTurn).ToList();

    [Fact]
    public void OverCap_TrimsToExactly50Pairs_PreservingSystemAndPreamble()
    {
        var history = BuildHistory(pairs: 60); // 120 dialogue messages

        LocalAiService.TrimDialogueHistory(history, MaxPairs);

        // System + preamble (2 non-dialogue) + exactly 100 dialogue messages = 102 total.
        Assert.Equal(102, history.Count);
        var dialogue = Dialogue(history);
        Assert.Equal(MaxPairs * 2, dialogue.Count); // 100 messages = 50 pairs

        // Non-dialogue preamble entries preserved, in original order, still at the front.
        Assert.Equal("system", history[0].Role);
        Assert.Equal("you are bambi", history[0].Content);
        Assert.Equal("user", history[1].Role);
        Assert.Contains(ContextBlock, history[1].Content);

        // Even, alternating input -> clean 50 user-first pairs after trim.
        for (int p = 0; p < MaxPairs; p++)
        {
            Assert.Equal("user", dialogue[p * 2].Role);
            Assert.Equal("assistant", dialogue[p * 2 + 1].Role);
        }
    }

    [Fact]
    public void OldestPairsRemoved_NewestKept()
    {
        var history = BuildHistory(pairs: 60); // pairs 0..59

        LocalAiService.TrimDialogueHistory(history, MaxPairs);

        var dialogue = Dialogue(history);
        // Dropped the oldest 10 pairs (0..9); retained pairs 10..59.
        Assert.Equal("u10", dialogue.First().Content);
        Assert.Equal("a59", dialogue.Last().Content);
        Assert.DoesNotContain(dialogue, m => m.Content == "u0");
        Assert.DoesNotContain(dialogue, m => m.Content == "a9");
        Assert.Contains(dialogue, m => m.Content == "u10");
        Assert.Contains(dialogue, m => m.Content == "a59");
    }

    [Fact]
    public void AtCap_Untouched()
    {
        var history = BuildHistory(pairs: 50); // exactly 100 dialogue messages
        var before = history.ToList();

        LocalAiService.TrimDialogueHistory(history, MaxPairs);

        Assert.Equal(before.Count, history.Count);
        Assert.True(before.SequenceEqual(history)); // same references, same order
    }

    [Fact]
    public void UnderCap_Untouched()
    {
        var history = BuildHistory(pairs: 10);
        var before = history.ToList();

        LocalAiService.TrimDialogueHistory(history, MaxPairs);

        Assert.Equal(before.Count, history.Count);
        Assert.True(before.SequenceEqual(history));
    }

    [Fact]
    public void RealUsage_TrailingUserTurn_TailPreserved_AndCappedByMessageCount()
    {
        // Mirrors how the app calls it: right after appending the newest USER turn (before the
        // assistant reply exists), so the dialogue count is odd. The cap is by message count,
        // so the retained window is exactly the last 2*MaxPairs dialogue messages and the
        // just-appended tail turn is never dropped.
        var history = BuildHistory(pairs: 100); // 200 dialogue messages
        history.Add(User(100));                 // trailing lone user -> 201 dialogue messages
        var originalDialogue = Dialogue(history);

        LocalAiService.TrimDialogueHistory(history, MaxPairs);

        var dialogue = Dialogue(history);
        Assert.Equal(MaxPairs * 2, dialogue.Count); // capped to 100 messages
        // Tail (the freshly appended user turn the error paths RemoveAt) survives untouched.
        Assert.Equal("u100", history[^1].Content);
        Assert.Equal("u100", dialogue.Last().Content);
        // Retained set is exactly the last 100 messages of the original dialogue, in order.
        Assert.True(originalDialogue.Skip(originalDialogue.Count - MaxPairs * 2)
            .SequenceEqual(dialogue));
    }

    [Fact]
    public void PreambleMayAppearAfterSystem_IsNeverCountedOrDropped()
    {
        // The enrichment block is a user-role message but is context, not dialogue: it must be
        // preserved regardless of how much real dialogue is trimmed around it.
        var history = BuildHistory(pairs: 70, withPreamble: true);

        LocalAiService.TrimDialogueHistory(history, MaxPairs);

        Assert.Contains(history, m => m.Content != null && m.Content.Contains(ContextBlock));
        Assert.Equal("system", history[0].Role);
        Assert.Contains(ContextBlock, history[1].Content);
    }

    [Fact]
    public void IsDialogueTurn_ExcludesSystemAndContextBlock()
    {
        Assert.True(LocalAiService.IsDialogueTurn(User(1)));
        Assert.True(LocalAiService.IsDialogueTurn(Asst(1)));
        Assert.False(LocalAiService.IsDialogueTurn(Sys()));
        Assert.False(LocalAiService.IsDialogueTurn(Preamble()));
        Assert.False(LocalAiService.IsDialogueTurn(new ChatMessage("user", "")));
        Assert.False(LocalAiService.IsDialogueTurn(new ChatMessage("user", null)));
    }
}
