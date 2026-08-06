using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The arbiter's decision table (doc 02 §5.1) and the two guarantees the whole train rests on:
/// <b>exactly one reaction per frame</b>, and <b>cooldowns burn on delivery, never on attempt</b>.
///
/// <para>Both are counting arguments, so every test here counts calls on a fake mouth rather than
/// inspecting state. "Zero double-reactions" is a ship criterion (MASTER-SCOPE §10) and the reason it
/// fails today is that the two speaking paths were only reachable through a live speech bubble — a
/// criterion nobody could check. These are the check.</para>
/// </summary>
public class AwarenessArbiterDecisionTests
{
    private static readonly DateTime T0 = new(2026, 8, 3, 9, 0, 0, DateTimeKind.Local);

    // ===================== fakes =====================

    private sealed class Clock
    {
        public DateTime Now = T0;
        public DateTime Read() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    private sealed class FakeSpeaker : IAwarenessSpeaker
    {
        public string? CurrentAppId { get; set; }
        public bool BarkAvailable { get; set; } = true;
        public bool MouthAvailable { get; set; } = true;

        public int BarkCount;
        public int LineCount;
        public string? LastLine;
        public RarityTier LastTier;

        /// <summary>Total lines that reached the user, from any mouth. The number the guarantee is about.</summary>
        public int SpokenCount => BarkCount + LineCount;

        public bool TrySpeakBark(ContextFrame frame)
        {
            if (!BarkAvailable) return false;
            BarkCount++;
            return true;
        }

        public bool TrySpeakLine(string line, RarityTier tier)
        {
            if (!MouthAvailable) return false;
            LineCount++;
            LastLine = line;
            LastTier = tier;
            return true;
        }
    }

    private sealed class FakeLineSource : IAwarenessLineSource
    {
        public bool IsAvailable { get; set; } = true;
        public int Calls;
        public AwarenessReply Reply = AwarenessReply.Empty;
        public Func<CancellationToken, Task<AwarenessReply>>? Behavior;

        public Task<AwarenessReply> RequestAsync(ContextFrame frame, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Behavior != null ? Behavior(cancellationToken) : Task.FromResult(Reply);
        }
    }

    private static ContextFrame Frame(
        string appId = "youtube",
        RarityTier tier = RarityTier.Common,
        string? cluster = null,
        TransitionKind transition = TransitionKind.NewApp) =>
        new()
        {
            AppId = appId,
            AppCluster = cluster,
            ServiceName = "YouTube",
            Tier = tier,
            Transition = transition,
            CutAt = T0
        };

    private sealed record Rig(
        ReactionArbiter Arbiter,
        FakeSpeaker Speaker,
        FakeLineSource Source,
        ReactionCooldownLedger Cooldowns,
        WorthinessScorer Scorer,
        StubCompanionMemory Memory,
        Clock Clock);

    private static Rig Build(
        AwarenessIntensity intensity = AwarenessIntensity.Chatty,
        TimeSpan? timeout = null)
    {
        var clock = new Clock();
        var cooldowns = new ReactionCooldownLedger(() => intensity);
        var scorer = new WorthinessScorer(() => intensity);
        var speaker = new FakeSpeaker();
        var source = new FakeLineSource();
        var memory = new StubCompanionMemory();

        var arbiter = new ReactionArbiter(
            cooldowns, scorer, clock.Read, speaker, source, memory,
            timeout ?? TimeSpan.FromMilliseconds(200));

        return new Rig(arbiter, speaker, source, cooldowns, scorer, memory, clock);
    }

    // ===================== decision table =====================

    [Fact]
    public async Task CommonTier_SpeaksABarkAndNeverReachesTheModel()
    {
        var rig = Build();

        var decision = await rig.Arbiter.SubmitAsync(Frame(tier: RarityTier.Common));

        Assert.Equal(AwarenessVerdict.Bark, decision.Verdict);
        Assert.Equal(RarityTier.Common, decision.Tier);
        Assert.Equal(1, rig.Speaker.BarkCount);
        Assert.Equal(0, rig.Speaker.LineCount);
        Assert.Equal(0, rig.Source.Calls);
        Assert.NotEmpty(decision.Reason);
    }

    [Fact]
    public async Task UncommonTier_SpeaksAnLlmLineAndSuppressesTheBarkForThatFrame()
    {
        var rig = Build();
        rig.Source.Reply = new AwarenessReply("fourth time today, hm?", null, false);

        var decision = await rig.Arbiter.SubmitAsync(Frame(tier: RarityTier.Uncommon));

        Assert.Equal(AwarenessVerdict.Llm, decision.Verdict);
        Assert.Equal(1, rig.Speaker.LineCount);
        Assert.Equal(0, rig.Speaker.BarkCount);
        Assert.Equal("fourth time today, hm?", rig.Speaker.LastLine);
    }

