using System;
using System.IO;
using ConditioningControlPanel.Models.Race;

namespace ConditioningControlPanel.Services.Race;

/// <summary>Charts keyed by source hash under %LOCALAPPDATA%/ConditioningControlPanel/race/charts. PR c4.</summary>
public static class TrackChartCache
{
    public static string Root => Path.Combine(App.UserDataPath, "race", "charts");

    public static TrackChart? TryLoad(string hash)
        => throw new NotImplementedException("PR c4: TrackChartCache.TryLoad");

    public static void Save(TrackChart chart)
        => throw new NotImplementedException("PR c4: TrackChartCache.Save");
}
