using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BACK ROOM effect recipes (CONTRACT section 4): intensity x MotionLevel x toggles, resolved
/// purely. Every skip the host reports in an fx-ack comes out of this resolver, so the ack is only
/// as honest as these rules.
/// </summary>
public class BackRoomFxPlanTests
{
    private static readonly BackRoomMediaDeal Deal = new(7,
        new[]
        {
            new BackRoomGif("g0", "https://ccp.assets/images/a.gif", 480, 270, "pool"),
            new BackRoomGif("g1", "https://ccp.assets/images/b.gif", 480, 270, "pool"),
            new BackRoomGif("g2", "https://ccp.assets/images/c.gif", 480, 270, "pool"),
            new BackRoomGif("g3", "https://ccp.game/backroom/stations/slot/fallback/gif3.webp", 0, 0, "fallback"),
        },
        new[]
        {
            new BackRoomWord("s0", "Drop", "preset"),
            new BackRoomWord("s1", "Relax", "preset"),
            new BackRoomWord("s2", "Let Go", "preset"),
            new BackRoomWord("s3", "Sink", "preset"),
        });

    private static FxPlan Plan(string fx, BackRoomFxIntensity i = BackRoomFxIntensity.Normal,
        MotionLevel m = MotionLevel.Full, FxGates? g = null, params string[] symbols)
        => BackRoomFxPlan.Resolve(fx, i, m, g ?? FxGates.AllOn, symbols, Deal, new Random(1));

    private static BackRoomFxSkipReason? Why(FxPlan p, string prim)
        => p.Skipped.FirstOrDefault(s => s.Prim == prim)?.Why;

    public static IEnumerable<object[]> AllCombos()
    {
        foreach (var fx in BackRoomFxPlan.KnownIds)
            foreach (BackRoomFxIntensity i in Enum.GetValues(typeof(BackRoomFxIntensity)))
                foreach (MotionLevel m in Enum.GetValues(typeof(MotionLevel)))
                    yield return new object[] { fx, i, m };
    }

    [Fact]
    public void EveryContractId_HasARecipe_AtEveryIntensity()
    {
        foreach (var fx in BackRoomFxPlan.KnownIds)
            foreach (BackRoomFxIntensity i in Enum.GetValues(typeof(BackRoomFxIntensity)))
                Assert.NotNull(BackRoomFxPlan.Recipe(fx, i));
    }

    [Fact]
    public void UnknownId_IsOneUnknownSkip_AndNothingFires()
    {
        var p = Plan("fx.confetti");
        Assert.Empty(p.Fired);
        Assert.Equal(new[] { new BackRoomFxSkip("fx.confetti", BackRoomFxSkipReason.Unknown) }, p.Skipped);
    }

    [Fact]
    public void Jackpot_Normal_IsTheDesignDocRecipe()
    {
        var p = Plan("fx.jackpot");
        Assert.Equal(2400, p.HeroMs);
        Assert.Equal(new[] { "spiral-full", "flash-burst", "gif-rain", "glitch-bubbles", "sub-burst9" }, p.Fired);
        Assert.Equal(4, p.Steps.Single(s => s.Step.Prim == FxPrim.FlashBurst).Step.Count);
        Assert.All(p.Steps.Skip(1), s => Assert.Equal(2400, s.Step.AtMs));   // "then", after the spiral
        Assert.Empty(p.Skipped);
    }

    [Fact]
    public void Jackpot_Full_HasTwoBursts_OneAfterTheOther_AndTheGifFull()
    {
        var p = Plan("fx.jackpot", BackRoomFxIntensity.Full);
        Assert.Equal(4000, p.HeroMs);
        var bursts = p.Steps.Where(s => s.Step.Prim == FxPrim.SubBurst9).ToList();
        Assert.Equal(2, bursts.Count);
        Assert.Equal(bursts[0].Step.AtMs + 9 * BackRoomFxPlan.WordGapMs, bursts[1].Step.AtMs);
        Assert.NotEqual(bursts[0].Words, bursts[1].Words);   // the second run continues, not repeats
        Assert.Contains("gif-full", p.Fired);
    }