    [Fact]
    public async Task RareTier_IsDeliveredWithTheRareTierFanfare()
    {
        var rig = Build();
        rig.Source.Reply = new AwarenessReply("that's the third visit and you still haven't bought it", null, false);

        var decision = await rig.Arbiter.SubmitAsync(Frame(tier: RarityTier.Rare));

        Assert.Equal(AwarenessVerdict.Llm, decision.Verdict);
        Assert.Equal(RarityTier.Rare, decision.Tier);
        Assert.Equal(RarityTier.Rare, rig.Speaker.LastTier);
    }

    [Fact]
    public async Task NoLineSource_DegradesToTheFreeTierRatherThanGoingQuiet()
    {
        var rig = Build();
        rig.Source.IsAvailable = false;

        var decision = await rig.Arbiter.SubmitAsync(Frame(tier: RarityTier.Rare));

        Assert.Equal(AwarenessVerdict.Bark, decision.Verdict);
        Assert.Equal(1, rig.Speaker.BarkCount);
        Assert.Equal(0, rig.Source.Calls);
        Assert.Contains("llm-unavailable", decision.Reason);
    }

    [Fact]
    public async Task NoMatchingBarkRule_IsSilenceNotAnExceptionAndBurnsNothing()
    {
        var rig = Build();
        rig.Speaker.BarkAvailable = false;

        var decision = await rig.Arbiter.SubmitAsync(Frame(tier: RarityTier.Common));

        Assert.Equal(AwarenessVerdict.Silence, decision.Verdict);
        Assert.Equal(0, rig.Speaker.SpokenCount);
        Assert.Equal(0, rig.Cooldowns.LinesLastHour(T0));
    }

    // ===================== exactly one reaction per frame =====================

    [Fact]
    public async Task LlmTimeout_FallsBackToExactlyOneBark()
    {
        var rig = Build(timeout: TimeSpan.FromMilliseconds(50));
        rig.Source.Behavior = async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new AwarenessReply("too late to matter", null, false);
        };

        var decision = await rig.Arbiter.SubmitAsync(Frame(tier: RarityTier.Uncommon));

        Assert.Equal(AwarenessVerdict.Bark, decision.Verdict);
        Assert.Equal(1, rig.Speaker.SpokenCount);
        Assert.Equal(1, rig.Speaker.BarkCount);
        Assert.Equal(0, rig.Speaker.LineCount);
        Assert.Contains("llm-timeout", decision.Reason);
    }

    [Fact]
    public async Task LlmFailure_FallsBackToExactlyOneBark()
    {
        var rig = Build();
        rig.Source.Behavior = _ => throw new InvalidOperationException("provider exploded");

        var decision = await rig.Arbiter.SubmitAsync(Frame(tier: RarityTier.Uncommon));

        Assert.Equal(AwarenessVerdict.Bark, decision.Verdict);
        Assert.Equal(1, rig.Speaker.SpokenCount);
        Assert.Contains("llm-error", decision.Reason);
    }

    [Fact]
    public async Task ARefusedOrEmptyReply_FallsBackToExactlyOneBark()
    {
        // A moderation refusal reaches the arbiter as "nothing usable" — the guard already logged it
        // and the arbiter must not re-check, or the compliance log doubles.
        var rig = Build();
        rig.Source.Reply = AwarenessReply.Empty;

        var decision = await rig.Arbiter.SubmitAsync(Frame(tier: RarityTier.Uncommon));

        Assert.Equal(AwarenessVerdict.Bark, decision.Verdict);
        Assert.Equal(1, rig.Speaker.SpokenCount);
        Assert.Contains("llm-empty", decision.Reason);
    }

    [Fact]
    public async Task LlmPass_SaysNothingAtAllAndTheFallbackBarkDoesNotFire()
    {
        // [PASS] is her choosing silence. Answering it with a canned line would make the silence token
        // pointless and would put a bark in the exact slot she declined.
        var rig = Build();
        rig.Source.Reply = AwarenessReply.Pass;

        var decision = await rig.Arbiter.SubmitAsync(Frame(tier: RarityTier.Uncommon));

        Assert.Equal(AwarenessVerdict.Silence, decision.Verdict);
        Assert.Equal("pass", decision.Reason);
        Assert.Equal(0, rig.Speaker.SpokenCount);
    }

