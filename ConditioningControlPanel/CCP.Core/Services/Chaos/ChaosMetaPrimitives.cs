namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>The rank spine — the single source of truth for depth ranks. Order and
/// ordinals mirror WPF <c>ChaosRanks.cs</c> exactly (Entranced before Devoted; no Lost).</summary>
public enum ChaosRank { Curious = 0, Tempted = 1, Slipping = 2, Entranced = 3, Devoted = 4, Claimed = 5 }

/// <summary>Pure rank-threshold math: lifetime completed descents → depth rank. Mirrors the head
/// <c>ChaosRanks.For</c> (AvaloniaChaosStubs.cs:634) and WPF <c>ChaosRanks.cs:22</c> exactly. Portable
/// so the DTRH orchestrator's run-end rank-up check needs no head coupling.</summary>
public static class ChaosRankThresholds
{
    /// <summary>Lifetime completed-descent counts that unlock each successive rank.</summary>
    public static int[] Thresholds { get; } = { 0, 3, 10, 25, 50, 100 };

    /// <summary>The rank earned for <paramref name="runsCompleted"/> lifetime descents.</summary>
    public static ChaosRank For(int runsCompleted)
    {
        var r = ChaosRank.Curious;
        for (int i = Thresholds.Length - 1; i >= 0; i--)
            if (runsCompleted >= Thresholds[i]) { r = (ChaosRank)i; break; }
        return r;
    }
}

/// <summary>
/// Bench / console purchase identifiers (dollhouse convenience extras). Portable to Core so the
/// DTRH meta bridge's <c>bench-buy</c> whitelist and the reveal gates share one source of truth
/// with the head. Values are the on-disk ids persisted in
/// <see cref="ChaosMetaState.BenchPurchases"/>.
/// </summary>
public static class BenchIds
{
    public const string ToyPocket1 = "toy_pocket_1";
    public const string AccPocket1 = "acc_pocket_1";
    public const string StartMantra = "start_mantra";
    public const string Diary = "diary";
    public const string StatsPanel = "stats_panel";
    public const string ToyPocket2 = "toy_pocket_2";
    public const string AccPocket2 = "acc_pocket_2";
}

/// <summary>
/// Plain-value completed-run award input so run brains that are NOT a native ChaosRunState (the
/// DtRH browser game reports its runs over the meta bridge) share the exact same spark
/// formula/banking. Fields verbatim from WPF <c>ChaosMeta.ChaosRunRewardInput</c>
/// (ChaosUpgrades.cs:517). Fed to <see cref="ChaosEconomy.SparkReward"/> plus the state bumps the
/// bridge applies at run end.
/// </summary>
public readonly record struct ChaosRunRewardInput(
    double RunDurationSec, double DifficultyMult, double SparkGainMult,
    double Score, double TrickleDrops, bool DripFeedMaxed,
    int BestCombo, int Defused, double ElapsedSec);
