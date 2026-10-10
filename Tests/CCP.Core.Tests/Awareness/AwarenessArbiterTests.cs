using System;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The shared cooldown ledger and the memory stub behind it (doc 02 §5, §3.3).
///
/// <para>"Zero double-reactions" is a ship criterion (MASTER-SCOPE §10). Today it fails because
/// <c>BarkService</c> and the AvatarTube reaction path keep independent cooldowns and can both fire on
/// one tab switch — the character reads as a machine with two mouths. The fix is structural, so the
/// test is too: every source shares one ledger, and these pin each of its floors.</para>
/// </summary>
public class AwarenessArbiterTests
{
    private static readonly DateTime T0 = new(2026, 8, 3, 9, 0, 0, DateTimeKind.Local);

    private static ReactionCooldownLedger Chatty() => new(() => AwarenessIntensity.Chatty);

    // ===================== one mouth =====================

    [Fact]
    public void ABarkAndAnLlmLineCannotBothLandOnOneMoment()
    {
        var ledger = Chatty();
        Assert.True(ledger.CanSpeak(ReactionSource.Bark, "youtube", T0, out _));

        ledger.RecordDelivery(ReactionSource.Bark, "youtube", T0);

        Assert.False(ledger.CanSpeak(ReactionSource.AwarenessLlm, "youtube", T0.AddSeconds(2), out var reason));
        Assert.Equal("global-gap", reason);
    }

    [Fact]
    public void TheGlobalGapAppliesToEverySourceIncludingKeywordTriggers()
    {
        var ledger = Chatty();
        ledger.RecordDelivery(ReactionSource.AwarenessLlm, "youtube", T0);

        Assert.False(ledger.CanSpeak(ReactionSource.Keyword, null, T0.AddSeconds(5), out _));
        Assert.True(ledger.CanSpeak(ReactionSource.Keyword, null, T0.Add(ReactionCooldownLedger.GlobalGap), out _));
    }

    [Fact]
    public void TwoLlmLinesAreNeverLessThanNinetySecondsApart()
    {
        var ledger = Chatty();
        ledger.RecordDelivery(ReactionSource.AwarenessLlm, "youtube", T0);

        Assert.False(ledger.CanSpeak(ReactionSource.AwarenessLlm, "hades", T0.AddSeconds(80), out var reason));
        Assert.Equal("llm-gap", reason);
        Assert.True(ledger.CanSpeak(ReactionSource.AwarenessLlm, "hades", T0.AddSeconds(90), out _));
    }

    [Fact]
    public void ABarkMayFollowAnLlmLineAfterTheGlobalGapAlone()
    {
        // Barks are free and instant; only the LLM gap is the expensive one.
        var ledger = Chatty();
        ledger.RecordDelivery(ReactionSource.AwarenessLlm, "youtube", T0);

        Assert.True(ledger.CanSpeak(ReactionSource.Bark, "hades", T0.AddSeconds(61), out _));
    }

    [Fact]
    public void SheDoesNotMentionTheSameAppTwiceInsideTenMinutes()
    {
        // This is the one that kills "I see you're on Twitter~" for the fortieth time.
        var ledger = Chatty();
        ledger.RecordDelivery(ReactionSource.AwarenessLlm, "twitter", T0);

        Assert.False(ledger.CanSpeak(ReactionSource.Bark, "twitter", T0.AddMinutes(5), out var reason));
        Assert.Equal("same-app-gap", reason);
        Assert.True(ledger.CanSpeak(ReactionSource.Bark, "twitter", T0.AddMinutes(10), out _));
    }

    [Fact]
    public void ThePerAppGapIsPerAppNotGlobal()
    {
        var ledger = Chatty();
        ledger.RecordDelivery(ReactionSource.AwarenessLlm, "twitter", T0);

        Assert.True(ledger.CanSpeak(ReactionSource.Bark, "hades", T0.AddMinutes(2), out _));
    }

    // ===================== budget =====================

