using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>
/// THE MEDIA FEED (CONTRACT section 5, 10.13.C). Deals up to <c>count</c> animated GIFs (4 by
/// default, 13 for the card table) and four words for one sit-down. Everything it deals is a key plus a url on <c>ccp.assets</c> / <c>ccp.game</c>; no
/// path ever leaves this class, and nothing in a deal is logged beyond counts (PII rule).
///
/// <para>WHERE THE PICTURES COME FROM is one of five things (10.13.C), resolved ONCE by
/// <see cref="BackRoomHostService.EffectiveMediaSource"/> so that <c>auto</c> and the consent collapse
/// live in a single place:
/// <list type="bullet">
/// <item><c>local</c>: the DISK half of the flash pool (<see cref="FlashService.SnapshotLocalImagePaths"/>),
/// so the user's current assets folder and deselections apply exactly as they do to flashes;</item>
/// <item><c>online</c>: the warm remote pool (<see cref="BackRoomRemotePool"/>) - clips, for every
/// surface. A cold pool degrades to <c>local</c>, and a dry library to <c>bundled</c> - a provider
/// being down must look like "no remote content", never like an empty wall;</item>
/// <item><c>mixed</c>: both, blended per pick against <c>AppSettings.RemoteMediaRatio</c> the way
/// <c>FlashService.ShouldDrawRemote</c> rolls it;</item>
/// <item><c>bundled</c>: the four built-in loops ON PURPOSE, chosen rather than fallen back to;</item>
/// <item><c>auto</c> never reaches the deal.</item>
/// </list></para>
///
/// <para>Local candidates are deduped by FULL path (trap 147: two folders can hold the same file name
/// and they are two GIFs), sorted so the listing order cannot move the deal, then shuffled with the
/// seed. Only files under the assets root survive, because only those have a <c>ccp.assets</c> url -
/// which is also, and not by accident, why a materialized remote still has one too.</para>
///
/// <para>EVERY REMOTE PICK IS A CLIP (2026-09-17, "discard stills"). The provider's posters really are
/// static, byte-verified in <c>RemoteMediaFormats</c>, so the animated half of a remote post is the
/// webm/mp4 itself, and the owner's call was that a static picture is not a class of media the room
/// deals at all. WebView2 is Chromium and plays a clip natively - <c>room\clip-source.js</c>, reached on
/// the url's extension from <c>room\gif.js</c> (the wall), <c>stations\slot\media.js</c> (the reels) and
/// <c>shared\hypno\media.js</c> (cards, wheel, roulette), hands back the same shape a decoded GIF does -
/// so every surface gets moving pictures with no transcode and no new installer bytes. The ONE thing a
/// station and the wall are still dealt differently is nothing: <see cref="StationRoom"/> survives only
/// because the wire names the room's deal, and <see cref="RemotePicks"/> is the same draw for both.
/// The three things that used to keep a chair on stills are answered on the page: the card table's
/// deck caps resident sources at eight and pauses a clip it is not drawing, the reels pause theirs under
/// reduced motion, and a host effect that needs a FILE for a WPF overlay still gets a file, because a
/// materialized clip is one.</para>
///
/// <para>Does file I/O (a 30-byte header read per local candidate, bounded by
/// <see cref="ProbeBudgetFor"/>): call <see cref="DealAsync"/>, or call <see cref="Deal"/> off the UI
/// thread.</para>
/// </summary>
internal sealed class BackRoomMedia : IBackRoomMedia
{
    internal const int Slots = 4;
    /// <summary>The most GIFs one deal holds (the thirteen card values, 10.13.C).</summary>
    internal const int MaxCount = 13;
    internal const string FallbackBase = "https://ccp.game/backroom/stations/slot/fallback/";
    /// <summary>Pixel size of the built-in fallback loops (square).</summary>
    internal const int FallbackSize = 180;
    /// <summary>Header reads per deal. A library of still webps must not turn one sit-down into a
    /// file open per file; past the budget the shortfall is fallback art.</summary>
    internal const int ProbeBudget = 48;

    /// <summary>Resolved source values (<c>auto</c> is gone by the time the deal sees one).</summary>
    internal const string SourceLocal = "local", SourceOnline = "online", SourceMixed = "mixed",
        SourceBundled = "bundled", SourceAuto = "auto";

