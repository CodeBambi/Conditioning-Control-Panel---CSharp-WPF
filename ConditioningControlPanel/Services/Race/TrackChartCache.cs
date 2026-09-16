using System;
using System.IO;
using ConditioningControlPanel.Models.Race;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Race;

/// <summary>Charts keyed by source hash under %LOCALAPPDATA%/ConditioningControlPanel/race/charts.</summary>
public static class TrackChartCache
{
    public static string Root => Path.Combine(App.UserDataPath, "race", "charts");

    /// <summary>The file a chart with this source hash lives at.</summary>
    public static string PathFor(string hash) => Path.Combine(Root, hash + ".json");

    /// <summary>
    /// The cached chart for a source hash, or null when there is none, it will not parse, or it was
    /// written by an older chart version. Never throws: a bad cache entry is a cache miss.
    /// </summary>
    public static TrackChart? TryLoad(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return null;
        try
        {
            string path = PathFor(hash);
            if (!File.Exists(path)) return null;
            var chart = JsonConvert.DeserializeObject<TrackChart>(File.ReadAllText(path));
            if (chart == null || chart.Version != TrackChart.CurrentVersion) return null;
            return chart;
        }
        catch (Exception ex)
        {
            App.Logger?.Information("race-chart: cached chart {Hash} unreadable ({Message})", hash, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Writes the chart under its source hash. Goes to a .tmp first and moves over the top, so a
    /// crash mid-write cannot leave a half chart where the next run will read one.
    ///
    /// Refuses, quietly, in the two cases that would put a generated chart where an authored one
    /// belongs: the chart handed in is itself authored (nothing hand written is ever cached, it is
    /// read from where its author put it), or a track already has an authored chart (a generated one
    /// must not be sitting in the cache waiting for the day the authored one is moved). Refusing is
    /// a log line, never an exception: a cache that will not take a write is not a failed analysis.
    /// </summary>
    public static void Save(TrackChart chart)
    {
        if (chart == null) throw new ArgumentNullException(nameof(chart));
        string hash = chart.Source.Hash;
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException("The chart has no source hash to key the cache by", nameof(chart));

        if (AuthoredCharts.IsAuthored(chart))
        {
            App.Logger?.Information("race-chart: not caching {Hash}, it is an authored chart", hash);
            return;
        }
        if (AuthoredCharts.ExistsFor(hash, chart.Source.CloudId))
        {
            App.Logger?.Information("race-chart: not caching {Hash}, an authored chart already answers for it", hash);
            return;
        }

        Directory.CreateDirectory(Root);
        string path = PathFor(hash);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(chart, Formatting.Indented));
        File.Move(temp, path, overwrite: true);
    }
}