    [Fact]
    public void TheHourlyBudgetIsCountedAcrossEverySource()
    {
        var ledger = Chatty();   // six an hour
        var t = T0;

        for (int i = 0; i < 6; i++)
        {
            var source = i % 2 == 0 ? ReactionSource.Bark : ReactionSource.AwarenessLlm;
            Assert.True(ledger.CanSpeak(source, "app" + i, t, out _), $"line {i} should have been allowed");
            ledger.RecordDelivery(source, "app" + i, t);
            t = t.AddMinutes(2);
        }

        Assert.Equal(6, ledger.LinesLastHour(t));
        Assert.False(ledger.CanSpeak(ReactionSource.Bark, "app99", t, out var reason));
        Assert.Equal("hourly-budget", reason);
    }

    [Fact]
    public void TheBudgetWindowRollsForward()
    {
        var ledger = Chatty();
        var t = T0;
        for (int i = 0; i < 6; i++)
        {
            ledger.RecordDelivery(ReactionSource.Bark, "app" + i, t);
            t = t.AddMinutes(2);
        }

        Assert.Equal(0, ledger.LinesLastHour(T0.AddHours(2)));
        Assert.True(ledger.CanSpeak(ReactionSource.Bark, "app99", T0.AddHours(2), out _));
    }

    [Fact]
    public void IntensityChangesTheBudget()
    {
        Assert.Equal(2, AwarenessIntensityProfile.LinesPerHour(AwarenessIntensity.Subtle));
        Assert.Equal(6, AwarenessIntensityProfile.LinesPerHour(AwarenessIntensity.Chatty));
        Assert.Equal(12, AwarenessIntensityProfile.LinesPerHour(AwarenessIntensity.Unhinged));
        Assert.Equal(0, AwarenessIntensityProfile.LinesPerHour(AwarenessIntensity.Off));
    }

    [Fact]
    public void IntensityOff_SilencesAwarenessButNotAUserConfiguredKeywordTrigger()
    {
        var ledger = new ReactionCooldownLedger(() => AwarenessIntensity.Off);

        Assert.False(ledger.CanSpeak(ReactionSource.AwarenessLlm, "youtube", T0, out var reason));
        Assert.Equal("intensity-off", reason);
        Assert.True(ledger.CanSpeak(ReactionSource.Keyword, "youtube", T0, out _));
    }

    [Fact]
    public void Reset_ClearsEveryGate()
    {
        var ledger = Chatty();
        ledger.RecordDelivery(ReactionSource.AwarenessLlm, "youtube", T0);

        ledger.Reset();

        Assert.True(ledger.CanSpeak(ReactionSource.AwarenessLlm, "youtube", T0.AddSeconds(1), out _));
        Assert.Equal(0, ledger.LinesLastHour(T0));
    }

    // ===================== the arbiter =====================
    // The decision table, the one-reaction-per-frame guarantee and the [PASS]/timeout/staleness paths
    // live in AwarenessArbiterDecisionTests, which drives a fake mouth. These cover the wiring-free
    // arbiter — the shape the observer gets when nothing has been handed to it yet.

    [Fact]
    public async Task AnArbiterWithNoMouthWiredNeverPromisesALineItCannotDeliver()
    {
        var arbiter = new ReactionArbiter(Chatty(), new WorthinessScorer(() => AwarenessIntensity.Chatty), () => T0);
        var frame = new ContextFrame { AppId = "youtube", Tier = RarityTier.Rare };

        var decision = await arbiter.SubmitAsync(frame);

        Assert.NotEqual(AwarenessVerdict.Llm, decision.Verdict);
        Assert.Equal(RarityTier.Common, decision.Tier);
    }

    [Fact]
    public async Task TheArbiterRespectsALineSomethingElseAlreadySpoke()
    {
        var arbiter = new ReactionArbiter(Chatty(), null, () => T0);
        arbiter.RecordExternalLine(ReactionSource.Keyword, "youtube");

        var decision = await arbiter.SubmitAsync(new ContextFrame { AppId = "youtube", Tier = RarityTier.Uncommon });

        Assert.Equal(AwarenessVerdict.Silence, decision.Verdict);
    }

    [Fact]
    public async Task ANullFrameIsSilenceNotAnException()
    {
        var arbiter = new ReactionArbiter(Chatty(), null, () => T0);
        Assert.Equal(AwarenessVerdict.Silence, (await arbiter.SubmitAsync(null!)).Verdict);
    }

