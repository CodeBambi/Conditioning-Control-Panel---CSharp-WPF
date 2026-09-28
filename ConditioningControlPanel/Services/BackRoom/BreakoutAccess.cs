using ConditioningControlPanel.Localization;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>The normal Tier 2 entitlement, shared by the standalone and room doors.</summary>
public static class BreakoutAccess
{
    public static bool FullAllowed => TierGate.RequiresLab(Loc.Get("launcher_game_breakout_title")).Allowed;
    internal static bool DemandFull() => TierGate.DemandLab(Loc.Get("launcher_game_breakout_title"));

    // An explicit demo never expands when an account signs in or gains a tier.
    internal static BreakoutEntitlement Project(bool fullAllowed, bool forceDemo = false)
        => fullAllowed && !forceDemo ? new(8, true, false) : new(3, false, true);

    internal static BreakoutEntitlement Current(bool forceDemo = false) => Project(FullAllowed, forceDemo);
}

public sealed record BreakoutEntitlement(
    [property: JsonProperty("storyLimit")] int StoryLimit,
    [property: JsonProperty("endless")] bool Endless,
    [property: JsonProperty("demo")] bool Demo);
