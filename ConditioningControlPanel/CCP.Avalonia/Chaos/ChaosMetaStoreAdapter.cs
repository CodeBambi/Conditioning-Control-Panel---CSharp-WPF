using ConditioningControlPanel.Core.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>Backs the Core <see cref="IChaosMetaStore"/> seam (the narrow surface the ported
/// DtrhHostOrchestrator/DtrhMetaBridge need) over the head <c>IChaosMetaService</c> facade.</summary>
public sealed class ChaosMetaStoreAdapter : IChaosMetaStore
{
    private readonly IChaosMetaService _svc;
    public ChaosMetaStoreAdapter(IChaosMetaService svc) => _svc = svc;
    public ChaosMetaState State => _svc.State;
    public int RankIndex => _svc.RankIndex;
    public void Save() => _svc.Save();
}
