using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// Owns the online side of one consumer's remote pool: which channels (subreddits) are in
/// play, which one the next batch comes from, and what that consumer's dwell says about each.
///
/// Niches are just named bundles of subreddit slugs — scrolller's taxonomy IS the niche
/// taxonomy, so there is no server-side curation pipeline. The catalog below was
/// existence-checked against the live API (2026-08-10 for the first eight, 2026-08-11 for
/// the four the web port added); a channel that stops resolving simply retires itself for
/// the session, so a stale entry costs one wasted request, not a broken feed.
///
/// Learning happens here, not in the page: the page's per-segment EWMA is useless for
/// clips seen exactly once, so dwell aggregates per CHANNEL and biases which channel the
/// next fetch draws from. That is the whole "For You" algorithm for online content.
///
/// MULTI-TENANT: rotation state (iterators, dead flags, backoff) and the dwell EWMAs are
/// per CONSUMER, not per process. The For You feed, flashes, intake and DTRH each pick
/// their own niches, so a single shared rotation would have them fighting over one set of
/// iterators and cross-contaminating each other's taste. Get an instance from
/// <see cref="For"/> (or <see cref="Fyp"/>); the registry owns the lifetimes.
/// <see cref="Catalog"/> and <see cref="SanitizeSub"/> stay static — shared taxonomy, not
/// per-tenant state.
/// </summary>
internal sealed class FypOnlineCoordinator
{
    public sealed record Niche(string Id, string Label, string[] Subs);

    /// <summary>The single authority for the niche taxonomy across all four scrolller ports
    /// (desktop, mobile, web intake, web spiral-express) — the others hand-sync to this list.
    /// Subs verified live: the first eight 2026-08-10, the last four 2026-08-11 (existence +
    /// item counts); unresolvable ones fail soft. Order is display order in every picker.
    ///
    /// This is the union of what the ports had drifted to, so no port loses a niche: the four
    /// below came from web spiral-express, and "amateur" is the one the web ports still lack —
    /// it travels the other way on the next hand-sync.</summary>
    public static readonly Niche[] Catalog =
    {
        new("hypno",      "Hypno",       new[] { "EroticHypnosis", "sissyhypno" }),
        new("bimbo",      "Bimbo",       new[] { "bimbo", "bimbofication" }),
        new("sissy",      "Sissy",       new[] { "Sissies", "sissyhypno" }),
        new("hentai",     "Hentai",      new[] { "hentai", "rule34" }),
        new("censored",   "Censored",    new[] { "censoredporn" }),
        new("bbc",        "BBC",         new[] { "BBCSluts" }),
        new("goon",       "Goon",        new[] { "GOONED" }),
        new("amateur",    "Amateur",     new[] { "RealGirls", "TittyDrop" }),
        // The four the web spiral-express port added. Both relapse subs were live-verified
        // video-rich 2026-08-11 (91 / 630 videos); r/prematurefetish was requested alongside
        // them but does not exist on scrolller.
        new("relapse",    "Relapse",     new[] { "pornrelapsed", "stillstraightcaptions" }),
        new("bambisleep", "Bambi Sleep", new[] { "BambiSleep" }),
        new("futa",       "Futa",        new[] { "futanari" }),
        new("cosplay",    "Cosplay",     new[] { "nsfwcosplay" }),
    };

    /// <summary>Consumer id of the For You feed — the tenant that predates multi-tenancy and
    /// therefore keeps the original <c>fyp_online.json</c> store.</summary>
    public const string FypConsumerId = "fyp";

    private const int MaxChannels = 64;
    private const int MaxChannelTriesPerBatch = 3;
    private const double EwmaAlpha = 0.2;
    private const double EwmaCapMs = 30000;      // one clip watched ~30s is a maxed signal
    private const int ExploreViews = 3;          // channels with fewer views get a bonus

    // ---- tenant registry -----------------------------------------------------------
    // Consumers ask for their coordinator by id rather than holding one, so nothing has to
    // manage a lifetime and teardown is one SaveAll().

    private static readonly object RegistryLock = new();
    private static readonly Dictionary<string, FypOnlineCoordinator> Instances =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The coordinator for one consumer, created on first ask and cached forever.
    /// The first caller's channel provider and media kind win — a consumer id IS its
    /// configuration, so two different shapes under one id would be a bug, not a feature.</summary>
    public static FypOnlineCoordinator For(string consumerId, Func<IReadOnlyList<string>> channelProvider,
        FeedMediaKind kind)
    {
        lock (RegistryLock)
        {
            if (!Instances.TryGetValue(consumerId, out var existing))
            {
                existing = new FypOnlineCoordinator(consumerId, channelProvider, kind);
                Instances[consumerId] = existing;
            }
            return existing;
        }
    }

