using System;
using System.Linq;
using System.Reflection;
using ConditioningControlPanel.Core.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins every pure scoring/focus formula in <see cref="ChaosScoring"/> against the WPF
/// originals (contract: docs/chaos-run-engine-contracts/economy-scoring.md §1):
/// BasePoints (WPF ChaosModeService.cs:1670), the treat-pop chain (:1862-1869), the defuse
/// chain with Last Breath + Slowburner (:2015-2021), the prism 10x (:1836-1837), the flat
/// tease denial (:1411-1413), the darter/freeze catches WITHOUT BoonPayMult (:2280-2281,
/// :2311), ChanceFlip (:1697-1698), PendulumFactor (:1701-1702), the treat focus refund
/// (:1860) and DefuseCostFor (:1927-1929).
/// </summary>
public class ChaosScoringTests
{
    /// <summary>Rng whose NextDouble is scripted — proves the exact flip branches without
    /// seed archaeology.</summary>
    private sealed class ScriptedRandom : Random
    {
        private readonly double _value;
        public ScriptedRandom(double value) { _value = value; }
        public override double NextDouble() => _value;
    }

    /// <summary>Rng that throws on any draw — proves a code path never consults it.</summary>
    private sealed class ThrowingRandom : Random
    {
        public override double NextDouble() => throw new InvalidOperationException("rng must not be consulted");
    }

    // ================================================================
    // BasePoints (WPF ChaosModeService.cs:1670): 40 + strength * 1.6 — 40..200

    [Theory]
    [InlineData(0, 40.0)]     // weakest payload
    [InlineData(50, 120.0)]   // midpoint
    [InlineData(100, 200.0)]  // strongest payload
    public void BasePoints_MapsStrengthEndpoints(int strength, double expected)
        => Assert.Equal(expected, ChaosScoring.BasePoints(strength), 12);

    // ================================================================
    // ChanceFlip (WPF ChaosModeService.cs:1697-1698)

    [Fact]
    public void ChanceFlip_WinPaysDouble()
        => Assert.Equal(2.0, ChaosScoring.ChanceFlip(0.5, new ScriptedRandom(0.4)));

    [Fact]
    public void ChanceFlip_LossPaysHalf()
        => Assert.Equal(0.5, ChaosScoring.ChanceFlip(0.5, new ScriptedRandom(0.6)));

    [Fact]
    public void ChanceFlip_RollEqualToOddsLoses() // strict < in WPF
        => Assert.Equal(0.5, ChaosScoring.ChanceFlip(0.5, new ScriptedRandom(0.5)));

    [Fact]
    public void ChanceFlip_ZeroOddsIsNeutral_AndNeverConsultsRng()
        => Assert.Equal(1.0, ChaosScoring.ChanceFlip(0.0, new ThrowingRandom()));

    // ================================================================
    // PendulumFactor (WPF ChaosModeService.cs:1701-1702)

    [Fact]
    public void PendulumFactor_ActiveSwingWithMantraPays()
        => Assert.Equal(3.0, ChaosScoring.PendulumFactor(true, 3.0));

    [Fact]
    public void PendulumFactor_InactiveSwingIsNeutral()
        => Assert.Equal(1.0, ChaosScoring.PendulumFactor(false, 3.0));

    [Fact]
    public void PendulumFactor_PayMultAtOneIsNeutralEvenWhileActive() // strict > 1 in WPF
        => Assert.Equal(1.0, ChaosScoring.PendulumFactor(true, 1.0));

    // ================================================================
    // TreatPopScore (WPF ChaosModeService.cs:1862-1869) — the full chain

    [Fact]
    public void TreatPopScore_FullChainWithKnownInputs()
    {
        // BasePoints(50)=120; ×0.4 baseline ×3.0 heavy ×3.0 pendulum ×2.0 flip ×2.5 total ×1.15 blindfold
        double expected = 120.0 * 0.4 * 3.0 * 3.0 * 2.0 * 2.5 * 1.15; // = 2484
        Assert.Equal(expected, ChaosScoring.TreatPopScore(50, 0.4, 3.0, 3.0, 2.0, 2.5, 1.15), 9);
        Assert.Equal(2484.0, expected, 9);
    }

    [Fact]
    public void TreatPopScore_DefaultBaselinePaysFortyPercentOfBase()
        // BenignBaseline default 0.4 (WPF ChaosModels.cs:651); all other factors neutral.
        => Assert.Equal(ChaosScoring.BasePoints(50) * 0.4,
            ChaosScoring.TreatPopScore(50, 0.4, 1.0, 1.0, 1.0, 1.0, 1.0), 12);

