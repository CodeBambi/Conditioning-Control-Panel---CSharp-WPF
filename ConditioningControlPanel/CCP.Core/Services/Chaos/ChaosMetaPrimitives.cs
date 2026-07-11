namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>The rank spine — the single source of truth for depth ranks. Order and
/// ordinals mirror WPF <c>ChaosRanks.cs</c> exactly (Entranced before Devoted; no Lost).</summary>
public enum ChaosRank { Curious = 0, Tempted = 1, Slipping = 2, Entranced = 3, Devoted = 4, Claimed = 5 }

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
