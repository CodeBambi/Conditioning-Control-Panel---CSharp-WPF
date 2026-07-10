namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Pure per-pop scoring and focus-economy formulas, extracted verbatim from the WPF chaos
/// engine (WPF ChaosModeService.cs) so every payout is unit-testable without the service.
/// The service composes these pieces exactly like the WPF handlers do; NO formula may be
/// inlined back into handler code (contract: WPF ChaosModeService.cs + pinned
/// tests/CCP.Core.Tests/ChaosScoringTests.cs + Services/Chaos/CHAOS_DESIGN.md).
/// All state (multiplier stack, boon knobs) arrives as parameters — nothing here reads
/// run state or randomness of its own.
/// </summary>
public static class ChaosScoring
{
    /// <summary>Base score of a freeze-bubble catch. WPF keeps this as a private const on the
    /// service (WPF ChaosModeService.cs:2960 <c>FREEZE_BASE_POINTS = 140</c>); it lives here
    /// because the Core side has no other home for it yet.</summary>
    public const double FREEZE_BASE_POINTS = 140;

    /// <summary>Base points per bubble from payload strength 0..100 → 40..200
    /// (WPF ChaosModeService.cs:1670 <c>BasePoints</c>).</summary>
    public static double BasePoints(int strength) => 40 + strength * 1.6;

    /// <summary>Taking Chances coin-flip: every pop pays x2 with the level's odds, else x0.5;
    /// 1.0 when unworn — the rng is NOT consulted when odds are 0 (WPF ChaosModeService.cs:1697-1698
    /// <c>ChanceFlip</c>, a short-circuiting ternary over <c>ChanceDoubleOdds</c>).</summary>
    public static double ChanceFlip(double chanceDoubleOdds, Random rng) =>
        chanceDoubleOdds > 0 ? (rng.NextDouble() < chanceDoubleOdds ? 2.0 : 0.5) : 1.0;

    /// <summary>"Focus here...": xPendulumPayMult (3.0 with the mantra) ONLY while the pendulum's
    /// own slow swing holds; 1.0 otherwise. Darter slow-mo does NOT qualify
    /// (WPF ChaosModeService.cs:1701-1702 <c>PendulumFactor</c>).</summary>
    public static double PendulumFactor(bool pendulumSlowActive, double pendulumPayMult) =>
        pendulumSlowActive && pendulumPayMult > 1 ? pendulumPayMult : 1.0;

    /// <summary>Treat (benign) pop payout — the full WPF multiplication chain
    /// (WPF ChaosModeService.cs:1862-1869): BasePoints x BenignBaseline (0.4 default; Golden Touch
    /// 0.45/0.50/0.55/0.60) x spec.PayMult (Heavy Drop 3.0) x PendulumFactor x ChanceFlip
    /// x TotalMult x BoonPayMult.</summary>
    public static double TreatPopScore(int strength, double benignBaseline, double payMult,
        double pendulumFactor, double chanceFlip, double totalMult, double boonPayMult) =>
        BasePoints(strength) * benignBaseline * payMult * pendulumFactor * chanceFlip
        * totalMult * boonPayMult;

    /// <summary>Defuse (snap) payout (WPF ChaosModeService.cs:2015-2021): pays FULL base
    /// (x1.0 where a treat pop pays BenignBaseline). Last Breath: snapping with
    /// <paramref name="fuseSecLeft"/> at or under the window (window &gt; 0) pays
    /// <paramref name="lastBreathPayMult"/>. Slowburner capstone: a snap inside the final
    /// 1.5 seconds (inclusive) pays triple when the boon is maxed.</summary>
    public static double DefuseScore(int strength, double fuseSecLeft, double lastBreathWindowSec,
        double lastBreathPayMult, bool slowburnerMaxed, double pendulumFactor, double chanceFlip,
        double totalMult, double boonPayMult)
    {
        double lastBreath = lastBreathWindowSec > 0 && fuseSecLeft <= lastBreathWindowSec
            ? lastBreathPayMult : 1.0;
        double slowburn = fuseSecLeft <= 1.5 && slowburnerMaxed ? 3.0 : 1.0;
        return BasePoints(strength) * 1.0 * lastBreath * slowburn * pendulumFactor * chanceFlip
               * totalMult * boonPayMult;
    }

    /// <summary>Mimic prism pop: 10x pay — NO BenignBaseline, PayMult, PendulumFactor or
    /// ChanceFlip in the chain (WPF ChaosModeService.cs:1836-1837).</summary>
    public static double PrismScore(int strength, double totalMult, double boonPayMult) =>
        BasePoints(strength) * 10.0 * totalMult * boonPayMult;

    /// <summary>The Tease expired untouched: flat TEASE_DENIED_SCORE (120), no base-points chain
    /// (WPF ChaosModeService.cs:1411-1413; constant WPF ChaosTuning.cs TEASE_DENIED_SCORE).</summary>
    public static double TeaseDeniedScore(double totalMult, double boonPayMult) =>
        ChaosTuning.TEASE_DENIED_SCORE * totalMult * boonPayMult;

    /// <summary>White-rabbit darter catch: 120 base + 90 quick-catch bonus, times TotalMult —
    /// deliberately NO BoonPayMult, unlike treat/defuse/prism/tease
    /// (WPF ChaosModeService.cs:2280-2281; constants WPF ChaosBubbleVariants.cs:145-146).</summary>
    public static double DarterScore(bool quick, double totalMult) =>
        (ChaosSpawnCatalog.DARTER_BASE_POINTS
         + (quick ? ChaosSpawnCatalog.DARTER_QUICK_BONUS : 0)) * totalMult;

    /// <summary>Freeze-bubble catch: 140 base times TotalMult — NO BoonPayMult
    /// (WPF ChaosModeService.cs:2311; constant WPF ChaosModeService.cs:2960).</summary>
    public static double FreezeScore(double totalMult) => FREEZE_BASE_POINTS * totalMult;

    /// <summary>Focus refund for a treat-class pop: heavies (PayMult &gt; 1) refuel a little extra
    /// (WPF ChaosModeService.cs:1860 <c>spec.PayMult &gt; 1 ? FOCUS_PER_HEAVY : FOCUS_PER_POP</c>).</summary>
    public static double FocusForTreatPop(double payMult) =>
        payMult > 1 ? ChaosTuning.FOCUS_PER_HEAVY : ChaosTuning.FOCUS_PER_POP;

    /// <summary>Defuse cost for one completed channel on this bubble: Bound halves pay half each,
    /// so the pair totals one normal defuse. These are the ONLY two cases in the WPF original
    /// (WPF ChaosModeService.cs:1927-1929 <c>DefuseCostFor</c>).</summary>
    public static double DefuseCostFor(bool isBoundHalf) =>
        isBoundHalf ? ChaosTuning.DEFUSE_COST_BOUND : ChaosTuning.DEFUSE_COST;
}