    [Fact]
    public void TreatPopScore_GoldenTouchCapstoneBaselinePaysSixtyPercent()
        // Golden Touch capstone raises the calm-pop baseline to 0.60 (WPF ChaosLifetimeBoons golden_touch).
        => Assert.Equal(ChaosScoring.BasePoints(50) * 0.6,
            ChaosScoring.TreatPopScore(50, 0.6, 1.0, 1.0, 1.0, 1.0, 1.0), 12);

    // ================================================================
    // DefuseScore (WPF ChaosModeService.cs:2015-2021)

    [Fact]
    public void DefuseScore_PaysFullBase_NoBenignDiscount()
        // Defuse pays FULL base (×1.0 where a treat pop pays BenignBaseline).
        => Assert.Equal(ChaosScoring.BasePoints(50),
            ChaosScoring.DefuseScore(50, 10.0, 0, 1.0, false, 1.0, 1.0, 1.0, 1.0), 12);

    [Fact]
    public void DefuseScore_LastBreath_InsideWindowPays()
        // fuseSecLeft == window is INSIDE (<= in WPF).
        => Assert.Equal(ChaosScoring.BasePoints(50) * 4.0,
            ChaosScoring.DefuseScore(50, 2.0, 2.0, 4.0, false, 1.0, 1.0, 1.0, 1.0), 12);

    [Fact]
    public void DefuseScore_LastBreath_JustOutsideWindowDoesNot()
        => Assert.Equal(ChaosScoring.BasePoints(50),
            ChaosScoring.DefuseScore(50, 2.0001, 2.0, 4.0, false, 1.0, 1.0, 1.0, 1.0), 12);

    [Fact]
    public void DefuseScore_LastBreath_ZeroWindowNeverPays()
        // LastBreathWindowSec must be > 0 — an unworn boon never triggers, even at the brink.
        => Assert.Equal(ChaosScoring.BasePoints(50),
            ChaosScoring.DefuseScore(50, 0.0, 0.0, 4.0, false, 1.0, 1.0, 1.0, 1.0), 12);

    [Fact]
    public void DefuseScore_Slowburner_TriplesAtExactly1500ms()
        // fuseSecLeft <= 1.5 inclusive (WPF ChaosModeService.cs:2018).
        => Assert.Equal(ChaosScoring.BasePoints(50) * 3.0,
            ChaosScoring.DefuseScore(50, 1.5, 0, 1.0, true, 1.0, 1.0, 1.0, 1.0), 12);

    [Fact]
    public void DefuseScore_Slowburner_JustOutsideBrinkDoesNot()
        => Assert.Equal(ChaosScoring.BasePoints(50),
            ChaosScoring.DefuseScore(50, 1.5001, 0, 1.0, true, 1.0, 1.0, 1.0, 1.0), 12);

    [Fact]
    public void DefuseScore_Slowburner_NeedsTheMaxedBoon()
        => Assert.Equal(ChaosScoring.BasePoints(50),
            ChaosScoring.DefuseScore(50, 1.0, 0, 1.0, false, 1.0, 1.0, 1.0, 1.0), 12);

    [Fact]
    public void DefuseScore_LastBreathAndSlowburnerStack()
    {
        // Both windows hit at once: base ×4 lastBreath ×3 slowburn ×2 pendulum ×0.5 flip ×2 total ×1.15 pay.
        double expected = ChaosScoring.BasePoints(80) * 4.0 * 3.0 * 2.0 * 0.5 * 2.0 * 1.15;
        Assert.Equal(expected,
            ChaosScoring.DefuseScore(80, 1.2, 1.5, 4.0, true, 2.0, 0.5, 2.0, 1.15), 9);
    }

    // ================================================================
    // PrismScore (WPF ChaosModeService.cs:1836-1837) — 10x, no baseline/paymult/pendulum/flip

    [Fact]
    public void PrismScore_PaysTenTimesBase()
        => Assert.Equal(400.0, ChaosScoring.PrismScore(0, 1.0, 1.0), 12);

    [Fact]
    public void PrismScore_AppliesTotalMultAndBoonPayMult()
        => Assert.Equal(ChaosScoring.BasePoints(100) * 10.0 * 2.5 * 1.3,
            ChaosScoring.PrismScore(100, 2.5, 1.3), 9);

