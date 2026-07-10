namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Pure end-of-run ECONOMY formulas — the Spark reward and the XP grant — extracted verbatim
/// from the WPF chaos engine so the whole payout is unit-testable without the service or the
/// meta store. Nothing here reads run state, randomness, or persists anything; the caller
/// (the chaos service's EndRun) feeds the primitives in and mutates <c>ChaosMeta.State</c>
/// itself, exactly like the WPF <c>AwardRunRewards</c>/<c>EndRun</c> split.
///
/// Sources (behavior contract — the permanent reference is the WPF source cited below, pinned by
/// tests/CCP.Core.Tests/ChaosEconomyTests.cs and documented in Services/Chaos/CHAOS_DESIGN.md):
///  • Spark reward  → WPF <c>ChaosUpgrades.cs:495-521</c> (<c>ChaosMeta.AwardRunRewards</c>)
///  • XP grant      → WPF <c>ChaosModeService.cs:3163-3169</c> (<c>EndRun</c>)
///
/// P0-3: XP is paid PRE-multiplier — <see cref="BaseXp"/> is the UNmultiplied amount handed to
/// <c>Progression.AddXP</c>; the skill-tree multiplier is applied ONCE inside the progression
/// service. The run's own multiplier stack (TotalMult) never touches the XP grant. It only ever
/// inflated the <em>Score</em>, and the XP grant is Score CAPPED, not Score × mult.
/// </summary>
public static class ChaosEconomy
{
    /// <summary>The predictable per-descent Spark floor (WPF ChaosUpgrades.cs:499).</summary>
    public const double COMPLETION_BONUS_BASE = 35.0;

    /// <summary>√-compression scale on the score contribution (WPF ChaosUpgrades.cs:500).</summary>
    public const double SCORE_SQRT_SCALE = 1.5;

    /// <summary>Minutes of run length at which the completion bonus reaches full value; shorter
    /// runs scale down linearly so a 60s run cannot farm the flat floor (WPF ChaosUpgrades.cs:501).</summary>
    public const double FULL_BONUS_MINUTES = 3.0;

    /// <summary>One-time cold-start "first fall" bonus, the first time RunsCompleted goes 0→1
    /// (WPF ChaosUpgrades.cs:106 <c>FIRST_FALL_BONUS</c>; named on the recap card).</summary>
    public const int FIRST_FALL_BONUS = 25;

    /// <summary>Drip Feed capstone tips 10% extra on the WHOLE Spark haul (WPF ChaosUpgrades.cs:509).</summary>
    public const double DRIP_CAPSTONE_MULT = 1.10;

    /// <summary>XP cap scalar: the grant is capped at 250 · durationMinutes · difficultyMult
    /// (WPF ChaosModeService.cs:3164 <c>capBase = 250.0 * durMin * DifficultyMult</c>).</summary>
    public const double XP_PER_MINUTE_CAP = 250.0;

    /// <summary>
    /// End-of-run Spark reward — verbatim WPF <c>ChaosMeta.AwardRunRewards</c>
    /// (ChaosUpgrades.cs:504-514). Returns the Sparks banked (never negative). PURE: the caller
    /// adds this to <c>State.Sparks</c> and increments <c>State.RunsCompleted</c>.
    /// </summary>
    /// <param name="score">Final run score (WPF <c>run.Score</c>).</param>
    /// <param name="difficultyMult">Per-difficulty scalar 1.0/1.3/1.7/2.2 (WPF <c>run.Config.DifficultyMult</c>).</param>
    /// <param name="runDurationSec">Planned run duration in seconds (Relapse extends it via ExtendOneLoop;
    /// WPF <c>run.RunDurationSec</c> — note Max(0,…) here vs Max(1,…) in the XP cap).</param>
    /// <param name="sparkGainMult">Spark gain multiplier — ALWAYS 1.0 (the spark_gain habit was retired;
    /// WPF <c>run.Config.SparkGainMult</c>). Multiplies ONLY (scorePart + completionBonus).</param>
    /// <param name="trickleDrops">Drip Feed per-pop trickle gathered in-run, already capped
    /// (WPF <c>run.TrickleDrops</c>); added AFTER the SparkGainMult.</param>
    /// <param name="hasDripCapstone">True when <c>drip_feed</c> is maxed → ×1.10 on the whole haul
    /// (WPF <c>run.MaxedBoons.Contains("drip_feed")</c>).</param>
    /// <param name="firstFall">True when this is the very first completed descent
    /// (WPF <c>State.RunsCompleted == 0</c>, checked BEFORE the increment → exactly once ever).</param>
    public static int SparkReward(double score, double difficultyMult, double runDurationSec,
        double sparkGainMult, long trickleDrops, bool hasDripCapstone, bool firstFall)
    {
        double durationMin = System.Math.Max(0, runDurationSec) / 60.0;
        double completionBonus = COMPLETION_BONUS_BASE * difficultyMult
                                 * System.Math.Min(1.0, durationMin / FULL_BONUS_MINUTES);
        double scorePart = SCORE_SQRT_SCALE * System.Math.Sqrt(System.Math.Max(0, score));
        int sparks = (int)System.Math.Round((scorePart + completionBonus) * sparkGainMult);

        // Drip Feed: the per-pop trickle lands here (outside the SparkGainMult), and the capstone
        // tips 10% extra on the whole haul (WPF ChaosUpgrades.cs:509-511).
        sparks += (int)System.Math.Max(0, trickleDrops);
        if (hasDripCapstone) sparks = (int)System.Math.Round(sparks * DRIP_CAPSTONE_MULT);

        // One-time cold-start kindness, guarded so it only ever applies once (WPF ChaosUpgrades.cs:513).
        if (firstFall) sparks += FIRST_FALL_BONUS;

        return System.Math.Max(0, sparks);
    }

    /// <summary>
    /// End-of-run XP grant amount — verbatim WPF <c>EndRun</c> (ChaosModeService.cs:3163-3166).
    /// This is the UNmultiplied base handed to <c>Progression.AddXP</c> (P0-3). It is the run
    /// score CLAMPED to <c>250 · durationMinutes · difficultyMult</c> — the run's TotalMult
    /// multiplier stack is NOT applied here (it only ever inflated Score).
    /// </summary>
    /// <param name="score">Final run score (WPF <c>_state.Score</c>).</param>
    /// <param name="runDurationSec">Planned run duration in seconds; <c>Max(1,…)</c> here (a 0-length
    /// run still gets a non-zero cap minute) vs <c>Max(0,…)</c> in the spark reward.</param>
    /// <param name="difficultyMult">Per-difficulty scalar (WPF <c>_state.Config.DifficultyMult</c>).</param>
    public static double BaseXp(double score, double runDurationSec, double difficultyMult)
    {
        double durMin = System.Math.Max(1, runDurationSec) / 60.0;   // NOTE Max(1,…) here vs Max(0,…) in SparkReward
        double capBase = XP_PER_MINUTE_CAP * durMin * difficultyMult;
        return System.Math.Min(score, capBase);
    }
}
