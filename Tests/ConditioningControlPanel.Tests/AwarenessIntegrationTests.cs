using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Moderation;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The seams BETWEEN the four Train 2 packages — the parts no single package could test, and which
/// each package's own suite passed while the pipeline as a whole was disconnected.
///
/// <para>All four packages merged without a textual conflict and the app built with zero errors, yet
/// nothing in v2 ran: the observer enforced its own copy of the privacy rules instead of the ones the
/// consent dialog describes, the panel's live seam was never fed, the arbiter was never attached, and
/// the LLM leg stood itself down on a truncation guard. Each test below fails if one of those
/// reconnections is undone.</para>
/// </summary>
[Collection(AwarenessStaticsCollection.Name)]
public class AwarenessIntegrationTests : IDisposable
{
    public AwarenessIntegrationTests() => Reset();

    public void Dispose() => Reset();

    private static void Reset()
    {
        AwarenessPause.Resume();
        AwarenessLive.Clear();
        AwarenessLive.Ledger = null;
        AwarenessLive.Memory = null;
        AwarenessLive.ResetObserverState = null;
    }

    private static AwarenessPolicySettings Policy(
        IEnumerable<string>? deny = null,
        IEnumerable<string>? titles = null)
        => new(
            AwarenessText.SanitizeRuleList(deny),
            AwarenessText.SanitizeRuleList(titles),
            AdultReactionsEnabled: true,
            AdultRecordingEnabled: true);

    private static ForegroundSample Sample(string title, string process = "chrome")
        => new(new IntPtr(1), title, process, false);

    // =====================================================================================
    //  one privacy dialect: the observer enforces the rules the UI describes
    // =====================================================================================

    /// <summary>
    /// The real hole this closed. The privacy package ships its recommended protection as three GROUP
    /// TOKENS ("@passwords", "@banking", "@email-titles") that only mean anything once
    /// <c>AwarenessPrivacyRules</c> expands them. The observer used to match its deny list literally,
    /// so it compared the string "@passwords" against "1Password" and let the window through — while
    /// the privacy panel displayed the chip as an active rule.
    /// </summary>
    [Theory]
    [InlineData(AwarenessPrivacyRules.GroupPasswordManagers, "1Password - All Vaults", "1password")]
    [InlineData(AwarenessPrivacyRules.GroupPasswordManagers, "Bitwarden Web Vault - Google Chrome", "chrome")]
    [InlineData(AwarenessPrivacyRules.GroupBanking, "Chase Online - Google Chrome", "chrome")]
    public void SeededDenyGroups_ActuallyBlockAtTheObserver_NotJustInThePanel(
        string groupToken, string title, string process)
    {
        var verdict = AwarenessObserverPolicy.EvaluatePrivacy(
            Sample(title, process), Policy(deny: new[] { groupToken }));

        Assert.Equal(FrameDrop.DenyListed, verdict.Drop);
        Assert.False(verdict.Allowed);
    }

    /// <summary>
    /// Unifying the two dialects must not quietly cost a capability. Deny-by-cluster silences a whole
    /// category with one rule; the shared matcher had no notion of a cluster before this.
    /// </summary>
    [Fact]
    public void DenyByCluster_SurvivedTheUnification()
    {
        var verdict = AwarenessObserverPolicy.EvaluatePrivacy(
            Sample("reddit - the front page of the internet - Google Chrome"),
            Policy(deny: new[] { "site_doomscroll" }));

        Assert.Equal(FrameDrop.DenyListed, verdict.Drop);
    }

    /// <summary>
    /// A cluster rule is an exact token, not a substring. If it were a substring, the two-character
    /// minimum on a rule entry would let "si" silence every site_* cluster in one keystroke.
    /// </summary>
    [Fact]
    public void ClusterRules_AreExactTokens_SoAShortRuleCannotSilenceEveryCluster()
    {
        var verdict = AwarenessObserverPolicy.EvaluatePrivacy(
            Sample("reddit - the front page of the internet - Google Chrome"),
            Policy(deny: new[] { "site_" }));

        Assert.NotEqual(FrameDrop.DenyListed, verdict.Drop);
    }