    [Fact]
    public async Task ASecondFrameArrivingMidCallIsRefusedRatherThanQueued()
    {
        var rig = Build(timeout: TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource<AwarenessReply>();
        rig.Source.Behavior = _ =>
        {
            entered.TrySetResult();
            return release.Task;
        };

        var first = rig.Arbiter.SubmitAsync(Frame("youtube", RarityTier.Uncommon));
        await entered.Task;

        var second = await rig.Arbiter.SubmitAsync(Frame("hades", RarityTier.Uncommon));
        Assert.Equal(AwarenessVerdict.Silence, second.Verdict);
        Assert.Equal("busy", second.Reason);
        Assert.Equal(1, rig.Source.Calls);

        release.SetResult(new AwarenessReply("one line, one moment", null, false));
        var firstDecision = await first;

        Assert.Equal(AwarenessVerdict.Llm, firstDecision.Verdict);
        Assert.Equal(1, rig.Speaker.SpokenCount);
    }

    // ===================== delivery-time staleness (doc 02 §4.3) =====================

    [Fact]
    public async Task AStaleLineIsDroppedRatherThanDeliveredInThePresentTense()
    {
        var rig = Build();
        rig.Speaker.CurrentAppId = "hades";                 // the user moved on during the call
        rig.Source.Reply = new AwarenessReply("scrolling again, are we?", null, false);

        var decision = await rig.Arbiter.SubmitAsync(Frame("youtube", RarityTier.Uncommon));

        Assert.Equal(AwarenessVerdict.Silence, decision.Verdict);
        Assert.Equal("stale", decision.Reason);
        Assert.Equal(0, rig.Speaker.SpokenCount);           // and no bark either: a canned line is just as stale
        Assert.Equal(0, rig.Cooldowns.LinesLastHour(T0));
    }

    [Fact]
    public async Task AStaleLineIsRetaggedWhenTheModelOfferedTheCallbackVariant()
    {
        var rig = Build();
        rig.Speaker.CurrentAppId = "hades";
        rig.Source.Reply = new AwarenessReply("scrolling again, are we?", "saw you on there a minute ago~", false);

        var decision = await rig.Arbiter.SubmitAsync(Frame("youtube", RarityTier.Uncommon));

        Assert.Equal(AwarenessVerdict.Llm, decision.Verdict);
        Assert.Equal("delivered-alt", decision.Reason);
        Assert.Equal("saw you on there a minute ago~", rig.Speaker.LastLine);
    }

    [Fact]
    public async Task AnUnknownForegroundIsNotTreatedAsStale()
    {
        // Plenty of ordinary windows never classify. Reading "unknown" as "moved on" would drop every
        // line on those machines — a silent mute is the worse failure here, not the safer one.
        var rig = Build();
        rig.Speaker.CurrentAppId = null;
        rig.Source.Reply = new AwarenessReply("still here?", null, false);

        var decision = await rig.Arbiter.SubmitAsync(Frame("youtube", RarityTier.Uncommon));

        Assert.Equal(AwarenessVerdict.Llm, decision.Verdict);
        Assert.Equal(1, rig.Speaker.LineCount);
    }

    [Fact]
    public async Task StayingPutIsNotStale()
    {
        var rig = Build();
        rig.Speaker.CurrentAppId = "YouTube";               // same app, different casing
        rig.Source.Reply = new AwarenessReply("still here?", null, false);

        var decision = await rig.Arbiter.SubmitAsync(Frame("youtube", RarityTier.Uncommon));

        Assert.Equal("delivered", decision.Reason);
    }

    // ===================== floors, budget, intensity =====================

    [Fact]
    public async Task TheNinetySecondLlmFloorDegradesToABarkRatherThanToSilence()
    {
        var rig = Build();
        rig.Arbiter.RecordExternalLine(ReactionSource.AwarenessLlm, "twitter");

        rig.Clock.Advance(TimeSpan.FromSeconds(65));        // past the 60s global gap, inside the 90s LLM gap
        var decision = await rig.Arbiter.SubmitAsync(Frame("hades", RarityTier.Uncommon));

        Assert.Equal(AwarenessVerdict.Bark, decision.Verdict);
        Assert.Contains("llm-gap", decision.Reason);
        Assert.Equal(0, rig.Source.Calls);
    }

    [Fact]
    public async Task AnExhaustedHourlyBudgetServesNothingAndNeverPaysForATokenToFindOut()
    {
        var rig = Build();                                   // Chatty = 6 lines/hour
        for (int i = 0; i < 6; i++)
        {
            rig.Cooldowns.RecordDelivery(ReactionSource.Bark, "app" + i, T0.AddMinutes(i * 2));
        }

        rig.Clock.Advance(TimeSpan.FromMinutes(15));
        var decision = await rig.Arbiter.SubmitAsync(Frame("hades", RarityTier.Rare));

        Assert.Equal(AwarenessVerdict.Silence, decision.Verdict);
        Assert.Contains("hourly-budget", decision.Reason);
        Assert.Equal(0, rig.Source.Calls);
        Assert.Equal(0, rig.Speaker.SpokenCount);
    }

    [Fact]
    public async Task IntensityOffSilencesEveryTier()
    {
        var rig = Build(AwarenessIntensity.Off);
        rig.Source.Reply = new AwarenessReply("hi", null, false);

        Assert.Equal(AwarenessVerdict.Silence, (await rig.Arbiter.SubmitAsync(Frame(tier: RarityTier.Common))).Verdict);
        Assert.Equal(AwarenessVerdict.Silence, (await rig.Arbiter.SubmitAsync(Frame(tier: RarityTier.Rare))).Verdict);
        Assert.Equal(0, rig.Speaker.SpokenCount);
    }

    [Fact]
    public async Task SubtleIntensityStillHonoursTheSameFloors()
    {
        var rig = Build(AwarenessIntensity.Subtle);          // 2/hour
        rig.Cooldowns.RecordDelivery(ReactionSource.Bark, "a", T0);
        rig.Cooldowns.RecordDelivery(ReactionSource.Bark, "b", T0.AddMinutes(5));

        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        var decision = await rig.Arbiter.SubmitAsync(Frame("hades", RarityTier.Common));

        Assert.Equal(AwarenessVerdict.Silence, decision.Verdict);
        Assert.Contains("hourly-budget", decision.Reason);
    }

    // ===================== keyword priority (doc 02 §5.3) =====================

    [Fact]
    public async Task AKeywordCommentTakesTheMomentAndAwarenessDoesNotStackOnIt()
    {
        var rig = Build();
        rig.Arbiter.RecordExternalLine(ReactionSource.Keyword, null);

        rig.Clock.Advance(TimeSpan.FromSeconds(5));
        var decision = await rig.Arbiter.SubmitAsync(Frame(tier: RarityTier.Uncommon));

        Assert.Equal(AwarenessVerdict.Silence, decision.Verdict);
        Assert.Equal(0, rig.Speaker.SpokenCount);
    }

    [Fact]
    public void AKeywordLineIsNeverStarvedByTheAwarenessBudget()
    {
        // The user configured it by hand, so it is exempt from the LLM floor and the hourly budget —
        // paced by the global gap alone, never silenced by awareness's own spending.
        var rig = Build(AwarenessIntensity.Subtle);
        for (int i = 0; i < 6; i++) rig.Cooldowns.RecordDelivery(ReactionSource.Bark, "app" + i, T0.AddMinutes(i));

        rig.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True(rig.Arbiter.CanSpeak(ReactionSource.Keyword, "anything"));
        Assert.False(rig.Arbiter.CanSpeak(ReactionSource.Bark, "anything"));
    }

    [Fact]
    public void AKeywordLineDoesNotPushTheAwarenessThresholdUp()
    {
        var rig = Build();
        double baseline = rig.Scorer.CurrentThreshold(T0);

        rig.Arbiter.RecordExternalLine(ReactionSource.Keyword, "youtube");

        Assert.Equal(baseline, rig.Scorer.CurrentThreshold(T0), 6);
        Assert.Equal(0.0, rig.Scorer.RepetitionPenalty("youtube", T0), 6);
    }

    [Fact]
    public void ADeliveredBarkDoesRaiseTheSilenceBudget()
    {
        // Doc 02 §3.4: the budget is "all tiers combined, barks included". If a bark did not move the
        // scorer, the free tier would spend the hour and the threshold would never notice.
        var rig = Build();
        double baseline = rig.Scorer.CurrentThreshold(T0);

        rig.Arbiter.RecordExternalLine(ReactionSource.Bark, "youtube");

        Assert.True(rig.Scorer.CurrentThreshold(T0) > baseline);
    }

    // ===================== burn on delivery only =====================

    [Fact]
    public async Task APassCostsNothingAndTheVeryNextWorthyFrameStillSpeaks()
    {
        var rig = Build();
        rig.Source.Reply = AwarenessReply.Pass;
        double baseline = rig.Scorer.CurrentThreshold(T0);

        await rig.Arbiter.SubmitAsync(Frame("youtube", RarityTier.Uncommon));

        Assert.Equal(0, rig.Cooldowns.LinesLastHour(T0));
        Assert.Equal(baseline, rig.Scorer.CurrentThreshold(T0), 6);

        rig.Source.Reply = new AwarenessReply("there it is", null, false);
        var second = await rig.Arbiter.SubmitAsync(Frame("hades", RarityTier.Uncommon));

        Assert.Equal(AwarenessVerdict.Llm, second.Verdict);
    }

    [Fact]
    public async Task ATimeoutThatCannotEvenBarkLeavesTheBudgetUntouched()
    {
        var rig = Build(timeout: TimeSpan.FromMilliseconds(50));
        rig.Speaker.BarkAvailable = false;
        rig.Source.Behavior = async ct => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return AwarenessReply.Empty; };

        var decision = await rig.Arbiter.SubmitAsync(Frame(tier: RarityTier.Uncommon));

        Assert.Equal(AwarenessVerdict.Silence, decision.Verdict);
        Assert.Equal(0, rig.Cooldowns.LinesLastHour(T0));
        Assert.Equal(0.0, rig.Scorer.RepetitionPenalty("youtube", T0), 6);
    }

