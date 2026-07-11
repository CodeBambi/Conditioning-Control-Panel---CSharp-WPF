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
