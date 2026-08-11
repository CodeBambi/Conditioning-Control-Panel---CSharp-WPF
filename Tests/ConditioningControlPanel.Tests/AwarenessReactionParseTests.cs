using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Awareness;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The output contract as PARSED rather than as promised (doc 02 §3.1 item 5, §4.3): one line, ≤140
/// chars, <c>[PASS]</c> honoured, and the optional past-tense <c>CALLBACK:</c> variant pulled out for
/// the arbiter's delivery-time staleness re-tag.
/// </summary>
public class AwarenessReactionParseTests
{
    // ===================== [PASS] =====================

    [Theory]
    [InlineData("[PASS]")]
    [InlineData("  [PASS]  ")]
    [InlineData("[pass]")]
    [InlineData("\"[PASS]\"")]
    [InlineData("[PASS].")]
    [InlineData("PASS")]
    public void PassIsHonouredInTheShapesModelsActuallyEmitIt(string reply)
    {
        var parsed = AwarenessReactionService.Parse(reply);

        Assert.True(parsed.Passed);
        Assert.False(parsed.HasLine);
        Assert.Equal("pass", parsed.Reason);
    }

    [Fact]
    public void PassIsAnAnswerNotAFailure()
    {
        // The arbiter refunds the budget slot for a [PASS] (doc 02 §3.4/§7 item 5), which it can only
        // do if a pass is distinguishable from a dead call.
        Assert.True(AwarenessReactionService.Parse("[PASS]").IsAiGenerated);
        Assert.False(AwarenessReactionService.Parse("").IsAiGenerated);
        Assert.Equal("empty", AwarenessReactionService.Parse("").Reason);
    }

    [Fact]
    public void APassThatCameWithACallbackIsStillAPass()
    {
        var parsed = AwarenessReactionService.Parse("[PASS]\nCALLBACK: you were on there a minute ago");

        Assert.True(parsed.Passed);
        Assert.Null(parsed.Callback);
    }

    // ===================== CALLBACK =====================

    [Fact]
    public void TheCallbackVariantIsPulledOutSeparately()
    {
        var parsed = AwarenessReactionService.Parse(
            "fourth visit today and you still haven't picked one~\nCALLBACK: that was your fourth visit, for the record~");

        Assert.Equal("fourth visit today and you still haven't picked one~", parsed.Line);
        Assert.Equal("that was your fourth visit, for the record~", parsed.Callback);
        Assert.Equal("ok", parsed.Reason);
    }

    [Fact]
    public void ACallbackOnlyReplyIsUsedRatherThanDiscarded()
    {
        var parsed = AwarenessReactionService.Parse("CALLBACK: you were on there a minute ago~");

        Assert.Equal("you were on there a minute ago~", parsed.Line);
        Assert.Null(parsed.Callback);
        Assert.Equal("callback-only", parsed.Reason);
    }

    [Fact]
    public void OnlyTheFirstOfEachIsKept()
    {
        var parsed = AwarenessReactionService.Parse("one\ntwo\nCALLBACK: a\nCALLBACK: b");

        Assert.Equal("one", parsed.Line);
        Assert.Equal("a", parsed.Callback);
    }

    // ===================== the 140-char contract =====================

    [Fact]
    public void ALongLineIsTrimmedAtAWordBoundaryRatherThanDropped()
    {
        var line = string.Join(' ', Enumerable.Repeat("scrolling", 40));
        var parsed = AwarenessReactionService.Parse(line);

        Assert.True(parsed.Line.Length <= AwarenessReactionService.MaxLineLength);
        Assert.EndsWith("…", parsed.Line);
        Assert.DoesNotContain("scrollin…", parsed.Line);      // cut on a space, not mid-word
    }

    [Fact]
    public void ALineExactlyAtTheCapIsLeftAlone()
    {
        var line = new string('a', AwarenessReactionService.MaxLineLength);
        Assert.Equal(line, AwarenessReactionService.Parse(line).Line);
    }

    // ===================== hygiene =====================

    [Theory]
    [InlineData("\"back again already?~\"", "back again already?~")]
    [InlineData("'back again already?~'", "back again already?~")]
    [InlineData("“back again already?~”", "back again already?~")]
    public void WrappingQuotesAreShed(string reply, string expected)
        => Assert.Equal(expected, AwarenessReactionService.Parse(reply).Line);

