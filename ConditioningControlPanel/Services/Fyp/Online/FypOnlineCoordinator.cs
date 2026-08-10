using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// Owns the online side of the For You feed: which channels (subreddits) are in play,
/// which one the next batch comes from, and what the user's dwell says about each.
///
/// Niches are just named bundles of subreddit slugs — scrolller's taxonomy IS the niche
/// taxonomy, so there is no server-side curation pipeline. The starter catalog below was
/// existence-checked against the live API on 2026-08-10; a channel that stops resolving
/// simply retires itself for the session, so a stale entry costs one wasted request, not
/// a broken feed.
///
/// Learning happens here, not in the page: the page's per-segment EWMA is useless for
/// clips seen exactly once, so dwell aggregates per CHANNEL (fyp_online.json) and biases
/// which channel the next fetch draws from. That is the whole "For You" algorithm for
/// online content.
/// </summary>
internal static class FypOnlineCoordinator
{
    public sealed record Niche(string Id, string Label, string[] Subs);

    /// <summary>Starter catalog. Subs verified live 2026-08-10 (existence + item counts);
    /// unresolvable ones fail soft. Order is display order in the options popover.</summary>
    public static readonly Niche[] Catalog =
    {
        new("hypno",    "Hypno",    new[] { "EroticHypnosis", "sissyhypno" }),
        new("bimbo",    "Bimbo",    new[] { "bimbo", "bimbofication" }),
        new("sissy",    "Sissy",    new[] { "Sissies", "sissyhypno" }),
        new("hentai",   "Hentai",   new[] { "hentai", "rule34" }),
        new("censored", "Censored", new[] { "censoredporn" }),
        new("bbc",      "BBC",      new[] { "BBCSluts" }),
        new("goon",     "Goon",     new[] { "GOONED" }),
        new("amateur",  "Amateur",  new[] { "RealGirls", "TittyDrop" }),
    };

    private const int MaxCustomSubs = 20;
    private const int MaxChannelTriesPerBatch = 3;
    private const double EwmaAlpha = 0.2;
    private const double EwmaCapMs = 30000;      // one clip watched ~30s is a maxed signal
    private const int ExploreViews = 3;          // channels with fewer views get a bonus

    private static readonly object Lock = new();
    private static readonly IFeedSource Source = new ScrolllerSource();
    private static Dictionary<string, FeedChannelState> _channels = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, (double EwmaMs, int Views)> _dwell = new(StringComparer.OrdinalIgnoreCase);
    private static bool _dwellLoaded;
    private static readonly Random Rng = new();

    private static string StorePath => Path.Combine(App.UserDataPath, "fyp_online.json");

