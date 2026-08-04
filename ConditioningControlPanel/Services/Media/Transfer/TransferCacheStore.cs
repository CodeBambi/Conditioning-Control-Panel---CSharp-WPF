using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Transfer
{
    /// <summary>
    /// Disk authority for the user's OWN compressed artifacts:
    /// <c>%LOCALAPPDATA%/ConditioningControlPanel/transfer-cache/</c> with <c>art/</c> (artifacts),
    /// <c>prv/</c> (micro-previews) and <c>index.json</c>. Partial writes live in a SIBLING
    /// <c>transfer-cache.tmp/</c> — deliberately outside the folder that gets mapped as the
    /// <c>https://ccp.cache</c> virtual host, so a half-written .part is never reachable from the page.
    ///
    /// Received files (what a duel partner sent) get their own store in Phase 5 under
    /// <c>recv/</c>: "delete my compressed copies" must never touch a partner's media, and vice versa.
    ///
    /// Index writes are debounced 3s and go temp-write-then-File.Move
    /// (<see cref="ConditioningControlPanel.Services.VideoMetadataCache"/> +
    /// <see cref="ConditioningControlPanel.Services.Chaos.DtrhLoomStore"/> precedent) so a crash
    /// mid-save can't leave a truncated index — the worst case is losing the last few seconds of
    /// bookkeeping, which the planner re-derives anyway.
    ///
    /// Thread-safe and Dispatcher-free: the compression queue runs headless on plain Task workers,
    /// so nothing here may depend on a UI thread existing.
    /// </summary>
    internal sealed class TransferCacheStore
    {
        public static TransferCacheStore Instance { get; } = new();

        public const long DefaultCapBytes = 8L * 1024 * 1024 * 1024;
        public const long MinCapBytes = 1L * 1024 * 1024 * 1024;
        public const long MaxCapBytes = 64L * 1024 * 1024 * 1024;

        private const int SaveDebounceMs = 3000;
        /// <summary>Failed entries are retried on a fresh launch anyway; keeping them longer than
        /// this just hides a file the user has since replaced.</summary>
        private const int FailedTtlDays = 14;
        /// <summary>How many recently-used presets protect their members from eviction.</summary>
        private const int ProtectedPresetCount = 2;

        private readonly object _lock = new();
        private readonly object _saveTimerLock = new();
        private TransferCacheIndex _index = new();

        /// <summary>rel (lower-invariant) -> srcKey, so a re-saved file's stale entry gets retired.</summary>
        private readonly Dictionary<string, string> _byRel = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>sha -> the srcKeys that point at it. Two paths with identical bytes share one artifact.</summary>
        private readonly Dictionary<string, HashSet<string>> _bySha = new(StringComparer.Ordinal);
        /// <summary>sha -> refcount. A transfer holds a pin for the whole send; pinned never evicts.</summary>
        private readonly Dictionary<string, int> _pinned = new(StringComparer.Ordinal);
        /// <summary>srcKeys queued or running right now — evicting one would race its own commit.</summary>
        private readonly HashSet<string> _busy = new(StringComparer.Ordinal);

        private Timer? _saveTimer;
        private bool _loaded;
        private bool _dirty;

        /// <summary>Raised after any mutation that changes what the assets screen would render.
        /// Fires on a worker thread — subscribers marshal themselves.</summary>
        public event Action? Changed;

        /// <summary>True when the last <see cref="EnforceCap"/> had to evict entries belonging to a
        /// recently-used preset (the cap is too small for the pool). Surfaced in the UI, not fatal.</summary>
        public bool OverCapProtected { get; private set; }

        // ---- paths ----------------------------------------------------------------

        public string Root => Path.Combine(App.UserDataPath, "transfer-cache");
        public string ArtDir => Path.Combine(Root, "art");
        public string PrvDir => Path.Combine(Root, "prv");
        /// <summary>Reserved for the Phase-5 received-media inbox (TransferInboxStore).</summary>
        public string RecvDir => Path.Combine(Root, "recv");
        /// <summary>SIBLING of the mapped root on purpose — partials must not be page-reachable.</summary>
        public string TmpDir => Path.Combine(App.UserDataPath, "transfer-cache.tmp");
        public string IndexPath => Path.Combine(Root, "index.json");

        /// <summary>
        /// Create every folder the cache uses. MUST run before the WebView2 virtual-host mapping:
        /// AddIfPresent silently drops a mapping whose folder doesn't exist yet, which would leave
        /// https://ccp.cache dead for the whole session (trap 10).
        /// </summary>
        public void EnsureRoot()
        {
            try
            {
                Directory.CreateDirectory(Root);
                Directory.CreateDirectory(ArtDir);
                Directory.CreateDirectory(PrvDir);
                Directory.CreateDirectory(RecvDir);
                Directory.CreateDirectory(TmpDir);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("TransferCacheStore.EnsureRoot failed: {E}", ex.Message);
            }
        }

        public string ArtifactPathForSha(string sha, string ext) =>
            Path.Combine(ArtDir, sha + "." + (ext ?? "").TrimStart('.'));

        public string PreviewPathForSha(string sha) => Path.Combine(PrvDir, sha + ".mp4");

        /// <summary>The file whose bytes go on the wire: the artifact for ready entries, the
        /// original for exempt ones. Null when it has gone missing since it was indexed.</summary>
        public string? SendablePath(TransferCacheEntry e)
        {
            try
            {
                var p = e.State == TransferStates.Ready
                    ? ArtifactPathForSha(e.Sha, e.Ext)
                    : Path.Combine(App.EffectiveAssetsPath, e.Rel.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        // ---- load / save ----------------------------------------------------------

        public void Load()
        {
            lock (_lock)
            {
                if (_loaded) return;
                _loaded = true;
                EnsureRoot();
                try
                {
                    if (File.Exists(IndexPath))
                    {
                        var json = File.ReadAllText(IndexPath);
                        var loaded = JsonConvert.DeserializeObject<TransferCacheIndex>(json);
                        if (loaded?.Entries != null && loaded.Version == TransferCacheIndex.CurrentVersion)
                            _index = loaded;
                        else if (loaded != null)
                            App.Logger?.Information(
                                "TransferCacheStore: index version {V} != {C}, starting fresh",
                                loaded.Version, TransferCacheIndex.CurrentVersion);
                    }
                }
                catch (Exception ex)
                {
                    // A dead index is survivable: everything gets re-planned, nothing is lost but time.
                    App.Logger?.Warning("TransferCacheStore: index load failed ({E}) - starting fresh", ex.Message);
                    _index = new TransferCacheIndex();
                }
                Reindex();
                App.Logger?.Information("TransferCacheStore: {N} entries, {MB:F1} MB used",
                    _index.Entries.Count, _index.TotalBytes / 1048576.0);
            }
        }

        /// <summary>Rebuild the in-memory reverse maps and drop entries whose artifact vanished.</summary>
        private void Reindex()
        {
            _byRel.Clear();
            _bySha.Clear();
            var dead = new List<string>();
            foreach (var (key, e) in _index.Entries)
            {
                if (string.IsNullOrEmpty(e.Rel) || (string.IsNullOrEmpty(e.Sha) && e.State != TransferStates.Failed))
                {
                    dead.Add(key);
                    continue;
                }
                // Someone emptied art/ by hand (or a partial uninstall did) - the entry is a lie.
                if (e.State == TransferStates.Ready && !File.Exists(ArtifactPathForSha(e.Sha, e.Ext)))
                {
                    dead.Add(key);
                    continue;
                }
                _byRel[e.Rel] = key;
                Link(key, e.Sha);
            }
            foreach (var k in dead) _index.Entries.Remove(k);
            if (dead.Count > 0) _dirty = true;
            _index.Recount();
        }

        private void Link(string srcKey, string sha)
        {
            if (string.IsNullOrEmpty(sha)) return;
            if (!_bySha.TryGetValue(sha, out var set))
                _bySha[sha] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(srcKey);
        }

        private void Unlink(string srcKey, string sha)
        {
            if (string.IsNullOrEmpty(sha)) return;
            if (_bySha.TryGetValue(sha, out var set) && set.Remove(srcKey) && set.Count == 0)
                _bySha.Remove(sha);
        }

        private void ScheduleSave()
        {
            lock (_saveTimerLock)
            {
                if (_saveTimer == null)
                {
                    _saveTimer = new Timer(_ =>
                    {
                        try { Save(); }
                        catch (Exception ex) { App.Logger?.Debug("TransferCacheStore: deferred save failed: {E}", ex.Message); }
                    }, null, SaveDebounceMs, Timeout.Infinite);
                }
                else
                {
                    _saveTimer.Change(SaveDebounceMs, Timeout.Infinite);
                }
            }
        }

        /// <summary>Write index.json (temp file, then File.Move over the real one). Cheap no-op when clean.</summary>
        public void Save()
        {
            string json;
            lock (_lock)
            {
                if (!_dirty) return;
                _index.Recount();
                json = JsonConvert.SerializeObject(_index, Formatting.None);
                _dirty = false;
            }
            try
            {
                EnsureRoot();
                var tmp = IndexPath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, IndexPath, overwrite: true);
            }
            catch (Exception ex)
            {
                lock (_lock) { _dirty = true; }
                App.Logger?.Warning("TransferCacheStore: index save failed: {E}", ex.Message);
            }
        }

        // ---- budget ---------------------------------------------------------------

        /// <summary>User-configured cap, clamped to 1-64 GB. Never trusts the settings file.</summary>
        public long CapBytes
        {
            get
            {
                long v;
                try { v = App.Settings?.Current?.TransferCacheCapBytes ?? DefaultCapBytes; }
                catch { v = DefaultCapBytes; }
                if (v <= 0) v = DefaultCapBytes;
                return Math.Clamp(v, MinCapBytes, MaxCapBytes);
            }
        }

        public long UsedBytes { get { lock (_lock) return _index.TotalBytes; } }

        /// <summary>Disk cost of one entry: exempt originals live in the user's own library, so only
        /// artifacts and previews spend the budget.</summary>
        private static long DiskBytes(TransferCacheEntry e) =>
            (e.State == TransferStates.Ready ? e.Bytes : 0) + e.PrevBytes;

        // ---- reads ----------------------------------------------------------------

        public bool TryGet(string srcKey, out TransferCacheEntry entry)
        {
            lock (_lock)
            {
                if (_index.Entries.TryGetValue(srcKey, out var e)) { entry = e.Clone(); return true; }
            }
            entry = null!;
            return false;
        }

        /// <summary>Resolve a wire identity back to its record — the offer gate's "do I have this?".</summary>
        public bool TryGetBySha(string sha, out TransferCacheEntry entry)
        {
            lock (_lock)
            {
                if (_bySha.TryGetValue(sha ?? "", out var keys))
                    foreach (var k in keys)
                        if (_index.Entries.TryGetValue(k, out var e)) { entry = e.Clone(); return true; }
            }
            entry = null!;
            return false;
        }

        /// <summary>Every sendable sha (ready + exempt). Used to answer offers and to seed the queue.</summary>
        public IReadOnlyCollection<string> ReadyShas()
        {
            lock (_lock)
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var e in _index.Entries.Values)
                    if (e.State != TransferStates.Failed && e.Sha.Length > 0) set.Add(e.Sha);
                return set;
            }
        }

        /// <summary>Immutable copies of every entry, keyed by srcKey. Safe to hand to the bridge.</summary>
        public IReadOnlyList<KeyValuePair<string, TransferCacheEntry>> Snapshot()
        {
            lock (_lock)
                return _index.Entries
                    .Select(kv => new KeyValuePair<string, TransferCacheEntry>(kv.Key, kv.Value.Clone()))
                    .ToList();
        }

        public (int Total, int Ready, int Exempt, int Failed) Counts()
        {
            lock (_lock)
            {
                int r = 0, x = 0, f = 0;
                foreach (var e in _index.Entries.Values)
                {
                    if (e.State == TransferStates.Ready) r++;
                    else if (e.State == TransferStates.Exempt) x++;
                    else f++;
                }
                return (_index.Entries.Count, r, x, f);
            }
        }

        // ---- writes ---------------------------------------------------------------

        /// <summary>
        /// Insert or replace one entry. Any older entry for the same <c>rel</c> (a file the user has
        /// since edited) is retired in the same breath, so a churning asset folder can't accumulate
        /// orphan artifacts.
        /// </summary>
        public void Upsert(string srcKey, TransferCacheEntry entry)
        {
            if (string.IsNullOrEmpty(srcKey) || entry == null) return;
            List<string> orphanShas = new();
            lock (_lock)
            {
                if (_byRel.TryGetValue(entry.Rel, out var prevKey) && prevKey != srcKey
                    && _index.Entries.TryGetValue(prevKey, out var prev))
                {
                    _index.Entries.Remove(prevKey);
                    Unlink(prevKey, prev.Sha);
                    _busy.Remove(prevKey);
                    if (prev.State == TransferStates.Ready && !_bySha.ContainsKey(prev.Sha))
                        orphanShas.Add(prev.Sha + "|" + prev.Ext);
                }
                if (_index.Entries.TryGetValue(srcKey, out var old)) Unlink(srcKey, old.Sha);
                _index.Entries[srcKey] = entry;
                _byRel[entry.Rel] = srcKey;
                Link(srcKey, entry.Sha);
                _index.Recount();
                _dirty = true;
            }
            foreach (var s in orphanShas)
            {
                var parts = s.Split('|');
                DeleteArtifactFiles(parts[0], parts.Length > 1 ? parts[1] : "");
            }
            ScheduleSave();
            RaiseChanged();
        }

        /// <summary>Touch the LRU timestamp for everything pointing at this sha (a send happened).</summary>
        public void MarkUsed(string sha)
        {
            if (string.IsNullOrEmpty(sha)) return;
            lock (_lock)
            {
                if (!_bySha.TryGetValue(sha, out var keys)) return;
                var now = DateTime.UtcNow;
                foreach (var k in keys)
                    if (_index.Entries.TryGetValue(k, out var e)) e.LastUsed = now;
                _dirty = true;
            }
            ScheduleSave();
        }

        // ---- pins & busy ----------------------------------------------------------

        /// <summary>Hold an artifact against eviction for the duration of a send. Refcounted.</summary>
        public void Pin(string sha)
        {
            if (string.IsNullOrEmpty(sha)) return;
            lock (_lock) _pinned[sha] = _pinned.TryGetValue(sha, out var n) ? n + 1 : 1;
        }

        public void Unpin(string sha)
        {
            if (string.IsNullOrEmpty(sha)) return;
            lock (_lock)
            {
                if (!_pinned.TryGetValue(sha, out var n)) return;
                if (n <= 1) _pinned.Remove(sha); else _pinned[sha] = n - 1;
            }
        }

        public bool IsPinned(string sha)
        {
            lock (_lock) return _pinned.ContainsKey(sha ?? "");
        }

        /// <summary>Mark a srcKey queued/running so the LRU leaves it alone.</summary>
        public void SetBusy(string srcKey, bool busy)
        {
            if (string.IsNullOrEmpty(srcKey)) return;
            lock (_lock) { if (busy) _busy.Add(srcKey); else _busy.Remove(srcKey); }
        }

        public bool IsBusy(string srcKey)
        {
            lock (_lock) return _busy.Contains(srcKey ?? "");
        }

        // ---- deletes --------------------------------------------------------------

        /// <summary>Delete one entry and its files. Pinned artifacts are refused (a send is using them).</summary>
        public bool DeleteOne(string srcKey)
        {
            TransferCacheEntry? e;
            bool lastRef;
            lock (_lock)
            {
                if (!_index.Entries.TryGetValue(srcKey, out e)) return false;
                if (_pinned.ContainsKey(e.Sha)) return false;
                _index.Entries.Remove(srcKey);
                _byRel.Remove(e.Rel);
                Unlink(srcKey, e.Sha);
                _busy.Remove(srcKey);
                lastRef = !_bySha.ContainsKey(e.Sha);
                _index.Recount();
                _dirty = true;
            }
            if (lastRef) DeleteArtifactFiles(e.Sha, e.Ext);
            ScheduleSave();
            RaiseChanged();
            return true;
        }

        /// <summary>Nuke every compressed artifact + preview. Never touches originals or the inbox.</summary>
        public int DeleteAll()
        {
            int n;
            lock (_lock)
            {
                n = _index.Entries.Count;
                _index = new TransferCacheIndex();
                _byRel.Clear();
                _bySha.Clear();
                _busy.Clear();
                _dirty = true;
            }
            foreach (var dir in new[] { ArtDir, PrvDir })
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.EnumerateFiles(dir))
                        try { File.Delete(f); } catch { /* a locked file just survives to the next sweep */ }
                }
                catch (Exception ex) { App.Logger?.Debug("TransferCacheStore.DeleteAll({Dir}): {E}", dir, ex.Message); }
            }
            OverCapProtected = false;
            Save();
            RaiseChanged();
            App.Logger?.Information("TransferCacheStore: deleted {N} cached artifacts", n);
            return n;
        }

        private void DeleteArtifactFiles(string sha, string ext)
        {
            if (string.IsNullOrEmpty(sha)) return;
            try { var p = ArtifactPathForSha(sha, ext); if (File.Exists(p)) File.Delete(p); } catch { }
            try { var p = PreviewPathForSha(sha); if (File.Exists(p)) File.Delete(p); } catch { }
        }

        /// <summary>Delete stale <c>.part</c> files from a previous run. Safe at any time — live jobs
        /// hold their own freshly-named temp file.</summary>
        public void SweepTmp(TimeSpan olderThan)
        {
            try
            {
                if (!Directory.Exists(TmpDir)) return;
                var cutoff = DateTime.UtcNow - olderThan;
                foreach (var f in Directory.EnumerateFiles(TmpDir))
                {
                    try
                    {
                        if (new FileInfo(f).LastWriteTimeUtc < cutoff) File.Delete(f);
                    }
                    catch { }
                }
            }
            catch (Exception ex) { App.Logger?.Debug("TransferCacheStore.SweepTmp: {E}", ex.Message); }
        }

        // ---- LRU ------------------------------------------------------------------

        /// <summary>One entry's eviction inputs. Split out so the policy below is pure and testable.</summary>
        internal readonly record struct EvictCandidate(
            string SrcKey, long Bytes, DateTime LastUsed, DateTime MadeAt,
            bool Failed, bool Pinned, bool Busy, bool Protected);

        /// <summary>
        /// Pure LRU policy: which srcKeys have to go for <paramref name="used"/> to fit
        /// <paramref name="cap"/>. Order of operations is load-bearing:
        ///   1. failed entries older than 14 days go regardless of the cap (they cost nothing but lie),
        ///   2. then coldest-first among UNPROTECTED entries,
        ///   3. only if still over, coldest-first among protected ones (and the caller shouts).
        /// Pinned (mid-send) and busy (queued/running) entries are never candidates at any stage.
        /// </summary>
        internal static List<string> PlanEvictions(
            IReadOnlyList<EvictCandidate> cands, long used, long cap, DateTime nowUtc, out bool overCapProtected)
        {
            overCapProtected = false;
            var drop = new List<string>();
            var dropped = new HashSet<string>(StringComparer.Ordinal);
            long left = used;

            foreach (var c in cands)
            {
                if (!c.Failed || c.Pinned || c.Busy) continue;
                if ((nowUtc - c.MadeAt).TotalDays <= FailedTtlDays) continue;
                drop.Add(c.SrcKey); dropped.Add(c.SrcKey); left -= c.Bytes;
            }
            if (left <= cap) return drop;

            foreach (var c in cands.Where(x => !dropped.Contains(x.SrcKey) && !x.Pinned && !x.Busy && !x.Protected)
                                   .OrderBy(x => x.LastUsed))
            {
                if (left <= cap) break;
                drop.Add(c.SrcKey); dropped.Add(c.SrcKey); left -= c.Bytes;
            }
            if (left <= cap) return drop;

            foreach (var c in cands.Where(x => !dropped.Contains(x.SrcKey) && !x.Pinned && !x.Busy && x.Protected)
                                   .OrderBy(x => x.LastUsed))
            {
                if (left <= cap) break;
                drop.Add(c.SrcKey); dropped.Add(c.SrcKey); left -= c.Bytes;
                overCapProtected = true;
            }
            return drop;
        }

        /// <summary>
        /// Bring the cache back under <see cref="CapBytes"/>. Entries whose <c>rel</c> is enabled in
        /// either of the two most-recently-used <see cref="AssetPreset"/>s are protected (set lookup
        /// on DisabledAssetPaths — no tree walk, no disk touch).
        /// </summary>
        public void EnforceCap()
        {
            var protectedRels = BuildProtectedRelSet();
            List<string> drop;
            bool over;
            long used, cap = CapBytes;
            lock (_lock)
            {
                used = _index.TotalBytes;
                var cands = new List<EvictCandidate>(_index.Entries.Count);
                foreach (var (key, e) in _index.Entries)
                {
                    bool prot = protectedRels != null && protectedRels.Contains(e.Rel);
                    cands.Add(new EvictCandidate(key, DiskBytes(e), e.LastUsed, e.MadeAt,
                        e.State == TransferStates.Failed, _pinned.ContainsKey(e.Sha), _busy.Contains(key), prot));
                }
                drop = PlanEvictions(cands, used, cap, DateTime.UtcNow, out over);
            }

            if (drop.Count == 0)
            {
                OverCapProtected = false;
                return;
            }

            long freed = 0;
            var files = new List<(string Sha, string Ext)>();
            lock (_lock)
            {
                foreach (var key in drop)
                {
                    if (!_index.Entries.TryGetValue(key, out var e)) continue;
                    freed += DiskBytes(e);
                    _index.Entries.Remove(key);
                    _byRel.Remove(e.Rel);
                    Unlink(key, e.Sha);
                    if (!_bySha.ContainsKey(e.Sha)) files.Add((e.Sha, e.Ext));
                }
                _index.Recount();
                _dirty = true;
            }
            foreach (var (sha, ext) in files) DeleteArtifactFiles(sha, ext);

            OverCapProtected = over;
            if (over)
                App.Logger?.Warning(
                    "TransferCacheStore: cap {CapMB:F0} MB is smaller than the active pool - evicted {N} entries " +
                    "including some from a recently-used preset ({FreedMB:F1} MB freed)",
                    cap / 1048576.0, drop.Count, freed / 1048576.0);
            else
                App.Logger?.Information("TransferCacheStore: LRU evicted {N} entries ({FreedMB:F1} MB)",
                    drop.Count, freed / 1048576.0);

            ScheduleSave();
            RaiseChanged();
        }

        /// <summary>
        /// Rels enabled in either of the two most-recently-used presets. Null means "no preset tier
        /// applies" — either the user has no presets, or the top-2 between them enable everything.
        /// Both cases collapse to plain coldest-first LRU, which is the same eviction ORDER as
        /// "everything is protected" but without falsely reporting <see cref="OverCapProtected"/>.
        /// </summary>
        private HashSet<string>? BuildProtectedRelSet()
        {
            try
            {
                var presets = App.Settings?.Current?.AssetPresets;
                if (presets == null || presets.Count == 0) return null;

                // A preset stores what's OFF, so "protected" = every indexed rel MINUS the union of
                // the disabled sets of the top-2 presets... except a rel disabled in one but enabled
                // in the other is still protected, so intersect the disabled sets instead.
                var top = presets.OrderByDescending(p => p.LastUsed).Take(ProtectedPresetCount).ToList();
                HashSet<string>? disabledInAll = null;
                foreach (var p in top)
                {
                    var d = new HashSet<string>(
                        (p.DisabledAssetPaths ?? new HashSet<string>()).Select(x => (x ?? "").Replace('\\', '/')),
                        StringComparer.OrdinalIgnoreCase);
                    if (disabledInAll == null) disabledInAll = d;
                    else disabledInAll.IntersectWith(d);
                }
                if (disabledInAll == null || disabledInAll.Count == 0) return null;

                lock (_lock)
                {
                    var prot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var e in _index.Entries.Values)
                        if (!disabledInAll.Contains(e.Rel)) prot.Add(e.Rel);
                    return prot;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("TransferCacheStore.BuildProtectedRelSet: {E}", ex.Message);
                return null;   // fail safe: protect everything rather than evict the user's active pool
            }
        }

        private void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { App.Logger?.Debug("TransferCacheStore.Changed subscriber threw: {E}", ex.Message); }
        }
    }
}
