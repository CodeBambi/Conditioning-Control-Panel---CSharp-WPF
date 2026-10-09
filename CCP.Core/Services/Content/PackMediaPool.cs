using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Flash;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// One surface's content-pack half of a media pool (WPF FlashService _packImageList + PackBag +
    /// _tempPackFiles, VideoService _packVideoQueue + _tempPackFiles): the active pack entries of
    /// one type, dealt from a <see cref="ShuffleBag{T}"/> keyed on <see cref="ContentPackStore.SourceKey"/>
    /// (never a temp path), decrypts tracked so they are deleted (at more than
    /// <see cref="TempCap"/> on record, on <see cref="Invalidate"/> and at <see cref="CleanupTemps"/>).
    ///
    /// <para>The entry list is rebuilt lazily after the store's <c>PacksChanged</c> or an
    /// <see cref="Invalidate"/> (asset selection change, WPF ClearFileCache). The store is read
    /// through <c>ContentPackStore.Current</c> by default, so a store registered later is picked up.
    /// Thread-safe: every member locks.</para>
    /// </summary>
    public sealed class PackMediaPool
    {
        /// <summary>WPF: <c>if (_tempPackFiles.Count &gt; 50) CleanupTempPackFiles()</c>.</summary>
        public const int TempCap = 50;

        private readonly string _type;
        private readonly Func<ContentPackStore?> _store;
        private readonly object _lock = new();
        private readonly ShuffleBag<(string PackId, PackFileEntry File)> _bag;
        private readonly List<string> _temps = new();
        private List<(string PackId, PackFileEntry File)> _list = new();
        private ContentPackStore? _bound;
        private bool _dirty = true;

        /// <param name="type"><see cref="ContentPackStore.ImageType"/> or <see cref="ContentPackStore.VideoType"/>.</param>
        public PackMediaPool(string type, Random? rng = null, Func<ContentPackStore?>? store = null)
        {
            _type = type;
            _store = store ?? (() => ContentPackStore.Current);
            _bag = new ShuffleBag<(string PackId, PackFileEntry File)>(e => ContentPackStore.SourceKey(e.PackId, e.File), rng ?? new Random());
            // A per-file opt-out (pack:<id>/<name> ticked off in the Library) re-reads the list on the
            // next draw, as WPF PruneDeselectedFromPools does; the walk keeps its place.
            AssetSelection.Changed += () => { lock (_lock) _dirty = true; };
        }

        /// <summary>Active, enabled entries now in the pool (rebuilds when stale).</summary>
        public int Count { get { lock (_lock) { Refresh(); return _list.Count; } } }

        /// <summary>Entries left in this shuffle cycle before a repeat (WPF weights by queue length).</summary>
        public int Remaining { get { lock (_lock) { Refresh(); return _list.Count == 0 ? 0 : Math.Max(1, _bag.Remaining); } } }

        /// <summary>Decrypts on record (tests, diagnostics).</summary>
        public int TempCount { get { lock (_lock) return _temps.Count; } }

        /// <summary>WPF ClearFileCache / RefreshImageLists: drop the list, restart the walk, delete the decrypts.</summary>
        public void Invalidate()
        {
            lock (_lock)
            {
                _dirty = true;
                _bag.Reset();
            }
            CleanupTemps();
        }

        /// <summary>The next entry of the shuffled walk, or false when no active pack holds this type.</summary>
        public bool TryNext(out (string PackId, PackFileEntry File) entry)
        {
            lock (_lock)
            {
                Refresh();
                if (_list.Count == 0) { entry = default; return false; }
                return _bag.TryNext(_list, out entry);
            }
        }

        /// <summary>
        /// WPF GetPackFileTempPath + <c>_tempPackFiles.Add</c>: decrypt to a FRESH temp file and keep it
        /// on record. Null when the decrypt fails. Past <see cref="TempCap"/> the oldest half is deleted
        /// first (WPF deletes them all; the newest stay so a clip still playing keeps its file).
        /// </summary>
        public string? Decrypt((string PackId, PackFileEntry File) entry)
        {
            var store = _store();
            if (store == null || entry.File == null) return null;
            List<string>? stale = null;
            lock (_lock)
            {
                if (_temps.Count > TempCap)
                {
                    var drop = _temps.Count / 2;
                    stale = _temps.GetRange(0, drop);
                    _temps.RemoveRange(0, drop);
                }
            }
            if (stale != null) foreach (var p in stale) store.DeleteTempFile(p);
            var temp = store.GetPackFileTempPath(entry.PackId, entry.File);
            if (string.IsNullOrEmpty(temp))
            {
                Log.Information("ContentPacks: '{Name}' from pack {PackId} could not be decrypted", entry.File.OriginalName, entry.PackId);
                return null;
            }
            lock (_lock) _temps.Add(temp);
            return temp;
        }

        /// <summary>A caller done with a decrypt (decoded into memory): delete it now.</summary>
        public void Release(string? temp)
        {
            if (string.IsNullOrEmpty(temp)) return;
            lock (_lock) _temps.Remove(temp);
            _store()?.DeleteTempFile(temp);
        }

        /// <summary>WPF CleanupTempPackFiles: delete every decrypt on record.</summary>
        public void CleanupTemps()
        {
            List<string> all;
            lock (_lock) { all = new List<string>(_temps); _temps.Clear(); }
            var store = _store();
            foreach (var p in all)
            {
                if (store != null) store.DeleteTempFile(p);
                else try { System.IO.File.Delete(p); } catch { }
            }
        }

        // Caller holds _lock.
        private void Refresh()
        {
            var store = _store();
            if (!ReferenceEquals(store, _bound))
            {
                if (_bound != null) _bound.PacksChanged -= OnPacksChanged;
                _bound = store;
                if (store != null) store.PacksChanged += OnPacksChanged;
                _dirty = true;
            }
            if (!_dirty) return;
            _dirty = false;
            try
            {
                _list = store == null ? new List<(string, PackFileEntry)>()
                    : _type == ContentPackStore.VideoType ? store.GetAllActivePackVideos() : store.GetAllActivePackImages();
            }
            catch (Exception ex)
            {
                Log.Debug("ContentPacks: pool refresh failed: {E}", ex.Message);
                _list = new List<(string, PackFileEntry)>();
            }
        }

        private void OnPacksChanged()
        {
            lock (_lock) { _dirty = true; _bag.Reset(); }
        }
    }
}
