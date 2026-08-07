using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The worthiness scorer and its pacing state (doc 02 §4.1, §3.2, §3.4).
///
/// <para>This is the layer whose misbehaviour is indistinguishable from "the AI is being weird today":
/// too generous and she is spam, too mean and the feature looks broken, and neither shows up in a
/// stack trace. So the weights, the tier ladder, the floating threshold and the per-app repetition
/// penalty are all pinned here, with the clock passed in so a twenty-minute decay curve is exercised
/// in microseconds.</para>
/// </summary>
public class AwarenessScorerTests
{
    private static readonly DateTime T0 = new(2026, 8, 3, 9, 0, 0, DateTimeKind.Local);

    private static WorthinessScorer Chatty() => new(() => AwarenessIntensity.Chatty);

    private static WorthinessInput Input(
        string appId = "youtube",
        bool firstEver = false,
        bool firstToday = false,
        TransitionKind transition = TransitionKind.NewApp,
        int dwell = 0,
        IReadOnlyList<TrendEvent>? trends = null,
        bool session = false,
        bool achievement = false,
        int loginStreak = 0)
        => new(appId, firstEver, firstToday, transition, dwell,
            trends ?? Array.Empty<TrendEvent>(), session, achievement, loginStreak);

    private static TrendEvent Trend(TrendKind kind, int magnitude = 3) =>
        new(kind, "youtube", "site_video", magnitude, magnitude, 20, 1800, TimeSpan.FromMinutes(10));

    // ===================== components =====================

    [Fact]
    public void Novelty_RanksFirstEverAboveFirstTodayAboveSeenRecently()
    {
        Assert.Equal(1.0, WorthinessScorer.Novelty(Input(firstEver: true)));
        Assert.Equal(0.6, WorthinessScorer.Novelty(Input(firstToday: true)));
        Assert.Equal(0.1, WorthinessScorer.Novelty(Input()));
    }

    [Fact]
    public void DwellWeight_IsZeroUnderAMinuteAndOneAtAnHour()
    {
        Assert.Equal(0.0, WorthinessScorer.DwellWeight(59));
        Assert.Equal(0.0, WorthinessScorer.DwellWeight(60));
        Assert.Equal(1.0, WorthinessScorer.DwellWeight(3600));
        Assert.Equal(1.0, WorthinessScorer.DwellWeight(7200));
        Assert.InRange(WorthinessScorer.DwellWeight(600), 0.5, 0.7);   // ten minutes is mid-scale
    }

    [Fact]
    public void DwellWeight_IsMonotonic()
    {
        double previous = -1;
        foreach (var seconds in new[] { 0, 60, 120, 300, 600, 1800, 3000, 3600, 10800 })
        {
            var value = WorthinessScorer.DwellWeight(seconds);
            Assert.True(value >= previous, $"dwell weight went backwards at {seconds}s");
            previous = value;
        }
    }

    [Fact]
    public void TransitionWeight_RanksWakeAndExitFullscreenAboveATabChange()
    {
        Assert.True(WorthinessScorer.TransitionWeight(TransitionKind.WakeFromIdle) >
                    WorthinessScorer.TransitionWeight(TransitionKind.NewApp));
        Assert.True(WorthinessScorer.TransitionWeight(TransitionKind.ExitFullscreen) >
                    WorthinessScorer.TransitionWeight(TransitionKind.NewApp));
        Assert.True(WorthinessScorer.TransitionWeight(TransitionKind.NewApp) >
                    WorthinessScorer.TransitionWeight(TransitionKind.TabChange));
    }

    [Fact]
    public void TrendWeight_TakesTheStrongestAndAddsALittleForTheSecond()
    {
        double single = WorthinessScorer.TrendWeightOf(new[] { Trend(TrendKind.NightShift) });
        double pair = WorthinessScorer.TrendWeightOf(new[] { Trend(TrendKind.NightShift), Trend(TrendKind.ReturnVisit, 4) });

        Assert.Equal(0.90, single, 3);
        Assert.True(pair > single);
        Assert.True(pair <= 1.0);
        Assert.Equal(0.0, WorthinessScorer.TrendWeightOf(Array.Empty<TrendEvent>()));
        Assert.Equal(0.0, WorthinessScorer.TrendWeightOf(null));
    }