    [Fact]
    public void ADeliveredLlmLineAlsoRaisesTheScorersThreshold()
    {
        // One delivery, both pacing systems: otherwise the arbiter and the scorer disagree about how
        // much has already been said and the "silence budget" leaks.
        var scorer = new WorthinessScorer(() => AwarenessIntensity.Chatty);
        var arbiter = new ReactionArbiter(Chatty(), scorer, () => T0);
        double baseline = scorer.CurrentThreshold(T0);

        arbiter.RecordExternalLine(ReactionSource.AwarenessLlm, "youtube");

        Assert.True(scorer.CurrentThreshold(T0) > baseline);
        Assert.True(scorer.RepetitionPenalty("youtube", T0) > 0);
    }

    // ===================== the memory stub =====================

    [Fact]
    public async Task TheStubMemoryHasNoHabitsAndSaysSoQuietly()
    {
        var memory = new StubCompanionMemory();
        Assert.Empty(await memory.GetHabitsAsync("youtube", "site_video"));
    }

    [Fact]
    public async Task TheBanListKeepsTheMostRecentLinesNewestFirst()
    {
        var memory = new StubCompanionMemory();
        for (int i = 0; i < StubCompanionMemory.RingCapacity + 5; i++)
        {
            await memory.RecordReactionAsync(new ReactionSummary("line " + i, "youtube", RarityTier.Uncommon, T0.AddMinutes(i)));
        }

        var recent = await memory.GetRecentReactionsAsync(100);

        Assert.Equal(StubCompanionMemory.RingCapacity, recent.Count);
        Assert.Equal("line " + (StubCompanionMemory.RingCapacity + 4), recent[0].Text);
    }

    [Fact]
    public async Task ForgettingAnAppDropsOnlyThatAppsLines()
    {
        var memory = new StubCompanionMemory();
        await memory.RecordReactionAsync(new ReactionSummary("about youtube", "youtube", RarityTier.Uncommon, T0));
        await memory.RecordReactionAsync(new ReactionSummary("about hades", "hades", RarityTier.Uncommon, T0));

        await memory.ForgetAsync("youtube");
        var recent = await memory.GetRecentReactionsAsync(10);

        Assert.Single(recent);
        Assert.Equal("about hades", recent[0].Text);
    }

    [Fact]
    public async Task WipingErasesTheWholeBanList()
    {
        // Erasure has to be total: a surviving ban list is both a behavioural record the user asked to
        // be rid of AND a source that keeps feeding old lines back into later prompts.
        var memory = new StubCompanionMemory();
        await memory.RecordReactionAsync(new ReactionSummary("about youtube", "youtube", RarityTier.Rare, T0));

        await memory.ForgetAsync(null);

        Assert.Empty(await memory.GetRecentReactionsAsync(10));
    }

    [Fact]
    public async Task ABlankLineIsNeverRecorded()
    {
        var memory = new StubCompanionMemory();
        await memory.RecordReactionAsync(new ReactionSummary("   ", "youtube", RarityTier.Common, T0));

        Assert.Empty(await memory.GetRecentReactionsAsync(10));
    }

    // ===================== observer lifecycle =====================

    [Fact]
    public void TheObserverRefusesToStartWithoutConsent()
    {
        // App.Settings is null headlessly, and a null settings object must read as "no consent" rather
        // than as "sure, go ahead".
        Assert.False(AwarenessObserver.IsEnabled);

        using var ledger = new ActivityLedger(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"), "l.json"),
            () => T0, () => 30);
        using var observer = new AwarenessObserver(ledger, new WorthinessScorer(() => AwarenessIntensity.Chatty),
            new ReactionArbiter(Chatty(), null, () => T0), new StubCompanionMemory(), () => T0);

        observer.Start();

        Assert.False(observer.IsRunning);
        Assert.Equal(0, ledger.AppCount);
    }

    [Fact]
    public void TheObserverRequiresEveryDependency()
    {
        var scorer = new WorthinessScorer(() => AwarenessIntensity.Chatty);
        var arbiter = new ReactionArbiter(Chatty(), null, () => T0);

        Assert.Throws<ArgumentNullException>(() =>
            new AwarenessObserver(null!, scorer, arbiter, new StubCompanionMemory()));
        Assert.Throws<ArgumentNullException>(() =>
            new AwarenessObserver(new ActivityLedger("x.json", () => T0, () => 30), scorer, arbiter, null!));
    }
}
