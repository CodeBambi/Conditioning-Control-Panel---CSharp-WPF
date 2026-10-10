// PORTED from WPF 7.1.5 Services/GoonGame/GoonOnlineMedia.cs: the pure rules half (the fetcher is head-side).
// Deviation: public (the Avalonia head cannot see Core internals); bodies unchanged.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// The pure half of the Goon Game's online pictures: what a niche name may be, how many a
    /// pick may carry, what a stored custom blob may look like, which state the page is told
    /// and how a materialised temp file becomes a page url. No I/O, no App statics, so every
    /// rule here is a unit test.
    /// </summary>
    public static class GoonOnlineMediaRules
    {
        /// <summary>Same grammar as the page's <c>cleanNiche</c> (and Breakout's picker).</summary>
        private static readonly Regex NicheRx = new("^[a-zA-Z0-9_]{2,40}$", RegexOptions.CultureInvariant);

        public const int MaxSubs = 8;

        /// <summary>About what one fetch wave aims to put in the deck. Small enough that the
        /// first pictures land in seconds, big enough that a match does not repeat at once.</summary>
        public const int StillTarget = 24;
        public const int ClipTarget = 12;

        /// <summary>How many waves' worth the deck may hold. A refill past this retires the
        /// oldest picture as each fresh one lands, so the files held stay bounded.</summary>
        public const int MaxWaves = 3;

        /// <summary>The most pictures of one kind the deck holds at once.</summary>
        public static int Cap(int perWave) => perWave * MaxWaves;

        /// <summary>Downloads in flight at once, per kind.</summary>
        public const int Concurrency = 3;

        /// <summary>Batches in a row that add nothing before a kind gives up for this wave.</summary>
        public const int MaxDryBatches = 3;

        /// <summary>The page's custom blob is opaque to us; this only stops a runaway write.</summary>
        public const int MaxCustomChars = 16 * 1024;

        public static readonly IReadOnlyList<string> Flavours =
            new[] { "trance", "pink", "frills", "shiny", "censored", "mine" };

        public static bool IsNiche(string? s) => s != null && NicheRx.IsMatch(s);

        /// <summary>Valid names only, first spelling wins on a case-insensitive repeat, capped.</summary>
        public static List<string> CleanSubs(IEnumerable<string?>? raw)
        {
            var list = new List<string>();
            if (raw == null) return list;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in raw)
            {
                var s = r?.Trim();
                if (s != null && s.StartsWith("r/", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
                if (!IsNiche(s) || !seen.Add(s!)) continue;
                list.Add(s!);
                if (list.Count >= MaxSubs) break;
            }
            return list;
        }

        /// <summary>A known flavour id, or "" (never picked). Anything else collapses to "".</summary>
        public static string CleanFlavour(string? raw)
        {
            var s = (raw ?? "").Trim().ToLowerInvariant();
            return Flavours.Contains(s) ? s : "";
        }

        /// <summary>The custom blob as a JSON object string, or "" when it is missing, not an
        /// object, or too big. Re-serialised compactly so the stored form never carries junk
        /// whitespace from the page.</summary>
        public static string CleanCustom(JToken? raw)
        {
            if (raw is not JObject o) return "";
            var s = o.ToString(Newtonsoft.Json.Formatting.None);
            return s.Length <= MaxCustomChars ? s : "";
        }

        /// <summary>The stored custom blob back to an object for init; {} on anything odd.</summary>
        public static JObject ParseCustom(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return new JObject();
            try { return JToken.Parse(stored) as JObject ?? new JObject(); }
            catch { return new JObject(); }
        }

        public static string JoinSubs(IEnumerable<string> subs) => string.Join(",", subs);

        public static List<string> SplitSubs(string? stored)
            => CleanSubs((stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));

        /// <summary>Does this media-flavour frame opt the session in: the switch on and a real
        /// flavour picked. Session-only; never stored as consent.</summary>
        public static bool IsSessionOptIn(bool online, string flavour)
            => online && !string.IsNullOrEmpty(flavour);

        /// <summary>Should anything be fetched at all.</summary>
        public static bool ShouldFetch(bool online, string flavour, IReadOnlyCollection<string> subs)
            => online && !string.IsNullOrEmpty(flavour) && subs.Count > 0;

        /// <summary>The state the page is told. <paramref name="running"/> = a wave is still
        /// fetching; <paramref name="failed"/> = the feed itself was unreachable.</summary>
        public static string StateFor(bool online, int subs, int have, bool running, bool failed)
        {
            if (!online) return "off";
            if (subs == 0) return "empty";
            if (have > 0) return running ? "loading" : "ready";
            if (running) return "loading";
            return failed ? "error" : "empty";
        }

        /// <summary><c>https://ccp.assets/...</c> for a file under the assets root, or null when
        /// the file is not under it (the system temp fallback is not page-reachable).</summary>
        public static string? UrlFor(string? path, string? assetsRoot)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(assetsRoot)) return null;
            string full, root;
            try
            {
                full = Path.GetFullPath(path);
                root = Path.GetFullPath(assetsRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            }
            catch { return null; }
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            var rel = full.Substring(root.Length).Replace('\\', '/');
            var parts = rel.Split('/').Select(Uri.EscapeDataString);
            return "https://ccp.assets/" + string.Join("/", parts);
        }
    }

    /// <summary>
    /// The Sort duel's NOISE boards (2026-09-25): seven safe-for-work Scrolller boards a player
    /// sorts AGAINST. Mirrors the page's <c>goon/core/noiseSets.js</c> row for row (the page's
    /// selftest-noise reads both files and fails on drift). Only the set id ever crosses from the
    /// page; the board name is looked up here, so a page can never steer this fetch to a niche.
    /// </summary>
    public static class GoonNoiseSets
    {
        /// <summary>Stills one board fetches: plenty for the left pile of a short Sort.</summary>
        public const int Stills = 20;

        public static readonly IReadOnlyDictionary<string, string> Boards = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["architecture"] = "ArchitecturePorn",
            ["landscapes"] = "EarthPorn",
            ["space"] = "spaceporn",
            ["food"] = "FoodPorn",
            ["cars"] = "carporn",
            ["rooms"] = "RoomPorn",
            ["cats"] = "cats",
        };

        /// <summary>The Scrolller board for a set id, or null for anything off the list.</summary>
        public static string? SubFor(string? id)
            => id != null && Boards.TryGetValue(id, out var sub) ? sub : null;

        /// <summary>May this player's host fetch a noise board: online pictures not switched off,
        /// and either this session's Goon flavour pick (the game's own opt-in) or the app-wide
        /// remote consent with a non-local media source.</summary>
        public static bool FetchAllowed(bool? goonMediaOnline, bool sessionOptIn, string? mediaSource, bool remoteConsent)
        {
            if (goonMediaOnline == false) return false;
            if (sessionOptIn) return true;
            return !string.Equals(mediaSource ?? "local", "local", StringComparison.OrdinalIgnoreCase) && remoteConsent;
        }
    }

}