    /// <summary><c>BackRoomGif.Src</c> values. <c>online</c> is the third one (the room used to know
    /// only its own folders); the page's wall filter keys off <c>fallback</c> and nothing else, so a
    /// remote CLIP reads as the real picture it is, and the page tells it from a local GIF by the url's
    /// extension rather than by a fourth <c>Src</c> the wire would have to learn.</summary>
    internal const string SrcPool = "pool", SrcOnline = "online", SrcFallback = "fallback";

    /// <summary>The wall-screen deal's station id. <c>media-request.station</c> is <c>room</c> for the
    /// room's own screens and a station label otherwise ("one <c>media-request</c> with
    /// <c>station: "room"</c> at boot", CONTRACT section 5's wall-screens bullet). Since 2026-09-17 the
    /// deal itself no longer branches on it (the wall and a chair are dealt from one clip set); it is
    /// kept as the named wire word for the log line and the suite.</summary>
    internal const string StationRoom = "room";

    /// <summary><c>max(48, count x 4)</c>: a 13-GIF deal may read up to 52 headers.</summary>
    internal static int ProbeBudgetFor(int count) => Math.Max(ProbeBudget, count * 4);

    /// <summary>Preset words in contract order, as lexicon keys with neutral fallbacks (Law VII).</summary>
    internal static readonly (string Key, string Fallback)[] PresetWords =
    {
        ("br_word_drop", "Drop"),
        ("br_word_relax", "Relax"),
        ("br_word_let_go", "Let Go"),
        ("br_word_sink", "Sink"),
    };

    private static readonly Regex StationLabel = new("^[a-z0-9_-]{1,24}$", RegexOptions.CultureInvariant);

    private readonly Func<IReadOnlyList<string>> _listImages;
    private readonly Func<IReadOnlyList<string>> _listWords;
    private readonly Func<string?> _assetsRoot;
    private readonly Func<string, string, string> _lex;
    private readonly Func<ILogger?> _log;
    private readonly Func<Models.AppSettings?> _settings;
    private readonly Func<IBackRoomRemotePool?> _remote;

    /// <summary>The app's live sources. <paramref name="lex"/> resolves a preset key with its
    /// fallback; the host passes its room lexicon, and without one the neutral text is used.</summary>
    public BackRoomMedia(Func<string, string, string>? lex = null)
        : this(
            () => (IReadOnlyList<string>?)App.Flash?.SnapshotLocalImagePaths() ?? Array.Empty<string>(),
            ActiveWords,
            () => App.EffectiveAssetsPath,
            lex,
            () => App.Logger,
            () => App.Settings?.Current,
            // Resolved on use, not captured: nothing here may force the pool's static init while
            // BackRoomHostService is still building the statics that construct this.
            () => BackRoomRemotePool.Shared)
    {
    }

    /// <summary>Seams for tests: every source the deal reads is passed in. This overload is the
    /// local-only one - no settings and no remote pool means <c>EffectiveMediaSource(null)</c>, which
    /// is <c>local</c>.</summary>
    internal BackRoomMedia(Func<IReadOnlyList<string>> listImages, Func<IReadOnlyList<string>> listWords,
        Func<string?> assetsRoot, Func<string, string, string>? lex, Func<ILogger?> log)
        : this(listImages, listWords, assetsRoot, lex, log, () => null, () => null)
    {
    }

    /// <summary>Seams for tests, with the two the remote half reads: the settings the source and the
    /// ratio come from, and the warm pool.</summary>
    internal BackRoomMedia(Func<IReadOnlyList<string>> listImages, Func<IReadOnlyList<string>> listWords,
        Func<string?> assetsRoot, Func<string, string, string>? lex, Func<ILogger?> log,
        Func<Models.AppSettings?> settings, Func<IBackRoomRemotePool?> remote)
    {
        _listImages = listImages;
        _listWords = listWords;
        _assetsRoot = assetsRoot;
        _lex = lex ?? ((_, fallback) => fallback);
        _log = log;
        _settings = settings;
        _remote = remote;
    }

