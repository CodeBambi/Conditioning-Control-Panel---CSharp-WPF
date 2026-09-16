using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BACK ROOM effect recipes (CONTRACT section 4), the authored show (2026-09-15): every fx id always
/// plays its full recipe. No feature toggle and no motion level ever skips a step; the only two safety
/// lines are reduced motion (flash onsets capped at 3 Hz, the slower spiral) and Calm (every opacity
/// halved, every duration kept). Full keeps the counts and stretches durations x1.3. All pure.
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
        MotionLevel m = MotionLevel.Full, BackRoomFxArgs? args = null, params string[] symbols)
        => BackRoomFxPlan.Resolve(fx, i, m, symbols, Deal, new Random(1), args, spiralSource: Woven);

    /// <summary>Every preset has a weave on this desk (a missing one is its own test).</summary>
    internal static string? Woven(string preset) => @"C:\woven\" + preset + ".gif";

    private static FxStep Step(FxPlan p, FxPrim prim) => p.Steps.Single(s => s.Step.Prim == prim).Step;

    /// <summary>The whole word envelope: in 80, hold 400, out 350.</summary>
    private const int Word = 830;

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

    // ---- the authored table ---------------------------------------------------------------------------

    [Fact]
    public void WordEnvelope_IsVisibleThenFades_NotABlink()
    {
        Assert.Equal((80, 400, 350), (BackRoomFxPlan.WordFadeInMs, BackRoomFxPlan.WordHoldMs, BackRoomFxPlan.WordFadeOutMs));
        Assert.Equal(Word, BackRoomFxPlan.WordMs);
        Assert.Equal(500, BackRoomFxPlan.WordGap(FxPrim.SubSingle));
        Assert.Equal(500, BackRoomFxPlan.WordGap(FxPrim.SubSeq));
        Assert.Equal(350, BackRoomFxPlan.WordGap(FxPrim.SubBurst9));
        Assert.Equal(8 * 350 + Word, BackRoomFxPlan.RunMs(9, BackRoomFxPlan.BurstGapMs));
    }

    [Fact]
    public void Jackpot_Normal_IsTheHero()
    {
        var p = Plan("fx.jackpot");
        Assert.Equal(4000, p.HeroMs);
        Assert.Equal(new[] { "spiral-full", "flash-burst", "gif-rain", "glitch-bubbles", "sub-burst9", "sub-burst9", "gif-full" }, p.Fired);
        Assert.Empty(p.Skipped);

        var spiral = Step(p, FxPrim.SpiralFull);
        Assert.Equal((0, 4000, 0.7), (spiral.AtMs, spiral.DurationMs, spiral.Level));
        var flash = Step(p, FxPrim.FlashBurst);
        Assert.Equal((4000, 8, 1.0), (flash.AtMs, flash.Count, flash.Level));
        var rain = Step(p, FxPrim.GifRain);
        Assert.Equal((4000, 4000, 0.9), (rain.AtMs, rain.DurationMs, rain.Level));
        var glitch = Step(p, FxPrim.GlitchBubbles);
        Assert.Equal((4000, 3, 0.35), (glitch.AtMs, glitch.Count, glitch.Level));
        var gif = Step(p, FxPrim.GifFull);
        Assert.Equal((4000, 2000, 0.8), (gif.AtMs, gif.DurationMs, gif.Level));

        var bursts = p.Steps.Where(s => s.Step.Prim == FxPrim.SubBurst9).ToList();
        Assert.Equal(2, bursts.Count);
        Assert.Equal(4000, bursts[0].Step.AtMs);
        Assert.Equal(4000 + 9 * 350, bursts[1].Step.AtMs);
        Assert.All(bursts, b => Assert.Equal(9, b.Words.Count));
        Assert.NotEqual(bursts[0].Words, bursts[1].Words);   // the second run continues, not repeats
    }

    [Fact]
    public void Jackpot_Full_KeepsTheCounts_StretchesTheDurations()
    {
        var p = Plan("fx.jackpot", BackRoomFxIntensity.Full);
        Assert.Equal(5200, p.HeroMs);
        Assert.Equal(7, p.Steps.Count);
        Assert.Equal(5200, Step(p, FxPrim.SpiralFull).DurationMs);
        Assert.Equal(8, Step(p, FxPrim.FlashBurst).Count);
        Assert.Equal(5200, Step(p, FxPrim.GifRain).DurationMs);
        Assert.Equal(2600, Step(p, FxPrim.GifFull).DurationMs);
        Assert.All(p.Steps.Skip(1), s => Assert.True(s.Step.AtMs >= 5200));   // everything else after the spiral
        // Full changes no opacity.
        Assert.Equal(0.7, Step(p, FxPrim.SpiralFull).Level);
        Assert.Equal(1.0, Step(p, FxPrim.FlashBurst).Level);
    }

    [Fact]
    public void Jackpot_Calm_IsGentle_HalfOpacity_SameSteps_SameDurations()
    {
        var calm = Plan("fx.jackpot", BackRoomFxIntensity.Calm);
        var normal = Plan("fx.jackpot");
        Assert.Equal(normal.Fired, calm.Fired);
        Assert.Empty(calm.Skipped);
        Assert.Equal(normal.HeroMs, calm.HeroMs);
        for (int i = 0; i < normal.Steps.Count; i++)
        {
            var n = normal.Steps[i].Step;
            var c = calm.Steps[i].Step;
            Assert.Equal((n.Prim, n.AtMs, n.Count, n.DurationMs), (c.Prim, c.AtMs, c.Count, c.DurationMs));
            Assert.Equal(n.Level * 0.5, c.Level, 9);
        }
    }

    [Fact]
    public void GifStorm_IsFourteenFallingGifs_FiveFlashes_OneGlitchPulse()
    {
        var p = Plan("fx.gif_storm");
        Assert.Equal(new[] { "flash-burst", "gif-rain", "glitch-bubbles" }, p.Fired);
        Assert.Equal((5, 1.0), (Step(p, FxPrim.FlashBurst).Count, Step(p, FxPrim.FlashBurst).Level));
        var rain = Step(p, FxPrim.GifRain);
        Assert.Equal((14, 3000, 0.9), (rain.Count, rain.DurationMs, rain.Level));
        Assert.Equal((1, 0.35), (Step(p, FxPrim.GlitchBubbles).Count, Step(p, FxPrim.GlitchBubbles).Level));
        Assert.All(p.Steps, s => Assert.Equal(0, s.Step.AtMs));
        Assert.Equal(3900, Step(Plan("fx.gif_storm", BackRoomFxIntensity.Full), FxPrim.GifRain).DurationMs);
    }

    [Fact]
    public void GifBurst_IsFiveMediumFlashes_AtFullOpacity()
    {
        var p = Plan("fx.gif_burst");
        var flash = Assert.Single(p.Steps).Step;
        Assert.Equal((FxPrim.FlashBurst, 5, 1.0), (flash.Prim, flash.Count, flash.Level));
        Assert.Equal(0.5, Assert.Single(Plan("fx.gif_burst", BackRoomFxIntensity.Calm).Steps).Step.Level);
        Assert.Equal(5, Assert.Single(Plan("fx.gif_burst", BackRoomFxIntensity.Full).Steps).Step.Count);
        Assert.Equal(100, BackRoomFxPlan.FlashSize);   // ImageScale 100 = medium, whatever the user's slider says
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(8, 8)]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(50, 8)]
    public void GifBurst_ArgsCount_OverridesTheFlashCount_Clamped1To8(int count, int want)
    {
        var p = Plan("fx.gif_burst", args: new BackRoomFxArgs(Count: count));
        Assert.Equal(want, Assert.Single(p.Steps).Step.Count);
        Assert.Equal(want, BackRoomFxPlan.StepMs(p.Steps[0].Step) / BackRoomFxPlan.FlashImageGapMs + 1);
    }

    [Fact]
    public void GifBurst_ArgsCount_OnlyFeedsTheBurst()
    {
        // The slot's "GIF tease" sends count 1 to fx.gif_burst; a count on any other id is ignored.
        Assert.Equal(5, Step(Plan("fx.gif_storm", args: new BackRoomFxArgs(Count: 1)), FxPrim.FlashBurst).Count);
        Assert.Equal(8, Step(Plan("fx.jackpot", args: new BackRoomFxArgs(Count: 1)), FxPrim.FlashBurst).Count);
    }

    [Fact]
    public void SubSingle_IsOneStepPerWord_500Apart_FullOpacity()
    {
        var p = Plan("fx.sub_single", symbols: new[] { "sub0", "sub2" });
        Assert.Equal(new[] { "Drop", "Let Go" }, p.Steps.Select(s => s.Words.Single()));
        Assert.Equal(new[] { 0, 500 }, p.Steps.Select(s => s.Step.AtMs));
        Assert.All(p.Steps, s => Assert.Equal(1.0, s.Step.Level));
        Assert.All(Plan("fx.sub_single", BackRoomFxIntensity.Calm, symbols: new[] { "sub0" }).Steps, s => Assert.Equal(0.5, s.Step.Level));
    }

    [Fact]
    public void SubPair_IsTwoWords_ThenABriefSpiral()
    {
        var p = Plan("fx.sub_pair", symbols: new[] { "sub0", "sub3" });
        Assert.Equal(new[] { "sub-seq", "spiral-full" }, p.Fired);
        var words = p.Steps[0];
        Assert.Equal((0, 2), (words.Step.AtMs, words.Step.Count));
        Assert.Equal(new[] { "Drop", "Sink" }, words.Words);
        var spiral = Step(p, FxPrim.SpiralFull);
        Assert.Equal((500 + Word, 1500, 0.55), (spiral.AtMs, spiral.DurationMs, spiral.Level));
        Assert.Equal(@"C:\woven\screen.gif", p.Steps[1].SpiralPath);
    }

    [Fact]
    public void SubCascade_IsNineWords_350Apart_ThenAFullscreenGif()
    {
        var p = Plan("fx.sub_cascade", symbols: new[] { "sub1", "gif2" });
        Assert.Equal(new[] { "sub-burst9", "gif-full" }, p.Fired);
        Assert.Equal(9, p.Steps[0].Words.Count);
        Assert.Equal("Relax", p.Steps[0].Words[0]);   // the named word first, then the deal cycles
        var gif = Step(p, FxPrim.GifFull);
        Assert.Equal((8 * 350 + Word, 1500, 0.8), (gif.AtMs, gif.DurationMs, gif.Level));
        Assert.Equal("g2", p.Steps[1].Gif!.Key);
        Assert.Equal(1950, Step(Plan("fx.sub_cascade", BackRoomFxIntensity.Full), FxPrim.GifFull).DurationMs);
    }

    [Fact]
    public void WordsShown_SkipsTheWordSteps_AndPlaysOnlyTheRest()
    {
        var shown = new BackRoomFxArgs(WordsShown: true);

        var single = Plan("fx.sub_single", args: shown, symbols: new[] { "sub0", "sub1" });
        Assert.Empty(single.Fired);
        Assert.Empty(single.Skipped);   // nothing to do is not a skip

        var pair = Plan("fx.sub_pair", args: shown, symbols: new[] { "sub0" });
        Assert.Equal(new[] { "spiral-full" }, pair.Fired);
        Assert.Equal(500 + Word, Assert.Single(pair.Steps).Step.AtMs);   // where it would have landed after the words
        Assert.Empty(pair.Skipped);

        var cascade = Plan("fx.sub_cascade", args: shown, symbols: new[] { "gif1" });
        Assert.Equal(new[] { "gif-full" }, cascade.Fired);
        Assert.Equal(8 * 350 + Word, Assert.Single(cascade.Steps).Step.AtMs);
        Assert.Equal("g1", cascade.Steps[0].Gif!.Key);
        Assert.Empty(cascade.Skipped);

        // Only the three word ids read it.
        Assert.Equal(7, Plan("fx.jackpot", args: shown).Steps.Count);
    }

    [Fact]
    public void WordsShown_False_IsTheFullRecipe()
    {
        Assert.Equal(2, Plan("fx.sub_pair", args: new BackRoomFxArgs(WordsShown: false)).Steps.Count);
        Assert.Equal(2, Plan("fx.sub_cascade", args: BackRoomFxArgs.None).Steps.Count);
    }

    [Fact]
    public void Spirals_BriefAndFull_AtTheirAlphas()
    {
        var brief = Assert.Single(Plan("fx.spiral_brief").Steps).Step;
        Assert.Equal((FxPrim.SpiralFull, 1500, 0.55), (brief.Prim, brief.DurationMs, brief.Level));
        var full = Assert.Single(Plan("fx.spiral_full").Steps).Step;
        Assert.Equal((FxPrim.SpiralFull, 4000, 0.7), (full.Prim, full.DurationMs, full.Level));

        var briefFull = Assert.Single(Plan("fx.spiral_brief", BackRoomFxIntensity.Full).Steps).Step;
        Assert.Equal((1950, 0.55), (briefFull.DurationMs, briefFull.Level));
        var fullCalm = Assert.Single(Plan("fx.spiral_full", BackRoomFxIntensity.Calm).Steps).Step;
        Assert.Equal((4000, 0.35), (fullCalm.DurationMs, fullCalm.Level));
    }

    [Fact]
    public void GlitchPulse_Is600msAt035()
    {
        Assert.Equal((600, 0.35), (BackRoomFxPlan.GlitchPulseMs, BackRoomFxPlan.GlitchOpacity));
        Assert.Equal(3 * 600, BackRoomFxPlan.StepMs(Step(Plan("fx.jackpot"), FxPrim.GlitchBubbles)));
    }

    [Fact]
    public void Melt_Is6sRampingTo08()
    {
        var s = Assert.Single(Plan("fx.melt").Steps).Step;
        Assert.Equal((FxPrim.BrainDrainMelt, 6000, 0.8), (s.Prim, s.DurationMs, s.Level));
        var calm = Assert.Single(Plan("fx.melt", BackRoomFxIntensity.Calm).Steps).Step;
        Assert.Equal((6000, 0.4), (calm.DurationMs, calm.Level));
        Assert.Equal(7800, Assert.Single(Plan("fx.melt", BackRoomFxIntensity.Full).Steps).Step.DurationMs);
        // Off and Reduced motion play the same melt (no still blur, no rename).
        Assert.Equal(new[] { "brain-drain-melt" }, Plan("fx.melt", m: MotionLevel.Off).Fired);
        Assert.Equal(new[] { "brain-drain-melt" }, Plan("fx.melt", m: MotionLevel.Reduced).Fired);
    }

    // ---- the two safety lines, and nothing else -------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllCombos))]
    public void EveryId_FiresItsFullRecipe_AtEveryIntensityAndMotion(string fx, BackRoomFxIntensity i, MotionLevel m)
    {
        var p = Plan(fx, i, m, null, "sub1", "gif0");
        var authored = BackRoomFxPlan.Recipe(fx, i, 1)!.Steps.Select(s => BackRoomFxPlan.WireName(s.Prim)).ToList();
        Assert.Equal(authored, p.Fired);
        Assert.Empty(p.Skipped);
        Assert.Equal(i, p.Intensity);   // nothing forces Calm
        Assert.Equal(m != MotionLevel.Full, p.Reduced);
    }

    [Theory]
    [MemberData(nameof(AllCombos))]
    public void Calm_HalvesEveryLevel_AndKeepsEveryDuration_FullOnlyStretches(string fx, BackRoomFxIntensity i, MotionLevel m)
    {
        var normal = BackRoomFxPlan.Recipe(fx, BackRoomFxIntensity.Normal, 2)!;
        var here = BackRoomFxPlan.Recipe(fx, i, 2)!;
        Assert.Equal(normal.Steps.Count, here.Steps.Count);
        for (int k = 0; k < normal.Steps.Count; k++)
        {
            var n = normal.Steps[k];
            var h = here.Steps[k];
            Assert.Equal((n.Prim, n.Count), (h.Prim, h.Count));
            double wantLevel = i == BackRoomFxIntensity.Calm ? n.Level * 0.5 : n.Level;
            Assert.Equal(wantLevel, h.Level, 9);
            if (i != BackRoomFxIntensity.Full) Assert.Equal(n.DurationMs, h.DurationMs);
            else Assert.True(h.DurationMs >= n.DurationMs, $"{fx} {n.Prim}: Full shortened {n.DurationMs} to {h.DurationMs}");
        }
        _ = m;   // motion never touches the recipe; only the plan's Reduced flag
    }

    [Theory]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public void ReducedMotion_OnlyMarksThePlan_TheRecipeIsUntouched(MotionLevel m)
    {
        var reduced = Plan("fx.jackpot", m: m);
        var full = Plan("fx.jackpot");
        Assert.True(reduced.Reduced);
        Assert.False(full.Reduced);
        Assert.Equal(full.Fired, reduced.Fired);
        Assert.Equal(full.Steps.Select(s => s.Step), reduced.Steps.Select(s => s.Step));
        Assert.Equal(334, BackRoomFxPlan.ReducedFlashGapMs);   // the 3 Hz cap the executor applies
    }

    [Fact]
    public void SkipReasons_AreOnlyBusyAndUnknown()
        => Assert.Equal(new[] { BackRoomFxSkipReason.Busy, BackRoomFxSkipReason.Unknown }, Enum.GetValues<BackRoomFxSkipReason>());

    [Fact]
    public void LengthMs_IsTheLastStepsEnd()
    {
        Assert.Equal(4000 + 9 * 350 + 8 * 350 + Word, BackRoomFxPlan.LengthMs(BackRoomFxPlan.Recipe("fx.jackpot", BackRoomFxIntensity.Normal)!));
        Assert.Equal(0, BackRoomFxPlan.LengthMs(new FxRecipe(0, Array.Empty<FxStep>())));
    }
}