    /// <summary>The For You feed's coordinator: the user's FYP niche selection, video only.</summary>
    public static FypOnlineCoordinator Fyp => For(FypConsumerId, FypChannels, FeedMediaKind.Video);

    /// <summary>Session teardown: persist every live tenant's dwell EWMAs.</summary>
    public static void SaveAll()
    {
        List<FypOnlineCoordinator> all;
        lock (RegistryLock) all = Instances.Values.ToList();
        foreach (var c in all) c.Save();
    }

    /// <summary>The blocklist changed (it is app-wide, unlike niche selection): drop every
    /// tenant's rotation state so blocked subs leave the active sets immediately.</summary>
    public static void ResetAllChannels()
    {
        List<FypOnlineCoordinator> all;
        lock (RegistryLock) all = Instances.Values.ToList();
        foreach (var c in all) c.ResetChannels();
    }

    // ---- instance ------------------------------------------------------------------

    private readonly string _consumerId;
    private readonly Func<IReadOnlyList<string>> _channelProvider;
    private readonly FeedMediaKind _kind;
    private readonly object _lock = new();
    private readonly IFeedSource _source = new ScrolllerSource();
    private readonly Random _rng = new();
    private readonly string _storePath;
    private Dictionary<string, FeedChannelState> _channels = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, (double EwmaMs, int Views)> _dwell = new(StringComparer.OrdinalIgnoreCase);
    private bool _dwellLoaded;

    private FypOnlineCoordinator(string consumerId, Func<IReadOnlyList<string>> channelProvider, FeedMediaKind kind)
    {
        _consumerId = consumerId;
        _channelProvider = channelProvider;
        _kind = kind;
        // The FYP tenant keeps the file it has always written; anyone else gets their own,
        // so nobody inherits (or clobbers) a stranger's learned taste.
        _storePath = Path.Combine(App.UserDataPath,
            string.Equals(consumerId, FypConsumerId, StringComparison.OrdinalIgnoreCase)
                ? "fyp_online.json"
                : $"remote_online_{FileKey(consumerId)}.json");
    }