    [Fact]
    public void Jackpot_Calm_ReportsWhatCalmLeftOut()
    {
        var p = Plan("fx.jackpot", BackRoomFxIntensity.Calm);
        Assert.Equal(new[] { "spiral-full", "gif-full", "sub-single" }, p.Fired);
        foreach (var prim in new[] { "flash-burst", "gif-rain", "glitch-bubbles", "sub-burst9" })
            Assert.Equal(BackRoomFxSkipReason.Calm, Why(p, prim));
    }

    [Theory]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public void BelowFullMotion_CalmIsForced_WhateverTheSetting(MotionLevel m)
    {
        Assert.Equal(BackRoomFxIntensity.Calm, Plan("fx.gif_storm", BackRoomFxIntensity.Full, m).Intensity);
        Assert.Equal(BackRoomFxIntensity.Calm, BackRoomFxPlan.EffectiveIntensity(BackRoomFxIntensity.Normal, m));
    }

    [Fact]
    public void FullMotion_KeepsTheSetting()
        => Assert.Equal(BackRoomFxIntensity.Full, BackRoomFxPlan.EffectiveIntensity(BackRoomFxIntensity.Full, MotionLevel.Full));

    [Fact]
    public void SpiralCalm_IsHalfOpacity()
    {
        Assert.Equal(0.5, Plan("fx.spiral_full", BackRoomFxIntensity.Calm).Steps.Single().Step.Level);
        Assert.Equal(1.0, Plan("fx.spiral_full").Steps.Single().Step.Level);
        Assert.Equal(4000, Plan("fx.spiral_full", BackRoomFxIntensity.Full).Steps.Single().Step.DurationMs);
    }

    [Fact]
    public void MotionRules_PerPrimitive()
    {
        var gates = FxGates.AllOn;
        FxStep? Apply(FxPrim p, MotionLevel m, int count = 4)
            => BackRoomFxPlan.ApplyMotion(new FxStep(p, 0, count, 1000), m, gates, out _);

        Assert.Equal(1, Apply(FxPrim.FlashBurst, MotionLevel.Off)!.Count);
        Assert.Equal(4, Apply(FxPrim.FlashBurst, MotionLevel.Reduced)!.Count);
        Assert.Null(Apply(FxPrim.GifRain, MotionLevel.Reduced));
        Assert.Null(Apply(FxPrim.GifRain, MotionLevel.Off));
        Assert.NotNull(Apply(FxPrim.GlitchBubbles, MotionLevel.Reduced));
        Assert.Null(Apply(FxPrim.GlitchBubbles, MotionLevel.Off));
        Assert.NotNull(Apply(FxPrim.SubSingle, MotionLevel.Off));
        var reducedBurst = Apply(FxPrim.SubBurst9, MotionLevel.Reduced, 9)!;
        Assert.Equal((FxPrim.SubSeq, 2), (reducedBurst.Prim, reducedBurst.Count));
        Assert.True(Apply(FxPrim.SpiralFull, MotionLevel.Off)!.Still);
        Assert.False(Apply(FxPrim.SpiralFull, MotionLevel.Reduced)!.Still);
        Assert.Equal(FxPrim.BrainDrain, Apply(FxPrim.BrainDrainMelt, MotionLevel.Off)!.Prim);
        Assert.Equal(FxPrim.BrainDrainMelt, Apply(FxPrim.BrainDrainMelt, MotionLevel.Reduced)!.Prim);
        Assert.True(Apply(FxPrim.GifFull, MotionLevel.Off)!.Still);
    }

    [Fact]
    public void SpiralAtOff_WithNoStillFrameAvailable_IsAMotionSkip()
    {
        var p = Plan("fx.spiral_full", m: MotionLevel.Off, g: FxGates.AllOn with { SpiralStill = false });
        Assert.Empty(p.Fired);
        Assert.Equal(BackRoomFxSkipReason.Motion, Why(p, "spiral-full"));
    }