    /// <summary>
    /// What this deal will actually do. <paramref name="requested"/> is <c>media-request.source</c>,
    /// already whitelisted by the bridge; null, absent or <c>auto</c> means the room's setting decides,
    /// and <see cref="BackRoomHostService.EffectiveMediaSource"/> is the one authority on that.
    /// A request may NARROW the source but never widen it past consent: that is the single rule a
    /// per-request override has to repeat, because the resolver only ever sees the setting.
    /// </summary>
    internal string EffectiveSource(string? requested)
    {
        Models.AppSettings? s;
        string resolved;
        try
        {
            s = _settings();
            resolved = BackRoomHostService.EffectiveMediaSource(s);
        }
        catch { return SourceLocal; }

        if (string.IsNullOrEmpty(requested) || requested == SourceAuto) return resolved;
        if (requested is not (SourceLocal or SourceOnline or SourceMixed or SourceBundled)) return resolved;
        if (requested is SourceOnline or SourceMixed && !(s?.HasRemoteMediaConsent ?? false)) return SourceLocal;
        return requested;
    }

    public void WarmForRoomOpen()
    {
        try { _remote()?.EnsureWarm(); }
        catch (Exception ex) { _log()?.Debug("BackRoomMedia: warm kick failed ({Type})", ex.GetType().Name); }
    }

    public void ReleaseWarmPool()
    {
        try { _remote()?.Drain(); }
        catch (Exception ex) { _log()?.Debug("BackRoomMedia: warm release failed ({Type})", ex.GetType().Name); }
    }

    /// <summary>
    /// The async deal, and the one the protocol uses. The only thing it ever awaits is the warm pool
    /// topping itself up, and that wait is bounded and skipped entirely unless the source wants remote
    /// pictures: a sit-down is never put behind the provider's 1.1 s request gate (see
    /// <see cref="BackRoomRemotePool.WarmWaitMs"/>). Everything after it is the same sync deal
    /// <see cref="Deal"/> does.
    /// </summary>
    public async Task<BackRoomMediaDeal> DealAsync(string station, int seed, int count = Slots,
        string? source = null, CancellationToken ct = default)
    {
        var resolved = EffectiveSource(source);
        if (resolved is SourceOnline or SourceMixed)
        {
            var pool = _remote();
            if (pool != null)
            {
                try { await pool.WarmAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _log()?.Debug("BackRoomMedia: warm wait failed ({Type})", ex.GetType().Name); }
            }
        }
        return DealFrom(resolved, station, seed, count);
    }

    /// <summary>The sync deal, kept for the dev rig and the suite. Serves whatever the warm pool
    /// already holds and never waits on it, so it is safe anywhere <see cref="DealAsync"/> is not.</summary>
    public BackRoomMediaDeal Deal(string station, int seed, int count = Slots)
        => DealFrom(EffectiveSource(null), station, seed, count);

    private BackRoomMediaDeal DealFrom(string source, string station, int seed, int count)
    {
        var gifs = DealGifs(source, station, seed, Math.Clamp(count, 1, MaxCount), out var tally);
        var words = DealWords(seed);

        // Counts only. The station id comes from the page, so it is only echoed when it looks like one.
        var label = station != null && StationLabel.IsMatch(station) ? station : "?";
        // "online (N clip)" is kept in this shape on purpose: every online pick is a clip now, but the
        // pair is what a session log is grepped for ("dealt slot ... (N clip)").
        _log()?.Information(
            "BackRoomMedia: dealt {Station} from {Source}: {PoolGifs} pool + {OnlineGifs} online ({ClipGifs} clip) + {FallbackGifs} fallback gifs from {Candidates} candidates and {WarmClips} warm clips, {PoolWords} pool + {PresetWords} preset words",
            label, source, gifs.Count(g => g.Src == SrcPool), gifs.Count(g => g.Src == SrcOnline), tally.Clips,
            gifs.Count(g => g.Src == SrcFallback), tally.Candidates, tally.WarmClips,
            words.Count(w => w.Src == "pool"), words.Count(w => w.Src == "preset"));

        return new BackRoomMediaDeal(seed, gifs, words, source);
    }

    /// <summary>One candidate on its way into a deal: what the page loads, how big it is, which pool it
    /// came from, and whether it is a clip. <see cref="Clip"/> is for the log line and the suite only -
    /// the page routes a clip by the url's extension, which is why <see cref="BackRoomGif"/> needs no
    /// new field. Keys are handed out last, in deal order, so a blend cannot renumber.</summary>
    private readonly record struct Pick(string Url, int W, int H, string Src, bool Clip = false);

    /// <summary>What one deal touched, for the log line. Counts only, never a path and never a url
    /// (PII rule).</summary>
    private readonly record struct DealTally(int Candidates, int WarmClips, int Clips);

