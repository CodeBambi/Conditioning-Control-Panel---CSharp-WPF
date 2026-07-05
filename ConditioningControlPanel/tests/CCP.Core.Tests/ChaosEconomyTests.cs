using System;
using ConditioningControlPanel.Core.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the pure end-of-run ECONOMY formulas in <see cref="ChaosEconomy"/> against the WPF
/// originals (contract: docs/chaos-run-engine-contracts/economy-scoring.md §2-3):
///  • Spark reward → WPF <c>ChaosUpgrades.cs:495-521</c> (AwardRunRewards):
///    <c>round((1.5·√score + 35·diff·min(1,durMin/3))·SparkGainMult) + TrickleDrops</c>,
///    ×1.10 drip capstone on the WHOLE haul, +25 first-fall (once ever).
///  • XP grant → WPF <c>ChaosModeService.cs:3163-3166</c> (EndRun):
///    <c>min(Score, 250·max(1,durSec)/60·diff)</c> — the UNmultiplied base handed to AddXP (P0-3).
///
/// Golden inputs are chosen so the arithmetic lands on exact integers (no midpoint-rounding
/// ambiguity); differential cases prove the placement of each term (trickle outside SparkGainMult,
/// drip on the whole haul, first-fall additive-once).
/// </summary>
public class ChaosEconomyTests
{
    // ================================================================
    // SparkReward — golden exact-integer cases (WPF ChaosUpgrades.cs:504-514)
    // Gentle/3-min/score-400: scorePart = 1.5·√400 = 30; completion = 35·1.0·min(1,3/3) = 35 → 65.

    [Fact]
    public void SparkReward_Golden_Gentle3Min()
        => Assert.Equal(65, ChaosEconomy.SparkReward(
            score: 400, difficultyMult: 1.0, runDurationSec: 180, sparkGainMult: 1.0,
            trickleDrops: 0, hasDripCapstone: false, firstFall: false));

    // Inescapable diff scales ONLY the completion bonus (not scorePart): 30 + 35·2.2 = 30 + 77 = 107.
    [Fact]
    public void SparkReward_DifficultyScalesCompletionBonusOnly()
        => Assert.Equal(107, ChaosEconomy.SparkReward(
            score: 400, difficultyMult: 2.2, runDurationSec: 180, sparkGainMult: 1.0,
            trickleDrops: 0, hasDripCapstone: false, firstFall: false));

    // Base case reused below: score 100 / Gentle / 3-min → scorePart 15 + completion 35 = 50.
    [Fact]
    public void SparkReward_Golden_Score100()
        => Assert.Equal(50, ChaosEconomy.SparkReward(
            score: 100, difficultyMult: 1.0, runDurationSec: 180, sparkGainMult: 1.0,
            trickleDrops: 0, hasDripCapstone: false, firstFall: false));

    // ================================================================
    // First-fall (+25 once) — WPF ChaosUpgrades.cs:513 (FIRST_FALL_BONUS = 25)

    [Fact]
    public void SparkReward_FirstFall_AddsExactly25()
    {
        int without = ChaosEconomy.SparkReward(100, 1.0, 180, 1.0, 0, false, firstFall: false);
        int with = ChaosEconomy.SparkReward(100, 1.0, 180, 1.0, 0, false, firstFall: true);
        Assert.Equal(50, without);
        Assert.Equal(75, with);
        Assert.Equal(ChaosEconomy.FIRST_FALL_BONUS, with - without);
    }

    /// <summary>The "once ever" guard lives in the caller (EndRun reads RunsCompleted==0 BEFORE the
    /// increment). This mirrors that usage to prove the bonus lands on exactly the first descent and
    /// never again (WPF ChaosUpgrades.cs:513 guarded before RunsCompleted += 1).</summary>
    [Fact]
    public void SparkReward_FirstFall_AppliesExactlyOnceAcrossDescents()
    {
        int runsCompleted = 0;
        long banked = 0;
        for (int descent = 0; descent < 4; descent++)
        {
            bool firstFall = runsCompleted == 0;            // WPF: checked BEFORE the increment
            banked += ChaosEconomy.SparkReward(100, 1.0, 180, 1.0, 0, false, firstFall);
            runsCompleted += 1;                             // WPF: State.RunsCompleted += 1
        }
        // 4 descents × 50 base + one-time 25 first-fall.
        Assert.Equal(4 * 50 + ChaosEconomy.FIRST_FALL_BONUS, banked);
    }

    // ================================================================
    // Drip Feed capstone (×1.10 on the WHOLE haul) — WPF ChaosUpgrades.cs:509

    [Fact]
    public void SparkReward_DripCapstone_TipsTenPercentOnWholeHaul()
    {
        // base 50 → ×1.10 = 55.
        Assert.Equal(55, ChaosEconomy.SparkReward(100, 1.0, 180, 1.0, 0, hasDripCapstone: true, firstFall: false));
    }

    [Fact]
    public void SparkReward_DripCapstone_AppliesBeforeFirstFall()
    {
        // base 50 → drip ×1.10 = 55 → + first-fall 25 = 80 (first-fall is NOT scaled by the capstone).
        Assert.Equal(80, ChaosEconomy.SparkReward(100, 1.0, 180, 1.0, 0, hasDripCapstone: true, firstFall: true));
    }