    [Fact]
    public void Toggles_AlwaysWin_AndAreReported()
    {
        var noFlash = Plan("fx.gif_storm", g: FxGates.AllOn with { Flash = false });
        Assert.Empty(noFlash.Fired);
        Assert.Equal(BackRoomFxSkipReason.Toggle, Why(noFlash, "flash-burst"));
        Assert.Equal(BackRoomFxSkipReason.Toggle, Why(noFlash, "gif-rain"));
        Assert.Equal(BackRoomFxSkipReason.Toggle, Why(noFlash, "glitch-bubbles"));

        var noSubs = Plan("fx.sub_cascade", g: FxGates.AllOn with { Subliminal = false });
        Assert.Equal(new[] { "gif-full" }, noSubs.Fired);
        Assert.Equal(BackRoomFxSkipReason.Toggle, Why(noSubs, "sub-burst9"));

        var noSpiral = Plan("fx.sub_pair", g: FxGates.AllOn with { Spiral = false });
        Assert.Equal(new[] { "sub-seq" }, noSpiral.Fired);
        Assert.Equal(BackRoomFxSkipReason.Toggle, Why(noSpiral, "spiral-full"));
    }

    [Fact]
    public void Melt_BrainDrainOff_SkipsWhole()
    {
        var p = Plan("fx.melt", g: FxGates.AllOn with { BrainDrain = false });
        Assert.Empty(p.Fired);
        Assert.Equal(BackRoomFxSkipReason.Toggle, Why(p, "brain-drain-melt"));
    }

    [Fact]
    public void Melt_MeltToggleOff_PlaysTheBlurOnly_AndReportsTheDrip()
    {
        var p = Plan("fx.melt", g: FxGates.AllOn with { Melt = false });
        Assert.Equal(new[] { "brain-drain" }, p.Fired);
        Assert.Equal(BackRoomFxSkipReason.Toggle, Why(p, "brain-drain-melt"));
        Assert.Equal(6000, p.Steps.Single().Step.DurationMs);
    }

    [Fact]
    public void Melt_AtOff_IsBlurWithoutDrip_ReportedAsMotion()
    {
        var p = Plan("fx.melt", m: MotionLevel.Off);
        Assert.Equal(new[] { "brain-drain" }, p.Fired);
        Assert.Equal(BackRoomFxSkipReason.Motion, Why(p, "brain-drain-melt"));
        Assert.Equal(0.5, p.Steps.Single().Step.Level);   // Calm forced: half intensity, 4 s
        Assert.Equal(4000, p.Steps.Single().Step.DurationMs);
    }

    [Theory]
    [MemberData(nameof(AllCombos))]
    public void NoCombination_FiresADisabledFeature(string fx, BackRoomFxIntensity i, MotionLevel m)
    {
        var none = new FxGates(false, false, false, false, false);
        var p = Plan(fx, i, m, none, "sub0", "gif1");
        Assert.Empty(p.Fired);
        Assert.NotEmpty(p.Skipped);
    }

    [Theory]
    [MemberData(nameof(AllCombos))]
    public void EveryAuthoredPrimitive_EitherFiresOrIsReported(string fx, BackRoomFxIntensity i, MotionLevel m)
    {
        foreach (var gates in new[] { FxGates.AllOn, new FxGates(true, false, true, false, false), new FxGates(false, true, false, true, true) })
        {
            var p = Plan(fx, i, m, gates, "sub1");
            var normal = BackRoomFxPlan.Recipe(fx, BackRoomFxIntensity.Normal, 1)!.Steps.Select(s => BackRoomFxPlan.WireName(s.Prim));
            var effective = BackRoomFxPlan.Recipe(fx, p.Intensity, 1)!.Steps.Select(s => BackRoomFxPlan.WireName(s.Prim));
            var accounted = p.Fired.Concat(p.Skipped.Select(s => s.Prim)).ToHashSet();
            // Motion may rename (sub-burst9 -> sub-seq, melt -> brain-drain); those renames are
            // reported under the authored name or fire under the new one.
            foreach (var prim in effective.Concat(p.Intensity == BackRoomFxIntensity.Calm ? normal : Array.Empty<string>()))
                Assert.True(accounted.Contains(prim)
                            || (prim == "sub-burst9" && accounted.Contains("sub-seq")),
                    $"{fx} {i} {m}: {prim} neither fired nor reported");
        }
    }

    [Fact]
    public void SubSingle_IsOneStepPerWord_SpacedByTheWordGap()
    {
        var p = Plan("fx.sub_single", symbols: new[] { "sub0", "sub2" });
        Assert.Equal(new[] { "Drop", "Let Go" }, p.Steps.Select(s => s.Words.Single()));
        Assert.Equal(new[] { 0, BackRoomFxPlan.WordGapMs }, p.Steps.Select(s => s.Step.AtMs));
    }
}
