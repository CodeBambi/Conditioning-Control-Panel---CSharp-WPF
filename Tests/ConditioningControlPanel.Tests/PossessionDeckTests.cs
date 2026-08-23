using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Possession (the haunted-UI layer of Lockdown) - the rules the director enforces live in
/// PossessionDeck precisely so they can be pinned here: ladder bands, intensity caps, cadence,
/// concurrency, weighting and the eligibility filters (cooldown, already-possessed, never the same
/// victim twice in a row, no flicker when photosafe, no Full-Doki-only effect at Eerie).
/// See Services/Possession/POSSESSION.md.
/// </summary>
public class PossessionDeckTests
{
    // A Random whose rolls we choose, so range endpoints are testable instead of statistical.
    private sealed class StubRandom : Random
    {
        private readonly double _next;
        private readonly int _pick;
        public StubRandom(double next, int pick = 0) { _next = next; _pick = pick; }
        public override double NextDouble() => _next;
        public override int Next(int maxValue) => Math.Min(_pick, Math.Max(0, maxValue - 1));
    }

    private static PossessionEffectMeta Effect(
        string id,
        PossessionRung minRung = PossessionRung.Settle,
        PossessionIntensity minIntensity = PossessionIntensity.Gentle,
        bool flicker = false,
        double weight = 1.0,
        PossessionRole[]? roles = null)
        => new(id, minRung, minIntensity, flicker, weight, roles ?? Array.Empty<PossessionRole>());

    private static PossessionTargetMeta Target(string key, PossessionRole role = PossessionRole.Button, bool live = false, bool cooldown = false)
        => new(key, role, live, cooldown);