    // ================================================================
    // TrickleDrops (added AFTER SparkGainMult, folded INTO the drip capstone) — WPF ChaosUpgrades.cs:508-509

    [Fact]
    public void SparkReward_TrickleDrops_AddedAfterSparkGainMult()
    {
        // SparkGainMult multiplies ONLY (scorePart + completionBonus): round(65·2) = 130, then +10 trickle.
        Assert.Equal(140, ChaosEconomy.SparkReward(
            score: 400, difficultyMult: 1.0, runDurationSec: 180, sparkGainMult: 2.0,
            trickleDrops: 10, hasDripCapstone: false, firstFall: false));
    }

    [Fact]
    public void SparkReward_TrickleDrops_FoldedIntoDripCapstone()
    {
        // base 50 + trickle 10 = 60, then ×1.10 = 66 (the capstone tips the trickle too).
        Assert.Equal(66, ChaosEconomy.SparkReward(100, 1.0, 180, 1.0, trickleDrops: 10, hasDripCapstone: true, firstFall: false));
    }

    // ================================================================
    // Completion-bonus sub-3-minute scaling + non-negativity — WPF ChaosUpgrades.cs:504-505

    [Fact]
    public void SparkReward_CompletionBonus_ScalesDownBelowThreeMinutes()
    {
        // Score 0 isolates the completion bonus. A 90s run pays less than a 180s run (linear),
        // and a run at/over 3 min pays the same flat floor (min(1, durMin/3) caps at 1).
        int short90 = ChaosEconomy.SparkReward(0, 1.0, 90, 1.0, 0, false, false);
        int full180 = ChaosEconomy.SparkReward(0, 1.0, 180, 1.0, 0, false, false);
        int over300 = ChaosEconomy.SparkReward(0, 1.0, 300, 1.0, 0, false, false);
        Assert.True(short90 < full180, $"90s ({short90}) should pay less than 180s ({full180})");
        Assert.Equal(full180, over300);
        Assert.Equal(35, full180);   // 35·diff·1.0 at/over full-bonus minutes
    }

    [Fact]
    public void SparkReward_NegativeScore_ClampsScorePartToZero()
    {
        // √(max(0, score)) = 0, so only the completion bonus survives (35), never a negative.
        Assert.Equal(35, ChaosEconomy.SparkReward(-500, 1.0, 180, 1.0, 0, false, false));
    }

    [Fact]
    public void SparkReward_NeverNegative()
        => Assert.True(ChaosEconomy.SparkReward(-1000, 1.0, -60, 1.0, -5, false, false) >= 0);

    // ================================================================
    // BaseXp — the UNmultiplied, Score-CAPPED grant (P0-3) — WPF ChaosModeService.cs:3163-3166

    [Fact]
    public void BaseXp_BelowCap_ReturnsRawScore()
    {
        // cap = 250·3·1.0 = 750; score 100 < cap → 100 (NOT score×anymult — XP is pre-multiplier).
        Assert.Equal(100.0, ChaosEconomy.BaseXp(score: 100, runDurationSec: 180, difficultyMult: 1.0), 12);
    }

    [Fact]
    public void BaseXp_AboveCap_ClampsToCap()
    {
        // cap = 250·3·1.0 = 750; score 2000 > cap → 750.
        Assert.Equal(750.0, ChaosEconomy.BaseXp(score: 2000, runDurationSec: 180, difficultyMult: 1.0), 12);
    }

    [Fact]
    public void BaseXp_DifficultyScalesTheCap()
    {
        // cap = 250·3·2.2 = 1650; score 2000 > cap → 1650.
        Assert.Equal(1650.0, ChaosEconomy.BaseXp(score: 2000, runDurationSec: 180, difficultyMult: 2.2), 12);
    }

    [Fact]
    public void BaseXp_DurationFloor_UsesMaxOneSecond()
    {
        // WPF durMin = Max(1, RunDurationSec)/60 → a 0-length run floors at 1 SECOND, not 1 minute:
        // cap = 250·(1/60)·1.0 ≈ 4.1667; score 100 > cap → the tiny cap.
        Assert.Equal(250.0 * (1.0 / 60.0), ChaosEconomy.BaseXp(score: 100, runDurationSec: 0, difficultyMult: 1.0), 9);
    }

    [Fact]
    public void BaseXp_SparkAndXp_AreDistinctEconomies()
    {
        // Same run: the Spark haul (√-compressed) and the XP grant (Score-capped) are unrelated —
        // a porting trap is paying one as the other. Score 400 / Gentle / 3-min:
        //   sparks = 65 (√400·1.5 + 35)   vs   baseXp = 400 (< 750 cap) = raw score.
        int sparks = ChaosEconomy.SparkReward(400, 1.0, 180, 1.0, 0, false, false);
        double baseXp = ChaosEconomy.BaseXp(400, 180, 1.0);
        Assert.Equal(65, sparks);
        Assert.Equal(400.0, baseXp, 12);
    }
}