    [Fact]
    public void TrendWeight_GrowsWithTheReturnVisitCount()
    {
        double third = WorthinessScorer.TrendWeightOf(new[] { Trend(TrendKind.ReturnVisit, 3) });
        double sixth = WorthinessScorer.TrendWeightOf(new[] { Trend(TrendKind.ReturnVisit, 6) });
        Assert.True(sixth > third);
    }

    [Fact]
    public void InAppBonus_TakesTheStrongestHookNotTheSum()
    {
        Assert.Equal(1.0, WorthinessScorer.InAppBonus(Input(achievement: true, session: true, loginStreak: 30)));
        Assert.Equal(0.6, WorthinessScorer.InAppBonus(Input(session: true, loginStreak: 30)));
        Assert.Equal(0.3, WorthinessScorer.InAppBonus(Input(loginStreak: 3)));
        Assert.Equal(0.0, WorthinessScorer.InAppBonus(Input(loginStreak: 2)));
    }

    // ===================== the formula =====================

    [Fact]
    public void Score_MatchesTheWeightedSumExactly()
    {
        var input = Input(firstEver: true, transition: TransitionKind.WakeFromIdle, dwell: 3600,
            trends: new[] { Trend(TrendKind.Streak, 4) }, session: true);

        var expected =
            WorthinessScorer.NoveltyWeight * 1.0 +
            WorthinessScorer.TrendWeightFactor * 0.70 +
            WorthinessScorer.DwellWeightFactor * 1.0 +
            WorthinessScorer.TransitionWeightFactor * 0.90 +
            WorthinessScorer.InAppWeight * 0.6;

        Assert.Equal(Math.Min(1.0, expected), Chatty().Score(input, T0).Score, 6);
    }

    [Fact]
    public void Score_IsClampedToTheUnitRange()
    {
        var scorer = Chatty();
        var everything = Input(firstEver: true, transition: TransitionKind.WakeFromIdle, dwell: 7200,
            trends: new[] { Trend(TrendKind.NightShift), Trend(TrendKind.MediaLoop), Trend(TrendKind.Backslide) },
            achievement: true);

        Assert.InRange(scorer.Score(everything, T0).Score, 0.0, 1.0);
        Assert.InRange(scorer.Score(Input(transition: TransitionKind.TabChange), T0).Score, 0.0, 1.0);
    }

    [Fact]
    public void Score_IsPure_TwoIdenticalCallsAgree()
    {
        var scorer = Chatty();
        var input = Input(firstToday: true, dwell: 900);

        Assert.Equal(scorer.Score(input, T0).Score, scorer.Score(input, T0).Score, 10);
    }

    // ===================== tier ladder =====================

    [Fact]
    public void ANewAppEverIsWorthAnLlmLine()
    {
        // "She noticed!" is the moment that sells the feature — it must clear the bar on its own.
        var result = Chatty().Score(Input(firstEver: true, dwell: 300), T0);

        Assert.Equal(AwarenessVerdict.Llm, result.Verdict);
        Assert.Equal(RarityTier.Uncommon, result.Tier);
    }

    [Fact]
    public void ALedgerArmedTrendEarnsTheRareTier()
    {
        var result = Chatty().Score(
            Input(firstToday: true, dwell: 1800, trends: new[] { Trend(TrendKind.ReturnVisit, 4) }), T0);

        Assert.Equal(AwarenessVerdict.Llm, result.Verdict);
        Assert.Equal(RarityTier.Rare, result.Tier);
        Assert.Equal("trend-armed", result.Reason);
    }

    [Fact]
    public void AGhostTownGreetingIsNotACallback()
    {
        // GhostTown comes off the idle clock, not the ledger: a good line, not a "third time today".
        var result = Chatty().Score(
            Input(firstEver: true, transition: TransitionKind.WakeFromIdle,
                trends: new[] { Trend(TrendKind.GhostTown, 4) }), T0);

        Assert.Equal(AwarenessVerdict.Llm, result.Verdict);
        Assert.Equal(RarityTier.Uncommon, result.Tier);
    }