    /// <summary>
    /// The pause button. Before the packages were reconciled the observer had no notion of a pause at
    /// all: the panel's "pause for an hour" control set a flag that only the panel itself read, so she
    /// kept counting and kept talking. A pause that only mutes is a lie with a button on it.
    /// </summary>
    [Fact]
    public void Pause_StopsTheObserverRecordingAnything_NotJustSpeaking()
    {
        var now = new DateTime(2026, 8, 7, 12, 0, 0, DateTimeKind.Local);
        AwarenessPause.Pause(TimeSpan.FromHours(1), now);

        var verdict = AwarenessObserverPolicy.EvaluatePrivacy(
            Sample("YouTube - Google Chrome"), Policy(), now.AddMinutes(5));

        Assert.Equal(FrameDrop.Paused, verdict.Drop);
        Assert.False(verdict.Allowed);
    }

    [Fact]
    public void Pause_Expires_AndSheStartsSeeingAgain()
    {
        var now = new DateTime(2026, 8, 7, 12, 0, 0, DateTimeKind.Local);
        AwarenessPause.Pause(TimeSpan.FromHours(1), now);

        var verdict = AwarenessObserverPolicy.EvaluatePrivacy(
            Sample("YouTube - Google Chrome"), Policy(), now.AddHours(1).AddMinutes(1));

        Assert.True(verdict.Allowed);
    }

    /// <summary>
    /// Fail closed (security addendum D). Every reason the shared privacy layer can refuse for, and
    /// every reason it might learn later, has to land on a DROP in the observer's enum — never on
    /// <see cref="FrameDrop.None"/>, which would be "the layer said no, so send it".
    /// </summary>
    [Fact]
    public void EveryPrivacyDropReason_MapsToADropAndNeverToNone()
    {
        foreach (AwarenessDropReason reason in Enum.GetValues(typeof(AwarenessDropReason)))
        {
            if (reason == AwarenessDropReason.None) continue;

            var mapped = InvokeMapDrop(reason);
            Assert.NotEqual(FrameDrop.None, mapped);
        }
    }

