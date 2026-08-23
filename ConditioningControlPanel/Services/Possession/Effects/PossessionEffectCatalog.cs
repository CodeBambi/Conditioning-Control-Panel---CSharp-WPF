using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// Every haunt the director can deal from, one instance each. The director filters by rung, intensity,
/// photosafe (UsesFlicker) and CanApply, then picks by Weight - this list carries no ordering meaning.
///
/// The ladder, for reference (POSSESSION.md):
///   R0 Settle   nudge, typo, breathe
///   R1 Drift    swap, dodge, drift
///   R2 Melt     melt, dissolve, wobble
///   R3 Collapse drop, fall, crack
///   R4 It knows retitle, dokidialog        (Full Doki only)
/// </summary>
public static partial class PossessionEffectCatalog
{
    /// <summary>Wave-2 density batches register through these partial hooks so parallel authors never
    /// touch the same file: implement ONE of them in PossessionEffectCatalog.WaveA.cs / .WaveB.cs /
    /// .WaveC.cs (static partial void AddWaveX(List&lt;IPossessionEffect&gt; list) { list.Add(new ...); }).</summary>
    static partial void AddWaveA(List<IPossessionEffect> list);
    static partial void AddWaveB(List<IPossessionEffect> list);
    static partial void AddWaveC(List<IPossessionEffect> list);

    public static List<IPossessionEffect> CreateAll()
    {
        var list = CreateCore();
        AddWaveA(list);
        AddWaveB(list);
        AddWaveC(list);
        return list;
    }

    private static List<IPossessionEffect> CreateCore() => new()
    {
        // R0 - Settle: deniable in what, never in who.
        new NudgeEffect(),
        new TypoEffect(),
        new BreatheEffect(),

        // R1 - Drift.
        new SwapEffect(),
        new DodgeEffect(),
        new DriftEffect(),

        // R2 - Melt.
        new MeltEffect(),
        new DissolveEffect(),
        new WobbleEffect(),

        // R3 - Collapse.
        new DropEffect(),
        new FallEffect(),
        new CrackEffect(),

        // R4 - It knows (Full Doki only).
        new RetitleEffect(),
        new DokiDialogEffect(),
    };
}