    private List<BackRoomGif> DealGifs(string source, string station, int seed, int count, out DealTally tally)
    {
        tally = default;

        if (source == SourceBundled) return Bundled();

        int warmClips = 0;
        var remote = source is SourceOnline or SourceMixed
            ? RemotePicks(seed, count, out warmClips)
            : new List<Pick>();

        // The local half is only paid for when the source can use it: "online" must not read 48 file
        // headers to throw them away.
        //
        // THE LADDER, AND IT ONLY EVER GOES ONE WAY (10.13.C): clips -> the player's own folders -> the
        // built-in loops. A rung is taken only when everything above it came back with nothing, and the
        // bottom rung is Bundled() rather than an empty deal, because a wall showing the player's own
        // GIF is fine, a wall showing the bundled loop is fine, and a wall showing nothing is a bug.
        // There is no stills rung any more (2026-09-17): a cold clip pool goes straight to the folders,
        // which is the window the pool's small rendition and four download lanes exist to shorten.
        int localCandidates = 0;
        List<Pick> picks;
        if (source == SourceMixed)
        {
            picks = Blend(LocalPicks(seed, count, out localCandidates), remote, count, seed);
        }
        else if (source == SourceOnline)
        {
            // Dry or cold pool: the assets folder, not an empty wall.
            picks = remote.Count > 0 ? remote : LocalPicks(seed, count, out localCandidates);
        }
        else
        {
            picks = LocalPicks(seed, count, out localCandidates);
        }

        tally = new DealTally(localCandidates, warmClips, picks.Take(count).Count(p => p.Clip));
        if (picks.Count == 0) return Bundled();
        // 10.13.C: real pictures are never padded with fallback art (a deck cycles what it has).
        return picks.Take(count).Select((p, i) => new BackRoomGif("g" + i, p.Url, p.W, p.H, p.Src)).ToList();
    }

    /// <summary>The four built-in loops. Chosen on purpose under <c>bundled</c>, and the last resort
    /// under every other source when nothing real could be dealt at all.</summary>
    private static List<BackRoomGif> Bundled()
    {
        var dealt = new List<BackRoomGif>(Slots);
        for (int k = 0; k < Slots; k++)
            dealt.Add(new BackRoomGif("g" + k, $"{FallbackBase}gif{k}.webp", FallbackSize, FallbackSize, SrcFallback));
        return dealt;
    }