    private static FrameDrop InvokeMapDrop(AwarenessDropReason reason)
    {
        var method = typeof(AwarenessObserverPolicy).GetMethod(
            "MapDrop",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return (FrameDrop)method!.Invoke(null, new object[] { reason })!;
    }

    // =====================================================================================
    //  the live seam: the panel shows the real frame, and the wipe erases all of it
    // =====================================================================================

    [Fact]
    public void PublishingAFrame_FeedsTheLiveSeam_SoTheWireViewShowsTheRealThing()
    {
        var observer = NewObserver();
        var frame = NewFrame("youtube");

        observer.PublishFrame(frame);

        Assert.Same(frame, AwarenessLive.LastFrame);
        Assert.NotNull(AwarenessLive.LastFrameAt);
    }

    [Fact]
    public void PublishingAFrame_RaisesFramePublished_SoAnOpenPanelCanReRead()
    {
        var observer = NewObserver();
        int raised = 0;
        EventHandler handler = (_, _) => raised++;

        AwarenessLive.FramePublished += handler;
        try { observer.PublishFrame(NewFrame("steam")); }
        finally { AwarenessLive.FramePublished -= handler; }

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// Erasure must be total (security addendum B). The ledger and the memory are files; the
    /// observer's committed app, pending candidate and last frame are not, and nothing that deletes
    /// files reaches them. A wipe that leaves those behind leaves her able to comment on the very
    /// thing that was just erased.
    /// </summary>
    [Fact]
    public void Wipe_ResetsTheObserversInRamState_NotOnlyTheFiles()
    {
        var observer = NewObserver();
        observer.PublishFrame(NewFrame("youtube"));
        Assert.NotNull(observer.LastFrame);
        Assert.NotNull(AwarenessLive.LastFrame);

        AwarenessLive.WipeEverything();

        Assert.Null(AwarenessLive.LastFrame);
        Assert.Null(observer.LastFrame);
        Assert.Null(observer.CurrentAppId);
    }

    [Fact]
    public void Wipe_ForgetsEverythingInTheMemoryRing()
    {
        var memory = new RecordingMemory();
        AwarenessLive.Memory = memory;

        AwarenessLive.WipeEverything();

        Assert.Contains(memory.Forgotten, id => id == null);
    }

    [Fact]
    public void Wipe_SurvivesAnObserverResetThatThrows()
    {
        AwarenessLive.ResetObserverState = () => throw new InvalidOperationException("boom");
        AwarenessLive.Publish(NewFrame("steam"));

        // The file half of the erasure must still happen even when the RAM half fails.
        AwarenessLive.WipeEverything();

        Assert.Null(AwarenessLive.LastFrame);
    }

    [Fact]
    public void ForgetOneApp_ClearsTheWireViewWhenItIsShowingThatApp()
    {
        AwarenessLive.Publish(NewFrame("youtube"));

        AwarenessLive.Forget("youtube");

        Assert.Null(AwarenessLive.LastFrame);
    }

    // =====================================================================================
    //  the LLM leg: a reaction survives the trip from the service to the arbiter
    // =====================================================================================

    /// <summary>
    /// A deliberate <c>[PASS]</c> must arrive at the arbiter AS a pass. Collapsing it into "nothing
    /// came back" would make the arbiter answer her chosen silence with a canned bark — a line in the
    /// exact slot she declined — and charge the budget for it (doc 02 §7 item 5).
    /// </summary>
    [Fact]
    public async Task ADeliberatePass_ReachesTheArbiterAsAPass_NotAsAFailure()
    {
        var source = SourceSaying("[PASS]");

        var reply = await source.RequestAsync(NewFrame("youtube"), CancellationToken.None);

        Assert.True(reply.IsPass);
        Assert.False(reply.HasLine);
    }

    /// <summary>
    /// The callback is the staleness re-tag, and it is the clearest case of the two packages having
    /// been written against different contracts. The prompt teaches the model to write
    /// <c>CALLBACK:</c> (<see cref="AwarenessPromptBuilder.OutputContract"/>); the arbiter's own
    /// parser was written for <c>ALT:</c> and would have folded every callback into the spoken line or
    /// dropped it. Delegating to the service's parser keeps the one contract the model was taught.
    /// </summary>
    [Fact]
    public async Task ACallbackLine_SurvivesTheTripToTheArbiter()
    {
        var source = SourceSaying("third time on that tab today\nCALLBACK: you were on that tab a minute ago");

        var reply = await source.RequestAsync(NewFrame("youtube"), CancellationToken.None);

        Assert.True(reply.HasLine);
        Assert.True(reply.HasAlternate);
        Assert.Contains("a minute ago", reply.Alternate!);
        Assert.DoesNotContain("CALLBACK", reply.Line!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The prompt's own contract is the one the delegated parser honours — not a second dialect.</summary>
    [Fact]
    public void ThePromptTeachesTheCallbackKeyword_TheParserHonours()
    {
        Assert.Contains("CALLBACK:", AwarenessPromptBuilder.OutputContract, StringComparison.Ordinal);
    }

    /// <summary>
    /// A moderation refusal is never spoken and never becomes a line. It reads as "nothing usable",
    /// which costs a bark — the refusal itself stays where the spine logged it.
    /// </summary>
    [Fact]
    public async Task ARefusal_IsNeverSpoken()
    {
        var source = SourceReplying(new AiReplyResult(
            string.Empty, false, new ModerationRefusalInfo(null, ModerationSource.Input)));

        var reply = await source.RequestAsync(NewFrame("youtube"), CancellationToken.None);

        Assert.False(reply.HasLine);
        Assert.False(reply.IsPass);
    }

    /// <summary>A canned/fallback string is not a model line and must never be spoken as one.</summary>
    [Fact]
    public async Task ACannedFallback_IsNotSpokenAsHerLine()
    {
        var source = SourceReplying(new AiReplyResult("the servers are busy right now", false, null));

        var reply = await source.RequestAsync(NewFrame("youtube"), CancellationToken.None);

        Assert.False(reply.HasLine);
    }

    [Fact]
    public async Task AnEmptyReply_FallsBackRatherThanSpeakingNothing()
    {
        var source = SourceSaying("   ");

        var reply = await source.RequestAsync(NewFrame("youtube"), CancellationToken.None);

        Assert.False(reply.HasLine);
        Assert.False(reply.IsPass);
        Assert.Equal(AwarenessReply.Empty, reply);
    }

    /// <summary>
    /// Invariant 7's cost claim, asserted where it can actually regress: the reaction leg must be sent
    /// with the reaction purpose and its own small token cap, not the chat defaults.
    /// </summary>
    [Fact]
    public async Task TheReactionLeg_IsSentWithTheReactionPurposeAndItsOwnTokenCap()
    {
        var transport = new FakeTransport(new AiReplyResult("nice", true, null));
        var source = new BrainAwarenessLineSource(
            brain: () => null, isLocalTransport: () => false,
            reactions: new AwarenessReactionService(() => transport, isEnabled: () => true));

        await source.RequestAsync(NewFrame("youtube"), CancellationToken.None);

        Assert.NotNull(transport.LastOptions);
        Assert.Equal(AiCallOptions.Reaction.Purpose, transport.LastOptions!.Purpose);
        Assert.Equal(AwarenessPromptBuilder.ResponseMaxTokens, transport.LastOptions.MaxTokens);
    }

    /// <summary>
    /// Privacy, at the only place it can be checked end to end: whatever the transport receives must
    /// not contain the raw window title. The shipped allow list is empty, so no title may appear at
    /// all — this is the assertion that fails if a future change lets one through.
    /// </summary>
    [Fact]
    public async Task NothingSentToTheTransport_CarriesARawWindowTitle()
    {
        var transport = new FakeTransport(new AiReplyResult("nice", true, null));
        var source = new BrainAwarenessLineSource(
            brain: () => null, isLocalTransport: () => false,
            reactions: new AwarenessReactionService(() => transport, isEnabled: () => true));

        var frame = NewFrame("youtube") with { PageTitleSanitized = null };
        await source.RequestAsync(frame, CancellationToken.None);

        Assert.NotNull(transport.LastMessages);
        var wire = string.Join("\n", transport.LastMessages!.Select(m => m.Content));
        Assert.DoesNotContain("Google Chrome", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("codebambi@", wire, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The regression that made the whole LLM leg dead on arrival: the projection is several hundred
    /// characters and <c>CompanionEvent</c> clamps an ambient descriptor to ~100, so the old path
    /// stood itself down permanently. The dedicated reaction prompt has no such clamp — which is also
    /// the point of it having its own prompt at all.
    /// </summary>
    [Fact]
    public void TheProjection_IsFarLongerThanAnAmbientEventDescriptorMayBe()
    {
        var projection = AwarenessProjection.BuildCloudProjection(NewFrame("youtube"));

        Assert.True(projection.Length > CompanionEvent.MaxChars,
            $"projection was {projection.Length} chars; the clamp is {CompanionEvent.MaxChars}");
    }

    // =====================================================================================
    //  one mouth, and never zero mouths
    // =====================================================================================

    /// <summary>
    /// The legacy mouth is suppressed by "v2 has an arbiter attached", NOT by "the setting says v2".
    /// Those came apart in exactly the case the observer's construction catch block exists for: with
    /// the setting on and construction failed, the legacy events stayed muted and there was no arbiter
    /// to speak, so awareness went silent altogether while the log claimed a fallback to legacy.
    /// </summary>
    [Fact]
    public void WithNoArbiterAttached_TheLegacyMouthIsNotSuppressed()
    {
        AwarenessV2Routing.Detach();
        try
        {
            Assert.False(AwarenessV2Routing.IsActive);
            Assert.False(ReadV2OwnsReactions());
        }
        finally { AwarenessV2Routing.Detach(); }
    }

    /// <summary>
    /// The suppression predicate and the routing predicate must be the SAME question. Two predicates
    /// that can disagree is how a feature ends up with two mouths, or with none.
    /// </summary>
    [Fact]
    public void TheLegacySuppressionAsksExactlyTheRoutingQuestion()
    {
        AwarenessV2Routing.Detach();
        try { Assert.Equal(AwarenessV2Routing.IsActive, ReadV2OwnsReactions()); }
        finally { AwarenessV2Routing.Detach(); }
    }

    private static bool ReadV2OwnsReactions()
    {
        var prop = typeof(WindowAwarenessService).GetProperty(
            "V2OwnsReactions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(prop);
        return (bool)prop!.GetValue(null)!;
    }

    // =====================================================================================
    //  helpers
    // =====================================================================================

    private static AwarenessObserver NewObserver()
    {
        var ledger = new ActivityLedger();
        var memory = new StubCompanionMemory();
        var observer = new AwarenessObserver(ledger, new WorthinessScorer(), new ReactionArbiter(), memory);

        AwarenessLive.Ledger = ledger;
        AwarenessLive.Memory = memory;
        AwarenessLive.ResetObserverState = observer.ResetTransientState;
        return observer;
    }

    private static ContextFrame NewFrame(string appId) => new()
    {
        AppId = appId,
        ServiceName = appId,
        AppCluster = "site_video",
        Category = ActivityCategory.Media,
        Transition = TransitionKind.NewApp,
        VisitsToday = 3,
        MinutesToday = 42,
        DwellSeconds = 300,
        CutAt = new DateTime(2026, 8, 7, 21, 0, 0, DateTimeKind.Local)
    };

    /// <summary>A line source wired to the real reaction service over a transport that says one thing.</summary>
    private static BrainAwarenessLineSource SourceSaying(string modelText) =>
        SourceReplying(new AiReplyResult(modelText, true, null));

    private static BrainAwarenessLineSource SourceReplying(AiReplyResult result) =>
        new(brain: () => null, isLocalTransport: () => false,
            reactions: new AwarenessReactionService(
                transport: () => new FakeTransport(result), isEnabled: () => true));

    /// <summary>
    /// Mirrors the fake in <c>AwarenessReactionParseTests</c> — the reaction path's established test
    /// double, kept identical rather than forked.
    /// </summary>
    private sealed class FakeTransport : IAiService
    {
        private readonly AiReplyResult _result;
        public FakeTransport(AiReplyResult result) => _result = result;

        public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }
        public AiCallOptions? LastOptions { get; private set; }

        public bool IsAvailable => true;
        public int DailyRequestsRemaining => 100;

        public Task<AiReplyResult> SendAsync(IReadOnlyList<ChatMessage> messages, AiCallOptions options,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages;
            LastOptions = options;
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

    private sealed class RecordingMemory : ICompanionMemory
    {
        public List<string?> Forgotten { get; } = new();

        public Task<IReadOnlyList<HabitRecord>> GetHabitsAsync(string appId, string? cluster)
            => Task.FromResult<IReadOnlyList<HabitRecord>>(Array.Empty<HabitRecord>());

        public Task<IReadOnlyList<ReactionSummary>> GetRecentReactionsAsync(int count)
            => Task.FromResult<IReadOnlyList<ReactionSummary>>(Array.Empty<ReactionSummary>());

        public Task RecordReactionAsync(ReactionSummary summary) => Task.CompletedTask;

        public Task ForgetAsync(string? appId)
        {
            Forgotten.Add(appId);
            return Task.CompletedTask;
        }
    }
}