    [Fact]
    public void TheSameHygieneEveryOtherReplyPathRunsIsRunHere()
    {
        // Reasoning blocks, echoed context tags and the bark-echo sigil are all live failure modes on
        // the other reply paths; a new path that skipped them would be the fourth provider that
        // "quietly missed it" (AiTextHygiene's own docs).
        Assert.Equal("fourth time today~",
            AwarenessReactionService.Parse("<think>she is on twitter again</think>fourth time today~").Line);
        Assert.Equal("fourth time today~",
            AwarenessReactionService.Parse("[Category: Social | App: Twitter] fourth time today~").Line);
        Assert.Equal("fourth time today~",
            AwarenessReactionService.Parse("«Bambi said aloud: \"fourth time today~\"»").Line);
    }

    // ===================== the call =====================

    [Fact]
    public async Task ARefusalSurfacesAsARefusalAndNeverAsALine()
    {
        var service = Service(new FakeTransport(new AiReplyResult(
            string.Empty, IsAiGenerated: false,
            Refusal: new ModerationRefusalInfo(ProhibitedCategory.Minor, ModerationSource.Output))));

        var reaction = await service.GetAwarenessReactionAsync(Frame());

        Assert.NotNull(reaction.Refusal);
        Assert.False(reaction.HasLine);
        Assert.Equal("refused", reaction.Reason);
    }

    [Fact]
    public async Task ACannedFallbackIsNotSpokenAsAnAwarenessLine()
    {
        // IsAiGenerated=false means "offline / no entitlement / transport failure" and the canned text
        // belongs to the chat box, not to an unprompted observation about their screen.
        var service = Service(new FakeTransport(
            new AiReplyResult("Hmm... still thinking.", IsAiGenerated: false, Refusal: null)));

        var reaction = await service.GetAwarenessReactionAsync(Frame());

        Assert.False(reaction.HasLine);
        Assert.Equal("no-ai", reaction.Reason);
    }

    [Fact]
    public async Task TheCallIsTaggedAsAReactionAndCappedAtSixtyTokens()
    {
        var transport = new FakeTransport(new AiReplyResult("fourth time today~", true, null));
        var reaction = await Service(transport).GetAwarenessReactionAsync(Frame());

        Assert.True(reaction.HasLine);
        Assert.Equal(AiPurpose.Reaction, transport.LastOptions!.Purpose);
        Assert.Equal(AwarenessPromptBuilder.ResponseMaxTokens, transport.LastOptions.MaxTokens);
        Assert.False(transport.LastOptions.Interactive);       // never escalates the Content Policy Notice
    }

    [Fact]
    public async Task NothingIsSentWhenThereIsNoFrameAndNothingThrowsWhenTheTransportDoes()
    {
        var dead = new FakeTransport(null!) { Throw = true };
        var service = Service(dead);

        Assert.Equal("null-frame", (await service.GetAwarenessReactionAsync(null)).Reason);
        Assert.Equal("transport-failed", (await service.GetAwarenessReactionAsync(Frame())).Reason);
        Assert.Equal("no-transport",
            (await Service(null).GetAwarenessReactionAsync(Frame())).Reason);
    }

    [Fact]
    public async Task TheKillSwitchStopsTheCallBeforeAnythingIsAssembled()
    {
        // UseAwarenessV2 / AwarenessModeEnabled / AwarenessConsentGiven, via AwarenessObserver.IsEnabled.
        // The observer and the arbiter gate this too; a consent check that holds in only one of three
        // places is not a consent check.
        var transport = new FakeTransport(new AiReplyResult("fourth time today~", true, null));

        var reaction = await Service(transport, enabled: false).GetAwarenessReactionAsync(Frame());

        Assert.Equal("v2-off", reaction.Reason);
        Assert.Null(transport.LastMessages);
    }

    [Fact]
    public async Task TheCloudPathNeverGetsTheLocalProjection()
    {
        var transport = new FakeTransport(new AiReplyResult("ok~", true, null));
        var frame = Frame() with { NowPlaying = new MediaInfo("Sleepy Bimbo Loop 4", "Bambi Sleep", "Playing", 5) };

        await Service(transport, machineLocal: false).GetAwarenessReactionAsync(frame);
        Assert.DoesNotContain("Sleepy Bimbo Loop 4", transport.LastMessages!.Last().Content);

        await Service(transport, machineLocal: true).GetAwarenessReactionAsync(frame);
        Assert.Contains("Sleepy Bimbo Loop 4", transport.LastMessages!.Last().Content);
    }