    [Fact]
    public async Task ADeliveredLineBurnsExactlyOneSlot()
    {
        var rig = Build();
        rig.Source.Reply = new AwarenessReply("mm-hmm", null, false);

        await rig.Arbiter.SubmitAsync(Frame("youtube", RarityTier.Uncommon));

        Assert.Equal(1, rig.Cooldowns.LinesLastHour(T0));
        Assert.True(rig.Scorer.RepetitionPenalty("youtube", T0) > 0);
    }

    // ===================== ban list =====================

    [Fact]
    public async Task ADeliveredModelLineJoinsTheBanListButABarkDoesNot()
    {
        var rig = Build();
        rig.Source.Reply = new AwarenessReply("fourth time today", null, false);
        await rig.Arbiter.SubmitAsync(Frame("youtube", RarityTier.Uncommon));

        var afterLine = await rig.Memory.GetRecentReactionsAsync(10);
        Assert.Single(afterLine);
        Assert.Equal("fourth time today", afterLine[0].Text);

        rig.Clock.Advance(TimeSpan.FromMinutes(20));
        rig.Source.IsAvailable = false;
        await rig.Arbiter.SubmitAsync(Frame("hades", RarityTier.Common));

        Assert.Equal(1, rig.Speaker.BarkCount);
        Assert.Single(await rig.Memory.GetRecentReactionsAsync(10));   // still just the model line
    }

