using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Transfer;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>One received artifact as <c>recv_index.json</c> stores it.</summary>
    internal sealed class InboxEntry
    {
        /// <summary>Sniffed extension WITHOUT the dot. The claimed mime never picks this.</summary>
        [JsonProperty("ext")] public string Ext { get; set; } = "";
        /// <summary>The mime the sender declared AND the sniffer agreed with.</summary>
        [JsonProperty("mime")] public string Mime { get; set; } = "";
        /// <summary>
        /// "gif" when the SENDER said this artifact was a converted animated image rather than
        /// footage; "" otherwise, which is also what every row written before 2026-08-05 says.
        ///
        /// ADVISORY AND UNTRUSTED, which is why it is safe to take a peer's word for it: it never
        /// touches a filename, a size, a hash or a mime check, and the only thing that reads it is
        /// the page's preference for real footage in the VIDEO lane. Still indexed so a mid-match
        /// page reload keeps the distinction; the inbox itself no longer survives the session
        /// (ephemeral policy — see the class doc).
        /// </summary>
        [JsonProperty("origin")] public string Origin { get; set; } = "";
        [JsonProperty("bytes")] public long Bytes { get; set; }
        [JsonProperty("lastAccessUtc")] public DateTime LastAccessUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>recv_index.json — sha -&gt; entry, beside index.json in the ONE transfer-cache root.</summary>
    internal sealed class InboxIndex
    {
        public const int CurrentVersion = 1;
        [JsonProperty("version")] public int Version { get; set; } = CurrentVersion;
        [JsonProperty("entries")]
        public Dictionary<string, InboxEntry> Entries { get; set; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// The RECEIVED-artifact inbox: what a duel partner sent us, per the transfer protocol's §6.3.
    ///
    /// It is deliberately a SECOND STORE INSIDE ONE ROOT. Files land in
    /// <see cref="TransferCacheStore.RecvDir"/> (<c>transfer-cache/recv/</c>) with the bookkeeping in
    /// <c>recv_index.json</c> beside the own-artifact <c>index.json</c>, and partials go to the same
    /// SIBLING <see cref="TransferCacheStore.TmpDir"/> that is deliberately outside the mapped
    /// <c>https://ccp.cache</c> vhost. One root means one virtual host and one folder to explain;
    /// two stores mean "delete my compressed copies" can never touch a partner's media, and
    /// "forget what they sent me" can never touch the user's own cache.
    ///
    /// EVERY SECURITY PROPERTY HANGS OFF ONE RULE: the only thing that ever becomes a filename is a
    /// 64-hex sha that THIS PROCESS computed over the bytes on disk. The peer's claimed hash is a
    /// claim, checked at commit and thrown away on disagreement; the peer's claimed mime is a claim,
    /// checked against the sniffed magic and REJECTED (never silently re-labelled) on disagreement.
    /// Traversal is impossible because no peer-supplied string ever reaches <c>Path.Combine</c>.
    ///
    /// Thread-safety: every public method takes the instance lock. Commit hashes up to 24 MB, so
    /// callers should run it off the UI thread (the bridge does).
    ///
    /// THE INBOX IS EPHEMERAL (owner decision, 2026-08-05): a partner's media must never outlive
    /// the match it arrived in. The page purges per-artifact at its teardown seam; this class
    /// backstops every path the page cannot see by wiping ALL committed artifacts at the startup
    /// sweep (a crash), at page boot (<see cref="PurgeCommittedSafe"/> from OnPageReady — a window
    /// reopened within one app run), and at window close (DisposeAll). Before this policy the
    /// inbox persisted for cross-session <c>decline:'have'</c> dedupe — and boot.js primed it into
    /// the media pool, which let Practice's scripted peer replay past partners' media. Dedupe is
    /// now within-match only; a rematch re-transfers. Do not "optimize" persistence back in.
    /// </summary>
    internal sealed class TransferInboxStore
    {
        public static TransferInboxStore Instance { get; } = new();

        /// <summary>Byte budget for received media. HARDCODED for now — TODO: settings-backed
        /// (<c>AppSettings.TransferInboxCapBytes</c>) once the assets screen has a slider for it.</summary>
        public const long DefaultCapBytes = 2L * 1024 * 1024 * 1024;

        /// <summary>Mirrors the protocol's MAX_ARTIFACT_BYTES. A larger offer is refused at Begin.</summary>
        public const long MaxRecvBytes = 24L * 1024 * 1024;

        /// <summary>~256 KiB of payload once decoded — the protocol's chunk size plus base64 slack.</summary>
        public const int MaxChunkB64Chars = 350_000;

        /// <summary>A write session with no traffic for this long is abandoned by <see cref="SweepStaleSessions"/>.</summary>
        private static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(5);

        private const string PartPrefix = "recv-";

        /// <summary>THE only filename source. Nothing else in this file builds a name.</summary>
        private static readonly Regex ShaRe = new("^[0-9a-f]{64}$", RegexOptions.Compiled);

        /// <summary>What the offer gate may accept, mime -&gt; sniffable format id.</summary>
        private static readonly Dictionary<string, string> MimeToFormat = new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = "png",
            ["image/jpeg"] = "jpeg",
            ["image/jpg"] = "jpeg",      // tolerated alias; the sniff still decides
            ["image/gif"] = "gif",
            ["image/webp"] = "webp",
            ["video/mp4"] = "mp4",
            ["video/webm"] = "webm",
            // iPhone camera captures are QuickTime containers whatever the codec
            // ("Most Compatible" only swaps HEVC for H.264 inside the same
            // wrapper). Same ISO-BMFF sniff family as mp4 — the "qt  " brand
            // below is what actually admits them.
            ["video/quicktime"] = "mp4",
        };

        /// <summary>format id -&gt; the extension the file gets. SNIFFED, never claimed.</summary>
        private static readonly Dictionary<string, string> FormatToExt = new(StringComparer.Ordinal)
        {
            ["png"] = "png",
            ["jpeg"] = "jpg",
            ["gif"] = "gif",
            ["webp"] = "webp",
            ["mp4"] = "mp4",
            ["webm"] = "webm",
        };

        /// <summary>ISO-BMFF brands we will call an mp4. Tolerant of the streaming brands
        /// (<c>dash</c>, <c>msnv</c>) a phone-recorded clip can carry.</summary>
        private static readonly HashSet<string> Mp4Brands = new(StringComparer.Ordinal)
        {
            "isom", "mp42", "avc1", "iso2", "mp41", "M4V ", "dash", "msnv", "iso4", "iso5", "iso6", "mp71",
            "qt  ",     // QuickTime — every iPhone camera capture
        };

        private readonly object _lock = new();
        private readonly Dictionary<string, WriteSession> _sessions = new(StringComparer.Ordinal);
        private InboxIndex _index = new();
        private long _usedBytes;
        private bool _loaded;

        /// <summary>One in-flight chunked write. Keyed by the PAGE's correlation id; the sha only
        /// ever names the temp file, and only after <see cref="ShaRe"/> has approved it.</summary>
        private sealed class WriteSession
        {
            public required string Id { get; init; }
            public required string Sha { get; init; }
            public required string Mime { get; init; }
            public required long Declared { get; init; }
            public required string PartPath { get; init; }
            /// <summary>"gif" or "". Carried from Begin to Commit so it can be indexed — see
            /// <see cref="InboxEntry.Origin"/> for why a peer's claim is safe here.</summary>
            public string Origin { get; init; } = "";
            /// <summary>We already hold these bytes. Chunks are ACKED AND DISCARDED and Commit is a
            /// no-op success — a peer that ignored <c>decline:'have'</c> must not get us to rewrite
            /// 24 MB we already have.</summary>
            public bool AlreadyHave { get; init; }
            public long Written;
            public int NextSeq;
            public DateTime TouchedUtc = DateTime.UtcNow;
        }

        private TransferCacheStore Cache => TransferCacheStore.Instance;
        public string RecvDir => Cache.RecvDir;
        public string TmpDir => Cache.TmpDir;
        /// <summary>Beside index.json in the transfer-cache root. There is no second root.</summary>
        public string IndexPath => Path.Combine(Cache.Root, "recv_index.json");

        public long CapBytes => DefaultCapBytes;
        public long UsedBytes { get { lock (_lock) return _usedBytes; } }

        // ---- lifecycle ------------------------------------------------------------

        /// <summary>
        /// Load the index and run the startup sweep. Idempotent and lazily triggered by every public
        /// method, so nothing has to remember to call it.
        /// </summary>
        public void Initialize()
        {
            lock (_lock)
            {
                if (_loaded) return;
                _loaded = true;
                try
                {
                    Cache.EnsureRoot();
                    if (File.Exists(IndexPath))
                    {
                        var loaded = JsonConvert.DeserializeObject<InboxIndex>(File.ReadAllText(IndexPath));
                        if (loaded?.Entries != null && loaded.Version == InboxIndex.CurrentVersion) _index = loaded;
                    }
                }
                catch (Exception ex)
                {
                    // A dead index costs the user a re-transfer, nothing more.
                    App.Logger?.Warning("TransferInboxStore: index load failed ({E}) - starting fresh", ex.Message);
                    _index = new InboxIndex();
                }
                SweepLocked();
                App.Logger?.Information("TransferInboxStore: {N} received artifacts, {MB:F1} MB",
                    _index.Entries.Count, _usedBytes / 1048576.0);
            }
        }

        private void EnsureLoaded()
        {
            bool need;
            lock (_lock) need = !_loaded;
            if (need) Initialize();
        }

        /// <summary>
        /// Startup sweep, caller holds the lock. Under the ephemeral policy (class doc) anything in
        /// recv/ at startup is a leftover from an unclean exit — the page's teardown purge and the
        /// window-close purge already ran on every clean path — so the sweep deletes ALL of it, the
        /// index with it, and every <c>recv-*.part</c> from a previous run. Resume across an app
        /// restart was never supported; now the committed files do not cross a restart either.
        /// </summary>
        private void SweepLocked()
        {
            PurgeCommittedLocked("startup sweep");

            try
            {
                if (Directory.Exists(TmpDir))
                    foreach (var f in Directory.EnumerateFiles(TmpDir, PartPrefix + "*.part"))
                        TryDelete(f, "stale partial");
            }
            catch (Exception ex) { App.Logger?.Debug("TransferInboxStore.Sweep(tmp): {E}", ex.Message); }

            SaveLocked();
        }

        /// <summary>
        /// Delete every committed artifact and its index row. Caller holds the lock. Never touches
        /// TmpDir partials — a live session's .part is the one thing a purge may not race.
        /// </summary>
        private void PurgeCommittedLocked(string why)
        {
            var n = 0;
            try
            {
                Directory.CreateDirectory(RecvDir);
                foreach (var f in Directory.EnumerateFiles(RecvDir)) { TryDelete(f, why); n++; }
            }
            catch (Exception ex) { App.Logger?.Debug("TransferInboxStore.Purge(recv): {E}", ex.Message); }
            _index.Entries.Clear();
            RecountLocked();
            if (n > 0)
                App.Logger?.Information("TransferInboxStore: purged {N} received artifact(s) ({Why})", n, why);
        }

        /// <summary>
        /// The ephemeral-inbox wipe (class doc). Callable ONLY from moments when nothing can be
        /// mid-match — page boot (OnPageReady) and window close (DisposeAll) — because it deletes
        /// files the page may otherwise still be rendering. Never throws.
        /// </summary>
        public void PurgeCommittedSafe(string why)
        {
            try
            {
                EnsureLoaded();
                lock (_lock)
                {
                    PurgeCommittedLocked(why);
                    SaveLocked();
                }
            }
            catch (Exception ex) { App.Logger?.Warning("TransferInboxStore.PurgeCommittedSafe: {E}", ex.Message); }
        }

        private static void TryDelete(string path, string why)
        {
            if (string.IsNullOrEmpty(path)) return;   // a sink session has no file
            try
            {
                File.Delete(path);
                App.Logger?.Information("TransferInboxStore: deleted {File} ({Why})", Path.GetFileName(path), why);
            }
            catch (Exception ex) { App.Logger?.Debug("TransferInboxStore.TryDelete({P}): {E}", path, ex.Message); }
        }

        private void RecountLocked()
        {
            long total = 0;
            foreach (var e in _index.Entries.Values) total += e.Bytes;
            _usedBytes = total;
        }

        private void SaveLocked()
        {
            try
            {
                Cache.EnsureRoot();
                var tmp = IndexPath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(_index, Formatting.None));
                File.Move(tmp, IndexPath, overwrite: true);
            }
            catch (Exception ex) { App.Logger?.Warning("TransferInboxStore: index save failed: {E}", ex.Message); }
        }

        // ---- reads ----------------------------------------------------------------

        /// <summary>The offer gate's <c>decline:'have'</c> answer. Synchronous by contract.</summary>
        public bool Has(string? sha)
        {
            if (!IsSha(sha)) return false;
            EnsureLoaded();
            lock (_lock) return _index.Entries.ContainsKey(sha!);
        }

        /// <summary>
        /// Bytes already on disk for a paused transfer, i.e. <c>xfer_accept{from_offset}</c>.
        /// Answered from the live session first, then from a <c>recv-&lt;sha&gt;.part</c> left by a
        /// page reload; 0 once the app has restarted (the sweep deletes those).
        /// </summary>
        public long PartialLength(string? sha)
        {
            if (!IsSha(sha)) return 0;
            EnsureLoaded();
            lock (_lock)
            {
                foreach (var s in _sessions.Values)
                    if (s.Sha == sha) return s.Written;
            }
            try
            {
                var p = PartPathFor(sha!);
                return File.Exists(p) ? new FileInfo(p).Length : 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// What a returning session already has, folded into the host's existing <c>manifest</c>
        /// frame as <c>received</c>: anonymous objects so the JSON keys are the wire keys.
        /// </summary>
        public IReadOnlyList<object> ListForManifest()
        {
            EnsureLoaded();
            lock (_lock)
                return _index.Entries
                    .Select(kv => (object)new
                    {
                        sha = kv.Key,
                        ext = kv.Value.Ext,
                        mime = kv.Value.Mime,
                        origin = kv.Value.Origin,
                        bytes = kv.Value.Bytes,
                    })
                    .ToList();
        }

        /// <summary>The page-facing URL of a committed artifact, or null when we don't have it.</summary>
        public string? UrlFor(string? sha)
        {
            if (!IsSha(sha)) return null;
            EnsureLoaded();
            lock (_lock)
                return _index.Entries.TryGetValue(sha!, out var e) ? RecvUrl(sha!, e.Ext) : null;
        }

        public static string RecvUrl(string sha, string ext) => "https://ccp.cache/recv/" + sha + "." + ext;

        // ---- chunked write session ------------------------------------------------

        /// <summary>
        /// Open a write session. Returns null on success or one of
        /// <c>bad-name | too-big | bad-format | cap-reached | io-failed</c>.
        ///
        /// A pre-existing <c>.part</c> for the same sha is RESUMED (the protocol offered
        /// <c>from_offset = PartialLength</c>), because the commit hash is the backstop: a resume
        /// that stitched the wrong bytes together fails <c>hash-mismatch</c> and is deleted.
        /// </summary>
        /// <param name="origin">The offer's advisory <c>origin</c> ("gif" or anything else, which
        /// is normalised to ""). Never validated beyond that string compare, and never used for
        /// anything that can fail — see <see cref="InboxEntry.Origin"/>.</param>
        public string? Begin(string? id, string? sha, string? mime, long bytes, string? origin = null)
        {
            if (string.IsNullOrEmpty(id)) return "bad-name";
            if (!IsSha(sha)) return "bad-name";
            if (mime == null || !MimeToFormat.ContainsKey(mime)) return "bad-format";
            if (bytes <= 0 || bytes > MaxRecvBytes) return "too-big";
            EnsureLoaded();

            lock (_lock)
            {
                SweepStaleSessionsLocked();
                if (_sessions.ContainsKey(id!)) return "bad-name";     // the page reused a live id
                if (_index.Entries.ContainsKey(sha!))
                {
                    // Already have it: open a sink session so the page's chunk/commit round trip
                    // still succeeds, but never touch the disk.
                    _sessions[id!] = new WriteSession
                    {
                        Id = id!, Sha = sha!, Mime = mime, Declared = bytes,
                        PartPath = "", AlreadyHave = true,
                    };
                    return null;
                }
                // NEVER prune mid-match (a partner's file could vanish while it is on screen), so a
                // full inbox is refused honestly and the protocol declines with 'quota'.
                if (_usedBytes + bytes > CapBytes) return "cap-reached";

                try
                {
                    Directory.CreateDirectory(TmpDir);
                    var part = PartPathFor(sha!);
                    long already = 0;
                    if (File.Exists(part))
                    {
                        already = new FileInfo(part).Length;
                        if (already > bytes) { File.Delete(part); already = 0; }
                    }
                    _sessions[id!] = new WriteSession
                    {
                        Id = id!,
                        Sha = sha!,
                        Mime = mime,
                        Declared = bytes,
                        PartPath = part,
                        Origin = origin == "gif" ? "gif" : "",
                        Written = already,
                    };
                    return null;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("TransferInboxStore.Begin({Sha}): {E}", sha, ex.Message);
                    return "io-failed";
                }
            }
        }

        /// <summary>
        /// Append one base64 chunk. Returns null on success or
        /// <c>unknown-job | bad-seq | too-big | io-failed</c>. Sequence numbers are gapless from 0;
        /// writing stops at exactly the declared byte count (a peer that keeps sending gets
        /// <c>too-big</c>, which the protocol turns into <c>xfer_fail:'too_big'</c>).
        /// </summary>
        public string? AppendChunk(string? id, int seq, string? b64)
        {
            if (string.IsNullOrEmpty(id)) return "unknown-job";
            if (b64 == null || b64.Length == 0) return "bad-seq";
            if (b64.Length > MaxChunkB64Chars) return "too-big";
            EnsureLoaded();

            lock (_lock)
            {
                if (!_sessions.TryGetValue(id!, out var s)) return "unknown-job";
                if (seq != s.NextSeq) return "bad-seq";
                if (s.AlreadyHave) { s.NextSeq = seq + 1; s.TouchedUtc = DateTime.UtcNow; return null; }

                byte[] bytes;
                try { bytes = Convert.FromBase64String(b64); }
                catch { return "bad-seq"; }
                if (bytes.Length == 0) return "bad-seq";
                if (s.Written + bytes.Length > s.Declared) return "too-big";

                try
                {
                    using (var fs = new FileStream(s.PartPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                    {
                        fs.Seek(s.Written, SeekOrigin.Begin);
                        fs.Write(bytes, 0, bytes.Length);
                    }
                    s.Written += bytes.Length;
                    s.NextSeq = seq + 1;
                    s.TouchedUtc = DateTime.UtcNow;
                    return null;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("TransferInboxStore.AppendChunk({Id}): {E}", id, ex.Message);
                    return "io-failed";
                }
            }
        }

        /// <summary>
        /// Close a session: hash the temp file HOST-SIDE, compare with the sha the peer claimed,
        /// sniff the magic, and only then move it to <c>recv/&lt;sha&gt;.&lt;sniffed ext&gt;</c>.
        /// The extension comes from the SNIFF, never from the claimed mime, and a sniff that
        /// disagrees with the claim is a rejection rather than a relabel.
        ///
        /// Errors: <c>unknown-job | too-big | hash-mismatch | bad-format | io-failed</c>.
        /// Runs up to 24 MB of SHA-256 — call it off the UI thread.
        /// </summary>
        public (bool Ok, string Sha, string Ext, string? Url, string? Error) Commit(string? id)
        {
            if (string.IsNullOrEmpty(id)) return (false, "", "", null, "unknown-job");
            EnsureLoaded();

            WriteSession s;
            lock (_lock)
            {
                if (!_sessions.TryGetValue(id!, out var found)) return (false, "", "", null, "unknown-job");
                s = found;
                // Idempotent success: the same bytes arrived in an earlier session.
                if (_index.Entries.TryGetValue(s.Sha, out var have))
                {
                    _sessions.Remove(id!);
                    have.LastAccessUtc = DateTime.UtcNow;
                    return (true, s.Sha, have.Ext, RecvUrl(s.Sha, have.Ext), null);
                }
            }

            string? error = null;
            string ext = "";
            try
            {
                if (!File.Exists(s.PartPath)) error = "io-failed";
                else if (new FileInfo(s.PartPath).Length != s.Declared) error = "too-big";

                if (error == null)
                {
                    var actual = TransferCacheHash.Sha256File(s.PartPath);
                    if (actual == null) error = "io-failed";
                    else if (!string.Equals(actual, s.Sha, StringComparison.Ordinal)) error = "hash-mismatch";
                }

                if (error == null)
                {
                    var fmt = SniffFile(s.PartPath);
                    // The claim and the bytes must AGREE. A png offered as video/mp4 is a lie about
                    // one of the two, and we do not get to pick which.
                    if (fmt == null || !FormatToExt.TryGetValue(fmt, out var e2)
                        || !MimeToFormat.TryGetValue(s.Mime, out var claimed) || claimed != fmt)
                        error = "bad-format";
                    else
                        ext = e2;
                }

                if (error == null)
                {
                    Cache.EnsureRoot();
                    var dest = Path.Combine(RecvDir, s.Sha + "." + ext);
                    File.Move(s.PartPath, dest, overwrite: true);
                    var len = new FileInfo(dest).Length;
                    lock (_lock)
                    {
                        _sessions.Remove(id!);
                        _index.Entries[s.Sha] = new InboxEntry
                        {
                            Ext = ext,
                            Mime = s.Mime,
                            Origin = s.Origin,
                            Bytes = len,
                            LastAccessUtc = DateTime.UtcNow,
                        };
                        RecountLocked();
                        SaveLocked();
                    }
                    App.Logger?.Information("TransferInboxStore: received {Sha}.{Ext} ({KB} KB)",
                        s.Sha[..12], ext, len / 1024);
                    return (true, s.Sha, ext, RecvUrl(s.Sha, ext), null);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("TransferInboxStore.Commit({Id}): {E}", id, ex.Message);
                error = "io-failed";
            }

            lock (_lock) _sessions.Remove(id!);
            TryDelete(s.PartPath, "commit failed: " + error);
            App.Logger?.Information("TransferInboxStore: commit rejected ({Error}) for {Sha}", error, s.Sha[..12]);
            return (false, s.Sha, "", null, error ?? "io-failed");
        }

        /// <summary>Give up on a session and delete its partial. Never fails.</summary>
        public void Abort(string? id)
        {
            if (string.IsNullOrEmpty(id)) return;
            WriteSession? s;
            lock (_lock)
            {
                if (!_sessions.Remove(id!, out s)) return;
            }
            if (s != null) TryDelete(s.PartPath, "aborted");
        }

        /// <summary>Delete one received artifact (blocklist sweep, user delete). Idempotent.</summary>
        public bool Drop(string? sha)
        {
            if (!IsSha(sha)) return false;
            EnsureLoaded();
            InboxEntry? e;
            lock (_lock)
            {
                if (!_index.Entries.Remove(sha!, out e)) return false;
                RecountLocked();
                SaveLocked();
            }
            try
            {
                var p = Path.Combine(RecvDir, sha + "." + (e?.Ext ?? ""));
                if (File.Exists(p)) File.Delete(p);
            }
            catch (Exception ex) { App.Logger?.Debug("TransferInboxStore.Drop({Sha}): {E}", sha, ex.Message); }
            return true;
        }

        /// <summary>Refresh an artifact's last-access stamp. (The LRU prune that read it is gone -
        /// ephemeral policy - but the stamp is still honest bookkeeping for the index row.)</summary>
        public void Touch(string? sha)
        {
            if (!IsSha(sha)) return;
            lock (_lock)
                if (_index.Entries.TryGetValue(sha!, out var e)) e.LastAccessUtc = DateTime.UtcNow;
        }

        // (The LRU prune that lived here died with cross-session persistence: the ephemeral wipes
        //  at startup, page boot and window close are the only artifact-deletion entry points now,
        //  and CapBytes is enforced at Begin. LastAccessUtc stays on the index rows — a mid-match
        //  page reload still reads them.)

        /// <summary>Drop write sessions the page stopped feeding, so one dead transfer can't hold
        /// its .part (and its cap bytes) forever. Caller holds the lock.</summary>
        private void SweepStaleSessionsLocked()
        {
            if (_sessions.Count == 0) return;
            var now = DateTime.UtcNow;
            foreach (var (id, s) in _sessions.ToList())
            {
                if (now - s.TouchedUtc < SessionIdleTimeout) continue;
                _sessions.Remove(id);
                TryDelete(s.PartPath, "session idle > 5 min");
            }
        }

        // ---- naming + magic -------------------------------------------------------

        private static bool IsSha(string? s) => s != null && ShaRe.IsMatch(s);

        /// <summary>The ONE place a temp name is built, and the sha is already regex-approved.</summary>
        private string PartPathFor(string sha) => Path.Combine(TmpDir, PartPrefix + sha + ".part");

        private static string MimeForExt(string ext) => ext switch
        {
            "png" => "image/png",
            "jpg" => "image/jpeg",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "mp4" => "video/mp4",
            "webm" => "video/webm",
            _ => "application/octet-stream",
        };

        /// <summary>Read the first 32 bytes and name the format, or null when nothing matches.</summary>
        private static string? SniffFile(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var head = new byte[32];
                int n = fs.Read(head, 0, head.Length);
                return SniffFormat(head, n);
            }
            catch { return null; }
        }

        /// <summary>
        /// Magic table (§6.3). Exposed internally so a self-test can drive it without touching disk.
        /// PNG / JPEG / GIF / WEBP (RIFF....WEBP) / ISO-BMFF (<c>ftyp</c> at offset 4, brand
        /// allowlist) / Matroska-WebM (1A 45 DF A3).
        /// </summary>
        internal static string? SniffFormat(byte[] b, int n)
        {
            if (b == null || n < 12) return null;

            if (n >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
                && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "png";

            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpeg";

            if (b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'8'
                && (b[4] == (byte)'7' || b[4] == (byte)'9') && b[5] == (byte)'a') return "gif";

            if (b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
                && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P') return "webp";

            if (b[0] == 0x1A && b[1] == 0x45 && b[2] == 0xDF && b[3] == 0xA3) return "webm";

            if (n >= 12 && b[4] == (byte)'f' && b[5] == (byte)'t' && b[6] == (byte)'y' && b[7] == (byte)'p')
            {
                var brand = new string(new[] { (char)b[8], (char)b[9], (char)b[10], (char)b[11] });
                if (Mp4Brands.Contains(brand)) return "mp4";
            }
            return null;
        }

        /// <summary>ISO-BMFF <c>ftyp</c> check for the lane-B commit path in the bridge — the page's
        /// WebCodecs output must actually be an mp4 before it becomes a cache artifact.</summary>
        internal static bool LooksLikeMp4(byte[] bytes) =>
            bytes != null && SniffFormat(bytes, Math.Min(bytes.Length, 32)) == "mp4";

#if DEBUG
        /// <summary>Headless smoke of the pure bits — no disk, no page. Every line starts OK or FAIL.</summary>
        public static string DebugSelfTest()
        {
            var lines = new List<string>();
            void Check(string name, bool ok) => lines.Add((ok ? "OK   " : "FAIL ") + name);

            byte[] Pad(params byte[] head)
            {
                var b = new byte[32];
                Array.Copy(head, b, head.Length);
                return b;
            }

            Check("sha regex accepts 64 hex", ShaRe.IsMatch(new string('a', 64)));
            Check("sha regex rejects uppercase", !ShaRe.IsMatch(new string('A', 64)));
            Check("sha regex rejects traversal", !ShaRe.IsMatch("../../evil"));
            Check("sha regex rejects short", !ShaRe.IsMatch(new string('a', 63)));

            Check("png sniff", SniffFormat(Pad(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A), 32) == "png");
            Check("jpeg sniff", SniffFormat(Pad(0xFF, 0xD8, 0xFF, 0xE0), 32) == "jpeg");
            Check("gif sniff", SniffFormat(Pad(0x47, 0x49, 0x46, 0x38, 0x39, 0x61), 32) == "gif");
            Check("webp sniff", SniffFormat(Pad(0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50), 32) == "webp");
            Check("webm sniff", SniffFormat(Pad(0x1A, 0x45, 0xDF, 0xA3), 32) == "webm");
            Check("mp4 sniff isom", SniffFormat(
                Pad(0, 0, 0, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
                    (byte)'i', (byte)'s', (byte)'o', (byte)'m'), 32) == "mp4");
            Check("mp4 brand allowlist", SniffFormat(
                Pad(0, 0, 0, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
                    (byte)'x', (byte)'x', (byte)'x', (byte)'x'), 32) == null);
            Check("garbage sniffs null", SniffFormat(Pad(1, 2, 3, 4, 5, 6, 7, 8), 32) == null);
            Check("short buffer sniffs null", SniffFormat(new byte[4], 4) == null);

            int fails = lines.Count(l => l.StartsWith("FAIL"));
            lines.Add(fails == 0 ? $"ALL {lines.Count} CHECKS PASSED" : $"{fails} CHECK(S) FAILED");
            return string.Join(Environment.NewLine, lines);
        }
#endif
    }
}