    /// <summary>Channel names currently in play (selected niches ∪ custom subs; the first
    /// catalog niche when nothing is selected, so "Online" can never mean "nothing").</summary>
    public static List<string> ActiveChannels()
    {
        var s = App.Settings?.Current;
        var selected = new HashSet<string>(s?.FypOnlineNiches ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var subs = new List<string>();
        foreach (var n in Catalog)
            if (selected.Contains(n.Id))
                subs.AddRange(n.Subs);
        foreach (var c in s?.FypOnlineCustomSubs ?? new List<string>())
        {
            var clean = SanitizeSub(c);
            if (clean != null) subs.Add(clean);
        }
        var distinct = subs.Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToList();
        if (distinct.Count == 0) distinct.AddRange(Catalog[0].Subs);
        return distinct;
    }

    /// <summary>"r/Name", urls and stray punctuation → bare subreddit name, or null.</summary>
    public static string? SanitizeSub(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        int idx = s.LastIndexOf("/r/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) s = s[(idx + 3)..];
        else if (s.StartsWith("r/", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        s = new string(s.TakeWhile(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
        return s.Length is >= 2 and <= 40 ? s : null;
    }

    /// <summary>Niche selection / custom subs changed: drop rotation state (iterators,
    /// dead flags) but keep the learned dwell — taste survives a channel reshuffle.</summary>
    public static void ResetChannels()
    {
        lock (Lock) _channels.Clear();
    }

    /// <summary>Session teardown: persist the dwell EWMAs.</summary>
    public static void Save()
    {
        lock (Lock)
        {
            if (!_dwellLoaded) return;
            try
            {
                var o = new JObject();
                foreach (var (k, v) in _dwell)
                    o[k] = new JObject { ["ewmaMs"] = Math.Round(v.EwmaMs), ["views"] = v.Views };
                Directory.CreateDirectory(App.UserDataPath);
                File.WriteAllText(StorePath, new JObject { ["channels"] = o }.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception ex) { App.Logger?.Debug("FypOnline: dwell save failed: {E}", ex.Message); }
        }
    }

    /// <summary>A remote clip was watched ≥5s: fold its dwell into the channel EWMA.
    /// segId shape "scrolller/&lt;sub&gt;/&lt;post&gt;:k".</summary>
    public static void RecordDwell(string segId, long dwellMs)
    {
        var parts = segId.Split('/');
        if (parts.Length < 3) return;
        var sub = parts[1];
        double capped = Math.Min(dwellMs, EwmaCapMs);
        lock (Lock)
        {
            EnsureDwellLoaded();
            _dwell.TryGetValue(sub, out var cur);
            double ewma = cur.Views == 0 ? capped : EwmaAlpha * capped + (1 - EwmaAlpha) * cur.EwmaMs;
            _dwell[sub] = (ewma, cur.Views + 1);
        }
    }

    /// <summary>
    /// Fetch the next batch of entries from the rotation. Returns (entries, error): a
    /// null error with zero entries means "nothing new right now" (all channels dry),
    /// a non-null error means the API itself is unreachable. Runs off the UI thread.
    /// </summary>
    public static async Task<(List<FypAssetManifest.Entry> Entries, string? Error)> FetchBatchAsync(CancellationToken ct)
    {
        List<FeedChannelState> order;
        lock (Lock)
        {
            EnsureDwellLoaded();
            var active = ActiveChannels();
            // Rebuild the state map to the active set (keeps live iterators for kept subs).
            var next = new Dictionary<string, FeedChannelState>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in active)
                next[name] = _channels.TryGetValue(name, out var st) ? st : new FeedChannelState { Name = name };
            _channels = next;
            order = PickOrder(active);
        }

        bool sawTransportFailure = false;
        foreach (var channel in order.Take(MaxChannelTriesPerBatch))
        {
            if (channel.Dead || channel.Failures >= 3) continue;
            var page = await Source.FetchPageAsync(channel, ct).ConfigureAwait(false);
            if (page == null) { channel.Failures++; sawTransportFailure = true; continue; }
            channel.Failures = 0;
            // A drained iterator restarts from the top: RANDOM sort deals different pages
            // and the page's cooldown machinery absorbs the occasional repeat.
            channel.Iterator = page.NextIterator;
            if (page.Entries.Count > 0)
            {
                App.Logger?.Information("[FYP online] +{N} entries from r/{Sub}", page.Entries.Count, channel.Name);
                return (page.Entries, null);
            }
        }
        return (new List<FypAssetManifest.Entry>(), sawTransportFailure ? "offline" : null);
    }

    /// <summary>Channels sorted by a weighted-random draw over dwell taste: a channel the
    /// user lingers on reaches ~4x the base weight; barely-explored ones get a bonus.</summary>
    private static List<FeedChannelState> PickOrder(List<string> active)
    {
        var scored = new List<(FeedChannelState St, double Key)>();
        foreach (var name in active)
        {
            var st = _channels[name];
            _dwell.TryGetValue(name, out var d);
            double w = 1 + 3 * Math.Min(1, d.EwmaMs / EwmaCapMs) + (d.Views < ExploreViews ? 1 : 0);
            // Exponential-race sampling: sorting by -log(u)/w draws without replacement
            // proportionally to weight, giving a full fallback order in one pass.
            double u = 1 - Rng.NextDouble();
            scored.Add((st, -Math.Log(u) / w));
        }
        return scored.OrderBy(x => x.Key).Select(x => x.St).ToList();
    }

    private static void EnsureDwellLoaded()
    {
        if (_dwellLoaded) return;
        _dwellLoaded = true;
        try
        {
            if (!File.Exists(StorePath)) return;
            var o = JObject.Parse(File.ReadAllText(StorePath));
            if (o["channels"] is JObject ch)
                foreach (var p in ch.Properties())
                    _dwell[p.Name] = ((double?)p.Value["ewmaMs"] ?? 0, (int?)p.Value["views"] ?? 0);
        }
        catch (Exception ex) { App.Logger?.Debug("FypOnline: dwell load failed: {E}", ex.Message); }
    }
}