    // ===================== privacy gates =====================

    [Fact]
    public async Task TheAdultClusterFailsClosedWithoutItsOwnReactionToggle()
    {
        // App.Settings is null headlessly, and an unreadable toggle is not permission. Same posture as
        // the observer's consent read: no answer means no.
        var rig = Build();
        rig.Source.Reply = new AwarenessReply("caught you", null, false);

        var decision = await rig.Arbiter.SubmitAsync(
            Frame("some-site", RarityTier.Rare, AwarenessClusters.Adult));

        Assert.Equal(AwarenessVerdict.Silence, decision.Verdict);
        Assert.Equal("adult-off", decision.Reason);
        Assert.Equal(0, rig.Source.Calls);
        Assert.Equal(0, rig.Speaker.SpokenCount);
    }

    [Fact]
    public async Task ANullFrameIsSilenceNotAnException()
    {
        var rig = Build();
        var decision = await rig.Arbiter.SubmitAsync(null!);

        Assert.Equal(AwarenessVerdict.Silence, decision.Verdict);
        Assert.Equal("null-frame", decision.Reason);
    }

    // ===================== the routing switch =====================

    [Fact]
    public void LegacyPathsStayLiveUntilAnArbiterIsActuallyAttached()
    {
        // The suppression is deliberately NOT a settings read alone: v2 configured but unwired would
        // otherwise mute her completely. Unwired always means "legacy, unchanged".
        AwarenessV2Routing.Detach();
        Assert.Null(AwarenessV2Routing.Arbiter);
        Assert.False(AwarenessV2Routing.IsActive);

        var rig = Build();
        AwarenessV2Routing.Attach(rig.Arbiter);
        try
        {
            Assert.Same(rig.Arbiter, AwarenessV2Routing.Arbiter);
            // Headless there is no consent and no settings, so IsActive is still false — attaching an
            // arbiter is necessary but not sufficient.
            Assert.False(AwarenessV2Routing.IsActive);
        }
        finally
        {
            AwarenessV2Routing.Detach();
        }

        Assert.Null(AwarenessV2Routing.Arbiter);
    }
}
