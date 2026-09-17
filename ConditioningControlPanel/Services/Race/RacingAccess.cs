using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Race;

/// <summary>Purchases, not subscription tier, open the race. Catalogue rows retain their own grants.</summary>
public static class RacingAccess
{
    public static bool CanLaunch => AllowsLaunch(PrizeGrants.IsGranted);
    public static int[] OwnedTracks => Enumerable.Range(0, PrizeGrants.RacingTrackMax + 1)
        .Where(n => PrizeGrants.IsGranted(PrizeGrants.RacingTrack(n))).ToArray();

    internal static bool AllowsLaunch(Func<string, bool> owns)
        => Enumerable.Range(0, PrizeGrants.RacingTrackMax + 1).Any(n => owns(PrizeGrants.RacingTrack(n)));

    internal static Dictionary<string, int> ParseCatalog(JToken json)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in json.SelectTokens("sets[*].levels[*]"))
        {
            var id = (string?)row["id"];
            if (row["trackNum"]?.Type != JTokenType.Integer || string.IsNullOrWhiteSpace(id)) continue;
            var n = (int)row["trackNum"]!;
            if (n is >= 0 and <= PrizeGrants.RacingTrackMax) result[id] = n;
        }
        return result;
    }

    internal static bool AllowsCloud(string? url, IReadOnlyDictionary<string, int>? catalog, Func<string, bool> owns)
    {
        if (!AllowsLaunch(owns) || catalog == null) return false;
        var id = AuthoredCharts.CloudIdFrom(url);
        return !catalog.TryGetValue(id, out var n) || owns(PrizeGrants.RacingTrack(n));
    }

    private static readonly Lazy<IReadOnlyDictionary<string, int>?> Catalog = new(() =>
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "web", "dtrh", "race", "levels.json");
            var rows = ParseCatalog(JToken.Parse(File.ReadAllText(path)));
            return rows.Count == PrizeGrants.RacingTrackMax + 1 ? rows : null;
        }
        catch (Exception ex) { Diag.Swallowed(ex, "race catalogue unavailable; cloud gate closed"); return null; }
    });

    public static bool CanOpenCloud(string? url) => AllowsCloud(url, Catalog.Value, PrizeGrants.IsGranted);
}

/// <summary>A denied browser source is rechecked on Play, never autoplayed by a purchase.</summary>
internal sealed class CloudTrackRetry
{
    private JObject? _track;
    public void Remember(JObject track) => _track = (JObject)track.DeepClone();
    public void Clear() => _track = null;
    public JObject? TakeIfAllowed(Func<string?, bool> allows)
    {
        if (_track == null || !allows((string?)_track["src"])) return null;
        var track = _track;
        _track = null;
        return track;
    }
}