    [Fact]
    public void AnOrdinaryTabChangeIsNotWorthASound()
    {
        var result = Chatty().Score(Input(transition: TransitionKind.TabChange, dwell: 300), T0);

        Assert.Equal(AwarenessVerdict.Silence, result.Verdict);
        Assert.Equal(RarityTier.Common, result.Tier);
        Assert.Equal("below-floor", result.Reason);
    }

    [Fact]
    public void AMiddlingMomentIsABark()
    {
        var result = Chatty().Score(Input(firstToday: true, transition: TransitionKind.NewApp), T0);

        Assert.Equal(AwarenessVerdict.Bark, result.Verdict);
        Assert.Equal(RarityTier.Common, result.Tier);
        Assert.InRange(result.Score, WorthinessScorer.BarkFloor, result.Threshold);
    }

    [Fact]
    public void LegendaryIsNeverReturnedInTrain2()
    {
        var scorer = Chatty();
        foreach (var kind in Enum.GetValues<TrendKind>())
        {
            var result = scorer.Score(
                Input(firstEver: true, transition: TransitionKind.WakeFromIdle, dwell: 7200,
                    trends: new[] { Trend(kind, 5) }, achievement: true), T0);
            Assert.NotEqual(RarityTier.Legendary, result.Tier);
        }
    }

    [Fact]
    public void IntensityOff_SilencesEverything()
    {
        var scorer = new WorthinessScorer(() => AwarenessIntensity.Off);
        var result = scorer.Score(Input(firstEver: true, dwell: 7200, trends: new[] { Trend(TrendKind.NightShift) }), T0);

        Assert.Equal(AwarenessVerdict.Silence, result.Verdict);
        Assert.Equal("intensity-off", result.Reason);
    }

    [Fact]
    public void IntensityChangesHowMuchClearsTheBar()
    {
        var input = Input(firstToday: true, dwell: 600, transition: TransitionKind.NewApp);

        Assert.Equal(AwarenessVerdict.Bark, new WorthinessScorer(() => AwarenessIntensity.Subtle).Score(input, T0).Verdict);
        Assert.Equal(AwarenessVerdict.Llm, new WorthinessScorer(() => AwarenessIntensity.Unhinged).Score(input, T0).Verdict);
    }

    // ===================== floating threshold =====================

    [Fact]
    public void ADeliveredLineRaisesTheThreshold()
    {
        var scorer = Chatty();
        double baseline = scorer.CurrentThreshold(T0);

        scorer.RegisterDelivery("youtube", T0);

        Assert.True(scorer.CurrentThreshold(T0) > baseline);
        Assert.Equal(baseline + WorthinessScorer.ThresholdBump, scorer.CurrentThreshold(T0), 6);
    }

    [Fact]
    public void TheThresholdDecaysBackToBaselineOverAboutTwentyMinutes()
    {
        var scorer = Chatty();
        double baseline = scorer.CurrentThreshold(T0);
        scorer.RegisterDelivery("youtube", T0);

        double afterSeven = scorer.CurrentThreshold(T0.AddMinutes(7));
        double afterTwenty = scorer.CurrentThreshold(T0.AddMinutes(20));

        // One half-life ≈ half the bump left; by twenty minutes it is noise.
        Assert.Equal(baseline + WorthinessScorer.ThresholdBump / 2, afterSeven, 3);
        Assert.InRange(afterTwenty - baseline, 0.0, 0.03);
    }

    [Fact]
    public void ABurstOfLinesCannotPushTheThresholdUnbounded()
    {
        var scorer = Chatty();
        double baseline = scorer.CurrentThreshold(T0);

        for (int i = 0; i < 20; i++) scorer.RegisterDelivery("youtube", T0);

        Assert.InRange(scorer.CurrentThreshold(T0) - baseline, 0.0, WorthinessScorer.MaxThresholdBump + 1e-9);
    }

