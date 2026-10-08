using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models.Program;

namespace ConditioningControlPanel.Services.Program;

/// <summary>
/// The order the Programs tab lists the library in (ccp-bugs #966): the active mod's own programs
/// first, then the programs that belong to no mod, then every other mod's. The sort is stable, so
/// inside each group the library's own order is kept. The mod match is the same one
/// ProgramsIntroPopup uses to pick its featured program (OrdinalIgnoreCase on ModId).
/// </summary>
public static class ProgramBrowseOrder
{
    public static IReadOnlyList<ProgramDefinition> Sort(
        IEnumerable<ProgramDefinition>? library, string? activeModId)
    {
        if (library == null) return Array.Empty<ProgramDefinition>();
        return library.OrderBy(def => Rank(def, activeModId)).ToList();
    }

    /// <summary>0 = the active mod's, 1 = no mod, 2 = another mod's.</summary>
    public static int Rank(ProgramDefinition def, string? activeModId)
    {
        var modId = def?.ModId;
        if (string.IsNullOrWhiteSpace(modId)) return 1;
        if (!string.IsNullOrWhiteSpace(activeModId) &&
            string.Equals(modId, activeModId, StringComparison.OrdinalIgnoreCase)) return 0;
        return 2;
    }
}
