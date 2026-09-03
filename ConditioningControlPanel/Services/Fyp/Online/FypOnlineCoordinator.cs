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
    /// Every sub below was existence-checked against the live API on 2026-08-23 and carries its
    /// then-current <c>videoCount</c> in the comments; unresolvable ones fail soft (one wasted
    /// request, then the channel retires itself for the session). Order is display order in
    /// every picker.
    ///
    /// Ids are NEVER removed or renamed — saved settings and web share codes reference them —
    /// so a widened niche keeps its id (Bambi Sleep has 9 clips of its own, hence the two
    /// neighbours; the pickers' grey sub-expander shows the real list so nothing is hidden).
    /// "beta" was appended LAST for the same reason.</summary>
    public static readonly Niche[] Catalog =
    {
        new("hypno",      "Hypno",       new[] { "EroticHypnosis", "sissyhypno", "HypnoGoneWild", "HypnoHentai" }),
        new("bimbo",      "Bimbo",       new[] { "bimbo", "bimbofication", "bimbofetish" }),
        new("sissy",      "Sissy",       new[] { "Sissies", "sissyhypno", "sissycaptions" }),
        new("hentai",     "Hentai",      new[] { "hentai", "rule34", "nsfwanimegifs", "ecchi" }),
        // The ceiling for this niche: censoredporn (64 videos) + Censored_Porn (121) is
        // everything scrolller has — a dozen plausible neighbours (BlurredPorn,
        // censoredcaptions, CensoredHentai, BetaSafePorn...) simply do not exist there.
        new("censored",   "Censored",    new[] { "censoredporn", "Censored_Porn" }),
        new("bbc",        "BBC",         new[] { "BBCSluts", "interracial_porn", "QOS" }),
        new("goon",       "Goon",        new[] { "GOONED", "GoonCaves", "edging" }),
        new("amateur",    "Amateur",     new[] { "RealGirls", "TittyDrop", "gonewild" }),
        new("relapse",    "Relapse",     new[] { "pornrelapsed", "stillstraightcaptions" }),
        // 9 videos on its own, so it borrows the two nearest hypno communities rather than
        // exhausting in the first minute. r/Bambi_Sleep and friends do not exist on scrolller.
        new("bambisleep", "Bambi Sleep", new[] { "BambiSleep", "HypnoGoneWild", "EroticHypnosis" }),
        new("futa",       "Futa",        new[] { "futanari" }),
        new("cosplay",    "Cosplay",     new[] { "nsfwcosplay", "cosplaygirls", "CosplayLewd", "cosplaybutts" }),
        // Appended 2026-08-23; new ids go on the END so existing selections keep their order.
        new("beta",       "Beta / SPH",  new[] { "sph", "SmallPenisHumiliation" }),
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

    /// <summary>Probes have no tenant (the name was typed a second ago and may never become a
    /// channel), so they get their own stateless source instance; the politeness gate inside
    /// it is static, so this shares the queue with every real fetch.</summary>
    private static readonly IFeedSource ProbeSource = new ScrolllerSource();

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

    /// <summary>A channel-shaping setting changed app-wide (media source, niche selection,
    /// custom subs): drop every tenant's rotation state so the new set takes effect at once.</summary>
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
        _storePath = Path.Combine(CorePaths.UserData,
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
    /// dedupe cap. Never throws: a provider that fails means "no
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

    /// <summary>Channel names currently in play for this consumer (its selection, with the
    /// first catalog niche as fallback when nothing is selected, so "Online" can never mean
    /// "nothing").</summary>
    public List<string> ActiveChannels()
    {
        var distinct = ProvidedChannels()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxChannels)
            .ToList();
        if (distinct.Count == 0)
            distinct.AddRange(Catalog[0].Subs);
        return distinct;
    }

    /// <summary>"r/Name", urls and stray punctuation → bare subreddit name, or null.</summary>
    public static string? SanitizeSub(string? raw) => SubredditName.Sanitize(raw);   // the rule lives in Core

    // There is deliberately NO content filter between the query and the pool: picking the
    // subreddits IS the content control. Scrolller's isNsfw is fetched by the query and read
    // by nothing here (or in the mobile / web ports), because the whole niche catalog is
    // adult: filtering on it would empty the pool rather than shape it. Owner decision
    // 2026-08-12; leave isNsfw unread, it is not an oversight. (A per-sub/per-post blocklist
    // existed here until 2026-08-14 but nothing ever fed it, so it was removed.)

    /// <summary>Niche selection / custom subs changed: drop rotation state (iterators, dead
    /// flags, backoff, and the exhaustion flags with their served-id sets) but keep the learned
    /// dwell — taste survives a channel reshuffle. Clearing the map IS the reset: the next
    /// fetch rebuilds every channel from scratch.</summary>
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
                Directory.CreateDirectory(CorePaths.UserData);
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
    /// One batch plus everything a UI needs to explain it. <see cref="Entries"/> are FRESH
    /// only (ids this channel has not served this session); <see cref="Dry"/> means no channel
    /// can produce right now (all dead, exhausted or cooling), which is the difference between
    /// "wait, it's loading" and "you have seen everything in this niche"; <see cref="PoolTotal"/>
    /// is how many distinct remote ids the whole rotation has handed out, so the page can say
    /// the number out loud and scale its reshuffle cooldown to the real pool size.
    /// </summary>
    public readonly record struct FeedBatch(
        List<FypAssetManifest.Entry> Entries, string? Error, bool Dry, int PoolTotal);

    /// <summary>
    /// Fetch the next batch of entries from the rotation, in this consumer's media kind.
    /// Returns (entries, error): a null error with zero entries means "nothing new right
    /// now" (all channels dry or cooling down), a non-null error means the
    /// API itself is unreachable. Runs off the UI thread.
    ///
    /// This two-value shape is what every consumer except the FYP page wants (they all dedupe
    /// again on their own side and have nothing to render a dry verdict with); it delegates to
    /// <see cref="FetchBatchDetailedAsync(FeedMediaKind, CancellationToken)"/>.
    /// </summary>
    public Task<(List<FypAssetManifest.Entry> Entries, string? Error)> FetchBatchAsync(CancellationToken ct)
        => FetchBatchAsync(_kind, ct);

    /// <summary>As above, for a consumer that needs a kind other than the one it registered
    /// with (a surface showing both stills and clips asks twice, not once with Any).</summary>
    public async Task<(List<FypAssetManifest.Entry> Entries, string? Error)> FetchBatchAsync(
        FeedMediaKind kind, CancellationToken ct)
    {
        var batch = await FetchBatchDetailedAsync(kind, ct).ConfigureAwait(false);
        return (batch.Entries, batch.Error);
    }

    /// <summary>The full batch result — see <see cref="FeedBatch"/>. Same fetch, more answer.</summary>
    public Task<FeedBatch> FetchBatchDetailedAsync(CancellationToken ct)
        => FetchBatchDetailedAsync(_kind, ct);

    /// <summary>The full batch result for an explicit media kind.</summary>
    public async Task<FeedBatch> FetchBatchDetailedAsync(FeedMediaKind kind, CancellationToken ct)
    {
        List<FeedChannelState> order;
        lock (_lock)
        {
            EnsureDwellLoaded();
            var active = ActiveChannels();
            // Rebuild the state map to the active set (keeps live iterators for kept subs).
            // Rebuilding is also what clears exhaustion on ResetChannels(): a dropped sub loses
            // its state entirely, and a re-picked one comes back as a virgin channel.
            var next = new Dictionary<string, FeedChannelState>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in active)
                next[name] = _channels.TryGetValue(name, out var st) ? st : new FeedChannelState { Name = name };
            _channels = next;
            foreach (var st in next.Values) st.ReviveIfDue();
            order = PickOrder(active);
        }

        bool sawTransportFailure = false;
        // Dead is forever (the subreddit doesn't resolve at all); a channel serving a backoff
        // cooldown is skipped for THIS batch and comes back on its own, so a flaky minute no
        // longer shrinks the pool for the whole session. Exhaustion is the third flavour: the
        // channel answers fine, it just has nothing left we have not already served.
        //
        // All three are filtered BEFORE the take, not skipped inside the loop: a skip used to be
        // rare (three hard failures) but a cooldown starts at two seconds, so counting cooling
        // channels against the batch's three attempts would return an empty batch precisely
        // when the feed is already struggling.
        foreach (var channel in order.Where(c => !c.Dead && !c.InBackoff && !c.InExhaustion)
                                     .Take(MaxChannelTriesPerBatch))
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
            // A drained iterator restarts from the top: RANDOM sort deals different pages and
            // the page's cooldown machinery absorbs the occasional repeat — right up to the
            // point where the whole sub fits inside what we have already served, which is what
            // the fresh count below is for.
            channel.Iterator = page.NextIterator;

            // Mutating filter on purpose: NoteServed records the id AND reports whether it was
            // new. Dedupe is per channel per consumer, which is what every consumer wants —
            // they all dedupe again anyway, and re-serving is the bug being fixed here.
            var fresh = page.Entries.Where(e => channel.NoteServed(e.Id)).ToList();
            if (fresh.Count > 0) channel.DryPages = 0;
            else if (page.Entries.Count > 0) channel.DryPages++;

            // Two all-repeat pages in a row, or a wrapped iterator that produced nothing new:
            // the channel has been walked. (One dry page alone is not proof — RANDOM sort can
            // legitimately re-deal a page we already hold from a mid-sized sub.)
            if (channel.DryPages >= 2 || (page.NextIterator == null && fresh.Count == 0))
            {
                channel.NoteExhausted();
                App.Logger?.Information(
                    "[FYP online] r/{Sub} exhausted after {N} unique ids ({Consumer}) — reshuffling for 10 min",
                    channel.Name, channel.ServedIds.Count, _consumerId);
            }

            if (fresh.Count > 0)
            {
                App.Logger?.Information("[FYP online] +{N} entries from r/{Sub} ({Consumer})",
                    fresh.Count, channel.Name, _consumerId);
                return Summarize(fresh, null);
            }
            App.Logger?.Debug("[FYP online] r/{Sub} dealt {N} already-served ids ({Consumer}, dry page {Dry})",
                channel.Name, page.Entries.Count, _consumerId, channel.DryPages);
        }
        return Summarize(new List<FypAssetManifest.Entry>(), sawTransportFailure ? "offline" : null);
    }

    /// <summary>Wrap a result with the rotation-wide verdict the consumer UIs render: "nothing
    /// can produce right now" and "this is how big the pool actually turned out to be".</summary>
    private FeedBatch Summarize(List<FypAssetManifest.Entry> entries, string? error)
    {
        bool dry;
        int pool;
        lock (_lock)
        {
            var states = _channels.Values.ToList();
            // No channels at all is "not configured", not "exhausted" — never tell the user
            // they have seen everything when they have seen nothing.
            dry = states.Count > 0 && states.All(c => c.Dead || c.InExhaustion || c.InBackoff);
            pool = states.Sum(c => c.ServedIds.Count);
        }
        return new FeedBatch(entries, error, dry, pool);
    }

    /// <summary>Does this subreddit exist upstream, and how much video does it hold? Static
    /// because it is taxonomy, not tenant state: both pickers (the FYP popover and the Assets
    /// tab) ask the same question about a name the user just typed, long before any coordinator
    /// would be built for it. Rides the source's own 1.1s politeness gate.</summary>
    public static async Task<SubProbe> ProbeSubAsync(string? rawSub, CancellationToken ct)
    {
        var clean = SanitizeSub(rawSub);
        if (clean == null) return new SubProbe { Ok = false, Error = "invalid" };
        return await ProbeSource.ProbeSubAsync(clean, ct).ConfigureAwait(false);
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