    // ===================== invented links =====================

    /// <summary>
    /// Merge defect (2026-08-07): the invented-link strip was installed at every model-to-user seam
    /// that existed at the time, all of which run through CompanionBrain. This leg deliberately
    /// routes around the brain, so it inherited none of it - and the speech bubble turns a bare URL
    /// into a CLICKABLE hyperlink whose text is derived from the URL, so a hallucinated link arrives
    /// looking like a curated title. The awareness prompt ships no media catalogue, so nothing here
    /// is ever legitimately sanctioned.
    /// </summary>
    [Fact]
    public void AnInventedLinkNeverSurvivesIntoAnAwarenessLine()
    {
        var reaction = AwarenessReactionService.Parse(
            "bored of that one? mine's better - https://hypnotube.com/deep-bambi-drop-88421.html");

        // The sentence carrying the link goes; the question before it is still hers to say.
        Assert.DoesNotContain("hypnotube.com", reaction.Line);
        Assert.DoesNotContain("http", reaction.Line);
        Assert.Equal("bored of that one?", reaction.Line);
    }

    [Fact]
    public void TheCallbackVariantIsStrippedToo_AndAnAllLinkReplyBecomesSilence()
    {
        var reaction = AwarenessReactionService.Parse(
            "https://youtu.be/9d7WKQwz7qCcM\nCALLBACK: saw you on www.youtube.com/watch?v=abcdefghijk earlier");

        // Both fields were nothing but invented links, so there is nothing trustworthy left to say.
        // None() carries an empty Line rather than null, and the reason must not read "callback-only".
        Assert.Equal(string.Empty, reaction.Line);
        Assert.Null(reaction.Callback);
        Assert.Equal("no-line", reaction.Reason);
    }

    [Fact]
    public void AnOrdinaryLineIsUntouched()
    {
        var reaction = AwarenessReactionService.Parse("third time on Amazon today~");
        Assert.Equal("third time on Amazon today~", reaction.Line);
    }

    // ===================== helpers =====================

    private static ContextFrame Frame() => new()
    {
        AppId = "twitter",
        AppCluster = "site_doomscroll",
        Category = ActivityCategory.Social,
        ServiceName = "Twitter",
        VisitsToday = 4,
        Tier = RarityTier.Rare,
        CutAt = DateTime.Now
    };

    private static AwarenessReactionService Service(
        FakeTransport? transport, bool machineLocal = false, bool enabled = true)
        => new(() => transport,
               new AwarenessPromptBuilder(() => "builtin-bambisleep", AwarenessAngleCards.Embedded, () => null, 7),
               () => machineLocal,
               () => enabled);

    private sealed class FakeTransport : IAiService
    {
        private readonly AiReplyResult _result;
        public FakeTransport(AiReplyResult result) => _result = result;

        public bool Throw { get; init; }
        public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }
        public AiCallOptions? LastOptions { get; private set; }

        public bool IsAvailable => true;
        public int DailyRequestsRemaining => 100;

        public Task<AiReplyResult> SendAsync(IReadOnlyList<ChatMessage> messages, AiCallOptions options,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages;
            LastOptions = options;
            if (Throw) throw new InvalidOperationException("transport is down");
            return Task.FromResult(_result);
        }

        public void Dispose() { }

#pragma warning disable CS0618 // legacy one-shot surface; unused by the v2 path
        public Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
            => Task.FromResult(string.Empty);
        public Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
            => Task.FromResult(new AiReplyResult(string.Empty, false, null));
        public Task<string?> GetAwarenessReactionAsync(string detectedName, string category,
            string serviceName = "", string pageTitle = "", TimeSpan? duration = null)
            => Task.FromResult<string?>(null);
        public Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
            => Task.FromResult<string?>(null);
        public Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
            => Task.FromResult<string?>(null);
        public Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
            => Task.FromResult<string?>(null);
        public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
            => Task.FromResult<string?>(null);
#pragma warning restore CS0618
    }
}
