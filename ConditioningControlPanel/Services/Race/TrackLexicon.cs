using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Race;

/// <summary>
/// The phrases the word spotter listens for: CHART.md STRUCTURE_WORDS + the active mod's trigger
/// phrases + the user's custom triggers + keyword-trigger phrases; lowercased, letters and spaces
/// only, distinct. Filled in by PR c5.
/// </summary>
public static class TrackLexicon
{
    public static IReadOnlyList<string> Build()
        => throw new NotImplementedException("PR c5: TrackLexicon.Build");
}
