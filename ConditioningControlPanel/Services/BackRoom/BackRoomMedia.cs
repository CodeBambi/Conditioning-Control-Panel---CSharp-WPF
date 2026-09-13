using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Serilog;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>
/// THE MEDIA FEED (CONTRACT section 5). Deals four local animated GIFs and four words for one
/// sit-down. Everything it deals is a key plus a url on <c>ccp.assets</c> / <c>ccp.game</c>; no
/// path ever leaves this class, and nothing in a deal is logged beyond counts (PII rule).
///
/// <para>GIFs come from the DISK half of the flash pool (<see cref="FlashService.SnapshotLocalImagePaths"/>),
/// so the user's current assets folder and deselections apply exactly as they do to flashes.
/// Candidates are deduped by FULL path (trap 147: two folders can hold the same file name and they
/// are two GIFs), sorted so the listing order cannot move the deal, then shuffled with the seed.
/// Only files under the assets root survive, because only those have a <c>ccp.assets</c> url.</para>
///
/// <para>Does file I/O (a 30-byte header read per candidate, bounded by <see cref="ProbeBudget"/>):
/// call it off the UI thread.</para>
/// </summary>
internal sealed class BackRoomMedia : IBackRoomMedia
{
    internal const int Slots = 4;
    internal const string FallbackBase = "https://ccp.game/backroom/stations/slot/fallback/";
    /// <summary>Pixel size of the built-in fallback loops (square).</summary>
    internal const int FallbackSize = 180;
    /// <summary>Header reads per deal. A library of still webps must not turn one sit-down into a
    /// file open per file; past the budget the shortfall is fallback art.</summary>
    internal const int ProbeBudget = 48;

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

    /// <summary>The app's live sources. <paramref name="lex"/> resolves a preset key with its
    /// fallback; the host passes its room lexicon, and without one the neutral text is used.</summary>
    public BackRoomMedia(Func<string, string, string>? lex = null)
        : this(
            () => (IReadOnlyList<string>?)App.Flash?.SnapshotLocalImagePaths() ?? Array.Empty<string>(),
            ActivePoolWords,
            () => App.EffectiveAssetsPath,
            lex,
            () => App.Logger)
    {
    }

    /// <summary>Seams for tests: every source the deal reads is passed in.</summary>
    internal BackRoomMedia(Func<IReadOnlyList<string>> listImages, Func<IReadOnlyList<string>> listWords,
        Func<string?> assetsRoot, Func<string, string, string>? lex, Func<ILogger?> log)
    {
        _listImages = listImages;
        _listWords = listWords;
        _assetsRoot = assetsRoot;
        _lex = lex ?? ((_, fallback) => fallback);
        _log = log;
    }

    public BackRoomMediaDeal Deal(string station, int seed)
    {
        var gifs = DealGifs(seed, out int candidates);
        var words = DealWords(seed);

        // Counts only. The station id comes from the page, so it is only echoed when it looks like one.
        var label = station != null && StationLabel.IsMatch(station) ? station : "?";
        _log()?.Information(
            "BackRoomMedia: dealt {Station} {PoolGifs} pool + {FallbackGifs} fallback gifs from {Candidates} candidates, {PoolWords} pool + {PresetWords} preset words",
            label, gifs.Count(g => g.Src == "pool"), gifs.Count(g => g.Src == "fallback"), candidates,
            words.Count(w => w.Src == "pool"), words.Count(w => w.Src == "preset"));

        return new BackRoomMediaDeal(seed, gifs, words);
    }

    private List<BackRoomGif> DealGifs(int seed, out int candidates)
    {
        var dealt = new List<BackRoomGif>(Slots);
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
                for (int i = 0; i < pool.Count && dealt.Count < Slots && probes < ProbeBudget; i++)
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
                    dealt.Add(new BackRoomGif("g" + dealt.Count, url, w, h, "pool"));
                }
            }
        }
        catch (Exception ex)
        {
            // Message only: an IO exception message can carry a path, so the type is all that is kept.
            _log()?.Debug("BackRoomMedia: gif pool unavailable ({Type})", ex.GetType().Name);
        }

        for (int k = dealt.Count; k < Slots; k++)
            dealt.Add(new BackRoomGif("g" + k, $"{FallbackBase}gif{k}.webp", FallbackSize, FallbackSize, "fallback"));
        return dealt;
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

    /// <summary>The live subliminal vocabulary: <c>SubliminalPool</c> is where the app writes the
    /// per-mode / per-mod variant, so its ENABLED keys are exactly what subliminals flash now.</summary>
    private static IReadOnlyList<string> ActivePoolWords()
    {
        var pool = App.Settings?.Current?.SubliminalPool;
        if (pool == null) return Array.Empty<string>();
        return pool.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
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
