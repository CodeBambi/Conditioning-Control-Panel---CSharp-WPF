using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Race;

/// <summary>Purchases, not subscription tier, open the race. Catalogue rows retain their own grants.
/// <para>ARMED since 2026-09-18 - see <see cref="PurchaseDoorArmed"/>. The rules below are the
/// race-06/race-07 stack's, kept whole and tested, and they now decide who gets in.</para></summary>
public static class RacingAccess
{
    /// <summary>
    /// THE DOOR IS CLOSED, and this constant is the only place that says so.
    ///
    /// <para>History, short. On 2026-09-17 the owner removed Racing Thoughts' tier gate for open
    /// testing (208711cfc). The race-06/race-07 stack landed a day later proposing a PURCHASE gate
    /// in its place and was disarmed here at the merge. Later on 2026-09-18 the owner reversed
    /// that: Racing Thoughts is a Back Room unlock. A player who owns no racing track does not
    /// get the race, and the launcher shows a mystery card in its place until the first track is
    /// bought at the counter (PrizeGrants.RacingTrack(n), see LauncherCatalogue).</para>
    ///
    /// <para>Armed in ONE place rather than six. Six call sites ask this class whether the race
    /// may open: CaucusHostService.Launch and its grant watcher and both cloud hooks, and
    /// BackRoomHostService's cabinet handoff (OnRoomMessage, OnRoomClosed). The `--race` dev arg
    /// and BtnStartRace_Click both go through CaucusHostService.Launch, so they inherit the door
    /// without asking themselves.</para>
    ///
    /// <para>Flip this to false and the purchase door opens everywhere at once: launching, the
    /// cabinet, the per-track catalogue and the BambiCloud source check. Nothing else needs
    /// touching, and Tests/ConditioningControlPanel.Tests/RacingAccessTests.cs still asserts the
    /// rules either way, because the tests drive <see cref="AllowsLaunch"/> and
    /// <see cref="AllowsCloud"/> directly and those are left pure on purpose.</para>
    ///
    /// <para>NOT part of this. One payout per run is a different rule with a different owner -
    /// <see cref="RaceRunLifecycle"/> latches the active run and never asks anything about
    /// ownership - and it came in from the stack untouched and in force, because it is anti-abuse
    /// and the owner wants it.</para>
    /// </summary>
    private const bool PurchaseDoorArmed = true;

    /// <summary>Every original track, which is what an open door hands the page.</summary>
    private static int[] AllTracks => Enumerable.Range(0, PrizeGrants.RacingTrackMax + 1).ToArray();

    public static bool CanLaunch => !PurchaseDoorArmed || AllowsLaunch(PrizeGrants.IsGranted);

    /// <summary>The tracks the page may pick from. An open door hands over the whole catalogue: the
    /// page treats an EMPTY list as "you own nothing" and hides every built-in level (CONTRACT
    /// 971, smoke/ownership-check.mjs), so sending real grants here while the door is open would
    /// gate the game's content even though its door is unlocked, which is the same refusal one
    /// screen later.</summary>
    public static int[] OwnedTracks => PurchaseDoorArmed
        ? Enumerable.Range(0, PrizeGrants.RacingTrackMax + 1)
            .Where(n => PrizeGrants.IsGranted(PrizeGrants.RacingTrack(n))).ToArray()
        : AllTracks;

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

    /// <summary>A BambiCloud source the player asked to race to. Open door, any source: the authored
    /// charts are the game's own content, so refusing one is refusing the game.</summary>
    public static bool CanOpenCloud(string? url) =>
        !PurchaseDoorArmed || AllowsCloud(url, Catalog.Value, PrizeGrants.IsGranted);
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
