namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// The minimal seam the portable <c>DtrhMetaBridge</c> needs onto the C#-owned Chaos meta save
/// (chaos_meta.json). Deliberately narrow (advisor ruling 2026-07-11, Option 3): the bridge
/// mutates <see cref="State"/> in place and calls <see cref="Save"/>; completed-run banking is
/// done inside the bridge via <see cref="ChaosEconomy.SparkReward"/>, so no award method lives
/// here. The head backs this over its existing <c>IChaosMetaService</c>/<c>ChaosMeta</c> facade;
/// tests supply an in-memory fake. This is NOT the full head meta service — reveal / narrative /
/// catalogue machinery stays head-side until the native run is decommissioned (row #6 phase 8).
/// </summary>
public interface IChaosMetaStore
{
    /// <summary>The live meta state. The bridge mutates this reference directly (mirrors WPF
    /// <c>ChaosMeta.State</c>); call <see cref="Save"/> to persist.</summary>
    ChaosMetaState State { get; }

    /// <summary>Current depth-rank ordinal (0-based; mirrors WPF <c>ChaosMeta.RankIndex</c>). Gates
    /// the two feral option-panel dials (hydra / glitchTimer) at <see cref="ChaosRank.Entranced"/>.</summary>
    int RankIndex { get; }

    /// <summary>Persist <see cref="State"/> to disk (best-effort; mirrors WPF <c>ChaosMeta.Save</c>).</summary>
    void Save();
}