    [Fact]
    public void TheRaisedThresholdActuallySuppressesTheNextMarginalLine()
    {
        var scorer = Chatty();
        var input = Input(firstEver: true, dwell: 300, appId: "hades");
        Assert.Equal(AwarenessVerdict.Llm, scorer.Score(input, T0).Verdict);

        scorer.RegisterDelivery("vscode", T0);   // a line about something else

        Assert.NotEqual(AwarenessVerdict.Llm, scorer.Score(input, T0.AddSeconds(1)).Verdict);
    }

    // ===================== repetition penalty =====================

    [Fact]
    public void RepetitionPenalty_IsZeroForAnAppSheHasNotMentioned()
        => Assert.Equal(0.0, Chatty().RepetitionPenalty("youtube", T0));

    [Fact]
    public void RepetitionPenalty_GrowsWithEachLineAboutTheSameApp()
    {
        var scorer = Chatty();
        scorer.RegisterDelivery("youtube", T0);
        double one = scorer.RepetitionPenalty("youtube", T0);

        scorer.RegisterDelivery("youtube", T0);
        double two = scorer.RepetitionPenalty("youtube", T0);

        Assert.InRange(one, 0.4, 0.6);
        Assert.True(two > one);
        Assert.True(two < 1.0);
    }

    [Fact]
    public void RepetitionPenalty_IsPerAppNotGlobal()
    {
        var scorer = Chatty();
        scorer.RegisterDelivery("youtube", T0);

        Assert.True(scorer.RepetitionPenalty("youtube", T0) > 0);
        Assert.Equal(0.0, scorer.RepetitionPenalty("hades", T0));
    }

    [Fact]
    public void RepetitionPenalty_DecaysAwayOverTheAfternoon()
    {
        var scorer = Chatty();
        scorer.RegisterDelivery("youtube", T0);

        double half = scorer.RepetitionPenalty("youtube", T0.AddMinutes(30));
        double later = scorer.RepetitionPenalty("youtube", T0.AddHours(4));

        Assert.True(half < scorer.RepetitionPenalty("youtube", T0));
        Assert.InRange(later, 0.0, 0.05);
    }

    [Fact]
    public void Forget_DropsOneAppsRepetitionHistory()
    {
        var scorer = Chatty();
        scorer.RegisterDelivery("youtube", T0);
        scorer.RegisterDelivery("hades", T0);

        scorer.Forget("youtube");

        Assert.Equal(0.0, scorer.RepetitionPenalty("youtube", T0));
        Assert.True(scorer.RepetitionPenalty("hades", T0) > 0);
    }

    [Fact]
    public void Reset_ClearsEveryPacingArtifact()
    {
        var scorer = Chatty();
        double baseline = scorer.CurrentThreshold(T0);
        scorer.RegisterDelivery("youtube", T0);

        scorer.Reset();

        Assert.Equal(baseline, scorer.CurrentThreshold(T0), 9);
        Assert.Equal(0.0, scorer.RepetitionPenalty("youtube", T0));
    }

    // ===================== the [AWARE] line =====================

    [Fact]
    public void EveryScoredEventProducesOneDecisionLine()
    {
        var result = Chatty().Score(Input(firstEver: true, dwell: 600), T0);

        Assert.StartsWith("[AWARE] ", result.LogLine);
        Assert.Contains("score=", result.LogLine);
        Assert.Contains("verdict=Llm", result.LogLine);
        Assert.Contains("tier=Uncommon", result.LogLine);
        Assert.Contains("gate=", result.LogLine);
        Assert.DoesNotContain("\n", result.LogLine);
    }

    [Fact]
    public void TheDecisionLineCannotBeForgedByAModSuppliedAppId()
    {
        // app_clusters.json is mod-overridable, so an app id is attacker-authored text and must not be
        // able to inject a second log line or impersonate another field.
        var result = Chatty().Score(Input(appId: "evil\nverdict=Llm score=1.0"), T0);

        Assert.Single(result.LogLine.Split('\n'));
        Assert.Contains("app=evil_verdict_llm_score_1.0", result.LogLine);
    }
}
