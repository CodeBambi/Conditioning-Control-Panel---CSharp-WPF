using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models.Race;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Race;

/// <summary>
/// AUTHORED CHARTS ALWAYS WIN (owner rule). A chart a person wrote is used instead of anything the
/// analysis could produce, it is never merged into, never written over and never charted again.
/// This is the desktop half of the lookup the page does in <c>race/chartSource.js</c>, and the two
/// read the same index file and derive the same <c>cloudId</c>, so a chart linked once answers on
/// both sides.
///
/// Every track is looked up in this order and stops at the first answer:
///   a. the player's own folder, <c>%LOCALAPPDATA%/ConditioningControlPanel/race/authored/*.json</c>
///   b. the shipped index, <c>Resources/web/dtrh/race/charts/index.json</c>, by cloudId then hash
///   c. the generated-chart cache, by hash (<see cref="TrackChartCache"/>)
///   d. nothing: chart the audio
///
/// a and b answer with a url and a hash in hand, both of which are cheap: the hash is a length and
/// the first megabyte, not the file. So an authored track never costs a download.
///
/// Nothing here throws. A folder that is not there, a chart that will not parse and an index row
/// pointing at a file nobody shipped are all the same thing to a caller: no authored chart.
/// </summary>
public static class AuthoredCharts
{
    // The four door names the page logs, so a desktop log and a browser log read alike, plus the
    // one door only the desktop has.
    public const string DoorUserFolder = "authored (user folder)";
    public const string DoorCloudId = "authored by cloudId";
    public const string DoorHash = "authored by hash";
    public const string DoorCache = "cached";
    public const string DoorGenerated = "generated";

    /// <summary>Where a player drops a chart of their own. Any *.json in here with `hand: true`.</summary>
    public static string UserRoot => Path.Combine(App.UserDataPath, "race", "authored");

    /// <summary>The race page's folder as it lands next to the exe (DtrhHostService maps this same
    /// Resources/web tree to ccp.game), so the shipped index is read off disk, not over the bridge.</summary>
    private static string RaceWebRoot => Path.Combine(AppContext.BaseDirectory, "Resources", "web", "dtrh", "race");

    private static string ShippedIndexPath => Path.Combine(RaceWebRoot, "charts", "index.json");

    /// <summary>What makes a chart authored, on both sides: a top level `hand: true`.</summary>
    public static bool IsAuthored(TrackChart? chart) => chart?.Hand == true;

    /// <summary>
    /// The authored chart for this track, or null. <paramref name="door"/> names the door it came
    /// through, ready for the one Information line the host logs per track.
    /// </summary>
    public static TrackChart? Find(string? hash, string? cloudId, out string door)
    {
        door = "";
        string h = (hash ?? "").Trim().ToLowerInvariant();
        string id = (cloudId ?? "").Trim().ToLowerInvariant();
        if (h.Length == 0 && id.Length == 0) return null;

        var mine = FindInUserFolder(h, id);
        if (mine != null) { door = DoorUserFolder; return mine; }

        var rows = ShippedRows();
        if (rows.Count == 0) return null;
        if (id.Length > 0)
        {
            var chart = LoadRow(rows.FirstOrDefault(r => Same(r.CloudId, id)));
            if (chart != null) { door = DoorCloudId; return chart; }
        }
        if (h.Length > 0)
        {
            var chart = LoadRow(rows.FirstOrDefault(r => Same(r.Hash, h)));
            if (chart != null) { door = DoorHash; return chart; }
        }
        return null;
    }

    /// <summary>Is there an authored chart for this track? The question TrackChartCache.Save asks
    /// before it writes, so a generated chart can never shadow one somebody wrote.</summary>
    public static bool ExistsFor(string? hash, string? cloudId = null) => Find(hash, cloudId, out _) != null;

    // ---- cloudId, ported verbatim from race/chartSource.js cloudIdFrom() ----