    /// <summary>Consumer id → a filename fragment that can't escape the data folder.</summary>
    private static string FileKey(string consumerId)
    {
        var clean = new string(consumerId.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
        return clean.Length > 0 ? clean.ToLowerInvariant() : "consumer";
    }

    /// <summary>The subreddits this consumer's settings currently select, before the
    /// blocklist and the dedupe cap. Never throws: a provider that fails means "no
    /// channels", which <see cref="ActiveChannels"/> turns into the catalog fallback.</summary>
    private IReadOnlyList<string> ProvidedChannels()
    {
        try { return _channelProvider() ?? (IReadOnlyList<string>)Array.Empty<string>(); }
        catch (Exception ex)
        {
            App.Logger?.Debug("FypOnline[{Consumer}]: channel provider failed: {E}", _consumerId, ex.Message);
            return Array.Empty<string>();
        }
    }

    /// <summary>Niche ids + custom subs → channel names, in catalog order. Shared taxonomy
    /// resolution: every consumer's channel provider ends up here.</summary>
    public static List<string> ResolveChannels(IEnumerable<string>? nicheIds, IEnumerable<string>? customSubs)
    {
        var selected = new HashSet<string>(nicheIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var subs = new List<string>();
        foreach (var n in Catalog)
            if (selected.Contains(n.Id))
                subs.AddRange(n.Subs);
        foreach (var c in customSubs ?? Enumerable.Empty<string>())
        {
            var clean = SanitizeSub(c);
            if (clean != null) subs.Add(clean);
        }
        return subs;
    }

    /// <summary>The For You feed's channel provider: its own niche selection and custom subs.</summary>
    private static IReadOnlyList<string> FypChannels()
    {
        var s = App.Settings?.Current;
        return ResolveChannels(s?.FypOnlineNiches, s?.FypOnlineCustomSubs);
    }

    /// <summary>Channel names currently in play for this consumer (its selection minus the
    /// user's blocked subs; the first catalog niche when nothing is selected, so "Online" can
    /// never mean "nothing" — unless the user blocked that too, which is their call).</summary>
    public List<string> ActiveChannels()
    {
        var blocked = BlockedSubs();
        var distinct = ProvidedChannels()
            .Where(name => !blocked.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxChannels)
            .ToList();
        if (distinct.Count == 0)
            distinct.AddRange(Catalog[0].Subs.Where(name => !blocked.Contains(name)));
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

    // ---- blocklist (B4) ------------------------------------------------------------
    //
    // The ONE choke point: blocked subs never reach an active channel set and blocked entry
    // ids never reach a pool, so every consumer built on this class inherits the filter for
    // free. Deliberately NOT an NSFW filter — scrolller's isNsfw is fetched by the query and
    // read by nothing here (or in the mobile / web ports), because the whole niche catalog is
    // adult: filtering on it would empty the pool rather than shape it. Owner decision
    // 2026-08-12; leave isNsfw unread, it is not an oversight.

    private static HashSet<string> BlockedSubs()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in App.Settings?.Current?.RemoteMediaBlockedSubs ?? new List<string>())
            if (!string.IsNullOrWhiteSpace(s)) set.Add(s);
        return set;
    }

    private static HashSet<string> BlockedIds()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in App.Settings?.Current?.RemoteMediaBlockedIds ?? new List<string>())
            if (!string.IsNullOrWhiteSpace(s)) set.Add(s);
        return set;
    }

    /// <summary>Block a whole subreddit, everywhere, and persist immediately (settings saving
    /// is manual). Rotation state is dropped so the sub leaves every active set at once.</summary>
    public static void BlockSub(string? raw)
    {
        var sub = SanitizeSub(raw);
        var s = App.Settings?.Current;
        if (sub == null || s == null) return;
        try
        {
            var list = new List<string>(s.RemoteMediaBlockedSubs) { sub };
            s.RemoteMediaBlockedSubs = list;      // the setter sanitizes, dedupes and caps
            App.Settings?.Save();
            ResetAllChannels();
            App.Logger?.Information("[remote media] blocked r/{Sub}", sub);
        }
        catch (Exception ex) { App.Logger?.Debug("FypOnline: block sub failed: {E}", ex.Message); }
    }

    /// <summary>Block one entry by id ("scrolller/&lt;sub&gt;/&lt;post&gt;") and persist. Channels
    /// are left alone — one dud post says nothing about the rest of its subreddit.</summary>
    public static void BlockEntry(string? entryId)
    {
        var s = App.Settings?.Current;
        if (string.IsNullOrWhiteSpace(entryId) || s == null) return;
        try
        {
            var list = new List<string>(s.RemoteMediaBlockedIds) { entryId.Trim() };
            s.RemoteMediaBlockedIds = list;
            App.Settings?.Save();
            App.Logger?.Information("[remote media] blocked entry {Id}", entryId);
        }
        catch (Exception ex) { App.Logger?.Debug("FypOnline: block entry failed: {E}", ex.Message); }
    }

    /// <summary>Entry ids look like "scrolller/&lt;sub&gt;/&lt;post&gt;"; the middle segment is
    /// re-checked against the blocked subs so an entry can't slip through on a channel that
    /// was blocked after the page was already in flight.</summary>
    private static List<FypAssetManifest.Entry> ApplyBlocklist(List<FypAssetManifest.Entry> entries)
    {
        var ids = BlockedIds();
        var subs = BlockedSubs();
        if (ids.Count == 0 && subs.Count == 0) return entries;
        var kept = new List<FypAssetManifest.Entry>(entries.Count);
        foreach (var e in entries)
        {
            if (ids.Contains(e.Id)) continue;
            var parts = e.Id.Split('/');
            if (parts.Length >= 3 && subs.Contains(parts[1])) continue;
            kept.Add(e);
        }
        return kept;
    }

    /// <summary>Niche selection / custom subs / blocklist changed: drop rotation state
    /// (iterators, dead flags, backoff) but keep the learned dwell — taste survives a
    /// channel reshuffle.</summary>
    public void ResetChannels()
    {
        lock (_lock) _channels.Clear();
    }

    /// <summary>Session teardown: persist the dwell EWMAs.</summary>
    public void Save()
    {
        lock (_lock)
        {
            if (!_dwellLoaded) return;
            try
            {
                var o = new JObject();
                foreach (var (k, v) in _dwell)
                    o[k] = new JObject { ["ewmaMs"] = Math.Round(v.EwmaMs), ["views"] = v.Views };
                Directory.CreateDirectory(App.UserDataPath);
                File.WriteAllText(_storePath, new JObject { ["channels"] = o }.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("FypOnline[{Consumer}]: dwell save failed: {E}", _consumerId, ex.Message);
            }
        }
    }

    /// <summary>A remote clip was watched ≥5s: fold its dwell into the channel EWMA.
    /// segId shape "scrolller/&lt;sub&gt;/&lt;post&gt;:k".</summary>
    public void RecordDwell(string segId, long dwellMs)
    {
        var parts = segId.Split('/');
        if (parts.Length < 3) return;
        var sub = parts[1];
        double capped = Math.Min(dwellMs, EwmaCapMs);
        lock (_lock)
        {
            EnsureDwellLoaded();
            _dwell.TryGetValue(sub, out var cur);
            double ewma = cur.Views == 0 ? capped : EwmaAlpha * capped + (1 - EwmaAlpha) * cur.EwmaMs;
            _dwell[sub] = (ewma, cur.Views + 1);
        }
    }

    /// <summary>
    /// Fetch the next batch of entries from the rotation, in this consumer's media kind.
    /// Returns (entries, error): a null error with zero entries means "nothing new right
    /// now" (all channels dry, cooling down or fully blocked), a non-null error means the
    /// API itself is unreachable. Runs off the UI thread.
    /// </summary>
    public Task<(List<FypAssetManifest.Entry> Entries, string? Error)> FetchBatchAsync(CancellationToken ct)
        => FetchBatchAsync(_kind, ct);

    /// <summary>As above, for a consumer that needs a kind other than the one it registered
    /// with (a surface showing both stills and clips asks twice, not once with Any).</summary>
    public async Task<(List<FypAssetManifest.Entry> Entries, string? Error)> FetchBatchAsync(
        FeedMediaKind kind, CancellationToken ct)
    {
        List<FeedChannelState> order;
        lock (_lock)
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
        // Dead is forever (the subreddit doesn't resolve at all); a channel serving a backoff
        // cooldown is skipped for THIS batch and comes back on its own, so a flaky minute no
        // longer shrinks the pool for the whole session.
        //
        // Both are filtered BEFORE the take, not skipped inside the loop: a skip used to be
        // rare (three hard failures) but a cooldown starts at two seconds, so counting cooling
        // channels against the batch's three attempts would return an empty batch precisely
        // when the feed is already struggling.
        foreach (var channel in order.Where(c => !c.Dead && !c.InBackoff).Take(MaxChannelTriesPerBatch))
        {
            var page = await _source.FetchPageAsync(channel, kind, ct).ConfigureAwait(false);
            if (page == null)
            {
                channel.NoteFailure("fetch-failed");
                sawTransportFailure = true;
                App.Logger?.Debug("[FYP online] r/{Sub} failed ({N}) — cooling down until {Until:HH:mm:ss}Z",
                    channel.Name, channel.Failures, channel.NextTryAtUtc);
                continue;
            }
            channel.NoteSuccess();
            // A drained iterator restarts from the top: RANDOM sort deals different pages
            // and the page's cooldown machinery absorbs the occasional repeat.
            channel.Iterator = page.NextIterator;
            var entries = ApplyBlocklist(page.Entries);
            if (entries.Count > 0)
            {
                App.Logger?.Information("[FYP online] +{N} entries from r/{Sub} ({Consumer})",
                    entries.Count, channel.Name, _consumerId);
                return (entries, null);
            }
        }
        return (new List<FypAssetManifest.Entry>(), sawTransportFailure ? "offline" : null);
    }

    /// <summary>Channels sorted by a weighted-random draw over dwell taste: a channel the
    /// user lingers on reaches ~4x the base weight; barely-explored ones get a bonus.</summary>
    private List<FeedChannelState> PickOrder(List<string> active)
    {
        var scored = new List<(FeedChannelState St, double Key)>();
        foreach (var name in active)
        {
            var st = _channels[name];
            _dwell.TryGetValue(name, out var d);
            double w = 1 + 3 * Math.Min(1, d.EwmaMs / EwmaCapMs) + (d.Views < ExploreViews ? 1 : 0);
            // Exponential-race sampling: sorting by -log(u)/w draws without replacement
            // proportionally to weight, giving a full fallback order in one pass.
            double u = 1 - _rng.NextDouble();
            scored.Add((st, -Math.Log(u) / w));
        }
        return scored.OrderBy(x => x.Key).Select(x => x.St).ToList();
    }

    private void EnsureDwellLoaded()
    {
        if (_dwellLoaded) return;
        _dwellLoaded = true;
        try
        {
            if (!File.Exists(_storePath)) return;
            var o = JObject.Parse(File.ReadAllText(_storePath));
            if (o["channels"] is JObject ch)
                foreach (var p in ch.Properties())
                    _dwell[p.Name] = ((double?)p.Value["ewmaMs"] ?? 0, (int?)p.Value["views"] ?? 0);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("FypOnline[{Consumer}]: dwell load failed: {E}", _consumerId, ex.Message);
        }
    }
}