    // ---------------------------------------------------------------------------------------------
    //  The ladder
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.0, PossessionRung.Settle)]
    [InlineData(0.09, PossessionRung.Settle)]
    [InlineData(0.10, PossessionRung.Drift)]     // bands are lower-inclusive
    [InlineData(0.34, PossessionRung.Drift)]
    [InlineData(0.35, PossessionRung.Melt)]
    [InlineData(0.59, PossessionRung.Melt)]
    [InlineData(0.60, PossessionRung.Collapse)]
    [InlineData(0.84, PossessionRung.Collapse)]
    public void RungFor_WalksTheBands(double fraction, PossessionRung expected)
        => Assert.Equal(expected, PossessionDeck.RungFor(fraction, PossessionIntensity.FullDoki));

    [Theory]
    [InlineData(0.85)]
    [InlineData(0.99)]
    [InlineData(1.0)]
    [InlineData(5.0)]   // clamped
    public void RungFor_TopBandIsItKnows_OnFullDoki(double fraction)
        => Assert.Equal(PossessionRung.ItKnows, PossessionDeck.RungFor(fraction, PossessionIntensity.FullDoki));

    [Fact]
    public void RungFor_GentleNeverPassesMelt()
    {
        Assert.Equal(PossessionRung.Melt, PossessionDeck.RungFor(0.60, PossessionIntensity.Gentle));
        Assert.Equal(PossessionRung.Melt, PossessionDeck.RungFor(0.85, PossessionIntensity.Gentle));
        Assert.Equal(PossessionRung.Melt, PossessionDeck.RungFor(1.0, PossessionIntensity.Gentle));
        // ...but the rungs below the cap are still walked normally.
        Assert.Equal(PossessionRung.Drift, PossessionDeck.RungFor(0.10, PossessionIntensity.Gentle));
    }

    [Fact]
    public void RungFor_EerieCapsAtCollapse()
    {
        Assert.Equal(PossessionRung.Collapse, PossessionDeck.RungFor(0.85, PossessionIntensity.Eerie));
        Assert.Equal(PossessionRung.Collapse, PossessionDeck.RungFor(1.0, PossessionIntensity.Eerie));
        Assert.Equal(PossessionRung.Melt, PossessionDeck.RungFor(0.35, PossessionIntensity.Eerie));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void RungFor_GarbageFractionsSettle(double fraction)
        => Assert.Equal(PossessionRung.Settle, PossessionDeck.RungFor(fraction, PossessionIntensity.FullDoki));

    [Fact]
    public void CapFor_MatchesTheOwnerLockedPresets()
    {
        Assert.Equal(PossessionRung.Melt, PossessionDeck.CapFor(PossessionIntensity.Gentle));
        Assert.Equal(PossessionRung.Collapse, PossessionDeck.CapFor(PossessionIntensity.Eerie));
        Assert.Equal(PossessionRung.ItKnows, PossessionDeck.CapFor(PossessionIntensity.FullDoki));
    }

    // ---------------------------------------------------------------------------------------------
    //  Concurrency
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(PossessionRung.Settle, 2)]
    [InlineData(PossessionRung.Drift, 2)]
    [InlineData(PossessionRung.Melt, 3)]
    [InlineData(PossessionRung.Collapse, 4)]
    [InlineData(PossessionRung.ItKnows, 4)]
    public void MaxLive_TightensEarly_LoosensLate(PossessionRung rung, int expected)
        => Assert.Equal(expected, PossessionDeck.MaxLive(rung));

    // ---------------------------------------------------------------------------------------------
    //  Cadence
    // ---------------------------------------------------------------------------------------------

    // Wave 2 (density) ladder - POSSESSION.md "The ladder". These five rows ARE the feature's pace;
    // the owner play-test that produced them called the wave-1 numbers "not dense, not impressive".
    [Theory]
    [InlineData(PossessionRung.Settle, 20, 30)]
    [InlineData(PossessionRung.Drift, 12, 18)]
    [InlineData(PossessionRung.Melt, 8, 12)]
    [InlineData(PossessionRung.Collapse, 5, 8)]
    [InlineData(PossessionRung.ItKnows, 4, 6)]
    public void NextDelay_EerieBaseRanges(PossessionRung rung, double min, double max)
    {
        Assert.Equal(min, PossessionDeck.NextDelay(rung, PossessionIntensity.Eerie, new StubRandom(0.0)).TotalSeconds, 3);
        Assert.Equal(max, PossessionDeck.NextDelay(rung, PossessionIntensity.Eerie, new StubRandom(1.0)).TotalSeconds, 3);
    }

    [Fact]
    public void NextDelay_GentleDoubles_FullDokiTightens()
    {
        var eerie = PossessionDeck.NextDelay(PossessionRung.Melt, PossessionIntensity.Eerie, new StubRandom(0.0)).TotalSeconds;
        var gentle = PossessionDeck.NextDelay(PossessionRung.Melt, PossessionIntensity.Gentle, new StubRandom(0.0)).TotalSeconds;
        var doki = PossessionDeck.NextDelay(PossessionRung.Melt, PossessionIntensity.FullDoki, new StubRandom(0.0)).TotalSeconds;

        Assert.Equal(8, eerie, 3);
        Assert.Equal(16, gentle, 3);      // double the wait
        Assert.Equal(6.4, doki, 3);       // 0.8x
    }

    [Fact]
    public void NextDelay_StaysInsideTheBand_ForRealRolls()
    {
        var rng = new Random(1234);
        for (int i = 0; i < 500; i++)
        {
            var d = PossessionDeck.NextDelay(PossessionRung.Collapse, PossessionIntensity.Eerie, rng).TotalSeconds;
            Assert.InRange(d, 5, 8);
        }
    }

    [Fact]
    public void FirstDelay_NeverSoonerThanTheFirstWait()
    {
        // R4 Full Doki rolls 3.2s at the low end - the room still gets its 20s to settle.
        var first = PossessionDeck.FirstDelay(PossessionRung.ItKnows, PossessionIntensity.FullDoki, new StubRandom(0.0));
        Assert.Equal(20, first.TotalSeconds, 3);
        Assert.Equal(PossessionDeck.FirstWait, first);
    }

    [Fact]
    public void FirstDelay_KeepsTheLongerCadenceWhenItIsLonger()
    {
        // R0 Gentle is 40s+, so the 20s floor must not shorten it.
        var first = PossessionDeck.FirstDelay(PossessionRung.Settle, PossessionIntensity.Gentle, new StubRandom(0.0));
        Assert.Equal(40, first.TotalSeconds, 3);
    }

    [Fact]
    public void TargetCooldown_IsTheWave2FortyFive()
        => Assert.Equal(45, PossessionDeck.TargetCooldown.TotalSeconds, 3);

    // ---------------------------------------------------------------------------------------------
    //  Weighting
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void WeightOf_IsZeroBelowMinRung()
    {
        var e = Effect("melt", PossessionRung.Melt, weight: 3.0);
        Assert.Equal(0, PossessionDeck.WeightOf(e, PossessionRung.Settle));
        Assert.Equal(0, PossessionDeck.WeightOf(e, PossessionRung.Drift));
        Assert.Equal(3.0, PossessionDeck.WeightOf(e, PossessionRung.Melt), 6);
    }

    [Fact]
    public void WeightOf_GainsFiftyPercentPerRungAbove_CappedAtDouble()
    {
        var e = Effect("tic", PossessionRung.Settle, weight: 2.0);
        Assert.Equal(2.0, PossessionDeck.WeightOf(e, PossessionRung.Settle), 6);
        Assert.Equal(3.0, PossessionDeck.WeightOf(e, PossessionRung.Drift), 6);      // +50%
        Assert.Equal(4.0, PossessionDeck.WeightOf(e, PossessionRung.Melt), 6);       // +100%
        Assert.Equal(4.0, PossessionDeck.WeightOf(e, PossessionRung.Collapse), 6);   // capped
        Assert.Equal(4.0, PossessionDeck.WeightOf(e, PossessionRung.ItKnows), 6);    // still capped
    }

    // ---------------------------------------------------------------------------------------------
    //  Eligibility
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EffectEligible_RespectsMinRung()
    {
        var e = Effect("fall", PossessionRung.Collapse);
        Assert.False(PossessionDeck.EffectEligible(e, PossessionRung.Melt, PossessionIntensity.FullDoki, photosafe: false));
        Assert.True(PossessionDeck.EffectEligible(e, PossessionRung.Collapse, PossessionIntensity.FullDoki, photosafe: false));
    }

    [Fact]
    public void EffectEligible_SkipsIntensityAboveTheCurrentPreset()
    {
        var doki = Effect("fake_crash", PossessionRung.Settle, PossessionIntensity.FullDoki);
        Assert.False(PossessionDeck.EffectEligible(doki, PossessionRung.ItKnows, PossessionIntensity.Gentle, photosafe: false));
        Assert.False(PossessionDeck.EffectEligible(doki, PossessionRung.ItKnows, PossessionIntensity.Eerie, photosafe: false));
        Assert.True(PossessionDeck.EffectEligible(doki, PossessionRung.ItKnows, PossessionIntensity.FullDoki, photosafe: false));
    }

    [Fact]
    public void EffectEligible_SkipsFlickerWhenPhotosafe()
    {
        var blink = Effect("blink", flicker: true);
        Assert.True(PossessionDeck.EffectEligible(blink, PossessionRung.Drift, PossessionIntensity.Eerie, photosafe: false));
        Assert.False(PossessionDeck.EffectEligible(blink, PossessionRung.Drift, PossessionIntensity.Eerie, photosafe: true));

        var calm = Effect("drift", flicker: false);
        Assert.True(PossessionDeck.EffectEligible(calm, PossessionRung.Drift, PossessionIntensity.Eerie, photosafe: true));
    }

    [Fact]
    public void TargetEligible_SkipsLive_Cooldown_AndTheLastVictim()
    {
        var e = Effect("nudge", roles: new[] { PossessionRole.Button });

        Assert.True(PossessionDeck.TargetEligible(Target("btnStart"), e, lastTargetKey: null));
        Assert.False(PossessionDeck.TargetEligible(Target("btnStart", live: true), e, null));
        Assert.False(PossessionDeck.TargetEligible(Target("btnStart", cooldown: true), e, null));
        Assert.False(PossessionDeck.TargetEligible(Target("btnStart"), e, lastTargetKey: "btnStart"));
        Assert.True(PossessionDeck.TargetEligible(Target("btnStop"), e, lastTargetKey: "btnStart"));
    }

    [Fact]
    public void TargetEligible_RespectsRoles()
    {
        var cardOnly = Effect("fall", roles: new[] { PossessionRole.Card });
        Assert.False(PossessionDeck.TargetEligible(Target("btn", PossessionRole.Button), cardOnly, null));
        Assert.True(PossessionDeck.TargetEligible(Target("card", PossessionRole.Card), cardOnly, null));

        // An effect with no roles needs no victim, so nothing matches it here.
        var targetless = Effect("retitle");
        Assert.False(PossessionDeck.TargetEligible(Target("card", PossessionRole.Card), targetless, null));
    }

    [Fact]
    public void EligibleTargets_ReturnsIndexes()
    {
        var e = Effect("nudge", roles: new[] { PossessionRole.Button }, minRung: PossessionRung.Settle);
        var targets = new List<PossessionTargetMeta>
        {
            Target("a", PossessionRole.Button),
            Target("b", PossessionRole.Card),
            Target("c", PossessionRole.Button, cooldown: true),
            Target("d", PossessionRole.Button),
        };
        Assert.Equal(new[] { 0, 3 }, PossessionDeck.EligibleTargets(e, targets, lastTargetKey: null));
        Assert.Equal(new[] { 3 }, PossessionDeck.EligibleTargets(e, targets, lastTargetKey: "a"));
    }

    // ---------------------------------------------------------------------------------------------
    //  The pick
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Pick_ReturnsNull_WhenNothingMayRun()
    {
        var effects = new List<PossessionEffectMeta> { Effect("fall", PossessionRung.Collapse, roles: new[] { PossessionRole.Card }) };
        var targets = new List<PossessionTargetMeta> { Target("card", PossessionRole.Card) };

        // Below the effect's MinRung.
        Assert.Null(PossessionDeck.Pick(effects, targets, PossessionRung.Drift, PossessionIntensity.FullDoki, false, null, new Random(1)));
        // Right rung, but the only victim is on cooldown.
        var cooling = new List<PossessionTargetMeta> { Target("card", PossessionRole.Card, cooldown: true) };
        Assert.Null(PossessionDeck.Pick(effects, cooling, PossessionRung.Collapse, PossessionIntensity.FullDoki, false, null, new Random(1)));
        // Empty deck.
        Assert.Null(PossessionDeck.Pick(new List<PossessionEffectMeta>(), targets, PossessionRung.Collapse, PossessionIntensity.FullDoki, false, null, new Random(1)));
    }

    [Fact]
    public void Pick_TargetlessEffect_ReportsMinusOne()
    {
        var effects = new List<PossessionEffectMeta> { Effect("retitle") };
        var pick = PossessionDeck.Pick(effects, new List<PossessionTargetMeta>(), PossessionRung.Drift,
            PossessionIntensity.Eerie, false, null, new Random(7));

        Assert.NotNull(pick);
        Assert.Equal(0, pick!.Value.EffectIndex);
        Assert.Equal(-1, pick.Value.TargetIndex);
    }

    [Fact]
    public void Pick_NeverReturnsTheSameVictimTwiceInARow()
    {
        var effects = new List<PossessionEffectMeta> { Effect("nudge", roles: new[] { PossessionRole.Button }) };
        var targets = new List<PossessionTargetMeta> { Target("a"), Target("b") };
        var rng = new Random(99);

        for (int i = 0; i < 200; i++)
        {
            var pick = PossessionDeck.Pick(effects, targets, PossessionRung.Drift, PossessionIntensity.Eerie, false, "a", rng);
            Assert.NotNull(pick);
            Assert.Equal(1, pick!.Value.TargetIndex);   // "a" was last - only "b" is left
        }
    }

    [Fact]
    public void Pick_IsDeterministicForTheSameSeed()
    {
        var effects = new List<PossessionEffectMeta>
        {
            Effect("nudge", roles: new[] { PossessionRole.Button }),
            Effect("sag", PossessionRung.Melt, weight: 4.0, roles: new[] { PossessionRole.Card }),
            Effect("retitle", PossessionRung.Drift),
        };
        var targets = new List<PossessionTargetMeta>
        {
            Target("a"), Target("b"), Target("c", PossessionRole.Card), Target("d", PossessionRole.Card),
        };

        var first = Enumerable.Range(0, 40)
            .Select(_ => PossessionDeck.Pick(effects, targets, PossessionRung.Melt, PossessionIntensity.Eerie, false, null, new Random(2026)))
            .ToList();
        var second = Enumerable.Range(0, 40)
            .Select(_ => PossessionDeck.Pick(effects, targets, PossessionRung.Melt, PossessionIntensity.Eerie, false, null, new Random(2026)))
            .ToList();

        Assert.Equal(first, second);
        Assert.All(first, p => Assert.NotNull(p));
    }

    [Fact]
    public void Pick_HonoursPhotosafeAndIntensity_Together()
    {
        var effects = new List<PossessionEffectMeta>
        {
            Effect("blink", flicker: true, roles: new[] { PossessionRole.Button }),
            Effect("fake_crash", minIntensity: PossessionIntensity.FullDoki, roles: new[] { PossessionRole.Button }),
            Effect("nudge", roles: new[] { PossessionRole.Button }),
        };
        var targets = new List<PossessionTargetMeta> { Target("a") };

        // Photosafe + Eerie leaves exactly one legal effect, whatever the roll.
        for (int seed = 0; seed < 25; seed++)
        {
            var pick = PossessionDeck.Pick(effects, targets, PossessionRung.Collapse, PossessionIntensity.Eerie,
                photosafe: true, lastTargetKey: null, rng: new Random(seed));
            Assert.NotNull(pick);
            Assert.Equal(2, pick!.Value.EffectIndex);
        }
    }

    // ---------------------------------------------------------------------------------------------
    //  Proximity (wave 2, A5) - "the haunt happens where you are looking"
    // ---------------------------------------------------------------------------------------------

    private static readonly (double X, double Y)[] Centres =
    {
        (100, 100),   // 0: on the cursor
        (150, 120),   // 1: near
        (900, 700),   // 2: far
        (100, 260),   // 3: 160px straight down - exactly on the radius, which counts
    };

    [Fact]
    public void WithinRadius_KeepsTheNearOnes_AndIsInclusiveAtTheEdge()
    {
        var hits = PossessionDeck.WithinRadius(Centres, 100, 100, PossessionDeck.ProximityRadius);
        Assert.Equal(new[] { 0, 1, 3 }, hits);
    }

    [Fact]
    public void WithinRadius_SurvivesGarbage()
    {
        var centres = new[] { (double.NaN, 0.0), (0.0, double.NaN), (10.0, 10.0) };
        Assert.Equal(new[] { 2 }, PossessionDeck.WithinRadius(centres, 0, 0, 100));
        Assert.Empty(PossessionDeck.WithinRadius(centres, double.NaN, 0, 100));
        Assert.Empty(PossessionDeck.WithinRadius(null!, 0, 0, 100));
        Assert.Empty(PossessionDeck.WithinRadius(centres, 0, 0, 0));
    }

    [Fact]
    public void ShouldUseProximity_IsAboutHalf()
    {
        Assert.True(PossessionDeck.ShouldUseProximity(new StubRandom(0.0)));
        Assert.False(PossessionDeck.ShouldUseProximity(new StubRandom(0.5)));
        Assert.False(PossessionDeck.ShouldUseProximity(new StubRandom(0.99)));
    }

    [Fact]
    public void Pick_WithNearList_StaysInsideIt()
    {
        var effects = new[] { Effect("nudge", roles: new[] { PossessionRole.Button }) };
        var targets = new[]
        {
            Target("far-a"), Target("far-b"), Target("near-a"), Target("near-b"),
        };

        // Only the last two are near; whatever the rng rolls, the victim must be one of them.
        for (int seed = 0; seed < 40; seed++)
        {
            var pick = PossessionDeck.Pick(effects, targets, PossessionRung.Settle, PossessionIntensity.Eerie,
                                           false, null, new Random(seed), new[] { 2, 3 });
            Assert.NotNull(pick);
            Assert.InRange(pick!.Value.TargetIndex, 2, 3);
        }
    }

    [Fact]
    public void Pick_FallsBackToTheFullPool_WhenNothingNearMayRun()
    {
        var effects = new[] { Effect("nudge", roles: new[] { PossessionRole.Button }) };
        var targets = new[]
        {
            Target("far-a"),
            Target("near-live", live: true),      // near, but already possessed
            Target("near-cool", cooldown: true),  // near, but cooling down
        };

        var pick = PossessionDeck.Pick(effects, targets, PossessionRung.Settle, PossessionIntensity.Eerie,
                                       false, null, new Random(7), new[] { 1, 2 });
        Assert.NotNull(pick);
        Assert.Equal(0, pick!.Value.TargetIndex);   // fell back to the far one rather than doing nothing
    }

    [Fact]
    public void Pick_ProximityRound_SkipsTargetlessEffects()
    {
        // A title effect has no coordinates, so it cannot be "near the cursor"; a proximity round that
        // let it win would quietly turn half the picks into window effects.
        var effects = new[]
        {
            Effect("crack", weight: 100),                                       // targetless, heavy
            Effect("nudge", weight: 1, roles: new[] { PossessionRole.Button }),
        };
        var targets = new[] { Target("far"), Target("near-a"), Target("near-b") };

        for (int seed = 0; seed < 25; seed++)
        {
            var pick = PossessionDeck.Pick(effects, targets, PossessionRung.Settle, PossessionIntensity.Eerie,
                                           false, null, new Random(seed), new[] { 1, 2 });
            Assert.NotNull(pick);
            Assert.Equal(1, pick!.Value.EffectIndex);
            Assert.InRange(pick.Value.TargetIndex, 1, 2);
        }
    }

    [Fact]
    public void Pick_NearListOfOne_IsIgnored()
    {
        // One candidate is not a neighbourhood; the deck treats it as no hint at all so the pick keeps
        // its full spread rather than hammering the single control under the cursor.
        var effects = new[] { Effect("crack", weight: 5) };   // targetless: only reachable unrestricted
        var targets = new[] { Target("a"), Target("b") };

        var pick = PossessionDeck.Pick(effects, targets, PossessionRung.Settle, PossessionIntensity.Eerie,
                                       false, null, new Random(3), new[] { 1 });
        Assert.NotNull(pick);
        Assert.Equal(-1, pick!.Value.TargetIndex);
    }
}
