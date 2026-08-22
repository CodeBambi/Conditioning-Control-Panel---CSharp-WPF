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
public static class PossessionEffectCatalog
{
    public static List<IPossessionEffect> CreateAll() => new()
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