    private List<Pick> LocalPicks(int seed, int count, out int candidates)
    {
        var dealt = new List<Pick>(count);
        int budget = ProbeBudgetFor(count);
        candidates = 0;
        try
        {
            var root = _assetsRoot();
            if (!string.IsNullOrEmpty(root))
            {
                var rootFull = Path.GetFullPath(root);
                // Remote entries have no file behind them; only .gif and .webp can animate, so
                // every other extension is dropped here without a file open.
                var pool = (_listImages() ?? Array.Empty<string>())
                    .Where(p => !string.IsNullOrWhiteSpace(p) && !FlashService.IsRemotePath(p))
                    .Where(IsLoopExtension)
                    .Select(SafeFullPath)
                    .Where(p => p != null)
                    .Select(p => p!)
                    // Sorted BEFORE the dedupe, so which spelling of a path survives cannot depend
                    // on the listing order either.
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p, StringComparer.Ordinal)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                candidates = pool.Count;

                var rng = new Random(seed);
                int probes = 0;
                for (int i = 0; i < pool.Count && dealt.Count < count && probes < budget; i++)
                {
                    // Partial Fisher-Yates, consumed as it goes: a candidate only proves it animates
                    // once its header is read.
                    int j = rng.Next(i, pool.Count);
                    (pool[i], pool[j]) = (pool[j], pool[i]);

                    var url = ToAssetsUrl(rootFull, pool[i]);
                    if (url == null) continue;
                    probes++;
                    var (ok, w, h) = ProbeAnimated(pool[i]);
                    if (!ok) continue;
                    if (Path.GetExtension(pool[i]).Equals(".webp", StringComparison.OrdinalIgnoreCase))
                        url += Arcademy.ArcademyHostService.AnimatedImageHint;
                    dealt.Add(new Pick(url, w, h, SrcPool));
                }
            }
        }
        catch (Exception ex)
        {
            // Message only: an IO exception message can carry a path, so the type is all that is kept.
            _log()?.Debug("BackRoomMedia: gif pool unavailable ({Type})", ex.GetType().Name);
        }
        return dealt;
    }

    /// <summary>
    /// The warm pool's remote picks, shuffled with the seed so a replayed sit-down deals the same wall.
    /// Reads only what is already on disk - no fetch, no wait. No <c>AnimatedImageHint</c> either: that
    /// hint tells the host a local <c>.webp</c> animates, and a clip (which the page PLAYS rather than
    /// decodes) has no use for it.
    ///
    /// <para>ONE DRAW FOR EVERY SURFACE (2026-09-17). The wall and a chair used to be dealt from two
    /// sets, clips and posters, because the station pages could not play a webm and thirteen video
    /// decoders looked reckless. Both were answered on the page rather than here: every media module
    /// routes on the extension now, and the surfaces that hold many pictures cap and pause them
    /// (<c>shared\hypno\media.js</c> keeps eight resident and pauses a clip it is not drawing). What
    /// stays true is that a host effect resolves a dealt url back to a FILE for a WPF overlay
    /// (<c>BackRoomFxServices.LocalFile</c>), and a materialized clip is a file; what that overlay can do
    /// with an mp4 is the overlay's business and was never this class's promise.</para>
    /// </summary>
    private List<Pick> RemotePicks(int seed, int count, out int warmClips)
    {
        warmClips = 0;
        var dealt = new List<Pick>(count);
        try
        {
            var pool = _remote();
            if (pool == null) return dealt;

            var clips = pool.Ready();
            warmClips = clips?.Count ?? 0;
            // Its own stream, keyed off the seed like the word pick, so how many local headers were
            // read cannot move which clips a surface gets.
            if (warmClips > 0)
                Draw(clips!.Select(c => (c.Url, c.W, c.H)).ToList(),
                    new Random(unchecked(seed * 23 + 5)), count, clip: true, dealt);
        }
        catch (Exception ex)
        {
            _log()?.Debug("BackRoomMedia: remote pool unavailable ({Type})", ex.GetType().Name);
        }
        return dealt;
    }

    /// <summary>Partial Fisher-Yates over one warm set, consumed as it goes, until the deal has
    /// <paramref name="count"/> picks. Deterministic for a seed, which is what makes a replayed
    /// sit-down deal the same wall.</summary>
    private static void Draw(List<(string Url, int W, int H)> order, Random rng, int count, bool clip,
        List<Pick> into)
    {
        for (int i = 0; i < order.Count && into.Count < count; i++)
        {
            int j = rng.Next(i, order.Count);
            (order[i], order[j]) = (order[j], order[i]);
            into.Add(new Pick(order[i].Url, order[i].W, order[i].H, SrcOnline, clip));
        }
    }

    /// <summary>
    /// <c>mixed</c>: <c>RemoteMediaRatio</c> is the share of picks drawn remotely, rolled per pick
    /// exactly as <c>FlashService.ShouldDrawRemote</c> rolls it (re-clamped here, because a synced
    /// settings file is not ours to trust). Whichever side is dry yields to the other, so a blend
    /// never deals fewer pictures than either pool could have on its own.
    /// </summary>
    private List<Pick> Blend(List<Pick> local, List<Pick> remote, int count, int seed)
    {
        var picks = new List<Pick>(count);
        int ratio;
        try { ratio = Math.Clamp(_settings()?.RemoteMediaRatio ?? 30, 0, 100); }
        catch { ratio = 30; }

        // Its own stream again: the blend must not shift with the local shuffle or the word pick.
        var rng = new Random(unchecked(seed * 7 + 11));
        int li = 0, ri = 0;
        while (picks.Count < count && (li < local.Count || ri < remote.Count))
        {
            bool wantRemote = rng.Next(100) < ratio;
            if (wantRemote && ri < remote.Count) picks.Add(remote[ri++]);
            else if (li < local.Count) picks.Add(local[li++]);
            else picks.Add(remote[ri++]);
        }
        return picks;
    }

    private List<BackRoomWord> DealWords(int seed)
    {
        var dealt = new List<BackRoomWord>(Slots);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var pool = (_listWords() ?? Array.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .OrderBy(t => t, StringComparer.Ordinal)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            // Its own stream, so the word pick does not shift with how many GIF headers were read.
            var rng = new Random(unchecked(seed * 31 + 7));
            for (int i = 0; i < pool.Count && dealt.Count < Slots; i++)
            {
                int j = rng.Next(i, pool.Count);
                (pool[i], pool[j]) = (pool[j], pool[i]);
                if (seen.Add(pool[i])) dealt.Add(new BackRoomWord("s" + dealt.Count, pool[i], "pool"));
            }
        }
        catch (Exception ex)
        {
            _log()?.Debug("BackRoomMedia: word pool unavailable ({Type})", ex.GetType().Name);
        }

        foreach (var (key, fallback) in PresetWords)
        {
            if (dealt.Count >= Slots) break;
            string text;
            try { text = _lex(key, fallback); } catch { text = fallback; }
            if (string.IsNullOrWhiteSpace(text)) text = fallback;
            if (seen.Add(text.Trim())) dealt.Add(new BackRoomWord("s" + dealt.Count, text.Trim(), "preset"));
        }
        return dealt;
    }

    /// <summary>
    /// THE PLAYER'S OWN WORDS. Everything CCP counts as selected AND active right now, in one list:
    /// <list type="bullet">
    /// <item><c>Settings.SubliminalPool</c>: the live subliminal vocabulary (the app writes the per-mode /
    /// per-mod variant into it, and a running session prescribes its own), so its ENABLED keys are exactly
    /// what the subliminal flashes say now;</item>
    /// <item><c>Settings.KeywordTriggers</c> where <c>Enabled</c>: every Awareness keyword the player left
    /// switched on, their own and the ones an installed preset (<c>KeywordTriggerPreset.MasterEnabled</c>)
    /// cloned in. These are the phrases that already have clips in the app, which is what lets
    /// <see cref="BackRoomVoice"/> say them in the player's own audio.</item>
    /// </list>
    /// Empty means the player has nothing active, and only then does the deal fall back to the four
    /// preset words (Law VII).
    /// </summary>
    internal static IReadOnlyList<string> ActiveWords()
    {
        var words = new List<string>();
        var settings = App.Settings?.Current;
        if (settings == null) return words;

        var pool = settings.SubliminalPool;
        if (pool != null)
            foreach (var kv in pool)
                if (kv.Value && !string.IsNullOrWhiteSpace(kv.Key)) words.Add(kv.Key);

        var triggers = settings.KeywordTriggers;
        if (triggers != null)
            foreach (var t in triggers)
                if (t is { Enabled: true } && !string.IsNullOrWhiteSpace(t.Keyword)) words.Add(t.Keyword);

        return words;
    }

    private static bool IsLoopExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return null; }
    }

    /// <summary>The <c>ccp.assets</c> url for a file under <paramref name="rootFull"/>, encoded
    /// the way ArcademyHostService.ToAssetsUrl does, or null for anything outside the mapping.</summary>
    internal static string? ToAssetsUrl(string rootFull, string fileFull)
    {
        var rel = Path.GetRelativePath(rootFull, fileFull);
        if (Path.IsPathRooted(rel) || rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar)
            || rel.StartsWith("../")) return null;
        rel = rel.Replace('\\', '/');
        return "https://ccp.assets/" + string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));
    }

    /// <summary>Does this file animate, and how big is it? One 30-byte read. A <c>.gif</c> is a loop
    /// by name (as everywhere else in the app) once its magic checks out; a <c>.webp</c> needs the
    /// VP8X animation flag. Size comes from the same bytes, 0 when the header does not carry it.</summary>
    internal static (bool Ok, int W, int H) ProbeAnimated(string file)
    {
        try
        {
            Span<byte> h = stackalloc byte[30];
            int read;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                read = fs.Read(h);

            if (read >= 10 && h[0] == 'G' && h[1] == 'I' && h[2] == 'F' && h[3] == '8')
                return (true, h[6] | h[7] << 8, h[8] | h[9] << 8);

            if (read >= 30 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F'
                && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P'
                && h[12] == 'V' && h[13] == 'P' && h[14] == '8' && h[15] == 'X' && (h[20] & 0x02) != 0)
                return (true, 1 + (h[24] | h[25] << 8 | h[26] << 16), 1 + (h[27] | h[28] << 8 | h[29] << 16));

            return (false, 0, 0);
        }
        catch { return (false, 0, 0); }
    }
}
