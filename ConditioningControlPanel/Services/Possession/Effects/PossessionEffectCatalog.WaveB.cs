using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// Wave 2 (density), the EFFECTS-B batch: the haunts that take something away rather than wobble it.
/// Registered through the catalog's partial hook so this file is the only one this author touches -
/// see PossessionEffectCatalog.cs for the WaveA/WaveB/WaveC split and POSSESSION.md "Wave 2 - density"
/// for who owns what.
///
/// <para>Deck weights (the director scales them per rung above each effect's MinRung):</para>
/// <list type="bullet">
///   <item>R1 toast 3</item>
///   <item>R2 xpdrain 3, misroute 2, scrollhijack 2</item>
///   <item>R3 stealcard 3, roomwarp 2, reorderdoors 2</item>
///   <item>R4 deletedialog 2 (Full Doki only)</item>
/// </list>
/// </summary>
public static partial class PossessionEffectCatalog
{
    static partial void AddWaveB(List<IPossessionEffect> list)
    {
        // R1 - Drift.
        list.Add(new ToastEffect());              // weight 3

        // R2 - Melt.
        list.Add(new XpDrainEffect());            // weight 3
        list.Add(new MisrouteEffect());           // weight 2
        list.Add(new ScrollHijackEffect());       // weight 2

        // R3 - Collapse.
        list.Add(new StealCardEffect());          // weight 3
        list.Add(new RoomWarpEffect());           // weight 2
        list.Add(new ReorderDoorsEffect());       // weight 2

        // R4 - It knows (Full Doki only).
        list.Add(new DeleteDialogEffect());       // weight 2
    }
}