    /// <summary>A path segment this long, made only of id characters, is an id and not a word.</summary>
    private const int IdMin = 20;
    private static readonly Regex Uuid = new(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Idish = new(@"^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex Extension = new(@"\.[a-z0-9]{2,4}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The stable name of a file on the CDN, and the key an author writes in the index. A file name
    /// can be renamed, an id segment cannot: if any segment of the path is a uuid or is
    /// <see cref="IdMin"/> characters or more of id characters, the LAST such segment is the id.
    /// Otherwise it is the last segment with its extension taken off. Lower cased either way, so an
    /// index a person types by hand does not have to care about case. A url this cannot parse
    /// answers "", the same as the page.
    /// </summary>
    public static string CloudIdFrom(string? url)
    {
        Uri uri;
        try { uri = new Uri(url ?? "", UriKind.Absolute); }
        catch { return ""; }

        var segments = uri.AbsolutePath.Split('/')
            .Select(s => { try { return Uri.UnescapeDataString(s); } catch { return s; } })
            .Where(s => s.Length > 0)
            .ToList();
        if (segments.Count == 0) return "";

        for (int i = segments.Count - 1; i >= 0; i--)
        {
            string s = segments[i];
            if (Uuid.IsMatch(s) || (s.Length >= IdMin && Idish.IsMatch(s))) return s.ToLowerInvariant();
        }
        return Extension.Replace(segments[^1], "").ToLowerInvariant();
    }

    // ---- the player's own folder ----

    /// <summary>key (hash or cloudId, lower case) -> the file it was read out of.</summary>
    private static Dictionary<string, string> _userIndex = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The folder stamp the index was built off. A file appearing or going restamps it.</summary>
    private static DateTime _userStamp = DateTime.MinValue;
    private static bool _userScanned;
    private static readonly object UserLock = new();

    private static TrackChart? FindInUserFolder(string hash, string cloudId)
    {
        Dictionary<string, string> index;
        lock (UserLock)
        {
            RescanUserFolderIfStale();
            index = _userIndex;
        }
        // The chart is re-read on the hit rather than held, so editing one in place takes effect on
        // the next track without a restart.
        if (cloudId.Length > 0 && index.TryGetValue(cloudId, out var byId)) return Load(byId);
        if (hash.Length > 0 && index.TryGetValue(hash, out var byHash)) return Load(byHash);
        return null;
    }

    private static void RescanUserFolderIfStale()
    {
        try
        {
            string root = UserRoot;
            if (!Directory.Exists(root))
            {
                if (_userScanned && _userIndex.Count == 0) return;
                _userIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _userScanned = true;
                _userStamp = DateTime.MinValue;
                return;
            }
            var stamp = Directory.GetLastWriteTimeUtc(root);
            if (_userScanned && stamp == _userStamp) return;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
            {
                var chart = Load(file);
                if (!IsAuthored(chart)) continue;
                string h = (chart!.Source?.Hash ?? "").Trim();
                string id = (chart.Source?.CloudId ?? "").Trim();
                if (h.Length > 0) map[h] = file;
                if (id.Length > 0) map[id] = file;
                if (h.Length == 0 && id.Length == 0)
                    App.Logger?.Information("race-chart: authored chart {File} has no source.hash and no source.cloudId, so nothing can match it", Path.GetFileName(file));
            }
            _userIndex = map;
            _userStamp = stamp;
            _userScanned = true;
            if (map.Count > 0) App.Logger?.Information("race-chart: {Keys} authored key(s) in {Root}", map.Count, root);
        }
        catch (Exception ex)
        {
            App.Logger?.Information("race-chart: authored folder unreadable ({Message})", ex.Message);
            _userIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _userScanned = true;
        }
    }

    // ---- the shipped index ----

    /// <summary>One row of race/charts/index.json. See that folder's README for the format.</summary>
    private sealed class IndexRow
    {
        [JsonProperty("cloudId")] public string? CloudId { get; set; }
        [JsonProperty("hash")] public string? Hash { get; set; }
        [JsonProperty("title")] public string? Title { get; set; }
        [JsonProperty("durationSec")] public double DurationSec { get; set; }
        /// <summary>Relative to race/, so "charts/&lt;name&gt;.chart.json".</summary>
        [JsonProperty("chart")] public string? Chart { get; set; }
    }

    private sealed class IndexFile
    {
        [JsonProperty("version")] public int Version { get; set; } = 1;
        [JsonProperty("tracks")] public List<IndexRow> Tracks { get; set; } = new();
    }

    private static List<IndexRow> _shipped = new();
    private static DateTime _shippedStamp = DateTime.MinValue;
    private static bool _shippedRead;
    private static readonly object ShippedLock = new();

    private static List<IndexRow> ShippedRows()
    {
        lock (ShippedLock)
        {
            try
            {
                string path = ShippedIndexPath;
                if (!File.Exists(path))
                {
                    _shipped = new List<IndexRow>();
                    _shippedRead = true;
                    return _shipped;
                }
                var stamp = File.GetLastWriteTimeUtc(path);
                if (_shippedRead && stamp == _shippedStamp) return _shipped;

                var file = JsonConvert.DeserializeObject<IndexFile>(File.ReadAllText(path));
                _shipped = (file?.Tracks ?? new List<IndexRow>())
                    .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Chart))
                    .ToList();
                _shippedStamp = stamp;
                _shippedRead = true;
                if (_shipped.Count > 0) App.Logger?.Information("race-chart: {N} authored track(s) in the shipped index", _shipped.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Information("race-chart: shipped chart index unreadable ({Message})", ex.Message);
                _shipped = new List<IndexRow>();
                _shippedRead = true;
            }
            return _shipped;
        }
    }

    private static bool Same(string? a, string b)
        => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The chart a row points at, or null. A row is a path into the shipped race folder and
    /// nowhere else: one that climbs out of it is a broken index, not a file to open.</summary>
    private static TrackChart? LoadRow(IndexRow? row)
    {
        if (row?.Chart == null) return null;
        try
        {
            string root = Path.GetFullPath(RaceWebRoot);
            string full = Path.GetFullPath(Path.Combine(root, row.Chart.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                App.Logger?.Information("race-chart: index row {Chart} points outside the race folder, ignored", row.Chart);
                return null;
            }
            var chart = Load(full);
            if (chart == null) return null;
            if (!IsAuthored(chart))
            {
                App.Logger?.Information("race-chart: index row {Chart} is not marked hand: true, ignored", row.Chart);
                return null;
            }
            // The chart's own name wins; the row's title is the fallback, per the README.
            if (string.IsNullOrWhiteSpace(chart.Source.Name) && !string.IsNullOrWhiteSpace(row.Title))
                chart.Source.Name = row.Title!;
            return chart;
        }
        catch (Exception ex)
        {
            App.Logger?.Information("race-chart: index row {Chart} unreadable ({Message})", row.Chart, ex.Message);
            return null;
        }
    }

    /// <summary>Read a chart file. A newer chart version is loaded rather than refused: the model's
    /// extension bags carry whatever this build has no property for, and the page normalises.</summary>
    private static TrackChart? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var chart = JsonConvert.DeserializeObject<TrackChart>(File.ReadAllText(path));
            if (chart == null) return null;
            if (chart.Version != TrackChart.CurrentVersion)
                App.Logger?.Information("race-chart: authored chart {File} is version {V}, this build writes {Cur}",
                    Path.GetFileName(path), chart.Version, TrackChart.CurrentVersion);
            return chart;
        }
        catch (Exception ex)
        {
            App.Logger?.Information("race-chart: authored chart {File} unreadable ({Message})", Path.GetFileName(path), ex.Message);
            return null;
        }
    }
}
