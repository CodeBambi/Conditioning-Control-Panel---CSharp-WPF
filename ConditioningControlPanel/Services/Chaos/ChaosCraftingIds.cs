using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Id whitelists for the DtRH crafting system (THE BOUDOIR, 2026-07). C# holds IDS ONLY —
/// grid shapes, names, glyphs, costs and effects live page-side in
/// <c>Resources/web/dtrh/game/crafting.js</c>, the single source of truth. If a material
/// or recipe is added there, add its id here too or <see cref="DtrhMetaBridge"/> will
/// reject the <c>material-add</c> / <c>craft</c> op.
/// Ids persist in <see cref="ChaosMetaState.Materials"/>, <see cref="ChaosMetaState.DiscoveredRecipes"/>
/// and <see cref="ChaosMetaState.CraftedItems"/>.
/// </summary>
public static class ChaosCraftingIds
{
    /// <summary>The six droppable materials (crafting.js MATERIALS).</summary>
    public static readonly HashSet<string> Materials = new()
    {
        "silicone", "makeup", "pills", "lace", "lube", "chrome",
    };

    /// <summary>The 21 pictogram recipes (crafting.js RECIPES).</summary>
    public static readonly HashSet<string> Recipes = new()
    {
        "slippery_slope", "rubber_up", "skeleton_key", "locked_collar", "the_cage",
        "ragdoll", "porcelain", "the_ballerina", "the_compact", "the_loom",
        "extensions", "lipstick", "the_hourglass", "the_timepiece", "the_ring_box",
        "the_corset", "sugar_cube", "the_shot", "headphones", "the_padlock",
        "the_pink_heart",
    };
}
