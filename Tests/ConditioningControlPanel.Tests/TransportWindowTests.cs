using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.AIService;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Train 1 — the pure parts of the two non-cloud providers' multi-turn <c>SendAsync</c>: which text
/// gets moderated, and where the effects context block is spliced into the outgoing window.
///
/// <para>The "newest user message only" rule is load-bearing. Re-checking the whole window on every
/// turn would write one compliance-log line per historical turn per request and would trip the
/// escalating <c>ModerationCounter</c> after a couple of messages instead of after the intended
/// number of hits.</para>
/// </summary>
public class TransportWindowTests
{
    private static ChatMessage Sys(string c) => ChatMessage.System(c);
    private static ChatMessage Usr(string c) => ChatMessage.User(c);
    private static ChatMessage Bot(string c) => ChatMessage.Assistant(c);

    // ---------- newest-user selection ----------

    [Fact]
    public void NewestUserText_PicksTheLastUserTurn_NotTheFirstAndNotTheAssistant()
    {
        var window = new List<ChatMessage> { Sys("persona"), Usr("first"), Bot("ok~"), Usr("second") };

        Assert.Equal("second", LocalAiService.NewestUserText(window));
        Assert.Equal("second", OpenAiCompatibleService.NewestUserText(window));
    }

    [Fact]
    public void NewestUserText_IsEmptyWhenTheWindowHasNoUserTurn()
    {
        // Possible on a restored-history-only window; must not throw and must not moderate garbage.
        var window = new List<ChatMessage> { Sys("persona"), Bot("i remember~") };

        Assert.Equal(string.Empty, LocalAiService.NewestUserText(window));
        Assert.Equal(string.Empty, OpenAiCompatibleService.NewestUserText(window));
    }

    [Fact]
    public void NewestUserText_HandlesAnEmptyOrNullWindow()
    {
        Assert.Equal(string.Empty, LocalAiService.NewestUserText(Array.Empty<ChatMessage>()));
        Assert.Equal(string.Empty, OpenAiCompatibleService.NewestUserText(null!));
    }

    // ---------- outgoing assembly ----------

    [Fact]
    public void BuildOutgoing_ForwardsTheWindowVerbatimWhenEffectsAreOff()
    {
        // The whole point of the transport collapse: the brain's window reaches the model in order,
        // untouched. A provider that rewrites or reorders it is not a transport.
        var window = new List<ChatMessage> { Sys("persona"), Usr("first"), Bot("ok~"), Usr("second") };

        var outgoing = LocalAiService.BuildOutgoing(window, enrichment: null);

        Assert.Equal(4, outgoing.Count);
        Assert.Equal(new[] { "system", "user", "assistant", "user" }, outgoing.Select(m => m.Role));
        Assert.Equal(new[] { "persona", "first", "ok~", "second" }, outgoing.Select(m => m.Content));
    }

    [Fact]
    public void BuildOutgoing_SplicesTheEffectsBlockRightAfterTheSystemMessage()
    {
        // The legacy path put it at index 1; keeping that position is what stops the model from
        // reading the context block as part of the conversation.
        var window = new List<ChatMessage> { Sys("persona"), Usr("first"), Bot("ok~"), Usr("second") };

        var outgoing = LocalAiService.BuildOutgoing(window, "[CONTEXT BLOCK — NOT DIALOGUE] facts");

        Assert.Equal(5, outgoing.Count);
        Assert.Equal("system", outgoing[0].Role);
        Assert.Equal("user", outgoing[1].Role);
        Assert.StartsWith("[CONTEXT BLOCK", outgoing[1].Content);
        Assert.Equal("first", outgoing[2].Content);
        Assert.Equal("second", outgoing[^1].Content);
    }

    [Fact]
    public void BuildOutgoing_WithNoSystemMessage_PutsTheEffectsBlockFirst()
    {
        var window = new List<ChatMessage> { Usr("hi") };

        var outgoing = LocalAiService.BuildOutgoing(window, "[CONTEXT BLOCK — NOT DIALOGUE]");

        Assert.Equal(2, outgoing.Count);
        Assert.StartsWith("[CONTEXT BLOCK", outgoing[0].Content);
        Assert.Equal("hi", outgoing[1].Content);
    }

    [Fact]
    public void BuildOutgoing_TreatsAWhitespaceEnrichmentAsAbsent()
    {
        var outgoing = LocalAiService.BuildOutgoing(new[] { Sys("persona"), Usr("hi") }, "   ");
        Assert.Equal(2, outgoing.Count);
    }

    [Fact]
    public void BuildOutgoing_DoesNotMutateTheCallersWindow()
    {
        // The caller is CompanionBrain's live session window; splicing into it would corrupt the log.
        var window = new List<ChatMessage> { Sys("persona"), Usr("hi") };

        LocalAiService.BuildOutgoing(window, "[CONTEXT BLOCK — NOT DIALOGUE]");

        Assert.Equal(2, window.Count);
    }

    [Fact]
    public void BuildOutgoing_HandlesANullWindow()
    {
        Assert.Empty(LocalAiService.BuildOutgoing(null!, null));
    }
}