    // ================================================================
    // TeaseDeniedScore (WPF ChaosModeService.cs:1411-1413) — flat 120, no base-points chain

    [Fact]
    public void TeaseDeniedScore_FlatBaseIs120()
    {
        Assert.Equal(120.0, ChaosTuning.TEASE_DENIED_SCORE);
        Assert.Equal(120.0, ChaosScoring.TeaseDeniedScore(1.0, 1.0), 12);
    }

    [Fact]
    public void TeaseDeniedScore_AppliesTotalMultAndBoonPayMult()
        => Assert.Equal(120.0 * 3.0 * 1.15, ChaosScoring.TeaseDeniedScore(3.0, 1.15), 9);

    // ================================================================
    // DarterScore (WPF ChaosModeService.cs:2280-2281) — NO BoonPayMult

    [Fact]
    public void DarterScore_BaseCatchPays120TimesTotal()
        => Assert.Equal(120.0 * 2.0, ChaosScoring.DarterScore(false, 2.0), 12);

    [Fact]
    public void DarterScore_QuickCatchAdds90()
        => Assert.Equal((120.0 + 90.0) * 2.0, ChaosScoring.DarterScore(true, 2.0), 12);

    [Fact]
    public void DarterScore_HasNoBoonPayMultParameter_Structurally()
    {
        // WPF pays darter catches WITHOUT the Blindfold pay layer (unlike treat/defuse/prism/
        // tease) — the parameter's absence from the signature pins that structurally.
        var pars = typeof(ChaosScoring).GetMethod(nameof(ChaosScoring.DarterScore),
            BindingFlags.Public | BindingFlags.Static)!.GetParameters();
        Assert.Equal(new[] { "quick", "totalMult" }, pars.Select(p => p.Name).ToArray());
    }

    // ================================================================
    // FreezeScore (WPF ChaosModeService.cs:2311, const :2960) — NO BoonPayMult

    [Fact]
    public void FreezeScore_Pays140TimesTotal()
    {
        Assert.Equal(140.0, ChaosScoring.FREEZE_BASE_POINTS);
        Assert.Equal(140.0 * 2.5, ChaosScoring.FreezeScore(2.5), 12);
    }

    [Fact]
    public void FreezeScore_HasNoBoonPayMultParameter_Structurally()
    {
        var pars = typeof(ChaosScoring).GetMethod(nameof(ChaosScoring.FreezeScore),
            BindingFlags.Public | BindingFlags.Static)!.GetParameters();
        Assert.Equal(new[] { "totalMult" }, pars.Select(p => p.Name).ToArray());
    }

    // ================================================================
    // FocusForTreatPop (WPF ChaosModeService.cs:1860)

    [Fact]
    public void FocusForTreatPop_StandardTreatRefuelsTen()
    {
        Assert.Equal(ChaosTuning.FOCUS_PER_POP, ChaosScoring.FocusForTreatPop(1.0));
        Assert.Equal(10.0, ChaosScoring.FocusForTreatPop(1.0));
    }

    [Fact]
    public void FocusForTreatPop_HeavyRefuelsFifteen()
    {
        Assert.Equal(ChaosTuning.FOCUS_PER_HEAVY, ChaosScoring.FocusForTreatPop(3.0));
        Assert.Equal(15.0, ChaosScoring.FocusForTreatPop(3.0));
    }

    [Fact]
    public void FocusForTreatPop_PayMultExactlyOneIsStandard() // strict > 1 in WPF
        => Assert.Equal(ChaosTuning.FOCUS_PER_POP, ChaosScoring.FocusForTreatPop(1.0 + 0.0));

    // ================================================================
    // DefuseCostFor (WPF ChaosModeService.cs:1927-1929) — only two cases in WPF

    [Fact]
    public void DefuseCostFor_NormalBubbleCostsThirty()
    {
        Assert.Equal(ChaosTuning.DEFUSE_COST, ChaosScoring.DefuseCostFor(false));
        Assert.Equal(30.0, ChaosScoring.DefuseCostFor(false));
    }

    [Fact]
    public void DefuseCostFor_BoundHalfCostsFifteen()
    {
        Assert.Equal(ChaosTuning.DEFUSE_COST_BOUND, ChaosScoring.DefuseCostFor(true));
        Assert.Equal(15.0, ChaosScoring.DefuseCostFor(true));
    }
}
