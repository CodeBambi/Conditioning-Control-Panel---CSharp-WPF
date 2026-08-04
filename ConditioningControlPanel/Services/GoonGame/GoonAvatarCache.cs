using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// Disk cache for the three avatars the Goon Game's Discord sharing ever holds
    /// (GOON_DISCORD_CONTRACT §4): the player's OWN Discord avatar, the CURRENT match peer's,
    /// and the LAST OPPONENT's. Nothing else is ever stored here and nothing here is ever
    /// user-content: these are ≤256 KB images the app fetched from a pinned source.
    ///
    /// WHY A FIXED FILENAME TABLE AND NOT A DERIVED ONE. Every filename in this class is a
    /// compile-time constant (<see cref="OwnFile"/>, <see cref="PeerFile"/>,
    /// <see cref="LastOpponentFile"/>) — no page string, no peer string and no server string ever
    /// reaches Path.Combine, so traversal is not a thing that can be attempted here any more than
    /// it can in the transfer inbox. The last-opponent record therefore stores a BARE filename
    /// (contract §2), and the reader below only ever accepts one of the three constants.
    ///
    /// Self-healing by construction: every read is a try/catch that returns null, every write is a
    /// try/catch that returns false, and a corrupt or truncated file simply fails the next read and
    /// is re-fetched (own) or falls back to the page's initial-letter tile (peer/last). A dead cache
    /// costs a picture, never a boot and never a match.
    /// </summary>
    internal static class GoonAvatarCache
    {
        /// <summary>Bare filenames — the ONLY names this cache will read or write. See the class
        /// remark: the last-opponent record persists one of these strings, never a path.</summary>
        public const string OwnFile = "own.img";
        public const string PeerFile = "peer.img";
        public const string LastOpponentFile = "last.img";

        /// <summary>Sidecar for <see cref="OwnFile"/>: "&lt;avatarHash&gt;|&lt;unixSeconds&gt;".
        /// Deliberately a flat two-field string rather than JSON — a malformed sidecar must cost a
        /// re-fetch, not a parse exception on a boot path.</summary>
        private const string OwnTagFile = "own.tag";

        /// <summary>Same ceiling the server applies to a peer avatar (contract §3). Anything larger
        /// is not an avatar and is dropped rather than truncated.</summary>
        public const int MaxBytes = 256 * 1024;

        /// <summary>How long a cached own-avatar is trusted without re-asking the CDN. The hash is
        /// the real invalidator (a changed avatar changes the hash, and Discord validate refreshes
        /// it); the TTL only covers the case where the same hash's bytes were fetched badly.</summary>
        private static readonly TimeSpan OwnTtl = TimeSpan.FromHours(24);

        /// <summary>3 s, per contract — an avatar may never be on the critical path of anything.</summary>
        private static readonly HttpClient Http = BuildHttpClient();

        private static HttpClient BuildHttpClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            try
            {
                c.DefaultRequestHeaders.UserAgent.ParseAdd(
                    $"ConditioningControlPanel/{UpdateService.AppVersion}");
            }
            catch { }
            return c;
        }

        public static string Dir => Path.Combine(App.UserDataPath, "goon_avatars");

        private static bool IsKnownFile(string? bare) =>
            bare == OwnFile || bare == PeerFile || bare == LastOpponentFile;

        private static string? PathFor(string bare)
        {
            if (!IsKnownFile(bare)) return null;   // fail closed: an unknown name is never a path
            try
            {
                var dir = Dir;
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, bare);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("GoonAvatarCache.PathFor: {E}", ex.Message);
                return null;
            }
        }

        // ============================ raw file ops ============================

        public static bool Write(string bare, byte[] bytes)
        {
            try
            {
                if (bytes == null || bytes.Length == 0 || bytes.Length > MaxBytes) return false;
                var p = PathFor(bare);
                if (p == null) return false;
                File.WriteAllBytes(p, bytes);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("GoonAvatarCache.Write({F}): {E}", bare, ex.Message);
                return false;
            }
        }

        public static byte[]? Read(string bare)
        {
            try
            {
                var p = PathFor(bare);
                if (p == null || !File.Exists(p)) return null;
                var fi = new FileInfo(p);
                if (fi.Length == 0 || fi.Length > MaxBytes) { Delete(bare); return null; }
                return File.ReadAllBytes(p);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("GoonAvatarCache.Read({F}): {E}", bare, ex.Message);
                return null;
            }
        }

        public static void Delete(string bare)
        {
            try
            {
                var p = PathFor(bare);
                if (p != null && File.Exists(p)) File.Delete(p);
                if (bare == OwnFile)
                {
                    var tag = PathForTag();
                    if (tag != null && File.Exists(tag)) File.Delete(tag);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("GoonAvatarCache.Delete({F}): {E}", bare, ex.Message); }
        }

        /// <summary>Read a cached avatar as a data URI, or null when there isn't a usable one.
        /// The mime is decided by the bytes' OWN magic number, never by whatever produced them.</summary>
        public static string? ReadDataUri(string bare)
        {
            var bytes = Read(bare);
            if (bytes == null) return null;
            var mime = SniffMime(bytes);
            if (mime == null) { Delete(bare); return null; }   // not an image -> heal
            return "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
        }

        /// <summary>Promote the current match peer's cached avatar into the last-opponent slot.
        /// Returns the BARE filename to persist in the record, or null if there was nothing to copy.</summary>
        public static string? PromotePeerToLastOpponent()
        {
            try
            {
                var src = PathFor(PeerFile);
                var dst = PathFor(LastOpponentFile);
                if (src == null || dst == null || !File.Exists(src)) return null;
                File.Copy(src, dst, overwrite: true);
                return LastOpponentFile;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("GoonAvatarCache.PromotePeerToLastOpponent: {E}", ex.Message);
                return null;
            }
        }

        // ============================ own avatar ============================

        private static string? PathForTag()
        {
            try
            {
                var dir = Dir;
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, OwnTagFile);
            }
            catch { return null; }
        }

        /// <summary>The cached own-avatar, WITHOUT touching the network — this is what the init
        /// payload can afford to read synchronously. Null when absent, stale or for a different
        /// avatar hash; <see cref="RefreshOwnAvatarAsync"/> then fills it in and the host echoes a
        /// `discord` frame when it lands.</summary>
        public static string? ReadOwnDataUriIfFresh(string? avatarHash)
        {
            try
            {
                if (string.IsNullOrEmpty(avatarHash)) return null;
                var tag = PathForTag();
                if (tag == null || !File.Exists(tag)) return null;
                var parts = File.ReadAllText(tag).Split('|');
                if (parts.Length != 2) return null;
                if (!string.Equals(parts[0], avatarHash, StringComparison.Ordinal)) return null;
                if (!long.TryParse(parts[1], out var unix)) return null;
                var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix);
                if (age > OwnTtl || age < TimeSpan.FromHours(-1)) return null;   // clock moved backwards -> re-fetch
                return ReadDataUri(OwnFile);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("GoonAvatarCache.ReadOwnDataUriIfFresh: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>Fetch (or re-use) the player's own Discord avatar and return it as a data URI.
        ///
        /// Requests the STATIC .png even for an animated (a_) avatar, exactly as the server does for
        /// a peer (contract §3): a duel plate does not need a looping gif, and the contract's data
        /// URI is specified as image/png. Caps at <see cref="MaxBytes"/>, requires an image/*
        /// content type, 3 s. Never throws — the caller's fallback is "no picture".</summary>
        public static async Task<string?> RefreshOwnAvatarAsync()
        {
            try
            {
                var d = App.Discord;
                if (d == null || !d.IsAuthenticated) return null;
                var hash = d.Avatar;
                if (string.IsNullOrEmpty(hash)) return null;   // default avatar -> nothing to fetch

                var fresh = ReadOwnDataUriIfFresh(hash);
                if (fresh != null) return fresh;

                var url = d.GetAvatarUrl(128);
                if (string.IsNullOrEmpty(url)) return null;
                // Animated avatars come back as .gif from GetAvatarUrl; the duel wants the still.
                url = url!.Replace(".gif?", ".png?");

                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
                    CancellationToken.None).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                var ctype = resp.Content.Headers.ContentType?.MediaType ?? "";
                if (!ctype.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
                var len = resp.Content.Headers.ContentLength;
                if (len.HasValue && len.Value > MaxBytes) return null;

                var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (bytes.Length == 0 || bytes.Length > MaxBytes) return null;
                if (SniffMime(bytes) == null) return null;

                if (!Write(OwnFile, bytes)) return null;
                try
                {
                    var tag = PathForTag();
                    if (tag != null)
                        File.WriteAllText(tag, hash + "|" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                }
                catch { /* a missing tag only costs the next boot a re-fetch */ }

                return ReadDataUri(OwnFile);
            }
            catch (Exception ex)
            {
                // NEVER the URL: a Discord CDN avatar URL contains the snowflake (contract §6).
                App.Logger?.Debug("GoonAvatarCache.RefreshOwnAvatarAsync failed: {E}", ex.Message);
                return null;
            }
        }

        // ============================ helpers ============================

        /// <summary>Decode a `data:image/*;base64,...` URI the SERVER produced into bytes, with the
        /// same ≤256 KB / must-be-an-image rules applied a second time on this side. Returns null on
        /// anything that isn't a small image.</summary>
        public static byte[]? DecodeDataUri(string? dataUri)
        {
            try
            {
                if (string.IsNullOrEmpty(dataUri)) return null;
                if (!dataUri!.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return null;
                var comma = dataUri.IndexOf(',');
                if (comma < 0 || comma + 1 >= dataUri.Length) return null;
                var header = dataUri.Substring(0, comma);
                if (header.IndexOf("base64", StringComparison.OrdinalIgnoreCase) < 0) return null;
                var b64 = dataUri.Substring(comma + 1);
                // Cheap pre-check before allocating: base64 is 4/3 of the payload.
                if ((long)b64.Length / 4 * 3 > MaxBytes) return null;
                var bytes = Convert.FromBase64String(b64);
                if (bytes.Length == 0 || bytes.Length > MaxBytes) return null;
                return SniffMime(bytes) == null ? null : bytes;
            }
            catch { return null; }
        }

        /// <summary>Magic-number mime sniff. The bytes decide, not the label they arrived with —
        /// same rule the received-artifact inbox applies to a peer's media.</summary>
        private static string? SniffMime(byte[] b)
        {
            if (b.Length < 12) return null;
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
            if (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38) return "image/gif";
            if (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
                && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return "image/webp";
            return null;
        }
    }
}
